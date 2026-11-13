using Elsa.Expressions.Models;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.StateMachine.Activities;
using Elsa.Workflows.Activities.StateMachine.Models;
using Elsa.Workflows.Runtime.Activities;
using ElsaServer.Workflows.Shared;
using static ElsaServer.Workflows.Shared.Designer;

namespace ElsaServer.Workflows;

/// <summary>
/// Multi-stage approval: manager → finance, or finance only for orders placed by a company employee.
/// CustomApi decides which (Order.RequireManagerApproval); this workflow only routes on it.
///
/// Same Flowchart and State Machine as L21_MultiStageApproval.json — same states, transitions, IDs and positions —
/// so Studio draws both identically:
///   Routing ──(customer)──► ManagerReview ──[L22.ManagerReviewed]──► ManagerDecided ──(approved)──► FinanceReview
///      └────(employee)─────────────────────────────────────────────────────────────────────────────►─┘
///   FinanceReview ──[L22.FinanceReviewed]──► FinanceDecided ──► Approved | Rejected
///   ManagerDecided ──(rejected)──► Rejected
/// Each state waits for ONE event; the approve/reject decision travels in the event's input and conditional
/// (automatic) transitions route on it.
/// </summary>
public class MultiStageApprovalWorkflow : WorkflowBase
{
    public const string DefinitionId = "L22_MultiStageApproval";
    public const string ManagerReviewedEvent = "L22.ManagerReviewed";
    public const string FinanceReviewedEvent = "L22.FinanceReviewed";

    protected override void Build(IWorkflowBuilder builder)
    {
        // Identity: CustomApi starts this workflow by this definition ID (Workflows:MultiStage in its appsettings.json).
        builder.WithDefinitionId(DefinitionId);
        builder.Name = DefinitionId;
        builder.Description = "Manager → finance; company-employee orders go straight to finance.";
        // Read-only in Studio: the C# class is the source of truth. Without this, Studio's Auto-save would store an
        // editable DRAFT version on top of the code, and Studio would keep showing that draft instead of the code.
        builder.IsReadonly = true;

        // Variables survive suspension when stored with the workflow instance; inputs do not.
        var orderId = builder.WithVariable<string>("OrderId", "").WithWorkflowStorage();   // = correlation ID
        var product = builder.WithVariable<string>("Product", "").WithWorkflowStorage();
        var amount = builder.WithVariable<decimal>("Amount", 0).WithWorkflowStorage();
        var requireManager = builder.WithVariable<bool>("RequireManagerApproval", true).WithWorkflowStorage(); // set by CustomApi's rule
        var review = builder.WithVariable<object>("Review", null!).WithWorkflowStorage();  // the latest stage decision
        var status = builder.WithVariable<string>("Status", "").WithWorkflowStorage();     // approved | rejected

        bool Approved(ExpressionExecutionContext context) => Wf.IsApproved(review.Get(context));

        // ---- The boxes on the outer Flowchart ----

        // Step 1 — copy the "Order" input into the variables above (same Run JavaScript box as level 2.1),
        // plus the routing flag CustomApi computed from "Placed by": Customer → true, CompanyEmployee → false.
        // Missing → ask the manager, to be safe.
        var capture = WorkflowParts.CaptureOrder(
                "\nvariables.RequireManagerApproval = o.RequireManagerApproval !== false;")
            .At("l22msCapture", "CaptureOrder", "Copy order into variables", -700, 200);

        // Step 2 — correlation ID = order ID, so CustomApi's stage events reach THIS instance.
        var correlate = new Correlate { CorrelationId = new(orderId) }
            .At("l22msCorrelate", "CorrelateByOrderId", "Correlation ID = OrderId", -460, 200);

        // Step 3 — the approval stages, as a State Machine (states + transitions below).
        var stages = new StateMachine
            {
                InitialState = "Routing",
                States =
                {
                    // Routing — no work; its two conditional transitions pick the first stage.
                    new StateMachineState { Name = "Routing" },

                    // ManagerReview — on entry: rename the instance and notify the manager (Stage = "Manager"
                    // makes CustomApi open a pending MANAGER approval). Then it waits for its trigger.
                    new StateMachineState
                    {
                        Name = "ManagerReview",
                        Entry = new Sequence
                        {
                            Id = "l22msManagerEntry",
                            Activities =
                            {
                                new SetName { Id = "l22msManagerName", Value = new(context => $"Order {product.Get(context)} ({amount.Get(context)}) · awaiting Manager") },
                                Id("l22msManagerNotify", WorkflowParts.Notify(context => StageNotification(context, "Manager",
                                    $"Stage 1 of 2 — manager approval: {product.Get(context)}",
                                    $"Order {orderId.Get(context)} for {amount.Get(context)} needs the MANAGER's approval.",
                                    orderId.Get(context))))
                            }
                        }
                    },

                    // FinanceReview — on entry: rename and notify finance (Stage = "Finance"). The wording depends
                    // on whether the manager stage ran.
                    new StateMachineState
                    {
                        Name = "FinanceReview",
                        Entry = new Sequence
                        {
                            Id = "l22msFinanceEntry",
                            Activities =
                            {
                                new SetName { Id = "l22msFinanceName", Value = new(context => $"Order {product.Get(context)} ({amount.Get(context)}) · awaiting Finance") },
                                Id("l22msFinanceNotify", WorkflowParts.Notify(context => requireManager.Get(context)
                                    ? StageNotification(context, "Finance",
                                        $"Stage 2 of 2 — finance approval: {product.Get(context)}",
                                        $"The manager approved order {orderId.Get(context)}. FINANCE approval is now needed.",
                                        orderId.Get(context))
                                    : StageNotification(context, "Finance",
                                        $"Finance approval (employee order): {product.Get(context)}",
                                        $"Order {orderId.Get(context)} for {amount.Get(context)} was placed by a company employee, so it goes straight to FINANCE approval.",
                                        orderId.Get(context))))
                            }
                        }
                    },

                    // ManagerDecided / FinanceDecided — no work; conditional transitions route on the decision.
                    new StateMachineState { Name = "ManagerDecided" },
                    new StateMachineState { Name = "FinanceDecided" },

                    // Approved / Rejected — final states: set the Status variable. With no transitions out,
                    // the State Machine completes and the Flowchart continues at Step 4.
                    new StateMachineState { Name = "Approved", Entry = Id("l22msApprovedEntry", WorkflowParts.Set(status, _ => "approved")) },
                    new StateMachineState { Name = "Rejected", Entry = Id("l22msRejectedEntry", WorkflowParts.Set(status, _ => "rejected")) }
                },
                Transitions =
                {
                    // Routing: customer orders go to the manager, employee orders straight to finance.
                    new Transition { Name = "NeedsManager", From = "Routing", To = "ManagerReview",
                        Condition = new(context => requireManager.Get(context)) },
                    new Transition { Name = "SkipManager", From = "Routing", To = "FinanceReview",
                        Condition = new(context => !requireManager.Get(context)) },

                    // ManagerReview WAITS for "L22.ManagerReviewed" (CustomApi triggers it when the manager decides);
                    // the event's input ({ Approved, Reviewer, Comment }) is stored in "review".
                    new Transition { Name = "ManagerReviews", From = "ManagerReview", To = "ManagerDecided",
                        Trigger = new Event(ManagerReviewedEvent) { Id = "l22msTrigManager", Result = new(review) } },
                    new Transition { Name = "ManagerApproved", From = "ManagerDecided", To = "FinanceReview",
                        Condition = new(context => Approved(context)) },
                    new Transition { Name = "ManagerRejected", From = "ManagerDecided", To = "Rejected",
                        Condition = new(context => !Approved(context)) },

                    // FinanceReview WAITS for "L22.FinanceReviewed"; finance has the final word.
                    new Transition { Name = "FinanceReviews", From = "FinanceReview", To = "FinanceDecided",
                        Trigger = new Event(FinanceReviewedEvent) { Id = "l22msTrigFinance", Result = new(review) } },
                    new Transition { Name = "FinanceApproved", From = "FinanceDecided", To = "Approved",
                        Condition = new(context => Approved(context)) },
                    new Transition { Name = "FinanceRejected", From = "FinanceDecided", To = "Rejected",
                        Condition = new(context => !Approved(context)) }
                }
            }
            .At("l22msStateMachine", "ApprovalStages", "Manager → Finance (State Machine)", -200, 200);

        // Step 4 — rename the instance with the result.
        var finalName = new SetName { Value = new(context => $"Order {product.Get(context)} ({amount.Get(context)}) · {status.Get(context)}") }
            .At("l22msFinalName", "NameWithResult", "Rename with the result", 100, 200);

        // Step 5 — expose the result as the workflow's "Status" output.
        var output = WorkflowParts.OutputStatus(status)
            .At("l22msOutput", "OutputStatus", "Output Status", 360, 200);

        // ---- The arrows on the outer Flowchart ----
        builder.Root = new Flowchart
        {
            Id = "l22msFlow",
            Activities = { capture, correlate, stages, finalName, output },
            Connections =
            {
                Flow(capture, correlate),
                Flow(correlate, stages),
                Flow(stages, finalName),
                Flow(finalName, output)
            }
        };
    }

    /// <summary>Give an activity inside a state a fixed ID (it is not on the canvas, so it needs no position).</summary>
    private static T Id<T>(string id, T activity) where T : IActivity
    {
        activity.Id = id;
        return activity;
    }

    /// <summary>Inputs for the L22_SendNotification child: one pending approval for the given stage.</summary>
    private static IDictionary<string, object> StageNotification(
        ExpressionExecutionContext context, string stage, string title, string message, string? orderId) =>
        new Dictionary<string, object>
        {
            ["Title"] = title,
            ["Message"] = message,
            ["OrderId"] = orderId ?? "",
            ["ApproveLink"] = Wf.CustomUiUrl(context, "Approvals"),
            ["RejectLink"] = "",
            ["Pattern"] = "MultiStage",
            ["Stage"] = stage
        };
}
