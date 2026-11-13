using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Runtime.Activities;
using ElsaServer.Workflows.Shared;
using static ElsaServer.Workflows.Shared.Designer;

namespace ElsaServer.Workflows;

/// <summary>
/// ELSA CONCEPT: an Event activity with CanStartWorkflow = true is a TRIGGER. Whenever any workflow publishes
/// "OrderDecided" (Publish Event), Elsa starts a new instance of this one.
/// Same Flowchart as L21_OrderDecidedListener.json, so Studio draws both identically.
/// </summary>
public class OrderDecidedListenerWorkflow : WorkflowBase
{
    public const string DefinitionId = "L22_OrderDecidedListener";

    protected override void Build(IWorkflowBuilder builder)
    {
        // Identity: nobody starts this workflow by ID — the "OrderDecided" event does.
        builder.WithDefinitionId(DefinitionId);
        builder.Name = DefinitionId;
        builder.Description = "Started by Publish Event \"OrderDecided\"; forwards it to CustomApi.";
        // Read-only in Studio: the C# class is the source of truth. Without this, Studio's Auto-save would store an
        // editable DRAFT version on top of the code, and Studio would keep showing that draft instead of the code.
        builder.IsReadonly = true;

        var decision = builder.WithVariable<object>("Decision", null!).WithWorkflowStorage();   // { OrderId, Status, Pattern }

        // Step 1 — the TRIGGER: a new instance starts each time a workflow publishes "OrderDecided"
        // (Step 6 of L22_EventApproval). The published payload is stored in "decision".
        var onDecided = new Event("OrderDecided") { CanStartWorkflow = true, Result = new(decision) }
            .At("l22listenEvent", "OnOrderDecided", "When \"OrderDecided\" is published", 0, 100);

        // Step 2 — forward what was received to CustomApi as a notification (POST /api/notifications).
        var forward = WorkflowParts.PostNotification(context =>
            {
                var payload = decision.Get(context);
                return new
                {
                    title = "Listener: OrderDecided received",
                    message = "Another workflow published OrderDecided: " + JsonSerializer.Serialize(payload),
                    orderId = Wf.ConvertTo<Dictionary<string, object>>(payload) is { } d && d.TryGetValue("OrderId", out var id) ? id?.ToString() : null
                };
            })
            .At("l22listenWebhook", "ForwardToWebhook", "Notify", 300, 100);

        // ---- The arrow ----
        builder.Root = new Flowchart
        {
            Id = "l22listenFlow",
            Activities = { onDecided, forward },
            Connections = { Flow(onDecided, forward) }
        };
    }
}
