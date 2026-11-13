using System.Text.Json;
using Elsa.Expressions.Models;
using Elsa.Http;
using Elsa.Expressions.JavaScript.Activities;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Management.Activities.SetOutput;
using Elsa.Workflows.Memory;
using Elsa.Workflows.Runtime.Activities;

namespace ElsaServer.Workflows.Shared;

/// <summary>
/// Reusable activity "parts". In the designer you would copy these boxes between workflows;
/// in C# they are just methods that return configured activities. They create the SAME activity types the
/// level 2.1 JSON uses (Run JavaScript, Execute Workflow, HTTP Request (flow), Set Output …), so Studio draws
/// the same boxes. Callers position them with Designer.At(...).
/// </summary>
public static class WorkflowParts
{
    public const string SendNotificationId = "L22_SendNotification";
    public const string OrderAuditId = "L22_OrderAudit";

    /// <summary>
    /// Copy the "Order" input into the OrderId / Product / Amount variables (inputs are not kept once the
    /// workflow suspends). Same Run JavaScript box and script as level 2.1; JavaScript reads and writes the
    /// C# variables by name.
    /// </summary>
    public static RunJavaScript CaptureOrder(string extraScript = "") => new()
    {
        Script = new("const o = getInput(\"Order\");\nvariables.OrderId = o.OrderId;\nvariables.Product = o.Product;\nvariables.Amount = o.Amount;" + extraScript)
    };

    /// <summary>
    /// ELSA CONCEPT: Execute Workflow — run the L22_SendNotification child and wait for it.
    /// <paramref name="input"/> builds the child's inputs (Title, Message, OrderId, ApproveLink, RejectLink, Pattern, Stage).
    /// </summary>
    public static ExecuteWorkflow Notify(Func<ExpressionExecutionContext, IDictionary<string, object>> input) => new()
    {
        WorkflowDefinitionId = new(SendNotificationId),
        WaitForCompletion = new(true),
        Input = new(context => (IDictionary<string, object>?)input(context))
    };

    /// <summary>ELSA CONCEPT: HTTP Request (flow) — post a notification to CustomApi (POST /api/notifications).</summary>
    public static FlowSendHttpRequest PostNotification(Func<ExpressionExecutionContext, object> body) => new()
    {
        ExpectedStatusCodes = new([200]),
        Url = new(context => new Uri(Wf.CustomApiUrl(context, "api/notifications"))),
        Method = new("POST"),
        ContentType = new("application/json"),
        Content = new(context => (object?)JsonSerializer.Serialize(body(context)))
    };

    /// <summary>ELSA CONCEPT: Dispatch Workflow — start L22_OrderAudit in the background (fire and forget).</summary>
    public static DispatchWorkflow DispatchAudit(Variable<string> orderId, Variable<string> status, string pattern) => new()
    {
        WorkflowDefinitionId = new(OrderAuditId),
        WaitForCompletion = new(false),
        Input = new(context => (IDictionary<string, object>?)new Dictionary<string, object>
        {
            ["OrderId"] = orderId.Get(context) ?? "",
            ["Status"] = status.Get(context) ?? "",
            ["Pattern"] = pattern
        })
    };

    /// <summary>Set Output "Status" from the Status variable.</summary>
    public static SetOutput OutputStatus(Variable<string> status) => new()
    {
        OutputName = new("Status"),
        OutputValue = new(context => (object?)status.Get(context))
    };

    /// <summary>Set Variable box: <paramref name="variable"/> = <paramref name="value"/>.</summary>
    public static SetVariable Set<T>(Variable<T> variable, Func<ExpressionExecutionContext, T> value) => new()
    {
        Variable = variable,
        Value = new(context => (object?)value(context))
    };
}
