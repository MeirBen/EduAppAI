# Family Learning

**Parent prompt → AI template → parent review → saved template → AI tasks.**
Parents reuse a template with different parameter choices. Generated content and
answers are saved snapshots; template edits publish new revisions. Every subject
uses the same flow. See the [core specification](docs/product-specification.md)
for scope and next steps, and [architecture](docs/architecture.md) for
implementation.

## Start

Use **.NET 8 SDK** and **Node 24.15+ LTS or 26+** with npm. `global.json`
selects an installed stable 8.0 SDK. Dependencies and lockfiles pin the
application stack.

```bash
./scripts/create-parent.sh # First run only
./scripts/configure-ai.sh  # Enter your OpenRouter key
./scripts/dev.sh
```

Open <http://localhost:4200>. The parent password is hidden during setup and
must have 12–256 characters with upper/lowercase, a number and a symbol. Each
provisioned parent gets a family. There is no default account or public
registration.

`dev.sh` installs missing client dependencies and runs both reload watchers.
**Ctrl+C** stops both. Restart after configuration changes; run
`npm --prefix frontend ci` after dependency changes. Angular proxies `/api` to
`http://localhost:5124`. Use `localhost` consistently for cookies. Development
applies migrations automatically and starts with an empty learning library.

## AI configuration

`configure-ai.sh` stores the key in .NET development user secrets outside the
repo; it makes no AI call. Restart the server afterward. Alternatively provide
`Ai__ApiKey` or `OPENROUTER_API_KEY` through server secrets. Production does not
load development user secrets.

The default model is [Qwen3.8 27B (free)](https://openrouter.ai/qwen/qwen3.8-27b:free),
which supports structured output and optional reasoning. To change it, set
`Ai__Model` to an available `:free` model (or `Ai:Model` in development user
secrets), then restart. `openrouter/free` is also accepted but randomly selects
an eligible model. Paid models are rejected. Choose an endpoint that supports
JSON-schema structured output and the requested parameters; popularity does not
measure suitability. Qwen is a configurable baseline, not a proven Hebrew winner.
Evaluate fluency, educational correctness and latency on representative prompts;
schema validation cannot guarantee language or answer quality. Saved tasks
remain readable without AI.

Generation waits up to three minutes. Set `Ai__RequestTimeoutSeconds` (1–300) to
change the deadline, then restart. Reasoning is disabled by default to reduce
latency; this can trade away accuracy on complex tasks. To evaluate thinking
with Qwen, set `Ai__ReasoningEnabled=true`, `Ai__ReasoningEffort=low`,
`Ai__Temperature=1` and `Ai__TopP=0.95`. Reasoning effort is model-specific, not
a hard token budget. Check the selected model's supported levels before changing
it; low effort is not universally supported.

The default non-thinking sampling settings are temperature `0.7` and top-p `0.8`,
following [Qwen's guidance](https://huggingface.co/Qwen/Qwen3.8-27B#best-practices).
Both are configurable through the environment settings above; null in JSON
omits the parameter. Temperature accepts 0–2 and top-p accepts greater than 0
through 1. Sampling belongs to the provider configuration, not the task engine.
Free providers may be slow, unavailable or rate-limited; requests are not
automatically retried or switched to another model.

## Verify

```bash
./scripts/verify.sh
```

Checks locked restores, .NET builds/tests/XML docs, source/config formatting,
Markdown, shell syntax, browser-test and harness type checks, Angular tests and
production build. TypeScript rejects unused locals/parameters. No AI key is needed.

For the isolated browser workflow:

```bash
./scripts/publish.sh
cd frontend
npx playwright install chromium
npm run e2e
```

Tests use disposable data and a local AI provider. They cover authoring, reuse,
immutable tasks, conflicts, failures, keyboard/RTL behavior and 360px/200% text.
Screenshots go to `artifacts/`. On Linux CI, install browser libraries with
`npx playwright install --with-deps chromium`.

Before editing, read the [comment rules](docs/commenting-guide.md) and
[UI guide](docs/ui-guide.md). Open `FamilyLearning.sln` for backend development.

## Data

Remove saved drafts and templates from the library. **איפוס נתוני הלמידה**
(reset learning data) clears your family's templates, revisions and drafts after
confirmation; your login and AI configuration remain. This also removes old
content retained from earlier development.

Development stores SQLite and Data Protection keys in
`backend/FamilyLearning.Api/data/`, ignored by Git. Set `Storage__Directory` to
an absolute path to relocate them; use the same path for provisioning,
migrations and runtime. Retain keys with the database so cookies survive
restarts.

```bash
dotnet tool restore
dotnet ef migrations add YourChange \
  --project backend/FamilyLearning.Api \
  --output-dir Infrastructure/Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project backend/FamilyLearning.Api
```

Stop `dev.sh` before creating or editing migrations: its reload watcher can apply
an unfinished migration. Once applied, keep migration files unchanged and add a
new migration for corrections.

Review migrations before applying them. Migrations can remove incompatible
learning data; rolling back cannot restore deleted content. Back up using
SQLite's backup API or `VACUUM INTO`, not by copying an active database file.

## Publish

```bash
./scripts/publish.sh
```

`artifacts/app/` contains the API and client and requires the ASP.NET Core 8
runtime. Publishing preserves default `artifacts/app/data/`; keep custom
persistent storage outside the package. For a local preview:

```bash
export Storage__Directory="$PWD/backend/FamilyLearning.Api/data"
ASPNETCORE_ENVIRONMENT=Development dotnet artifacts/app/FamilyLearning.Api.dll \
  --contentRoot "$PWD/artifacts/app" --urls http://localhost:5124
```

Deployment uses **Production**, HTTPS, one process and persistent storage for
both database and keys. Restrict data-directory permissions. Apply migrations
explicitly before starting; Production does not apply them automatically:

```bash
Storage__Directory=/absolute/persistent/data dotnet \
  artifacts/app/FamilyLearning.Api.dll --migrate
```

The PWA caches assets only; task operations require a connection. Before a live
deployment, verify HTTPS and trusted proxy handling if applicable, persistent
storage permissions, a tested backup/restore procedure and operational monitoring.
`/health` checks process availability, not database or AI readiness. Account
recovery and real-model evaluation remain in the [next steps](docs/product-specification.md#next-steps).
