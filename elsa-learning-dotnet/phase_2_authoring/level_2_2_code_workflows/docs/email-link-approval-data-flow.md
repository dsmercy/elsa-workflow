# Level 2.2 · Email-link approval — data flow

How one **"Approve by email link"** order (`L22_EmailLinkApproval`, amount over 1000) moves between the four apps,
the two databases and the mail server. Port numbers are the defaults from each app's `launchSettings.json` /
`appsettings.json`.

> The diagrams are Mermaid. They render on GitHub and in VS Code's Markdown preview with a Mermaid extension.

## 1. Data flow diagram

Numbers on the arrows match the steps in section 3. Solid arrows happen for every order; dashed ones are the
alternatives and the read-only Studio view.

```mermaid
flowchart LR
    %% ---- external entities ----
    Customer(["👤 Customer<br/>(browser)"])
    Approver(["👤 Approver<br/>(mail client + browser)"])
    Admin(["👤 Developer<br/>(browser)"])

    %% ---- the four apps ----
    UI["<b>CustomUi</b> :5201<br/>MVC pages<br/>no database"]
    API["<b>CustomApi</b> :5101<br/>REST API<br/>ElsaClient"]
    ES["<b>ElsaServer</b> :5001<br/>Elsa engine · /elsa/api · /workflows<br/>L22_EmailLinkApproval (C#)<br/>WorkflowStatusForwarder"]
    Studio["<b>ElsaStudio</b> :6001<br/>Blazor WASM"]

    %% ---- data stores / external system ----
    CDB[("<b>custom.sqlite.db</b><br/>Orders · Approvals · Notifications")]
    EDB[("<b>elsa.sqlite.db</b><br/>WorkflowDefinitions · WorkflowInstances<br/>Bookmarks · ActivityExecutionRecords · Users")]
    SMTP[["SMTP server :2525<br/>(smtp4dev / Papercut)"]]

    %% ---- create the order ----
    Customer -->|"1 · order form<br/>product, amount, pattern=EmailLink"| UI
    UI -->|"2 · POST /api/orders"| API
    API -->|"3 · INSERT Orders<br/>status=running"| CDB
    API -->|"4 · POST /elsa/api/workflow-definitions/<br/>L22_EmailLinkApproval/execute<br/>correlationId=orderId, input.Order"| ES
    ES -->|"5 · new WorkflowInstance<br/>+ child L22_SendNotification"| EDB
    ES -->|"6a · Send Email<br/>Approve / Reject links"| SMTP
    ES -->|"6b · POST /api/notifications<br/>pattern=EmailLink, links"| API
    API -->|"6c · INSERT Notifications<br/>INSERT Approvals status=pending"| CDB
    ES -->|"7 · HTTP Endpoint bookmark<br/>instance Suspended"| EDB
    ES -->|"8 · execute response<br/>instanceId, Suspended"| API
    API -->|"9 · UPDATE Orders<br/>status=waiting, instanceId"| CDB
    API -->|"10 · order JSON"| UI
    UI -->|"11 · 'waiting for approval'"| Customer

    %% ---- decision, path A: the email link ----
    SMTP -->|"12 · email"| Approver
    Approver -->|"13A · click link<br/>GET /workflows/l22/approvals/decision<br/>?correlationId=…&decision=approve"| ES

    %% ---- decision, path B: the Approvals page ----
    Approver -.->|"13B · Approvals page → Approve"| UI
    UI -.->|"13B · POST /api/approvals/{id}/decision"| API
    API -.->|"13B · UPDATE Approvals<br/>status=decided"| CDB
    API -.->|"13B · GET the same link<br/>(on the approver's behalf)"| ES

    %% ---- resume and finish ----
    ES -->|"14 · find bookmark by correlationId<br/>resume · status · instance Finished"| EDB
    ES -->|"15 · thank-you page"| Approver
    ES -->|"16 · WorkflowStatusForwarder<br/>PUT /api/orders/{id}/status"| API
    API -->|"17 · UPDATE Orders status=approved<br/>UPDATE Approvals pending→closed"| CDB

    %% ---- read-only views ----
    Customer -.->|"18 · Orders page"| UI
    Admin -.->|"view definitions & instances"| Studio
    Studio -.->|"/elsa/api (bearer token)"| ES
```

## 2. Sequence diagram

The same flow in time order, with the activity boxes of `L22_EmailLinkApproval` named.

```mermaid
sequenceDiagram
    autonumber
    actor C as Customer
    participant UI as CustomUi :5201
    participant API as CustomApi :5101
    participant CDB as custom.sqlite.db
    participant ES as ElsaServer :5001
    participant EDB as elsa.sqlite.db
    participant M as SMTP :2525
    actor A as Approver

    C->>UI: POST /Orders/Create (form, antiforgery)
    UI->>API: POST /api/orders {product, amount, pattern: EmailLink, createdBy}
    API->>CDB: INSERT Orders (status = running)
    API->>ES: POST /elsa/api/identity/login (once, token cached)
    API->>ES: POST /elsa/api/workflow-definitions/L22_EmailLinkApproval/execute<br/>{correlationId: orderId, input: {Order: {OrderId, Product, Amount}}}
    ES->>EDB: create WorkflowInstance
    Note over ES: Copy order into variables → Correlation ID = OrderId →<br/>Name the instance → Amount > 1000? → True
    ES->>EDB: Execute Workflow: child instance L22_SendNotification
    ES->>M: Send Email (Approve link, Reject link)
    ES->>API: POST /api/notifications {title, message, orderId, link, rejectLink, pattern: EmailLink}
    API->>CDB: INSERT Notifications, INSERT Approvals (pending, ApproveLink, RejectLink)
    API-->>ES: 200
    Note over ES: Wait for GET /l22/approvals/decision (HTTP Endpoint)
    ES->>EDB: save Bookmark, instance Suspended
    ES-->>API: execute response {workflowState: id, Running/Suspended}
    API->>CDB: UPDATE Orders (status = waiting, WorkflowInstanceId)
    API-->>UI: 201 order JSON
    UI-->>C: "Order waiting for approval"

    M-->>A: email with Approve / Reject links

    alt A · approver clicks the link in the email
        A->>ES: GET /workflows/l22/approvals/decision?correlationId=…&decision=approve
    else B · approver uses CustomUi → Approvals
        A->>UI: POST /Approvals/Decide (id, approved)
        UI->>API: POST /api/approvals/{id}/decision {approved, reviewer}
        API->>CDB: UPDATE Approvals (pending → decided) — claim, so a 2nd click gets 409
        API->>ES: GET the stored Approve/Reject link (same URL as path A)
    end

    ES->>EDB: find Bookmark by correlationId, resume instance
    Note over ES: Status from ?decision= → Show a thank-you page →<br/>Output Status → workflow Finished
    ES-->>A: thank-you HTML page (path B: returned to CustomApi)
    ES->>EDB: instance Finished, Output {Status: approved}
    ES->>API: WorkflowStatusForwarder: PUT /api/orders/{id}/status {status: approved}
    API->>CDB: UPDATE Orders (status = approved), UPDATE Approvals (pending → closed)
    C->>UI: Orders page
    UI->>API: GET /api/orders
    API->>CDB: SELECT Orders
    API-->>UI: order (approved)
```

## 3. Steps and the data they carry

| # | From → To | Call / action | Data |
|---|-----------|---------------|------|
| 1 | Customer → CustomUi | `POST /Orders/Create` | Product, Amount, Pattern = `EmailLink`, Placed by |
| 2 | CustomUi → CustomApi | `POST /api/orders` | `{product, amount, pattern, createdBy}` |
| 3 | CustomApi → custom.sqlite.db | `INSERT Orders` | new order ID (GUID), status `running` |
| 4 | CustomApi → ElsaServer | `POST /elsa/api/workflow-definitions/L22_EmailLinkApproval/execute` (bearer token) | `correlationId` = order ID, `input.Order` = `{OrderId, Product, Amount, CreatedBy, RequireManagerApproval}` |
| 5 | ElsaServer → elsa.sqlite.db | create instance (+ child `L22_SendNotification` instance) | variables OrderId, Product, Amount; correlation ID |
| 6a | ElsaServer → SMTP | Send Email (child workflow) | to `manager@l22.local`; Approve/Reject links `…/workflows/l22/approvals/decision?correlationId=<orderId>&decision=approve\|reject` |
| 6b | ElsaServer → CustomApi | `POST /api/notifications` (child workflow, HTTP Request) | `{title, message, orderId, link, rejectLink, pattern: "EmailLink"}` |
| 6c | CustomApi → custom.sqlite.db | `INSERT Notifications`, `INSERT Approvals` | approval `pending` with the two links |
| 7 | ElsaServer → elsa.sqlite.db | HTTP Endpoint suspends | Bookmark (path + correlation ID); instance `Suspended` |
| 8 | ElsaServer → CustomApi | execute response | instance ID, status/sub-status |
| 9 | CustomApi → custom.sqlite.db | `UPDATE Orders` | status `waiting`, `WorkflowInstanceId` |
| 10–11 | CustomApi → CustomUi → Customer | response / page | order JSON → "waiting for approval" |
| 12 | SMTP → Approver | email delivered | the two links |
| 13A | Approver → ElsaServer | `GET /workflows/l22/approvals/decision?…` (anonymous) | `correlationId`, `decision` |
| 13B | Approver → CustomUi → CustomApi → ElsaServer | `POST /Approvals/Decide` → `POST /api/approvals/{id}/decision` → `GET <stored link>` | approved, reviewer; approval row claimed (`decided`) first |
| 14 | ElsaServer → elsa.sqlite.db | bookmark matched, instance resumed and finished | Query variable, Status variable, Output `Status` |
| 15 | ElsaServer → Approver | Write HTTP Response | thank-you HTML |
| 16 | ElsaServer → CustomApi | `WorkflowStatusForwarder` on WorkflowFinished: `PUT /api/orders/{id}/status` | `{status: "approved" \| "rejected"}` |
| 17 | CustomApi → custom.sqlite.db | `UPDATE Orders`, `UPDATE Approvals` | order status + `DecidedAt`; remaining `pending` approvals → `closed` |
| 18 | Customer → CustomUi → CustomApi | `GET /api/orders` | final status shown |

**Amount 1000 or less:** steps 6–7 and 12–17 don't happen. "Amount > 1000?" takes the False branch
("Status = approved (auto)"), the workflow finishes inside step 4, and CustomApi reads `Status` from the execute
response in step 8 (the forwarder also reports it).

## 4. Who owns which data

| Store | Written by | Holds for this flow |
|-------|------------|---------------------|
| `CustomApi/custom.sqlite.db` | CustomApi only | **Orders** (product, amount, pattern, placed by, status, workflow instance ID) · **Approvals** (pattern, links, pending/decided/closed, reviewer) · **Notifications** (title, message, links) |
| `ElsaServer/elsa.sqlite.db` | ElsaServer only | **WorkflowDefinitions** (L22_* published from C#) · **WorkflowInstances** (variables, output, correlation ID, status) · **Bookmarks** (the waiting HTTP Endpoint) · **ActivityExecutionRecords** / logs (Studio's journal) · **Users** (admin) |
| SMTP server | ElsaServer | the approval email |

The two databases never talk to each other. They share only two plain values: the **order ID** (= the workflow's
correlation ID) and the **workflow instance ID** stored on the order.
