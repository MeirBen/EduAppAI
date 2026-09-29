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

`dev.sh` installs missing client dependencies, runs the API/client reload watchers
and starts the evaluation dashboard at <http://127.0.0.1:5180> without AI calls.
**Ctrl+C** stops all three. Restart after configuration or evaluation-tool changes.
Run `npm --prefix frontend ci` after dependency changes. Angular proxies `/api`
to `http://localhost:5124`. Use `localhost` consistently for cookies. Development
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
saving. See [reasoning controls][reasoning] for provider differences. Token budgets
and the deadline are application cost/latency limits, not vendor quality defaults.
Tune them against representative tasks; a tight thinking budget can reduce quality.
The [Qwen Flash-Next guide][qwen-guide] recommends temperature/top-p/top-k of
1.0/0.95/20 in thinking mode, while the [hosted Flash guide][qwen-hosted] documents
a temperature default of 0.6. These are different baselines, neither a validated
Hebrew optimum. Local quantization and inference-engine reports do not establish
hosted API behavior. Qwen also warns that high presence penalties can mix languages.

The current Qwen profile tests the hosted temperature of 0.6, retaining 4,096
thinking tokens within the 8,192-token total. Compare with temperature 1.0 using
the same prompts, cases, checks and judge controls. [Thinking stops at its
budget][qwen-thinking]; more thinking or lower temperature does not guarantee
better Hebrew. Assess manually reviewed quality alongside latency and cost.

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
[qwen-hosted]: https://docs.qwencloud.com/developer-guides/getting-started/latest-model#thinking
[qwen-thinking]: https://docs.qwencloud.com/developer-guides/text-generation/thinking#token-budget
[fallback]: https://openrouter.ai/docs/guides/routing/model-fallbacks

## Hebrew AI evaluation

The developer harness reuses the app's configured AI engine and validators.
It creates no learning records and performs no runtime proofreading or rewriting.

```bash
# Preview all 24 synthetic scenarios; no key or API calls.
./scripts/evaluate-ai.sh --case all

# One real template + one task: at most two billable calls.
./scripts/evaluate-ai.sh --live --case reading-grade3 --max-calls 2

# Four calibration controls + template, task and advisory review: seven calls.
./scripts/evaluate-ai.sh --live --case ants-inference --judge --max-calls 7

# Three-repeat reading comparison: 13 base calls plus room for 3 retries.
./scripts/evaluate-ai.sh --live --case reading-grade3 --repeat 3 \
  --judge --max-calls 16

# Repeat the full suite twice: at most 96 calls.
./scripts/evaluate-ai.sh --live --case all --repeat 2 --max-calls 96

# Local developer dashboard; startup makes no AI calls.
./scripts/evaluate-ai.sh --ui

# Compare saved reports offline; no key or provider calls.
./scripts/evaluate-ai.sh --compare baseline/run.json candidate/run.json
```

Maintain the synthetic requests in
[`cases.json`](tools/FamilyLearning.Evaluation/cases.json). They cover the three
answer types, Hebrew/niqqud/bilingual content, fixed and configurable templates,
empty/false/zero/negative defaults, 1–20 questions, 2–6 choices, two passages,
supplied source text and quoted instructions. One full run plans at most 48 calls,
or 76 with the four judge controls. Select individual cases for focused checks.

`reviewFocus` guides human review of field design, language, source fidelity and
educational quality; it does not add automatic assertions. The runner uses
generated defaults, except `useMaximumQuestionCount`, which selects the generated
count field's maximum. Arbitrary user-entered values and repeated tasks from the
same template are not exercised by these fixtures. HTTP, persistence and UI
behavior have separate automated tests.

Add a distinct case for a real coverage gap or reported failure; keep its ID stable
and its measurable expectations consistent with the parent request. Do not relax
expectations to hide a model failure. Preview validates all fixtures without AI
calls. Restart `dev.sh` after editing to rebuild its dashboard's fixture copies.
Suite changes require new baseline and candidate runs for direct comparison.

The loopback dashboard opens at `http://127.0.0.1:5180` (`--port` changes the port).
It shows case selection, the call budget and the app's nonsecret AI profile.
Only confirmed runs spend credits; one run may be active, and Cancel keeps partial
results. Missing or invalid AI settings disable real runs while offline features
remain available. Fix the settings and restart; configuration errors stay private.

History renders saved tasks and findings, with collapsible request/output data.
**Copy for AI** copies a Markdown brief of the report for AI agents: context, requests,
outputs, findings and human reviews, including answer keys.
Review completed results using the six scores and notes (up to 4,000 characters).
Saving updates the summary, export and review badges while preserving other form
edits. Reviews save one at a time; generated evidence stays unchanged.
Optional run labels (120 characters) and notes (4,000) help identify baselines;
CLI equivalents are `--label` and `--notes`.

CLI and dashboard share the evaluator and artifacts. The tool's
[local access protections](docs/architecture.md#ai-and-persistence) are separate
from the production app. Keep artifacts private: they contain prompts and answers.

Use `--live` for billable calls. Runs are sequential. A 5-second pause separates
calls, including calibration and reviews.
Change **Pause between calls** in the dashboard or use `--call-delay-seconds N`
(0–60; 0 disables it). Waiting is cancellable and excluded from per-call deadlines
and latency measurements. Reports record the pause; older reports used zero.
Paid providers can still throttle requests; spacing cannot guarantee availability.
HTTP 429 allows up to three retries per stage, within the total call budget.
Set Max calls above the base plan to leave retry headroom. Retries honor
`Retry-After`, or use 5/10/20 seconds plus up to 20% jitter; the configured pause
is also a minimum. A provider wait over five minutes stops the run rather than
retrying early. Other failures are not retried. Every superseded 429 attempt is
checkpointed separately in `run.json`; call and cost coverage include it.
The production application still makes one call.
Runs use the app's secrets, environment overrides, deadline and token cap.
`--repeat` accepts 1–5; `--max-calls` accepts 1–100 and must cover
the plan:
`cases × repeats × 2`, or `cases × repeats × 3 + controls` with `--judge`.
Controls load only with `--judge`; broken controls do not block basic evaluation.
Only successful template/task pairs receive content reviews.
This caps application calls, not currency or fallback attempts. Set an OpenRouter
key spending limit for a monetary cap. Keep fallback empty for model comparisons;
check the actual returned model and change one profile setting at a time.

For prompt experiments, keep the model profile, cases, repeats and judge setup
fixed; change one generation stage at a time and label the candidate run. Compare
automatic failures and manually review the full outputs, including wrong choices.
A prompt's self-check is an instruction, not a deterministic quality guarantee.

Reports go to ignored `artifacts/evaluations/<run>/` or under `--output`:

- **run.json** is the authoritative checkpoint, saved after each call and on
  cancellation. Format 2 captures cases, controls, fixture hashes, exact judge
  prompt/schema/version when judging is enabled, nonsecret profile, engine
  messages, final and rejected outputs, actual models, generation prompt versions,
  finish reasons and usage.
  Secrets, raw provider errors and separate reasoning text are excluded.
- **summary.json** separates stage outcomes, automatic check failures, calibration
  health, generated findings by kind/case, human scores, models and measurements.
  Token and [cost totals][usage-accounting] include known subtotals and missing
  counts; absent measurements mean null. Reasoning is already included in output
  tokens. Average latency includes returned responses, even rejected content,
  and excludes timeouts, cancellation and transport failures.

Interpret the results separately:

- **Code tests:** harness/app behavior with local providers, not model quality.
- **Automatic checks:** app contracts, defaults, parameter references,
  question/choice counts, interaction and whitespace word counts. Generated-passage
  upper bounds allow five extra title words; `verbatim-source` uses an exact
  length, and no-passage cases use zero. `parameterReferences` checks that every
  key occurs as a complete, case-sensitive ASCII identifier in the instructions.
  Presence does not prove correct usage or complete instructions. Failed checks
  retain the template and continue generation/review to preserve evidence.
- **Calibration:** known defect detection and false alarms, not general accuracy.
- **Generated findings:** exact field, quote, correction, explanation and kind;
  advisory language review, never edits or educational scores. Paths beginning
  with `template.` refer to the reusable blueprint, including its instructions;
  `task.` refers to the generated learner content. Suggestions can also be wrong.
- **Human review:** Hebrew, correctness, age fit, adherence, answer clarity and
  consistency. Enter 0 (unusable), 1 (needs edits), 2 (ready), or null (unreviewed),
  with evidence in notes.

The stateless judge uses the same model. Its [controls](tools/FamilyLearning.Evaluation/hebrew-review-samples.json)
cover language defects and clean text, including intentional errors and mixed
languages. Findings must quote an existing field and use a supported kind;
expected defects match whole tokens or short containing phrases. Extra findings
fail controls unless explicitly permitted by the sample. A same-model reviewer
can repeat the generator's mistakes ([judge limitations][judge-limitations]);
retain failed generations and use human review when assessing quality.

Comparison rereads `run.json`, so edited human scores take effect without updating
summary files. It reports profile changes and candidate-minus-baseline deltas,
never a winner or combined score. Direct comparison requires matching suite
hashes, selected cases/order, captured inputs, repeats, automatic-check versions
and judge setup, plus complete stage evidence. Hebrew comparisons also require
passing calibration and matching reviewed cases; human-score deltas require the
same scored case/repetition pairs. Token/cost deltas require full measurement
coverage. Other deltas are null
or explicitly qualified. Only format 2 reports are supported; mismatched embedded
controls and invalid human scores are rejected. Older reports retain their original
checks (version 1); new runs use version 2, adding parameter-reference coverage.

Run exit codes: 0 completed automatic checks and, when enabled, calibration and
reviews passed without findings; 1 failures/findings or stopped run; 2 invalid
arguments/configuration/report or file failure; 130 cancellation. Comparison uses
0 for comparable inputs, 1 for incompatible inputs and 2 for invalid input, never
a quality verdict. CI does not run live evaluation.

[usage-accounting]: https://openrouter.ai/docs/cookbook/administration/usage-accounting
[judge-limitations]: https://arxiv.org/abs/2306.05685

## Verify

```bash
./scripts/verify.sh
```

Checks locked restores, .NET builds/tests/XML docs, source/config formatting,
Markdown, shell syntax, browser-test and harness type checks, isolated dashboard
tests, Angular tests and production build. TypeScript rejects unused
locals/parameters. No AI key is needed.
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
recovery and the child flow remain in the [next steps](docs/product-specification.md#next-steps).
