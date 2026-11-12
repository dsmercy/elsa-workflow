# Learning Path — Elsa Workflows with ASP.NET Core MVC

Six phases, two levels each. Read the phase's `docs/concepts.md`, build its two levels, answer the self-check questions, then move on.

```
Phase 1  Foundations      ──►  Phase 2  Authoring        ──►  Phase 3  Extending
(run & see a workflow)         (data, branching, MVC)         (your own activities)
                                                                      │
Phase 6  Production       ◄──  Phase 5  Composition &    ◄──  Phase 4  Long-running
(secure & operate)             Reliability                     (pause, resume, time)
                               (child workflows, faults)
```

## What each phase builds on

| Phase | Needs from earlier phases | Produces for later phases |
|-------|---------------------------|---------------------------|
| 1 Foundations | .NET basics, MVC basics | A running server + Studio; the idea of definition vs instance |
| 2 Authoring | Publish/run basics | The Order workflow and the MVC start-and-read-result pattern |
| 3 Extending | Inputs/outputs/outcomes | Custom activities used in the Order workflow |
| 4 Long-running | Order workflow, activities | Bookmarks, correlation IDs, timers — the idea of waiting |
| 5 Composition & Reliability | Bookmarks, correlation | Child workflows, fault handling, diagnostics |
| 6 Production | All of the above | Security, real database, multi-node awareness |

## Suggested pace

| Phase | Reading | Building | Total |
|-------|---------|----------|-------|
| 1 | 30 min | 2 evenings | ~1 weekend |
| 2 | 30 min | 2 evenings | ~1 weekend |
| 3 | 20 min | 1–2 evenings | ~1 weekend |
| 4 | 45 min | 2–3 evenings | ~1–2 weekends |
| 5 | 45 min | 2–3 evenings | ~1–2 weekends |
| 6 | 45 min | 2 evenings + optional cluster experiment | ~1–2 weekends |

These are rough guesses to help planning, not measurements.

## "Basics only" route

If you only need a simple workflow started from MVC, do **Phases 1 and 2** and stop. Phases 3–6 are optional depth.

## How to ask for work

| You say | What you get |
|---------|--------------|
| "Teach Phase 4" | The concept document for the phase, in the standard template |
| "Implement Phase 4, Level 6" | The full level: code, config examples, `.gitignore`, run steps, Studio steps |
| "Review my Level 6" | A check of your code against the phase's concepts and pitfalls |
| "Quiz me on Phase 3" | The self-check questions, one at a time |
