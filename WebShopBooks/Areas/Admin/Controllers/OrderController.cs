﻿using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using WebShopBooks.DataAccess.Repository.IRepository;
using WebShopBooks.Models.Models;
using WebShopBooks.Utility;

namespace WebShopBooks.Areas.Admin.Controllers;

[Area("Admin")]
public class OrderController : Controller
{
    private readonly IUnitOfWork _unitOfWork;

    public OrderController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public IActionResult Index()
    {
        return View();
    }

    #region API Calls

    [HttpGet]
    public IActionResult GetAll(string orderStatus)
    {
        IEnumerable<OrderHeader> orderHeaderList = _unitOfWork.OrderHeader.GetAll(includeProperties: "ApplicationUser").ToList();

        switch (orderStatus)
        {
            case "pending":
                orderHeaderList = orderHeaderList.Where(ohl => ohl.PaymentStatus == PaymentStatus.Pending);
                break;
            case "inprocess":
                orderHeaderList = orderHeaderList.Where(ohl => ohl.OrderStatus == OrderStatus.InProcess);
                break;
            case "completed":
                orderHeaderList = orderHeaderList.Where(ohl => ohl.OrderStatus == OrderStatus.Shipped);
                break;
            case "approved":
                orderHeaderList = orderHeaderList.Where(ohl => ohl.OrderStatus == OrderStatus.Approved);
                break;
            default:
                break;
        }

        return Json(new { data = orderHeaderList });
    }

    #endregion
}
