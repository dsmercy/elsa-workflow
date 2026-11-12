# Phase 1 · Level 0 · Server and Studio

## Concept overview

Elsa has two halves. The **Elsa Server** (`ElsaServer/`) is an ASP.NET Core app that runs workflows, stores them in SQLite, and exposes a REST API. **Elsa Studio** (`ElsaStudio/`) is a Blazor WebAssembly app that only calls that API. In this level you draw a workflow in Studio, publish it, and call the HTTP endpoint it creates.

## What's in this folder

```
level_0_server_and_studio/
├── ElsaServer/                  Elsa Server (engine + REST API + SQLite)
│   ├── ElsaServer.csproj
│   ├── Program.cs
│   ├── appsettings.example.json committed template
│   └── appsettings.json         gitignored; your real local values
├── ElsaStudio/                  Elsa Studio (Blazor WASM designer)
│   ├── ElsaStudio.csproj
│   ├── Program.cs               (no App.razor: `App` comes from Elsa.Studio.Shell)
│   └── wwwroot/
│       ├── index.html           official Elsa Studio 3.9.0 WASM template
│       ├── appsettings.example.json
│       └── appsettings.json     gitignored; holds no secrets (it is served to the browser)
├── workflows/L0_Hello.json      optional import of the workflow you'll build
└── .gitignore
```

## The two URLs (they are different things)

| URL | What it is | Who uses it |
|-----|------------|-------------|
| `https://localhost:5001/elsa/api` | **Management API** (definitions, instances, login) | Studio (`Backend:Url`) |
| `https://localhost:5001/workflows/...` | **Workflow endpoints**: `Http:BasePath` + the path set on an *HTTP Endpoint* activity | You / any HTTP client |

So the workflow you build below answers at **`https://localhost:5001/workflows/hello`**, not under `/elsa/api`.

## Setup

Prerequisites: the .NET 10 SDK, and the dev certificate trusted once:

```bash
dotnet --version              # should print 10.x
dotnet dev-certs https --trust
```

### 1. Server config

`ElsaServer/appsettings.json` was generated with a random 48-character signing key and a random admin password. Open it and note `Identity:Bootstrap:Password`, which you need to log in. To recreate it:

```bash
cd ElsaServer
cp appsettings.example.json appsettings.json
# then replace <random-32+-char-string> and <local-password>
```

You can override any value with an environment variable instead, e.g. `Identity__Bootstrap__Password`.

### 2. Studio config

`ElsaStudio/wwwroot/appsettings.json` already exists. Recreate it with `cp wwwroot/appsettings.example.json wwwroot/appsettings.json`. It contains no secrets.

### 3. Restore and build

```bash
cd ElsaServer && dotnet build && cd ..
cd ElsaStudio && dotnet build && cd ..
```

(For reference, these projects were scaffolded from `dotnet new web -n ElsaServer --framework net10.0` and `dotnet new blazorwasm -n ElsaStudio --framework net10.0`. The template's `Pages/`, `Layout/`, `App.razor`, `_Imports.razor` and `wwwroot/css` were removed, as the official guide instructs.)

## Run

Use two terminals and start the server first:

```bash
# Terminal 1
cd ElsaServer
dotnet run --urls https://localhost:5001
```

On first start, Elsa creates `elsa.sqlite.db`, applies its migrations, and creates the admin user from `Identity:Bootstrap:*`.

```bash
# Terminal 2
cd ElsaStudio
dotnet run --urls https://localhost:6001
```

Open **https://localhost:6001** and log in with the `UserName` / `Password` from `ElsaServer/appsettings.json`.

### Or from Visual Studio

Open `elsa-learning-dotnet.sln` (course root). Right-click the solution → **Configure Startup Projects…** → **Multiple startup projects** → set **ElsaServer** and **ElsaStudio** to *Start* with ElsaServer first. Press F5. Each project's `Properties/launchSettings.json` pins the ports to 5001 and 6001. Leave **MvcApp** (level 1) set to *None*, because it also uses port 5001.

## Studio steps: build `L0_Hello`

1. In the left menu, open **Workflows → Definitions** and click **Create workflow**. Name it `L0_Hello` and click **OK**/**Create**.
2. From the **Activities** toolbox (left side of the designer), open the **HTTP** category and drag **HTTP Endpoint** onto the canvas.
3. Select it. In the property panel at the bottom:
   - **Path**: `hello` (relative to `Http:BasePath`; no `/workflows` prefix)
   - **Supported methods**: `GET`
   - Make sure **Trigger workflow** / *Can start workflow* is checked. It is on by default for a first HTTP Endpoint.
4. Drag **Write HTTP Response** (also under **HTTP**) onto the canvas.
   - **Content**: `Hello from Elsa`
   - **Status code**: `OK`
5. Connect the **Done** port of *HTTP Endpoint* to the input of *Write HTTP Response*.
6. Click **Publish** (top right). **Save** alone creates a *draft*, which gets no route.
7. The **definition ID** appears in the URL (`/workflows/definitions/<definitionId>/edit`) and in the workflow's **Properties/Info** tab. Level 0 doesn't need it; level 2 does.

**Alternative:** in **Workflows → Definitions**, use **Import** and choose `workflows/L0_Hello.json`, then open the workflow and click **Publish**. This file was written by hand in the Elsa 3 export format, not exported from a running 3.9.0 Studio. If the import complains, build the workflow by hand with the steps above. That is the point of this level anyway.

## How to verify

```bash
curl -k https://localhost:5001/workflows/hello
# → Hello from Elsa
```

Or open https://localhost:5001/workflows/hello in the browser.

Then go back to Studio, open **Workflows → Instances**, and you should see one **Finished** instance of `L0_Hello` per request. Click one to see the executed activities and their journal.

Things to try:
- Change the text, click **Save** without publishing, and call the URL again. You still get the *old* text, because only the published version runs.
- Call `https://localhost:5001/elsa/api/workflow-definitions` without a token. You get **401**: the management API is protected, while the workflow endpoint (`Authorize = false`) is not.

## Troubleshooting

| Symptom | Likely cause |
|---------|--------------|
| Server won't start: `Identity:Bootstrap:UserName is required` | `appsettings.json` missing; copy it from the example |
| Studio login fails / network errors in the browser console | Server not running, `Backend:Url` wrong, or the dev cert isn't trusted |
| CORS error in the browser console | Server not started with the CORS policy, or `app.UseCors()` was moved after `MapWorkflowsApi()` |
| `/workflows/hello` returns 404 | Workflow not **published**, or you set Path to `/workflows/hello` (BasePath is added for you) |

## Key takeaway

Studio is only a client of the server's `/elsa/api`. A workflow's HTTP Endpoint trigger gets a route under `Http:BasePath` only once that workflow is **published**.

**Docs pointer:** `../docs/concepts.md` → §3 *How the pieces fit together* (the two URLs) and §4 *Worked example — Level 0*.
