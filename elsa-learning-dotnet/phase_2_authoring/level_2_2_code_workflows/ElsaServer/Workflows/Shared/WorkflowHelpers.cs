using System.Text.Json;
using Elsa.Expressions.Models;
using Elsa.Extensions;
using Elsa.Workflows;

namespace ElsaServer.Workflows.Shared;

/// <summary>The "Order" input CustomApi sends with POST /elsa/api/workflow-definitions/{id}/execute.</summary>
public record OrderInput(string OrderId, string Product, decimal Amount, string? CreatedBy, bool? RequireManagerApproval);

/// <summary>The decision CustomApi sends when it resumes an Event or completes a Run Task.</summary>
public record ReviewDecision(bool Approved, string? Reviewer, string? Comment);

/// <summary>
/// Small helpers shared by the C# workflows. In level 2.1 the same logic was JavaScript inside the JSON;
/// here it is ordinary, type-checked C#.
/// </summary>
public static class Wf
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Inputs and event payloads arrive from the REST API as dictionaries / JsonElements, not as our records.
    /// A JSON round trip turns any of those shapes into the typed record.
    /// </summary>
    public static T? ConvertTo<T>(object? value) => value switch
    {
        null => default,
        T typed => typed,
        JsonElement element => element.Deserialize<T>(Json),
        _ => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value), Json)
    };

    public static OrderInput Order(ActivityExecutionContext context) => Order(context.WorkflowExecutionContext.Input);

    /// <summary>Same, from an expression (e.g. a Flow Decision's condition).</summary>
    public static OrderInput Order(ExpressionExecutionContext context) => Order(context.GetWorkflowExecutionContext().Input);

    private static OrderInput Order(IDictionary<string, object> input) =>
        ConvertTo<OrderInput>(input.TryGetValue("Order", out var order) ? order : null)
        ?? throw new InvalidOperationException("The workflow needs an \"Order\" input.");

    /// <summary>A plain string input of the current workflow (used by the child workflows).</summary>
    public static string Input(ExpressionExecutionContext context, string name) =>
        context.GetWorkflowExecutionContext().Input.TryGetValue(name, out var value) ? Convert.ToString(value) ?? "" : "";

    public static bool IsApproved(object? review) => ConvertTo<ReviewDecision>(review)?.Approved == true;

    // ---- URLs come from ElsaServer's configuration, not from literals inside the workflow. ----

    private static string Setting(ExpressionExecutionContext context, string key) =>
        context.GetRequiredService<IConfiguration>()[key] ?? throw new InvalidOperationException($"{key} is required.");

    public static string CustomApiUrl(ExpressionExecutionContext context, string path) =>
        $"{Setting(context, "CustomApi:BaseUrl").TrimEnd('/')}/{path.TrimStart('/')}";

    public static string CustomUiUrl(ExpressionExecutionContext context, string path) =>
        $"{Setting(context, "CustomUi:BaseUrl").TrimEnd('/')}/{path.TrimStart('/')}";

    /// <summary>A URL served by an HTTP Endpoint activity on this server (Http:BaseUrl + Http:BasePath).</summary>
    public static string HttpWorkflowUrl(ExpressionExecutionContext context, string path) =>
        $"{Setting(context, "Http:BaseUrl").TrimEnd('/')}{Setting(context, "Http:BasePath")}/{path.TrimStart('/')}";
}
