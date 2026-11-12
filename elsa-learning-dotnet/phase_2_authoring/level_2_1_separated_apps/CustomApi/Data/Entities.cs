namespace CustomApi.Data;

// The business data of level 2.1. It lives in custom.sqlite.db and has NO link to Elsa's tables
// except two plain strings: the order ID (= the workflow's correlation ID) and the workflow instance ID.

/// <summary>Which approval workflow an order runs. Each maps to a definition ID in "Workflows" config.</summary>
public enum ApprovalPattern { Rule, Event, EmailLink, RunTask, MultiStage }

/// <summary>Who placed the order. Drives which approvals the multi-stage workflow asks for.</summary>
public enum OrderCreator { Customer, CompanyEmployee }

/// <summary>
/// Business rules owned by CustomApi. The workflow does not know WHY a stage is skipped; it receives the
/// result as Order.RequireManagerApproval and routes on it.
/// </summary>
public static class ApprovalRules
{
    /// <summary>Customer orders need manager + finance; company-employee orders go straight to finance.</summary>
    public static bool RequiresManagerApproval(OrderCreator createdBy) => createdBy == OrderCreator.Customer;
}

public static class OrderStatuses
{
    public const string Running = "running";       // workflow started, no result yet
    public const string Waiting = "waiting";       // workflow suspended, waiting for a person
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string NeedsReview = "needs_review"; // rule-only pattern for amounts over 1000
    public const string Failed = "failed";         // workflow faulted or could not be started
}

public static class ApprovalStatuses
{
    public const string Pending = "pending";   // waiting for a decision
    public const string Decided = "decided";   // decided through CustomApi
    public const string Closed = "closed";     // the order finished another way (e.g. the email link was clicked)
}

public class Order
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Product { get; set; } = "";
    public decimal Amount { get; set; }
    public ApprovalPattern Pattern { get; set; }
    public OrderCreator CreatedBy { get; set; } = OrderCreator.Customer;
    public string Status { get; set; } = OrderStatuses.Running;
    public string? WorkflowDefinitionId { get; set; }
    public string? WorkflowInstanceId { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
}

public class Approval
{
    public int Id { get; set; }
    public string OrderId { get; set; } = "";
    public ApprovalPattern Pattern { get; set; }
    /// <summary>Manager or Finance (multi-stage only).</summary>
    public string? Stage { get; set; }
    /// <summary>Elsa task ID (Run Task only).</summary>
    public string? TaskId { get; set; }
    /// <summary>Elsa HTTP Endpoint URLs (email-link only).</summary>
    public string? ApproveLink { get; set; }
    public string? RejectLink { get; set; }
    public string Status { get; set; } = ApprovalStatuses.Pending;
    public bool? Approved { get; set; }
    public string? Reviewer { get; set; }
    public string? Comment { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
}

public class Notification
{
    public int Id { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? OrderId { get; set; }
    public string? Link { get; set; }
    public string? RejectLink { get; set; }
    /// <summary>The approval this notification asked for, if any.</summary>
    public int? ApprovalId { get; set; }
}
