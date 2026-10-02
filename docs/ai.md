# AI guide

Current decisions and accumulated evidence, consolidated on 2 October 2026.
This is the maintained reference for AI configuration, evaluation and tuning.
Product contracts live in the [product specification](product-specification.md);
implementation boundaries live in [architecture](architecture.md).

## Current status

**Strict schema mode works and is the active profile. Educational quality is
still not established**, and superiority to the old one-shot approach has not
been shown. Passing code tests and schema checks does not mean generated Hebrew,
answer keys or educational content are consistently good.

- Use Gemini 3.8 Flash in strict `json_schema` mode with medium reasoning and
  provider-default sampling. The schema is sent once, natively; mandatory
  application validation is unchanged. Low reasoning was faster and cheaper,
  but failed confirmation quality checks.
- The former strict-mode instability had three causes, all in the schema
  contract rather than the model: an OpenRouter conversion defect for integer
  enums, bounded control/option arrays exceeding Gemini's schema-complexity
  budget, and an OpenRouter widening of null-only branches. See
  [strict schema contract](#strict-schema-contract) and
  [resolved strict-mode failures](#resolved-strict-mode-failures).
- Live acceptance ran six cases covering every stage in three rounds: 42 calls,
  all automatic checks passed, every response contract-valid, no retry or
  repair. This small sample is not a reliability rate.
- An activity has at most 20 questions (owner decision, 2 October). Larger
  exact-count question arrays exceed Gemini's budget for heavier shapes.
- Keep the earlier material instructions and clearer authoring-field
  descriptions: they improved sampled contract adherence on DeepSeek. The latest
  shared material replacement instructions are a cleanup with comparable
  reviewed quality.
- Reject both question-prompt experiments. Neither earned adoption.
- Keep parent review, strict validation and explicit recovery. No automatic
  output repair, weakened tests, model-specific branch or production retry.

Paid diagnostics are closed with earlier unknown costs still reserved;
representative content needs human review before further quality tuning.

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

Current profile: `google/gemini-3.8-flash`, strict `json_schema`, reasoning
enabled at `medium`, no fixed reasoning-token budget, no temperature/top-p/top-k
overrides, 16,384 requested output tokens, 180-second deadline, no fallback
model and no excluded providers. The compiled request ceiling is 512 KiB; the
schema ceiling is 64 KiB. The output character limit is separately enforced by
the engine. Requests set `strict: true` and `provider.require_parameters`, so
OpenRouter routes only to endpoints supporting structured outputs.

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

Prefer strict schema mode when the endpoint accepts the full schema. Each call has
one schema source: strict mode sends it only as the native response format;
JSON-object and text modes, which have no provider enforcement, carry it in the
prompt. Every mode applies identical server validation. The adapter preserves
constraints through native SDK options and requires support for requested
parameters. It does not rewrite schemas or downgrade constraints; the
application owns the schema and keeps it within the
[strict schema contract](#strict-schema-contract). Schema validity still cannot
prove language quality or answer correctness.

Reasoning generally shares the output ceiling; output-limit responses are rejected.
Effort is not an exact token allocation or a reservation for final JSON. Token
caps/deadlines are application limits, not guaranteed billing ceilings. Use an
OpenRouter key spending limit. A [community token-overshoot report][token-report]
remains unverified here; [community low-thinking reports][community] concern other
workloads and do not establish Hebrew educational quality.

Before switching models, check [endpoint metadata][metadata] and official provider
docs, then review the entire profile. Schema acceptance is provider-specific:
rerun strict acceptance, and confirm the endpoint exposes the schema to the model
(Gemini uses field names and descriptions), before relying on strict mode there.
Any fallback must support the same controls;
[fallback routing][fallback] handles provider errors, not invalid or poor content.
The app makes one call per applicable stage. The optional evaluation judge uses
the same model, so a profile change also requires judge recalibration.

### Strict schema contract

These rules keep one application schema valid, enforceable and faithfully
translated for Gemini behind OpenRouter. Each was verified live on 2 October
with OpenRouter's [upstream debug echo][upstream-debug]: the app's schemas now
reach Google unchanged apart from an added `propertyOrdering`, which matches
the declared key order.

- **No prompt copy in strict mode.** Google's [structured output guidance][vertex-schema]
  says to supply the schema only as the response schema, because a prompt copy
  can lower quality. Gemini counts the native schema as input, so removing the
  copy and the noise below cut authoring input from about 7,100 to 5,000 tokens.
- **Stay inside the complexity budget.** Gemini compiles the schema into a
  decoding constraint and expands bounded arrays per item. Past an undisclosed
  budget it returns a bare HTTP 400 `INVALID_ARGUMENT` that names no field;
  Google lists "long array length limits" and nested arrays as causes.
  Size policies that the validator owns, such as 16 controls per plan and 1–20
  distinct options, are therefore not decoding bounds. Generation counts stay
  exact: up to four materials and 1–20 questions. The heaviest valid question
  shape (all formats, six choices) is accepted at 20 and rejected at 25.
- **Only meaningful constraints.** Numeric bounds are the validator's real ones:
  question count 1–20, positive counts and lengths, and text-control length
  1–500. C# int32 sentinels are not emitted. `minLength`, `maxLength` and
  `pattern` are outside [Gemini's supported subset][gemini-schema] and are
  ignored there, which is how an empty clarification once passed the provider.
  They still inform the model in every mode; the validator enforces them.
- **OpenRouter-safe spellings.** Its [Gemini conversion][structured-output]
  erases the properties of an object containing an integer `enum`, widens a
  standalone `"type": "null"` to a nullable string, and drops a `description`
  placed beside `anyOf`. The fixed version uses `minimum` = `maximum`;
  null-only branches use `["null"]`; nullable arrays use type arrays;
  cross-field rules such as `choiceCount` live in the prompt. These are
  equivalent JSON Schema, not relaxations; the reproduction for OpenRouter
  support is in the [retained evidence](#costs-and-retained-evidence).
- **Descriptions carry validator semantics.** `adjustable` is true only for an
  explicitly requested per-activity input. Before that description, the first
  acceptance round invented adjustable choice and length counts for the space
  prompt, matching three earlier observations; the next two rounds did not.
  That sample is too small to estimate a rate.

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
| Gemini strict schema   | 18/18           | —           | Adopt     |

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

Strict-schema acceptance on 2 October used the current profile without retries:
`space-reading-grade3`, `number-gender`, `many-questions` (20 questions),
`question-replacement`, `reading-refinement` and `fixed-bilingual`. Together
they cover authoring, refinement, materials, questions, question replacement
and a supplied source. Each round made 14 calls: the schema fix alone, then
without the prompt copy and with descriptions, then the release schema. All 18
workflows passed automatic checks, at $0.13–0.15 reported cost per round and a
13.8–15.5 second median call. One release call hit Gemini's implicit cache for
an identical request and was billed for 1,070 input tokens instead of about
5,900, so compare token counts with caching in mind. Material replacement and
judge review were not exercised, and the content was not reviewed.

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

## Resolved strict-mode failures

Owner reports on 1–2 October showed strict mode returning empty proposals, an
empty clarification despite `minLength: 1`
(`gen-1790943339-87Hr7EqfWwN5XsjGx3wo`), a both-null reply under the earlier
flat envelope, and HTTP 400 once the plan survived conversion. JSON-object mode
was the interim profile until strict acceptance passed.

- **Erased plan.** OpenRouter converted the nine-property plan containing
  `schemaVersion: { "enum": [1] }` into an empty closed object
  (`gen-1790948761-8cTFOE1kL7ekmZkA1cOc`), so `proposal: {}` was the only valid
  plan. Equal integer bounds preserve both the constraint and the plan.
- **Opaque HTTP 400.** With the plan intact, Google rejected the schema
  (`gen-1790948826-ywSQZpwH5ZuAOU7i3gxi`). Eight earlier probes each changed
  one feature and all failed, because the dominant cost remained in every one.
  A factorial screen then showed that removing any single plan subtree still
  failed, while removing all controls or all array bounds passed. Removing only
  the control and option array bounds passed with every other constraint
  intact; restoring a single 16-item control bound failed again. Exact question
  arrays fail between 20 and 30 items, while 100 plain strings pass, so this is
  expansion complexity rather than a fixed bound.
- **Widened nulls.** Null-only branches became nullable strings upstream, so
  strict mode allowed, for example, a string proposal beside a clarification.
  `["null"]` reaches Google unchanged.
- **Empty strings.** Gemini ignores `minLength`. The validator rejects empty
  clarifications in every mode, as before.

Google publishes no budget figure, so any new schema feature needs a live
acceptance check before release.

## Unresolved failures

**DeepSeek verification (2 October):** changing only the model of the failing
strict-schema request to `deepseek/deepseek-v4.1-flash` produced three nonempty
space plans and one meaningful clarification. All four responses, served by
AtlasCloud, passed independent schema validation. Two plans passed application
validation; the third, through the existing harness, failed because
`questions.choiceCount` was null. Material/question generation was skipped and
paid testing stopped. The authoring prompt now states that single-choice
requires a 2–6 choice count, and `adjustable` has a schema description; DeepSeek
has not been retested since. All three plans also invented adjustable word
counts. This sample proves neither general provider reliability nor better
educational quality.

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
The JSON-mode confirmation passage incorrectly described orbit as having no
gravity; one proposal also enabled length adjustment without an explicit request.
These findings are retained in the private review and are not counted as an
educational-quality pass.

**Evaluation judge:** the optional judge still appends its schema to its prompt
in strict mode. Its exact prompt is part of the calibration fingerprint, so the
change needs its own recalibration run. Production generation is unaffected.

## Costs and retained evidence

The paid tuning ledger is closed. Amounts below are USD; reservations are
conservative allowances for missing costs, **not confirmed charges**.

| Phase                 | Calls |    Known cost |      Reserve |
| --------------------- | ----: | ------------: | -----------: |
| First tuning          |   128 |  $0.312352616 | $0.081100800 |
| Quality/routing       |    43 |  $0.064295840 | $0.243302400 |
| Gemini                |    53 |  $0.354201000 | $0.315187200 |
| Authoring contract    |    23 |  $0.098323500 | $0.464459700 |
| Owner's new report    |     1 |  $0.004589250 | $0.000000000 |
| DeepSeek verification |     4 | $0.0066341346 | $0.000000000 |
| Gemini strict schema  |    11 | $0.0117457500 | $0.326841750 |
| Strict root cause     |    96 | $0.5185207500 | $0.199239750 |
| Total                 |   359 | $1.3706628406 | $1.630131600 |

First tuning includes the post-cutover material and qualification experiments.

Total charged/reserved: **$3.0007944406**. The owner authorized up to $3 of live
calls for the 2 October root-cause session. Its known cost was $0.5185; the
reserve covers 28 HTTP 400 schema rejections at full output price. Key usage
afterwards matched reported per-call costs exactly, including two rejections at
$0, so the rejections appear free, but they stay reserved under this ledger's
rule.
The owner raised the aggregate cap and authorized eight further calls after the
first two strict-schema diagnostics;
all ten are retained. A subsequent allowance covered one nullable-enum probe and
five confirmation calls conditional on acceptance. The probe failed; those five
calls were not made. No unknown is counted as free.
The six-call JSON-mode confirmation cost $0.04964250; the owner's latest reported
call is conservatively included too. Prior reservations were not released.
The earlier Task 2 comparison was separate: 19 of 21 authorized calls,
$0.062444323 of its $1 cap. Cutover itself used isolated providers with no paid
calls. Application attempts do not count OpenRouter's internal routing attempts.

All paths below are under ignored `artifacts/evaluations/`; they describe private
evidence, not repository fixtures. Earlier directories are historical locations;
they were unavailable during the 2 October authoring investigation and their raw
evidence could not be reverified. The new `authoring-contract-2026-10-02/` directory
retains all three diagnostic attempts, schemas, validation and budget evidence.
The subsequent `authoring-*-2026-10-02/` directories retain every probe's exact
request, response, independent validation and cost. The JSON-mode ledger is
`authoring-empty-clarification-2026-10-02/budget.json`, closed after six calls.
That directory includes the owner's failure, exact-mode comparison, full existing
harness report, assistant review, costs and isolated verification.
The ledger `deepseek-schema-verification-2026-10-02/budget.json` is closed
after four calls at the first application rejection. That directory preserves the
matched requests, provider responses, failed harness run and review.
The latest ledger is `gemini-strict-contract-2026-10-02/budget.json`, closed after
eleven calls. It retains exact requests, upstream captures, responses, schema deltas,
review and red/green regression evidence. Error-payload debug captures were
recovered after the streaming parser completed; the ledger includes those frames,
while original per-attempt result snapshots preserve the parser's zero count.
The `authoring-native-format-2026-10-02/` directory contains a local request
capture and an unexecuted candidate, not another paid attempt.
The root-cause ledger `gemini-strict-root-cause-2026-10-02/budget.json` is closed
after 96 calls. That directory keeps the acceptance probe, every probe schema and
response, exact application wire captures, upstream echoes and diffs, the three
harness rounds (`harness/`, `harness-final/`, `harness-release/`) and
`acceptance-summary.json`. The OpenRouter reproduction for support remains
`gemini-strict-contract-2026-10-02/support-reproduction.md`; it predates the
dropped-`description` finding.
Do not edit historical reports to match new code or report formats. The latest manifest
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
browser tests, packaging and a no-call 27-case / 64-planned-call preview. The
2 October schema correction passed `scripts/verify.sh`: 555 backend, 123 Angular
and 23 dashboard tests plus restores/builds, formatting, Markdown and TypeScript
checks. The focused regression failed before the correction and passed after it.
These isolated checks do not establish full Gemini strict-schema acceptance. Private
transport/accounting, negative replacement and compiled prompt-scope checks passed.
The browser workflow did not change during tuning, so its cutover suite was not
rerun for those changes. These are dated engineering results, not AI quality scores.

The 2 October strict-schema change passed `scripts/verify.sh`: 557 backend, 123
Angular and 23 dashboard tests plus builds, formatting, Markdown and TypeScript
checks. New regressions cover null-only wire branches, unbounded control lists
with validator-owned limits, and the 20-question cap in the API and plan editor;
each failed before its fix and passed after. After `scripts/publish.sh`, the 16
isolated browser tests passed. That rerun also fixed the e2e provider, which
still read the version from the integer enum removed in the previous change, so
its authoring paths had been failing since then.

Future work must address a concrete failure with one bounded hypothesis at a time.
Freeze cases/settings/checks, register an adoption/stop rule, preserve all failures
and costs, compare to the current baseline and stop on flat results or regressions.
Do not add repeated prompt exceptions for individual holdouts. Make any claim
proportional to the evidence, with human review for language/education. Maintain
this guide in place instead of adding another dated narrative report; keep run
protocols and immutable experimental evidence in artifacts.

[gemini]: https://ai.google.dev/gemini-api/docs/generate-content/latest-model
[gemini-schema]: https://ai.google.dev/gemini-api/docs/structured-output
[vertex-schema]: https://docs.cloud.google.com/vertex-ai/generative-ai/docs/multimodal/control-generated-output
[reasoning]: https://openrouter.ai/docs/guides/best-practices/reasoning-tokens
[metadata]: https://openrouter.ai/api/v1/models
[fallback]: https://openrouter.ai/docs/guides/routing/model-fallbacks
[token-report]: https://discuss.ai.google.dev/t/gemini-3-8-flash-high-does-maxoutputtokens-include-thinking-tokens/181077/4
[community]: https://www.reddit.com/r/hermesagent/comments/1w5jj6w/gemini_38_flash_is_awesome_as_the_main_agent/
[upstream-debug]: https://openrouter.ai/docs/api_reference/errors-and-debugging
[structured-output]: https://openrouter.ai/docs/guides/features/structured-outputs
[usage]: https://openrouter.ai/docs/cookbook/administration/usage-accounting
[judge]: https://arxiv.org/abs/2306.05685
[judge-controls]: ../tools/FamilyLearning.Evaluation/hebrew-review-samples.json
