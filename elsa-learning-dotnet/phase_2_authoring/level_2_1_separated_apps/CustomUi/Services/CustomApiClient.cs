using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CustomUi.Models;

namespace CustomUi.Services;

/// <summary>The only way CustomUi gets data: CustomApi's REST endpoints.</summary>
public class CustomApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;

    public CustomApiClient(HttpClient http) => _http = http;

    public async Task<List<OrderDto>> GetOrdersAsync(CancellationToken ct) =>
        await _http.GetFromJsonAsync<List<OrderDto>>("api/orders", Json, ct) ?? [];

    /// <summary>Returns the created order, or an error message. A failed workflow start still returns the saved order.</summary>
    public async Task<(OrderDto? Order, string? Error)> CreateOrderAsync(OrderForm form, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("api/orders",
            new { form.Product, form.Amount, form.Pattern, form.CreatedBy }, Json, ct);
        if (response.IsSuccessStatusCode || (int)response.StatusCode == StatusCodes.Status502BadGateway)
        {
            var order = await response.Content.ReadFromJsonAsync<OrderDto>(Json, ct);
            return (order, order?.Error);
        }
        return (null, await ErrorAsync(response, ct));
    }

    public async Task<List<ApprovalDto>> GetPendingApprovalsAsync(CancellationToken ct) =>
        await _http.GetFromJsonAsync<List<ApprovalDto>>("api/approvals?status=pending", Json, ct) ?? [];

    public async Task<string?> DecideAsync(int approvalId, bool approved, string? reviewer, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync($"api/approvals/{approvalId}/decision",
            new { approved, reviewer }, Json, ct);
        return response.IsSuccessStatusCode ? null : await ErrorAsync(response, ct);
    }

    public async Task<List<NotificationDto>> GetNotificationsAsync(CancellationToken ct) =>
        await _http.GetFromJsonAsync<List<NotificationDto>>("api/notifications", Json, ct) ?? [];

    /// <summary>CustomApi answers errors as { "error": "..." } or a validation problem; show something readable.</summary>
    private static async Task<string> ErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("error", out var e)) return e.GetString() ?? "Request failed.";
            if (doc.RootElement.TryGetProperty("errors", out var errors))
                return string.Join(" ", errors.EnumerateObject().SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString())));
            if (doc.RootElement.TryGetProperty("title", out var t)) return t.GetString() ?? "Request failed.";
        }
        catch (JsonException) { }
        return $"CustomApi returned HTTP {(int)response.StatusCode}.";
    }
}
