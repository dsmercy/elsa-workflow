using System.Net;
using Elsa.Email.Activities;
using Elsa.Expressions.Models;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using ElsaServer.Workflows.Shared;
using static ElsaServer.Workflows.Shared.Designer;

namespace ElsaServer.Workflows;

/// <summary>
/// Child workflow, started by the approval workflows with Execute Workflow.
/// Inputs: Title, Message, OrderId, ApproveLink, RejectLink, Pattern, Stage.
/// Sends one email (Send Email) and posts the same notification to CustomApi (HTTP Request). When Pattern is
/// set, CustomApi also records a pending approval.
/// Same Flowchart as L21_SendNotification.json, so Studio draws both identically.
/// </summary>
public class SendNotificationWorkflow : WorkflowBase
{
    public const string ApproverEmail = "manager@l22.local";

    protected override void Build(IWorkflowBuilder builder)
    {
        // Identity: the parent workflows run this child by this definition ID (WorkflowParts.Notify).
        builder.WithDefinitionId(WorkflowParts.SendNotificationId);
        builder.Name = WorkflowParts.SendNotificationId;
        builder.Description = "Child: email the approver and POST the notification to CustomApi.";
        // Read-only in Studio: the C# class is the source of truth. Without this, Studio's Auto-save would store an
        // editable DRAFT version on top of the code, and Studio would keep showing that draft instead of the code.
        builder.IsReadonly = true;

        // No variables: the child never suspends, so it reads its inputs directly (Wf.Input).

        // Step 1 — email the approver: subject = Title, body = Message plus the links (see EmailBody).
        // ELSA CONCEPT: Send Email — SMTP settings (and the default sender) come from "Smtp" in appsettings.json.
        var email = new SendEmail
            {
                To = new([ApproverEmail]),
                Subject = new(context => Wf.Input(context, "Title")),
                Body = new(context => EmailBody(context))
            }
            .At("l22notifyEmail", "SendEmail", "Email the approver", 0, 100);

        // Step 2 — post the same notification to CustomApi (POST /api/notifications), the Teams/Slack stand-in.
        // When Pattern is set, CustomApi also opens a pending approval for the order.
        var webhook = WorkflowParts.PostNotification(context => new
            {
                title = Wf.Input(context, "Title"),
                message = Wf.Input(context, "Message"),
                orderId = Wf.Input(context, "OrderId"),
                link = Wf.Input(context, "ApproveLink"),
                rejectLink = Wf.Input(context, "RejectLink"),
                pattern = Wf.Input(context, "Pattern"),
                stage = Wf.Input(context, "Stage")
            })
            .At("l22notifyWebhook", "PostWebhook", "POST to webhook (Teams/Slack stand-in)", 300, 100);

        // ---- The arrow: email first, then the CustomApi notification; the parent waits for both ----
        builder.Root = new Flowchart
        {
            Id = "l22notifyFlow",
            Activities = { email, webhook },
            Connections = { Flow(email, webhook) }
        };
    }

    /// <summary>
    /// HTML email body: the message, then Approve/Reject links (email-link pattern) or a link to the approvals
    /// page (other patterns). Every value is HTML-encoded.
    /// </summary>
    private static string EmailBody(ExpressionExecutionContext context)
    {
        string E(string name) => WebUtility.HtmlEncode(Wf.Input(context, name));
        var links = Wf.Input(context, "RejectLink") != ""
            ? $"<p><a href=\"{E("ApproveLink")}\">Approve</a> | <a href=\"{E("RejectLink")}\">Reject</a></p>"
            : Wf.Input(context, "ApproveLink") != "" ? $"<p><a href=\"{E("ApproveLink")}\">Open the approvals page</a></p>" : "";
        return $"<p>{E("Message")}</p>{links}<p>Order: {E("OrderId")}</p>";
    }
}
