using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using CustomApi.Data;

namespace CustomApi.Models;

// ---- Requests from CustomUi (or curl/Postman) ----

public record CreateOrderRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Product,
    [Range(0.01, 1_000_000)] decimal Amount,
    ApprovalPattern Pattern = ApprovalPattern.Event,
    OrderCreator CreatedBy = OrderCreator.Customer);

public record DecisionRequest(bool Approved, [StringLength(100)] string? Reviewer, [StringLength(500)] string? Comment);

// ---- Callbacks from the Elsa Server (workflow HTTP Request activities and RunTaskForwarder) ----

/// <summary>PUT /api/orders/{id}/status — sent by the "ReportStatus" activity at the end of each approval workflow.</summary>
public record OrderStatusUpdate([Required] string Status);

/// <summary>POST /api/notifications — sent by the L22_SendNotification child workflow and the audit/listener workflows.</summary>
public record NotificationRequest(
    string? Title, string? Message, string? OrderId, string? Link, string? RejectLink,
    string? Pattern, string? Stage);

/// <summary>POST /api/tasks — sent by ElsaServer's RunTaskForwarder when a workflow reaches "Run Task".</summary>
public record RunTaskRequest(
    [Required] string TaskId, string? TaskName, string? WorkflowInstanceId,
    Dictionary<string, JsonElement>? Payload);

// ---- Responses ----

public record OrderDto(
    string Id, string Product, decimal Amount, string Pattern, string Status,
    string? WorkflowDefinitionId, string? WorkflowInstanceId, string? Error,
    DateTime CreatedAt, DateTime? DecidedAt, string CreatedBy)
{
    public static OrderDto From(Order o) => new(o.Id, o.Product, o.Amount, o.Pattern.ToString(), o.Status,
        o.WorkflowDefinitionId, o.WorkflowInstanceId, o.Error, o.CreatedAt, o.DecidedAt, o.CreatedBy.ToString());
}

public record ApprovalDto(
    int Id, string OrderId, string? Product, decimal? Amount, string Pattern, string? Stage, string? TaskId,
    string? ApproveLink, string? RejectLink, string Status, bool? Approved, string? Reviewer, string? Comment,
    DateTime RequestedAt, DateTime? DecidedAt, string? OrderCreatedBy)
{
    public static ApprovalDto From(Approval a, Order? o) => new(a.Id, a.OrderId, o?.Product, o?.Amount,
        a.Pattern.ToString(), a.Stage, a.TaskId, a.ApproveLink, a.RejectLink, a.Status, a.Approved, a.Reviewer,
        a.Comment, a.RequestedAt, a.DecidedAt, o?.CreatedBy.ToString());
}

/// <summary>
/// A notification plus where its order stands now. IsActionable = its approval is still pending,
/// so a UI may show its links; otherwise the links are history and OrderStatus says how it ended.
/// </summary>
public record NotificationDto(
    int Id, DateTime ReceivedAt, string Title, string Message, string? OrderId, string? Link, string? RejectLink,
    int? ApprovalId, bool IsActionable, string? OrderStatus);
