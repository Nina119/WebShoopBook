﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WebShopBooks.DataAccess.Repository.IRepository;
using WebShopBooks.Models.Models;
using WebShopBooks.Models.ViewModels;
using WebShopBooks.Utility;
using Stripe.Checkout;

namespace WebShopBooks.Areas.Customer.Controllers;

[Area("Customer")]
[Authorize]
public class ShoppingCartController : Controller
{
    private readonly IUnitOfWork _unitOfWork;
    [BindProperty]
    public ShoppingCartViewModel ShoppingCartViewModel { get; set; }

    public ShoppingCartController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public IActionResult Index()
    {
        var claimsIdentity = (ClaimsIdentity)User.Identity;
        var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

        ShoppingCartViewModel = new ShoppingCartViewModel()
        {
            ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(sp => sp.ApplicationUserId == userId, includeProperties: "Product"),
            OrderHeader = new OrderHeader()
        };

        foreach (var cart in ShoppingCartViewModel.ShoppingCartList)
        {
            cart.Price = GetPriceBasedOnQuantity(cart);
            ShoppingCartViewModel.OrderHeader.OrderTotal += (cart.Price * cart.Count);
        }

        return View(ShoppingCartViewModel);
    }

    public IActionResult Summary()
    {
        var claimsIdentity = (ClaimsIdentity)User.Identity;
        var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

        ShoppingCartViewModel = new ShoppingCartViewModel()
        {
            ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(sp => sp.ApplicationUserId == userId, includeProperties: "Product"),
            OrderHeader = new OrderHeader()
        };

        var applicationUser = new ApplicationUser();

        applicationUser = _unitOfWork.ApplicationUser.Get(au => au.Id == userId);

        ShoppingCartViewModel.OrderHeader.Name = applicationUser.Name;
        ShoppingCartViewModel.OrderHeader.PhoneNumber = applicationUser.PhoneNumber;
        ShoppingCartViewModel.OrderHeader.StreetAddress = applicationUser.StreetAddress;
        ShoppingCartViewModel.OrderHeader.City = applicationUser.City;
        ShoppingCartViewModel.OrderHeader.State = applicationUser.State;
        ShoppingCartViewModel.OrderHeader.PostalCode = applicationUser.PostalCode;

        foreach (var cart in ShoppingCartViewModel.ShoppingCartList)
        {
            cart.Price = GetPriceBasedOnQuantity(cart);
            ShoppingCartViewModel.OrderHeader.OrderTotal += (cart.Price * cart.Count);
        }

        return View(ShoppingCartViewModel);
    }

    [HttpPost]
    [ActionName("Summary")]
	public IActionResult SummaryPOST()
	{
		var claimsIdentity = (ClaimsIdentity)User.Identity;
		var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

        ShoppingCartViewModel.ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(sp => sp.ApplicationUserId == userId, includeProperties: "Product");
		
        ShoppingCartViewModel.OrderHeader.OrderDate = DateTime.Now;
        ShoppingCartViewModel.OrderHeader.ApplicationUserId = userId;

        // Changed to this because of key constraint
		ApplicationUser applicationUser = _unitOfWork.ApplicationUser.Get(au => au.Id == userId);

        // Removed because we will get all of this data populated in last Summary call
		//ShoppingCartViewModel.OrderHeader.Name = applicationUser.Name;
		//ShoppingCartViewModel.OrderHeader.PhoneNumber = applicationUser.PhoneNumber;
		//ShoppingCartViewModel.OrderHeader.StreetAddress = applicationUser.StreetAddress;
		//ShoppingCartViewModel.OrderHeader.City = applicationUser.City;
		//ShoppingCartViewModel.OrderHeader.State = applicationUser.State;
		//ShoppingCartViewModel.OrderHeader.PostalCode = applicationUser.PostalCode;

		foreach (var cart in ShoppingCartViewModel.ShoppingCartList)
		{
			cart.Price = GetPriceBasedOnQuantity(cart);
			ShoppingCartViewModel.OrderHeader.OrderTotal += (cart.Price * cart.Count);
		}

        if (applicationUser.CompanyId.GetValueOrDefault() == 0)
        {
			// Customer account
			ShoppingCartViewModel.OrderHeader.PaymentStatus = PaymentStatus.Pending;
            ShoppingCartViewModel.OrderHeader.OrderStatus = OrderStatus.Pending;
		}
        else
        {
			// Company account
			ShoppingCartViewModel.OrderHeader.PaymentStatus = PaymentStatus.Delayed;
			ShoppingCartViewModel.OrderHeader.OrderStatus = OrderStatus.Approved;
		}

        _unitOfWork.OrderHeader.Add(ShoppingCartViewModel.OrderHeader);
        _unitOfWork.Save();

        foreach (var cart in ShoppingCartViewModel.ShoppingCartList)
        {
            OrderDetail orderDetail = new()
            {
                ProductId = cart.ProductId,
                OrderHeaderId = ShoppingCartViewModel.OrderHeader.Id,
                Price = cart.Price,
                Count = cart.Count,
            };

            _unitOfWork.OrderDetail.Add(orderDetail);
            _unitOfWork.Save();
        }

		if (applicationUser.CompanyId.GetValueOrDefault() == 0)
		{
            var domain = "https://localhost:7232/";
            // Customer account - make payment
            var options = new SessionCreateOptions
            {
                //SuccessUrl = "https://example.com/success",
                SuccessUrl = domain + $"customer/shoppingcart/OrderConfirmation?id{ShoppingCartViewModel.OrderHeader.Id}",
                CancelUrl = domain + "customer/cart/index",
                LineItems = new List<SessionLineItemOptions>(),
                Mode = "payment",
            };

            foreach (var item in ShoppingCartViewModel.ShoppingCartList) 
            {
                var sessionLineItem = new SessionLineItemOptions()
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)(item.Price * 100), // 10.10 => 1010
                        Currency = "eur",
                        ProductData = new SessionLineItemPriceDataProductDataOptions()
                        {
                            Name = item.Product.Title,
                        }
                    },
                    Quantity = item.Count
                };
                options.LineItems.Add(sessionLineItem);
            }

            var service = new SessionService();
            Session session = service.Create(options);

            _unitOfWork.OrderHeader.UpdateStripePaymentId(ShoppingCartViewModel.OrderHeader.Id, session.Id, session.PaymentIntentId);
            _unitOfWork.Save();

            Response.Headers.Add("Location", session.Url);

            return new StatusCodeResult(303);
        }

        // Call OrderConfirmation with the id equal to =>
		return RedirectToAction(nameof(OrderConfirmation), new { id = ShoppingCartViewModel.OrderHeader.Id });
	}

    public IActionResult OrderConfirmation(int id)
    {
        // Logic to check whether payment went successfully
        OrderHeader orderHeader = _unitOfWork.OrderHeader.Get(oh => oh.Id == id, includeProperties: "ApplicationUser");

        // Customer payments (only Company payment can be in Delayed
        if (orderHeader.PaymentStatus != PaymentStatus.Delayed)
        {
            var sessionService = new SessionService();
            Session session = sessionService.Get(orderHeader.SessionId);

            if (session.PaymentStatus.ToLower() == "paid")
            {
                _unitOfWork.OrderHeader.UpdateStripePaymentId(id, session.Id, session.PaymentIntentId);
                _unitOfWork.OrderHeader.UpdateStatus(id, OrderStatus.Approved, PaymentStatus.Approved);
                _unitOfWork.Save();
            }
        }

        List<ShoppingCart> shoppingCarts = _unitOfWork.ShoppingCart.GetAll(sc => sc.ApplicationUserId == orderHeader.ApplicationUserId).ToList();

        _unitOfWork.ShoppingCart.DeleteRange(shoppingCarts);
        _unitOfWork.Save();

        return View(id);
    }

	public IActionResult Plus(int cartId)
    {
        var cartFromDb = _unitOfWork.ShoppingCart.Get(sc => sc.Id == cartId);
        cartFromDb.Count += 1;
        
        _unitOfWork.ShoppingCart.Update(cartFromDb);
        _unitOfWork.Save();

        return RedirectToAction(nameof(Index));
    }

    public IActionResult Minus(int cartId)
    {
        var cartFromDb = _unitOfWork.ShoppingCart.Get(sc => sc.Id == cartId);
        // If we are at 1 or less, then we don't lower count, we remove whole cart for that item
        if (cartFromDb.Count <= 1)
        {
            _unitOfWork.ShoppingCart.Delete(cartFromDb);
        }
        else
        {
            cartFromDb.Count -= 1;
            _unitOfWork.ShoppingCart.Update(cartFromDb);
        }
        
        _unitOfWork.Save();

        return RedirectToAction(nameof(Index));
    }

    public IActionResult Remove(int cartId)
    {
        var cartFromDb = _unitOfWork.ShoppingCart.Get(sc => sc.Id == cartId);

        _unitOfWork.ShoppingCart.Delete(cartFromDb);
        _unitOfWork.Save();

        return RedirectToAction(nameof(Index));
    }

    private double GetPriceBasedOnQuantity(ShoppingCart shoppingCart)
    {
        if (shoppingCart.Count <= 50)
        {
            return shoppingCart.Product.Price;
        }
        else
        {
            if (shoppingCart.Count <= 100)
            {
                return shoppingCart.Product.Price50;
            }
            else
            {
                return shoppingCart.Product.Price100;
            }
        }
    }
}
