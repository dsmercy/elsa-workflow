using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using ElsaServer.Workflows.Shared;

namespace ElsaServer.Workflows;

/// <summary>
/// Child workflow started with Dispatch Workflow (fire and forget) once an order is decided.
/// Inputs: OrderId, Status, Pattern. Records an audit notification in CustomApi.
/// Same Flowchart as L21_OrderAudit.json, so Studio draws both identically.
/// </summary>
public class OrderAuditWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        // Identity: L22_EventApproval dispatches this child by this definition ID (WorkflowParts.DispatchAudit).
        builder.WithDefinitionId(WorkflowParts.OrderAuditId);
        builder.Name = WorkflowParts.OrderAuditId;
        builder.Description = "Child (dispatched): record an audit notification for a decided order.";
        // Read-only in Studio: the C# class is the source of truth. Without this, Studio's Auto-save would store an
        // editable DRAFT version on top of the code, and Studio would keep showing that draft instead of the code.
        builder.IsReadonly = true;

        // Step 1 (the only step) — record the decision as an audit notification in CustomApi
        // (POST /api/notifications). It runs as its own instance in the background; the parent does not wait.
        var record = WorkflowParts.PostNotification(context => new
            {
                title = "Audit (dispatched child workflow)",
                message = $"Order {Wf.Input(context, "OrderId")} was {Wf.Input(context, "Status")} via the {Wf.Input(context, "Pattern")} pattern.",
                orderId = Wf.Input(context, "OrderId")
            })
            .At("l22auditWebhook", "RecordAudit", "Record audit entry", 0, 100);

        builder.Root = new Flowchart
        {
            Id = "l22auditFlow",
            Activities = { record }
        };
    }
}
