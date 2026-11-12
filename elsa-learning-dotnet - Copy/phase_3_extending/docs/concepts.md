# Phase 3 — Extending: Concepts

Covers: `level_4_custom_activity`, `level_5_activity_outcomes_and_services`

## 1. What this phase is about

The built-in activities cover generic work. Your business logic — calculating a discount, checking a credit limit — belongs in **custom activities**: C# classes that appear in the Studio toolbox next to the built-in ones. This phase shows how to define an activity's inputs and outputs, how to give it several **outcomes** so the designer can branch on its result, and how to use dependency injection so activities call your normal application services.

## 2. Key concepts

| Term | Meaning | Where you see it |
|------|---------|------------------|
| Custom activity | A C# class that Elsa discovers and shows in Studio | `Activities/CalculateDiscount.cs` |
| `[Activity(...)]` attribute | Gives the activity a namespace, category and description in the toolbox | Above the class |
| Code activity | An activity that does work and completes (as opposed to blocking) | Base class such as `CodeActivity` / `CodeActivity<T>` |
| `Input<T>` | A typed property the designer lets the user set (literal or expression) | Property on the activity |
| `Output<T>` | A typed value the activity produces for later activities | Property on the activity |
| Outcome | A named exit chosen by the activity at completion | e.g. *Approved* / *Declined* ports |
| Activity registration | Telling Elsa where to find your activities | `.AddActivitiesFrom<Program>()` |
| Dependency injection | Resolving your services inside an activity | Constructor injection of `ICreditService` |
| `ActivityExecutionContext` | The activity's handle to the running workflow (inputs, variables, completion) | Parameter of the execute method |

## 3. How the pieces fit together

```
C# class CalculateDiscount : CodeActivity<decimal>
        │  discovered at startup via AddActivitiesFrom<Program>()
        ▼
 Activity registry ──► Server API ──► Studio toolbox ("Learning ▸ Orders")
                                          │ user drags it onto the canvas
                                          ▼
                      Inputs set in designer ─► ExecuteAsync(context) ─► Output / Outcome
```

The designer never contains your code. It only stores which activity type to run and the values for its inputs. The server owns the implementation, which is why it must be registered on the server, and why Studio shows nothing new until the server restarts.

## 4. Worked example

**Level 4.** `CalculateDiscount` takes the order amount and a customer tier, and outputs the discounted amount. You rebuild and restart the server, refresh Studio, find the activity in the toolbox, and drop it into the order workflow before the approval rule.

**Level 5.** `CheckCreditLimit` receives an `ICreditService` through dependency injection, asks it whether the customer can afford the order, and completes with either an *Approved* or a *Declined* outcome. In the designer the two outcomes lead to different branches.

## 5. Common pitfalls

- **Activity missing in Studio.** The server was not restarted, the assembly was not scanned, or Studio has a stale cache. Check `AddActivitiesFrom<...>()` first.
- **Changing an activity's shape.** Renaming an input or output that existing workflows use can break them; plan changes, and version where needed.
- **Heavy work inside an activity.** Long operations belong in a background mechanism or a blocking activity (Phase 4), not in a blocking call.
- **Swallowing exceptions.** Let faults surface (Phase 5 shows how Elsa records them) rather than catching everything.
- **Service lifetime mistakes.** Activities resolve services from DI; register scoped/singleton services deliberately.
- **Assuming APIs.** Verify member names against the Elsa 3.9.0 docs and source; the course prompts mark uncertain ones with `// VERIFY:`.

## 6. Self-check questions

1. Why does a new custom activity not appear in Studio until the server restarts?
2. What is the difference between an `Output<T>` and an outcome?
3. How does an activity get access to an application service?
4. What does the `[Activity]` attribute control?
5. Why should activity behaviour changes be made carefully?

**Answers.** (1) Activities are discovered by the server at startup; Studio only reads the server's registry. (2) An output carries a *value*; an outcome selects which *path* the workflow follows. (3) By constructor injection, as for any DI-registered type. (4) Namespace, category, display name and description in the toolbox. (5) Published workflows reference the activity by type and property names, so changes can break them.

## 7. Official docs to read next

- Blocking activities and triggers (also shows activity patterns): https://docs.elsaworkflows.io/activities/blocking-and-triggers
- Concepts: https://docs.elsaworkflows.io/getting-started/concepts
- Architecture (activities and modules): https://docs.elsaworkflows.io/guides/architecture
