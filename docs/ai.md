# AI guide

The maintained reference for AI configuration, evaluation and tuning at engine
revision 48. Product contracts live in the
[product specification](product-specification.md); implementation boundaries
live in [architecture](architecture.md).

## Current status

GPT-6.1 Sol (since 8 October, revision 35) uses native strict `json_schema`,
medium reasoning and provider-default sampling. **Structural acceptance does
not establish educational quality or superiority to the old one-shot flow.**
Keep parent review, server validation and explicit recovery. Generated text gets
one measured automatic polish; there is no production retry or model-specific code
path. Six acceptance cases passed three historical rounds (42 calls, no
retry/repair), before later prompt changes. Do not treat that sample as a
current reliability rate. The switch rests on small 8 October comparisons; see
[model comparison](#model-comparison).

Gemini 3.8 Flash is the measured rollback: change only `Ai:Model` to
`google/gemini-3.8-flash`. At revision 44 the same contract and everyday-edit
probes passed on both models ([Sol][activity-contract-44],
[Gemini contract][activity-contract-44-gemini],
[Gemini edits][activity-edits-44-gemini]). Gemini keeps every contract and
preservation rule but writes somewhat less faithful rewrites, so a rollback
trades quality for availability. Any other model needs its own probe run.

## Design and revisions

The engine uses concrete activity plans and atomic Create/Revise operations. New
generated texts get ideas, writing and polish before questions; existing
rewrites get no polish; supplied sources stay exact. Revision planning returns a
reply, clarification or validated change and never repairs invalid output (see
[architecture](architecture.md#durable-generation) and the
[chat design](activity-chat-design.md#ai-contract-and-context)). Evaluation runs
the same engine stages directly, without family data. Isolated tests do not
establish live-model quality.

The [prompt decisions](#prompt-decisions) below predate revision 39. Since then:

- **39–40:** activity-only prompts and schema; the staged operation contract is
  retired.
- **41:** restores the measured authoring omission rule, shares planning
  defaults with revision and keeps planning permissions out of content stages.
- **42:** a singleton enum replaces equal version bounds after the
  [live diagnostic][activity-schema-diagnostic]. The
  [final contract run][activity-contract-42-complete] passed all eight scenarios
  in 17 calls through the production engine and native adapter, including
  new-only generation and question append; this is contract evidence, not a
  content-quality benchmark.
- **43:** only a supplied text is transformed into a separate text; a requested
  change to a generated text changes it in place. The
  [everyday-edit run][activity-edits-43] passed all eight chat edits.
- **44:** the schema version is application-owned, which restores the Gemini
  rollback without a model-specific path.
- **45:** chat never adds the parent's own text and refuses such requests; not
  measured live.
- **46:** authoring reads the same conversation window as revision (six turns
  within 12,000 characters) as conversation behind the current plan, not as
  unresolved requests. Live on 10 October (6 calls, $0.024, then 20 calls) it
  returned to "the previous topic", re-applied nothing after a follow-up, and
  past the window asked for the first message's topic instead of guessing.
- **47:** a chat change can replace the learner title or instructions
  (`document`). Live, "remove the vowel marks" rewrote a first-grade title in
  place and an unrelated change left it alone. The planner also recorded "no
  vowel marks" as lasting guidance, rebuilding the questions; a prompt line
  against that changed nothing and was dropped.
- **48:** the plan gains `documentGuidance` (schema 3) for lasting
  title/instruction rules, outside content fingerprints, so the planner's
  choice of field decides whether questions rebuild. In the owner's own session
  on 10 October, three title/instruction niqqud requests each ran only the
  planner, recorded the rule there and rewrote the title or instructions with no
  question stage, and an answer explained the grade-2 niqqud default correctly.

The 1 October comparison did **not** meet its quality threshold: one-shot passed
9/9 structural trials and split 7/9, with split costing about 3× and taking
3.4× median provider latency. The one-shot path was removed; do not restore a
parallel legacy path or describe the switch as a measured quality win.

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
  formal words. Cost is about $0.010 per text, median 15 seconds (35 at p90);
  later runs measured a 26–27 second median.

- **Question thinking level (revision 36):** GPT-6.1 Sol pushed inference too
  far for young readers; one owner grade-3 easy set had 6 of 10 questions with
  hypotheticals, judged claims or the author's purpose. One sentence now matches
  the thinking to audience and difficulty and keeps inference close to the text
  for young readers and easy activities. On 18 fixed-text sets, far questions on
  grade 1–3 easy inputs fell from 4 of 46 to 0 and near inference rose from 24
  to 28, with requested inference kept and no wrong keys. The grade-7 control
  also lost its two far questions; watch older audiences.

- **Stage-scoped language rules (revision 37):** text writing, rewrite and
  polish no longer receive the question-only lines (app terms, answer-type
  names, question numbering), and the polish no longer receives the writing
  length rules that contradicted "keep the length". Authoring, ideas and
  question prompts are byte-identical. On 9 write-then-polish pairs the package
  caused no validation, Hebrew or content regression.

- **Answer-key scope (revision 38):** a real plan asked for a solution
  explanation in the answer key, which holds only the expected learner answer.
  Authoring now leaves such requests out and says so in assumptions. Explicit
  requests went from 4/4 written into guidance to 0/6, each with the
  assumption; plans that ask the learner to explain keep that requirement.
  Side effect: some plans add an accurate but redundant "the key holds only the
  answer" sentence to guidance.

Rejected or unproven; revisit only with new evidence:

- **Plain-language writing sentence:** "write in plain, everyday language ...;
  use a harder word only when the text teaches it". With the cleanup it cut
  hard or formal words from 20 to 16 over 9 pairs, short of the registered
  30% bar. Tested alone against revision 37 on 16 writing pairs: 75 hard or
  formal words versus 74, with equal validation and niqqud. Rejected.
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
- **Polish after rewrite:** suggestion-chip rewrites replace most of a text
  (body similarity 0.11–0.67) and skip the polish. A registered run on 20
  rewrites found 2 genuine corrections against the fresh-text bar of 3, none
  introduced. Not wired: it would also double the parent's wait (27 s + 27 s).
- **One text per activity:** removing the multi-text prompt lines, ID schema and
  `totalLength` from the writing call changed nothing measurable (18 vs 20
  issues; preferences 4/3/5 ties over 12 blind pairs). A cap is a product
  choice worth about 200 lines of activity code, not a quality lever. The child
  player shows every material, so a pasted source and its adaptation both reach
  the learner.
- **Hebrew-specialist polish (DictaLM 3.0 24B Thinking on Featherless):** on 35
  production Hebrew texts it was worse than the Gemini polish in every tested
  setup. Under the production contract 14 validated, with 0 genuine fixes and 1
  introduced error against Gemini's 8 fixes and none. It repeatedly turned the
  text into quiz questions. Narrower inputs with the production prompt validated
  none of 15 attempts. An exploratory minimal Hebrew prompt validated 25 with 15
  introduced errors (niqqud added or stripped against requirements, nonsense
  words) and 8 meaning changes. The integration also needed `max_tokens`,
  answers read from `message.reasoning`, streaming and disabled SDK retries, and
  met ~90 s provider caps. The model's recommended sampling (temperature 0.6,
  top-k 20, top-p 0.95) changed nothing: 4 of 15 returned, with 7 errors. No
  second provider was added.
- **Earlier provider trials:** model comparisons, provider exclusions and
  shorter material prompts produced no stable quality winner. DeepSeek strict
  authoring remains unverified after contract-description fixes. See
  [model comparison](#model-comparison) for the 8 October results.

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

## Model comparison

Prompt and schema changes had stopped producing measured gains, so on 8 October
the same production code ran three models, changing only configuration: the
vendors' recommended medium effort and unset sampling. Labels are the
assistant's and blind where possible; samples are small.

| Stage test                 | Gemini 3.8 Flash | Opus 5.5     | GPT-6.1 Sol  |
| -------------------------- | ---------------- | ------------ | ------------ |
| Writing issues (4 texts)   | 7                | 4            | 2            |
| Mean writing rank (1 best) | 2.75             | 1.75         | 1.5          |
| Inference questions (of 4) | 3                | 3.5          | 4            |
| Editor fixes (of 4)        | 2                | 2            | 1            |
| Writing call: cost, median | $0.013, 31 s     | $0.056, 32 s | $0.010, 14 s |

All 12 math keys were correct. No model fixed the ant claim, which came from the
shared idea, and each still made Hebrew errors (Opus: masculine agreement for
ants; Sol: `חָטִיף`). Only Opus simplified formal wording when editing, at four
to five times the cost. Both candidates accepted the material, polish and
question schemas in strict mode. OpenAI rejected the template schema
(`invalid_json_schema`) until revision 35: removing one part at a time traced it
to a nullable array of `$ref` items (control options), now an `anyOf` that both
providers accept.

End to end through the evaluation runner, GPT-6.1 Sol completed six cases
(reading grades 1, 3 and 7, a drill, word problems, a shared math scenario) with
every stage accepted and every automatic check passed, including exact
10-question drills without a schema count. Against the latest Gemini results,
blind: 2 issues against 5, preferences 2/2/2 ties. Sol was better on inference
and the grade-7 text, worse on one grade-1 niqqud word (`בְּמָה`) and one
incoherent scenario detail; all math keys were correct. The grade-7 and math
baselines predate the polish stage, which fixes some of the counted wording. A
reading activity cost about $0.056 against $0.046, with the text ready sooner
(51 s against 77 s for ideas, writing and polish). At revision 35 Sol passed
strict end to end on a select-control plan and an exact 10-question drill, and
Gemini passed the same plan. The owner made the switch; a wider read of real
activities is still advised, since Sol edited worst of the three and its
grade-1 niqqud is not error-free.

Effort stays medium. On fixed inputs, high cost about 1.8 times as much and took
twice as long (15 s against 28 s median) for no writing or question gain: 5
issues against 4 over 10 blind pairs. Its polish fixed 3 of 4 labelled errors
against 1 of 4, on only three texts; the app has no per-stage effort.

## Configuration

[appsettings.json](../backend/FamilyLearning.Api/appsettings.json) holds the
active profile. `scripts/configure-ai.sh` stores the key in development user
secrets outside the repo; `Ai__ApiKey` or `OPENROUTER_API_KEY` supply server
secrets, and Production ignores user secrets. Environment variables (`Ai__…`)
and development secrets (`Ai:…`) override the file; restart after changes.
Credentials never belong in reports.

| Setting                       | Contract                        |
| ----------------------------- | ------------------------------- |
| `Model`                       | Required OpenRouter model ID.   |
| `ResponseFormat`              | Schema, JSON object, or text.   |
| `SchemaInPrompt`              | Strict-mode prompt copy.        |
| `StrictQuestionCountLimit`    | Exact-count ceiling; 0–20.      |
| `ReasoningEnabled`            | True, false, or null.           |
| `ReasoningEffort`             | Supported effort; empty omits.  |
| `ReasoningMaxTokens`          | Budget; null omits.             |
| `Temperature`, `TopP`, `TopK` | Nullable sampling controls.     |
| `MaxOutputTokens`             | 1–32768; option default 8192.   |
| `RequestTimeoutSeconds`       | 1–300; default 180.             |
| `MaxRequestBytes`             | At most 512 KiB.                |
| `MaxSchemaBytes`              | At most 64 KiB.                 |
| `FallbackModel`               | Empty disables fallback.        |
| `IgnoredProviders`            | Empty array excludes none.      |
| `MaxPrice`                    | USD per million tokens; paired. |

The current profile requests 16,384 output tokens with a 180-second deadline,
no fallback model and no excluded providers. Medium reasoning and omitted
sampling follow [OpenAI's guidance][gpt6] (medium is the default; temperature
and top-p are not allowed with reasoning) and the
[OpenRouter reasoning mapping][reasoning]. The adapter sends
`reasoning: { effort, exclude: true }`; **exclusion hides returned thinking
text but does not disable reasoning or its billing.** Reasoning shares the
output ceiling, and output-limit responses are rejected. Token caps and
deadlines are application limits, not billing ceilings. `MaxPrice` sends
OpenRouter's [`max_price`][routing] at the model's current list price ($2 and
$10 per million prompt and completion tokens), so no route can charge more. The
cheaper Gemini rollback fits under it; only a pricier model needs a deliberate
raise. Spending itself stays bounded by an OpenRouter key limit: one family
uses a deployment, and per-family quotas come before others are invited.

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

The current strict profile targets GPT-6.1 Sol. Authoring and revision reuse one
plan schema; the native adapter sends it unchanged. The server validates every
response independently of provider enforcement.

- **No duplicate schema in the prompt** unless an explicitly configured endpoint
  needs `SchemaInPrompt`.
- **Only meaningful constraints.** Numeric bounds, field lengths and patterns
  reflect validator limits. Engine constants supply the fixed version and
  question cap; request-owned schemas supply exact counts and allowed IDs.
- **An application-owned version.** The plan's `schemaVersion` is a plain
  integer in provider schemas, and the server sets the current version while
  assigning IDs. Equal numeric bounds stalled Sol authoring twice
  ([diagnostic][activity-schema-diagnostic]). An integer enum works on Sol but
  is outside Gemini's response-schema subset, which accepts enums only on
  strings. Without a base plan, Sol returns version 1, so the server must own it.
- **Explicit nullable shapes.** Null-only branches use `["null"]`. A nullable
  array of `$ref` items uses `anyOf` with a null branch because the alternative
  type-array shape was rejected by OpenAI strict mode. Cross-field rules such
  as `choiceCount` remain in the prompt and server validator.
- **Descriptions carry validator semantics.** Activity schemas describe concrete
  settings and generated/supplied source roles. Reusable controls, adjustable
  fields and per-use overrides are absent.

Limit provenance: `StrictQuestionCountLimit` is endpoint-specific and measured
(20 for Gemini and GPT-6.1 Sol at six choices; 20 is also the product
maximum), because providers publish no budget; re-measure
it for a new model or a higher choice cap. New schema features also need live
acceptance within an explicitly authorized budget. Question count, choices,
materials, content size and field lengths are owner-set product
limits in the [product specification](product-specification.md). The 16,384
output tokens (validated up to 32,768) stay below the endpoint maxima
(128,000 for GPT-6.1 Sol, 65,536 for Gemini 3.8 Flash).

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
- **Grade-1 niqqud:** all four Gemini grade-1 texts of 7 October pointed
  `חָתוּל` as `חֲתוּל`, and three mispointed `כְּלַבְלַב`. Each tested model's
  polish fixed one of the two words, never both.
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

Keep each experiment together: original requests/results (including failures),
prompts, fixtures, calibration, comparisons, reviews and cost ledgers. Historical
probe sources explain those runs; use the current harness for new evaluations.
Do not retain copied executables, `bin`/`obj`, patch scripts or superseded
build/test logs. When removing an archived build, preserve unique schema/fixture
bytes under its experiment's `frozen-inputs/`; `index.json` maps original paths
to retained files and SHA-256 hashes. Never rewrite historical reports to make
them load under a newer format.

`artifacts/verification/` holds the latest isolated checks and the cutover receipt.
`artifacts/app/` is disposable publish output, regenerated by `scripts/publish.sh`;
its `data/`, if present, is persistent storage and must be preserved separately.
The early writing comparisons are grouped in `hebrew-writing-2026-10-05/`;
schema reviews are in `claude-strict-schema-2026-10-02/` and
`authoring-wire-review-2026-10-03/`, all under `artifacts/evaluations/`.

Costs are rounded; each experiment's `budget.json` holds the exact ledger.

| Run                                           | Calls  | USD (cap)       |
| --------------------------------------------- | ------ | --------------- |
| Early tuning, schema dirs of 2–3 October      | 370    | 1.47 + 1.63 res |
| One-shot comparison                           | 19     | 0.06            |
| `reasoning-effort-2026-10-05`                 | —      | 0.70 (2 shared) |
| `inference-line-2026-10-05`                   | —      | 1.00 (2 shared) |
| `factual-line-2026-10-05`                     | —      | 2.75 (3)        |
| `judge-repair-2026-10-05`                     | —      | 0.22 (1)        |
| `math-2026-10-06`                             | 73     | 0.66 (1)        |
| `math-formats-2026-10-06`                     | 70     | 0.65 (1)        |
| `difficulty-default-2026-10-07`               | —      | ledger (1)      |
| `polish-2026-10-07`                           | 138    | 1.29 (1.50)     |
| `stale-rewrite-2026-10-07`                    | 18     | 0.20 (0.50)     |
| `rewrite-polish(-confirm)-2026-10-07`         | 64     | 0.67            |
| `one-text-writing-2026-10-07`                 | 24     | 0.30            |
| `flagship-models-2026-10-08`                  | 23     | 0.57 (1)        |
| `gpt-end-to-end-2026-10-08`                   | 25     | 0.27 (0.50)     |
| `dictalm-polish-2026-10-08`                   | 9+     | at most 0.28    |
| `sol-strict-switch-2026-10-08`                | —      | 0.14 + probes   |
| `sol-effort-2026-10-08`                       | 17     | 0.31 (0.60)     |
| `question-level-2026-10-08`                   | 18     | 0.27 (0.50)     |
| `text-prompts-2026-10-08`                     | 36     | 0.34 (0.50)     |
| `plain-language-2026-10-08`                   | 32     | 0.27 (0.40)     |
| `answer-key-scope-2026-10-08`                 | 21     | 0.18 (0.20)     |
| `revise-planner-`, `activity-chat-review-`    | 24     | 0.34 (0.30)     |
| [Rev 41 contract][activity-contract-41]       | 4      | 0.03 + 0.19 res |
| [Rev 42 schema][activity-schema-diagnostic]   | 2      | 0.02            |
| [Rev 42 contract][activity-contract-42]       | 11     | 0.08            |
| [Rev 42 final][activity-contract-42-complete] | 17     | 0.13            |
| [Rev 42 edits][activity-edits-42]             | 4      | 0.03            |
| [Rev 43 edits][activity-edits-43]             | 17     | 0.11            |
| [Rev 44 Sol][activity-contract-44]            | 17     | 0.13            |
| [Rev 44 Gemini][activity-contract-44-gemini]  | 17     | 0.13            |
| [Gemini edits 44][activity-edits-44-gemini]   | 3 + 17 | 0.02 + 0.13     |

"res" marks unresolved timeout reserves. The planner probes overran their cap
by $0.04. The revision 41–42 contract runs shared a $1 cap ($0.45 used), the
42–43 edit runs a $0.45 guard ($0.15) and the revision-44 runs an estimate of
about $0.30 ($0.41). The revision-41 probe timed out during authoring, the first
revision-42 run stopped on a probe comparison error that was then fixed, the
revision-42 edit run stopped when a story became a separate poem (scoped in
revision 43), and the [stopped Gemini run][activity-edits-44-gemini-stopped]
failed a literal wording check that a correct story did not meet. None retried
automatically.

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

For the final activity-only contract, preview the fixed probe:

```bash
./scripts/evaluate-ai.sh --activity-contract-probe
# Only after an explicit dollar budget is approved:
./scripts/evaluate-ai.sh --activity-contract-probe --live --budget-usd 1
```

This separate probe runs eight synthetic cases, at most 17 calls, through the
production engine and native adapter: empty/populated revision targets,
authoring defaults and unsupported extras, reply/refusal, metadata rename,
new-only writing/polish beside retained texts, and question append.
`--protocol edits` instead runs eight everyday chat edits on one current
grade-3 story, also at most 17 calls: a topic change, a poem with the same
plot, a half-length story, an easier replacement for a targeted question,
removing a named question, a focused added question, a vocabulary focus and a
decrease that names no question. Each case checks the scope the server derives,
then runs the worker's stages and checks that untouched content keeps its IDs
and text; wording checks run last. `--model gemini` runs either protocol on
Gemini 3.8 Flash, routed only to Google AI Studio or Vertex at standard-tier
caps ($0.75/M input, $3.75/M output); only the model changes. The guard reserves
about $0.21 per Sol call and $0.08 per Gemini call, so a budget must exceed the
expected spend by one reservation for the last call to start. Neither protocol
exercises the worker/API or establishes content quality.
They have no retry, judge, fallback or schema downgrade and stop on a failed
expectation. Keep the production strict schema, medium reasoning and 16,384
output-token profile.
Probe-only routing selects standard OpenAI and caps prices at $2/M input and
$10/M output; requests are bounded to 64 KiB, never truncated. Reserve each
call's wire byte count plus 4,096 input tokens and all allowed output tokens
before sending; unknown costs retain that reserve. Lower budgets can stop
incomplete; the modeled maximum for all calls is $5.152768 (accepted budgets
are at most $6). These local checks are not a provider billing guarantee.

Each probe directory freezes prompts/fixtures in `protocol.json`, engine checks
and snapshots in `probe.json`, and sanitized wire requests, outputs and the cost
ledger in `transport.json`. Inspect the retained output for Hebrew, niqqud and
unsupported-detail behavior; passing structural checks alone is insufficient.
No production learning records are read or changed.

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
`initialPlan`, up to three refinements and scoped replacements.
`expectedGeneratedMaterials`, `expectedLength` and
`minPassageWords` / `maxPassageWords` are independent
checks; `reviewFocus` guides human review. Two content checks need no fixture:
`calculationKeys` recalculates every bare calculation prompt exactly (rational
arithmetic, so `1/4 + 1/6 =` must key `5/12` or `0.41666…` never) and compares
it with the key. A key in another form, such as a remainder, stays unchecked:
`calculations` records applicable, checked and incorrect keys, and the check is
absent when no key was readable. `signDirection` fails any learner-visible
text that puts `<` or `>` beside Hebrew letters, where bidi mirroring reverses
the sign. Keep case IDs and expectations stable, add cases only for real
coverage gaps and never relax a check to raise pass rates.

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

[gpt6]: https://developers.openai.com/api/docs/guides/latest-model
[reasoning]: https://openrouter.ai/docs/guides/best-practices/reasoning-tokens
[routing]: https://openrouter.ai/docs/features/provider-routing
[metadata]: https://openrouter.ai/api/v1/models
[fallback]: https://openrouter.ai/docs/guides/routing/model-fallbacks
[usage]: https://openrouter.ai/docs/cookbook/administration/usage-accounting
[judge]: https://arxiv.org/abs/2306.05685
[judge-controls]: ../tools/FamilyLearning.Evaluation/hebrew-review-samples.json
[grade3]: https://meyda.education.gov.il/files/Mazkirut_Pedagogit/math/primary-school/math2023/Newprogramgrade3.pdf
[activity-contract-41]: ../artifacts/evaluations/activity-contract-20261009T151649Z-f75c37bfb13c41a4964795151db832e9/review.md
[activity-schema-diagnostic]: ../artifacts/evaluations/activity-schema-diagnostic-20261009/review.md
[activity-contract-42]: ../artifacts/evaluations/activity-contract-20261009T160621Z-80857ab3351a4f918cf4a0fed289af8d/review.md
[activity-contract-42-complete]: ../artifacts/evaluations/activity-contract-20261009T181523Z-78987c34b464423e8c16be451b165222/review.md
[activity-edits-42]: ../artifacts/evaluations/activity-edits-20261009T195734Z-1edb04ddc70444a499e77f8d8665cbd0/review.md
[activity-edits-43]: ../artifacts/evaluations/activity-edits-20261009T200011Z-67b459e1ef7b43b4abdd9f1165267b57/review.md
[activity-contract-44]: ../artifacts/evaluations/activity-contract-20261009T201752Z-f6a6adc9c62e4c77b87f986406579d45/review.md
[activity-contract-44-gemini]: ../artifacts/evaluations/activity-contract-gemini-20261009T202112Z-68eb07f91d33447f94da0ac692eb5b19/review.md
[activity-edits-44-gemini-stopped]: ../artifacts/evaluations/activity-edits-gemini-20261009T202519Z-e0255017593a4f28aaa5e26e6c001203/review.md
[activity-edits-44-gemini]: ../artifacts/evaluations/activity-edits-gemini-20261009T202635Z-114e168c065b47049560c60764d86254/review.md
