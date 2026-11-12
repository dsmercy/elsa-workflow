# Phase 4 — Long-Running Workflows: Concepts

Covers: `level_6_human_approval_bookmarks`, `level_7_timers_and_reminders`

## 1. What this phase is about

Real processes wait: for a manager to click *Approve*, for a payment webhook, for 24 hours to pass. Elsa handles waiting by **suspending** a workflow instance at a **bookmark** and storing its state. Later, an external signal (a **stimulus**) or a scheduled time **resumes** it from exactly where it stopped. This is what separates a workflow engine from a plain function call, and it is the heart of this phase.

## 2. Key concepts

| Term | Meaning | Where you see it |
|------|---------|------------------|
| Blocking activity | An activity that does not complete immediately; it pauses the workflow | `Event`, `Delay`, inline `Timer`/`Cron` |
| Bookmark | A saved "pause point" belonging to a *workflow instance*, stored in the database | Instance details in Studio |
| Stimulus | An external signal Elsa tries to match to triggers and bookmarks | Event name, payload, correlation data |
| Resume | Continuing a suspended instance from its bookmark | Resume endpoint, runtime APIs |
| Trigger | An activity that *starts* new instances (`HTTP Endpoint`, `Timer`, `Cron`, `StartAt`) | Needs `CanStartWorkflow` for scheduling activities |
| Correlation ID | An identifier that ties instances to a business object (e.g. the order ID) | Workflow instance details; `Correlate` activity |
| Suspended | Instance sub-status for a healthy waiting workflow | Instances list |
| `Delay` | Pauses an instance and resumes it after a time span | Always resumes an existing instance |
| `Timer` / `Cron` / `StartAt` | Scheduling activities; as triggers they start new instances, inline they wait | Scheduling category |
| Bookmark resume token | A token-based URL that resumes a specific bookmark | `/elsa/api/bookmarks/resume?t=…` |
| Persistence of runtime state | Bookmarks and instances must be stored to survive restarts | `UseWorkflowRuntime(...EF Core…)` |
| Scheduler | Component that fires scheduled resumes and triggers | `.UseScheduling()` |

## 3. How the pieces fit together

```
Start ─► … ─► [Event "OrderApproved"]  ← blocking activity
                   │ creates bookmark (saved to DB), instance = Suspended
                   ▼
        … hours or days pass, even a process restart …
                   ▲
Manager clicks Approve in MVC ─► stimulus (event name + correlation ID = OrderId)
                   │ Elsa matches the bookmark (hashed lookup, under a lock)
                   ▼
              workflow resumes after the Event ─► … ─► Finish
```

Two different "starts": a **trigger** begins a *new* instance; a **bookmark** resumes an *existing* one. Delay is bookmark-based; a `Timer` or `Cron` used as the first activity with `CanStartWorkflow` enabled is trigger-based and begins a new instance on every tick.

## 4. Worked example

**Level 6.** Orders above the threshold reach an `Event` activity and suspend. The order ID is set as the **correlation ID** when the workflow is started. The MVC page `Orders/Approve/{id}` sends the matching event with that correlation ID, so Elsa resumes only that order's instance, and the workflow sets the final status.

**Level 7.** A `Delay` branch runs in parallel with the approval wait: if no one approves within N minutes, a reminder is logged. A separate workflow starts from a `Timer` (or `Cron`) trigger and lists orders that are still `needs_review`.

## 5. Common pitfalls

- **Nothing persists across restarts** unless runtime persistence is configured; with scheduling, add `UseScheduling()` and a persistent provider.
- **Case-sensitive matching.** The default bookmark hash is case-sensitive; normalise event names and correlation values.
- **Payload agreement.** The activity that creates the bookmark and the code that resumes it must agree on the stimulus shape.
- **Unpublished or untriggered schedules.** `Timer`, `Cron` and `StartAt` only start workflows when `CanStartWorkflow` is enabled *and* the workflow is published. In Elsa 3.8+ the Timer input is named `TimeSpan` (older examples show `Duration`).
- **Treating `Suspended` as broken.** A running-but-suspended instance is normally just waiting.
- **Always add a timeout path.** A wait with no timeout or reminder can leave instances waiting forever.
- **Several nodes.** A scheduled activity on three nodes without proper clustering can fire three times (Phase 6).

## 6. Self-check questions

1. Do bookmarks belong to workflow definitions or to workflow instances?
2. What is the difference between a trigger and a bookmark?
3. Why use the order ID as a correlation ID?
4. Which activity always resumes an existing instance and never starts one?
5. What must be configured so a waiting workflow survives an app restart?

**Answers.** (1) Instances. (2) A trigger starts a new instance; a bookmark resumes an existing one. (3) It lets you find and resume exactly the instances that belong to that order. (4) `Delay`. (5) A persistent runtime store (EF Core here), plus scheduling for time-based waits.

## 7. Official docs to read next

- Long-running workflows: https://docs.elsaworkflows.io/guides/running-workflows/long-running-workflows
- Blocking activities and triggers: https://docs.elsaworkflows.io/activities/blocking-and-triggers
- Timer and scheduled workflows: https://docs.elsaworkflows.io/guides/running-workflows/timer-and-scheduled-workflows
- Correlation ID: https://docs.elsaworkflows.io/getting-started/concepts/correlation-id
- Human approval pattern: https://docs.elsaworkflows.io/guides/patterns
