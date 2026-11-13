using System.Net;
using Elsa.Extensions;
using Elsa.Http;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Activities.Flowchart.Activities;
using ElsaServer.Workflows.Shared;
using static ElsaServer.Workflows.Shared.Designer;

namespace ElsaServer.Workflows;

/// <summary>
/// Approve by email link: Correlate → email Approve/Reject links (Execute Workflow) → HTTP Endpoint waits for the
/// click → thank-you page. (ElsaServer reports the final status to CustomApi.) The link carries ?correlationId= so Elsa resumes the right instance.
/// Same Flowchart as L21_EmailLinkApproval.json, so Studio draws both identically.
/// </summary>
public class EmailLinkApprovalWorkflow : WorkflowBase
{
    public const string DefinitionId = "L22_EmailLinkApproval";
    public const string DecisionPath = "l22/approvals/decision";

    protected override void Build(IWorkflowBuilder builder)
    {
        // Identity: CustomApi starts this workflow by this definition ID (Workflows:EmailLink in its appsettings.json).
        builder.WithDefinitionId(DefinitionId);
        builder.Name = DefinitionId;
        builder.Description = "Approve by email link: HTTP Endpoint waits for the click.";
        // Read-only in Studio: the C# class is the source of truth. Without this, Studio's Auto-save would store an
        // editable DRAFT version on top of the code, and Studio would keep showing that draft instead of the code.
        builder.IsReadonly = true;

        // Variables survive suspension when stored with the workflow instance; inputs do not.
        var orderId = builder.WithVariable<string>("OrderId", "").WithWorkflowStorage();   // = correlation ID
        var product = builder.WithVariable<string>("Product", "").WithWorkflowStorage();
        var amount = builder.WithVariable<decimal>("Amount", 0).WithWorkflowStorage();
        var query = builder.WithVariable<IDictionary<string, object>>("Query", null!).WithWorkflowStorage(); // the clicked link's query string
        var status = builder.WithVariable<string>("Status", "").WithWorkflowStorage();     // approved | rejected

        // ---- The boxes (activity, then .At(id, name, box text, x, y)) ----

        // Step 1 — copy the "Order" input into the variables above.
        var capture = WorkflowParts.CaptureOrder()
            .At("l22elCapture", "CaptureOrder", "Copy order into variables", -900, 200);

        // Step 2 — correlation ID = order ID. The email links carry ?correlationId=<order ID>, which is how Elsa
        // knows WHICH waiting instance a click belongs to.
        var correlate = new Correlate { CorrelationId = new(orderId) }
            .At("l22elCorrelate", "CorrelateByOrderId", "Correlation ID = OrderId", -680, 200);

        // Step 3 — readable instance name for Studio.
        var setName = new SetName { Value = new(context => $"Order {product.Get(context)} ({amount.Get(context)}) · Email-link approval") }
            .At("l22elSetName", "NameInstance", "Name the instance", -460, 200);

        // Step 4 — orders over 1000 need a person (True); smaller ones are approved straight away (False).
        var decision = new FlowDecision(context => amount.Get(context) > 1000)
            .At("l22elDecision", "NeedsApproval", "Amount > 1000?", -240, 200);

        // Step 4 (False) — approve automatically, no email is sent.
        var autoApprove = WorkflowParts.Set(status, _ => "approved")
            .At("l22elAutoApprove", "AutoApprove", "Status = approved (auto)", 560, 360);

        // Step 4a (True) — email Approve/Reject links (L22_SendNotification child). The links point at this server:
        // {Http:BaseUrl}{Http:BasePath}/l22/approvals/decision?correlationId=…&decision=approve|reject.
        // Pattern = "EmailLink" makes CustomApi open a pending approval that can also "click" for you.
        var notify = WorkflowParts.Notify(context =>
            {
                var link = Wf.HttpWorkflowUrl(context, $"{DecisionPath}?correlationId={orderId.Get(context)}&decision=");
                return new Dictionary<string, object>
                {
                    ["Title"] = $"Approve by link: {product.Get(context)}",
                    ["Message"] = $"Order {orderId.Get(context)} for {amount.Get(context)} needs approval. Click a link below.",
                    ["OrderId"] = orderId.Get(context) ?? "",
                    ["ApproveLink"] = link + "approve",
                    ["RejectLink"] = link + "reject",
                    ["Pattern"] = "EmailLink",
                    ["Stage"] = ""
                };
            })
            .At("l22elNotify", "EmailLinks", "Email Approve/Reject links (child workflow)", 0, 60);

        // Step 4b — WAIT for the click.
        // ELSA CONCEPT: HTTP Endpoint (not a trigger here) — suspends until a GET hits the path for THIS
        // instance's correlation ID. The query string is stored in "query".
        var waitClick = new HttpEndpoint
            {
                Path = new(DecisionPath),
                SupportedMethods = new(["GET"]),
                Authorize = new(false),
                CanStartWorkflow = false,
                QueryStringData = new(query)
            }
            .At("l22elWaitClick", "WaitForClick", "Wait for GET /l22/approvals/decision", 260, 60);

        // Step 4c — read ?decision=approve|reject from the clicked link into the order status.
        var applyClick = WorkflowParts.Set(status, context =>
            {
                var q = query.Get(context);
                var clicked = q is not null && q.TryGetValue("decision", out var d) ? Convert.ToString(d) : null;
                return clicked == "approve" ? "approved" : "rejected";
            })
            .At("l22elApplyClick", "ApplyClick", "Status from ?decision=", 520, 60);

        // Step 4d — answer the browser that clicked the link with a thank-you page.
        // ELSA CONCEPT: Write HTTP Response — replies to the request the HTTP Endpoint received.
        var respond = new WriteHttpResponse
            {
                StatusCode = new(HttpStatusCode.OK),
                ContentType = new("text/html"),
                Content = new(context => (object?)ThankYouPage(
                    orderId.Get(context), product.Get(context), status.Get(context), Wf.CustomUiUrl(context, "Orders")))
            }
            .At("l22elRespond", "ThankYouPage", "Show a thank-you page", 780, 60);

        // Step 5 — expose the result as the workflow's "Status" output.
        var output = WorkflowParts.OutputStatus(status)
            .At("l22elOutput", "OutputStatus", "Output Status", 1040, 200);

        // ---- The arrows ----
        builder.Root = new Flowchart
        {
            Id = "l22elFlow",
            Activities = { capture, correlate, setName, decision, autoApprove, notify, waitClick, applyClick, respond, output },
            Connections =
            {
                Flow(capture, correlate),
                Flow(correlate, setName),
                Flow(setName, decision),
                Flow(decision, autoApprove, "False"),
                Flow(decision, notify, "True"),
                Flow(notify, waitClick),
                Flow(waitClick, applyClick),
                Flow(applyClick, respond),
                Flow(respond, output),
                Flow(autoApprove, output)
            }
        };
    }

    /// <summary>HTML shown to the person who clicked the link. Every value is HTML-encoded.</summary>
    private static string ThankYouPage(string? orderId, string? product, string? status, string ordersUrl)
    {
        string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        return "<!DOCTYPE html><html><body style=\"font-family:system-ui;margin:2rem\"><h1>Thank you</h1>" +
               $"<p>Order {E(orderId)} ({E(product)}) is now <strong>{E(status)}</strong>.</p>" +
               $"<p><a href=\"{E(ordersUrl)}\">See all orders</a></p></body></html>";
    }
}
