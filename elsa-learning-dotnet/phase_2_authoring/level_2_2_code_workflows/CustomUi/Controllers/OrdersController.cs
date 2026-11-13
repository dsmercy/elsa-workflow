using System.Diagnostics;
using CustomUi.Models;
using CustomUi.Services;
using Microsoft.AspNetCore.Mvc;

namespace CustomUi.Controllers;

public class OrdersController : Controller
{
    private readonly CustomApiClient _api;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(CustomApiClient api, ILogger<OrdersController> logger)
    {
        _api = api;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateOrderViewModel());

    /// <summary>Form → POST {CustomApi}/api/orders → CustomApi starts the workflow on the Elsa Server.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] OrderForm form, CancellationToken ct)
    {
        // "Placed by" only applies to the multi-stage pattern (the field is hidden for the others).
        if (form.Pattern != ApprovalPattern.MultiStage)
            form.CreatedBy = OrderCreator.Customer;

        var model = new CreateOrderViewModel { Form = form };
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            (model.Result, model.Error) = await _api.CreateOrderAsync(form, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "CustomApi is not reachable");
            model.Error = "CustomApi is not reachable. Is it running on its configured URL?";
        }
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            return View(await _api.GetOrdersAsync(ct));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Loading orders from CustomApi failed");
            ViewBag.Error = "Could not load orders from CustomApi.";
            return View(new List<OrderDto>());
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View("Error", HttpContext.TraceIdentifier ?? Activity.Current?.Id);
}
