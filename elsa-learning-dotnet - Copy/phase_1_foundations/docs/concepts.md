# Phase 1 — Foundations: Concepts

Covers: `level_0_server_and_studio`, `level_1_mvc_code_workflow`

## 1. What this phase is about

Elsa Workflows is a .NET library that lets you describe a process as a set of steps (activities) and then run it, pause it, and inspect it. In this phase you learn the two halves of the product: the **Elsa Server**, which is an ASP.NET Core app that runs workflows and exposes a REST API, and **Elsa Studio**, a Blazor app that lets you design and monitor workflows visually. You also see the two ways a workflow can come into existence: drawn in Studio, or written in C#.

Understanding the Server/Studio split now saves a lot of confusion later: Studio does not run anything itself. It is a client of the server's API.

## 2. Key concepts

| Term | Meaning | Where you see it |
|------|---------|------------------|
| Elsa Server | ASP.NET Core app hosting the workflow engine and REST API | `AddElsa(...)` in `Program.cs`; API at `/elsa/api` |
| Elsa Studio | Blazor app (designer + monitoring UI) that talks to a server | `ElsaStudio` project; `Backend:Url` setting |
| Activity | One step in a workflow | Studio toolbox; C# classes such as `WriteLine` |
| Workflow definition | The reusable blueprint of a workflow | "Workflow Definitions" list in Studio |
| Workflow instance | One execution of a definition, with its own state | "Workflow Instances" list in Studio |
| Draft vs published | Only a **published** version is executed and has its triggers indexed | Publish button in Studio |
| Trigger | An activity that can *start* a workflow, e.g. `HTTP Endpoint` | First activity of a workflow |
| Code-first workflow | A workflow defined in C# by deriving from `WorkflowBase` | `Workflows/HelloWorkflow.cs` |
| Module / feature | A capability you switch on in `AddElsa` (HTTP, scheduling, identity…) | `.UseHttp()`, `.UseScheduling()` … |
| Persistence | Where definitions, instances and bookmarks are stored | EF Core + SQLite in this course |

## 3. How the pieces fit together

```
 Browser ──► Elsa Studio (Blazor WASM, :6001)
                  │  REST + login (ElsaIdentity)
                  ▼
 Browser ──► Elsa Server / MVC app (:5001)
              ├─ /elsa/api/...        management API (used by Studio)
              ├─ {Http:BasePath}/...  endpoints created by HTTP Endpoint activities
              ├─ Workflow engine      runs activities
              └─ SQLite               definitions + instances
```

Two URLs that look alike but differ: `/elsa/api` is the **management API** Studio uses; `Http:BasePath` (for example `/workflows`) is the prefix of **endpoints your own workflows expose**. Mixing them up is the most common first-day mistake.

## 4. Worked example

**Level 0.** You start the server and Studio, log in with the bootstrap admin from your config, and draw a workflow: `HTTP Endpoint` (path `hello`, GET) → `Write HTTP Response` ("Hello from Elsa"). Publishing it makes Elsa register the route, so a browser request to `{Http:BasePath}/hello` creates a workflow instance that returns the text. You then open *Workflow Instances* to see that run.

**Level 1.** The same server now lives inside an MVC app. A C# class `HelloWorkflow : WorkflowBase` describes a few `WriteLine` steps, and a controller action runs it. There is no designer here, which shows that Elsa is a library first and a UI second.

## 5. Common pitfalls

- **Elsa 2 vs Elsa 3.** Elsa 3 is a full rewrite and is not backwards compatible with Elsa 2. Old blog posts and Stack Overflow answers using `Elsa.Server.*` or `IWorkflowLaunchpad` do not apply.
- **Version mismatch.** Studio and Server packages should be the same version (3.9.0 here).
- **Forgetting to publish.** A saved draft does not start from its trigger; publish it.
- **CORS.** A separate Studio origin needs CORS enabled on the server. The permissive dev policy is for development only.
- **No default credentials.** The admin user is created from your configuration on first start; there is no built-in `admin`/`password`.
- **HTTPS certificate.** Run `dotnet dev-certs https --trust` or the browser blocks the calls.

## 6. Self-check questions

1. What is the difference between a workflow definition and a workflow instance?
2. Why does Studio need a `Backend:Url`?
3. What must you do before an `HTTP Endpoint` workflow responds to requests?
4. What is the difference between `/elsa/api` and `Http:BasePath`?
5. Name one reason to define a workflow in C# rather than in Studio.

**Answers.** (1) A definition is the blueprint; an instance is one run with its own state. (2) Studio is only a client; the URL tells it which server API to call. (3) Publish it so its trigger is indexed. (4) The first is the management API; the second prefixes endpoints created by your workflows. (5) It lives in source control, can be code-reviewed and tested, and ships with the app.

## 7. Official docs to read next

- Concepts: https://docs.elsaworkflows.io/getting-started/concepts
- Architecture overview: https://docs.elsaworkflows.io/getting-started/architecture-overview
- Elsa Studio application type: https://docs.elsaworkflows.io/application-types/elsa-studio
- Elsa Server + Studio (WASM): https://docs.elsaworkflows.io/application-types/elsa-server-+-studio-wasm
