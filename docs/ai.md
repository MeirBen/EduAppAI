# AI guide

The maintained reference for AI configuration, evaluation and tuning, as of
2 October 2026. Product contracts live in the
[product specification](product-specification.md); implementation boundaries
live in [architecture](architecture.md).

## Current status

**Strict schema mode works and is the active profile. Educational quality is
not established**, and superiority to the old one-shot approach has not been
shown. Passing tests and schema checks does not mean generated Hebrew, answer
keys or educational content are good.

- Gemini 3.8 Flash in strict `json_schema` mode, medium reasoning,
  provider-default sampling. The schema is sent once, natively; application
  validation is unchanged.
- Strict mode's former instability came from the schema contract, not the
  model; see [resolved failures](#resolved-strict-mode-failures).
- Six acceptance cases covering every stage passed in three rounds (42 calls,
  no retry or repair). That sample is not a reliability rate, and it predates
  the 4–5 October prompt revisions (through 26), which targeted live replays
  and A/B runs checked instead; see [prompt decisions](#prompt-decisions).
- Keep parent review, strict validation and explicit recovery. No automatic
  output repair, weakened tests, model-specific branch or production retry.
- Representative content needs human review before further quality tuning.

## Design and cutover decision

The flow is prompt → editable plan → applicable material generation →
questions → editable saved draft → parent review → immutable snapshot. Template
publication is independent. Supplied sources are assembled verbatim by the app;
question-only and supplied-source activities skip material generation. Scoped
replacement preserves unrelated content, and replacing material makes
dependent questions stale instead of regenerating them. A question replacement
reads the other questions and learner instructions as context, so it stays
distinct from them and consistent with the instructions.

On 1 October the owner chose this split flow for source preservation, editing
and checkpoint recovery, although the comparative threshold was **not met**: a
value decision, not evidence of better Hebrew. Across three fixed cases with
three repetitions, one-shot passed 9/9 structural trials and split 7/9; split
cost about 3× and took 3.4× median provider latency. The one-shot
implementation and its schemas were removed without compatibility readers.
Development database setup is in the [README](../README.md#data).

## Prompt decisions

On 4 October each change replayed the app's exact provider request, varying
only the lines under test; counts are small samples, not reliability rates.

- **Variety:** the material stage lists five less-typical premises of different
  kinds before writing the one an app-drawn number selects. Over 30 stories per
  arm, the chance that two share a premise kind fell from 0.34 to 0.22 (p =
  0.015; two thirds classified blind); informational texts stayed varied. Naming
  characters in premises was rejected: it spread names but narrowed premise
  kinds.
- **Question replacement:** with the other questions and learner instructions
  as context, "another question" stopped duplicating existing ones (4/8 before,
  0/8 after).
- **Calculations:** drills embedded expressions in Hebrew sentences, which
  render reversed (16/25); the expression rule made them whole items (0/25),
  kept requested word problems (20/20) and left reading questions unchanged.
  Applied to texts too, it caused no regression; texts showed no symbol
  expressions with or without it.
- **Niqqud:** the plan chat added unrequested niqqud for third grade in 3/4
  plans; with a stated default, 0/4. Third-grade texts had no points, and
  first-grade texts kept full niqqud (6/6) once the rule led with that case.
  A third-grade story still came back vocalized once in about 33 runs, so the
  rule now says "up to second grade" instead of "usually first and second
  grade" (third grade 0/4, first grade 2/2 full afterwards).
- **Paragraphs:** without guidance, 62% of 60–149-word texts came back as one
  block. A paragraph rule split 120-word stories into 3–4 paragraphs (0/3
  before, 3/3 after), left long texts paragraphed and kept poem lines.
- **Dropped:** a "shared directions belong in the instructions" line had no
  effect on drills, and "find the facts in the text; no clues across
  questions" showed none over 48 questions per arm (4% restated operands
  either way, no leaks). Duplicate rules were removed so each stage receives
  every rule once.

On 5 October each change ran in parallel against a frozen baseline build through
the repaired harness; question types were scored blind by the assistant, not by
a human.

- **Inference (revision 26, kept):** one line makes inference, cause and
  conclusion questions connect or interpret information instead of restating the
  material. Explicit inference requests produced genuine inference questions
  23/24 times with it and 16/24 without (six trials per arm, p = 0.023; the
  replication alone was 11/12 against 9/12). Cause-and-effect, main-idea and
  plain reading questions did not change.
- **Low reasoning (rejected):** on Gemini 3.8 Flash it removed reasoning
  entirely and roughly halved generation cost and latency (median material call
  22 s to 8 s). Across 14 judged trials it produced 9 Hebrew findings against 1,
  including a wrong plural in a question and all its options, a missing
  preposition, a construct-form error and an invented word, and one length
  failure. Medium stays.
- **Paragraph array (rejected):** returning material bodies as `paragraphs`
  removed every line break inside poem stanzas (0 against 12) and gave stories
  fewer paragraphs (2–3 against 5); the string body with the paragraph rule kept
  both.
- **Hebrew style section (rejected):** thirteen grammar lines from a writing
  skill added 7–9% to every prompt with no measured gain, and no failure it
  targeted had been observed.
- **Factual line (rejected):** "state only well-established facts" over 200
  blind-scored informational texts on 14 topics changed nothing: clear factual
  errors in 6/106 texts without it and 7/94 with it (p = 0.78). Twelve of the
  thirteen came from one topic, ants in the rain, where the model moves brood to
  deep rooms said to stay dry in 12 of 15 texts; flooding studies show colonies
  moving up. The other topics had one error in 185 texts. A topic-specific rule
  would be a holdout exception, so parent review stays the safeguard.

## Configuration

[appsettings.json](../backend/FamilyLearning.Api/appsettings.json) holds the
active profile. `scripts/configure-ai.sh` stores the key in development user
secrets outside the repo; `Ai__ApiKey` or `OPENROUTER_API_KEY` supply server
secrets, and Production ignores user secrets. Environment variables (`Ai__…`)
and development secrets (`Ai:…`) override the file; restart after changes.
Credentials never belong in reports.

| Setting                       | Contract                       |
| ----------------------------- | ------------------------------ |
| `Model`                       | Required OpenRouter model ID.  |
| `ResponseFormat`              | Schema, JSON object, or text.  |
| `SchemaInPrompt`              | Strict-mode prompt copy.       |
| `StrictQuestionCountLimit`    | Exact-count ceiling; 0–20.     |
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

The current profile requests 16,384 output tokens with a 180-second deadline,
no fallback model and no excluded providers. Medium reasoning and omitted
sampling follow [Google's guidance][gemini] and the
[OpenRouter reasoning mapping][reasoning]. The adapter sends
`reasoning: { effort, exclude: true }`; **exclusion hides returned thinking
text but does not disable reasoning or its billing.** Reasoning shares the
output ceiling, and output-limit responses are rejected. Token caps and
deadlines are application limits, not billing ceilings, so use an OpenRouter key
spending limit.

Use effort or a token budget, never both; either enables reasoning unless
explicitly disabled. Unsupported sampling controls stay null. Temperature
accepts 0–2, top-p greater than 0 through 1, and top-k 0 or more (0 disables
it). Provider exclusions allow up to 16 lowercase slugs; they apply to fallback
and enter profile fingerprints. Requests set `strict: true` and
`provider.require_parameters`, so OpenRouter routes only to endpoints that
support every requested parameter.

Every response mode applies the same server validation. `json_schema` sends the
schema natively; `json_object` and `text` carry it in the prompt. The adapter
uses native SDK options and never rewrites or downgrades the schema.

**Switching models is a configuration change**; no code branches on the model.
Check [endpoint metadata][metadata] and provider docs, then set `Model` and its
supported reasoning and sampling controls. Use `json_schema` when a strict
acceptance run passes, otherwise `json_object`. Set `SchemaInPrompt` when the
endpoint enforces the schema without showing it to the model, and
`StrictQuestionCountLimit` to the largest exact question batch it accepts;
larger batches still work, with the count enforced by validation. A fallback
must support the same controls, and [fallback routing][fallback] covers
provider errors, not poor content. The evaluation judge is pinned separately,
so a generation profile change needs no judge recalibration.

### Strict schema contract

Each rule was verified live with OpenRouter's [upstream debug
echo][upstream-debug]: the app's schemas reach Google unchanged apart from an
added `propertyOrdering` matching the declared key order.

- **No prompt copy in strict mode** unless `SchemaInPrompt` is set, following
  [Google's guidance][vertex-schema] that a copy can lower quality. Gemini
  counts the native schema as input; removing the copy and schema noise cut
  authoring input from about 7,100 to 5,000 tokens.
- **Stay inside the complexity budget.** Gemini expands bounded arrays while
  compiling the schema and returns a bare HTTP 400 `INVALID_ARGUMENT` past an
  undisclosed budget ([long array length limits][vertex-schema]). So the
  validator, not the schema, bounds controls (16 per plan) and select options
  (1–20); the authoring prompt states both limits from the same constants.
  Materials (up to 4) and questions up to `StrictQuestionCountLimit` stay exact.
  Gemini accepts 20 questions in the heaviest shape (all formats, six choices)
  and rejects 25.
- **Only meaningful constraints.** Numeric bounds are the validator's real
  ones, with the version and question cap applied from engine constants.
  `minLength`, `maxLength` and `pattern` are outside
  [Gemini's subset][gemini-schema] and ignored there; they still inform the
  model, and the validator enforces them.
- **OpenRouter-safe spellings.** Its [Gemini conversion][structured-output]
  erases an object holding an integer `enum`, widens `"type": "null"` to a
  nullable string and drops a `description` beside `anyOf`. So the version uses
  `minimum` = `maximum` ([string enums only][vertex-schema]), null-only branches
  use `["null"]`, nullable arrays use type arrays, and cross-field rules such as
  `choiceCount` live in the prompt. All are equivalent JSON Schema.
- **Descriptions carry validator semantics.** `adjustable` is true only for an
  explicitly requested per-activity input; before that description, models
  repeatedly invented adjustable counts.

Over-limit requests for 30 questions, six passages and a 25-option select each
drew a focused clarification instead of a rejected plan.

Limit provenance: `StrictQuestionCountLimit` is endpoint-specific and measured
(20 for Gemini at six choices), because Google publishes no budget; re-measure
it for a new model or a higher choice cap. Question count, choices, controls,
options, materials, content size and field lengths are owner-set product
limits in the [product specification](product-specification.md). The 16,384
output tokens (validated up to 32,768) stay below the endpoint maximum of
65,536.

## Evaluation results

These are small exploratory samples with assistant reviews; human scores remain
unfilled. “Usable” means automatic checks pass and review finds no substantive
correction. Do not pool phases into a success rate. Columns show baseline /
candidate; a dash means no comparable usable count.

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
| Length contract        | 2/2             | —           | Adopt     |

- **Material and authoring wording:** body-only counting/no-filler rules and
  clearer `defaultFormat`/`source` descriptions raised adherence; a shorter
  material alternative introduced story inconsistencies.
- **Reasoning disabled:** about 70% cheaper and 61% faster on DeepSeek, but
  produced a speaker contradiction and corrupted Hebrew.
- **Split / one-shot and model choice:** no stable cost or latency winner, and
  Gemini was the owner's choice, not a measured quality win.
- **Question prompts:** purpose definitions and a worked example regressed
  reviewed quality or missed requested inference coverage.
- **DekaLLM exclusion:** the supplied-source timeout persisted; the setting
  remains available but unused.
- **Gemini effort:** low cost about a third of medium but duplicated a sibling
  question; both returned empty plans, later traced to the enum defect. Medium
  stays; neither was consistently good.
- **Strict schema:** the six acceptance cases ran in three rounds of 14 calls
  at $0.13–0.15 per round. Material replacement and judge review were not
  exercised, and content was not reviewed.
- **Length contract:** exact word counts and per-activity bounds were removed
  (engine revision 11). "Exactly 120 words" became an approximate 120-word
  target with a stated assumption (124 words generated), and a strict 100–150
  range still held (117 words). One run each; content was not reviewed.

Samples reuse few distinct material contexts, and Gemini's implicit cache can
bill repeated requests for far fewer input tokens, so treat every count as
directional.

## Resolved strict-mode failures

Strict mode returned empty proposals, an empty clarification despite
`minLength: 1` (`gen-1790943339-87Hr7EqfWwN5XsjGx3wo`) and HTTP 400 once the
plan survived conversion. JSON-object mode served until strict acceptance
passed.

- **Erased plan:** OpenRouter converted the plan holding `"enum": [1]` into an
  empty closed object (`gen-1790948761-8cTFOE1kL7ekmZkA1cOc`), so `{}` was the
  only valid plan.
- **Opaque HTTP 400:** Google rejected the intact schema
  (`gen-1790948826-ywSQZpwH5ZuAOU7i3gxi`). One-feature probes all failed because
  the dominant cost remained; a factorial screen isolated the control and
  option array bounds. Exact question arrays fail between 20 and 30 items while
  100 plain strings pass: expansion complexity, not a fixed bound.
- **Widened nulls:** a null-only branch accepted strings upstream.
- **Empty strings:** Gemini ignores `minLength`; the validator rejects them.

Google publishes no budget, so any new schema feature needs live acceptance.

## Unresolved failures

- **DeepSeek:** the failing strict request on `deepseek/deepseek-v4.1-flash`
  returned schema-valid replies, but one plan left `choiceCount` null and all
  three invented adjustable word counts. The prompt and descriptions now cover
  both; it has not been retested.
- **Availability:** Google 429/504 and AI Studio 503 responses occurred at zero
  cost. They are upstream serving errors; later successes show intermittent
  availability, not stability.
- **Content:** inference coverage, natural Hebrew/niqqud, factual precision,
  answer-key arithmetic and answer clarity need human review; one confirmation
  passage claimed orbit has no gravity. Tests, schema mode and a same-model
  judge cannot certify content. Gemini 3.8 Flash repeatedly writes that ants
  carry their brood deeper during rain; no generic prompt line changed it.
- **Judge blind spot:** Gemini 3.8 Flash, GPT-5.6 Terra and Claude Sonnet 5.5
  each caught all three `להסיין` in `reported-ants-defects` but never the two
  `נמלות` (the plural is `נמלים`). The owner made that control advisory: every
  run reports its result, but it does not gate calibration. Judge findings can
  miss non-standard plurals.

## Costs and retained evidence

Amounts are USD. Reserves are conservative allowances for unknown costs,
**not confirmed charges**.

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
| Control limits        |     3 | $0.0293505000 | $0.000000000 |
| Plan limits           |     2 | $0.0192232500 | $0.000000000 |
| Length contract       |     6 | $0.0473685000 | $0.000000000 |
| Total                 |   370 | $1.4666050906 | $1.630131600 |

Total charged/reserved: **$3.0967366906**, every phase under an
owner-authorized cap. The reserve includes 28 HTTP 400 rejections that key usage
later showed at $0. The earlier one-shot comparison was separate (19 calls,
$0.062444323).

Evidence lives under the ignored `artifacts/evaluations/`, one directory per
phase with exact requests, responses, validation, reviews and a closed
`budget.json`. Never edit it to match new code. Key directories:
`gemini-strict-contract-2026-10-02/` (upstream captures and the OpenRouter
`support-reproduction.md`), `gemini-strict-root-cause-2026-10-02/` (factorial
probes and acceptance rounds), the `gemini-*-limits-2026-10-03/` clarification
checks and the `length-contract-2026-10-03/` harness runs. On 5 October,
`reasoning-effort-2026-10-05/` ($0.704) and `inference-line-2026-10-05/`
($0.996, including the replication and blind scores) ran under a $2 cap;
`factual-line-2026-10-05/` ($2.745, 200 texts with blind scores) ran under a
separate $3 cap.
Retired design documents are in `documentation-history-2026-10-01.zip`.

## Using the evaluation harness

The developer tool reuses the app engine and provider without learning records.
Preview, the dashboard and offline comparison make no AI calls; CI never runs
live evaluation.

```bash
./scripts/evaluate-ai.sh --case all
./scripts/evaluate-ai.sh --case reading-grade3,number-gender
./scripts/evaluate-ai.sh --ui
./scripts/evaluate-ai.sh --compare baseline/run.json candidate/run.json

# Only within an explicitly authorized live budget:
./scripts/evaluate-ai.sh --live --case reading-grade3 --max-calls 3
./scripts/evaluate-ai.sh --live --case ants-inference --judge --max-calls 8
```

`--repeat` accepts 1–5 and `--max-calls` 1–100; every attempt consumes the hard
call limit, which caps calls, not dollars. Calls run sequentially with a
default five-second pause (`--call-delay-seconds` 0–60), excluded from latency.
Only HTTP 429 retries, at most three per stage within budget, honoring
`Retry-After` or 5/10/20 seconds with jitter; a wait over five minutes stops the
run. Production never retries.

The dashboard binds loopback at `http://127.0.0.1:5180` (`--port` overrides),
runs one confirmed run at a time and keeps partial results on Cancel; see
[architecture](architecture.md#evaluation) for its security boundary. **Copy for
AI** includes answer keys, so keep exports private. Six human scores use 0
unusable, 1 needs edits, 2 ready or null unreviewed; labels and notes also come
from `--label` / `--notes`.

[Cases](../tools/FamilyLearning.Evaluation/cases.json) hold a prompt or fixed
`initialPlan`, optional input, up to three refinements and scoped replacements.
`expectedGeneratedMaterials`, `settingsOverride`, `additionalControlCount`,
`expectedLength` and `minPassageWords` / `maxPassageWords` are independent
checks; `reviewFocus` guides human review. Keep case IDs and expectations
stable, add cases only for real coverage gaps and never relax a check to raise
pass rates.

Reports go to `artifacts/evaluations/<run>/` or `--output`. `run.json` is the
authoritative checkpoint with exact requests, schemas, versions, content,
usage and safe diagnostics, never secrets or reasoning text. `summary.json` is
derived; unknown costs are null with coverage counts
([usage accounting][usage]).

Judge findings are advisory: each must quote a supplied field and never edits
output or scores. `HebrewJudge` pins the judge's model, reasoning effort and
strict `json_schema` mode; it shares only the app's key, endpoint and limits,
and reports record its profile separately, so generation profile changes stay
comparable under the same judge. Its schema is fixed: input fields carry ids,
and the evaluator resolves each returned id and verifies the quote. A
per-request enum of field paths made Google reject every review of a task with
materials (`INVALID_ARGUMENT`, 12/12 across three runs); the fixed contract
accepted the same four inputs. On 5 October, two calibration passes chose the
judge: Flash passed 6/8 controls with no false alarms, GPT-5.6 Terra 3/5 with
none (three calls were refused for account credit) and Claude Sonnet 5.5 2/8
with four. Flash stays pinned; changing it needs a new review version and
recalibration. Evidence is in `artifacts/evaluations/judge-repair-2026-10-05/`
($0.218 of a $1 cap). Calibration [controls][judge-controls] measure detection
of planted defects, not general accuracy ([same-model limits][judge]). Advisory
controls are reported without gating calibration, and at least one control must
gate. Comparison reports candidate-minus-baseline deltas, never a winner, and
requires matching suites, inputs, repeats and check versions; judge and human
deltas need matching judge setups and scored pairs. Only the [current report
format](../tools/FamilyLearning.Evaluation/EvaluationVersions.cs) is read. Run
exits: 0 pass, 1 failures or findings, 2 invalid input, 130 cancelled; compare
exits: 0 compatible, 1 incompatible, 2 invalid input.

## Verification and future changes

Run `scripts/verify.sh` for every change; after workflow changes, run
`scripts/publish.sh` and the isolated browser suite. These are engineering
checks, not AI quality scores.

Change one bounded hypothesis at a time: freeze cases, settings and checks,
register an adoption/stop rule, keep every failure and cost, compare to the
current baseline and stop on flat results. Do not add prompt exceptions for
individual holdouts, and keep claims proportional to the evidence. Maintain
this guide in place; keep experimental evidence in artifacts.

[gemini]: https://ai.google.dev/gemini-api/docs/generate-content/latest-model
[gemini-schema]: https://ai.google.dev/gemini-api/docs/structured-output
[vertex-schema]: https://docs.cloud.google.com/vertex-ai/generative-ai/docs/multimodal/control-generated-output
[reasoning]: https://openrouter.ai/docs/guides/best-practices/reasoning-tokens
[metadata]: https://openrouter.ai/api/v1/models
[fallback]: https://openrouter.ai/docs/guides/routing/model-fallbacks
[upstream-debug]: https://openrouter.ai/docs/api_reference/errors-and-debugging
[structured-output]: https://openrouter.ai/docs/guides/features/structured-outputs
[usage]: https://openrouter.ai/docs/cookbook/administration/usage-accounting
[judge]: https://arxiv.org/abs/2306.05685
[judge-controls]: ../tools/FamilyLearning.Evaluation/hebrew-review-samples.json
