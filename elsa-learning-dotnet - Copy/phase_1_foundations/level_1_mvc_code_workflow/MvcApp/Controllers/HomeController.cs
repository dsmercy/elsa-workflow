using Elsa.Workflows;
using Elsa.Workflows.Options;
using Microsoft.AspNetCore.Mvc;
using MvcApp.Models;
using MvcApp.Workflows;

namespace MvcApp.Controllers;

public class HomeController : Controller
{
    private readonly IWorkflowRunner _workflowRunner;
    private readonly ILogger<HomeController> _logger;

    // ELSA CONCEPT: IWorkflowRunner runs a workflow right now, in-process, and returns when it
    // finishes (or suspends). Phase 2 switches to IWorkflowRuntime for persisted, managed instances.
    public HomeController(IWorkflowRunner workflowRunner, ILogger<HomeController> logger)
    {
        _workflowRunner = workflowRunner;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index() => View(new HelloPageViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(HelloPageViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(nameof(Index), model);

        var order = new Order(
            OrderId: Guid.NewGuid().ToString(),
            CustomerId: Guid.NewGuid().ToString(),
            Product: model.Form.Product,
            Amount: model.Form.Amount,
            Status: "created");

        try
        {
            // ELSA CONCEPT: workflow input is a name → value dictionary; the name matches WithInput("Order").
            var options = new RunWorkflowOptions
            {
                Input = new Dictionary<string, object> { [HelloWorkflow.OrderInputName] = order }
            };

            // Builds the definition from HelloWorkflow.Build() and executes a new INSTANCE of it.
            var result = await _workflowRunner.RunAsync(new HelloWorkflow(), options, cancellationToken);
            var state = result.WorkflowState;

            // ELSA CONCEPT: Status is Running/Finished; SubStatus says HOW (Finished, Faulted, Suspended, ...).
            if (state.SubStatus == WorkflowSubStatus.Faulted)
            {
                _logger.LogError(
                    "Workflow {WorkflowName} instance {WorkflowInstanceId} faulted for order {OrderId}. Incidents: {IncidentCount}",
                    "L1_Hello", state.Id, order.OrderId, state.Incidents.Count);
                model.Error = "The workflow failed. Check the server log for details.";
                return View(nameof(Index), model);
            }

            state.Output.TryGetValue(HelloWorkflow.MessageOutputName, out var message);

            _logger.LogInformation(
                "Workflow {WorkflowName} instance {WorkflowInstanceId} completed with {Status}/{SubStatus} for order {OrderId}",
                "L1_Hello", state.Id, state.Status, state.SubStatus, order.OrderId);

            model.Result = new HelloRunResult
            {
                Order = order,
                WorkflowInstanceId = state.Id,
                Status = state.Status.ToString(),
                SubStatus = state.SubStatus.ToString(),
                Message = message?.ToString()
            };
            return View(nameof(Index), model);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Request cancelled while running L1_Hello for order {OrderId}", order.OrderId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Running workflow L1_Hello threw for order {OrderId}", order.OrderId);
            model.Error = "Something went wrong while running the workflow. Please try again.";
            return View(nameof(Index), model);
        }
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    // Note: `Activity` would be ambiguous here (Elsa.Workflows.Activity vs System.Diagnostics.Activity),
    // so the request's TraceIdentifier is used as the correlation id shown to the user.
    public IActionResult Error() => View(model: HttpContext.TraceIdentifier);
}
