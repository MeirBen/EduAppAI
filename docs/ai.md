# AI guide

Current decisions and accumulated evidence, consolidated on 1 October 2026.
This is the maintained reference for AI configuration, evaluation and tuning.
Product contracts live in the [product specification](product-specification.md);
implementation boundaries live in [architecture](architecture.md).

## Current status

**Tuning is closed. AI quality is not fully passing, and superiority to the old
one-shot approach has not been established.** Local code tests passing does not
mean generated Hebrew, answer keys or educational content are consistently good.

- Keep Gemini 3.8 Flash with medium reasoning and provider-default sampling.
  Low reasoning was faster and cheaper, but failed confirmation quality checks.
- Keep the earlier material instructions and clearer authoring-field descriptions:
  they improved sampled contract adherence on DeepSeek. The latest shared material
  replacement instructions are a cleanup with comparable reviewed quality.
- Reject both question-prompt experiments. Neither earned adoption.
- Authoring can return an empty plan despite strict schema mode. Validation
  rejects it safely; the cause remains unresolved.
- Keep parent review, strict validation and explicit recovery. No automatic
  output repair, weakened tests, model-specific branch or production retry.

The next useful investigation is the [empty-plan failure](#unresolved-failures),
followed by human review of representative Hebrew activities. More open-ended
prompt editing is not currently justified. Further paid experiments need an
explicit scope and call/cost budget; an unused allowance is not a reason to run.

## Design and cutover decision

The deployed flow is prompt → editable plan → applicable material generation →
questions → editable saved draft → parent review → immutable snapshot. Template
publication is independent. Supplied sources are assembled verbatim by the app;
question-only and supplied-source activities skip material generation. Scoped
replacement preserves unrelated content. Replacing material makes dependent
questions stale instead of silently regenerating them.

On 1 October the owner approved cutover for source preservation, editing and
checkpoint recovery, accepting the original trial's reliability/cost/latency
tradeoff. The original comparative threshold was **not met**; this was an explicit
value decision, not evidence of better Hebrew. The former one-shot implementation,
staged route compositions, old schemas and prototype migrations were removed.
One production lifecycle and the current evaluation format remain, without
compatibility readers. Development database setup is in [README](../README.md#data).

The original comparison used three fixed cases, three repetitions per variant,
matched model/settings and no retries/repairs. One-shot passed 9/9 structural
trials; split passed 7/9. Generated reading passed 3/3 versus 1/3, with split at
about 3× median cost and 3.4× median provider latency. Exact supplied sources and
isolated recovery tests demonstrated the control benefits. Human review was
unscored. Later trials below do not rewrite that decision or its evidence.

## Configuration

[appsettings.json](../backend/FamilyLearning.Api/appsettings.json) is the active
profile's source of truth. `scripts/configure-ai.sh` stores the key in development
user secrets outside the repo without a call. `Ai__ApiKey` or
`OPENROUTER_API_KEY` can supply server secrets; Production does not load development
user secrets. Environment variables (`Ai__…`) and development secrets (`Ai:…`)
override the file. Restart after changes. Credentials never belong in reports.

Current profile: `google/gemini-3.8-flash`, `json_schema`, reasoning enabled at
`medium`, no fixed reasoning-token budget, no temperature/top-p/top-k overrides,
16,384 requested output tokens, 180-second deadline, no fallback model and no
excluded providers. The compiled request ceiling is 512 KiB; the schema ceiling
is 64 KiB. The output character limit is separately enforced by the engine.

Medium reasoning and omitted sampling follow the reviewed [Google guidance][gemini]
and [OpenRouter reasoning mapping][reasoning]. These are a starting profile, not
a guarantee of quality. The matched reasoning experiment below supports retaining
medium over the tested low candidate, without proving general superiority.

The adapter sends `reasoning: { effort: "medium", exclude: true }`. **Exclusion
omits returned thinking text; it does not disable reasoning or billing.** DeepSeek
already used `exclude: true`. Successful Gemini trials reported nonzero reasoning
tokens. OpenRouter dashboard visibility alone cannot establish whether thinking
occurred; the reason its display differs between models has not been verified.

| Setting                       | Contract                       |
| ----------------------------- | ------------------------------ |
| `Model`                       | Required OpenRouter model ID.  |
| `ResponseFormat`              | Schema, JSON object, or text.  |
| `ReasoningEnabled`            | True, false, or null.          |
| `ReasoningEffort`             | Supported effort; empty omits. |
| `ReasoningMaxTokens`          | Budget; null omits.            |
| `Temperature`, `TopP`, `TopK` | Nullable sampling controls.    |
| `MaxOutputTokens`             | 1–32768; option default 8192.  |
| `RequestTimeoutSeconds`       | 1–300; default 180.            |
| `MaxRequestBytes`             | At most 512 KiB.               |
| `MaxSchemaBytes`              | At most 64 KiB.                |
| `FallbackModel`               | Empty disables fallback.       |
| `IgnoredProviders`            | Empty array excludes none.     |

Use effort or a token budget, never both. Either enables reasoning unless
explicitly disabled. To omit reasoning entirely, use null enabled/budget and
empty effort. Unsupported sampling controls should be null; an empty environment
override omits nullable controls. Temperature accepts 0–2, top-p greater than
0 through 1, and top-k 0 or higher (0 disables top-k). Provider exclusions allow
up to 16 slugs of 1–64 lowercase letters, digits, hyphens, underscores or slashes;
they apply to fallback and enter evaluation/profile fingerprints.

Prefer strict schema mode when the endpoint supports the full schema. All modes
include that schema in the prompt and apply identical server validation; JSON-only
or text mode does not guarantee equivalent provider enforcement. The adapter
preserves constraints through native SDK options and requires support for requested
parameters. It does not silently rewrite schemas or downgrade constraints.
Schema validity still cannot prove language quality or answer correctness.

Reasoning generally shares the output ceiling; output-limit responses are rejected.
Effort is not an exact token allocation or a reservation for final JSON. Token
caps/deadlines are application limits, not guaranteed billing ceilings. Use an
OpenRouter key spending limit. A [community token-overshoot report][token-report]
remains unverified here; [community low-thinking reports][community] concern other
workloads and do not establish Hebrew educational quality.

Before switching models, check [endpoint metadata][metadata] and official provider
docs, then review the entire profile. Any fallback must support the same controls;
[fallback routing][fallback] handles provider errors, not invalid or poor content.
The app makes one call per applicable stage. The optional evaluation judge uses
the same model, so a profile change also requires judge recalibration.

## Evaluation results

These are small exploratory samples. “Usable” means automatic checks pass and
assistant review finds no substantive correction across Hebrew, correctness, age
fit, adherence, answer clarity and consistency. Human scores remain unfilled.
Do not pool different phases, fixed-plan tests and authoring tests into a success
rate, or mistake assistant reviews for human educational assessment.

The paired columns show baseline / candidate, except the authoring sequence.
A dash means no comparable usable count was recorded, not a pass.

| Trial                  | Strict passes   | Usable      | Decision  |
| ---------------------- | --------------- | ----------- | --------- |
| DeepSeek material      | 2/5 → 5/5       | —           | Keep      |
| Authoring descriptions | 0/6 → 3/6 → 6/6 | —           | Keep      |
| Reasoning disabled     | Candidate 3/3   | —           | Reject    |
| Split / one-shot       | 9/9 / 9/9       | —           | No winner |
| DeepSeek / Gemini      | 5/6 / 6/6       | 2/6 / 2/6   | No winner |
| Question definitions   | 10/10 / 8/10    | 4/10 / 5/10 | Reject    |
| DekaLLM exclusion      | 3/4 workflows   | —           | Limited   |
| Gemini medium / low    | 7/9 / 6/9       | 4/9 / 6/9   | Confirm   |
| Effort confirmation    | 1/2 / 1/2       | 1/2 / 0/2   | Medium    |
| Question example       | 6/6 / 6/6       | 4/6 / 2/6   | Reject    |
| Material replacement   | 6/6 / 6/6       | 5/6 / 5/6   | Keep tie  |

DeepSeek material confirmation passed 4/5 attempts: all nine returned bodies
across candidate and confirmation passed; one provider failure. Keep body-only
counting/no-filler instructions. A shorter alternative introduced story
inconsistencies and was rejected. Authoring used the same three cases twice:
`defaultFormat` wording raised plan acceptance to 3/6, then `source` wording to
6/6. Final activities passed 5/6; one strict length failure remained. These are
clearer descriptions, not a relaxed schema or a general success-rate estimate.

Disabling DeepSeek reasoning reduced median cost about 70.5% and provider latency
61.3%, but introduced a speaker contradiction and corrupted Hebrew. Reject it and
skip conditional holdouts. The model-only Gemini trial missed the required two
additional usable activities; the owner's later Gemini selection was not a
measured quality win. Question-purpose definitions also missed that gate, added
a grammar error and had two timeouts. Shared guidance risked conflicting with
single-question replacement. The DekaLLM smoke used six calls; the supplied-source
workflow still timed out. Native exclusion capability remains, but the current
Gemini profile excludes no providers and reliability improvement is unproven.

Gemini low effort initially looked promising, but confirmation failed: both
settings returned empty authoring plans, and low replacement duplicated a sibling
question. Keep medium. The worked question example regressed reviewed quality and
still missed requested inference coverage; reject it without further holdouts.
Shared material replacement rules tied across training and niqqud/older-reader
holdouts, earning retention under the preregistered cleanup rule only.

The fresh DeepSeek comparison used matched inputs/settings/checks and alternating
order. Median split / one-shot costs were $0.003533 / $0.005388 for reading,
$0.001157 / $0.001842 for bilingual, and $0.000766 / $0.000805 for numeric.
Summed provider seconds were 14.646 / 14.910, 9.476 / 13.794 and 3.079 / 2.826.
The evaluator pause made median reading wall time 19.666 / 14.948 seconds;
application queue/persistence latency was not measured. Three repeats cannot
establish a stable performance advantage.

Supplemental DeepSeek coverage passed combined two-passage length (149 in
120–160), numeric replacement and three exactly-120-word trials. A general
authoring case duplicated shared settings as custom controls and failed adherence.
Story consistency, niqqud, inference coverage and awkward phrasing remained weak.

For completed Gemini activities, medium / low median cost was $0.00677625 /
$0.002108625 and summed provider latency 16.65 / 5.75 seconds. These exclude failures
and evaluator waits. Two provider failures occurred per arm; low also returned
151 words against the unchanged 100–150 range. Medium samples had tense and
lending/borrowing wording errors. Neither setting was consistently good.

Only the shared replacement rules survived the latest round. In
[AiPrompts.cs](../backend/FamilyLearning.Api/TaskEngine/Ai/AiPrompts.cs), a
compile-time constant shares existing body-counting/length/no-filler guidance.
Replacement adds that total length includes unchanged generated bodies and excludes
supplied sources. Initial material generation and all other stage prompts remained
byte-for-byte identical; the engine revision advanced for replacement semantics.
No extra runtime layer, call, repair or schema transformation was added.

### Method limits and audit

The available tuning artifacts retain failures, rejected candidates, blind
packs/keys and review addenda. The original 27-case suite and strict expectations
were preserved. Private scoped probes reused app services, assembly and validators;
they were not production features. The Gemini experiment used the same 65,536-byte
request ceiling in both arms, below production's 512 KiB.

- DeepSeek question trials used five snapshots but only three distinct material
  contexts; Gemini question trials used six snapshots but four contexts. Repeated
  captures of one supplied source are not independent passages.
- DeepSeek baseline question calls preceded candidate calls, so provider conditions
  confound timing comparisons. Gemini paired calls alternated order.
- A private question probe initially retained stale detail flags after clearing
  questions. Overall readiness remained false; flags were corrected offline and
  the already-started failed call was preserved and annotated.
- Reviewer disputes were resolved while blinded, retaining original scores.
  Relationship questions need not span sentences; explicitly stated relationships
  do not automatically satisfy a separately requested inference question.
- Material review initially lacked prior bodies. A supplemental blind pack verified
  novelty with no score changes; confirmation calls had already started. One
  reviewer flagged broad environmental claims in both older-reader arms, the other
  accepted both. Conservative scoring treats both as needing edits.
- Replacement tests establish material validity/scope, not whole-activity readiness:
  existing dependent questions intentionally become stale.
- An early Gemini stop-rule gap allowed three more requests after the second
  unknown-cost failure. All eight initial attempts remain. Peak charged/reserved
  exposure was $1.983489706, below $2. Three later zero-cost confirmations released
  $0.94556160. Admission/outage guards were corrected and isolated-tested before
  continuation; that does not erase the deviation.

## Unresolved failures

**Empty authoring plans:** medium and low each returned an empty `proposal` for
the number-gender case, ending normally below the output cap. All twelve schema
references resolved and the plan required nine properties. Native wire tests
confirmed the full schema, `strict: true` and `require_parameters: true`. The
responses were rejected by independent schema/domain checks. Both serving
providers appeared; no app-side schema defect was found.

An upstream schema-enforcement/translation problem is an inference, not a proven
cause: we cannot inspect the schema OpenRouter forwarded to Google. Preserve strict
rejection and investigate these IDs before changing schema representation:

- Medium: `gen-1790881909-U5JOfcRZwvJzNCwL5Din`.
- Low: `gen-1790881938-v19J3UwkWAkhuyKkFalI`.

[Google's schema contract][google-schema] and [OpenRouter's structured-output
contract][structured-output] inform that investigation; neither proves what
happened in these requests. Do not assume unsupported references or add an
inlining/repair workaround without evidence.

**Availability:** user-supplied generation IDs confirmed Google 429/504 and Google
AI Studio 503 responses with zero charged cost. These are upstream serving errors;
the logs do not establish that our app caused them. Later successes establish
intermittent availability, not stability. Earlier DekaLLM timeout attribution was
owner-reported, without serving-provider IDs in local artifacts; exclusion did not
eliminate timeouts. Missing request IDs/costs remain unknown and reserved.

**Content:** inference/relationship coverage, natural Hebrew/niqqud, factual
precision and answer clarity still need human review. Code tests, schema mode and
a same-model judge cannot certify them. No claim of being better than one-shot is
supported across these dimensions.

## Costs and retained evidence

The paid tuning ledger is closed. Amounts below are USD; reservations are
conservative allowances for missing costs, **not confirmed charges**.

| Phase           | Calls |   Known cost |      Reserve |
| --------------- | ----: | -----------: | -----------: |
| First tuning    |   128 | $0.312352616 | $0.081100800 |
| Quality/routing |    43 | $0.064295840 | $0.243302400 |
| Gemini          |    53 | $0.354201000 | $0.315187200 |
| Total           |   224 | $0.730849456 | $0.639590400 |

First tuning includes the post-cutover material and qualification experiments.

Total charged/reserved: **$1.370439856 of $2**. No unknown is counted as free.
The earlier Task 2 comparison was separate: 19 of 21 authorized calls,
$0.062444323 of its $1 cap. Cutover itself used isolated providers with no paid
calls. Application attempts do not count OpenRouter's internal routing attempts.

All paths below are under ignored `artifacts/evaluations/`; they describe private
evidence, not repository fixtures. The four tuning directories are present. The
original Task 2 directory referenced by earlier reports is absent from this
workspace; its raw evidence could not be reverified during consolidation. Do not
edit historical reports to match new code or report formats. The latest manifest
hashes captured source versions, not this subsequently consolidated documentation.

- `task2-structured-2026-10-01/`: historical location for the original
  one-shot/split run, preregistration, review and cutover evidence; currently absent.
- `post-cutover-tuning-2026-10-01/`: first material baseline/candidates and costs.
- `qualification-2026-10-01/`: authoring descriptions, reasoning-off, fresh one-shot
  comparison and supplemental cases.
- `quality-2026-10-01/`: model comparison, question-purpose candidate, routing
  trial and billing.
- `gemini-tuning-2026-10-01/`: frozen profiles/cases, three experiments, diagnosis,
  reviews, closed `budget.json`, `manifest.json` and `verify.log`.

Historical reports record Task 2 run ID
`20260930T213012Z-25ad70d9dd6f49128325ef36512f0ad9` and run SHA-256
`ca3be34a99691af75ef9ca701f3affd17aec4f8e8faef63cfeabb5dfe22007e9`.
The retired six design/plan/report documents and pre-consolidation README are
preserved verbatim in `documentation-history-2026-10-01.zip` in the same artifact
root. This archive is historical evidence, not another maintained guide.

## Using the evaluation harness

The developer executable reuses the app engine/provider without learning records.
Preview, dashboard startup and offline comparison make no AI calls. Automated
tests use isolated providers; CI never runs live evaluation.

```bash
./scripts/evaluate-ai.sh --case all
./scripts/evaluate-ai.sh --ui
./scripts/evaluate-ai.sh --compare baseline/run.json candidate/run.json

# Only within an explicitly authorized live budget:
./scripts/evaluate-ai.sh --live --case reading-grade3 --max-calls 3
./scripts/evaluate-ai.sh --live --case ants-inference --judge --max-calls 8
./scripts/evaluate-ai.sh --live --case reading-grade3 --repeat 3 \
  --judge --max-calls 19
```

The examples cover three base stages; four calibration controls plus stages/review;
and a repeated comparison with 16 base calls plus room for three retries.
`--repeat` accepts 1–5, `--max-calls` 1–100. Preview sums applicable stages,
replacements, reviews and once-per-run calibration. Every actual attempt consumes
the hard call limit, even if authoring deviates from the estimate. The normal
harness caps calls, not dollars; use a key spending limit and explicit accounting.

Calls run sequentially with a default five-second pause. Dashboard **Pause between
calls** or `--call-delay-seconds N` accepts 0–60; waiting is cancellable and excluded
from request deadlines/provider latency. Only HTTP 429 retries, at most three per
stage within budget. Honor `Retry-After`, otherwise 5/10/20 seconds plus up to 20%
jitter, with the configured pause as minimum. A wait over five minutes stops the
run. Production has no such retries; spacing does not guarantee availability.

The dashboard binds loopback at `http://127.0.0.1:5180` (`--port` overrides).
One confirmed run may be active; Cancel preserves partial results. Invalid settings
disable live runs but keep offline routes available. Restart after configuration
or fixture edits. Host/Origin/CSRF/CSP and bounded artifact access protect this
developer-only paid-run boundary; see [architecture](architecture.md#evaluation).

History displays requests, outputs and findings as text. **Copy for AI** includes
answer keys: keep exports private. Six human scores use 0 unusable, 1 needs edits,
2 ready, or null unreviewed, with notes up to 4,000 characters. Reviews save
serially and preserve generated evidence. Labels (120 characters) and notes (4,000)
also use CLI `--label` / `--notes`. Correction counts and time-to-ready retain
actual effort including failed trials; missing values mean unknown.

### Cases, reports and comparisons

The synthetic [cases](../tools/FamilyLearning.Evaluation/cases.json) contain:
prompt or fixed `initialPlan`, optional initial input, up to three 4,000-character
refinements for authored plans, and replacements by zero-based fixture index
resolved to app IDs. Fixed plans skip authoring. Supplied material skips
material AI.
`expectedGeneratedMaterials` estimates calls and independently checks adherence.
`settingsOverride` supplies all four settings with matching question count;
`additionalControlCount` excludes shared settings/native material choices.
`expectedLength` checks plan requirements (aggregate for multiple bodies);
`minPassageWords` / `maxPassageWords` check assembled bodies with shared
TextLength.
`reviewFocus` guides review, not automatic assertions.

Keep case IDs and expectations stable. Add cases for real coverage gaps; never
relax a check to improve pass rates. Dropped authoring requirements remain failures
even if the weaker plan validates. Targets are advisory; exact/range lengths are
strict, including the unchanged 100–150 regression. Body headings count; titles,
instructions, questions/answers and punctuation-only tokens do not.

Reports go to `artifacts/evaluations/<run>/` or `--output`:

- **run.json:** authoritative checkpoint after each call and cancellation. Contains
  exact requests/schemas/hashes, inputs, fixture/stage/engine versions,
  accepted/rejected content, source revisions, application state/skips,
  model/provider when known, finish reasons and usage. Domain rejections include
  safe field diagnostics; secrets, raw provider errors and separate reasoning
  text are excluded.
- **summary.json:** derived stage outcomes, independent adherence failures,
  calibration, findings, human scores and measurements. Missing costs/tokens are
  null with coverage counts, not zero; reasoning is already in output tokens.
  Response latency includes returned but rejected output and excludes transport
  failures/timeouts/cancellation. See [usage accounting][usage].

Keep code tests, deterministic checks, calibrated model findings and human judgment
separate. A model finding must identify a supplied field, exact quote, correction,
explanation and supported kind. It is advisory and never edits output or assigns
educational scores. `plan.` / `document.` paths distinguish sources. An answer
position advisory flags three or more choice answers in one position; it neither
reorders choices nor changes scores.

Optional judge [controls][judge-controls]
contain known defects and clean variants. Expected findings are not sent to the
judge. Preserve planted defects. Expected matches use whole tokens/short containing
phrases; corrections must remove the offending phrase. All expected defects and
no disallowed extra findings must pass. Invalid/unavailable reviews fail calibration
but leave unmeasured detection unknown. Calibration measures detection, not general
accuracy or correction quality; [same-model judging has limitations][judge].

Comparison rereads authoritative runs, including edited human scores. It reports
candidate-minus-baseline deltas and compatibility, never a combined score/winner.
Direct comparisons require matching suite hashes, selected case order, captured
inputs, repeats, check versions and complete workflows. Generator deltas also need
matched effective inputs/settings/sources; authoring needs matched refinement
sequences. An engine revision difference alone does not invalidate comparison.
Judge model/profile/prompt/rubric/coverage and passing calibration must match for
judge deltas. Human deltas require the same scored case/repetition pairs. Resource
deltas require full measurement coverage and comparable judge work. Unsupported
formats or invalid embedded controls/scores are rejected, not converted.

Only the format owned by
[EvaluationVersions](../tools/FamilyLearning.Evaluation/EvaluationVersions.cs) is
supported. Reports require their check version, call delay and calibration results
(empty when unused). Changed fixtures/checks/contracts require a new matched
baseline. Run exits: 0 automatic checks and enabled reviews/calibration pass without
findings; 1 failures/findings/stopped; 2 invalid input/configuration/report/file;
130 cancellation. Compare exits: 0 compatible, 1 incompatible, 2 invalid input;
these are not quality verdicts.

## Verification and future changes

Cutover verification passed 527 backend, 98 Angular, 23 dashboard and 16 isolated
browser tests, packaging and a no-call 27-case / 64-planned-call preview. Latest
Gemini changes passed `scripts/verify.sh`: 534 backend, 98 Angular and 23 dashboard
tests plus restores/builds, formatting, Markdown and TypeScript checks. Private
transport/accounting, negative replacement and compiled prompt-scope checks passed.
The browser workflow did not change during tuning, so its cutover suite was not
rerun for those changes. These are dated engineering results, not AI quality scores.

Future work must address a concrete failure with one bounded hypothesis at a time.
Freeze cases/settings/checks, register an adoption/stop rule, preserve all failures
and costs, compare to the current baseline and stop on flat results or regressions.
Do not add repeated prompt exceptions for individual holdouts. Make any claim
proportional to the evidence, with human review for language/education. Maintain
this guide in place instead of adding another dated narrative report; keep run
protocols and immutable experimental evidence in artifacts.

[gemini]: https://ai.google.dev/gemini-api/docs/generate-content/latest-model
[reasoning]: https://openrouter.ai/docs/guides/best-practices/reasoning-tokens
[metadata]: https://openrouter.ai/api/v1/models
[fallback]: https://openrouter.ai/docs/guides/routing/model-fallbacks
[token-report]: https://discuss.ai.google.dev/t/gemini-3-8-flash-high-does-maxoutputtokens-include-thinking-tokens/181077/4
[community]: https://www.reddit.com/r/hermesagent/comments/1w5jj6w/gemini_38_flash_is_awesome_as_the_main_agent/
[google-schema]: https://ai.google.dev/api/generate-content#v1beta.GenerationConfig
[structured-output]: https://openrouter.ai/docs/guides/features/structured-outputs
[usage]: https://openrouter.ai/docs/cookbook/administration/usage-accounting
[judge]: https://arxiv.org/abs/2306.05685
[judge-controls]: ../tools/FamilyLearning.Evaluation/hebrew-review-samples.json
