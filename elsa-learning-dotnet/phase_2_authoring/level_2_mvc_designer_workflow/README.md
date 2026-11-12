# Phase 2 · Level 2 · Designer workflow started from MVC

## Concept overview

You design workflows in Studio (or import them as JSON) and publish them. The MVC app hosts the Elsa Server and starts a *published* definition by its **definition ID** with `IWorkflowRuntime`, passing the submitted order as input and showing the `Status` output.

This level has two parts:

1. **The core lesson: `L2_OrderApproval`.** A rule with no human step: an `Order` **input**, a **Decision** on `Amount > 1000`, and a `Status` **output**.
2. **Extension: approvals and notifications.** Four approval patterns and five notification channels, built from Elsa's standard activities. Here the workflow **suspends** (saves a bookmark and waits) until a person decides.

> **Course note.** Waiting for people is Phase 4 material and was added here on request. It also adds one package outside the course baseline, `Elsa.Email`. If you only want the Phase 2 basics, choose **Rule** on the form and ignore the rest.

## What's in this folder

```
level_2_mvc_designer_workflow/
├── MvcApp/                                   Elsa Server + MVC (port 5001)
│   ├── Program.cs                            AddElsa(...) incl. UseEmail, UseRealTimeWorkflows
│   ├── Controllers/OrdersController.cs       start a workflow by definition ID; list orders
│   ├── Controllers/ApprovalsController.cs    resume waiting workflows (event / task)
│   ├── Controllers/NotificationsController.cs webhook receiver (Teams/Slack stand-in) + inbox page
│   ├── Handlers/ReviewOrderTaskHandler.cs    receives "Run Task" requests from workflows
│   ├── Services/ApprovalInbox.cs             in-memory notifications + pending tasks (learning only)
│   ├── Services/OrderInstances.cs            reads orders from Elsa's instance store
│   ├── Models/…, Views/…                     Orders, Approvals, Notifications pages
│   ├── appsettings.example.json              committed template
│   └── appsettings.json                      gitignored
├── ElsaStudio/                               Elsa Studio (port 6001)
├── workflows/                                importable definitions, all tested against 3.9.0
│   ├── L2_OrderApproval.json                 core: rule only
│   ├── L2_EventApproval.json                 single approver (Event)
│   ├── L2_EmailLinkApproval.json             approve by email link (HTTP Endpoint)
│   ├── L2_RunTaskApproval.json               approval as an app task (Run Task)
│   ├── L2_MultiStageApproval.json            manager → finance (State Machine)
│   ├── L2_SendNotification.json              child: email + webhook (used by the approvals)
│   ├── L2_OrderAudit.json                    child: started with Dispatch Workflow
│   └── L2_OrderDecidedListener.json          started by Publish Event "OrderDecided"
└── .gitignore
```

| URL | What it is |
|-----|------------|
| `https://localhost:5001/` | Order form (redirects to `/Orders/Create`) |
| `https://localhost:5001/Orders` · `/Approvals` · `/Notifications` | Orders list · approver screen · notification inbox |
| `https://localhost:5001/elsa/api` | **Management API**: Studio's `Backend:Url` |
| `https://localhost:5001/workflows/l2/approvals/decision` | `Http:BasePath` + an HTTP Endpoint: the email approve/reject links |
| `https://localhost:5001/elsa/hubs/workflow-instance` | SignalR hub for Studio's real-time updates |

## Setup

`MvcApp/appsettings.json` already exists with a random signing key and admin password, plus:

```json
"Workflow": {
  "DefinitionId": "L2_OrderApproval",
  "Approvals": { "Event": "L2_EventApproval", "EmailLink": "L2_EmailLinkApproval",
                 "RunTask": "L2_RunTaskApproval", "MultiStage": "L2_MultiStageApproval" }
},
"Smtp": { "Host": "localhost", "Port": 2525, "SecureSocketOptions": "None",
          "RequireCredentials": false, "DefaultSender": "orders@l2.local" }
```

The IDs match the import files. If you build a workflow in Studio by hand, paste its definition ID here.

**Email needs an SMTP server.** For local testing, use a fake one that catches mail instead of sending it, for example [smtp4dev](https://github.com/rnwood/smtp4dev) (`dotnet tool install -g Rnwood.Smtp4dev`, then `smtp4dev --smtpport=2525`; its inbox is at http://localhost:5000) or Papercut SMTP. If nothing listens on port 2525, the approval workflows fault at the *Send Email* step.

```bash
cd MvcApp && dotnet build && cd ..
cd ElsaStudio && dotnet build && cd ..
```

## Run

Stop any other level first, because they share ports.

```bash
smtp4dev --smtpport=2525            # terminal 1 (or Papercut)
cd MvcApp && dotnet run --urls https://localhost:5001       # terminal 2
cd ElsaStudio && dotnet run --urls https://localhost:6001   # terminal 3
```

Log in to https://localhost:6001 with `Identity:Bootstrap:UserName` / `Password` from `MvcApp/appsettings.json`.

**Import the workflows.** In Studio go to **Workflows → Definitions → Import**, import every file in `workflows/`, then open each one and click **Publish**. Import the three helper workflows (`L2_SendNotification`, `L2_OrderAudit`, `L2_OrderDecidedListener`) as well; the approval workflows call them.

---

## Part 1: the core lesson, `L2_OrderApproval`

### Studio steps

1. **Workflows → Definitions → Create workflow**. Name: `L2_OrderApproval`.
2. Click an empty spot of the canvas, then go to **Input/Output**:
   - Inputs: `Order` (Object).
   - Outputs: `Status` (String).

   Names are case-sensitive and must match the controller.
3. **Branching → Decision**. Condition, with syntax **JavaScript**: `getInput("Order").Amount > 1000`.
4. **Composition → Set Output** ×2: `Status` = `needs_review` and `Status` = `approved` (Literal).
5. Connect Decision **True** → *needs_review* and **False** → *approved*. Then **Publish**.
6. Copy the definition ID (from the URL or the Info panel) into `Workflow:DefinitionId`.

### Verify

On the form, choose **Rule only**. 250 → `approved`, 5000 → `needs_review`, 1000 → `approved`. All are `Finished / Finished`.

---

## Part 2: approvals and notifications

### One-time server setup (C#, already done)

| Line in `Program.cs` | Why |
|---|---|
| `.UseEmail(e => e.ConfigureOptions = o => configuration.GetSection("Smtp").Bind(o))` | Adds **Send Email** to Studio's toolbox |
| `.UseRealTimeWorkflows()` + `app.UseWorkflowsSignalRHubs()` | Real-time instance updates in Studio |
| `services.AddNotificationHandlersFrom<Program>()` | Registers `ReviewOrderTaskHandler`, which receives **Run Task** requests |

Everything else, meaning who is notified, the wording, the waiting and the branching, lives in the workflows and can be changed in Studio without recompiling.

### The building blocks

| Activity | Role here |
|---|---|
| **Run JavaScript** | Copies the order into variables first. Inputs aren't persisted, so values needed after a pause must be in variables |
| **Correlate** | Sets the instance's correlation ID to the order ID, which is how a later approval finds the right instance |
| **Set Name** | Gives the instance a readable name in Studio and the Orders page |
| **Execute Workflow** | Runs `L2_SendNotification` and waits for it to finish |
| **Event** | Waits for a named event. The approval's payload becomes the Event's **Result** |
| **HTTP Endpoint** (mid-flow) | Waits for a request to its path. `?correlationId=` picks the instance |
| **Run Task** | Asks the app to perform a task and waits until the app reports it done |
| **State Machine** | Named states with transitions; useful for multi-stage approval |
| **Publish Event** | Broadcasts `OrderDecided`, which starts `L2_OrderDecidedListener` |
| **Dispatch Workflow** | Queues `L2_OrderAudit` to run in the background, fire and forget (*wait for completion* off — see Known issues) |

Notification channels: **Send Email** (SMTP), **HTTP Request** (POST to a webhook, here the app's own `/notifications/webhook`; paste a Teams or Slack incoming-webhook URL into the activity instead), **Run Task** (the app decides how to notify; here an in-app message), **Publish Event** (to other workflows) and **real-time updates** (to Studio).

### Pattern 1: single approver (`L2_EventApproval`)

```
Run JavaScript → Correlate → Set Name → Decision: Amount > 1000?
  ├─ False → Status = approved
  └─ True  → Execute Workflow L2_SendNotification (email + webhook)
             → Event "OrderReviewed"  ⏸ waits (Running / Suspended)
             → Status = Review.Approved ? approved : rejected
→ Set Output Status → Publish Event "OrderDecided" → Dispatch Workflow L2_OrderAudit
```

**Resume:** *Approvals → Approve/Reject* calls `IEventPublisher.PublishAsync("OrderReviewed", correlationId: orderId, payload: new ReviewDecision(...))`.

### Pattern 2: approve by email link (`L2_EmailLinkApproval`)

```
… → Execute Workflow L2_SendNotification with links
      https://localhost:5001/workflows/l2/approvals/decision?correlationId=<orderId>&decision=approve|reject
  → HTTP Endpoint GET l2/approvals/decision   ⏸ waits for the click
  → Status from ?decision=  → Write HTTP Response (thank-you page) → Set Output Status
```

**Resume:** the approver clicks a link in the email. The same links are on the Approvals and Notifications pages. No MVC code is involved; Elsa's HTTP middleware resumes the instance whose correlation ID is in the URL.

> ⚠ The links are **unauthenticated**: anyone holding one can decide that order. That's fine for learning, but production needs signed, expiring links (Elsa has a SAS-token feature for this) or a login.

### Pattern 3: Run Task (`L2_RunTaskApproval`)

```
… → Run Task "ReviewOrder" (payload: OrderId, Product, Amount)  ⏸ waits
  → Status from the task result → Set Output Status
```

**Resume:** `ReviewOrderTaskHandler` receives the `RunTaskRequest`, adds the task to the inbox and posts an in-app notification. *Approvals → Approve/Reject* then calls `ITaskReporter.ReportCompletionAsync(taskId, new ReviewDecision(...))`.

Pending tasks are held **in memory** (`ApprovalInbox`), so restarting the app forgets them while the workflow keeps waiting. A real app would store them in its own database.

### Pattern 4: multi-stage (`L2_MultiStageApproval`)

```
Run JavaScript → Correlate → State Machine:
   ManagerReview  (entry: rename "· awaiting Manager", notify)
      ── Event "L2.ManagerReviewed" ──► ManagerDecided
   ManagerDecided ── Review.Approved ──► FinanceReview   | otherwise ──► Rejected
   FinanceReview  (entry: rename "· awaiting Finance", notify)
      ── Event "L2.FinanceReviewed" ──► FinanceDecided
   FinanceDecided ── Review.Approved ──► Approved        | otherwise ──► Rejected
→ Set Name (with the result) → Set Output Status
```

**Resume:** *Approvals → Manager: Approve / Finance: Reject …* publishes the current stage's event, with the decision in the payload.

The multi-stage pattern always asks both approvers, whatever the amount.

This workflow was written as JSON. I haven't checked how Studio's designer displays a State Machine, so if it looks odd in the designer, edit the JSON and re-import it.

### Verify

All results below were observed with the import files on Elsa 3.9.0 (11 November 2026):

| Pattern | Amount | What you see | After the decision |
|---|---|---|---|
| Rule | 250 / 5000 | `approved` / `needs_review`, Finished | n/a |
| Event | 250 | `approved`, briefly *Suspended* while the audit child runs, then Finished | Notifications: Audit + Listener |
| Event | 5000 | *Running / Suspended*; email and "Approval needed" notification sent | Approve → `approved`, Finished; Audit + Listener notifications |
| Email link | 2500 | *Suspended*; email with Approve/Reject links | Click Approve → thank-you page, `approved`. A second waiting order stays waiting until its own link is clicked |
| Run Task | 9000 | *Suspended*; task on the Approvals page and an in-app notification | Reject → `rejected`, Finished |
| Multi-stage | any | *Suspended*, "· awaiting Manager"; stage 1 notification | Manager approve → "· awaiting Finance" with a stage 2 notification; Finance approve → `approved`. Rejecting at either stage → `rejected` |

In Studio → **Workflows → Instances**, open any of these to see the journal: which activity suspended, when it resumed, and the variables (`Review`, `Status` …).

### Known issues found while testing (Elsa 3.9.0)

- **State Machine inside a Flowchart: avoid two triggers on one state.** The first version had separate *Approved* and *Rejected* events leaving the same state. When one fired, cancelling the other faulted the workflow ("Activity … is not reachable from the flowchart graph"). The fix used here is one event per stage, followed by conditional automatic transitions.
- **Dispatch Workflow with *wait for completion*: one intermittent stuck run.** In 1 of 8 runs (the first after startup), the parent instance kept its earlier saved state and stayed *Suspended*, although the journal shows it ran to the dispatch step. Its saved state still had the `OrderReviewed` bookmark and no Status output, so it kept appearing on the Approvals page as pending. It looks like a race: the audit child finished and resumed the parent while the parent was still saving, and the older state won. `L2_EventApproval` now dispatches the audit with *wait for completion* **off**, since the parent never needed the audit's result. To wait for a child reliably, use **Execute Workflow**, as the notification step does; keep Dispatch Workflow for fire-and-forget work. If an instance does get stuck, cancel it in Studio (Workflow instances → Cancel). Approving it again would repeat the steps after the decision.
- **Package advisories.** `Elsa.Email` brings in `System.Security.Cryptography.Pkcs` 10.0.0, which has known moderate and high advisories (NuGet warnings NU1902/NU1903). Check for an updated package before using this outside local learning.

## Key takeaway

A workflow pauses by creating a **bookmark** (Event, HTTP Endpoint, Run Task, a State Machine trigger) and resumes when a matching **stimulus** arrives for its **correlation ID**. Notifications and approvals are ordinary activities you arrange in the designer. The app only provides the screens and resume calls, written once.

**Docs pointer:** `../docs/concepts.md` → §2 (input, output, definition ID) and §5 (pitfalls) for Part 1. For Part 2, see `../../phase_4_long_running/docs/concepts.md` (bookmarks, correlation, stimuli).
