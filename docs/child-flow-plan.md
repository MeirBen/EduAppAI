# Child flow acceptance record

**Completed 6 October 2026:** Tasks 1–7 and approved follow-ups are implemented.
This is a closed milestone record, not instructions to restart implementation.
The next product step is the family sanity test below; other future work lives
in the [specification](product-specification.md#next-steps).

Use the [child-flow specification](product-specification.md#child-flow) for
behavior and limits, [architecture](architecture.md#child-device-access) for
ownership/transactions, and [UI guide](ui-guide.md#parent-learning-management)
for presentation. This record maps the completed work to regression coverage.

## Ownership and regression suites

- Backend: `Features/Children`, `Features/Assignments` and
  `Infrastructure/Auth/ChildAccess.cs`. Generation stays in the generic engine;
  child access, assignment, submission and grading make no AI calls.
- Frontend: `features/children`, `features/assignments`, `features/child` and
  their typed clients under `core/api` / `core/auth`. Child pages use child-only
  models and never load parent limits or answer-bearing views.
- Backend test names below are under
  [tests/FamilyLearning.Api.Tests](../tests/FamilyLearning.Api.Tests).
  Browser suites are under [frontend/e2e](../frontend/e2e); Angular component
  tests sit beside their owning pages.

## Task 1: Child profiles and independent device access

Implemented profiles, one-time activation, persistent device grants, named
parent/child cookie policies, identity-bound CSRF and revocation.

`Integration/ChildAccessTests.cs`, `ChildProfileTests.cs`, `ApiBoundaryTests.cs`
and `ParentAccountTests.cs` cover family/scheme isolation, stale revisions,
activation replacement and concurrent redemption, disable/re-enable, rate limits,
expiry and opposite-mode session conflicts. `ChildHarness` supplies disposable
storage, an isolated provider and a controllable clock.

## Task 2: Reviewed assignments, safe projections and retention

Implemented one assignment per child/snapshot pair, bounded lists, withdrawal,
explicit learner projections and archive-on-removal for assigned snapshots.

`Integration/AssignmentTests.cs`, `AssignmentRetentionTests.cs` and
`LibraryDeletionTests.cs` cover cross-family/sibling access, duplicate creation,
exact nested learner response allowlists, assign/delete races, retained history,
reset rollback and records beyond the first 100. Archived snapshots remain in
generation history; their content JSON is never rewritten.

## Task 3: Resumable answers and atomic automatic scoring

Implemented explicit start/save/submit, one resumable session per assignment,
revision conflicts, atomic answer freezing and persisted scoring policy.

`Integration/ChildSessionTests.cs`, `SessionRaceTests.cs` and
`Assignments/SessionScoringTests.cs` cover malformed and unfinished answers,
identical/reordered submission replays, changed-answer conflicts, zero-point
work and restart persistence. Numeric equality tests include signed zero,
trailing fractional zeros and distinct values that decimal parsing rounds equal.
Nonblank short text is preserved exactly and always awaits parent review.

## Task 4: Parent grading and immutable result reports

Implemented parent-only reports and atomic finalization of all pending grades.

`Integration/ParentReviewTests.cs` and the scoring/race suites cover ownership,
exact pending-ID sets, bounded integer awards, concurrent/reordered replays,
immutable automatic awards and completed results. Pending work has no final
score; zero possible points never produces a percentage. Child responses remain
allowlisted after parent review.

## Task 5: Parent child-management, assignment and review screens

Implemented `/children`, `/assignments`, `/assignments/:assignmentId` and
assignment from frozen previews. Forms use shared theme primitives and native
controls; activation codes are transient, and pending grades start empty.

Owning Angular specs and `parent-assignments.spec.ts` cover profile/device
management, assignment/withdrawal, pagination, review conflicts, lost responses,
retained local edits, archive/reset and keyboard use at 360px/200% text.

## Task 6: Child activation, inbox and resumable activity player

Implemented `/child/activate`, `/child` and
`/child/assignments/:assignmentId` with a separate shell and child-only clients.

Owning Angular specs and `child-workflow.spec.ts` cover independent parent/child
browser contexts, activation recovery, all answer types, explicit save/reload,
missing-answer confirmation, submission, parent grading and final receipts.
They also cover 401/404/410 and conflict/outage feedback, dirty-navigation guards,
transport cancellation, preserved local input, Hebrew/RTL and material line
breaks. No browser-persistent answer cache or automatic mutation retry was added.

## Task 7: Full-flow acceptance and documentation cutover

Acceptance used disposable databases, isolated providers and the published app:

- Two families and siblings complete activation → assignment → save/reload →
  lost submission response → saved-state recovery → grading → unchanged results.
  Exact child response allowlists and zero child-flow AI calls are asserted.
- `ProductionHostTests.cs` covers cookie flags, HTTPS/CSRF protection and restart
  persistence; `ChildAccessTests.cs` covers scheme and CSRF identity isolation.
  Reopened browsers retain only the original grant term.
- `SessionRaceTests.cs` covers access loss after authentication, expiry while
  waiting for a write, duplicate submit/review, withdrawal and reset. Writes must
  recheck current access and sample UTC after acquiring their transaction.
- Retention suites cover archive/delete/assignment races, complete family reset
  beyond pagination and unaffected other-family records.
- `MigrationTests.cs` covers fresh install, repeated migration and upgrades from
  pre-child and intermediate grade/age schemas while preserving snapshot JSON.
  Management commands work without AI configuration.

The 6 October acceptance ran `scripts/verify.sh` (606 backend, 268 Angular and
23 dashboard tests, plus formatting/type/build checks), `scripts/publish.sh`,
all 29 browser tests and the EF pending-model check successfully. Follow the
[current verification commands](../README.md#verify) after changes; these counts
are historical evidence, not a substitute for rerunning checks.

A later AI-work verification run intermittently failed the unchanged
submit/expire race case; targeted and full reruns passed. Its cause remains
unresolved. The failure and reruns are retained locally in
`artifacts/evaluations/math-2026-10-06/verification.json`.

## Approved follow-ups

Also implemented and verified: optional grade/age without an age-confirmation
checkbox, elapsed opening-to-submission time including breaks, quiet timestamp
disclosures, deletion of profiles without assignments and removal of inactive
device records. History remains intact. See the owning specification sections;
profile-based AI audience prefilling remains future work.

## Family sanity test

Use separate parent and child browsers/devices to assign reviewed content,
activate, save/reopen, submit and grade it. Check that Hebrew instructions,
answer controls and results make sense to the family. Independently review
representative generated content for accuracy, age suitability and answer
quality; software acceptance does not certify these.
