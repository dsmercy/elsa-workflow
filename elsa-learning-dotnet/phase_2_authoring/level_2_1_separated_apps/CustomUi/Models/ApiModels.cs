using System.ComponentModel.DataAnnotations;

namespace CustomUi.Models;

// Shapes of CustomApi's JSON. CustomUi keeps its own copies on purpose: the two apps share no code,
// only the HTTP contract.

public enum ApprovalPattern { Rule, Event, EmailLink, RunTask, MultiStage }

/// <summary>Who placed the order. CustomApi uses it to decide which approvals a multi-stage order needs.</summary>
public enum OrderCreator { Customer, CompanyEmployee }

public static class ApprovalPatterns
{
    public static string Describe(ApprovalPattern pattern) => pattern switch
    {
        ApprovalPattern.Rule => "Rule only (level 2 basics): no approver, amount > 1000 → needs_review",
        ApprovalPattern.Event => "Single approver: notify → wait for Event \"OrderReviewed\"",
        ApprovalPattern.EmailLink => "Approve by email link: HTTP Endpoint waits for the click",
        ApprovalPattern.RunTask => "Run Task \"ReviewOrder\": CustomApi completes the task",
        ApprovalPattern.MultiStage => "Multi-stage (State Machine): manager → finance (employee orders: finance only)",
        _ => pattern.ToString()
    };

    public static string Describe(OrderCreator creator) => creator switch
    {
        OrderCreator.Customer => "Customer",
        OrderCreator.CompanyEmployee => "Company employee",
        _ => creator.ToString()
    };
}

public record OrderDto(
    string Id, string Product, decimal Amount, string Pattern, string Status,
    string? WorkflowDefinitionId, string? WorkflowInstanceId, string? Error,
    DateTime CreatedAt, DateTime? DecidedAt, string CreatedBy)
{
    public bool IsWaiting => Status is "waiting" or "running";
}

public record ApprovalDto(
    int Id, string OrderId, string? Product, decimal? Amount, string Pattern, string? Stage, string? TaskId,
    string? ApproveLink, string? RejectLink, string Status, bool? Approved, string? Reviewer, string? Comment,
    DateTime RequestedAt, DateTime? DecidedAt, string? OrderCreatedBy);

public record NotificationDto(
    int Id, DateTime ReceivedAt, string Title, string Message, string? OrderId, string? Link, string? RejectLink,
    int? ApprovalId, bool IsActionable, string? OrderStatus);

// ---- View models ----

public class OrderForm
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string Product { get; set; } = "";

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; } = 5000;

    public ApprovalPattern Pattern { get; set; } = ApprovalPattern.Event;

    public OrderCreator CreatedBy { get; set; } = OrderCreator.Customer;
}

public class CreateOrderViewModel
{
    public OrderForm Form { get; set; } = new();
    public OrderDto? Result { get; set; }
    public string? Error { get; set; }
}

public class ApprovalsViewModel
{
    public List<ApprovalDto> Pending { get; set; } = [];
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public class NotificationsViewModel
{
    public List<NotificationDto> NeedsAction { get; set; } = [];
    public List<NotificationDto> History { get; set; } = [];
    public string? Error { get; set; }
}
