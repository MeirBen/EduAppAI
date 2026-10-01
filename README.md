# Family Learning

**Parent prompt → editable plan → generated activity → parent review → frozen
snapshot.**
Generate without saving a template, edit the content or replace one question,
then save the draft and mark the reviewed revision ready. Saving a reusable
template is a separate action; later template edits publish immutable versions
and never change existing activities. See the [core
specification](docs/product-specification.md) for scope
and next steps, and [architecture](docs/architecture.md) for implementation.

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

`dev.sh` installs missing client dependencies, runs the API/client reload
watchers
and starts the evaluation dashboard at <http://127.0.0.1:5180> without AI calls.
**Ctrl+C** stops all three. Restart after configuration or evaluation-tool
changes.
Run `npm --prefix frontend ci` after dependency changes. Angular proxies `/api`
to `http://localhost:5124`. Use `localhost` consistently for cookies.
Development
applies the current initial migration automatically on an empty database. The
content-first cutover uses a fresh database; see [Data](#data) for local setup.

## AI configuration

`configure-ai.sh` stores the key in .NET development user secrets outside the
repo; it makes no AI call. Restart the server afterward. Alternatively provide
`Ai__ApiKey` or `OPENROUTER_API_KEY` through server secrets. Production does not
load development user secrets.

The `Ai` section in
[appsettings.json](backend/FamilyLearning.Api/appsettings.json)
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
| `MaxRequestBytes`             | Compiled HTTP body cap; at most 512 KiB.  |
| `MaxSchemaBytes`              | Output schema cap; at most 64 KiB.        |
| `FallbackModel`               | Compatible model ID; `""` disables it.    |
| `IgnoredProviders`            | Provider slugs to exclude from routing.   |

Prefer `json_schema` when supported. In `text` mode, JSON is requested through
the prompt only, so malformed output may be more common. Every mode sends the
full schema in the prompt and applies the same strict server validation before
saving. Schema mode sends the same constraints to the provider without SDK
rewriting. The endpoint must support those schema keywords; use `json_object`
for providers with a more limited schema implementation.
No mode guarantees fluent Hebrew or correct answers; review generated
content before use. Saved tasks remain readable without AI.

`IgnoredProviders` uses OpenRouter's native `provider.ignore` routing option.
It accepts up to 16 slugs, each 1–64 lowercase letters, digits, hyphens,
underscores or slashes. An empty array excludes no providers. Edit the array and
restart to change this operational choice. The exclusion also applies to fallback
models and is captured in evaluation reports
and durable-work profile fingerprints. It does not retry or repair responses.

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
saving. See [reasoning controls][reasoning] for provider differences. Token
budgets
and the deadline are application cost/latency limits, not vendor quality
defaults.
Tune them against representative tasks; a tight thinking budget can reduce
quality.

Check [OpenRouter's model metadata][model-metadata] and the serving provider's
documentation when changing profiles. Reasoning effort is not a hard token
budget;
it does not reserve room for final JSON. Keep the active values in
`appsettings.json`
and evaluate them with representative cases. Changing the model also changes the
evaluation judge, so repeat calibration and manual review.

The same settings apply to an optional fallback: it must support the selected
output format, reasoning and sampling controls. [Fallback routing][fallback]
handles provider errors such as rate limits, not invalid or low-quality
output. The app makes one call per applicable stage without automatic retries
and
records the actual model. It requires support for explicitly requested
parameters
instead of silently discarding them. There is no runtime model catalog
dependency
or automatic downgrade of output constraints.

Paid models require account credits. Automated tests use an isolated provider
configuration, independently of the active model, and consume no credits.

[parameters]: https://openrouter.ai/docs/api/reference/parameters
[reasoning]: https://openrouter.ai/docs/guides/best-practices/reasoning-tokens
[model-metadata]: https://openrouter.ai/api/v1/models
[fallback]: https://openrouter.ai/docs/guides/routing/model-fallbacks

## Hebrew AI evaluation

The developer harness uses the app's AI engine and validators without creating
learning records or rewriting content at runtime.

```bash
# Preview all synthetic scenarios; no key or API calls.
./scripts/evaluate-ai.sh --case all

# Interpretation, generated materials and questions: three base calls.
./scripts/evaluate-ai.sh --live --case reading-grade3 --max-calls 3

# Four calibration controls + interpretation, materials, questions and review.
./scripts/evaluate-ai.sh --live --case ants-inference --judge --max-calls 8

# Three-repeat reading comparison: 16 base calls plus room for 3 retries.
./scripts/evaluate-ai.sh --live --case reading-grade3 --repeat 3 \
  --judge --max-calls 19

# Local developer dashboard; startup makes no AI calls.
./scripts/evaluate-ai.sh --ui

# Compare saved reports offline; no key or provider calls.
./scripts/evaluate-ai.sh --compare baseline/run.json candidate/run.json
```

Maintain synthetic requests in
[`cases.json`](tools/FamilyLearning.Evaluation/cases.json).
They cover all three answer types, Hebrew/niqqud/bilingual content, requested
controls, shared-setting overrides, supplied sources, refinement and scoped
replacement. A case supplies a prompt or a fixed `initialPlan`. Prompts permit
up to three `refinements` of 4,000 characters each; fixed plans omit authoring
and
may supply `initialInput`. Optional `replacements` identify a material or
question
by zero-based fixture index, resolved to its application-owned ID.

Preview sums applicable stages: interpretation/refinements, generated materials
when needed, questions, explicit replacements and optional review/calibration.
For authored plans, `expectedGeneratedMaterials` determines the material-call
estimate and independently checks adherence. A model may deviate; every actual
attempt still consumes the hard call limit. Supplied sources skip material AI.

`reviewFocus` guides human review, not automatic assertions.
`settingsOverride` supplies all four per-task settings; its question count must
match the case. `additionalControlCount` checks requested controls across all
scopes, excluding shared settings and native material choices.
`expectedLength` independently checks the requested plan constraint (the
aggregate
constraint for multiple generated materials). `minPassageWords` and
`maxPassageWords` check the assembled bodies with the engine's shared
`TextLength` rules. A dropped authoring demand fails adherence even if the
weaker plan validates.

Add a distinct case for a real coverage gap or reported failure; keep its ID
stable
and its measurable expectations consistent with the parent request. Do not relax
expectations to hide a model failure. Preview validates all fixtures without AI
calls. Restart `dev.sh` after editing to rebuild its dashboard's fixture copies.
Suite changes require new baseline and candidate runs for direct comparison.

The loopback dashboard runs at `http://127.0.0.1:5180` (change with `--port`).
It shows cases, call budget and nonsecret AI settings. Runs require
confirmation;
only one may be active, and Cancel preserves partial results. Invalid AI
settings
disable real runs but leave offline features available; fix settings and
restart.
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
from the production app. Keep artifacts private: they contain prompts and
answers.

Billable CLI runs require `--live` and use the app's secrets, environment
overrides, deadline and token cap. Calls run sequentially with a 5-second pause,
including calibration and reviews. Set **Pause between calls** or
`--call-delay-seconds N` to 0–60 (0 disables it). Waiting is cancellable and
excluded from deadlines and latency. Reports record the pause; spacing cannot
guarantee provider availability.

Only HTTP 429 is retried, at most three times per stage within the call budget.
Retries honor `Retry-After`, otherwise 5/10/20 seconds plus up to 20% jitter,
with the configured pause as a minimum. A wait over five minutes stops the run.
Superseded attempts are checkpointed in `run.json` and included in call/cost
coverage. Production still makes one call per stage.

`--repeat` accepts 1–5; `--max-calls` accepts 1–100 and must cover the base
plan:
sum of applicable case stages × repeats, plus one review per case/repeat
and the once-per-run calibration controls when `--judge` is enabled.
Increase the budget to allow retries. Controls load only with `--judge`, so
broken
controls do not block basic evaluation. Assembled results receive reviews even
when independent case checks fail.
The budget caps application calls, not currency or provider fallback attempts;
use an OpenRouter key spending limit for a monetary cap. For model comparisons,
disable fallback, check the returned model and change one profile setting at a
time.

For prompt experiments, keep the model profile, cases, repeats and judge setup
fixed; change one generation stage at a time and label the candidate run.
Compare
automatic failures and manually review the full outputs, including wrong
choices.
A prompt's self-check is an instruction, not a deterministic quality guarantee.

Reports go to ignored `artifacts/evaluations/<run>/` or under `--output`:

- **run.json** is the authoritative checkpoint, saved after each call and on
  cancellation. The current format captures cases, resolved inputs, controls,
  fixture hashes, stage roles, engine/schema versions, exact requests and schemas
  with hashes, normalized plans, source revisions, assembled documents, explicit
  skips and candidate acceptance/application. It retains the judge prompt/version,
  nonsecret profile, final and rejected outputs, actual models/providers when
  known,
  finish reasons and usage. Domain rejections include safe `validationErrors`
  with field paths and messages, also shown beside the failed dashboard stage.
  Non-validation failures have no field diagnostics.
  Secrets, raw provider errors and separate reasoning text are excluded.
- **summary.json** separates stage outcomes, automatic check failures,
  calibration
  health, generated findings by kind/case, human scores, models and measurements.
  Token and [cost totals][usage-accounting] include known subtotals and missing
  counts; absent measurements mean null. Reasoning is already included in output
  tokens. Average latency includes returned responses, even rejected content,
  and excludes timeouts, cancellation and transport failures.

Interpret the results separately:

- **Code tests:** harness/app behavior with local providers, not model quality.
- **Automatic checks:** separate interpretation, generation, replacement and
  end-to-end readiness. Shared engine validation enforces exact/range lengths;
  targets remain advisory. Independent case checks cover requested constraints,
  control counts, question/choice counts and formats. Word counts include headings
  inside material bodies and exclude punctuation-only tokens. Material titles,
  instructions, questions and answers are outside body counts. The historical
  100–150 range keeps its original bounds. Failed independent checks retain valid
  content for human and language review.
- **Calibration:** known defect detection and false alarms, not general
  accuracy.
  Invalid or unavailable reviews fail calibration but leave detection counts
  unknown, rather than counting unmeasured defects as misses.
- **Generated findings:** exact field, quote, correction, explanation and kind;
  advisory language review, never edits or educational scores. Paths beginning
  with `plan.` refer to the learning plan;
  `document.` refers to assembled learner content. Source context comes from the
  captured judge request. Suggestions can also be wrong.
- **Answer positions:** an advisory flags three or more choice answers all using
  the same position. Check whether ordering is intentional; this never reorders
  options or affects automatic scores.
- **Human review:** Hebrew, correctness, age fit, adherence, answer clarity and
  consistency. Enter 0 (unusable), 1 (needs edits), 2 (ready), or null
  (unreviewed),
  with evidence in notes. Optional parent correction counts and time-to-ready
  seconds retain measured effort, including unsuccessful trials; missing is
  unknown.

The stateless judge uses the same model. Its
[controls](tools/FamilyLearning.Evaluation/hebrew-review-samples.json)
cover plan/document defects and clean text, including accepted grammatical
variants,
stray answer prefixes, meaningful punctuation, intentional errors and mixed
languages.
Preserve planted defects when editing;
expected findings are never sent to the judge. Finding paths must identify
supplied
fields, quotations must match the source, and kinds must be supported.
Expected defects match whole tokens or short containing phrases; corrections
must
remove the offending phrase. This measures detection, not correction quality.
All controls must pass: each expected defect must be found, and extra findings
fail unless the sample permits them. A same-model reviewer can repeat generation
mistakes ([judge limitations][judge-limitations]); human review remains
necessary.

Comparison rereads `run.json`, so edited human scores take effect without
updating
summary files. It reports profile changes and candidate-minus-baseline deltas,
never a winner or combined score. Direct comparison requires matching suite
hashes, selected cases/order, captured inputs, repeats, automatic-check versions
plus complete workflow evidence. Generator deltas additionally require matched
effective inputs, settings and supplied sources; engine revision alone does not
invalidate an experiment. Authoring comparisons require matched refinement
sequences. Judge model/profile/prompt/rubric, review coverage or calibration
mismatches suppress judge-quality deltas independently. Human-score deltas
require the same scored case/repetition pairs. Resource deltas require full
measurement coverage and a comparable judge workload because they include review
calls. Other deltas are null or explicitly qualified. Only the current format
owned by [EvaluationVersions] is supported; mismatched embedded controls and
invalid human scores are rejected.

[EvaluationVersions]: tools/FamilyLearning.Evaluation/EvaluationVersions.cs

Reports require their recorded check version, call delay and calibration results
(an empty array when unused). Start a new baseline for direct comparison after
changing contracts, checks or fixtures.

Run exit codes: 0 completed automatic checks and, when enabled, calibration and
reviews passed without findings; 1 failures/findings or stopped run; 2 invalid
arguments/configuration/report or file failure; 130 cancellation. Comparison
uses
0 for comparable inputs, 1 for incompatible inputs and 2 for invalid input,
never
a quality verdict. CI does not run live evaluation.

[usage-accounting]: https://openrouter.ai/docs/cookbook/administration/usage-accounting
[judge-limitations]: https://arxiv.org/abs/2306.05685

## Cutover evidence

The [approved
decision](docs/superpowers/specs/2026-10-01-content-first-cutover-decision.md)
accepts the measured reading cost, latency and strict-length reliability
tradeoff
for source preservation, editing and recovery. It makes no claim of improved
Hebrew quality; human quality review remains incomplete. The original comparison
artifacts remain private under
`artifacts/evaluations/task2-structured-2026-10-01/`.
The temporary one-shot experiment and its CLI switches have been removed.
New runs use the supported workflow and current report format.

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

Tests use disposable data and a local AI provider. They exercise the published
app's complete activity lifecycle, independent template
publication, immutable previews, source fidelity, strict blockers, recovery,
conflicts, keyboard/RTL behavior and 360px/200% text. Screenshots go to the
ignored
`.superpowers/sdd/2026-09-30-structured-templates/` verification workspace. On
Linux CI, install browser libraries with
`npx playwright install --with-deps chromium`.

Before editing, read the [comment rules](docs/commenting-guide.md) and
[UI guide](docs/ui-guide.md). Open `FamilyLearning.sln` for backend development.

## Data

Delete individual drafts, templates or frozen snapshots from the library.
Their contents are independent: deleting a template does not erase its
activities. **איפוס נתוני הלמידה**
(reset learning data) clears your family's templates, versions, drafts,
operations
and snapshots, including items beyond the 100-item list limits, after
confirmation; your login and AI configuration remain.

Development stores SQLite and Data Protection keys in
`backend/FamilyLearning.Api/data/`, ignored by Git. Set `Storage__Directory` to
an absolute path to relocate them; use the same path for provisioning,
migrations and runtime. Retain keys with the database so cookies survive
restarts.

The cutover replaces all prototype migrations with one `InitialCreate` baseline;
it has no old-schema upgrade path. For an existing prototype installation, stop
this project's watchers/connections, remove only its configured
`family-learning.db` and matching `-wal`, `-shm` and `-journal` files, then run
`./scripts/create-parent.sh`. Keep configuration, credentials and Data
Protection
keys. This reset also removes local accounts. Automated tests use disposable
storage and never reset your database.

For subsequent model changes:

```bash
dotnet tool restore
dotnet ef migrations add YourChange \
  --project backend/FamilyLearning.Api \
  --output-dir Infrastructure/Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project
backend/FamilyLearning.Api
```

Stop `dev.sh` before creating or editing migrations: its reload watcher can
apply
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
recovery and the child flow remain in the [next
steps](docs/product-specification.md#next-steps).
