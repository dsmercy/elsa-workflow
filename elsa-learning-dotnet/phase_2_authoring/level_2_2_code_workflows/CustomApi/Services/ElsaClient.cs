using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CustomApi.Services;

/// <summary>Result of starting a workflow: what Elsa reports right after the first run.</summary>
public record WorkflowRunResult(string InstanceId, string Status, string SubStatus, string? StatusOutput);

/// <summary>
/// ELSA CONCEPT: CustomApi has no Elsa packages — it drives the Elsa Server through its REST API (/elsa/api),
/// exactly like Studio does. It signs in with an Elsa Identity user and sends the bearer token.
///   start   → POST /elsa/api/workflow-definitions/{definitionId}/execute   { correlationId, input }
///   event   → POST /elsa/api/events/{eventName}/trigger                    { correlationId, input, workflowExecutionMode }
///   task    → POST /elsa/api/tasks/{taskId}/complete                       { result }
/// Note: the event trigger reads the payload from "input" — a "payload" field is ignored.
/// </summary>
public class ElsaClient
{
    // Send property names EXACTLY as written. HttpClient's default (camelCase) would turn Order.OrderId into
    // "orderId", and the workflows' JavaScript (getInput("Order").OrderId, variables.Review.Approved) is case-sensitive.
    private static readonly JsonSerializerOptions ExactNames = new() { PropertyNamingPolicy = null };

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ElsaClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTime _tokenRefreshAt;

    public ElsaClient(HttpClient http, IConfiguration configuration, ILogger<ElsaClient> logger)
    {
        _http = http;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<WorkflowRunResult> ExecuteAsync(string definitionId, string correlationId, object input, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"elsa/api/workflow-definitions/{Uri.EscapeDataString(definitionId)}/execute",
            new { correlationId, input }, ct);
        await EnsureSuccessAsync(response, $"start workflow {definitionId}", ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var state = doc.RootElement.GetProperty("workflowState");
        string? statusOutput = null;
        if (state.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Object
            && output.TryGetProperty("Status", out var s) && s.ValueKind == JsonValueKind.String)
            statusOutput = s.GetString();

        return new WorkflowRunResult(
            state.GetProperty("id").GetString() ?? "",
            state.GetProperty("status").GetString() ?? "",
            state.GetProperty("subStatus").GetString() ?? "",
            statusOutput);
    }

    public async Task TriggerEventAsync(string eventName, string correlationId, object input, CancellationToken ct)
    {
        // "Synchronous": Elsa resumes the workflow before answering, so when this returns the workflow has
        // already moved on (e.g. the finance approval exists). The default queues it and returns at once.
        using var response = await SendAsync(HttpMethod.Post,
            $"elsa/api/events/{Uri.EscapeDataString(eventName)}/trigger",
            new { correlationId, input, workflowExecutionMode = "Synchronous" }, ct);
        await EnsureSuccessAsync(response, $"trigger event {eventName}", ct);
    }

    public async Task CompleteTaskAsync(string taskId, object result, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post,
            $"elsa/api/tasks/{Uri.EscapeDataString(taskId)}/complete",
            new { result }, ct);
        await EnsureSuccessAsync(response, $"complete task {taskId}", ct);
    }

    /// <summary>Email-link pattern: "click" the link on the reviewer's behalf (an anonymous GET to the HTTP Endpoint).</summary>
    public async Task FollowLinkAsync(string url, CancellationToken ct)
    {
        var allowedBase = _configuration["ElsaServer:HttpWorkflowsBaseUrl"]
            ?? throw new InvalidOperationException("ElsaServer:HttpWorkflowsBaseUrl is required.");
        // Only follow links that point at our own Elsa Server — never an arbitrary URL from the database.
        if (!url.StartsWith(allowedBase.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The approval link does not point at the configured Elsa Server.");

        using var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, "follow the approval link", ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: ExactNames) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(attempt > 1, ct));
            var response = await _http.SendAsync(request, ct);
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt > 1)
                return response;
            response.Dispose(); // token expired or server restarted with a new key: sign in again once
        }
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && _token is not null && DateTime.UtcNow < _tokenRefreshAt)
                return _token;

            var userName = _configuration["ElsaServer:UserName"] ?? throw new InvalidOperationException("ElsaServer:UserName is required.");
            var password = _configuration["ElsaServer:Password"] ?? throw new InvalidOperationException("ElsaServer:Password is required.");

            using var response = await _http.PostAsJsonAsync("elsa/api/identity/login", new { username = userName, password }, ct);
            await EnsureSuccessAsync(response, "sign in to the Elsa Server", ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            _token = doc.RootElement.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(_token))
                throw new ElsaApiException("Signing in to the Elsa Server failed (check ElsaServer:UserName/Password).");

            // Refresh well before Elsa's default token lifetime ends; a 401 also forces a refresh.
            _tokenRefreshAt = DateTime.UtcNow.AddMinutes(10);
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string what, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning("Elsa Server call failed: {What} → {StatusCode} {Body}", what, (int)response.StatusCode,
            body.Length > 500 ? body[..500] : body);
        throw new ElsaApiException($"Elsa Server could not {what} (HTTP {(int)response.StatusCode}).");
    }
}

public class ElsaApiException(string message) : Exception(message);
