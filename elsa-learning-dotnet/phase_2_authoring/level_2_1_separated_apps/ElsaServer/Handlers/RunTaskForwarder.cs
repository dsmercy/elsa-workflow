using System.Net.Http.Json;
using Elsa.Mediator.Contracts;
using Elsa.Workflows.Runtime.Notifications;

namespace ElsaServer.Handlers;

/// <summary>
/// ELSA CONCEPT: when a workflow reaches "Run Task", Elsa publishes a RunTaskRequest notification in this
/// process and the workflow SUSPENDS. In level 2 the MVC app handled it in-process; here the inbox lives
/// in CustomApi, so this handler forwards the request over HTTP (POST {CustomApi}/api/tasks).
/// CustomApi later resumes the workflow with POST /elsa/api/tasks/{taskId}/complete.
/// (Elsa.Webhooks would do this generically, but it has no 3.9.0 release.)
/// </summary>
public class RunTaskForwarder : INotificationHandler<RunTaskRequest>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RunTaskForwarder> _logger;

    public RunTaskForwarder(IHttpClientFactory httpClientFactory, ILogger<RunTaskForwarder> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task HandleAsync(RunTaskRequest notification, CancellationToken cancellationToken)
    {
        var workflowInstanceId = notification.ActivityExecutionContext.WorkflowExecutionContext.Id;
        var body = new
        {
            notification.TaskId,
            notification.TaskName,
            WorkflowInstanceId = workflowInstanceId,
            Payload = notification.TaskPayload
        };

        try
        {
            var client = _httpClientFactory.CreateClient("CustomApi");
            using var response = await client.PostAsJsonAsync("api/tasks", body, cancellationToken);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("Run Task {TaskName} {TaskId} forwarded to CustomApi (instance {WorkflowInstanceId})",
                notification.TaskName, notification.TaskId, workflowInstanceId);
        }
        catch (Exception ex)
        {
            // Rethrown so the failure shows up as an incident on the instance in Studio,
            // instead of a workflow that waits for a task nobody can see.
            _logger.LogError(ex, "Forwarding Run Task {TaskName} {TaskId} to CustomApi failed", notification.TaskName, notification.TaskId);
            throw;
        }
    }
}
