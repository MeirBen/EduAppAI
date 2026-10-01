# Post-cutover AI tuning

Status: bounded trials completed on 1 October 2026. Retain the material-stage
instructions and two authoring-schema descriptions. Keep the existing model and
reasoning-enabled/low profile. These changes improve sampled contract adherence;
they do not establish educational equivalence or superiority to one-shot generation.

## Retained changes

- Material generation explicitly separates source bodies from questions, choices,
  answers and learner instructions, and explains the existing body-word counting
  rules. Strict validation remains authoritative; the prompt's self-check is not
  a guarantee.
- `defaultFormat` describes the existing null requirement when answer format is
  not selectable. `source` distinguishes AI-generated material from text supplied
  by the parent, including text supplied separately for each activity.
- The shared engine revision records semantic changes. Schema version, domain
  contracts, validators, provider adapter, application flow and dependencies stay
  unchanged. No output repair, coercion, fallback or production retry was added.

The schema annotations follow OpenRouter's guidance to give fields clear
[descriptions](https://openrouter.ai/docs/guides/features/structured-outputs).
The reasoning experiment used its supported native
[reasoning control](https://openrouter.ai/docs/guides/best-practices/reasoning-tokens).
No setting was adopted solely because provider documentation recommends it.

## Evidence and decisions

The original post-cutover reading baseline passed 2/5 strict 100–150-word trials.
Retained material candidate A passed 5/5, then 4/5 confirmation attempts; the
remaining attempt failed at the provider with no captured response or cost.
All nine returned bodies passed. A shorter candidate also passed length checks
but introduced story inconsistencies, so it was rejected. All evidence remains
under `artifacts/evaluations/post-cutover-tuning-2026-10-01/`.

The continuation preserved the original 27-case fixture and checks. On the same
three authored cases, twice each, the prior material-A configuration accepted
0/6 plans. Describing `defaultFormat` increased that to 3/6; additionally describing
`source` increased it to 6/6. Five final activities passed all automatic checks.
The sixth produced 161 words against a required 180–220 range and was rejected.
These small, stochastic trials support clearer contract wording, not a general
success-rate estimate or improved language quality.

Disabling reasoning passed 3/3 reading structure checks and reduced median
whole-task cost by about 70.5% and summed model-call latency by 61.3%. It introduced
a speaker contradiction and corrupted Hebrew wording. Reject it under the
preregistered quality gate; do not spend on its conditional holdouts or retain a
faster configuration with worse observed content.

## Fresh one-shot comparison

Three repetitions of each fixed case used identical resolved learning inputs,
model profile, fixture hash and current strict validators. Variant order alternated
between repetitions. The private reference probe replayed the original archived
one-shot instructions/schema through the existing provider and current assembly
checks. It restores no production capability or historical report reader.

Both variants passed all nine structural trials. All six supplied bilingual
sources were preserved exactly; all 18 numeric answers were correct.

| Case      |  Split $ | One-shot $ | Split s | One-shot s |
| --------- | -------: | ---------: | ------: | ---------: |
| Reading   | 0.003533 |   0.005388 |  14.646 |     14.910 |
| Bilingual | 0.001157 |   0.001842 |   9.476 |     13.794 |
| Numeric   | 0.000766 |   0.000805 |   3.079 |      2.826 |

Values are medians; times sum every model call needed for an activity.
The evaluator's five-second
inter-call pause is excluded from that column: median reading wall time was
19.666 s for split versus 14.948 s for one-shot. Application queue and persistence
latency were not measured. All costs in this matched batch were reported.
Three repetitions do not establish a stable performance advantage.

Two independent assistant reviews found quality limitations in both variants.
One split story contradicts whether a character noticed a fallen hat. Both
variants sometimes replace requested inference/relationship questions with
retrieval questions; one reference question introduces unsupported immediacy.
Other authored samples contain niqqud and phrasing errors. Human scores remain
null. A shuffled, unscored review pack and separate key are preserved locally;
assistant review is not a substitute for blinded human educational assessment.

## Wider coverage and stopping point

The unchanged two-passage case passed its combined 120–160-word range with 149
words and a grounded question linking both passages. Numeric question replacement
passed with correct answers. The general grade-three authoring case failed its
independent control-count check because it duplicated topic, difficulty and
question count as custom controls. Its generated reading length passed; the
activity remains an adherence failure in the evidence.

A separate exact-length fixture reused the fixed-reading plan with exactly 120
body words and matching strict expectations. All three repetitions passed without
repairs. This is supplemental coverage, not a changed comparison suite. Content
review still found repetitive moral statements, awkward phrasing and occasional
retrieval questions in place of inference. Live material-replacement quality was
not sampled; its mechanics remain covered by isolated tests.

Stop here: retain the measured instruction/contract improvements and reject the
faster but worse-quality setting. Do not chase individual holdout failures by
adding special-case instructions, rerolling outputs or weakening checks. The
remaining limitations require parent review; neither architecture nor this small
sample justifies calling the AI fully reliable or generally better than one-shot.

## Budget

This continuation made 76 attempts and recorded $0.179313376 in provider-reported
cost. Including the preceding post-cutover experiment, the tuning phases made
128 attempts with $0.312352616 known cost. One earlier unmeasured call retains a
conservative $0.081100800 reserve: **$0.393453416 charged/reserved against the
shared $2 cap**. No unknown call is counted as free, and no remaining budget was
spent merely to obtain a better-looking sample. The historical Task 2 comparison
had its own earlier authorization and is excluded from this tuning ledger.

## Reproducibility and verification

Continuation evidence lives under
`artifacts/evaluations/qualification-2026-10-01/`: preregistered protocols, every
raw request/output and failed run, provider pricing, budget reservations, native
harness comparisons, derived measurements and review findings. Supplemental
coverage is identified separately and never pooled into the original comparison.
The reference probe and experiment wrappers are private artifacts outside the
production/evaluation projects and solution.

`scripts/verify.sh` passed: 527 backend tests, 98 Angular tests, 23 evaluation
dashboard tests, formatting, Markdown checks, TypeScript checks and production
builds. Focused engine verification also passed 327 tests. This tuning changed no
UI/workflow; the cutover's isolated browser verification remains separate.
No Git mutations were performed.
