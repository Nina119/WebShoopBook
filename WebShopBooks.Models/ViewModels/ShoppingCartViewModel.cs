﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebShopBooks.Models.Models;

namespace WebShopBooks.Models.ViewModels;

public class ShoppingCartViewModel
{
    public IEnumerable<ShoppingCart> ShoppingCartList { get; set; }
    public double OrderTotal { get; set; }
}
