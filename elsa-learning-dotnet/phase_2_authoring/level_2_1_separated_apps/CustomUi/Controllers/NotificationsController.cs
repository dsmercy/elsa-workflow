using CustomUi.Models;
using CustomUi.Services;
using Microsoft.AspNetCore.Mvc;

namespace CustomUi.Controllers;

public class NotificationsController : Controller
{
    private readonly CustomApiClient _api;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(CustomApiClient api, ILogger<NotificationsController> logger)
    {
        _api = api;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = new NotificationsViewModel();
        try
        {
            var all = await _api.GetNotificationsAsync(ct);
            // CustomApi decides what is actionable: the notification's approval is still pending.
            model.NeedsAction = all.Where(n => n.IsActionable).ToList();
            model.History = all.Where(n => !n.IsActionable).ToList();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Loading notifications from CustomApi failed");
            model.Error = "Could not load notifications from CustomApi.";
        }
        return View(model);
    }
}
