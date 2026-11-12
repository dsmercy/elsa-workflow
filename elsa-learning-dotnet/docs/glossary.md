# Glossary

Terms used across the course, grouped by theme. The phase number shows where each is introduced.

## Core

| Term | Meaning | Phase |
|------|---------|-------|
| Elsa Server | ASP.NET Core app hosting the workflow engine and REST API | 1 |
| Elsa Studio | Blazor designer and monitoring UI that connects to a server | 1 |
| Activity | One step of a workflow | 1 |
| Workflow definition | Reusable blueprint; has versions (draft/published) | 1 |
| Workflow instance | One execution of a definition, with its own state | 1 |
| Published | The version that executes and whose triggers are indexed | 1 |
| Trigger | An activity that can start new instances | 1 |
| Code-first workflow | Workflow written in C# by deriving from `WorkflowBase` | 1 |
| Module / feature | A capability enabled in `AddElsa(...)` | 1 |

## Data and flow

| Term | Meaning | Phase |
|------|---------|-------|
| Input / Output (workflow) | Values going into and out of a workflow | 2 |
| Variable | Typed value local to one instance | 2 |
| Expression | Computed value for an input (JavaScript or Liquid here) | 2 |
| Flowchart / Sequence | Containers that define how activities are connected or ordered | 2 |
| Outcome | Named exit of an activity that selects the next path | 2 |
| Definition ID | Stable identifier of a workflow definition | 2 |

## Extensibility

| Term | Meaning | Phase |
|------|---------|-------|
| Custom activity | Your C# activity discovered by the server and shown in Studio | 3 |
| `Input<T>` / `Output<T>` | Typed activity properties | 3 |
| `ActivityExecutionContext` | Handle to the running workflow inside an activity | 3 |

## Waiting and time

| Term | Meaning | Phase |
|------|---------|-------|
| Blocking activity | Activity that pauses the workflow until resumed | 4 |
| Bookmark | Saved pause point of an instance | 4 |
| Stimulus | External signal matched against triggers and bookmarks | 4 |
| Resume | Continue a suspended instance | 4 |
| Correlation ID | Identifier tying instances to a business object | 4 |
| Suspended | Healthy waiting sub-status of a running instance | 4 |
| Delay / Timer / Cron / StartAt | Scheduling activities (inline = wait; trigger = start new instance) | 4 |
| Scheduler | Component that fires scheduled work | 4 |

## Composition and reliability

| Term | Meaning | Phase |
|------|---------|-------|
| Child workflow | Workflow started by another workflow | 5 |
| Dispatch / Dispatcher | Queueing a workflow to run asynchronously | 5 |
| Bulk dispatch | One child per item in a collection | 5 |
| Fault | Instance state after an unhandled error stops the run | 5 |
| Incident | Recorded error with activity and exception details | 5 |
| Incident strategy | Policy: Fault or ContinueWithIncidents | 5 |
| Resilience strategy | Retry/timeout pipeline around an activity | 5 |
| Activation strategy | Rule for whether a new instance may start | 5 |
| Journal | Ordered execution log of an instance | 5 |

## Services you may meet in code

| Service | Role | Phase |
|---------|------|-------|
| `IWorkflowRunner` | Runs a workflow now, in-process | 1 |
| `IWorkflowRuntime` | Higher-level API to start/resume managed workflows | 2 |
| `IWorkflowDispatcher` | Asynchronous queue boundary for starting/resuming work | 5 |

## Production

| Term | Meaning | Phase |
|------|---------|-------|
| Elsa Identity | Built-in users, roles, applications and tokens | 6 |
| API key | Machine credential tied to an identity application | 6 |
| Permission | Claim granting access to an API operation | 6 |
| Distributed lock | Cross-node mutual exclusion for resuming/processing | 6 |
| Distributed runtime | Runtime mode for multiple nodes | 6 |
| Clustered scheduler | Makes scheduled work fire once across nodes | 6 |
| Health checks | Liveness/readiness probes | 6 |
