namespace MvcApp.Models;

/// <summary>
/// The approval patterns this level demonstrates. Each one is a separate Studio workflow;
/// its definition ID comes from configuration (see appsettings.json → Workflow).
/// </summary>
public enum ApprovalPattern
{
    /// <summary>L2_OrderApproval — the original rule-only workflow, no human step.</summary>
    Rule,
    /// <summary>L2_EventApproval — Correlate → notify → Event "OrderReviewed" → Decision.</summary>
    Event,
    /// <summary>L2_EmailLinkApproval — email with links → HTTP Endpoint waits for the click.</summary>
    EmailLink,
    /// <summary>L2_RunTaskApproval — Run Task "ReviewOrder"; the app completes the task.</summary>
    RunTask,
    /// <summary>L2_MultiStageApproval — State Machine: manager, then finance.</summary>
    MultiStage
}

public static class ApprovalPatterns
{
    /// <summary>Configuration key that holds the definition ID for each pattern.</summary>
    public static string ConfigKey(ApprovalPattern pattern) => pattern switch
    {
        ApprovalPattern.Rule => "Workflow:DefinitionId",
        _ => $"Workflow:Approvals:{pattern}"
    };

    public static string Describe(ApprovalPattern pattern) => pattern switch
    {
        ApprovalPattern.Rule => "Rule only (no approval): Amount ≤ 1000 approved, otherwise needs_review",
        ApprovalPattern.Event => "Single approver: notify → wait for Event \"OrderReviewed\"",
        ApprovalPattern.EmailLink => "Approve by email link: notify → HTTP Endpoint waits for the click",
        ApprovalPattern.RunTask => "Run Task: workflow asks the app to run \"ReviewOrder\" and waits for completion",
        ApprovalPattern.MultiStage => "Multi-stage (State Machine): manager review → finance review",
        _ => pattern.ToString()
    };
}

/// <summary>
/// The approver's answer. Sent as the Event payload (Event pattern, multi-stage) or as the
/// Run Task result; the workflows read it in JavaScript as variables.Review.Approved.
/// </summary>
public record ReviewDecision(bool Approved, string Reviewer, string? Comment);

/// <summary>A message posted to /notifications/webhook by a workflow's HTTP Request activity.</summary>
public record WebhookNotification(
    DateTimeOffset ReceivedAt,
    string Title,
    string Message,
    string? OrderId,
    string? Link,
    string? RejectLink);

/// <summary>Body the workflows POST to the webhook (same shape a Teams/Slack bridge would receive).</summary>
public class WebhookPayload
{
    public string? Title { get; set; }
    public string? Message { get; set; }
    public string? OrderId { get; set; }
    public string? Link { get; set; }
    public string? RejectLink { get; set; }
}

/// <summary>A pending "ReviewOrder" Run Task waiting for someone to complete it.</summary>
public record PendingReviewTask(
    string TaskId,
    string WorkflowInstanceId,
    string OrderId,
    string Product,
    string Amount,
    DateTimeOffset RequestedAt);

/// <summary>One row on the Orders and Approvals pages, read from Elsa's instance store.</summary>
public record OrderInstanceRow(
    string WorkflowInstanceId,
    string DefinitionId,
    ApprovalPattern? Pattern,
    string? OrderId,
    string? Name,
    string Status,
    string SubStatus,
    string? OrderStatus,
    DateTimeOffset CreatedAt)
{
    /// <summary>Suspended with no Status output yet → still waiting for a person.</summary>
    public bool IsWaiting => SubStatus == "Suspended" && OrderStatus is null;

    /// <summary>For the multi-stage workflow, the State Machine renames the instance per stage.</summary>
    public string? Stage =>
        Name?.Contains("awaiting Finance", StringComparison.OrdinalIgnoreCase) == true ? "Finance"
        : Name?.Contains("awaiting Manager", StringComparison.OrdinalIgnoreCase) == true ? "Manager"
        : null;
}

public class ApprovalsViewModel
{
    public List<OrderInstanceRow> WaitingForEvent { get; set; } = [];
    public List<OrderInstanceRow> WaitingForEmailLink { get; set; } = [];
    public List<OrderInstanceRow> WaitingInStateMachine { get; set; } = [];
    public List<PendingReviewTask> PendingTasks { get; set; } = [];
    public string? Message { get; set; }
    public string? Error { get; set; }
    public string EmailLinkBaseUrl { get; set; } = "";

    public bool HasPending =>
        WaitingForEvent.Count + WaitingForEmailLink.Count + WaitingInStateMachine.Count + PendingTasks.Count > 0;
}

/// <summary>
/// One notification plus what has happened to its order since it was sent.
/// IsActionable: the order still waits for a person AND this is that order's latest notification with
/// links — only then are its Approve/Reject links shown. Outcome: the order's result once decided.
/// </summary>
public record NotificationRow(WebhookNotification Notification, bool IsActionable, string? Outcome)
{
    public bool HasLinks => !string.IsNullOrWhiteSpace(Notification.Link) || !string.IsNullOrWhiteSpace(Notification.RejectLink);
}

public class NotificationsViewModel
{
    public List<NotificationRow> NeedsAction { get; set; } = [];
    public List<NotificationRow> History { get; set; } = [];
    public string? Error { get; set; }
}
