using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Runtime.Activities;
using ElsaServer.Workflows.Shared;
using static ElsaServer.Workflows.Shared.Designer;

namespace ElsaServer.Workflows;

/// <summary>
/// Single approver: Correlate → notify (Execute Workflow) → wait for Event "OrderReviewed" → status →
/// Publish Event "OrderDecided" → Dispatch the audit. (ElsaServer reports the final status to CustomApi.)
/// CustomApi resumes it with POST /elsa/api/events/OrderReviewed/trigger (correlationId = order ID).
///
/// Same Flowchart as L21_EventApproval.json — same boxes, IDs, texts, positions and arrows — so Studio draws
/// both identically. Only the expressions differ: C# lambdas here, JavaScript there.
/// </summary>
public class EventApprovalWorkflow : WorkflowBase
{
    public const string DefinitionId = "L22_EventApproval";
    public const string ReviewedEvent = "OrderReviewed";

    protected override void Build(IWorkflowBuilder builder)
    {
        // Identity: CustomApi starts this workflow by this definition ID (Workflows:Event in its appsettings.json).
        builder.WithDefinitionId(DefinitionId);
        builder.Name = DefinitionId;
        builder.Description = "Single approver: wait for Event \"OrderReviewed\".";
        // Read-only in Studio: the C# class is the source of truth. Without this, Studio's Auto-save would store an
        // editable DRAFT version on top of the code, and Studio would keep showing that draft instead of the code.
        builder.IsReadonly = true;

        // Variables survive suspension when stored with the workflow instance; inputs do not.
        var orderId = builder.WithVariable<string>("OrderId", "").WithWorkflowStorage();   // = correlation ID
        var product = builder.WithVariable<string>("Product", "").WithWorkflowStorage();
        var amount = builder.WithVariable<decimal>("Amount", 0).WithWorkflowStorage();
        var review = builder.WithVariable<object>("Review", null!).WithWorkflowStorage();  // the approver's decision
        var status = builder.WithVariable<string>("Status", "").WithWorkflowStorage();     // approved | rejected

        // ---- The boxes (activity, then .At(id, name, box text, x, y)) ----

        // Step 1 — copy the "Order" input (OrderId, Product, Amount) into the variables above.
        var capture = WorkflowParts.CaptureOrder()
            .At("l22evCapture", "CaptureOrder", "Copy order into variables", -900, 200);

        // Step 2 — correlation ID = order ID: lets CustomApi resume THIS instance by order ID.
        var correlate = new Correlate { CorrelationId = new(orderId) }
            .At("l22evCorrelate", "CorrelateByOrderId", "Correlation ID = OrderId", -680, 200);

        // Step 3 — readable instance name for Studio's instance list.
        var setName = new SetName { Value = new(context => $"Order {product.Get(context)} ({amount.Get(context)}) · Event approval") }
            .At("l22evSetName", "NameInstance", "Name the instance", -460, 200);

        // Step 4 — orders over 1000 need a person (True); smaller ones are approved straight away (False).
        var decision = new FlowDecision(context => amount.Get(context) > 1000)
            .At("l22evDecision", "NeedsApproval", "Amount > 1000?", -240, 200);

        // Step 4 (False) — approve automatically, nobody is asked.
        var autoApprove = WorkflowParts.Set(status, _ => "approved")
            .At("l22evAutoApprove", "AutoApprove", "Status = approved (auto)", 300, 360);

        // Step 4a (True) — notify the approver: runs the L22_SendNotification child (email + CustomApi
        // notification). Pattern = "Event" makes CustomApi open a pending approval.
        var notify = WorkflowParts.Notify(context => new Dictionary<string, object>
            {
                ["Title"] = $"Approval needed: {product.Get(context)}",
                ["Message"] = $"Order {orderId.Get(context)} for {amount.Get(context)} is waiting for your approval.",
                ["OrderId"] = orderId.Get(context) ?? "",
                ["ApproveLink"] = Wf.CustomUiUrl(context, "Approvals"),
                ["RejectLink"] = "",
                ["Pattern"] = "Event",
                ["Stage"] = ""
            })
            .At("l22evNotify", "NotifyApprover", "Notify approver (child workflow)", 0, 60);

        // Step 4b — WAIT for the decision.
        // ELSA CONCEPT: Event — the workflow SUSPENDS here (a bookmark) until "OrderReviewed" arrives for this
        // correlation ID. CustomApi sends it with POST /elsa/api/events/OrderReviewed/trigger; the event's
        // input ({ Approved, Reviewer, Comment }) is stored in "review".
        var wait = new Event(ReviewedEvent) { Result = new(review) }
            .At("l22evWait", "WaitForReview", "Wait for \"OrderReviewed\"", 260, 60);

        // Step 4c — turn the decision into the order status.
        var applyReview = WorkflowParts.Set(status, context => Wf.IsApproved(review.Get(context)) ? "approved" : "rejected")
            .At("l22evApplyReview", "ApplyReview", "Status from the review", 520, 60);

        // Step 5 — expose the result as the workflow's "Status" output (visible in Studio).
        var output = WorkflowParts.OutputStatus(status)
            .At("l22evOutput", "OutputStatus", "Output Status", 780, 200);

        // Step 6 — announce the decision.
        // ELSA CONCEPT: Publish Event — starts L22_OrderDecidedListener (its Event is a trigger).
        var publish = new PublishEvent
            {
                EventName = new("OrderDecided"),
                IsLocalEvent = new(false),
                Payload = new(context => (object)new Dictionary<string, object>
                {
                    ["OrderId"] = orderId.Get(context) ?? "",
                    ["Status"] = status.Get(context) ?? "",
                    ["Pattern"] = "Event"
                })
            }
            .At("l22evPublish", "PublishDecided", "Publish \"OrderDecided\"", 1020, 200);

        // Step 7 — start the L22_OrderAudit child in the background (Dispatch Workflow, no waiting).
        var audit = WorkflowParts.DispatchAudit(orderId, status, "Event")
            .At("l22evAudit", "DispatchAudit", "Dispatch audit (fire and forget)", 1260, 200);

        // ---- The arrows: which box runs after which (port "Done" unless stated) ----
        builder.Root = new Flowchart
        {
            Id = "l22evFlow",
            Activities = { capture, correlate, setName, decision, autoApprove, notify, wait, applyReview, output, publish, audit },
            Connections =
            {
                Flow(capture, correlate),
                Flow(correlate, setName),
                Flow(setName, decision),
                Flow(decision, autoApprove, "False"),
                Flow(decision, notify, "True"),
                Flow(notify, wait),
                Flow(wait, applyReview),
                Flow(applyReview, output),
                Flow(autoApprove, output),
                Flow(output, publish),
                Flow(publish, audit)
            }
        };
    }
}
