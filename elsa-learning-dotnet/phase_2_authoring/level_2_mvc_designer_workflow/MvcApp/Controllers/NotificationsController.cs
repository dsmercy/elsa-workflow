using Elsa.Workflows.Management;
using Microsoft.AspNetCore.Mvc;
using MvcApp.Models;
using MvcApp.Services;

namespace MvcApp.Controllers;

/// <summary>
/// A stand-in for Teams/Slack/Discord. The workflows' "HTTP Request" activity POSTs JSON here;
/// point that activity's URL at a real incoming-webhook URL instead and nothing else changes.
/// The page splits notifications into "needs action" (the order is still waiting) and history,
/// checking each order's workflow instance so links are not offered for orders already decided.
/// </summary>
public class NotificationsController : Controller
{
    private readonly ApprovalInbox _inbox;
    private readonly IWorkflowInstanceStore _workflowInstanceStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        ApprovalInbox inbox,
        IWorkflowInstanceStore workflowInstanceStore,
        IConfiguration configuration,
        ILogger<NotificationsController> logger)
    {
        _inbox = inbox;
        _workflowInstanceStore = workflowInstanceStore;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = new NotificationsViewModel();
        var notifications = _inbox.GetNotifications(); // newest first

        // Order ID → its latest workflow instance (the order ID is the instance's correlation ID).
        var latestByOrder = new Dictionary<string, OrderInstanceRow>();
        try
        {
            var rows = await OrderInstances.LoadAsync(_workflowInstanceStore, _configuration, cancellationToken);
            foreach (var row in rows.Where(r => r.OrderId is not null).OrderBy(r => r.CreatedAt))
                latestByOrder[row.OrderId!] = row;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading workflow instances for the notifications page failed");
            model.Error = "Could not check which orders are still pending, so no links are offered. Check the server log.";
        }

        var pendingTaskOrders = _inbox.GetTasks().Select(t => t.OrderId).ToHashSet();
        var ordersWithActionShown = new HashSet<string>();

        foreach (var n in notifications)
        {
            OrderInstanceRow? instance = null;
            if (n.OrderId is not null)
                latestByOrder.TryGetValue(n.OrderId, out instance);

            var orderPending = n.OrderId is not null && model.Error is null &&
                               (instance?.IsWaiting == true || pendingTaskOrders.Contains(n.OrderId));

            // Only the newest notification with links per order stays actionable (multi-stage sends one per stage).
            var hasLinks = !string.IsNullOrWhiteSpace(n.Link) || !string.IsNullOrWhiteSpace(n.RejectLink);
            var actionable = orderPending && hasLinks && ordersWithActionShown.Add(n.OrderId!);

            // Once the order is no longer waiting: its "Status" output (approved / rejected), else the sub-status (e.g. Faulted).
            var outcome = instance is null || instance.IsWaiting ? null : instance.OrderStatus ?? instance.SubStatus;

            var item = new NotificationRow(n, actionable, outcome);
            (actionable ? model.NeedsAction : model.History).Add(item);
        }

        return View(model);
    }

    // DEV ONLY: anonymous machine-to-machine endpoint (no antiforgery token — the caller is a workflow,
    // not a browser form). A real receiver would check a shared secret or signature.
    [HttpPost("/notifications/webhook")]
    [IgnoreAntiforgeryToken]
    public IActionResult Webhook([FromBody] WebhookPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Title) && string.IsNullOrWhiteSpace(payload.Message))
            return BadRequest(new { error = "Title or Message is required." });

        _inbox.AddNotification(new WebhookNotification(
            DateTimeOffset.UtcNow,
            Title: payload.Title ?? "(no title)",
            Message: payload.Message ?? "",
            OrderId: payload.OrderId,
            Link: payload.Link,
            RejectLink: payload.RejectLink));

        _logger.LogInformation("Webhook notification received: {Title} (order {OrderId})", payload.Title, payload.OrderId);
        return Ok(new { received = true });
    }
}
