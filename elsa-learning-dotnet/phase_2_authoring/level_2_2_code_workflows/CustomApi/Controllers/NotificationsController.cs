using CustomApi.Data;
using CustomApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomApi.Controllers;

/// <summary>
/// The workflows' "HTTP Request" activities POST here (a stand-in for Teams/Slack). A notification that
/// carries an approval pattern also opens a pending approval for its order.
/// </summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly CustomDbContext _db;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(CustomDbContext db, ILogger<NotificationsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IEnumerable<NotificationDto>> List(CancellationToken ct)
    {
        var notifications = await _db.Notifications.OrderByDescending(n => n.ReceivedAt).ThenByDescending(n => n.Id).Take(200).ToListAsync(ct);

        var approvalIds = notifications.Where(n => n.ApprovalId is not null).Select(n => n.ApprovalId!.Value).Distinct().ToList();
        var pendingApprovalIds = (await _db.Approvals
            .Where(a => approvalIds.Contains(a.Id) && a.Status == ApprovalStatuses.Pending)
            .Select(a => a.Id).ToListAsync(ct)).ToHashSet();

        var orderIds = notifications.Where(n => n.OrderId is not null).Select(n => n.OrderId!).Distinct().ToList();
        var orderStatuses = await _db.Orders.Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Status, ct);

        return notifications.Select(n => new NotificationDto(
            n.Id, n.ReceivedAt, n.Title, n.Message, n.OrderId, n.Link, n.RejectLink, n.ApprovalId,
            IsActionable: n.ApprovalId is { } aid && pendingApprovalIds.Contains(aid),
            OrderStatus: n.OrderId is not null ? orderStatuses.GetValueOrDefault(n.OrderId) : null));
    }

    /// <summary>
    /// Webhook for the workflows. DEV ONLY: anonymous machine-to-machine endpoint — a real receiver
    /// would check a shared secret or signature. Returns 200 (the workflows expect exactly 200).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Receive(NotificationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title) && string.IsNullOrWhiteSpace(request.Message))
            return ValidationProblem("Title or Message is required.");

        var notification = new Notification
        {
            Title = request.Title?.Trim() ?? "(no title)",
            Message = request.Message ?? "",
            OrderId = Blank(request.OrderId),
            Link = Blank(request.Link),
            RejectLink = Blank(request.RejectLink)
        };

        // ELSA CONCEPT: the workflow is about to SUSPEND waiting for a decision; recording the pending
        // approval here is how the business side learns what it waits for.
        Approval? approval = null;
        if (notification.OrderId is not null
            && Enum.TryParse<ApprovalPattern>(request.Pattern, ignoreCase: true, out var pattern)
            && pattern is ApprovalPattern.Event or ApprovalPattern.EmailLink or ApprovalPattern.MultiStage)
        {
            // A new request for the same order replaces any older one (multi-stage: manager → finance).
            await _db.Approvals
                .Where(a => a.OrderId == notification.OrderId && a.Status == ApprovalStatuses.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ApprovalStatuses.Closed), ct);

            approval = new Approval
            {
                OrderId = notification.OrderId,
                Pattern = pattern,
                Stage = Blank(request.Stage),
                ApproveLink = pattern == ApprovalPattern.EmailLink ? notification.Link : null,
                RejectLink = pattern == ApprovalPattern.EmailLink ? notification.RejectLink : null
            };
            _db.Approvals.Add(approval);
            await _db.SaveChangesAsync(ct);
            notification.ApprovalId = approval.Id;
        }

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Notification {Title} for order {OrderId}{Approval}", notification.Title, notification.OrderId,
            approval is null ? "" : $" → pending approval {approval.Id} ({approval.Pattern} {approval.Stage})");
        return Ok(new { received = true, notification.Id, approvalId = approval?.Id });
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
