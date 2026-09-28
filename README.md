# Family Learning

A small, private family learning app. This repository is the first working foundation,
not the complete product in [the product specification](docs/product-specification.md).

**Works now:** parent sign-in, empty template library, multiplication template creation,
dynamic task parameters, immutable template versions, and saved draft previews.

**Next:** child profiles and device activation, assignment, sessions and scoring,
static templates, a child task player, optional AI, and reports.

## Run locally

Prerequisites: .NET SDK **8.0.425**, and Node **24.15+ LTS** or **26+** with npm.
The app was verified with Node 26.5 and npm 12. Dependencies and lockfiles are committed.
Open `FamilyLearning.sln` in your IDE. Both backend projects target `net8.0`, using
ASP.NET Core / EF Core **8.0.31**. `global.json` pins the SDK; CI uses the same version.
This machine also has a repository-local .NET 8 SDK in ignored `.tools/dotnet`.
`scripts/dotnet.sh` uses it when it satisfies `global.json`, otherwise it uses the
system `dotnet`. Your system installation is unchanged.

```bash
cd ~/Desktop/EduApp
npm --prefix frontend ci
./scripts/create-parent.sh
./scripts/dev.sh
```

Open **http://localhost:4200**. Use the parent email and password you just created.
Use `localhost` consistently; switching to `127.0.0.1` creates a different cookie origin.
Press **Ctrl+C** to stop both development servers.

The parent setup command prompts for a password without echoing it. Use at least
12 characters with uppercase, lowercase, a number and a symbol. There is no default
account or public registration endpoint. Each provisioned account owns a separate
family; joining multiple parents to one family is not implemented yet.

The API listens at `http://localhost:5124`. Angular proxies `/api` to it, so cookies
and CSRF protection work on one browser origin. Development applies checked-in EF
migrations automatically. Your templates and tasks start empty.

To debug with two terminals instead:

```bash
./scripts/dotnet.sh watch --project backend/FamilyLearning.Api
```

```bash
npm --prefix frontend start
```

## Check the code

```bash
./scripts/verify.sh
```

This restores locked dependencies, builds .NET, runs SQLite integration/unit tests,
checks formatting, runs Angular tests, and builds the client. No AI keys are required.
The API build also generates and validates XML documentation. Review comment accuracy
using the [commenting guide](docs/commenting-guide.md); formatting cannot verify meaning.

The browser test uses the published application and its own temporary SQLite database:

```bash
./scripts/publish.sh
cd frontend
npx playwright install chromium
npm run e2e
```

It signs in, creates a template and draft, reloads the frozen questions, checks a
360px screen at 200% text size, and signs out. It never touches your local family data.
On Linux CI hosts, `npx playwright install --with-deps chromium` also installs browser libraries.

## Read the code in this order

1. [Architecture guide](docs/architecture.md) — the data flow and decisions.
2. [Template model](backend/FamilyLearning.Api/TaskEngine/Models/TaskTemplateDefinition.cs)
   — what is fixed and what a parent chooses each time.
3. [Parameter validation](backend/FamilyLearning.Api/TaskEngine/Validation/ParameterValidator.cs)
   and [math generation](backend/FamilyLearning.Api/TaskEngine/Generators/MathTaskGenerator.cs)
   — small C# functions without HTTP or database dependencies.
4. [Create-instance endpoint](backend/FamilyLearning.Api/Features/Instances/InstanceEndpoints.cs)
   — load, validate, generate, freeze, save.
5. [Dynamic parameter form](frontend/src/app/dynamic-form/parameter-form/parameter-form.ts)
   — the same metadata becomes a form through Angular signals.
6. [Integration tests](tests/FamilyLearning.Api.Tests/Integration/ParentWorkflowTests.cs)
   — executable examples of the important contracts.

See [the roadmap](docs/roadmap.md) for the next small increments.
Before editing, read the [commenting guide](docs/commenting-guide.md) for C# XML docs,
TypeScript JSDoc, useful inline comments and the review checklist.

## Database and migrations

By default, local development stores SQLite and Data Protection keys in
`backend/FamilyLearning.Api/data/`. The whole directory is ignored by Git.
Set `Storage__Directory` to an absolute directory to use a different location; use
that same value for provisioning, migrations and the running application.

```bash
./scripts/dotnet.sh tool restore
./scripts/dotnet.sh ef migrations add YourChange \
  --project backend/FamilyLearning.Api \
  --output-dir Infrastructure/Persistence/Migrations
```

The EF CLI is pinned to 8.0.31 in `.config/dotnet-tools.json`. To check the model:

```bash
./scripts/dotnet.sh ef migrations has-pending-model-changes --project backend/FamilyLearning.Api
```

The .NET 8 change preserves the initial migration ID, database schema and JSON
snapshots. Existing databases do not need to be reset. Keep their Data Protection
keys alongside the database so existing authentication cookies remain readable.

Review each generated migration before applying it. Keep old template JSON compatible;
`schemaVersion` is the contract version, not the database migration version.

## One-process build

```bash
./scripts/publish.sh
```

The result is `artifacts/app/`: the API and Angular assets in `wwwroot`. Publishing
uses a fresh staging directory and replaces the generated package, preserving an
existing default `artifacts/app/data/` directory. Keep custom persistent storage
outside `artifacts/app`.

This is a framework-dependent build requiring the ASP.NET Core 8 runtime.
For a local same-origin preview on port 5124:

```bash
export Storage__Directory="$PWD/backend/FamilyLearning.Api/data"
ASPNETCORE_ENVIRONMENT=Development ./scripts/dotnet.sh artifacts/app/FamilyLearning.Api.dll \
  --contentRoot "$PWD/artifacts/app" --urls http://localhost:5124
```

For deployment, use **Production**, HTTPS, one application process and persistent
storage for **both** the database and Data Protection keys. Apply migrations explicitly
before starting the application (they do not run automatically in Production):

```bash
Storage__Directory=/absolute/persistent/data ./scripts/dotnet.sh \
  artifacts/app/FamilyLearning.Api.dll --migrate
```

Configure HTTPS in your hosting environment. Do not expose the development server.
Protect the data directory with OS permissions and back it up using SQLite's backup
API or `VACUUM INTO`, not by copying an active database file. Container packaging,
automated backups, password recovery and operational monitoring are still roadmap work.

The PWA caches application assets only. Task data and authenticated API responses
are not cached, and task operations require a connection.
