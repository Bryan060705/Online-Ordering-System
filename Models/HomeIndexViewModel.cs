using System.Collections.Generic;

namespace Demo.Models
{
    public class HomeIndexViewModel
    {
        public List<Product> Products { get; set; } = new();
        public List<Ad> Ads { get; set; } = new();
    }
}

