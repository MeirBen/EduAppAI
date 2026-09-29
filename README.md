# Family Learning

**Parent prompt → AI template → parent review → saved template → AI tasks.**
Parents reuse a template with different parameter choices. Generated content and
answers are saved snapshots; template edits publish new revisions. Every subject
uses the same flow. See the [core specification](docs/product-specification.md)
for scope and next steps, and [architecture](docs/architecture.md) for
implementation.

## Start

Use **.NET 8 SDK** and a Node version allowed by `engines.node` in
[package.json](frontend/package.json), with npm. CI uses Node 24. `global.json`
selects an installed stable 8.0 SDK. Dependencies and lockfiles pin the stack.

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

The `Ai` section in [appsettings.json](backend/FamilyLearning.Api/appsettings.json)
is the source of truth for the active model and its settings. Switch OpenRouter
chat models by editing this configuration and restarting; no C#, prompt, UI or
test changes are needed. Environment variables (`Ai__…`) and development user
secrets (`Ai:…`) override the file. Review the whole profile when switching so
sampling or reasoning settings from the previous model are not carried over.

Choose settings supported by the model's current OpenRouter endpoint:

| Setting                       | Purpose                                   |
| ----------------------------- | ----------------------------------------- |
| `Model`                       | Required OpenRouter model ID.             |
| `ResponseFormat`              | `json_schema`, `json_object`, or `text`.  |
| `ReasoningEnabled`            | `true`, `false`, or `null` (unspecified). |
| `ReasoningEffort`             | Effort level; `""` omits it.              |
| `ReasoningMaxTokens`          | Thinking budget; `null` omits it.         |
| `Temperature`, `TopP`, `TopK` | Sampling; `null` uses provider defaults.  |
| `MaxOutputTokens`             | Total token cap: 1–32768; default 8192.   |
| `RequestTimeoutSeconds`       | Deadline: 1–300 seconds; default 180.     |
| `FallbackModel`               | Compatible model ID; `""` disables it.    |

Prefer `json_schema` when supported. In `text` mode, JSON is requested through
the prompt only, so malformed output may be more common. Every mode sends the
full schema in the prompt and applies the same strict server validation before
saving. No mode guarantees fluent Hebrew or correct answers; review generated
content before use. Saved tasks remain readable without AI.

For a model without reasoning support, set `ReasoningEnabled` and
`ReasoningMaxTokens` to `null`, and `ReasoningEffort` to `""`; the request will
omit reasoning entirely. Set a budget or an effort, never both; the budget must
be positive and below `MaxOutputTokens`. Either enables reasoning unless
`ReasoningEnabled=false`. Unsupported sampling controls should also be `null`.
An empty environment override omits nullable controls. Temperature accepts 0–2,
top-p greater than 0 through 1, and top-k 0 or higher (0 disables top-k).
These controls follow [OpenRouter's parameter contract][parameters].

Reasoning tokens are billed even when excluded from the response and generally
share the output ceiling. A response stopped by the cap is rejected without
saving. See [reasoning controls][reasoning] for provider differences. Tune budgets
and sampling against representative tasks; model support does not establish an
optimal Hebrew configuration. For example, the [Qwen guide][qwen-guide] recommends
sampling of 1.0/0.95/20 in thinking mode. High presence penalties can cause
language mixing.

The same settings apply to an optional fallback: it must support the selected
output format, reasoning and sampling controls. [Fallback routing][fallback]
handles provider errors such as rate limits, not invalid or low-quality
output. The app makes one call per generation without automatic retries and
records the actual model. It requires support for explicitly requested parameters
instead of silently discarding them. There is no runtime model catalog dependency
or automatic downgrade of output constraints.

Paid models require account credits; your existing OpenRouter key still works.
Local automated tests use a fixed, isolated provider configuration and consume no
credits. Their model IDs and settings are independent of the active model.

[parameters]: https://openrouter.ai/docs/api/reference/parameters
[reasoning]: https://openrouter.ai/docs/guides/best-practices/reasoning-tokens
[qwen-guide]: https://huggingface.co/Qwen/Qwen3.8-Flash-Next#best-practices
[fallback]: https://openrouter.ai/docs/guides/routing/model-fallbacks

## Verify

```bash
./scripts/verify.sh
```

Checks locked restores, .NET builds/tests/XML docs, source/config formatting,
Markdown, shell syntax, browser-test and harness type checks, Angular tests and
production build. TypeScript rejects unused locals/parameters. No AI key is needed.
Test hosts use fixed configuration without file watchers.

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
deployment, set `AllowedHosts` to the real hostnames and verify HTTPS.
Behind a reverse proxy, configure its trusted addresses/networks. Process
forwarded headers before HTTPS redirection, authentication and rate limiting;
follow
[Microsoft's proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).
Also verify storage permissions, backup/restore and operational monitoring.
`/health` checks process availability, not database or AI readiness. Account
recovery and real-model evaluation remain in the [next steps](docs/product-specification.md#next-steps).
