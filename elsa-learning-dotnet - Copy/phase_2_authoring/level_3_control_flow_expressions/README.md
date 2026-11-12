# Phase 2 · Level 3 · Variables, branching, loops, expressions

## Concept overview

The order now has **lines**. `L3_OrderProcessing` keeps working state in **variables** and loops over the lines with **For Each** to compute a total (JavaScript). It branches twice: a **Decision** sets the status (`> 1000` → `needs_review`) and a **Switch** picks a customer tier. A **Liquid** template builds a summary message. Four **outputs** (`Total`, `Status`, `Tier`, `Message`) go back to MVC. This is everyday Elsa: data in, local state, decisions, data out.

## What's in this folder

```
level_3_control_flow_expressions/
├── MvcApp/                              Elsa Server + MVC (port 5001)
│   ├── Controllers/OrdersController.cs  builds Order + Lines, starts by definition ID, reads 4 outputs
│   ├── Models/Order.cs                  Order + OrderLine records
│   ├── Models/OrderViewModels.cs        3-row line form, result
│   ├── Views/Orders/Create.cshtml (+ shared views)
│   ├── Program.cs, MvcApp.csproj, Properties/launchSettings.json
│   ├── appsettings.example.json         committed (Workflow:DefinitionId placeholder)
│   └── appsettings.json                 gitignored
├── ElsaStudio/                          Elsa Studio (port 6001), this level's own copy
├── workflows/L3_OrderProcessing.json    importable definition (tested against 3.9.0)
└── .gitignore
```

## The workflow

```
Set Variable  OrderId   = getInput("Order").OrderId          (JavaScript)
Set Variable  LineCount = getInput("Order").Lines.Count      (JavaScript)
For Each      Items = getInput("Order").Lines                (JavaScript), Current value → CurrentLine
   └ Body: Set Variable Total = variables.Total
                                + variables.CurrentLine.Quantity * variables.CurrentLine.UnitPrice
Decision      variables.Total > 1000
   ├ True  → Set Variable Status = needs_review
   └ False → Set Variable Status = approved
Switch (flow), mode Match first
   ├ Gold    : variables.Total >= 5000 → Set Variable Tier = Gold
   ├ Silver  : variables.Total >= 1000 → Set Variable Tier = Silver
   └ Default                           → Set Variable Tier = Bronze
Set Variable  Message = (Liquid)
   Order {{ Variables.OrderId }} with {{ Variables.LineCount }} line(s): total {{ Variables.Total | round: 2 }}, tier {{ Variables.Tier }}, status {{ Variables.Status }}.
Set Output    Total, Status, Tier, Message   (JavaScript: variables.X)
```

| Variable | Type | Purpose |
|----------|------|---------|
| `OrderId` | String | copy of the input, for Liquid |
| `LineCount` | Int32 | number of lines, for Liquid |
| `Total` | Double (initial `0`) | running sum in the loop |
| `CurrentLine` | Object | For Each's *Current value* |
| `Status`, `Tier`, `Message` | String | results |

**Why copy `OrderId` and `LineCount` into variables?** JavaScript can read the C# `Order` record passed from MVC directly (`getInput("Order").Amount`). Liquid can't: `{{ Input.Order.OrderId }}` renders **empty**, because Elsa's Liquid engine (Fluid) doesn't read properties of unregistered CLR types. Variables do work in Liquid. So the rule this level teaches is: JavaScript for logic and data access, Liquid for text templates over variables. This was tested on 3.9.0.

**Why `Double` for money?** JavaScript numbers are doubles. The controller rounds the `Total` output back to a 2-decimal `decimal`. Phase 3's custom activities are the place to do exact `decimal` maths in C#.

## Setup

`MvcApp/appsettings.json` already exists, with a random signing key and password, and `Workflow:DefinitionId` = `L3_OrderProcessing` (the ID the import file uses).

```bash
cd MvcApp && dotnet build && cd ..
cd ElsaStudio && dotnet build && cd ..
```

## Run

Stop any other level first.

```bash
# Terminal 1
cd MvcApp
dotnet run --urls https://localhost:5001

# Terminal 2
cd ElsaStudio
dotnet run --urls https://localhost:6001
```

Log in to https://localhost:6001 with the bootstrap admin from `MvcApp/appsettings.json`.

## Studio steps: build `L3_OrderProcessing`

1. **Workflows → Definitions → Create workflow**. Name: `L3_OrderProcessing`.
2. In the workflow settings (click an empty spot on the canvas):
   - **Input/Output → Inputs**: `Order` (Object).
   - **Input/Output → Outputs**: `Total` (Double), `Status` (String), `Tier` (String), `Message` (String).
   - **Variables**: add the seven variables in the table above. Give `Total` the initial value `0`.
3. **Primitives → Set Variable** ×2: `OrderId` = JS `getInput("Order").OrderId`; `LineCount` = JS `getInput("Order").Lines.Count`. Connect them in that order.
4. **Looping → For Each**: **Items** = JS `getInput("Order").Lines`, **Current value** → variable `CurrentLine`.
   Open its **Body** (double-click or use the *Body* port) and drop one **Set Variable** in it: `Total` = JS
   `variables.Total + variables.CurrentLine.Quantity * variables.CurrentLine.UnitPrice`.
   Connect the second capture step → For Each.
5. **Branching → Decision**: Condition JS `variables.Total > 1000`. Connect For Each **Done** → Decision.
   Add two **Set Variable** (`Status` = `needs_review` / `approved`, Literal) on **True** / **False**.
6. **Branching → Switch (flow)**: mode *Match first*. Cases: `Gold` → JS `variables.Total >= 5000`; `Silver` → JS `variables.Total >= 1000`.
   Connect **both** status activities → Switch. Add three **Set Variable** for `Tier` on **Gold**, **Silver** and **Default** (= Bronze).
7. **Set Variable** `Message` with syntax **Liquid** and the template shown above. Connect all three tier activities → it.
8. **Composition → Set Output** ×4, in a chain: `Total`, `Status`, `Tier`, `Message`, each with JS value `variables.Total` / `variables.Status` / etc.
9. **Publish**. Copy the **definition ID** into `MvcApp/appsettings.json` → `Workflow:DefinitionId` and restart the MvcApp.

**Shortcut:** **Import** `workflows/L3_OrderProcessing.json` and **Publish**. Its ID already matches `appsettings.json`.

## How to verify

Open https://localhost:5001 and submit these orders. Blank rows are ignored. Each result below was observed on 3.9.0:

| Lines | Total | Status | Tier |
|-------|-------|--------|------|
| Pen 2 × 100, Pad 1 × 50 | 250.00 | approved | Bronze |
| Desk 1 × 1000 | 1000.00 | approved (rule is `> 1000`) | Silver |
| Rack 10 × 600 | 6000.00 | needs_review | Gold |

The message looks like this:
`Order 145d78fe-… with 1 line(s): total 1000, tier Silver, status approved.`

Also try:
- A line with quantity `0` is rejected by MVC validation before any workflow runs.
- Studio → **Workflows → Instances** → open one. The **Variables** tab shows `Total`, `Tier`, etc. for *that instance only*; every run has its own copy. The journal shows the For Each body executing once per line.
- Change the Message expression to `{{ Input.Order.OrderId }}`, then publish and submit. The ID comes out empty (see "Why copy" above).

## Key takeaway

Variables are an instance's local state. Activities like For Each, Decision and Switch move data between them and choose paths by **outcome**, while each input's **syntax** (Literal / JavaScript / Liquid) decides how its value is computed.

**Docs pointer:** `../docs/concepts.md` → §3 *How the pieces fit together*, §4 *Worked example — Level 3*, and §5 *Common pitfalls* (expression syntax, scope, casing and types).
