# Family Learning

A small, private family learning app with a working parent authoring workflow.
The broader destination is described in [the product specification](docs/product-specification.md).

**Works now:** parent sign-in, an empty library, AI template creation from a parent prompt,
editable reusable instructions and dynamic fields, explicit publication and immutable
revisions, generic AI task generation, validation and saved parent previews.

The interface is Hebrew and RTL, using native controls and Tailwind theme tokens.
All subjects use the same AI flow. There are no math-specific or static template modes.
Child accounts, assignment, sessions, scoring and reports are still future work.

## Create a template from your idea

Choose **תבנית חדשה**, describe the learning goal and generate a proposal. Review its
name, instructions and configurable fields; edit them or regenerate before explicitly
saving. Then choose values for this use and create a task. For example, reuse one reading
template with dinosaurs today and space tomorrow, with different difficulty and question
counts. The generated content and parent answer keys are frozen on save.

Use **עריכת התבנית** to publish a new revision. Existing tasks retain their original
content. A two-tab editing conflict preserves local edits until you explicitly reload.
The supported content is plain text passages with numeric, short-text and single-choice
questions. AI cannot introduce new UI controls or executable code.

## Connect OpenRouter

Create an OpenRouter API key in your own account, then run:

```bash
./scripts/configure-ai.sh
```

The prompt hides the key and stores it in .NET development user secrets **outside this
repository**. Restart `scripts/dev.sh` after configuring it. The script does not call AI.
Alternatively supply `Ai__ApiKey` or `OPENROUTER_API_KEY` through your server's secret
configuration; production does not load development user secrets.

The default model is `openrouter/free`. You may set `Ai__Model` to a specific `:free`
model. Paid model IDs are rejected; requests have a 60-second timeout and no automatic
retries. Free-model availability and output quality vary. The app shows missing
configuration or generation failures explicitly and never substitutes fake content.
Read saved tasks without a provider connection. Verify educational correctness in the
parent preview before use. No live AI calls are part of normal tests.

Provider behavior follows [OpenRouter's free router](https://openrouter.ai/docs/guides/routing/routers/free-router)
and [structured-output documentation](https://openrouter.ai/docs/guides/features/structured-outputs).

## Run locally

Prerequisites: a stable **.NET 8 SDK** available as `dotnet`, and Node **24.15+ LTS** or **26+** with npm.
The app was verified with Node 26.5 and npm 12. Dependencies and lockfiles are committed.
Open `FamilyLearning.sln` in your IDE. Both backend projects target `net8.0`, using
ASP.NET Core / EF Core **8.0.31** packages. `global.json` selects the newest installed
stable 8.0 SDK; CI installs the latest 8.0 SDK. Scripts use `dotnet` from your PATH.
The SDK is installed on the machine, outside this repository.

```bash
cd ~/Desktop/EduApp
./scripts/create-parent.sh # First run only
./scripts/dev.sh
```

Open **http://localhost:4200**. Use the parent email and password you just created.
Use `localhost` consistently; switching to `127.0.0.1` creates a different cookie origin.
Leave the script running while you edit. It installs frontend dependencies when the
Angular CLI is missing, starts both servers, and prints their URLs.

- **C#:** `dotnet watch` applies supported edits with Hot Reload and automatically
  restarts the API when an edit requires it. Compilation errors appear in the terminal;
  fix and save to try again.
- **Angular, HTML and styles:** Angular rebuilds and updates the browser on save.
- **Stop:** press **Ctrl+C** once to stop both watchers and their child servers. If
  either watcher exits, the script stops the other one too.

Changes to startup configuration or environment variables can require a manual restart.
After changing frontend dependencies, stop the script, run `npm --prefix frontend ci`,
then start it again. Database model changes still need an EF migration.

The parent setup command prompts for a password without echoing it. Use at least
12 characters with uppercase, lowercase, a number and a symbol. There is no default
account or public registration endpoint. Each provisioned account owns a separate
family; joining multiple parents to one family is not implemented yet.

The API listens at `http://localhost:5124`. Angular proxies `/api` to it, so cookies
and CSRF protection work on one browser origin. Development applies checked-in EF
migrations automatically. Your templates and tasks start empty.

To debug with two terminals instead:

```bash
dotnet watch --project backend/FamilyLearning.Api --launch-profile http
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

It uses a test-only local provider to exercise prompt authoring, review, two different
tasks from one template, frozen previews, revisions, two-tab conflicts and AI failures.
It also checks Hebrew/RTL and keyboard navigation. Screens are checked at 360px with 200% text size. Screenshots are written
to `artifacts/`. It never touches your local family data.
On Linux CI hosts, `npx playwright install --with-deps chromium` also installs browser libraries.

## Read the code in this order

1. [Architecture guide](docs/architecture.md) — the data flow and decisions.
2. [Template model](backend/FamilyLearning.Api/TaskEngine/Models/TaskTemplateDefinition.cs)
   — what is fixed and what a parent chooses each time.
3. [Content validation](backend/FamilyLearning.Api/TaskEngine/Validation/TaskContentValidator.cs)
   and [AI generation](backend/FamilyLearning.Api/TaskEngine/Ai/AiGenerationService.cs)
   — bounded content checks and the generic IChatClient integration without database dependencies.
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
dotnet tool restore
dotnet ef migrations add YourChange \
  --project backend/FamilyLearning.Api \
  --output-dir Infrastructure/Persistence/Migrations
```

The EF CLI is pinned to 8.0.31 in `.config/dotnet-tools.json`. To check the model:

```bash
dotnet ef migrations has-pending-model-changes --project backend/FamilyLearning.Api
```

The AI-only migration removes incompatible prototype learning data (schema version 1)
and obsolete generator fields. Parent accounts and families remain. Deleted learning
data cannot be restored by rolling the migration down. New templates use schema version 2.
Keep Data Protection keys alongside the database so authentication cookies remain readable.

Review each generated migration before applying it. `schemaVersion` is the JSON contract
version, independent of template revision and database migration versions.

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
ASPNETCORE_ENVIRONMENT=Development dotnet artifacts/app/FamilyLearning.Api.dll \
  --contentRoot "$PWD/artifacts/app" --urls http://localhost:5124
```

For deployment, use **Production**, HTTPS, one application process and persistent
storage for **both** the database and Data Protection keys. Apply migrations explicitly
before starting the application (they do not run automatically in Production):

```bash
Storage__Directory=/absolute/persistent/data dotnet \
  artifacts/app/FamilyLearning.Api.dll --migrate
```

Configure HTTPS in your hosting environment. Do not expose the development server.
Protect the data directory with OS permissions and back it up using SQLite's backup
API or `VACUUM INTO`, not by copying an active database file. Container packaging,
automated backups, password recovery and operational monitoring are still roadmap work.

The PWA caches application assets only. Task data and authenticated API responses
are not cached, and task operations require a connection.
