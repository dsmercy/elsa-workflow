using System.ComponentModel.DataAnnotations;

namespace MvcApp.Models;

/// <summary>What the user types in the Orders/Create form.</summary>
public class OrderForm
{
    [Required, StringLength(100)]
    public string Product { get; set; } = "Office chair";

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; } = 250m;

    /// <summary>Which workflow to start — each pattern is a separate Studio definition.</summary>
    public ApprovalPattern Pattern { get; set; } = ApprovalPattern.Rule;
}

/// <summary>What the view shows after the workflow ran.</summary>
public class OrderResult
{
    public required Order Order { get; init; }
    public required ApprovalPattern Pattern { get; init; }
    public required string WorkflowInstanceId { get; init; }
    public required string WorkflowStatus { get; init; }
    public required string WorkflowSubStatus { get; init; }

    /// <summary>The workflow's "Status" output: "approved", "rejected" or "needs_review"; null while waiting.</summary>
    public string? OrderStatus { get; init; }

    /// <summary>Paused at a bookmark with no decision yet — waiting for an approver.</summary>
    public bool IsWaiting => WorkflowSubStatus == "Suspended" && OrderStatus is null;

    /// <summary>Decided, but still suspended while a dispatched child workflow (the audit) finishes.</summary>
    public bool IsFinishing => WorkflowSubStatus == "Suspended" && OrderStatus is not null;
}

public class CreateOrderViewModel
{
    public OrderForm Form { get; set; } = new();
    public OrderResult? Result { get; set; }
    public string? Error { get; set; }
}
