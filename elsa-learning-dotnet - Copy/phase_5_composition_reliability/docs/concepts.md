# Phase 5 — Composition & Reliability: Concepts

Covers: `level_8_child_workflows`, `level_9_errors_and_incidents`

## 1. What this phase is about

Large processes are easier to maintain when they are built from smaller workflows, and real systems fail in the middle of a run. This phase covers both: **composition** (a parent workflow dispatching child workflows, one or many) and **reliability** (what Elsa does when an activity throws, how to retry, how to prevent duplicate runs, and how to find out what happened).

## 2. Key concepts

| Term | Meaning | Where you see it |
|------|---------|------------------|
| Child workflow | A workflow started by another workflow | `Dispatch Workflow` activity |
| Dispatch | Queue a workflow to run (asynchronously) instead of running it inline | `IWorkflowDispatcher` |
| Wait for completion | Option on dispatch: parent suspends until the child finishes | Activity setting |
| Bulk dispatch | Start the same child once per item in a collection | `Bulk Dispatch Workflows` (Composition category) |
| Child ports | `ChildCompleted` / `ChildFaulted`-style branches reacting to individual children | Bulk dispatch activity ports |
| Fault | An activity threw an unhandled exception | Instance sub-status *Faulted* |
| Incident | A recorded error (which activity, what exception) | Incidents view in Studio |
| Incident strategy | Policy for what to do after an incident: stop (**Fault**) or keep going (**ContinueWithIncidents**) | Workflow settings; global default in options |
| Resilience strategy | A retry/timeout policy (a Polly pipeline) applied to an activity | Resilience configuration in the host |
| Activation strategy | Whether a new instance may start: *Always*, *Singleton*, *Correlation*, *Correlated Singleton* | Workflow settings |
| Journal | Ordered execution log of one instance | Instance details; `…/journal` API |
| Execution state | Compact status for polling | `…/execution-state` API |

## 3. How the pieces fit together

```
Parent: Order workflow
   ├─ Bulk Dispatch (Items = order lines, per-line correlation ID)
   │       ├─ child instance (line 1) ─┐
   │       ├─ child instance (line 2) ─┼─► parent waits (optional) ─► continues
   │       └─ child instance (line 3) ─┘        └─ ChildFaulted port for any that failed
   └─ …

Failure inside any activity:
   exception ─► (resilience pipeline retries?) ─► incident recorded
             ─► incident strategy:  Fault → instance stops, status Faulted
                                    ContinueWithIncidents → run goes on, incidents are kept
```

Resilience and incident handling are separate layers. A resilience strategy only controls the retry pipeline around the activity; whether the workflow becomes faulted after the pipeline gives up is decided by normal incident handling.

## 4. Worked example

**Level 8.** An order has three lines. The parent uses bulk dispatch to start one `OrderLineChild` workflow per line, with the line ID as the correlation ID, waits for all of them, and reads each child's output (for example a stock result). A second variant dispatches a single child fire-and-forget.

**Level 9.** A stock check calls a service that fails a configurable share of the time. With the default strategy the instance becomes *Faulted*. You then switch to *ContinueWithIncidents*, add a retry policy, restrict creation with a *Correlated Singleton* activation strategy so the same order cannot be processed twice, and use Studio and the journal endpoint to explain what happened.

## 5. Common pitfalls

- **Fire-and-forget surprises.** Without "wait for completion" the parent finishes before children do; any child output is not available.
- **Dispatch is asynchronous.** Dispatched work runs through the dispatcher (background queue); do not assume it has started when the activity returns.
- **Retrying non-idempotent work.** Retrying a call that charges a card twice is worse than failing. Design activities to be idempotent first.
- **`ContinueWithIncidents` hiding errors.** The run proceeds, so you must watch incidents, or bad data flows downstream.
- **Treating retries as a replacement for fixing causes.** They help with transient failures only.
- **Activation strategy ≠ locking.** It decides whether a *new instance may start*; it does not make your downstream data operations safe.
- **Version drift.** Parent and child are separate definitions; publish both, and re-copy IDs after re-importing.

## 6. Self-check questions

1. When would you use Dispatch Workflow rather than putting all activities in one workflow?
2. What happens to the parent if "wait for completion" is turned off?
3. What is the difference between a fault and an incident?
4. Which incident strategy lets a run continue after an error?
5. Which activation strategy prevents two concurrent instances for the same order?

**Answers.** (1) For reuse, separation of concerns, independent scaling and per-item processing. (2) It completes the dispatch immediately and moves on, without child results. (3) An incident is the recorded error; a fault is the instance state that results when the strategy stops the run. (4) ContinueWithIncidents. (5) Correlated Singleton (same workflow and correlation ID) — or Correlation if no other workflow at all may run for that ID.

## 7. Official docs to read next

- Dispatch Workflow activity: https://docs.elsaworkflows.io/guides/running-workflows/dispatch-workflow-activity
- Bulk dispatch: https://docs.elsaworkflows.io/guides/running-workflows/bulk-dispatch-workflows
- Incident strategies: https://docs.elsaworkflows.io/operate/incidents/strategies
- Custom resilience strategies: https://docs.elsaworkflows.io/extensibility/custom-resilience-strategies
- Activation strategies: https://docs.elsaworkflows.io/operate/workflow-activation-strategies
- Workflow state and journal: https://docs.elsaworkflows.io/operate/workflow-state-and-journal
- Troubleshooting: https://docs.elsaworkflows.io/guides/troubleshooting
