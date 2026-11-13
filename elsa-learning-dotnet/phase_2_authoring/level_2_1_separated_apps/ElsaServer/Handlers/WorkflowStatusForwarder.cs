using System.Net.Http.Json;
using Elsa.Mediator.Contracts;
using Elsa.Workflows;
using Elsa.Workflows.Notifications;

namespace ElsaServer.Handlers;

/// <summary>
/// ELSA CONCEPT: Elsa publishes a WorkflowFinished notification inside this process whenever a workflow
/// completes. This handler tells CustomApi the order's final status (PUT {CustomApi}/api/orders/{id}/status).
///
/// Doing this here — instead of an "HTTP Request" box at the end of every approval workflow — keeps the level 2.1
/// workflows drawn exactly like level 2's: the separated-apps plumbing lives in the host, not in the diagrams.
/// Which workflows are reported is configured in CustomApi:ReportStatusFor (definition IDs).
/// </summary>
public class WorkflowStatusForwarder : INotificationHandler<WorkflowFinished>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkflowStatusForwarder> _logger;

    public WorkflowStatusForwarder(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<WorkflowStatusForwarder> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task HandleAsync(WorkflowFinished notification, CancellationToken cancellationToken)
    {
        var context = notification.WorkflowExecutionContext;
        var definitionId = context.Workflow.Identity.DefinitionId;

        // Only the approval workflows, only when they really finished (not faulted or cancelled).
        var reported = _configuration.GetSection("CustomApi:ReportStatusFor").Get<string[]>() ?? [];
        if (!reported.Contains(definitionId) || context.SubStatus != WorkflowSubStatus.Finished)
            return;

        // The order ID is the correlation ID; the result is the workflow's "Status" output.
        var orderId = context.CorrelationId;
        var status = context.Output.TryGetValue("Status", out var value) ? Convert.ToString(value) : null;
        if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(status))
        {
            _logger.LogWarning("{DefinitionId} instance {InstanceId} finished without a correlation ID or Status output; not reported",
                definitionId, context.Id);
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("CustomApi");
            using var response = await client.PutAsJsonAsync($"api/orders/{Uri.EscapeDataString(orderId)}/status", new { status }, cancellationToken);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Reported order {OrderId} = {Status} to CustomApi ({DefinitionId})", orderId, status, definitionId);
        }
        catch (Exception ex)
        {
            // Not rethrown: the workflow has already finished. The order stays "waiting" in CustomApi; the log says why.
            _logger.LogError(ex, "Reporting order {OrderId} = {Status} to CustomApi failed", orderId, status);
        }
    }
}
