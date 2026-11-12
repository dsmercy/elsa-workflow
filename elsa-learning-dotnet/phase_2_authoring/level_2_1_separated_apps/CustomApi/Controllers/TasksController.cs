using System.Text.Json;
using CustomApi.Data;
using CustomApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomApi.Controllers;

/// <summary>
/// ElsaServer's RunTaskForwarder POSTs here when a workflow reaches "Run Task" ("ReviewOrder").
/// The task becomes a pending approval; deciding it calls POST /elsa/api/tasks/{taskId}/complete.
/// </summary>
[ApiController]
[Route("api/tasks")]
public class TasksController : ControllerBase
{
    public const string ReviewOrderTask = "ReviewOrder";

    private readonly CustomDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TasksController> _logger;

    public TasksController(CustomDbContext db, IConfiguration configuration, ILogger<TasksController> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>DEV ONLY: anonymous — a real system would authenticate the Elsa Server.</summary>
    [HttpPost]
    public async Task<IActionResult> Receive(RunTaskRequest request, CancellationToken ct)
    {
        if (request.TaskName != ReviewOrderTask)
            return Ok(new { ignored = true, reason = $"Only '{ReviewOrderTask}' tasks are handled." });

        // Elsa may deliver the same task twice; keep one approval per task.
        if (await _db.Approvals.AnyAsync(a => a.TaskId == request.TaskId, ct))
            return Ok(new { received = true, duplicate = true });

        string Read(string key) =>
            request.Payload is not null && request.Payload.TryGetValue(key, out var v)
                ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()
                : "";

        var orderId = Read("OrderId");
        if (string.IsNullOrWhiteSpace(orderId))
            return ValidationProblem("The task payload has no OrderId.");

        var approval = new Approval { OrderId = orderId, Pattern = ApprovalPattern.RunTask, TaskId = request.TaskId };
        _db.Approvals.Add(approval);
        await _db.SaveChangesAsync(ct);

        // Run Task doubles as an in-app NOTIFICATION channel.
        _db.Notifications.Add(new Notification
        {
            Title = $"Task \"{ReviewOrderTask}\" requested",
            Message = $"Order {orderId} ({Read("Product")}, {Read("Amount")}) needs a review.",
            OrderId = orderId,
            Link = $"{_configuration["CustomUi:BaseUrl"]?.TrimEnd('/')}/Approvals",
            ApprovalId = approval.Id
        });
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Run Task {TaskId} for order {OrderId} → pending approval {ApprovalId}", request.TaskId, orderId, approval.Id);
        return Ok(new { received = true, approvalId = approval.Id });
    }
}
