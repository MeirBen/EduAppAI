# Follow-up AI quality comparison

Status: paid experiments and isolated verification complete.
This follows the
[post-cutover tuning](2026-10-01-post-cutover-ai-tuning.md), using its retained
engine revision 5 as the baseline. The owner authorized the review, one alternate
model and a targeted question-prompt experiment within the same $2 total budget.
No architecture changes, Git mutations or application-record writes were made.

## Model comparison

Compare the current DeepSeek V4.1 Flash profile with Gemini 3.8 Flash on the
unchanged fixed-reading and fixed-bilingual cases, three repetitions each.
Alternate model order between repetitions. Change only the model; keep the
prompts, schema, reasoning, sampling, resolved inputs and strict checks fixed.

| Model       | Automatic passes | Quality-usable activities |
| ----------- | ---------------: | ------------------------: |
| Current     |              5/6 |                       2/6 |
| Alternative |              6/6 |                       2/6 |

Two independent assistant reviewers assessed shuffled, model-blinded content.
Both returned the same usable counts. Usable means all automatic checks and all
six quality dimensions pass without substantive editing. The alternate failed
the preregistered requirement for two additional usable activities. Keep the
current model and skip the conditional model holdouts. This small sample does
not establish that either model is generally better.

Both models sometimes supplied retrieval questions when inference or a
relationship between details was requested. Each also produced a material
language defect. Supplied bilingual sources were preserved exactly.

## Question-purpose experiment

Freeze the five accepted current-model snapshots: two generated stories and
three captures of the same supplied bilingual passage. These represent three
distinct material contexts, not five independent passages. Replay two question
calls per snapshot for each arm. The private probe invokes the existing question
service, assembly, validators and original case checks without persistence or
material regeneration. Its reports explicitly use `question-snapshot-v1`.

Test only three added sentences defining retrieval, inference and relationships
and requesting coverage of the specified purposes. Preserve the same model,
profile, schemas, source bodies and effective inputs. The engine revision
identifies the experimental wording; it changes no validation contract.

The registered gate requires at least two more quality-usable activities out of
ten, no lower automatic success, and no new correctness, grounding or language
regression. Provider timeouts count as failures; unknown costs retain their full
reservations. Do not reroll failures or promote a candidate on successful
responses alone.

| Question arm              | Automatic passes | Quality-usable activities |
| ------------------------- | ---------------: | ------------------------: |
| Existing prompt           |            10/10 |                      4/10 |
| Added purpose definitions |             8/10 |                      5/10 |

Use the lower independent score after a documented blinded adjudication. One
reviewer initially required related details to occur in different sentences;
the rubric has no such restriction. That reviewer corrected the score without
seeing the mapping key. Preserve both original reviews and the addendum.

The candidate improves some inference questions but falls short of the required
two additional usable activities. It also introduces a question-option grammar
error: `מצאה אותה` refers to the masculine `כדור`. Both reviewers found the error.
Another question uses a rain-stopping coat as an implausible distractor. Existing
source grammar defects remain attributed to the source in both arms. Reject the
candidate and restore revision 5; preserve its source snapshot and evidence.

The two failed calls timed out after 180 seconds with no captured response or
cost. All eight returned candidates passed strict validation. Neither excluding
the timeouts nor treating them as prompt failures would justify promoting this
candidate on content quality alone.

Independent code review also identified a scope risk: the added batch-coverage
sentence lives in shared question guidance and therefore also reaches
single-question replacement. The question-only probe does not exercise that
replacement behavior. This is a competing-instruction risk, not an observed
replacement failure.

## Provider routing

The owner subsequently reported that the OpenRouter dashboard attributes the
timeouts to DekaLLM, which remained enabled. Archived public endpoint metadata
confirms its slug, `dekallm`. The timeout artifacts themselves contain no serving
provider identity, so preserve this attribution as owner-reported evidence.

Add `Ai:IgnoredProviders` through the existing OpenRouter registration and SDK
extension point, using the native
[`provider.ignore` option](https://openrouter.ai/docs/guides/routing/provider-selection#ignoring-providers).
Configure DekaLLM as the excluded provider. Validation bounds the list and slugs;
`require_parameters` remains enabled. The nonsecret profile records exclusions
for evaluation and durable-work compatibility. There is no provider-specific
branch, automatic retry, prompt repair or new dependency.

This operational change keeps the model, prompts, reasoning and generation
limits unchanged. It does not establish improved Hebrew or guarantee future
provider availability. A separate six-call workflow smoke test uses the original
reading, bilingual, numeric and question-replacement cases. Do not pool it with
the matched prompt experiment or discard the earlier failures.

The smoke test completed six calls: reading, numeric generation and numeric
question replacement passed all automatic checks; bilingual generation timed
out after 180 seconds with no response. Thus **3/4 workflows passed**, not 4/4.
All returned outputs passed strict validation. Numeric keys were checked
directly, and replacement changed only its selected question. DekaLLM exclusion
does not eliminate the observed availability problem. The failed call began at
17:49:30 UTC; its serving provider is unknown in the local artifact.

Retain the bounded routing configuration based on the reported DekaLLM issue and
verified outgoing request. Do not claim this small smoke test proves a reliability
improvement. Keep the current prompt/model and stop paid tuning here, preserving
the remaining budget instead of rerolling failures.

## Evidence and limitations

Local evidence is under `artifacts/evaluations/quality-2026-10-01/`: protocols,
frozen fixtures/prompts/settings, pricing, raw requests/responses/failures,
cost reservations, blinded review packs, separate mapping keys and assistant
ratings. Human-review fields remain null. Assistant ratings are provisional;
they do not replace a human Hebrew/educational review.

The private probe initially retained stale content-check flags after clearing
the original questions. Its overall readiness remained false. Recomputing the
existing checks fixed the detail flags, with an offline negative check. The
first candidate call was already in flight; its timed-out raw report is
preserved and annotated, not rewritten. Successful final checks and production
validators are unaffected.

The baseline question calls precede the candidate calls. Latency or timeout
differences cannot be attributed solely to prompt wording; provider conditions
may differ. The adoption gate nevertheless counts every observed failure.

## Budget

This follow-up made **43 calls** and recorded **$0.064295840** in known cost.
Three unmeasured calls retain **$0.243302400** in conservative reservations.
Including the previous tuning phases: **171 attempts**, **$0.376648456** known
cost and **$0.324403200** reserved for four unmeasured calls, totaling
**$0.701051656 charged/reserved against the shared $2 cap**. No failed or
unmeasured call is counted as free. The earlier Task 2 comparison remains under
its separate historical authorization.

## Code and verification

Changes remain inside the OpenRouter adapter registration, its shared nonsecret
profile, configuration, developer-dashboard label, focused tests and docs.
No prompt candidate, engine revision change, compatibility shim, extra AI layer
or production retry remains. Independent code review found no actionable issues.

The provider-exclusion tests failed before implementation, then all 50 isolated
OpenRouter configuration tests passed. `scripts/verify.sh` passed: 534 backend
tests, 98 Angular tests, 23 evaluation-dashboard tests, formatting, Markdown
checks, TypeScript checks and production builds. Earlier verification attempts
stopped on documentation formatting/line length; those were corrected without
changing code or tests. Logs remain in the experiment artifacts.

No parent/child workflow changed, so the cutover browser suite was not rerun.
Code verification passes; the live/model-quality results and unresolved timeout
remain explicitly separate. No Git mutations were performed.
