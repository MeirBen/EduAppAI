# AI guide

The maintained reference for AI configuration, evaluation and tuning, as of
6 October 2026. Product contracts live in the
[product specification](product-specification.md); implementation boundaries
live in [architecture](architecture.md).

## Current status

Gemini 3.8 Flash uses native strict `json_schema`, medium reasoning and
provider-default sampling. **Structural acceptance does not establish
educational quality or superiority to the old one-shot flow.** Keep parent
review, server validation and explicit recovery. Generated text gets one
measured automatic polish; there is no production retry or model-specific code
path. Six acceptance cases passed three historical rounds (42 calls, no
retry/repair), before later prompt changes. Do not treat that sample as a
current reliability rate.

## Design and cutover decision

The owner chose prompt → editable plan → material ideas/writing/polish → parent
text review → questions → reviewed immutable snapshot for editing, source
preservation and checkpoint recovery. Supplied sources and question-only
activities skip material generation. See
[architecture](architecture.md#ai-and-persistence) for stage ownership,
replacement, staleness and persistence.

The 1 October comparison did **not** meet its quality threshold: one-shot passed
9/9 structural trials and split 7/9, with split costing about 3× and taking
3.4× median provider latency. The one-shot implementation was removed; do not
restore a parallel legacy path or describe the cutover as a measured quality win.

## Prompt decisions

These decisions summarize 4–6 October experiments. Samples are small and
assistant-reviewed (some blind), not educator validation or reliability rates.
Keep the rationale below; exact requests, scores and costs belong in
[retained evidence](#costs-and-retained-evidence).

Retained:

- **Question replacement:** sibling questions and instructions provide context;
  duplicate replacements fell from 4/8 to 0/8.
- **Expressions:** whole-item calculations avoid reversed Hebrew sentence
  rendering (16/25 before, 0/25 after); requested word problems were retained.
- **Language presentation:** authoring records niqqud only when requested;
  writing uses full niqqud for beginning readers through grade 2 unless
  overridden. Paragraph guidance preserves prose paragraphs and poem lines.
- **Inference (revision 26):** require interpretation rather than literal recall
  when requested; genuine inference increased from 16/24 to 23/24, but coverage
  is still imperfect.
- **Rewrite scope (revision 30):** rewrites stay within effective requirements.
  Topic-changing instructions previously dropped the learning goal; tested
  variants then kept it. An AI rewrite clears the stored premise idea.
- **Math contracts (revision 31):** authoring guidance and schema descriptions
  clarify range assumptions, source roles and answer formats; the precise
  rules live in the [specification](product-specification.md#contracts).
  Paired trials had 23/23 correct arithmetic keys in each arm, preserved explicit
  two-digit bounds and disclosed the final short-request range assumption.
  Reading controls retained source/niqqud behavior; no arithmetic-quality gain
  was demonstrated. Fraction/remainder instructions were not separately tested.
- **Difficulty default (revision 33):** a request that names no difficulty
  now defaults to easy through third grade and medium above (was medium for
  all). Basis: medium third-grade texts used 6.1 formal words against 4.4 at
  easy (p ≈ 0.05, 6 October). Candidate runs: grade 1 and four grade-3 plans
  defaulted to easy, grade 4 and 7 stayed medium, and a grade-3 "up to 1000"
  drill kept its range (results to 800) with 20/20 correct keys. One ants pair
  read plainer at easy. Deviation: the registered rule required every grade 1–3
  plan to disclose the default, but `space-reading-grade3` did not (6 of 7 plans
  did); it was adopted anyway because the plan editor always shows the
  difficulty setting.
- **Comparison signs (revision 32):** comparison drills wrote every option and
  instruction as Hebrew words plus a sign (`קטן מ־ (<)`), which real Chromium
  displays as `(>)` through bidi mirroring, in 4/4 such baseline drills; the
  other 2 baseline drills planned sign-only options and were rejected because
  the model emits a bare `=` as `"= "` or `"=\""`. One `LanguageQuality`
  sentence names the relation in words outside whole-item expressions:
  4/4 candidate fraction comparisons had no reversed field, all 32 keys were
  correct. Question text is now trimmed before validation, which accepts the
  `"= "` form when a parent explicitly asks for sign options (3 of 4 observed
  rejections); the `="` form still fails. Probe keys: 51/52 correct across
  remainders, fractions, decimals, order of operations and word problems.

- **Text polish (revision 34):** after writing, one minimal-edit call reviews the
  generated text for its audience (spelling, grammar, agreement, niqqud,
  unnatural or too-formal wording, clearly wrong facts) in the writing schema.
  On 31 saved texts it fixed a meaning error, agreement, niqqud vowels and a
  factual imprecision, with three minor style regressions. A pre-registered
  confirmation on 20 fresh texts found 5 genuine corrections in 4 texts (a
  factual claim, a construct-state error, number agreement, two wrong words) and
  no introduced error; 14 came back unchanged. Validation rejected 0 of 51
  polishes. It left the ant-brood misconception in 6/6 texts and barely changed
  formal words. Cost is about $0.010 per text, median 15 seconds (35 at p90).

Rejected or unproven; revisit only with new evidence:

- **Question polish:** the same pass over questions, with code guards keeping
  formats, points, option positions, keys and numbers, was safe (0 of 53
  rejected, no harmful edit) but barely simplified wording: formal words per
  grade-3 set went from 2.33 to 2.21, and 2.12 after one operational revision,
  against a bar of 1.17. Not shipped; parent review and the "ניסוח פשוט וברור
  יותר" suggestion remain.

- **Less reasoning:** lower cost/latency came with more Hebrew errors; one
  Gemini comparison found 9 findings versus 1. Disabled reasoning also failed
  quality checks. Medium remains the active setting.
- **Prompt expansion:** extra Hebrew grammar, everyday wording, factual checks,
  math checklists and answer-check instructions were flat or harmful. A generic
  factual line left errors in 6/106 versus 7/94 texts. Worked question examples,
  definitions and shared-direction reminders also showed no dependable gain.
- **Difficulty mapping:** prescribed inference quotas and separating difficulty
  from wording missed adoption thresholds. Hard alone does not specify operand
  sizes. Two digit-limit prompt variants failed to reliably change the grade-3
  tendency and were removed; explicit factor/divisor ranges worked. The
  [Israeli curriculum][grade3] supports one-digit factors for distributivity
  and whole tens/hundreds, not a universal single-digit cap.
- **Rounded numeric keys:** one hard money problem keyed `25.17` for a value of
  25.1666…; five further fixed-plan batches had 30/30 exact keys (1 inexact in
  36), below the registered threshold, so no exactness sentence was added.
- **Material representation:** a paragraph array removed poem stanza breaks and
  reduced prose paragraphing. Keep string bodies. Naming characters in premise
  ideas narrowed variety; always selecting the first tied idea favored the most
  typical premise. Neither was retained.
- **Earlier provider trials:** model comparisons, provider exclusions and
  shorter material prompts produced no stable quality winner. DeepSeek strict
  authoring remains unverified after contract-description fixes.

## Material variety

The [generation architecture](architecture.md#ai-and-persistence) owns the
five-idea stage, bounded family history and application-drawn tie-breaking.
The idea stage adds one call to generated material and the polish another;
supplied sources and question-only work make neither.

Revision 29 trials on two grade-3 story topics found no repeated family premise
in 24 stories, versus 25–31% with revision 26. Without relevant history, all
51 idea calls tied at zero, which is why the app draws ties. Question history
reduced repeated targets, but 8/48 later questions fell back to literal recall
and one set missed requested inference. Cost rose from $0.0119 to $0.0153 per
story, median latency from 23 to 34 seconds. Variety is best effort, not a
uniqueness or educational-quality guarantee.

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
it for a new model or a higher choice cap. New schema features also need live
acceptance within an explicitly authorized budget. Question count, choices, controls,
options, materials, content size and field lengths are owner-set product
limits in the [product specification](product-specification.md). The 16,384
output tokens (validated up to 32,768) stay below the endpoint maximum of
65,536.

## Unresolved failures

- **Content:** incomplete inference coverage, unnatural Hebrew/niqqud, factual
  errors and wrong answer keys remain possible. Observed errors include claims
  that orbit has no gravity and that ants move brood deeper during rain. The
  text polish corrects some slips, but the same model keeps claims it believes,
  such as the ant one, and questions are not polished.
- **Shared math sources:** one structurally valid trial confused a book price
  with a booklet price and supplied the wrong key. Baseline and final outputs
  added quantities despite source-only requirements. A stronger answer-check
  line was flat (8/8 keys correct per arm, extra quantities still present) and
  removed. Parent review remains necessary.
- **Hand-graded exact answers:** fraction and remainder drills choose
  `text-input` (8/8 fraction questions; 3 of 8 remainder questions), so a parent
  grades `5/12` by hand. Numeric input holds one decimal by contract; widening
  it or adding a typed exact-answer format is a product decision, not a prompt
  fix. The planner is not told which formats grade automatically.
- **Zero-reasoning calls:** Gemini's dynamic thinking reported 0 reasoning
  tokens in 9 of 109 medium-effort question calls, all 6 on one money plan;
  keys stayed correct except the rounded one above.
- **DeepSeek:** the last strict authoring trial left `choiceCount` null once
  and invented adjustable counts in all three plans; fixes were not retested.
- **Availability:** Google 429/504 and AI Studio 503 responses were intermittent
  serving failures, not evidence that a schema or quality change was needed.
- **Judge blind spot:** all three tested judges missed `נמלות` (correct:
  `נמלים`) in `reported-ants-defects`. This control is advisory, not a calibration
  gate. A same-model judge and passing schema checks cannot certify content.

## Costs and retained evidence

Evidence is local and ignored under `artifacts/evaluations/`: exact requests,
responses, versions, validation, reviews and per-experiment `budget.json` files.
Preserve failures and never edit old reports to match new code. These records
may be absent in a fresh checkout; the decisions above remain the maintained
summary. Costs below are USD; reserves are allowances, not confirmed charges.
Historical caps do not authorize new paid runs.

- Early tuning: 370 calls, $1.4666050906 known plus $1.630131600 reserved
  ($3.0967366906 combined). Reserves included 28 HTTP 400 rejections later shown
  as zero cost. An earlier one-shot comparison was separate: 19 calls,
  $0.062444323. Schema evidence: `gemini-strict-contract-2026-10-02/`,
  `gemini-strict-root-cause-2026-10-02/`, `gemini-*-limits-2026-10-03/` and
  `length-contract-2026-10-03/`.
- `reasoning-effort-2026-10-05/` ($0.704) and
  `inference-line-2026-10-05/` ($0.996) shared a $2 cap.
  `factual-line-2026-10-05/` used $2.745 under a separate $3 cap.
- `judge-repair-2026-10-05/`: $0.218 of $1; calibration and fixed-schema evidence.
- `math-2026-10-06/`: 73 calls, $0.660264 of $1, no unknown costs; frozen
  comparisons, reading controls, rejected digit-limit variants, semantic failures
  and `review.md` / `verification.json`.

- `math-formats-2026-10-06/`: 70 calls, $0.64560975 of $1, no unknown costs;
  answer-format probe, comparison-sign and rounded-key hypotheses, browser
  evidence (`bidi.png`), final controls and a four-case reading regression.
- `difficulty-default-2026-10-07/`: difficulty-default candidate and a broad
  revision-33 regression; cost in its `budget.json`, within a separate $1 cap.
- `polish-2026-10-07/`: text and question polish replays over saved outputs
  and a final two-case live check, 138 calls, $1.2922 of a $1.50 cap, no
  unknown costs; `protocol.txt` records each registered rule before its run,
  with blind labels and analysis.
- `stale-rewrite-2026-10-07/`: rewrites of stale text through the real rewrite
  call; without an instruction, 3 of 6 titled texts came back unchanged, which
  `TaskAssembly.ReplaceMaterial` now accepts under the current requirements.
  18 calls, $0.19675 of a $0.50 cap, no unknown costs.

Retired design documents: `documentation-history-2026-10-01.zip`.

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
checks; `reviewFocus` guides human review. Two content checks need no fixture:
`calculationKeys` recalculates every bare calculation prompt exactly (rational
arithmetic, so `1/4 + 1/6 =` must key `5/12` or `0.41666…` never) and compares
it with the key, and `signDirection` fails any learner-visible text that puts
`<` or `>` beside Hebrew letters, where bidi mirroring reverses the sign. Keep
case IDs and expectations stable, add cases only for real coverage gaps and
never relax a check to raise pass rates.

Reports go to `artifacts/evaluations/<run>/` or `--output`. `run.json` is the
authoritative checkpoint with exact requests, schemas, versions, content,
usage and safe diagnostics, never secrets or reasoning text. Generated text is
recorded as written and as polished, in separate steps of the same run.
`summary.json` is derived; unknown costs are null with coverage counts
([usage accounting][usage]).

Judge findings are advisory: each must quote a supplied field and never edits
output or scores. `HebrewJudge` pins its model, reasoning and strict schema
separately from generation; changing it needs a new judge version and
recalibration. Input fields carry IDs whose returned quotes are verified. Keep
that fixed contract: per-request field-path enums caused Google to reject all
12 tested reviews containing materials.

Calibration [controls][judge-controls] measure planted-defect detection, not
general accuracy ([same-model limits][judge]). Advisory controls do not gate
calibration; at least one control must gate. Flash passed 6/8 controls with no
false alarms in the retained calibration.

Comparison reports candidate-minus-baseline deltas, never a winner, and
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
[grade3]: https://meyda.education.gov.il/files/Mazkirut_Pedagogit/math/primary-school/math2023/Newprogramgrade3.pdf
