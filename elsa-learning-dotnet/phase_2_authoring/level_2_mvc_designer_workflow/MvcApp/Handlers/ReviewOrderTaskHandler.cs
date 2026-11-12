using System.Globalization;
using Elsa.Mediator.Contracts;
using Elsa.Workflows.Runtime.Notifications;
using MvcApp.Models;
using MvcApp.Services;

namespace MvcApp.Handlers;

/// <summary>
/// ELSA CONCEPT: when a workflow reaches a "Run Task" activity, Elsa publishes a RunTaskRequest
/// notification and the workflow SUSPENDS. The application decides what "running the task" means —
/// here: put it in the approvals inbox so a person can complete it. Completing it later
/// (ITaskReporter.ReportCompletionAsync) resumes the workflow with the result.
/// </summary>
public class ReviewOrderTaskHandler : INotificationHandler<RunTaskRequest>
{
    public const string TaskName = "ReviewOrder";

    private readonly ApprovalInbox _inbox;
    private readonly ILogger<ReviewOrderTaskHandler> _logger;

    public ReviewOrderTaskHandler(ApprovalInbox inbox, ILogger<ReviewOrderTaskHandler> logger)
    {
        _inbox = inbox;
        _logger = logger;
    }

    public Task HandleAsync(RunTaskRequest notification, CancellationToken cancellationToken)
    {
        // Other Run Task names are ignored; each handler picks the tasks it understands.
        if (notification.TaskName != TaskName)
            return Task.CompletedTask;

        var payload = notification.TaskPayload ?? new Dictionary<string, object>();
        string Read(string key) =>
            payload.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";

        var workflowInstanceId = notification.ActivityExecutionContext.WorkflowExecutionContext.Id;
        var task = new PendingReviewTask(
            TaskId: notification.TaskId,
            WorkflowInstanceId: workflowInstanceId,
            OrderId: Read("OrderId"),
            Product: Read("Product"),
            Amount: Read("Amount"),
            RequestedAt: DateTimeOffset.UtcNow);

        _inbox.AddTask(task);

        // Run Task doubles as an in-app NOTIFICATION channel: the app decides how to tell people.
        _inbox.AddNotification(new WebhookNotification(
            DateTimeOffset.UtcNow,
            Title: $"Task \"{TaskName}\" requested",
            Message: $"Order {task.OrderId} ({task.Product}, {task.Amount}) needs a review. Complete it on the Approvals page.",
            OrderId: task.OrderId,
            Link: "/Approvals",
            RejectLink: null));

        _logger.LogInformation(
            "Run Task {TaskName} {TaskId} queued for order {OrderId} (workflow instance {WorkflowInstanceId})",
            TaskName, task.TaskId, task.OrderId, workflowInstanceId);

        return Task.CompletedTask;
    }
}
