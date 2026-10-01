# Gemini tuning after the profile review

Status: comparisons and isolated verification complete. Retain only the shared
material-replacement rules; keep medium reasoning and the existing question prompt.

The owner approved three bounded recommendations: compare reasoning effort,
try one worked example for requested question purposes, and share existing
material-writing rules with material replacement. No architecture redesign,
model-specific branch, automatic repair, weakened validator or Git mutation.

## Method and reasoning decision

Freeze engine revision 5, the original 27 cases and current Gemini 3.8 Flash
profile. Keep strict JSON schema, omitted sampling controls, no fixed thinking
token budget, 16,384 requested output tokens, 180-second deadline and no model
fallback. The private evaluation ceiling is 65,536 request bytes in both arms;
production remains 512 KiB. Existing engine services, assembly, strict validators
and case checks run without learning-record persistence.

Compare low and medium on fixed reading, supplied bilingual and numeric cases,
three repetitions per arm, alternating order. Two independent assistant reviewers
score shuffled, blinded content using the saved six-dimension rubric. A usable
activity needs all automatic checks and no substantive content correction. Missing
responses remain unscored and unusable. Human-review fields remain null.

| Initial reasoning arm | Strict passes | Quality-usable activities |
| --------------------- | ------------: | ------------------------: |
| Medium                |           7/9 |                       4/9 |
| Low                   |           6/9 |                       6/9 |

Four upstream request failures are retained, two per arm. Low also generated a
151-word passage against the unchanged 100–150 range; it was rejected. Medium
produced a verb-tense mismatch and two library word problems that used the Hebrew
verb for lending when describing students borrowing books. Arithmetic was correct.
The second reviewer initially allowed the latter wording, then agreed while still
blinded that it required correction for elementary-school exercises. Preserve both
original ratings and the adjudication.

For completed activities, median summed stage latency was 16.65 seconds for medium
and 5.75 seconds for low; median cost was $0.00677625 and $0.002108625 respectively.
These conditional medians exclude failed activities and evaluator waiting time;
they are not reliability estimates or user-perceived end-to-end latency.

Low provisionally met the registered alternative gate of two more usable
activities without a correctness/language regression. Confirmation used original
number-gender authoring and numeric question replacement, with matched settings
and alternating order. Both settings returned an empty authored proposal and
failed that case. Low also replaced a question with an exact duplicate of another
exercise. Medium returned three distinct, correct exercises. Confirmation was
1/2 strict for each arm, but 1/2 usable for medium and 0/2 for low.

Retain **medium**: low failed the confirmation no-regression requirement. Do not
pool the exploratory and confirmation results into a general model-quality claim.

## Provider evidence

The owner supplied three failed generation IDs. Read-only OpenRouter metadata
confirmed Google 429/504 and Google AI Studio 503 responses, each with zero cost.
One failure lacks an ID/cost and retains its full reservation. Later successful
requests demonstrate intermittent availability, not a guarantee of stability.

The empty authoring proposals are a separate unresolved problem. All twelve schema
references resolve; the plan requires nine properties. Both responses stopped
normally below the output cap. Native adapter tests confirm complete schema
preservation, `strict: true` and `require_parameters: true`. Empty proposals came
through both serving providers. No app-side schema defect was found.

Google documents references and unions in its [generation configuration][google-schema].
OpenRouter describes [endpoint-specific schema enforcement][router-schema]. An
upstream compliance or translation problem is an inference; the forwarded Google
schema is unavailable. Preserve rejection and investigate these generation IDs
before changing schema representation or adding a workaround:

- Medium: `gen-1790881909-U5JOfcRZwvJzNCwL5Din`.
- Low: `gen-1790881938-v19J3UwkWAkhuyKkFalI`.

Reasoning remains enabled. The adapter already sent `exclude: true` with DeepSeek
and continues doing so with Gemini. It excludes returned thinking text, not
thinking or billing, as [OpenRouter documents][reasoning]. Medium trial responses
reported nonzero reasoning tokens; dashboard visibility alone is not an enablement
check.

[google-schema]: https://ai.google.dev/api/generate-content#v1beta.GenerationConfig
[router-schema]: https://openrouter.ai/docs/guides/features/structured-outputs
[reasoning]: https://openrouter.ai/docs/guides/best-practices/reasoning-tokens

## Question-purpose experiment

Freeze all six material-valid medium snapshots from the reasoning comparison,
including the two whose original question calls failed. They contain three distinct
stories and three captures of the same supplied bilingual passage: four material
contexts, not six independent passages. Replay one baseline and one candidate
question call per snapshot, alternating order.

The candidate adds one worked example distinguishing retrieval, inference and
relationships, and explicitly limits its application to the task's requested
purposes/languages. Only batch question guidance changes; single-question
replacement remains identical. The private `question-snapshot-v1` probe reuses
existing generation, assembly, release validation and original case checks.

All twelve calls returned structurally valid activities. Retention additionally
required at least two more usable activities, no strict-count decrease and no
correctness, grounding, language or answer-clarity regression.

| Question arm   | Strict passes | Quality-usable activities |
| -------------- | ------------: | ------------------------: |
| Existing       |           6/6 |                       4/6 |
| Worked example |           6/6 |                       2/6 |

Reject the worked example and skip its conditional holdouts. It still produced
retrieval questions where inference was requested and added a wording error:
marking seedlings with signs became marking the signs themselves. The intended
numeric key remained correct. The inherited passage-tense defect was scored
equally in both arms.

Resolve reviewer disagreements while blinded: a temporal relationship can count
without an inference or a cross-sentence requirement; an explicitly named
problem–solution link is insufficient as the separate inference exercise. Both
reviewers agreed after adjudication. Preserve original reviews and the addendum;
do not add prompt rules or reroll outputs after this regression.

## Material-replacement experiment

Separately extract the existing body-only counting, strict-length and no-filler
instructions into a shared constant. Add them to material replacement with an
explicit reminder that aggregate length includes unchanged generated bodies and
excludes supplied sources. A compiled check confirms the initial material prompt
and every other stage prompt remain byte-for-byte unchanged.

Use three frozen reading snapshots and one accepted two-material snapshot. Each
gets one baseline and one candidate replacement, alternating order. The private
`material-snapshot-v1` probe uses the native replacement service and assembly,
checks strict material readiness and measurements, and verifies that unrelated
materials, questions, title and instructions stay unchanged. Existing questions
become stale by design; material acceptance is not whole-activity readiness.
Offline negative controls reject a wrong target and an invalid strict length.

Candidate retention requires every strict replacement check to pass and no
reviewed material-quality regression. A tie can justify the shared contract, but
cannot justify a quality-improvement claim. Confirmation used frozen accepted
early-reader-niqqud and older-reader sources, with matched baseline and candidate
calls. No fixture or strict threshold was weakened.

| Material sample | Baseline strict / usable | Candidate strict / usable |
| --------------- | -----------------------: | ------------------------: |
| Training        |              4/4 and 4/4 |               4/4 and 4/4 |
| Confirmation    |              2/2 and 1/2 |               2/2 and 1/2 |

Both reviewers accepted the training materials. The first blinded pack lacked
prior bodies; a separate pack and preserved addenda then verified novelty against
the originals. Two-passage replacement permits modest novelty because the
unchanged continuation constrains the facts. Conditional calls had already begun
before these addenda; the addenda found no score change.

For confirmation, both reviewers accepted the fully pointed first-grade material.
One reviewer flagged overly certain pollution-prevention wording in both
older-reader passages; the other accepted both. Use the conservative scores above
and preserve the disagreement. This does not support a general quality gain, but
neither review finds a measured dimension-score regression between the two arms.
Human factual/language review remains necessary.

Retain the shared rules under the registered tie/cleanup criterion. Only material
replacement gains instructions; initial material generation and every other stage
prompt remain identical. Advance the shared engine revision to 6, keep schema
version 1, and leave model settings, server validation and the question prompt
unchanged. Remove the experimental question example from production.

## Evidence and cost controls

Private artifacts are under `artifacts/evaluations/gemini-tuning-2026-10-01/`:
frozen inputs/prompts/settings, protocols, original reports, prices, failures,
source hashes, blinded packs/keys, separate assistant reviews and adjudications.
Rejected candidates remain evidence; they are absent from production code.

After the early outages, a private loopback transport forwarded unchanged request
and response bytes, captured only bounded identifiers/statuses, and fetched
read-only generation billing. It never logged credentials or content and added no
production transport or retry. Isolated checks cover response preservation,
cancelled-client late responses, upstream resets, metadata lookup timeouts,
reordered retry accounting, ambiguous-cost reservations and cross-batch outages.

The audit found an early stop-rule deviation: after the second unknown-cost
failure, three further requests ran before diagnosis. All eight initial attempts
remain in the comparison. Peak cumulative charged/reserved exposure was
$1.983489706, below the $2 cap. Three later zero-cost confirmations released
$0.94556160. The corrected admission/outage guards apply to subsequent runs;
they do not erase the initial deviation.

The closed ledger records **53 new application requests**, **$0.35420100** known
cost and **$0.31518720** reserved for one unmeasured failure. Including the earlier
closed ledger, known cost is **$0.730849456** and unknown-cost reserves total
**$0.639590400**, or **$1.370439856 charged/reserved against the $2 cap**.
No unknown failure is counted as free. OpenRouter may make multiple internal
provider attempts for one application request; the application call limit does
not count or control those internal attempts.

This is a bounded exploratory study with assistant judgments, not a human Hebrew
or educational review. It does not establish that split generation is better than
the former one-shot design. The unresolved authoring result remains a practical
limitation even when local code tests and fixed-plan generation succeed.

## Code and verification

The retained code is confined to `TaskEngine/Ai/AiPrompts.cs` and
`TaskEngine/EngineVersions.cs`. One private compile-time constant owns existing
material-writing instructions shared by batch generation and single replacement.
It adds no runtime allocation, helper layer, AI call, retry, schema transformation
or output repair. Rejected prompts and private orchestration stay in ignored
evaluation artifacts, outside production and the normal evaluator.

`scripts/verify.sh` passed: 534 backend tests, 98 Angular tests, 23 dashboard
tests, locked restore, builds, formatting, Markdown lint and browser-test
TypeScript checking. No browser workflow changed, so the interactive cutover
suite was not rerun. Local transport/accounting checks, frozen-snapshot negative
controls and compiled prompt-scope checks also passed. No Git mutations or
learning-record writes were performed.
