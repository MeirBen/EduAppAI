# Child Flow Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement one
> reviewed task at a time. Use subagent-driven development only if the user
> selects it. Checkboxes track implementation, not completion of this document.

**Status:** Tasks 1–5 implemented and verified on 6 October 2026. Tasks 6–7
have not started; the child activation, inbox and player screens remain incomplete.

**Verification:** `./scripts/verify.sh` passed with 592 backend tests, 211 Angular
tests and 23 dashboard tests, plus formatting, Markdown, type checks and builds.
`./scripts/publish.sh` and the isolated browser suite passed (28 tests), including
parent profile/device management, snapshot assignment/replay, withdrawal,
archiving, submitted-answer review, recovery after a committed grading response
is lost, revocation, disable and family reset. Component tests cover paging,
validation, conflicts, cancellation, exact text and delayed-refresh keyboard
focus. Browser checks cover 360px/200% text and light/dark themes. Independent
review findings were fixed and confirmed; no unresolved findings remain.
Verification used disposable databases and isolated providers, with no paid AI
calls or Git mutations. The parent-management browser workflow has a separate
disposable host so production login limits remain unchanged.

Optional school grade/age and elapsed activity duration are documented follow-ups
in the specification, outside these implementation tasks.

**Goal:** A parent assigns reviewed work to an activated child device, the child
saves and submits answers, and the parent sees stable results and grades short
text where required.

**Architecture:** Extend the existing .NET application, SQLite database and
Angular client. `Children` owns profiles and device access; `Assignments` owns
assignment lifecycle, work sessions, scoring and reports. Keep the generation
engine and immutable snapshot content intact. Use named cookie schemes,
DbContext directly, short transactions and explicit child-only projections.

**Tech stack:** Installed .NET 8 SDK, ASP.NET Core Identity/cookies/antiforgery,
EF Core SQLite, Angular 22 standalone components and Signal Forms, xUnit and
Playwright. No new runtime package is planned.

**Spec:** [Product specification — child flow](product-specification.md#child-flow--next-milestone).
Read the spec, [architecture](architecture.md), [UI guide](ui-guide.md) and
[comment rules](commenting-guide.md) before implementation. Product behavior
belongs in the spec; this plan maps it to changes and verification.

## Global constraints

- No Git mutations, live paid AI calls, educational seed data or fake working
  screens. All child-flow verification uses isolated providers and disposable
  databases. No generation or judging calls belong in this flow.
- One production backend project; feature-owned endpoints and state. No
  repository, mediator, generic workflow framework or parallel legacy path.
- Preserve parent URLs and authoring behavior. Keep snapshot content frozen and
  generation revisions unchanged; grading has its own policy version.
- Use bounded strict JSON, ProblemDetails, cancellation, UTC and server ownership
  checks. Never log activation codes, raw answers, cookies or answer keys.
- Preserve Hebrew/RTL, native controls, keyboard access, 360px width and 200%
  text. Keep state in its owning page; use existing request lifetime helpers.
- Spec limits: child/device names 1–100 characters; activation code 16 random
  bytes, ten-minute expiry; device grant 30 days, no sliding renewal; redemption
  ten/minute/IP and 120/minute/process; issuance ten/minute/parent; no queues.
- New lists default to 25 rows and accept at most 100. Answers are at most 200
  characters and at most the frozen question count, never over 20. Grading
  awards are integers from zero to that question's frozen possible points.
- Do not change existing numeric answer grammar or add AI grading. All nonblank
  `text-input` answers need parent review, including zero-point questions.
- Add migrations; never rewrite `InitialCreate` or delete a development database
  to make tests pass. Stop the development watcher before model/migration work.
  Keep SDKs, databases, keys and generated artifacts outside source control.
- At every task: run the focused tests, `./scripts/verify.sh`, and review the
  diff for ownership leaks, obsolete callers, comments and unnecessary layers.
  UI tasks also run the isolated published browser suite. Stop for user review
  before the next task; do not commit or silently start the next one.

## Review focus

These cases must have direct tests in the tasks that own them:

1. A request authenticated before revocation must fail if revocation commits
   before its write transaction checks access (Tasks 1, 3 and 7).
2. A timeout after a committed submit/review must resolve to the saved outcome,
   without a second attempt or overwritten answers (Tasks 3, 4 and 6).
3. Correctly worded short text must reach the parent unmodified and ungraded;
   pending and zero-point work must not display a misleading final percentage
   (Tasks 3–6). Numeric scoring must not award points because parsing rounded
   distinct values to the same decimal (Task 3).
4. Removing an assigned snapshot or racing withdrawal/reset against submission
   must not destroy retained history or leave partial state (Tasks 2, 3 and 7).
5. A child response may leak keys through a nested reused parent DTO or an
   error, even when its top-level fields look safe (Tasks 2, 3 and 7).

## Owning files and interfaces

Paths below are relative to the repository root. New files are proposed; do not
create empty scaffolding before its task. Add companion test/HTML files only
where the component or contract needs them.

Backend paths below are under `backend/FamilyLearning.Api/`:

- **Profiles and device access:** `Features/Children/Child.cs`,
  `ChildActivation.cs`, `ChildDeviceGrant.cs`, `ChildContracts.cs`,
  `ChildEndpoints.cs` and `ChildAuthEndpoints.cs`.
- **Authentication:** `Infrastructure/Auth/ChildAuthentication.cs` and
  `ChildAccess.cs`; update `AuthConfiguration.cs` and
  `Features/Auth/AuthEndpoints.cs`.
- **Assignment lifecycle:** `Features/Assignments/Assignment.cs`,
  `AssignmentContracts.cs`, `AssignmentEndpoints.cs` and
  `ChildAssignmentEndpoints.cs`.
- **Work and grading:** `Features/Assignments/TaskSession.cs`,
  `SessionContracts.cs`, `SessionValidation.cs`, `SessionScoring.cs`,
  `ChildSessionEndpoints.cs` and `ParentReviewEndpoints.cs`.
- **Composition and storage:** Existing `Infrastructure/Web/ApiConfiguration.cs`,
  `CsrfFilter.cs`, `Infrastructure/Persistence/LearningDbContext.cs`,
  `Migrations/` and `Program.cs`; add the pagination value contract at
  `Infrastructure/Web/PageResponse.cs`.

Frontend paths below are under `frontend/src/app/`:

- **Parent management:** `features/children/` and `features/assignments/`;
  reuse the snapshot preview and library entry points.
- **Child UI and access:** `features/child/`, `core/auth/child-auth.ts`,
  `child-guard.ts`, `core/api/child-api.ts` and `child-models.ts`.
- **Shared infrastructure:** Reuse the router, request cancellation,
  page-reuse strategy, theme, loading and focus helpers. Never reuse a parent
  answer-bearing model for the child.

Define the following interfaces in their owning task. Later tasks consume these
names rather than inventing a second contract:

- `ChildIdentity(Guid ChildId, Guid FamilyId, Guid DeviceGrantId)` and
  `ChildAccess.FindAsync(ClaimsPrincipal, LearningDbContext, DateTime utcNow,
CancellationToken) -> Task<ChildIdentity?>`: validate the stored profile and
  grant through fresh `AsNoTracking` queries. A null result means no current
  access. Reuse inside write transactions, sampling UTC after acquiring the
  transaction; never reuse an authentication-time entity or access result.
- `ChildSummary`: ID, name, enabled state, revision and creation time.
  `ChildSessionIdentity`: child's ID/name, grant expiry and `AnswerLength`;
  expose no parent identity or full parent limits document.
- `PageResponse<T>`: `Items`, `Page`, `PageSize`, `HasMore`. Use deterministic
  descending creation-time/ID order with database `Skip`/`Take(pageSize + 1)`.
  Reject nonpositive pages, sizes outside 1–100 and offset overflow. Family
  lists are small; no count-all query or generic query framework is needed.
  Filter and project summaries in SQL before pagination; do not load content
  JSON or issue per-row queries. Add indexes for the owned list predicates and
  ordering in their schema tasks.
- `CreateAssignmentRequest(Guid ChildId, Guid SnapshotId)`;
  `AssignmentSummary`: ID, child ID/name, snapshot ID/title, status, revision,
  creation time and whether work has started. Child summaries omit other
  children's and parent-only information.
- `LearnerDocument`, `LearnerMaterial`, `LearnerQuestion`, `LearnerInteraction`:
  explicitly declared allowlisted projections from the spec.
  `LearnerMaterial` carries ID, title and body; no origin, acceptance or idea.
  `LearnerQuestion` carries ID, prompt, interaction and possible points; no
  answer, origin or acceptance property.
- `SessionAnswer(string QuestionId, string Value)`;
  `SaveAnswersRequest(long ExpectedRevision, SessionAnswer[] Answers)` and
  `SubmitAnswersRequest(long ExpectedRevision, SessionAnswer[] Answers)`.
  Require JSON members and validate nulls as well as lengths.
- `QuestionEvaluation`: question ID, possible points, nullable awarded points
  and grading method (`automatic` or `parent`). `SessionEvaluation` holds these
  rows, automatic subtotal, possible total, pending count and nullable final
  total; no percentage until complete and none for a zero denominator.
- `SessionValidation.Validate(TaskDocument, SessionAnswer[], bool submitting)`
  returns field errors. `SessionScoring.Evaluate(TaskDocument, SessionAnswer[])`
  returns the persisted `SessionEvaluation`; call it only after validation.
  `SessionScoring.PolicyVersion` starts at 1, independent of `EngineVersions`.
- `ParentGrade(string QuestionId, int Points)`;
  `FinalizeReviewRequest(long ExpectedRevision, ParentGrade[] Grades)`.
  `SessionScoring.CompleteReview(SessionEvaluation, ParentGrade[])` completes
  only the pending rows; it never takes a live plan or recalculates automatic
  awards. Validate the exact pending ID set before calling it.

Use separate revision counters deliberately: profile edits advance the child
revision; withdrawal, submission and final review advance the assignment
revision; answer saves, submission and final review advance the session
revision. Start/save do not change assignment status or revision. Initialize
revisions at 1, configure each as an EF concurrency token and return unchanged
revisions/timestamps for idempotent replays. Check ownership and bounded request
shape before recognizing a replay, and recognize it before checking an old
expected revision. A pre-submission save is revision-checked, not replayable.

## Task 1: Child profiles and independent device access

**Deliverable:** Real parent management and child activation/session APIs, with
no dependence on AI and no weakening of parent authorization. UI comes later.

**Files:** Create the six `Features/Children` files, two authentication files
and `Infrastructure/Web/PageResponse.cs` listed above. Modify
`AuthConfiguration.cs`, `AuthEndpoints.cs`, `ApiConfiguration.cs`,
`LearningDbContext.cs`, `Program.cs` and
`Features/Templates/PlanTemplateEndpoints.cs`. Update the current library reset
confirmation and `core/api/learning-api.ts` comments in the frontend. Add
`Integration/ChildAccessTests.cs` and `Integration/ChildHarness.cs` under
`tests/FamilyLearning.Api.Tests`; update `ApiBoundaryTests.cs`,
`ParentAccountTests.cs`, `LibraryDeletionTests.cs` and `MigrationTests.cs`.

**Interfaces:** Produces `ChildIdentity`, `ChildAccess.FindAsync`,
`ChildSummary`, `ChildSessionIdentity` and `PageResponse<T>`.

- [x] Add failing host tests proving child cookies cannot read parent content,
      parent cookies cannot read child content, and mixed authenticated cookies
      cannot activate or sign in. Assert no parent data appears in failure bodies.
- [x] Add tests for two families, invalid names, stale profile revisions,
      disable/re-enable, missing/wrong-identity CSRF, expiry using an injected
      `TimeProvider`, two concurrent redemptions with exactly one grant, and the
      specified rate limits. A test harness may derive a controllable TimeProvider;
      do not use sleeps or introduce a production clock wrapper.
      Include issue-versus-disable, redeem-versus-disable and code replacement:
      a committed disable leaves no usable code/grant, and only the latest issued
      code can be redeemed. Verify persistent cookie expiry and no sliding renewal.
- [x] Run `dotnet test tests/FamilyLearning.Api.Tests --filter ChildAccessTests`.
      Confirm the new behavior fails before implementation; compile errors for new
      test contracts must be resolved before accepting a behavioral red test.
- [x] Add child/profile revision and enabled state, one activation slot per
      child (hash, device label, expiry/consumption), and device grants with fixed
      expiry/revocation. Family/child ownership is relational; enforce unique code
      hashes and foreign keys. Reuse `EngineValidation.NameLength` for child/device
      names so the existing parent `ContentLimits.NameLength` remains authoritative.
      Apply the existing UTC timestamp conversion to all new timestamp properties.
      Generate migration `AddChildAccess` with the normal EF tooling; inspect it
      and the updated model snapshot.
- [x] Register a named `Child` cookie scheme and explicit `Parent`/`Child`
      policies. Keep the parent's Identity scheme, cookie behavior and existing
      routes. Use native cookie validation plus `ChildAccess.FindAsync`;
      set `IsPersistent = true`, `ExpiresUtc` to the stored grant expiry and
      `SlidingExpiration = false`. Name the child cookie
      `FamilyLearning.Child` with path `/` so opposite-mode checks can see it;
      policy selection, not cookie path, isolates access. Register
      `TimeProvider.System` once.
- [x] Refactor only the common API composition needed to map sibling parent
      and `/api/child` groups. Both inherit strict JSON, ProblemDetails, no-store
      and `CsrfFilter`; the child group must not inherit `Parent` authorization.
      Preserve the unknown-API 404 and shared exception handling without copying
      them into each feature. Update the exact anonymous endpoint allowlist.
- [x] Implement parent `GET/POST /api/children`,
      `PUT /api/children/{id}` (`name`, `enabled`, `expectedRevision`),
      `POST /api/children/{id}/activation` (`deviceLabel`),
      `GET /api/children/{id}/devices` and
      `DELETE /api/children/{id}/devices/{grantId}`. Return activation code/expiry
      once. Use native cryptography and rate limiting with the spec's values.
- [x] Implement `/api/child/auth/csrf`, `POST /activate`, `GET /me` and
      `POST /logout`. Only child CSRF issuance and activation join the existing
      anonymous allowlist. Select the intended scheme before CSRF issuance or
      validation; keep the current Angular XSRF cookie/header mechanism. Refresh
      the token after activation. Do not rely on the default parent principal for
      child CSRF, or disable CSRF to resolve an identity mismatch. Token issuance
      must reject an active opposite-mode session without changing its token.
      Give these conflicts a distinct ProblemDetails type and add a narrow mapping
      in `frontend/src/app/core/api/api-error.ts` with tests; the existing generic
      409 template-conflict message is not suitable for a device-session conflict.
- [x] Perform activation, disable and revocation atomically. Repeated revoke
      is harmless for an owned grant; foreign IDs remain 404. Do not expose a
      credential in list responses or automatically retry a consumed activation.
      Use the existing short, non-deferred SQLite write transaction pattern;
      acquire it before reading authorization/state used by the write. Query
      current database values even if cookie validation used the same DbContext.
      Later assignment/session mutations follow this same ordering; do not add a
      process lock or a generic transaction/retry framework.
- [x] Extend the existing family reset transaction to remove grants, activation
      slots and child profiles. Update its confirmation and contract comments now,
      not after later tasks. Add reset-past-100 and cross-family tests. Update
      migration tests to verify the migration set instead of asserting one exists.
- [x] Verify `ChildAccessTests`, `ApiBoundaryTests`, `ParentAccountTests`,
      `LibraryDeletionTests` and `MigrationTests`, then `./scripts/verify.sh`.
      Expected: all pass, current model matches migrations, restart preserves
      accounts/content, and the AI test provider records zero child-flow calls.
      Run `./scripts/publish.sh` and `npm --prefix frontend run e2e` to verify the
      changed parent auth/reset paths against the published application.
- [x] Review access checks, secret handling and deletion ordering. Task 2 was
      explicitly authorized together with Task 1.

## Task 2: Reviewed assignments, safe projections and retention

**Deliverable:** A parent can assign a snapshot and inspect assignments; a
child can read only their own learner-facing work. Assigned snapshots survive
library removal. No session or grading endpoint is faked.

**Files:** Create the four `Features/Assignments` assignment files listed above.
Modify `LearningDbContext.cs`, `ApiConfiguration.cs`,
`Features/Instances/TaskSnapshot.cs`, `SnapshotContracts.cs`,
`SnapshotEndpoints.cs` and `Features/Templates/PlanTemplateEndpoints.cs`.
Update `frontend/src/app/core/api/models.ts`, `learning-api.ts`, the library
component/template and snapshot preview where archive metadata affects existing
behavior. Add `Integration/AssignmentTests.cs`, `AssignmentRetentionTests.cs`;
extend `LibraryDeletionTests.cs` and corresponding library/browser tests.

**Interfaces:** Consumes Task 1 access and pagination. Produces assignment
contracts, `LearnerDocument` and read endpoints used by Tasks 3, 5 and 6.

- [x] Add failing tests for two siblings and two families, unreviewed/unknown
      snapshots, disabled children, archived snapshots and concurrent duplicate
      assignment creation. A child/snapshot pair produces one row; replay returns
      the existing row without reopening withdrawn or completed work.
- [x] Add exact property allowlist assertions for every nested learner object,
      not just a search for the word `answer`. Include hostile-looking source text
      to prove content is preserved as data. Assert no generation calls.
- [x] Run `dotnet test tests/FamilyLearning.Api.Tests --filter AssignmentTests`
      and confirm expected failures.
- [x] Add `Assignment` with family/child/snapshot IDs, status, revision and UTC
      timestamps. Add a unique child/snapshot index and restrictive child/snapshot
      foreign keys. Add `ArchivedAtUtc` metadata to snapshots without changing
      their content JSON. Generate and inspect migration `AddAssignments`.
- [x] Implement `POST /api/assignments`, paged `GET /api/assignments` with
      optional child/status filters, `GET /api/assignments/{id}` and
      `POST /api/assignments/{id}/withdraw` with `expectedRevision`. Read owned
      existing pairs before new-create eligibility checks; returning existing
      history does not create work for a disabled child or archived snapshot.
- [x] Implement paged `GET /api/child/assignments` by available/submitted state
      and `GET /api/child/assignments/{id}`. Select the child from `ChildIdentity`,
      never request data. Withdrawn work returns 410 only after ownership is known;
      foreign/missing IDs return 404. Submitted rows become usable in Task 3.
- [x] Project learner content explicitly. Do not cast, inherit, spread or
      serialize a parent `TaskDocument`/`SnapshotPreview`, including on errors.
      Parent assignment details may reference the existing owned snapshot preview.
- [x] Make snapshot deletion archive referenced snapshots and hard-delete only
      unassigned ones. Hide archived snapshots from the ordinary library, keep
      owned parent previews and assignment reads working, and expose metadata for
      accurate archive/delete copy. `GenerationHistoryReader` keeps reading
      archived snapshots: the child did that work, so it still steers variety.
      Update existing UI/API comments in this task.
      Keep the existing DELETE success contract; confirmation must explain both
      outcomes if a concurrent assignment changes deletion into archiving, and
      success copy must not claim permanent deletion from stale list metadata.
- [x] Extend family reset to remove assignments before their referenced
      snapshots/children. Test delete-versus-assign,
      withdraw-versus-duplicate-create,
      reset rollback, rows beyond 100 and unaffected families, using separate
      DbContexts and deterministic synchronization rather than sleeps.
- [x] Verify assignment/retention/library/migration tests,
      `./scripts/verify.sh`, then `./scripts/publish.sh` and
      `npm --prefix frontend run e2e` for the changed library behavior.
- [x] Review snapshot immutability, paged ownership queries and archive copy;
      stop for user review.

## Task 3: Resumable answers and atomic automatic scoring

**Deliverable:** Child APIs start, save and submit one session per assignment.
Choice/number results complete immediately; answered short text becomes pending
parent review with frozen answers and automatic awards.

**Files:** Create `TaskSession.cs`, `SessionContracts.cs`, `SessionValidation.cs`,
`SessionScoring.cs` and `ChildSessionEndpoints.cs` in `Features/Assignments`.
Modify `Assignment.cs`, both assignment endpoint files, `ApiConfiguration.cs`,
`LearningDbContext.cs` and family reset. Add `Integration/ChildSessionTests.cs`,
`SessionRaceTests.cs` and `Assignments/SessionScoringTests.cs` under the test
project; extend restart/migration tests.

**Interfaces:** Consumes learner document and access checks. Produces answer
requests, persisted evaluation, a child-safe session view (revision, status,
answers, timestamps and final total only when complete), and the scoring APIs
listed above. `Assignment.Status` is the lifecycle authority; do not add a
second independently mutable session status.

- [x] Add failing scoring assertions: choice match/full points; valid wrong
      choice/zero; `+02.00` equals key `2`; negative decimals; zero; reject commas,
      exponent and decimal overflow at submission; preserve Hebrew/niqqud text with
      unset points; blank text/zero; answered zero-point text/still pending review.
      Include signed zero, long equal fractions with extra trailing zeros, and
      unequal fractions that `decimal.TryParse` rounds to the same value, including
      a nonzero fraction rounded to zero. Test both answer and key positions.
- [x] Add endpoint tests for empty/missing/null/duplicate/unknown answers,
      oversized strings/collections, save of unfinished numeric text, two starts
      producing one session, stale writes and independent sibling sessions.
- [x] Run the new `SessionScoringTests` and `ChildSessionTests` using
      `dotnet test tests/FamilyLearning.Api.Tests --filter` with the class name;
      confirm the required behavior fails before implementing it.
- [x] Add `TaskSession` keyed by its assignment ID (also a foreign key), with
      concurrency revision, `AnswersJson`, nullable `EvaluationJson`, scoring
      policy version, UTC start/save/submit/review timestamps and nullable
      `ReviewedByParentId`. Answers remain editable only before submission.
      The bounded result stores nullable parent awards; already computed
      automatic awards cannot change. Generate and inspect
      migration `AddTaskSessions`. Reset deletes sessions before assignments.
- [x] Implement `SessionValidation.Validate` and the numeric/choice scoring
      paths. Reuse `QuestionRules.ValidNumericAnswer` for existing grammar/range
      validation. For equality, normalize the validated strings' leading sign,
      leading integer zeros, trailing fractional zeros and signed zero; compare
      ordinally without changing stored answers. Do not compare parsed decimals:
      [`decimal.TryParse` can round distinct accepted values][decimal-parsing].
      Keep this bounded comparison local to scoring; no numeric library or engine
      change is needed. Never trim or normalize a nonblank saved short-text answer.
      Method comments must state whether validated input is required.
- [x] Implement `POST /api/child/assignments/{id}/session` (idempotent start),
      `GET` and `PUT` at that session path, and
      `POST /api/child/assignments/{id}/session/submit`. Start reads existing terminal
      work without resetting it. Saving validates ownership, active assignment,
      access and expected revision before updating the complete answer buffer.
- [x] Submit the complete final buffer in one short transaction. Recheck child,
      grant, assignment state and revision in that transaction; validate, freeze
      answers, evaluate once and persist the policy version and final/pending state.
      No HTTP/AI work occurs while a database transaction is held.
- [x] Recognize an identical submitted answer set before rejecting its old
      revision. Ignore collection ordering and blank-versus-omitted differences,
      but compare other strings exactly. Return the stored outcome; a changed set
      conflicts. The client never supplies totals or grades.
      Validate required members, nulls, IDs, duplicates and raw bounds first;
      replay cannot bypass validation. Use `string.IsNullOrWhiteSpace` for the
      unanswered rule without trimming other strings. Test malformed replays and
      oversized whitespace, as well as empty, omitted and reordered answers.
- [x] Add synchronized races for submit/submit, submit/withdraw, save/revoke,
      submit/disable and submit/reset. Assert the winning state, no partial results,
      no recreated deleted rows and no answer changes after submission. Test the
      gap between cookie validation and the transaction's access recheck, with
      access entities already read by that request. Also expire a grant while
      waiting to start its write, and assert access uses the later clock value.
- [x] Verify focused tests and `./scripts/verify.sh`. Reopen the same disposable
      database in a fresh host and prove answers, automatic awards, pending state
      and revisions survive. Assert completion/session JSON allowlists and
      zero AI calls for every operation. Stop for user review.

## Task 4: Parent grading and immutable result reports

**Deliverable:** Parents can read submitted work and finalize all pending
short-text grades once, with stable history and clear pending/final totals.

**Files:** Create `Features/Assignments/ParentReviewEndpoints.cs`; extend
`SessionContracts.cs`, `SessionScoring.cs`, `TaskSession.cs`,
`AssignmentEndpoints.cs` and composition. Add
`Integration/ParentReviewTests.cs` and extend `SessionScoringTests.cs` and
`SessionRaceTests.cs`. The review metadata is introduced with the session in
Task 3; this task needs no additional schema change.

**Interfaces:** Consumes frozen evaluation and answers. Produces
`GET /api/assignments/{id}/result` (parent-only) and
`POST /api/assignments/{id}/review` accepting `FinalizeReviewRequest`.
The result includes frozen keys, submitted answers, each award/method, policy
version, pending count, automatic subtotal, nullable final total and reviewer
metadata; none of this parent DTO is reused by the child API.

- [x] Add failing tests: foreign family cannot read/grade, unsent work cannot be
      graded, all pending IDs are required once, automatic grades cannot be edited,
      fractional/negative/excess awards are invalid, and partial credit is accepted.
      Automatically completed work rejects a review with 409 and unchanged metadata.
- [x] Run `dotnet test tests/FamilyLearning.Api.Tests --filter ParentReviewTests`
      and confirm the new behaviors fail before implementation.
- [x] Implement `CompleteReview` from the frozen evaluation alone. Preserve
      automatic awards and possible points, fill only pending parent rows and
      calculate the final total; never rerun generation or scoring against a live
      template. Store reviewer parent ID and review UTC time.
- [x] Finalize grades and assignment completion atomically with the session
      revision. Replaying the same grade set returns the stored report;
      changing completed grades conflicts. Test two parents finalizing concurrently.
      On replay, compare against stored parent-graded rows, not the now-empty
      pending set; validate raw bounds, nulls and duplicate IDs before comparing.
      An identical reordered grade set with an old revision keeps the original
      reviewer, timestamps and revisions. Add direct tests for these cases.
- [x] Test zero total points produces no percentage, pending work has no final
      total, archived snapshots remain readable, disabled children retain results,
      and a fresh host returns the identical stored awards after a restart.
- [x] Verify focused tests and `./scripts/verify.sh`; inspect serialized child
      completion/history again to ensure the new parent fields cannot appear.
      Stop for user review.

## Task 5: Parent child-management, assignment and review screens

**Deliverable:** Parents can manage profiles/devices, assign from an existing
snapshot, withdraw available work, inspect submissions and finalize grades.

**Files:** Create `features/children/children-page.ts` and `.html`,
`features/assignments/assignment-list.ts` and `.html`, `assignment-result.ts`
and `.html` under `frontend/src/app`, with companion specs. Share the paged
profile selector in `features/children/child-selector.ts` and `.html`. Add
`core/api/parent-children-api.ts`, `assignment-api.ts`, `assignment-models.ts`
and `parent-task-error.ts`; keep status labels in
`features/assignments/assignment-presentation.ts`.
Modify existing `app.routes.ts`, `app.html`, snapshot preview, library copy and
relevant tests. Add `frontend/e2e/parent-assignments.spec.ts` and extend the
isolated provider/server fixtures only as required.

**Interfaces:** Consume Tasks 1, 2 and 4 endpoints with standalone strict pages
and existing `requestResult`/`httpResource` conventions. Routes are `/children`,
`/assignments` and `/assignments/:assignmentId`, under the parent guard. The
snapshot preview owns selection of the child for its assign action.

- [x] Add failing component tests for profile validation/conflicts, one-time
      activation display, revocation/disable confirmation and pagination. Verify
      activation codes are cleared when leaving the page and never persisted.
- [x] Add grading tests proving keys appear only in parent views, pending rows
      stay unset until entered, integer bounds apply, automatic awards are read-only
      and duplicate submits are blocked. Conflicts/lost responses keep local grades
      and offer reading the saved result; no automatic mutation retry.
- [x] Run `npm --prefix frontend test -- --watch=false` with `--include` paths
      for the new specs and confirm behavioral failures before implementation.
- [x] Implement the typed API clients and profile/device UI. Read name limits
      from existing server `ContentLimits` and point bounds from frozen questions;
      do not make the browser an independent source of limits. Preserve lifetime
      cancellation, safe failure copy, immutable buffers and busy/error states.
      Map profile/assignment/review conflicts in their owning UI; the current
      generic `apiError` 409 copy refers to template publication and is unsuitable.
- [x] Add assignment creation to the existing frozen preview, with a child
      selector and an existing-assignment link on replay. Add lists by child/status
      with pagination, withdrawal only while assigned, and archived-item messaging.
      Refresh affected resources after acknowledged writes without dropping edits.
- [x] Implement result/review forms against frozen data. Finalize all pending
      grades together after an explicit action. Show pending subtotal distinctly
      from final points and handle zero possible points. Warn before leaving with
      unsaved grades; do not permit editing completed grades or child answers.
- [x] Update family-reset confirmation to name all child learning/access data.
      Use native labels, focus restoration and the existing theme; avoid new generic
      table, form, state-management or modal frameworks.
- [x] Verify `./scripts/verify.sh`, then `./scripts/publish.sh` and
      `npm --prefix frontend run e2e`. Browser tests manage a real disposable child,
      assign, revoke, archive and reset; grade submissions prepared through the
      real child API, not production seed data. Test 360px/200% text and keyboard.
- [x] Review parent behavior and code boundaries; stop for user review.

## Task 6: Child activation, inbox and resumable activity player

**Deliverable:** A separate browser completes real assigned work through a UI
that never receives answer keys, recovers saved progress and receives accurate
submission status.

**Files:** Create `core/auth/child-auth.ts`, `child-guard.ts`,
`core/api/child-api.ts`, `child-models.ts`; `features/child/child.routes.ts`,
`child-shell.ts`/`.html`, `child-activation.ts`/`.html`, `child-inbox.ts`/`.html`,
`child-player.ts`/`.html` and companion specs. Extract existing parent shell
behavior to `features/auth/parent-shell.ts`/`.html`; keep `App` as the shared
root outlet. Update `app.routes.ts`, `app.ts`, `app.html` and shell tests.
Add `frontend/e2e/child-workflow.spec.ts`.

**Interfaces:** Routes `/child/activate`, `/child` and
`/child/assignments/:assignmentId` use only `ChildAuth`/`ChildApi`. Parent routes
keep their paths and parent shell; child pages never inject `LearningApi`,
parent `Auth` or the parent-only `Limits` loader. Child bootstrap obtains its
answer limit from `ChildSessionIdentity`.

- [ ] Add failing routing/component tests: an activated child refreshes into
      the child area without a parent guard/limits request; child navigation never
      shows parent controls; 401 asks for activation, a temporary server error
      offers retry, and neither error is persisted in a URL.
- [ ] Add player tests for all three interactions, incomplete numeric edits,
      explicit save and unsaved warning, validation focus, stale revision, lost
      save/submit responses, terminal sessions, missing-answer confirmation and
      cancellation on navigation. Pending review shows receipt without a final
      score; completed zero-point work shows no percentage.
      Verify 410 withdrawal locks the player, 401 requests activation, and
      409/503 preserve local input with session-specific recovery feedback, never
      the current `apiError` parent-login/template/AI copy. Content tests keep a
      two-paragraph material's blank line and a poem's line breaks, number the
      questions, show choices as native options and keep a calculation prompt in
      order.
- [ ] Run the new Angular specs with `npm --prefix frontend test -- --watch=false`
      and targeted `--include` paths; confirm behavioral failures.
- [ ] Implement shell separation with existing native routing. Preserve parent
      login URLs, theme controls, skip link, responsive header/footer and navigation
      loading indicators. Child home links stay in `/child`; do not expose a
      same-browser mode switch or duplicate the parent workspace into a child view.
- [ ] Implement activation and child session checks using the native XSRF flow.
      Explain persistent device access before activation, clear codes after
      use/destruction and refresh the identity-bound token after activation.
      Disconnect revokes the grant and clears the cookie. Superseded guard reads
      are cancellable; distinguish invalid access from availability. If activation
      or token refresh loses its response, check `/me` and refresh CSRF when a
      grant cookie arrived; otherwise request a new code without replaying it.
- [ ] Implement the paged inbox and explicit session start. Use learner DTOs
      only, rendering text through interpolation by the
      [generated-text contract](ui-guide.md#direction-and-copy), which the
      generator relies on. The player keeps its own answer
      buffer, the last acknowledged revision and derived dirty state. Reuse small
      safe visual helpers, never `ActivityDocumentView` or other key-bearing views.
- [ ] Implement explicit save and final submit without background retries. On
      timeout, offer a read of the persisted session. Preserve local edits until
      the child explicitly loads saved work; a confirmed terminal state locks edits
      and shows its saved outcome. Warn on route exit and browser unload when dirty.
- [ ] Keep native choice controls and labeled text/numeric inputs. Match the
      server's numeric grammar and retain invalid keystrokes for correction; use
      logical RTL spacing and LTR numeric entry. No localStorage/IndexedDB answer
      cache or service-worker API caching is introduced.
- [ ] Verify `./scripts/verify.sh`, `./scripts/publish.sh` and
      `npm --prefix frontend run e2e`. Use independent browser contexts for parent
      and child. Exercise activation, all answer types, save/reload, submit, parent
      grading and the child's refreshed completed state at 360px/200% text.
- [ ] Inspect actual child response bodies, accessibility labels and focus,
      discarded requests and no-AI counters; stop for user review.

## Task 7: Full-flow acceptance and documentation cutover

**Deliverable:** Evidence that the whole milestone works under restart,
concurrency and ownership failures, and maintained docs describing only what
actually shipped. This task does not introduce another architecture or tuning
phase.

**Files:** Extend the owning integration/browser suites from Tasks 1–6,
`Integration/ProductionHostTests.cs`, `MigrationTests.cs`,
`frontend/e2e/start-server.mjs` and isolated fixtures where required. Update
`README.md`, `docs/architecture.md`, `docs/ui-guide.md`, the status of the
child-flow spec and this plan. Keep evaluation history and costs intact.

- [ ] Add a full two-family/two-sibling browser/API scenario with separate
      contexts: activate, assign mixed work, save/reload, lose the submit response,
      check the saved pending result, grade, reload and read the unchanged final
      result. Assert zero AI calls and exact child response allowlists throughout.
- [ ] Prove cross-scheme API denial, CSRF identity separation, production cookie
      flags and host restart persistence in the real middleware composition.
      Reopen the child browser with its persistent cookie and verify access lasts
      only until the original grant expiry, without another activation.
      Include a device revoked while a save is paused after authentication.
- [ ] Run the race/retention suite for reset, withdrawal, archive, duplicate
      submission and final grading. Verify new pages can reach entries past 100,
      reset affects all owned rows, and another family's content/session survives.
- [ ] Test upgrading a disposable copy of the pre-child migration schema with
      existing parent content, fresh install and repeated migration. Compare stored
      snapshot JSON before/after. No real user database is deleted or migrated by
      verification, and management commands still work without AI configuration.
- [ ] Update docs in place: activation/disconnect/revocation instructions,
      parent-review scoring, archive/reset semantics, shell boundaries and actual
      test commands. Remove superseded current-state statements and temporary
      helpers; do not scatter status/history into additional design files.
- [ ] Run `./scripts/verify.sh`, `./scripts/publish.sh` and
      `npm --prefix frontend run e2e`. Expected: all pass with isolated providers,
      no pending EF model changes and all existing parent workflows retained.
      Use Playwright-managed screenshot/trace output paths.
- [ ] Record checks and remaining product limits in the handoff. Keep the
      independent human content-quality review explicit; software tests do not
      certify generated educational content. Mark this milestone implemented only
      after its real acceptance passes, and stop for the user's family sanity test.

## Review before remaining implementation

Review the spec and this plan together, especially parent grading of every
answered short-text question, separate-device access, explicit save, one attempt
per assignment, fixed grant expiry and the expanded destructive reset scope.
The user may adjust the remaining product choices before Task 6. Do not start
additional tasks merely because their checklists exist.

[decimal-parsing]: https://learn.microsoft.com/dotnet/api/system.decimal.tryparse
