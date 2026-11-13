using CustomApi.Data;
using CustomApi.Models;
using CustomApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CustomApi.Controllers;

/// <summary>
/// Orders live in custom.sqlite.db. Creating one starts the chosen approval workflow on the Elsa Server;
/// the workflow reports its final status back with PUT /api/orders/{id}/status.
/// </summary>
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private static readonly HashSet<string> FinalStatuses =
        [OrderStatuses.Approved, OrderStatuses.Rejected, OrderStatuses.NeedsReview];

    private readonly CustomDbContext _db;
    private readonly ElsaClient _elsa;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(CustomDbContext db, ElsaClient elsa, IConfiguration configuration, ILogger<OrdersController> logger)
    {
        _db = db;
        _elsa = elsa;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IEnumerable<OrderDto>> List(CancellationToken ct) =>
        (await _db.Orders.OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync(ct)).Select(OrderDto.From);

    [HttpGet("{id}")]
    public async Task<ActionResult<OrderDto>> Get(string id, CancellationToken ct) =>
        await _db.Orders.FindAsync([id], ct) is { } order ? OrderDto.From(order) : NotFound();

    /// <summary>Save the order, then start its workflow: POST /elsa/api/workflow-definitions/{id}/execute.</summary>
    [HttpPost]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var definitionId = _configuration[$"Workflows:{request.Pattern}"];
        if (string.IsNullOrWhiteSpace(definitionId))
            return ValidationProblem($"No workflow is configured for pattern '{request.Pattern}' (Workflows:{request.Pattern}).");

        var order = new Order
        {
            Product = request.Product.Trim(),
            Amount = request.Amount,
            Pattern = request.Pattern,
            CreatedBy = request.CreatedBy,
            WorkflowDefinitionId = definitionId
        };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct); // saved first, so the workflow's callbacks can find it

        try
        {
            // ELSA CONCEPT: the order ID is the CORRELATION ID — every later event, task and link uses it
            // to find this instance. The workflow reads getInput("Order").
            // The BUSINESS RULE is applied here, not in the workflow: RequireManagerApproval tells the
            // multi-stage workflow whether to start at the manager or go straight to finance.
            var result = await _elsa.ExecuteAsync(definitionId, order.Id,
                new
                {
                    Order = new
                    {
                        OrderId = order.Id,
                        order.Product,
                        order.Amount,
                        CreatedBy = order.CreatedBy.ToString(),
                        RequireManagerApproval = ApprovalRules.RequiresManagerApproval(order.CreatedBy)
                    }
                }, ct);

            // The workflow may already have called PUT /status while we waited; read the latest row.
            await _db.Entry(order).ReloadAsync(ct);
            order.WorkflowInstanceId = result.InstanceId;
            if (!FinalStatuses.Contains(order.Status))
            {
                order.Status = result switch
                {
                    { StatusOutput: { Length: > 0 } s } => s,
                    { SubStatus: "Suspended" } => OrderStatuses.Waiting,
                    { SubStatus: "Faulted" } => OrderStatuses.Failed,
                    _ => OrderStatuses.Running
                };
                if (FinalStatuses.Contains(order.Status)) order.DecidedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Order {OrderId} started {DefinitionId} → instance {InstanceId}, {Status}",
                order.Id, definitionId, result.InstanceId, order.Status);
            return CreatedAtAction(nameof(Get), new { id = order.Id }, OrderDto.From(order));
        }
        catch (Exception ex) when (ex is ElsaApiException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Starting workflow {DefinitionId} for order {OrderId} failed", definitionId, order.Id);
            order.Status = OrderStatuses.Failed;
            order.Error = ex is ElsaApiException ? ex.Message : "The Elsa Server is not reachable.";
            await _db.SaveChangesAsync(CancellationToken.None);
            return StatusCode(StatusCodes.Status502BadGateway, OrderDto.From(order));
        }
    }

    /// <summary>
    /// Callback from the workflow's "ReportStatus" (HTTP Request) activity.
    /// DEV ONLY: anonymous — a real system would require a shared secret or signature.
    /// </summary>
    [HttpPut("{id}/status")]
    public async Task<ActionResult<OrderDto>> UpdateStatus(string id, OrderStatusUpdate update, CancellationToken ct)
    {
        var status = update.Status.Trim().ToLowerInvariant();
        if (!FinalStatuses.Contains(status))
            return ValidationProblem($"Status must be one of: {string.Join(", ", FinalStatuses)}.");

        var order = await _db.Orders.FindAsync([id], ct);
        if (order is null) return NotFound();

        order.Status = status;
        order.DecidedAt = DateTime.UtcNow;

        // The order is finished, so nothing is pending for it any more (e.g. the email link was clicked
        // directly at the Elsa Server, bypassing CustomApi).
        await _db.Approvals
            .Where(a => a.OrderId == id && a.Status == ApprovalStatuses.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ApprovalStatuses.Closed), ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Order {OrderId} reported {Status} by its workflow", id, status);
        return OrderDto.From(order);
    }
}
