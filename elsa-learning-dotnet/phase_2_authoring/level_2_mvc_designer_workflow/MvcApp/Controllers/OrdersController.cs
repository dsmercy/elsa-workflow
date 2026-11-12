using Elsa.Common.Models;
using Elsa.Workflows;
using Elsa.Workflows.Management;
using Elsa.Workflows.Models;
using Elsa.Workflows.Runtime;
using Elsa.Workflows.Runtime.Messages;
using Microsoft.AspNetCore.Mvc;
using MvcApp.Models;
using MvcApp.Services;

namespace MvcApp.Controllers;

public class OrdersController : Controller
{
    // Names shared with the Studio definitions — they must match exactly (case included).
    private const string OrderInputName = "Order";
    private const string StatusOutputName = "Status";

    private readonly IWorkflowRuntime _workflowRuntime;
    private readonly IWorkflowInstanceStore _workflowInstanceStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrdersController> _logger;

    // ELSA CONCEPT: IWorkflowRuntime starts MANAGED instances of stored definitions: the instance is
    // persisted (visible in Studio → Workflow Instances), unlike Phase 1's in-memory IWorkflowRunner.
    public OrdersController(
        IWorkflowRuntime workflowRuntime,
        IWorkflowInstanceStore workflowInstanceStore,
        IConfiguration configuration,
        ILogger<OrdersController> logger)
    {
        _workflowRuntime = workflowRuntime;
        _workflowInstanceStore = workflowInstanceStore;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateOrderViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateOrderViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(model);

        // ELSA CONCEPT: the DEFINITION ID tells the runtime which blueprint to start. It is copied from
        // Studio after publishing — it is not the workflow's name and not an instance ID.
        var pattern = model.Form.Pattern;
        var configKey = ApprovalPatterns.ConfigKey(pattern);
        var definitionId = _configuration[configKey];
        if (string.IsNullOrWhiteSpace(definitionId) || definitionId.StartsWith('<'))
        {
            _logger.LogError("{ConfigKey} is not configured", configKey);
            model.Error = $"The workflow is not configured yet: set {configKey} in appsettings.json (see README).";
            return View(model);
        }

        var order = new Order(
            OrderId: Guid.NewGuid().ToString(),
            CustomerId: Guid.NewGuid().ToString(),
            Product: model.Form.Product,
            Amount: model.Form.Amount,
            Status: "created");

        try
        {
            var client = await _workflowRuntime.CreateClientAsync(cancellationToken);

            var response = await client.CreateAndRunInstanceAsync(new CreateAndRunWorkflowInstanceRequest
            {
                // ELSA CONCEPT: only the PUBLISHED version runs. A saved-but-unpublished draft is ignored.
                WorkflowDefinitionHandle = WorkflowDefinitionHandle.ByDefinitionId(definitionId, VersionOptions.Published),
                // ELSA CONCEPT: workflow INPUT. The key must match the input name on Studio's Input/Output tab.
                Input = new Dictionary<string, object> { [OrderInputName] = order },
                // Ask the runtime to return the workflow OUTPUT values with the response.
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

            // ELSA CONCEPT: workflow OUTPUT — set inside the workflow with "Set Output", read back here by name.
            // An approval workflow that is WAITING (SubStatus = Suspended) has no Status output yet.
            object? status = null;
            response.Output?.TryGetValue(StatusOutputName, out status);

            _logger.LogInformation(
                "Workflow {DefinitionId} instance {WorkflowInstanceId} ended {Status}/{SubStatus}; order {OrderId} → {OrderStatus}",
                definitionId, response.WorkflowInstanceId, response.Status, response.SubStatus, order.OrderId, status);

            model.Result = new OrderResult
            {
                Order = order with { Status = status?.ToString() ?? order.Status },
                Pattern = pattern,
                WorkflowInstanceId = response.WorkflowInstanceId,
                WorkflowStatus = response.Status.ToString(),
                WorkflowSubStatus = response.SubStatus.ToString(),
                OrderStatus = status?.ToString()
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
            // Typical cause: the definition ID doesn't exist or has no published version.
            _logger.LogError(ex, "Starting workflow {DefinitionId} failed for order {OrderId}", definitionId, order.OrderId);
            model.Error = $"Could not start workflow '{definitionId}'. Is it published, and is the definition ID correct?";
            return View(model);
        }
    }

    /// <summary>All orders started from this app, read from Elsa's instance store.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var rows = await OrderInstances.LoadAsync(_workflowInstanceStore, _configuration, cancellationToken);
            return View(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading workflow instances failed");
            ViewBag.Error = "Could not load the orders. Check the server log.";
            return View(new List<OrderInstanceRow>());
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(model: HttpContext.TraceIdentifier);
}
