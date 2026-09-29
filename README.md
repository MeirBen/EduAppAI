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

The developer harness uses the app's AI engine and validators without creating
learning records or rewriting content at runtime.

```bash
# Preview all 22 synthetic scenarios; no key or API calls.
./scripts/evaluate-ai.sh --case all

# One real template + one task: at most two billable calls.
./scripts/evaluate-ai.sh --live --case reading-grade3 --max-calls 2

# Four calibration controls + template, task and advisory review: seven calls.
./scripts/evaluate-ai.sh --live --case ants-inference --judge --max-calls 7

# Three-repeat reading comparison: 13 base calls plus room for 3 retries.
./scripts/evaluate-ai.sh --live --case reading-grade3 --repeat 3 \
  --judge --max-calls 16

# Repeat the full suite twice: 88 base calls, no retry allowance.
./scripts/evaluate-ai.sh --live --case all --repeat 2 --max-calls 88

# Local developer dashboard; startup makes no AI calls.
./scripts/evaluate-ai.sh --ui

# Compare saved reports offline; no key or provider calls.
./scripts/evaluate-ai.sh --compare baseline/run.json candidate/run.json
```

Maintain synthetic requests in [`cases.json`](tools/FamilyLearning.Evaluation/cases.json).
They cover all three answer types, Hebrew/niqqud/bilingual content, fixed and
configurable templates, empty/false/zero/negative defaults, 1–20 questions,
2–6 choices, two passages, supplied source text and quoted instructions. A full
run plans 44 base calls, or 70 with the four judge controls, before retries.
Select individual cases for focused checks.

`reviewFocus` guides human review, not automatic assertions. The runner uses
generated defaults; `useMaximumQuestionCount` selects the count field's maximum.
Fixtures do not exercise arbitrary input values or repeated tasks from one
template. HTTP, persistence and UI behavior have separate automated tests.

Add a distinct case for a real coverage gap or reported failure; keep its ID stable
and its measurable expectations consistent with the parent request. Do not relax
expectations to hide a model failure. Preview validates all fixtures without AI
calls. Restart `dev.sh` after editing to rebuild its dashboard's fixture copies.
Suite changes require new baseline and candidate runs for direct comparison.

The loopback dashboard runs at `http://127.0.0.1:5180` (change with `--port`).
It shows cases, call budget and nonsecret AI settings. Runs require confirmation;
only one may be active, and Cancel preserves partial results. Invalid AI settings
disable real runs but leave offline features available; fix settings and restart.
Configuration errors stay private.

History shows saved tasks, findings and collapsible request/output data.
**Copy for AI** exports a Markdown brief with requests, outputs, calibration
context and expectations, findings and human reviews, including answer keys.
Review completed results using six scores and notes (up to 4,000 characters).
Reviews save one at a time, update summaries and exports, and preserve other
edits and generated evidence. Run labels (120 characters) and notes (4,000)
identify baselines; CLI equivalents are `--label` and `--notes`.

CLI and dashboard share the evaluator and artifacts. The tool's
[local access protections](docs/architecture.md#ai-and-persistence) are separate
from the production app. Keep artifacts private: they contain prompts and answers.

Billable CLI runs require `--live` and use the app's secrets, environment
overrides, deadline and token cap. Calls run sequentially with a 5-second pause,
including calibration and reviews. Set **Pause between calls** or
`--call-delay-seconds N` to 0–60 (0 disables it). Waiting is cancellable and
excluded from deadlines and latency. Reports record the pause; older reports
used zero. Spacing cannot guarantee provider availability.

Only HTTP 429 is retried, at most three times per stage within the call budget.
Retries honor `Retry-After`, otherwise 5/10/20 seconds plus up to 20% jitter,
with the configured pause as a minimum. A wait over five minutes stops the run.
Superseded attempts are checkpointed in `run.json` and included in call/cost
coverage. Production still makes one call.

`--repeat` accepts 1–5; `--max-calls` accepts 1–100 and must cover the base plan:
`cases × repeats × 2`, or `cases × repeats × 3 + controls` with `--judge`.
Increase the budget to allow retries. Controls load only with `--judge`, so broken
controls do not block basic evaluation. Only valid template/task pairs get reviews.
The budget caps application calls, not currency or provider fallback attempts;
use an OpenRouter key spending limit for a monetary cap. For model comparisons,
disable fallback, check the returned model and change one profile setting at a time.

For prompt experiments, keep the model profile, cases, repeats and judge setup
fixed; change one generation stage at a time and label the candidate run. Compare
automatic failures and manually review the full outputs, including wrong choices.
A prompt's self-check is an instruction, not a deterministic quality guarantee.

Reports go to ignored `artifacts/evaluations/<run>/` or under `--output`:

- **run.json** is the authoritative checkpoint, saved after each call and on
  cancellation. Format 2 captures cases, controls, fixture hashes, exact judge
  prompt/schema/version when judging is enabled, nonsecret profile, engine
  messages, final and rejected outputs, actual models, generation prompt versions,
  finish reasons and usage. Domain rejections include safe `validationErrors`
  with field paths and messages, also shown beside the failed dashboard stage.
  Older reports and non-validation failures have no field diagnostics.
  Secrets, raw provider errors and separate reasoning text are excluded.
- **summary.json** separates stage outcomes, automatic check failures, calibration
  health, generated findings by kind/case, human scores, models and measurements.
  Token and [cost totals][usage-accounting] include known subtotals and missing
  counts; absent measurements mean null. Reasoning is already included in output
  tokens. Average latency includes returned responses, even rejected content,
  and excludes timeouts, cancellation and transport failures.

Interpret the results separately:

- **Code tests:** harness/app behavior with local providers, not model quality.
- **Automatic checks:** contracts, defaults, parameter references, question/choice
  counts and interaction types. A bound question count must be required or have
  a valid default. Passage length is checked only when requested, using whitespace
  word counts and the case's bounds. An exact standalone task title at the start
  of the first block is excluded; other headings count. A zero maximum checks for
  no passage blocks. Counts do not establish source fidelity; review verbatim
  passages manually. `parameterReferences` checks for complete, case-sensitive
  ASCII identifiers, not correct usage or complete instructions. Failed checks
  retain valid templates and continue generation/review to preserve evidence.
- **Calibration:** known defect detection and false alarms, not general accuracy.
  Invalid or unavailable reviews fail calibration but leave detection counts
  unknown, rather than counting unmeasured defects as misses.
- **Generated findings:** exact field, quote, correction, explanation and kind;
  advisory language review, never edits or educational scores. Paths beginning
  with `template.` refer to the reusable blueprint, including its instructions;
  `task.` refers to the generated learner content. Source context comes from the
  captured judge request. Suggestions can also be wrong.
- **Answer positions:** an advisory flags three or more choice answers all using
  the same position. Check whether ordering is intentional; this never reorders
  options or affects automatic scores.
- **Human review:** Hebrew, correctness, age fit, adherence, answer clarity and
  consistency. Enter 0 (unusable), 1 (needs edits), 2 (ready), or null (unreviewed),
  with evidence in notes.

The stateless judge uses the same model. Its [controls](tools/FamilyLearning.Evaluation/hebrew-review-samples.json)
cover template/task defects and clean text, including accepted grammatical variants,
intentional errors and mixed languages. Preserve planted defects when editing;
expected findings are never sent to the judge. Finding paths must identify supplied
fields, quotations must match the source, and kinds must be supported.
Expected defects match whole tokens or short containing phrases; corrections must
remove the offending phrase. This measures detection, not correction quality.
All controls must pass: each expected defect must be found, and extra findings
fail unless the sample permits them. A same-model reviewer can repeat generation
mistakes ([judge limitations][judge-limitations]); human review remains necessary.

Comparison rereads `run.json`, so edited human scores take effect without updating
summary files. It reports profile changes and candidate-minus-baseline deltas,
never a winner or combined score. Direct comparison requires matching suite
hashes, selected cases/order, captured inputs, repeats, automatic-check versions
and judge setup, plus complete stage evidence. Hebrew comparisons also require
passing calibration and matching reviewed cases; human-score deltas require the
same scored case/repetition pairs. Token/cost deltas require full measurement
coverage. Other deltas are null or explicitly qualified. Only format 2 reports
are supported; mismatched embedded controls and invalid human scores are rejected.
Reports retain their recorded check version. Start a new baseline for direct
comparison after changing contracts, checks or fixtures.

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
