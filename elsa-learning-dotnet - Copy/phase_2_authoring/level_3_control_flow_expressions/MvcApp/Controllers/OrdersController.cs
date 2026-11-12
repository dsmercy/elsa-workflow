using System.Globalization;
using Elsa.Common.Models;
using Elsa.Workflows;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Microsoft.AspNetCore.Mvc;
using MvcApp.Models;

namespace MvcApp.Controllers;

public class OrdersController : Controller
{
    // Names shared with the Studio definition — they must match exactly (case included).
    private const string OrderInputName = "Order";
    private const string TotalOutputName = "Total";
    private const string StatusOutputName = "Status";
    private const string TierOutputName = "Tier";
    private const string MessageOutputName = "Message";

    private readonly IWorkflowRuntime _workflowRuntime;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IWorkflowRuntime workflowRuntime, IConfiguration configuration, ILogger<OrdersController> logger)
    {
        _workflowRuntime = workflowRuntime;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateOrderViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateOrderViewModel model, CancellationToken cancellationToken)
    {
        var lines = model.Form.Lines
            .Where(l => !l.IsBlank)
            .Select(l => new OrderLine(l.Product!.Trim(), l.Quantity, l.UnitPrice))
            .ToList();

        if (lines.Count == 0)
            ModelState.AddModelError(string.Empty, "Enter at least one order line.");
        if (lines.Any(l => l.Quantity <= 0))
            ModelState.AddModelError(string.Empty, "Every line with a product needs a quantity above 0.");
        if (!ModelState.IsValid)
            return View(model);

        var definitionId = _configuration["Workflow:DefinitionId"];
        if (string.IsNullOrWhiteSpace(definitionId) || definitionId.StartsWith('<'))
        {
            _logger.LogError("Workflow:DefinitionId is not configured");
            model.Error = "The workflow is not configured yet: set Workflow:DefinitionId in appsettings.json (see README).";
            return View(model);
        }

        var order = new Order(
            OrderId: Guid.NewGuid().ToString(),
            CustomerId: Guid.NewGuid().ToString(),
            Product: string.Join(", ", lines.Select(l => l.Product)),
            Amount: 0m, // computed by the workflow (ForEach over Lines)
            Status: "created",
            Lines: lines);

        try
        {
            var client = await _workflowRuntime.CreateClientAsync(cancellationToken);

            var response = await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
            {
                WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Published),
                // ELSA CONCEPT: one workflow input holding the whole order. Inside the workflow,
                // JavaScript reads it with getInput("Order") and Liquid with {{ Input.Order... }}.
                Input = new Dictionary<string, object> { [OrderInputName] = order },
                IncludeWorkflowOutput = true
            }, cancellationToken);

            if (response.SubStatus == WorkflowSubStatus.Faulted)
            {
                _logger.LogError(
                    "Workflow {DefinitionId} instance {WorkflowInstanceId} faulted for order {OrderId}. Incidents: {Incidents}",
                    definitionId, response.WorkflowInstanceId, order.OrderId,
                    string.Join(" | ", response.Incidents.Select(i => $"{i.ActivityId}: {i.Message}")));
                model.Error = $"The workflow faulted (instance {response.WorkflowInstanceId}). Open it in Studio → Workflow Instances to see the incident.";
                return View(model);
            }

            // ELSA CONCEPT: several named workflow OUTPUTS come back in one dictionary.
            var output = response.Output ?? new Dictionary<string, object>();
            var total = ReadDecimal(output, TotalOutputName);
            var status = ReadString(output, StatusOutputName);

            _logger.LogInformation(
                "Workflow {DefinitionId} instance {WorkflowInstanceId} ended {Status}/{SubStatus}; order {OrderId} total {Total} → {OrderStatus}",
                definitionId, response.WorkflowInstanceId, response.Status, response.SubStatus, order.OrderId, total, status);

            model.Result = new OrderResult
            {
                Order = order with { Amount = total ?? order.Amount, Status = status ?? order.Status },
                WorkflowInstanceId = response.WorkflowInstanceId,
                WorkflowStatus = response.Status.ToString(),
                WorkflowSubStatus = response.SubStatus.ToString(),
                Total = total,
                OrderStatus = status,
                Tier = ReadString(output, TierOutputName),
                Message = ReadString(output, MessageOutputName)
            };
            return View(model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Request cancelled while starting workflow {DefinitionId} for order {OrderId}", definitionId, order.OrderId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Starting workflow {DefinitionId} failed for order {OrderId}", definitionId, order.OrderId);
            model.Error = $"Could not start workflow '{definitionId}'. Is it published, and is the definition ID correct?";
            return View(model);
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(model: HttpContext.TraceIdentifier);

    private static string? ReadString(IDictionary<string, object> output, string name) =>
        output.TryGetValue(name, out var value) ? value?.ToString() : null;

    // JavaScript arithmetic produces a double, so the Total output arrives as a number — convert it back to decimal.
    private static decimal? ReadDecimal(IDictionary<string, object> output, string name) =>
        output.TryGetValue(name, out var value) && value is not null
            ? Math.Round(Convert.ToDecimal(value, CultureInfo.InvariantCulture), 2)
            : null;
}
