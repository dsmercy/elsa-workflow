using CustomApi.Data;
using CustomApi.Models;
using CustomApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomApi.Controllers;

/// <summary>
/// Pending approvals are rows in custom.sqlite.db, created when a workflow asks for one
/// (POST /api/notifications with a pattern, or POST /api/tasks). Deciding one resumes the workflow
/// on the Elsa Server — each pattern through a different Elsa API.
/// DEV ONLY: no authentication — anyone who can reach the API can approve.
/// </summary>
[ApiController]
[Route("api/approvals")]
public class ApprovalsController : ControllerBase
{
    public const string OrderReviewedEvent = "OrderReviewed";

    /// <summary>Multi-stage: one event per stage; the decision travels in the input.</summary>
    public static string StageEvent(string stage) => $"L22.{stage}Reviewed";

    private readonly CustomDbContext _db;
    private readonly ElsaClient _elsa;
    private readonly ILogger<ApprovalsController> _logger;

    public ApprovalsController(CustomDbContext db, ElsaClient elsa, ILogger<ApprovalsController> logger)
    {
        _db = db;
        _elsa = elsa;
        _logger = logger;
    }

    /// <summary>GET /api/approvals?status=pending (default) | decided | closed | all</summary>
    [HttpGet]
    public async Task<IEnumerable<ApprovalDto>> List([FromQuery] string status = ApprovalStatuses.Pending, CancellationToken ct = default)
    {
        var query = _db.Approvals.AsQueryable();
        if (!string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            query = query.Where(a => a.Status == status.ToLower());
        var approvals = await query.OrderBy(a => a.RequestedAt).Take(200).ToListAsync(ct);
        return await WithOrdersAsync(approvals, ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApprovalDto>> Get(int id, CancellationToken ct)
    {
        var approval = await _db.Approvals.FindAsync([id], ct);
        if (approval is null) return NotFound();
        return ApprovalDto.From(approval, await _db.Orders.FindAsync([approval.OrderId], ct));
    }

    /// <summary>
    /// Approve or reject. A decision can be made once: a second call gets 409 Conflict.
    ///   Event      → POST /elsa/api/events/OrderReviewed/trigger        (correlationId = order ID)
    ///   MultiStage → POST /elsa/api/events/L22.{Stage}Reviewed/trigger
    ///   RunTask    → POST /elsa/api/tasks/{taskId}/complete
    ///   EmailLink  → GET the approve/reject link (the workflow's HTTP Endpoint)
    /// </summary>
    [HttpPost("{id:int}/decision")]
    public async Task<ActionResult<ApprovalDto>> Decide(int id, DecisionRequest request, CancellationToken ct)
    {
        var approval = await _db.Approvals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (approval is null) return NotFound();

        var reviewer = string.IsNullOrWhiteSpace(request.Reviewer) ? "manager" : request.Reviewer.Trim();
        var now = DateTime.UtcNow;

        // Claim the approval atomically: only one request can move it out of "pending".
        var claimed = await _db.Approvals
            .Where(a => a.Id == id && a.Status == ApprovalStatuses.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, ApprovalStatuses.Decided)
                .SetProperty(a => a.Approved, request.Approved)
                .SetProperty(a => a.Reviewer, reviewer)
                .SetProperty(a => a.Comment, request.Comment)
                .SetProperty(a => a.DecidedAt, now), ct);
        if (claimed == 0)
            return Conflict(new { error = $"Approval {id} is no longer pending (it is '{approval.Status}')." });

        try
        {
            var decision = new { request.Approved, Reviewer = reviewer, request.Comment };
            switch (approval.Pattern)
            {
                case ApprovalPattern.Event:
                    await _elsa.TriggerEventAsync(OrderReviewedEvent, approval.OrderId, decision, ct);
                    break;
                case ApprovalPattern.MultiStage when approval.Stage is "Manager" or "Finance":
                    await _elsa.TriggerEventAsync(StageEvent(approval.Stage), approval.OrderId, decision, ct);
                    break;
                case ApprovalPattern.RunTask when approval.TaskId is not null:
                    await _elsa.CompleteTaskAsync(approval.TaskId, decision, ct);
                    break;
                case ApprovalPattern.EmailLink when (request.Approved ? approval.ApproveLink : approval.RejectLink) is { } link:
                    await _elsa.FollowLinkAsync(link, ct);
                    break;
                default:
                    throw new ElsaApiException($"Approval {id} ({approval.Pattern}) has no way to resume its workflow.");
            }
        }
        catch (Exception ex) when (ex is ElsaApiException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // Give the approval back so the reviewer can retry.
            await _db.Approvals.Where(a => a.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, ApprovalStatuses.Pending)
                .SetProperty(a => a.Approved, (bool?)null)
                .SetProperty(a => a.Reviewer, (string?)null)
                .SetProperty(a => a.Comment, (string?)null)
                .SetProperty(a => a.DecidedAt, (DateTime?)null), CancellationToken.None);
            _logger.LogError(ex, "Resuming the workflow for approval {ApprovalId} (order {OrderId}) failed", id, approval.OrderId);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex is ElsaApiException or InvalidOperationException ? ex.Message : "The Elsa Server is not reachable." });
        }

        _logger.LogInformation("Approval {ApprovalId} ({Pattern}) for order {OrderId}: approved={Approved} by {Reviewer}",
            id, approval.Pattern, approval.OrderId, request.Approved, reviewer);
        var updated = await _db.Approvals.AsNoTracking().FirstAsync(a => a.Id == id, ct);
        return ApprovalDto.From(updated, await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == approval.OrderId, ct));
    }

    private async Task<List<ApprovalDto>> WithOrdersAsync(List<Approval> approvals, CancellationToken ct)
    {
        var orderIds = approvals.Select(a => a.OrderId).Distinct().ToList();
        var orders = await _db.Orders.Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        return approvals.Select(a => ApprovalDto.From(a, orders.GetValueOrDefault(a.OrderId))).ToList();
    }
}
