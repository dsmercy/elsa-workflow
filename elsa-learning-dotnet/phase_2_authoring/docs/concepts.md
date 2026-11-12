# Phase 2 — Authoring: Concepts

Covers: `level_2_mvc_designer_workflow`, `level_3_control_flow_expressions`

## 1. What this phase is about

Once a workflow runs, the next question is how data gets in, moves around, and drives decisions. This phase covers **inputs and outputs**, **variables**, **branching and looping**, and **expressions**. You also learn how an application (your MVC app) starts a workflow that was designed in Studio and reads back a result. This is the core of everyday Elsa work.

## 2. Key concepts

| Term | Meaning | Where you see it |
|------|---------|------------------|
| Workflow input | A value supplied when the workflow starts | *Input/Output* tab of a workflow in Studio |
| Workflow output | A value the workflow hands back to its caller | Same tab; read by the caller after the run |
| Variable | A named, typed value that lives inside one workflow instance | *Variables* tab in Studio |
| Activity input / output | The properties an activity reads (inputs) and produces (outputs) | Activity settings panel |
| Expression | A computed value for an activity input; syntax chosen per input | Syntax dropdown beside an input |
| JavaScript / Liquid | The two expression languages enabled in this course | `.UseJavaScript()`, `.UseLiquid()` |
| Flowchart | The default designer container: activities connected by arrows | Studio canvas |
| Sequence | A container that runs children in order (common in code-first workflows) | `new Sequence { Activities = { … } }` |
| `If` / `Switch` | Branch on a condition or on a value | Studio toolbox, *Flow* / *Branching* |
| `ForEach` | Loop over a collection | Studio toolbox |
| Outcome | The named exit of an activity that decides which arrow is followed | Ports on an activity (e.g. *True* / *False*) |
| Definition ID | Stable identifier of a workflow definition | Workflow properties in Studio |

## 3. How the pieces fit together

```
MVC form (Product, Amount)
   │  input: { Order }
   ▼
 Workflow instance
   ├─ variable: Status
   ├─ If (Amount > 1000)
   │    ├─ True  → Set Status = "needs_review"
   │    └─ False → Set Status = "approved"
   ├─ ForEach (Lines) → add up line totals
   └─ output: { Status, Total }
   ▲
   │  result read back
MVC view shows the outcome
```

Think of it as a function: inputs go in, variables are the local state, outputs come out. Activities are the statements, outcomes are the `if`/`else` exits.

## 4. Worked example

**Level 2.** You build `L2_OrderApproval` in Studio with an `Order` input and a rule: amounts up to 1000 are *approved*, higher amounts are *needs_review*. After publishing, you copy the workflow's **definition ID** into the MVC app's configuration. The `Orders/Create` form starts the workflow with the submitted order as input and then shows the resulting status.

**Level 3.** The order now has several lines. A `ForEach` adds up the line totals, a `Switch` picks a customer tier, and JavaScript or Liquid expressions build a message. The computed total and status are returned to MVC as outputs.

## 5. Common pitfalls

- **Wrong expression syntax.** An input set to *Literal* will not evaluate `Amount > 1000`; pick *JavaScript* (or *Liquid* for text templates).
- **Scope confusion.** Variables belong to one workflow instance; they are not shared between instances or workflows.
- **Casing and types.** Input names and types must match between the Studio definition and the object your controller passes.
- **Copying the wrong ID.** The definition ID is not the same as the workflow *name* or an instance ID.
- **Unpublished changes.** The MVC call uses the *published* version; edit, then publish again.
- **C# and Python expressions.** These execute host code and are disabled in this course on purpose.

## 6. Self-check questions

1. What is the difference between a workflow variable and a workflow input?
2. Which part of the designer decides which arrow is followed after an `If`?
3. Why can an expression fail even when the logic is correct?
4. How does your MVC app know which workflow to start?
5. When would you pick Liquid over JavaScript?

**Answers.** (1) An input is supplied by the caller at start; a variable is internal state of the instance. (2) The outcome the `If` completes with (True/False). (3) The input's syntax may be set to Literal, or the data shape or types differ from what the expression expects. (4) By the workflow definition ID in configuration. (5) For simple text templating with placeholders; JavaScript for logic and calculations.

## 7. Official docs to read next

- Concepts (variables, inputs, outcomes): https://docs.elsaworkflows.io/getting-started/concepts
- Running workflows: https://docs.elsaworkflows.io/guides/running-workflows
- Execution model (runner vs runtime vs dispatcher): https://docs.elsaworkflows.io/guides/architecture/execution-model
