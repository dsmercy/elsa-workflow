using Elsa.Workflows.Management;
using Elsa.Workflows.Runtime;
using Microsoft.AspNetCore.Mvc;
using MvcApp.Models;
using MvcApp.Services;

namespace MvcApp.Controllers;

/// <summary>
/// The manager's screen. Every waiting workflow is listed here, and each button RESUMES one:
/// - Event / multi-stage: publish a named event for the order's correlation ID (IEventPublisher)
/// - Run Task:            report the task as completed (ITaskReporter)
/// - Email link:          nothing to do here — the link in the email resumes the HTTP Endpoint
///
/// DEV ONLY: the POST actions skip the antiforgery check so they can be called from curl/Postman while
/// learning. That leaves them open to cross-site request forgery and they have no login either — restore
/// [ValidateAntiForgeryToken] (and add [Authorize]) before this is used anywhere real.
/// </summary>
public class ApprovalsController : Controller
{
    // Event names the Studio workflows wait for (Event activity → Event name).
    public const string OrderReviewedEvent = "OrderReviewed";

    // One event per stage; the approve/reject decision travels in the payload (ReviewDecision.Approved).
    // (One trigger per state on purpose: in Elsa 3.9.0, two competing triggers on a State Machine nested
    //  in a Flowchart fault the workflow when the losing trigger is cancelled.)
    public static string StageEvent(string stage) => $"L2.{stage}Reviewed";

    private readonly IEventPublisher _eventPublisher;
    private readonly ITaskReporter _taskReporter;
    private readonly IWorkflowInstanceStore _workflowInstanceStore;
    private readonly ApprovalInbox _inbox;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApprovalsController> _logger;

    public ApprovalsController(
        IEventPublisher eventPublisher,
        ITaskReporter taskReporter,
        IWorkflowInstanceStore workflowInstanceStore,
        ApprovalInbox inbox,
        IConfiguration configuration,
        ILogger<ApprovalsController> logger)
    {
        _eventPublisher = eventPublisher;
        _taskReporter = taskReporter;
        _workflowInstanceStore = workflowInstanceStore;
        _inbox = inbox;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = new ApprovalsViewModel
        {
            Message = TempData["Message"] as string,
            Error = TempData["Error"] as string,
            PendingTasks = _inbox.GetTasks().ToList(),
            EmailLinkBaseUrl = $"{_configuration["Http:BaseUrl"]}{_configuration["Http:BasePath"]}/l2/approvals/decision"
        };

        try
        {
            var waiting = (await OrderInstances.LoadAsync(_workflowInstanceStore, _configuration, cancellationToken))
                .Where(r => r.IsWaiting)
                .ToList();
            model.WaitingForEvent = waiting.Where(r => r.Pattern == ApprovalPattern.Event).ToList();
            model.WaitingForEmailLink = waiting.Where(r => r.Pattern == ApprovalPattern.EmailLink).ToList();
            model.WaitingInStateMachine = waiting.Where(r => r.Pattern == ApprovalPattern.MultiStage).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading waiting workflow instances failed");
            model.Error = "Could not load waiting workflows. Check the server log.";
        }

        return View(model);
    }

    /// <summary>Single approver: publish "OrderReviewed" for this order → the Event activity resumes.</summary>
    [HttpPost]
    [IgnoreAntiforgeryToken] // DEV ONLY: lets curl/Postman call this without a token (see class comment)
    public Task<IActionResult> Review(string orderId, bool approved, string? reviewer, CancellationToken cancellationToken) =>
        PublishAsync(OrderReviewedEvent, orderId, approved, reviewer, cancellationToken);

    /// <summary>Multi-stage: publish "L2.ManagerReviewed" / "L2.FinanceReviewed" with the decision → the State Machine moves on.</summary>
    [HttpPost]
    [IgnoreAntiforgeryToken] // DEV ONLY: lets curl/Postman call this without a token (see class comment)
    public Task<IActionResult> Stage(string orderId, string stage, bool approved, string? reviewer, CancellationToken cancellationToken)
    {
        if (stage is not ("Manager" or "Finance"))
        {
            TempData["Error"] = $"Unknown stage '{stage}'.";
            return Task.FromResult<IActionResult>(RedirectToAction(nameof(Index)));
        }
        return PublishAsync(StageEvent(stage), orderId, approved, reviewer, cancellationToken);
    }

    /// <summary>Run Task: report the "ReviewOrder" task complete → the Run Task activity resumes with the result.</summary>
    [HttpPost]
    [IgnoreAntiforgeryToken] // DEV ONLY: lets curl/Postman call this without a token (see class comment)
    public async Task<IActionResult> CompleteTask(string taskId, bool approved, string? reviewer, CancellationToken cancellationToken)
    {
        if (!_inbox.TryRemoveTask(taskId, out var task) || task is null)
        {
            TempData["Error"] = "That task is no longer pending (already completed, or the app restarted).";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var decision = new ReviewDecision(approved, ReviewerName(reviewer), null);
            // ELSA CONCEPT: completing the task resumes the bookmark the Run Task activity created.
            await _taskReporter.ReportCompletionAsync(taskId, decision, cancellationToken);

            _logger.LogInformation("Run Task {TaskId} for order {OrderId} completed: approved={Approved}", taskId, task.OrderId, approved);
            TempData["Message"] = $"Task for order {task.OrderId} completed ({(approved ? "approved" : "rejected")}).";
        }
        catch (Exception ex)
        {
            _inbox.AddTask(task); // put it back so the user can retry
            _logger.LogError(ex, "Completing Run Task {TaskId} for order {OrderId} failed", taskId, task.OrderId);
            TempData["Error"] = "Completing the task failed. Check the server log.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> PublishAsync(string eventName, string orderId, bool approved, string? reviewer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            TempData["Error"] = "Missing order ID.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var decision = new ReviewDecision(approved, ReviewerName(reviewer), null);

            // ELSA CONCEPT: a STIMULUS. Elsa looks for bookmarks waiting for this event name on instances
            // whose CORRELATION ID is the order ID (set by the Correlate activity) and resumes them,
            // passing the payload as the Event activity's output.
            await _eventPublisher.PublishAsync(eventName, correlationId: orderId, payload: decision, cancellationToken: cancellationToken);

            _logger.LogInformation("Published {EventName} for order {OrderId}: approved={Approved}", eventName, orderId, approved);
            TempData["Message"] = $"Sent \"{eventName}\" for order {orderId}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publishing {EventName} for order {OrderId} failed", eventName, orderId);
            TempData["Error"] = $"Publishing \"{eventName}\" failed. Check the server log.";
        }

        return RedirectToAction(nameof(Index));
    }

    private static string ReviewerName(string? reviewer) =>
        string.IsNullOrWhiteSpace(reviewer) ? "manager" : reviewer.Trim();
}
