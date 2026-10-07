using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NexaCommerce.Infrastructure;
using NexaCommerce.Models;
using NexaCommerce.Services;
using NexaCommerce.Services.Intelligence;

namespace NexaCommerce.Controllers;

public class HomeController : BaseController
{
    private readonly ProductService _products;
    private readonly CategoryService _categories;
    private readonly BasketAnalysisService _basket;
    private readonly ContactService _contact;

    public HomeController(ProductService products, CategoryService categories, BasketAnalysisService basket, ContactService contact)
    {
        _products = products; _categories = categories; _basket = basket; _contact = contact;
    }

    public async Task<IActionResult> Index()
    {
        var vm = new HomeVm
        {
            Featured = await _products.FeaturedAsync(4),
            BestSellers = await _products.BestSellersAsync(8),
            Deals = await _products.DealsAsync(4),
            Categories = (await _categories.AllAsync(true)).Where(c => c.ParentCategoryId == null).ToList(),
            TopPairs = (await _basket.RulesAsync()).Where(r => r.AntecedentId < r.ConsequentId).Take(3).ToList()
        };
        return View(vm);
    }

    public IActionResult About() => View();
    public IActionResult Faq() => View();
    public IActionResult Privacy() => View();
    public IActionResult Terms() => View();

    [HttpGet]
    public IActionResult Contact() => View(new ContactMessage());

    [HttpPost]
    public async Task<IActionResult> Contact(ContactMessage model)
    {
        if (!ModelState.IsValid) return View(model);
        await _contact.AddAsync(model);
        Success("Thanks, your message has been sent. We reply within one working day.");
        return RedirectToAction(nameof(Contact));
    }

    [Route("/Home/StatusCode/{code:int}")]
    public IActionResult HttpStatus(int code)
    {
        var feature = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        if (feature?.OriginalPath?.StartsWith("/api", StringComparison.OrdinalIgnoreCase) == true)
            return new ObjectResult(new { status = code }) { StatusCode = code };
        Response.StatusCode = code;
        ViewBag.Code = code;
        return View("StatusCode");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewBag.RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View();
    }
}
