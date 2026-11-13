using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Management.Activities.SetOutput;
using ElsaServer.Workflows.Shared;
using static ElsaServer.Workflows.Shared.Designer;

namespace ElsaServer.Workflows;

/// <summary>
/// "Rule" pattern — the C# version of L21_OrderApproval: no approver, amount over 1000 → needs_review.
/// ELSA CONCEPT: a workflow written in C# derives from WorkflowBase and builds its activity tree in Build().
/// AddWorkflowsFrom&lt;Program&gt;() registers and publishes it at startup; Studio lists it, and the REST API
/// starts it by its definition ID exactly like a designer workflow.
/// Same Flowchart as L21_OrderApproval.json (Decision + two Set Output boxes), so Studio draws both identically.
/// </summary>
public class OrderApprovalWorkflow : WorkflowBase
{
    public const string DefinitionId = "L22_OrderApproval";

    protected override void Build(IWorkflowBuilder builder)
    {
        // Identity: CustomApi starts this workflow by this definition ID (Workflows:Rule in its appsettings.json).
        builder.WithDefinitionId(DefinitionId);
        builder.Name = DefinitionId;
        builder.Description = "Rule only: Amount > 1000 → needs_review, otherwise approved.";
        // Read-only in Studio: the C# class is the source of truth. Without this, Studio's Auto-save would store an
        // editable DRAFT version on top of the code, and Studio would keep showing that draft instead of the code.
        builder.IsReadonly = true;

        // No variables: nothing waits, so the input can be read directly, and CustomApi gets the result from the
        // execute response in the same call (no status callback is needed).

        // Step 1 — apply the rule to the "Order" input.
        var decision = new FlowDecision(context => Wf.Order(context).Amount > 1000)
            .At("l22decision00001", "AmountAbove1000", "Amount > 1000?", 0, 150);

        // Step 2 (True) — expose "needs_review" as the workflow's "Status" output.
        var needsReview = new SetOutput { OutputName = new("Status"), OutputValue = new("needs_review") }
            .At("l22setneedsrev01", "SetNeedsReview", "Status = needs_review", 350, 50);

        // Step 2 (False) — expose "approved" as the workflow's "Status" output.
        var approved = new SetOutput { OutputName = new("Status"), OutputValue = new("approved") }
            .At("l22setapproved01", "SetApproved", "Status = approved", 350, 250);

        // ---- The arrows ----
        builder.Root = new Flowchart
        {
            Id = "l22flowchart0001",
            Activities = { decision, needsReview, approved },
            Connections =
            {
                Flow(decision, needsReview, "True"),
                Flow(decision, approved, "False")
            }
        };
    }
}
