using CustomUi.Models;
using CustomUi.Services;
using Microsoft.AspNetCore.Mvc;

namespace CustomUi.Controllers;

/// <summary>
/// The approver's screen. Lists CustomApi's pending approvals; each button calls
/// POST {CustomApi}/api/approvals/{id}/decision, and CustomApi resumes the workflow on the Elsa Server.
/// </summary>
public class ApprovalsController : Controller
{
    private readonly CustomApiClient _api;
    private readonly ILogger<ApprovalsController> _logger;

    public ApprovalsController(CustomApiClient api, ILogger<ApprovalsController> logger)
    {
        _api = api;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = new ApprovalsViewModel
        {
            Message = TempData["Message"] as string,
            Error = TempData["Error"] as string
        };
        try
        {
            model.Pending = await _api.GetPendingApprovalsAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Loading approvals from CustomApi failed");
            model.Error = "Could not load approvals from CustomApi.";
        }
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Decide(int id, bool approved, string? reviewer, CancellationToken ct)
    {
        try
        {
            var error = await _api.DecideAsync(id, approved, reviewer, ct);
            if (error is null) TempData["Message"] = $"Approval {id}: {(approved ? "approved" : "rejected")}.";
            else TempData["Error"] = error;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Sending decision {ApprovalId} to CustomApi failed", id);
            TempData["Error"] = "CustomApi is not reachable.";
        }
        return RedirectToAction(nameof(Index));
    }
}
