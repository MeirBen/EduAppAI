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

Generation uses paid `qwen/qwen3.8-flash`, with no model fallback enabled.
Fund the OpenRouter account before generating; the existing API key still works.
Each template proposal or generated task makes one billable request. Local
automated tests use an isolated provider and consume no OpenRouter credits.
Model changes need configuration only: edit the `Ai` section in
[appsettings.json](backend/FamilyLearning.Api/appsettings.json), or override
settings with `Ai__…` environment variables or `Ai:…` development user secrets,
then restart. `Model` is required when a key is configured; there is no hidden
model default. Check the new endpoint's output format, reasoning and sampling
capabilities against the settings below. Fallback models share those settings.

`Ai:UseJsonSchema=true` requests provider-enforced JSON schemas, supported by
the [Qwen3.8 Flash endpoint](https://openrouter.ai/qwen/qwen3.8-flash).
For an endpoint that supports JSON output without schema enforcement, set it to
`false`. The full schema stays in the prompt in either mode, and the server rejects
invalid output before it can be saved. Changing this setting never disables
server validation.

An optional `Ai__FallbackModel` enables OpenRouter's
[fallback routing](https://openrouter.ai/docs/guides/routing/model-fallbacks);
an empty string disables it. Before enabling one, check its current endpoint
supports the configured output format and reasoning parameters. Free and paid
model IDs are accepted; paid fallbacks also consume credits.
OpenRouter tries a configured fallback for provider errors such as
rate limits or unavailability, not for truncated, malformed or poor-quality output.
`openrouter/free` remains an explicit option for random compatible-model routing.
The actual model is recorded with each result. Prompt guidance helps steer
wording but cannot guarantee fluency. Evaluate educational correctness, Hebrew
agreement, natural phrasing and latency on representative prompts;
schema validation cannot guarantee language or answer quality. Saved tasks
remain readable without AI.

Generation waits up to three minutes. Set `Ai__RequestTimeoutSeconds` (1–300) to
change the deadline, then restart. Requests enable reasoning with
`Ai:ReasoningEnabled=true`; reasoning text is excluded from returned content.
Qwen3.8 Flash supports a [reasoning token budget][reasoning].
`Ai:ReasoningMaxTokens=2048` limits thinking within the 8192-token total, leaving
room for the final JSON. This is an initial app budget, not a measured optimum
for Hebrew. Reasoning tokens are billed even when excluded from the response.
Set `Ai__ReasoningEnabled=false` to disable thinking. To use provider defaults,
clear `Ai:ReasoningMaxTokens` and leave `Ai:ReasoningEffort` empty. For models
that support effort selection, configure an effort instead of a token budget;
the two settings are mutually exclusive. The token budget must be positive and
below the total cap. Review these settings when changing models.
A response stopped by the total cap is rejected with a distinct output-limit
message; nothing is saved.

Sampling uses `Ai:Temperature=1.0`, `Ai:TopP=0.95` and `Ai:TopK=20`, following
the [Qwen model guide][qwen-guide] for thinking mode. Avoid adding repetition
penalties to fix Hebrew: the guide warns that higher presence penalties can
cause language mixing.
`Ai__Temperature` (0–2), `Ai__TopP` (greater than 0 through 1) and positive
`Ai__TopK` override these;
`null` in JSON or an empty environment override omits that parameter so the
provider applies its default. Parameter support varies by endpoint.
Review or clear these model-specific settings
when changing models or returning to automatic routing. Free providers may be
slow, unavailable or rate-limited. The app does not retry failed
calls automatically; OpenRouter owns model and provider routing.

[reasoning]: https://openrouter.ai/docs/guides/best-practices/reasoning-tokens
[qwen-guide]: https://huggingface.co/Qwen/Qwen3.8-Flash-Next#best-practices

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
deployment, verify HTTPS and trusted proxy handling if applicable, persistent
storage permissions, a tested backup/restore procedure and operational monitoring.
`/health` checks process availability, not database or AI readiness. Account
recovery and real-model evaluation remain in the [next steps](docs/product-specification.md#next-steps).
