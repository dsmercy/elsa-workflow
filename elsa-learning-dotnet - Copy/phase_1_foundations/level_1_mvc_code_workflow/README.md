# Phase 1 · Level 1 · Code-first workflow in MVC

## Concept overview

Elsa is a **library first**. In this level the Elsa Server runs *inside* an ordinary ASP.NET Core MVC app. A workflow definition is written in C# (`HelloWorkflow : WorkflowBase`), and an MVC controller runs it with `IWorkflowRunner`. You pass an `Order` in as **workflow input** and read a **workflow output** back. There is no Studio here.

## What's in this folder

```
level_1_mvc_code_workflow/
├── MvcApp/
│   ├── MvcApp.csproj
│   ├── Program.cs                      MVC + AddElsa(...) in one host
│   ├── Workflows/HelloWorkflow.cs      the code-first workflow definition (L1_Hello)
│   ├── Controllers/HomeController.cs   runs the workflow, handles faults/exceptions
│   ├── Models/Order.cs                 course domain record
│   ├── Models/HelloViewModels.cs       form + result view models
│   ├── Views/_ViewImports.cshtml, _ViewStart.cshtml
│   ├── Views/Shared/_Layout.cshtml, Error.cshtml
│   ├── Views/Home/Index.cshtml
│   ├── appsettings.example.json        committed template
│   └── appsettings.json                gitignored; real local values
└── .gitignore
```

## How it flows

```
Browser POST /Home/Run (Product, Amount)
  → HomeController builds an Order (status "created")
  → IWorkflowRunner.RunAsync(new HelloWorkflow(), Input = { "Order": order })
       Sequence:
         WriteLine      "workflow started"            → app terminal
         SetVariable    greeting = "Hello from Elsa! Order …"
         WriteLine      greeting                      → app terminal
         SetOutput      Output["Message"] = greeting
         WriteLine      "workflow finished"           → app terminal
  ← RunWorkflowResult.WorkflowState (Id, Status, SubStatus, Output)
  → Index view shows Message, Status/SubStatus, instance ID
```

## Setup

Prerequisites: the .NET 10 SDK, and `dotnet dev-certs https --trust` (once).

`MvcApp/appsettings.json` was generated with a random signing key and admin password. To recreate it:

```bash
cd MvcApp
cp appsettings.example.json appsettings.json
# replace <random-32+-char-string> (≥ 32 chars) and <local-password>
```

Build:

```bash
cd MvcApp
dotnet build
```

(For reference, this project was scaffolded with `dotnet new web -n MvcApp --framework net10.0`. The MVC parts were then added by hand, so there's no Bootstrap or jQuery.)

## Run

```bash
cd MvcApp
dotnet run --urls https://localhost:5001
```

Stop any level 0 server first, because they share port 5001.

**From Visual Studio:** open `elsa-learning-dotnet.sln` (course root), right-click **MvcApp** → **Set as Startup Project**, and press F5. `Properties/launchSettings.json` pins it to https://localhost:5001. On first start Elsa creates `elsa.sqlite.db` in `MvcApp/` and creates the admin user from `Identity:Bootstrap:*`.

## How to verify

1. Open **https://localhost:5001**.
2. Keep the defaults (`Coffee beans`, `42.50`) and click **Run workflow**.
3. The page shows:
   - **Workflow output "Message"**: `Hello from Elsa! Order <guid> for 'Coffee beans' (42.50) has status 'created'.`
   - **Status / SubStatus**: `Finished / Finished`
   - a **workflow instance ID**, which is different on every submit
4. The terminal running the app prints the three `WriteLine` lines, e.g. `L1_Hello: workflow started.`
5. Submit an empty product or amount `0`. MVC validation stops it before any workflow runs.

Optional, to see the code-first definition through the management API (requires a token):

```bash
# Log in with the bootstrap admin (values from appsettings.json) to get a JWT
curl -k -X POST https://localhost:5001/elsa/api/identity/login \
     -H "Content-Type: application/json" \
     -d '{"username":"admin","password":"<your-password>"}'

# Use the returned accessToken
curl -k https://localhost:5001/elsa/api/workflow-definitions \
     -H "Authorization: Bearer <accessToken>"
# → includes a read-only definition named L1_Hello
```

The login endpoint path is the standard Elsa Identity one. If it differs in your build, check the Elsa Identity docs.

Note that `IWorkflowRunner` executes the instance **in memory**. It is not saved to the instance store, so it won't appear under *Workflow Instances* if you point a Studio at this app. Phase 2 moves to `IWorkflowRuntime`, which does persist instances.

## Lines marked `// VERIFY:`

These use Elsa 3.9.0 API shapes that come from the docs and earlier 3.x releases but could not be compiled here:
- `builder.DefinitionId = ...` in `HelloWorkflow.cs`. If it doesn't compile, delete the line; Elsa then derives the definition ID from the class name.
- `Elsa.Workflows.Options.RunWorkflowOptions` in `HomeController.cs`. If the namespace differs, your IDE's "add using" fix finds it.

## Key takeaway

A workflow is a C# object graph of activities. Any ASP.NET Core app can host the engine and run it, so the designer is optional and the definition can live in source control like any other code.

**Docs pointer:** `../docs/concepts.md` → §2 *Key concepts* (Code-first workflow, Definition vs Instance) and §4 *Worked example — Level 1*.
