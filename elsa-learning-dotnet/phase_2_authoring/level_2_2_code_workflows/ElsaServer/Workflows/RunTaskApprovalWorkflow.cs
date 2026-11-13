using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Runtime.Activities;
using ElsaServer.Workflows.Shared;
using static ElsaServer.Workflows.Shared.Designer;

namespace ElsaServer.Workflows;

/// <summary>
/// Run Task: Correlate → Run Task "ReviewOrder" (RunTaskForwarder sends it to CustomApi) → waits until CustomApi
/// calls POST /elsa/api/tasks/{taskId}/complete → status. (ElsaServer reports the final status to CustomApi.)
/// Same Flowchart as L21_RunTaskApproval.json, so Studio draws both identically.
/// </summary>
public class RunTaskApprovalWorkflow : WorkflowBase
{
    public const string DefinitionId = "L22_RunTaskApproval";
    public const string ReviewTask = "ReviewOrder";

    protected override void Build(IWorkflowBuilder builder)
    {
        // Identity: CustomApi starts this workflow by this definition ID (Workflows:RunTask in its appsettings.json).
        builder.WithDefinitionId(DefinitionId);
        builder.Name = DefinitionId;
        builder.Description = "Run Task \"ReviewOrder\": CustomApi completes the task.";
        // Read-only in Studio: the C# class is the source of truth. Without this, Studio's Auto-save would store an
        // editable DRAFT version on top of the code, and Studio would keep showing that draft instead of the code.
        builder.IsReadonly = true;

        // Variables survive suspension when stored with the workflow instance; inputs do not.
        var orderId = builder.WithVariable<string>("OrderId", "").WithWorkflowStorage();   // = correlation ID
        var product = builder.WithVariable<string>("Product", "").WithWorkflowStorage();
        var amount = builder.WithVariable<decimal>("Amount", 0).WithWorkflowStorage();
        var review = builder.WithVariable<object>("Review", null!).WithWorkflowStorage();  // the task result
        var status = builder.WithVariable<string>("Status", "").WithWorkflowStorage();     // approved | rejected

        // ---- The boxes (activity, then .At(id, name, box text, x, y)) ----

        // Step 1 — copy the "Order" input into the variables above.
        var capture = WorkflowParts.CaptureOrder()
            .At("l22rtCapture", "CaptureOrder", "Copy order into variables", -900, 200);

        // Step 2 — correlation ID = order ID.
        var correlate = new Correlate { CorrelationId = new(orderId) }
            .At("l22rtCorrelate", "CorrelateByOrderId", "Correlation ID = OrderId", -680, 200);

        // Step 3 — readable instance name for Studio.
        var setName = new SetName { Value = new(context => $"Order {product.Get(context)} ({amount.Get(context)}) · Run Task approval") }
            .At("l22rtSetName", "NameInstance", "Name the instance", -460, 200);

        // Step 4 — orders over 1000 need a person (True); smaller ones are approved straight away (False).
        var decision = new FlowDecision(context => amount.Get(context) > 1000)
            .At("l22rtDecision", "NeedsApproval", "Amount > 1000?", -240, 200);

        // Step 4 (False) — approve automatically, no task is created.
        var autoApprove = WorkflowParts.Set(status, _ => "approved")
            .At("l22rtAutoApprove", "AutoApprove", "Status = approved (auto)", 300, 360);

        // Step 4a (True) — ask the application to do the task "ReviewOrder", then WAIT.
        // ELSA CONCEPT: Run Task — raises a RunTaskRequest notification and SUSPENDS until the task is reported
        // complete; the result becomes the activity's output. Handlers/RunTaskForwarder sends the request to
        // CustomApi (POST /api/tasks); deciding it calls POST /elsa/api/tasks/{taskId}/complete.
        var task = new RunTask(ReviewTask)
            {
                Payload = new(context => (IDictionary<string, object>?)new Dictionary<string, object>
                {
                    ["OrderId"] = orderId.Get(context) ?? "",
                    ["Product"] = product.Get(context) ?? "",
                    ["Amount"] = amount.Get(context)
                }),
                Result = new(review)   // { Approved, Reviewer, Comment } from CustomApi
            }
            .At("l22rtTask", "ReviewOrderTask", "Run Task \"ReviewOrder\" (waits)", 0, 60);

        // Step 4b — turn the task result into the order status.
        var apply = WorkflowParts.Set(status, context => Wf.IsApproved(review.Get(context)) ? "approved" : "rejected")
            .At("l22rtApply", "ApplyTaskResult", "Status from the task result", 260, 60);

        // Step 5 — expose the result as the workflow's "Status" output.
        var output = WorkflowParts.OutputStatus(status)
            .At("l22rtOutput", "OutputStatus", "Output Status", 560, 200);

        // ---- The arrows ----
        builder.Root = new Flowchart
        {
            Id = "l22rtFlow",
            Activities = { capture, correlate, setName, decision, autoApprove, task, apply, output },
            Connections =
            {
                Flow(capture, correlate),
                Flow(correlate, setName),
                Flow(setName, decision),
                Flow(decision, autoApprove, "False"),
                Flow(decision, task, "True"),
                Flow(task, apply),
                Flow(apply, output),
                Flow(autoApprove, output)
            }
        };
    }
}
