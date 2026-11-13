# Phase 2 · Level 2.2 · C# workflows

## Concept overview

Level 2.2 is **level 2.1 with the workflows written in C#** instead of designed in Studio. The four apps, the two
databases, CustomApi's endpoints and the CustomUi pages are the same — and so is **how each workflow looks in
Studio**: every C# workflow builds the same Flowchart as its level 2.1 JSON twin, with the same activity types,
IDs, names, box texts, canvas positions and arrows (the multi-stage one has the same State Machine). Only where the
workflows come from, and the language of the expressions, change:

| | Level 2.1 | Level 2.2 |
|---|---|---|
| Workflow source | `workflows/L21_*.json`, designed in Studio | `ElsaServer/Workflows/*.cs`, classes deriving from `WorkflowBase` |
| How they reach Elsa | you import and publish them in Studio | `AddWorkflowsFrom<Program>()` registers and publishes them when ElsaServer starts |
| Shape in Studio | Flowchart (multi-stage: + State Machine) | the same Flowchart and State Machine, box for box |
| Expressions inside activities | JavaScript and Liquid | C# lambdas, type-checked by the compiler (only "Copy order into variables" stays JavaScript, as in 2.1) |
| URLs the workflows call | hard-coded in the JSON | read from ElsaServer's `appsettings.json` at run time |
| Changing a workflow | edit in Studio, publish | edit the C# class, rebuild, restart ElsaServer |

Choose the C# approach when workflows belong to the application code: they are reviewed and versioned with it,
refactored with the IDE, and can share helpers (see `Workflows/Shared/`). Choose the designer approach when
people outside the development team need to change the process.

> **Course note.** Like levels 2 and 2.1, this uses Phase 4 material (waiting for people) and `Elsa.Email`, added
> on request.

## What's in this folder

```
level_2_2_code_workflows/
├── level_2_2_code_workflows.sln       Visual Studio solution with the four projects
├── level_2_2_code_workflows.slnLaunch startup profile "Level 2.2 - all apps"
├── ElsaServer/                        Elsa engine + REST API (port 5001)
│   ├── Program.cs                     AddElsa(...) + AddWorkflowsFrom<Program>()
│   ├── Handlers/RunTaskForwarder.cs        forwards "Run Task" requests to CustomApi
│   ├── Handlers/WorkflowStatusForwarder.cs reports each finished order's status to CustomApi (WorkflowFinished)
│   └── Workflows/
│       ├── OrderApprovalWorkflow.cs           L22_OrderApproval        rule only
│       ├── EventApprovalWorkflow.cs           L22_EventApproval        single approver (Event)
│       ├── EmailLinkApprovalWorkflow.cs       L22_EmailLinkApproval    approve by email link (HTTP Endpoint)
│       ├── RunTaskApprovalWorkflow.cs         L22_RunTaskApproval      Run Task "ReviewOrder"
│       ├── MultiStageApprovalWorkflow.cs      L22_MultiStageApproval   manager → finance
│       ├── SendNotificationWorkflow.cs        L22_SendNotification     child: email + CustomApi notification
│       ├── OrderAuditWorkflow.cs              L22_OrderAudit           child: started with Dispatch Workflow
│       ├── OrderDecidedListenerWorkflow.cs    L22_OrderDecidedListener started by Publish Event "OrderDecided"
│       └── Shared/                            Designer (IDs, box texts, positions, arrows), helpers, reusable activity parts
├── ElsaStudio/                        Elsa Studio (port 6001)
├── CustomApi/                         business REST API (port 5101), same as level 2.1 but L22_* IDs and L22.* events
└── CustomUi/                          MVC pages (port 5201), same as level 2.1
```

There is no `workflows/` folder: nothing needs importing.

## How a C# workflow is built

As in level 2.1, the workflows contain only the business process, so they are drawn exactly like level 2's. The
separated-apps plumbing lives in ElsaServer: `RunTaskForwarder` passes Run Task requests to CustomApi, and
`WorkflowStatusForwarder` reports each approval workflow's final `Status` output to CustomApi when it finishes
(the workflows listed in `CustomApi:ReportStatusFor`). The only difference from level 2 is the **Routing** state
inside the multi-stage State Machine ("Placed by" rule).

Each workflow is written in two parts, mirroring the JSON: first the **boxes**, then the **arrows**.

```csharp
public class EventApprovalWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WithDefinitionId("L22_EventApproval");          // the ID CustomApi starts it by

        // Inputs are gone once the workflow suspends; variables stored with the instance are not.
        var orderId = builder.WithVariable<string>("OrderId", "").WithWorkflowStorage();
        var review  = builder.WithVariable<object>("Review", null!).WithWorkflowStorage();
        ...
        // Boxes: activity + .At(id, name, box text, x, y) — the same values as in L21_EventApproval.json
        var decision = new FlowDecision(context => amount.Get(context) > 1000)
            .At("l22evDecision", "NeedsApproval", "Amount > 1000?", -240, 200);
        var wait = new Event("OrderReviewed") { Result = new(review) }          // suspends here
            .At("l22evWait", "WaitForReview", "Wait for \"OrderReviewed\"", 260, 60);
        var applyReview = WorkflowParts.Set(status, context => Wf.IsApproved(review.Get(context)) ? "approved" : "rejected")
            .At("l22evApplyReview", "ApplyReview", "Status from the review", 520, 60);
        ...
        // Arrows: from a box's outcome port ("Done", "True", "False") to the next box
        builder.Root = new Flowchart
        {
            Id = "l22evFlow",
            Activities = { capture, correlate, setName, decision, autoApprove, notify, wait, applyReview, ... },
            Connections =
            {
                Flow(capture, correlate),
                Flow(decision, autoApprove, "False"),
                Flow(decision, notify, "True"),
                Flow(notify, wait),
                ...
            }
        };
    }
}
```

Things to know when writing C# workflows this way:

- **`Designer.At(...)` is what makes Studio draw it like the JSON.** It sets the activity's ID, name,
  `metadata.displayText` and `metadata.designer.position`. Without it Studio still runs and shows the workflow,
  but with generated IDs, generic box labels and its default layout. Fixed IDs also matter at run time: a waiting
  instance remembers the ID of the activity it waits on.
- **Use the flow versions of activities inside a Flowchart**: `FlowDecision` (not `If`), `FlowSendHttpRequest`
  (not `SendHttpRequest`), so they have the outcome ports the arrows connect to.
- **Inputs and event payloads arrive as dictionaries or `JsonElement`s**, not as your records. `Wf.ConvertTo<T>`
  turns them into `OrderInput` / `ReviewDecision` with a JSON round trip.
- **The State Machine in C#** uses `StateMachineState { Name = …, Entry = … }` and
  `Transition { Name, From, To, Trigger = new Event(…), Condition = new(context => …) }`.
- **Activities are plain objects**, so the repeated pieces (notify, post notification, dispatch audit, set variable)
  are methods in `Workflows/Shared/WorkflowParts.cs` instead of copied boxes.

## Setup

Same as level 2.1. The real `appsettings.json` files are gitignored and were copied from level 2.1, so the admin
password is the same as level 2.1's. On a fresh clone, copy each `appsettings.example.json` to `appsettings.json`:

- `ElsaServer/appsettings.json`: `Identity:Tokens:SigningKey` (32+ random characters) and `Identity:Bootstrap:Password`.
  `Http`, `CustomApi:BaseUrl` and `CustomUi:BaseUrl` are the addresses the workflows use.
- `CustomApi/appsettings.json`: `ElsaServer:Password`, the same password.
- `CustomUi/appsettings.json` has no secrets.

Start a local test SMTP server on port 2525 (for example smtp4dev or Papercut).

## Run

Level 2.2 uses the same ports as levels 2 and 2.1, so run one level at a time. In Visual Studio open
**`level_2_2_code_workflows.sln`** and choose the startup profile **"Level 2.2 - all apps"**, or:

```bash
dotnet run --project ElsaServer   # https://localhost:5001
dotnet run --project CustomApi    # https://localhost:5101
dotnet run --project CustomUi     # https://localhost:5201
dotnet run --project ElsaStudio   # https://localhost:6001
```

Open **https://localhost:5201** and submit orders. The workflows are already published; Studio lists them under
**Workflows → Definitions** and shows their instances.

curl works exactly as in level 2.1 (`POST https://localhost:5101/api/orders`, `POST /api/approvals/{id}/decision`).

## Verification

Tested end to end on Elsa 3.9.0 (13 November 2026, after the Flowchart rewrite), on spare ports with scratch databases:

| Check | Result |
|-------|--------|
| ElsaServer start | all eight `L22_*` definitions registered and published, no import |
| Shape compared with level 2.1 | for all eight, the definition Elsa serves to Studio matches the `L21_*.json` twin: root type, every activity's type, ID, name, box text and position, every connection, and the State Machine's states and transitions |
| Rule: 250 / 5000 | `approved` / `needs_review` |
| Event: 500 / 5000 → approve | auto-approved / `approved`; Audit and Listener notifications arrive |
| Same approval decided twice | `409 Conflict` |
| Email link: reject through CustomApi | `rejected` |
| Email link: link clicked directly at ElsaServer | thank-you page; order `approved`; pending approval closed |
| Run Task: approve | `approved` |
| Multi-stage, customer: manager approve → finance approve | `approved` |
| Multi-stage, customer: manager reject | `rejected`; finance never asked |
| Multi-stage, company employee: finance approve | goes straight to finance; `approved` |

Not checked in a browser: Studio's drawing of the C# definitions (only the data it draws from was compared), and the
CustomUi pages (they are level 2.1's, unchanged apart from names).

## Known issues and limits

- **Studio shows an old design for a C# workflow?** Look for a *draft* version. Before the workflows were marked
  read-only (`builder.IsReadonly = true`), opening one in Studio with **Auto-save** on stored an editable draft
  (version 2) on top of the code (version 1). Studio opens the *latest* version, so it kept showing that draft —
  while the server still ran the code. Fix: delete the draft version in Studio (or with
  `POST /elsa/api/bulk-actions/delete/workflow-definitions/by-id`), then restart ElsaServer. Read-only workflows can
  no longer get such drafts.

- **Changing a workflow class while instances are waiting** was not tested. Every activity has a fixed ID
  (`Designer.At`), so a waiting instance should still find the activity it waits on; adding, removing or re-wiring
  boxes can still change what happens next for it. Finish or cancel waiting orders before larger changes, or give
  the changed workflow a new definition ID.
- **CustomApi must be running when a workflow calls it.** The notification and status calls are `FlowSendHttpRequest`
  activities whose failure ports are not connected (same as level 2.1). What happens when CustomApi is down was not
  tested; check the instance
  in Studio for an incident if an order stays `waiting`.
- Everything else from level 2.1 applies: Run Task completion is asynchronous, CustomApi creates its database with
  `EnsureCreated`, and `Elsa.Webhooks` has no 3.9.0 release.

## Security (DEV ONLY)

Same as level 2.1: CustomApi has no authentication, the Elsa callbacks are anonymous, CustomApi signs in to Elsa as
the admin, and ElsaServer's CORS policy allows any origin.
