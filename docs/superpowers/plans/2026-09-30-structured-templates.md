# Content-first activities implementation plan

**Goal:** Let a parent generate an editable activity from an unsaved valid plan,
repair it and freeze a reviewed snapshot, with independent template publication.

**Architecture:** Keep one .NET API project and direct DbContext access. The
TaskEngine owns pure contracts/resolution and scoped AI requests; a small
feature-owned worker persists bounded generation steps. One Angular route owns
local edits while SQLite owns saved drafts, operations and immutable snapshots.

**Tech stack:** Existing .NET 8, EF Core/SQLite,
Microsoft.Extensions.AI/OpenRouter, Angular standalone signals/Signal Forms,
native HTML and Tailwind 4. No additional provider, configuration source, UI or
orchestration framework.

**Spec:** [Content-first activities and structured templates][design]. Its
numbered sections own contracts and bounds; this plan owns files, interfaces,
tests and delivery order. Read both before implementation.

## Global constraints

- No Git mutations. This documentation task performs no implementation, paid
  calls, data reset or deployment. Preserve unrelated user work.
- Read README, architecture, commenting and UI guides. Keep meaningful XML
  docs/JSDoc accurate in the same changes as their contracts.
- All educational generation follows the generic AI path. No subject-specific
  generators, mandatory template publication, separate blueprint screen,
  per-question swarm, production retry/repair loop or child-flow placeholder.
- One bounded TaskDocument supports lenient draft saving and strict release;
  provider candidates pass strict structural checks before application.
- ActivityDraft exists before content generation. Template publication remains
  independent; released drafts are terminal and snapshots immutable.
- One worker; two shared provider-call slots; 32 queued/running operations
  globally, four per family, one active per draft; maximum 128 operations per
  draft; maximum two calls per operation; seven-day bulky artifact retention.
- Retain the spec's exact content/input/queue budgets, source rules and null
  semantics. Operation idempotency lasts for the draft lifetime; no external
  exactly-once billing claim.
- Follow the spec's [version ownership][versions]: one source per value, a
  shared engine revision and distinct evaluation versions. Do not duplicate
  counters in schemas, prompts, client code, fixture builders or documentation.
- Retain answer.value, parent-only keys, family authorization, CSRF,
  ProblemDetails, UTC, cancellation and the existing adapter/profile.
- Follow the spec's [disposable development-data policy][data-policy]: no data
  migration or backward compatibility. Local database recreation is authorized
  during implementation; final cutover still requires the value/test gates.
  Current product/architecture/README behavior remains current until cutover.

## Code quality

Working output and passing tests are necessary, but not sufficient. Each task
must leave code that is straightforward to read, explain and maintain:

- Use clear domain names, focused methods and explicit data flow. Keep state and
  mutations with their owning feature; share rules through the existing engine
  boundaries rather than duplicating them across API, worker, UI and evaluator.
- Prefer supported native .NET, EF Core and Angular APIs for the installed
  versions, checked against their official documentation. Keep provider protocol
  adaptation inside the existing adapter and presentation inside Angular.
- Apply KISS and DRY pragmatically: extract a helper for a real shared rule or
  responsibility, not merely to reduce line count. No speculative abstractions,
  empty scaffolding, pass-through layers or new dependencies without a concrete
  need in the current task.
- Fix the cause at its owning boundary. Do not stack special cases, silently
  coerce data, suppress failures or weaken validation/types to make a test pass.
  If a supported framework approach cannot meet the contract, explain the
  limitation and resolve the design before adding a workaround.
- Cover meaningful behavior, ownership, cancellation, concurrency and failure
  boundaries with isolated tests. Keep security and resource limits explicit;
  avoid hidden state and automatic side effects.
- Remove newly obsolete code, duplicate rules and stale comments/docs in the
  same task once callers have moved. Preserve readable structure over clever or
  compressed code. Leave no abandoned alternative or temporary patch behind.

## Review milestones

The user wants to learn the implementation through small reviews. **Each
numbered task below is one review milestone**; do not batch tasks unless the
user asks. If a task is too large to review comfortably, split it into named,
coherent checkpoints before implementing it.

For the authorized milestone, complete its implementation, focused cleanup and
relevant checks, including `scripts/verify.sh` and the isolated browser tests
for workflow changes. Then provide a concise walkthrough containing:

- What changed, why, and the important ownership/design decisions.
- A suggested reading order with links to the main files and one representative
  request or data flow to follow through the code.
- Verification results, limitations and what the next milestone would add.

**Stop for the user's review and explicit continuation before starting the next
milestone.** Address review feedback within the same milestone. Do not implement
later tasks or add their scaffolding in the background. This review pause is the
user's requested workflow; routine decisions within the authorized milestone do
not require repeated permission. Editing this plan does not start
implementation.

### Implementation discoveries

If implementation reveals a specification gap, conflicting requirement,
pre-existing bug or worthwhile improvement/optimization beyond the agreed task,
investigate enough to make the decision concrete, then ask the user how to
proceed before changing the affected design, behavior or scope. Do not silently
expand the task or add a workaround.

Present a concise decision brief:

- What was found, with relevant code, test or official-documentation evidence,
  and its practical impact.
- The feasible options, usually two or three, with their scope, complexity,
  effort and risks; include deferral only when it is safe.
- The recommended option and why, plus any spec/plan changes it requires.

Wait for the user's answer before implementing the affected change. Continue
only independent work already authorized within the current milestone; silence
does not authorize a choice. Record the agreed decision in the relevant spec or
plan before proceeding, and keep comments/tests aligned with the implementation.

Routine implementation choices and correcting defects introduced in the current
task within its agreed contract remain authorized; explain them in the milestone
review. A correction that requires changing that contract uses this checkpoint.

## Review focus

1. A replay after an operation advanced its draft must return the original
   operation before stale-revision checks (task 4).
2. A local unsaved keystroke must survive server operation completion even when
   server revision still matches (task 6).
3. Supplied bilingual sources and false/zero/empty/omitted/null choices must
   survive interpretation, saving and assembly without coercion (tasks 1/3/5).
4. A material saved manually with a strict mismatch cannot enter the question
   stage or be released through parent review (tasks 2/3/4).
5. A completed remote call with no durable checkpoint remains unknown after
   restart, while accepted earlier material and known usage survive (task 4).

## File and interface map

The following roots abbreviate exact repository paths in the tasks:

| Prefix      | Repository path                                       |
| ----------- | ----------------------------------------------------- |
| Engine      | backend/FamilyLearning.Api/TaskEngine                 |
| Features    | backend/FamilyLearning.Api/Features                   |
| Persistence | backend/FamilyLearning.Api/Infrastructure/Persistence |
| Client      | frontend/src/app                                      |
| Tests       | tests/FamilyLearning.Api.Tests                        |
| Evaluation  | tools/FamilyLearning.Evaluation                       |

Create focused files when their delivery task needs them, without empty
scaffolds, pass-through services or a generic service framework:

- `Engine/Models/LearningPlan.cs`: Canonical plan, scoped controls,
  material/question/length requirements.
- `Engine/Models/TaskRequest.cs`: TaskRequest, TaskResolution,
  ResolvedTaskRequest; add stage-input records with their task 2 consumers.
- `Engine/Models/TaskDocument.cs`: Editable/frozen shared content shape,
  candidate batches, measurements and origin/dependency records.
- `Engine/Models/TemplateAuthoring.cs`: Proposal/clarification envelope and
  request contracts, introduced with authoring in task 2.
- `Engine/EngineVersions.cs`: Sole schema-version and engine-revision constants;
  prompt stage labels derive from the engine revision.
- `Engine/Validation/LearningPlanValidator.cs`: Canonical plan and limits.
- `Engine/Validation/TaskDocumentValidator.cs`: Safe draft shape, strict
  candidate and release content checks.
- `Engine/TaskRequestResolver.cs`: Pure defaults, applicability, presence and
  effective requirements.
- `Engine/TaskAssembly.cs`: Shared stage selection, source insertion and
  validated candidate application for runtime and evaluation.
- `Engine/TextLength.cs`: Shared Unicode word measurement.
- `Engine/PlanChanges.cs`: Proposal identity normalization and actual change
  calculation, including the PlanChange result record.
- `Features/Activities/ActivityDraft.cs`: Bounded draft entity and application
  revision.
- `Features/Activities/ActivityContracts.cs`: Parent draft/save/adopt/release
  DTOs.
- `Features/Activities/ActivityEndpoints.cs`: Owned draft CRUD, adoption and
  atomic release.
- `Features/Activities/ActivityDraftChanges.cs`: Content/source differences and
  app-owned revision/acceptance-basis updates; validation derives staleness.
- `Features/Activities/GenerationOperation.cs`: Operation/step state and bounded
  evidence.
- `Features/Activities/GenerationOperationOptions.cs`: Queue, record, payload
  and retention policy.
- `Features/Activities/GenerationOperationEndpoints.cs`: Idempotent
  start/status/cancel.
- `Features/Activities/GenerationWorker.cs`: Short DB transitions, provider
  dispatch and restart/retention handling.
- `Features/Instances/TaskSnapshot.cs`: Self-contained immutable released
  entity.
- `Evaluation/ContentWorkflowPrototype.cs`: Temporary matched one-shot/split
  experiment over the shared engine.
- `Evaluation/EvaluationVersions.cs`: Sole report-format, automatic-check and
  Hebrew-review counters, consumed only by the evaluator.

Reuse the existing AI service/adapter, shared settings fields, template feature,
instance preview and evaluation files. Task 8 lists obsolete files to remove. No
separate production backend, repository, mediator or injectable wrapper around
each pure helper.

Shared engine signatures (small result records live beside their owning model):

```csharp
Dictionary<string, string[]> LearningPlanValidator.Validate(LearningPlan? plan);
TaskResolution TaskRequestResolver.Resolve(LearningPlan plan, TaskRequest input);
DraftDocumentCheck TaskDocumentValidator.ValidateDraft(
    ResolvedTaskRequest request, TaskDocument document);
Dictionary<string, string[]> TaskDocumentValidator.ValidateRelease(
    ResolvedTaskRequest request, TaskDocument document);
MaterialAcceptance TaskAssembly.AcceptMaterials(
    ResolvedTaskRequest request, TaskDocument current, MaterialCandidateBatch candidate);
TaskDocument TaskAssembly.AcceptQuestions(
    ResolvedTaskRequest request, TaskDocument current, QuestionCandidateBatch candidate);
int TextLength.CountWords(string text);
LengthMeasurement[] TextLength.Measure(
    ResolvedTaskRequest request, TaskDocument document);
LearningPlan PlanChanges.AssignNewIds(LearningPlan proposal, LearningPlan? previous);
PlanChange[] PlanChanges.Compare(LearningPlan? previous, LearningPlan current);
Task<AiResult<AuthoringReply>> AiGenerationService.AuthorAsync(
    TemplateAuthoringInput input, CancellationToken ct);
Task<AiResult<MaterialCandidateBatch>> AiGenerationService.GenerateMaterialsAsync(
    MaterialGenerationInput input, CancellationToken ct);
Task<AiResult<QuestionCandidateBatch>> AiGenerationService.GenerateQuestionsAsync(
    QuestionGenerationInput input, CancellationToken ct);
Task<AiResult<MaterialCandidate>> AiGenerationService.ReplaceMaterialAsync(
    MaterialReplacementInput input, CancellationToken ct);
Task<AiResult<QuestionCandidate>> AiGenerationService.ReplaceQuestionAsync(
    QuestionReplacementInput input, CancellationToken ct);
```

TaskResolution holds either a complete Value or Errors. DraftDocumentCheck
separates unsafe-input Errors from saved-content Diagnostics.
MaterialCandidateBatch contains generated id/title/body entries only;
QuestionCandidateBatch contains task title/instructions plus questions.
MaterialAcceptance holds either validated applicable material content or
diagnostic-only candidates; no partial application of a material batch.
AcceptQuestions validates the whole batch before assigning app question IDs and
records all app-selected source revisions as dependencies. Replacement validates
its target and assembled safety/aggregate bounds while preserving unrelated
draft diagnostics, task-level fields and the target ID. Pure helpers never
persist or authorize.

Drafts persist copied plan, accepted TaskRequest and document; resolve on valid
save/start rather than keeping an independently mutable resolved cache.
Operations pin resolved input and snapshots store it. Parent DTOs expose derived
diagnostics; only operation failures and release measurements need durable
evidence. Server fields (revision, origin, dependency and review metadata) never
come from an editable-content payload.

Ownership stays concrete: TaskRequestResolver resolves values; validators own
acceptance and derived diagnostics; TaskAssembly selects bounded stages and
applies content; AiGenerationService owns prompts, schemas and one provider
call. Features own authorization, persistence and operation transitions. The
evaluator reuses the engine without an application database. Angular owns form
state and presentation, never authoritative content checks. Share actual rules;
do not force HTTP writes and the worker through a generic command pipeline.

## Delivery sequence

Tasks 1–2 provide the minimal shared core and comparison prototype before
persistent production cutover. Tasks 3–7 can be built and tested with disposable
databases/providers while live evidence is pending. Task 8 is blocked until the
prototype demonstrates useful value under the pre-registered rubric and every
isolated check passes. No temporary prototype becomes a second deployed path.

### Task 1: Define and test the canonical core

**Files:** Create the Engine model/helper/validator files in the map. Reuse
`Engine/Models/TaskSettings.cs` and
`Engine/Validation/TaskSettingsValidator.cs`. Create
`Tests/TaskEngine/LearningPlanTests.cs`, `TaskRequestResolutionTests.cs`,
`TaskAssemblyTests.cs`, `TextLengthTests.cs` and `PlanChangesTests.cs`.

**Interfaces:** Produce the pure signatures above and the spec sections 3–4/8
contracts. Following the Task 1 cleanup review, keep only contracts consumed by
the canonical core. Authoring/stage DTOs and provider metadata belong in task 2,
when their consumers and verification are implemented.

- [ ] Add failing canonical fixtures for generated Hebrew reading with an
      explicitly adjustable target/story type; no-material numeric questions;
      supplied bilingual source; fixed mixed formats; selectable format. Use
      synthetic developer fixtures, never production seed content.
- [ ] Use EngineVersions for plan defaults, validation and evidence. Reject an
      unsupported schema version; preserve historical evidence instead of
      relabeling it. No per-helper policy counters or client-owned version.
- [ ] Pin presence tests: omitted default resolves; false/zero/empty optional
      text survive; null maps/members, numeric strings, unknown/inapplicable
      IDs, fixed overrides and blank required text fail. Cover raw HTTP JSON
      later in task 3 without inventing a general optional-value framework.
- [ ] Pin identity/limit tests: null new IDs, retained IDs on rename/move,
      reject unknown/duplicate/wrong-category IDs, fixed versus adjustable
      requirements, total/per-material overlap, mixture coverage and exact
      choice counts. Reject int.MaxValue allocation attempts and four
      4,000-character sources before AI; a feasible question count above 20
      remains valid.
- [ ] Pin source/measurement tests: preserve accepted quotes, newlines, niqqud
      and mixed text; supplied IDs never appear in generated output. Assert
      `שלום עולם`/`שלום — עולם` → 2, `בעלי־חיים`/`don't` → 1, emoji → 0,
      supplementary letters count, titles/questions excluded, target
      satisfaction null and exact/range Boolean. Keep 100–150 words a strict
      range.
- [ ] Pin validation modes: incomplete manual answers save with diagnostics;
      strict candidates reject them; changing a correct choice never chooses a
      new answer. A strict material mismatch returns an unapplied candidate;
      missing/duplicate IDs or a bad question batch apply nothing and allocate
      no accepted question IDs.
- [ ] Pin generated-material and question acceptance fingerprints plus source
      revisions. Changed effective input or source derives stale content;
      adoption updates that basis without rewriting generation provenance. No
      persisted stale/readiness flag can disagree with the shared validator. An
      engine-revision-only change does not stale unchanged requirements; current
      validators still enforce generation and release rules.
- [ ] Run `dotnet test --filter FullyQualifiedName~TaskEngine`; observe new
      assertions fail, implement the pure units, then rerun until they pass.
      Verify input collections are not mutated and identical inputs/engine
      revision yield identical requirements/measurements, excluding IDs/time.

### Task 2: Prove scoped generation and the comparative prototype

**Files:** Modify `Engine/Ai/AiGenerationService.cs`, `AiPrompts.cs`,
`AiSchemas.cs`, `AiGenerationOptions.cs`, `AiGenerationException.cs`,
`template.schema.json`; create `Engine/Models/TemplateAuthoring.cs`, add the
stage-input records to `Engine/Models/TaskRequest.cs`, and introduce provider
provenance in `Engine/Models/TaskDocument.cs` with its actual consumers. Create
`materials.schema.json`, `questions.schema.json`
alongside them. Replacement schemas specialize the same material/question
definitions. Modify `backend/FamilyLearning.Api/appsettings.json` and
`backend/FamilyLearning.Api/Infrastructure/Ai/OpenRouterRegistration.cs` for
named request/schema limits. Create
`Tests/TaskEngine/ContentGenerationTests.cs`; modify
`Tests/Fixtures/AiFixtures.cs`, `Tests/TaskEngine/AiDiagnosticsTests.cs`,
`Tests/TaskEngine/AiCapacityTests.cs`, and
`Tests/Integration/OpenRouterConfigurationTests.cs`. Create
`Evaluation/ContentWorkflowPrototype.cs` and `Evaluation/EvaluationVersions.cs`;
modify `Evaluation/EvaluationCommand.cs`, `EvaluationPlan.cs`,
`EvaluationRunner.cs`, `EvaluationReport.cs`, existing version consumers and
`cases.json`.

**Interfaces:** Implement the five AiGenerationService signatures. Prototype
consumes fixed LearningPlan/TaskRequest and a one-shot/split variant; both use
same source assembly, checks, provider configuration and evidence capture. The
worker and evaluator also use TaskAssembly's same finite stage-selection
methods; only scheduling, storage and evaluation retries differ. Stage inputs
contain one resolved request, accepted material revisions and an app-selected
target where relevant.

- [ ] Add isolated wire tests proving author/refine interprets only the plan;
      material stage returns generated bodies/titles only; question stage sees
      exact accepted materials and resolved settings; replacement receives only
      its authorized target plus needed context. No tools, family/operation
      metadata, unresolved defaults, hidden model reviewer or chain-of-thought
      request goes to the provider. Keep schema rules in schemas and shared
      semantic/presentation rules in one prompt fragment.
- [ ] Wire AiSchemas to the central schema constant and derive prompt labels
      from stable stage names plus the engine revision. Test that metadata and
      the actual wire schema agree with those owners, including the temporary
      one-shot stage. Remove superseded prompt counters; preserve immutable
      shared schemas during per-request specialization.
- [ ] Move evaluator format/check/judge counters into EvaluationVersions without
      changing their meaning. All existing consumers reference this owner;
      advance affected values as prototype/report contracts actually change.
- [ ] Add call-count and checkpoint-independent tests: generated reading needs
      at most two calls; supplied source/question-only needs one; material
      structural or strict-length failure prevents questions; target mismatch
      allows continuation; question failure retains accepted material. Absent or
      stale generated materials regenerate together; current accepted ones can
      be explicitly reused. New activity copies inputs without generated content
      so fresh generation cannot silently reuse old materials. Validate manually
      saved material under the same strict preflight before questions.
- [ ] Test every repair returns a complete valid target; a forged material ID,
      unrelated question change or supplied-source rewrite is rejected. Assert
      question batches own title/instructions, replacements preserve them, and
      valid repair remains possible beside an unrelated incomplete answer.
      Record dependencies from materials actually sent, never a model-declared
      subset.
- [ ] Add maximum-size wire fixtures for the 512 KiB compiled HTTP body, 64 KiB
      schema and 32,000-character output ceilings; test boundary + 1. Count
      schemas in both prompt and response-format locations. Preserve per-request
      constraints through actual SDK serialization and concurrent
      different-count/format requests. Tighten unsupported resource bounds
      explicitly before cutover rather than silently dropping requirements.
- [ ] Run the new test filter, implement scoped prompts/schemas/service methods
      through the unchanged adapter, then require passing isolated tests in
      schema and JSON-only modes. Refusal/truncation/transport errors stay safe;
      no new production retry, provider or model profile.
- [ ] Make AI failures independent of persistence: safe call failure/category
      and field diagnostics must not claim "nothing was saved." Test a failed
      question call after accepted material and a synchronous authoring failure;
      each caller renders recovery from its own saved state, not string patches.
- [ ] Add three fixed-plan prototype cases: generated Hebrew reading, exact
      supplied bilingual source and question-only control. Preserve case/source
      identity, matched model/settings, all attempts/failures, stage output,
      usage coverage, cost and latency. Run dry/fixture trials with zero real
      calls; ensure one-shot and split differ only in decomposition.
- [ ] Before any authorized live trial, save an experiment artifact declaring
      sample count/repeats, held-out cases, usable-task rubric, blinded
      randomized human review, acceptable cost/latency and measurable
      control/recovery gains. Obtain the user's explicit paid-call/cost budget.
      Without it, make no live calls and leave the comparative-value gate unmet.
- [ ] Record the falsifiable decision: added cost without useful quality,
      control or recovery benefit requires reconsideration before final cutover.
      A passing mock or external STACK case is not evidence of Hebrew
      improvement.

### Task 3: Persist editable drafts and release immutable snapshots

**Files:** Create ActivityDraft, ActivityContracts, ActivityEndpoints,
ActivityDraftChanges and TaskSnapshot from the map. Modify
`Persistence/LearningDbContext.cs`, `Features/Templates/TemplateContracts.cs`,
`TemplateEndpoints.cs`, `Features/Instances/InstanceContracts.cs`,
`InstanceEndpoints.cs`, `backend/FamilyLearning.Api/Program.cs` and existing
`Infrastructure/Web/ApiConfiguration.cs`. Create
`Tests/Integration/ActivityDraftTests.cs` and `ActivityReleaseTests.cs`; modify
`ParentWorkflowTests.cs`, `RequestValidationTests.cs`,
`LibraryDeletionTests.cs`.

**Interfaces:** Implement spec section 5 draft/create/save/adopt/release routes.
Create accepts plan/input plus optional templateId/expectedVersion, or owned
snapshotId (mutually exclusive). PUT accepts expectedRevision and editable
plan/input/content. Release accepts one expectedRevision as the explicit parent
review action and returns snapshot ID/preview. TaskSnapshot uses existing
`/api/instances/{id}` previews.

**Accepted staging decision (1 October 2026):** Implement the new lifecycle in
a separate API composition exercised by an isolated test host with a fresh
database. Keep the deployed route composition and schema-4 screens unchanged
until the gated cutover. Do not add schema compatibility readers, a runtime
lifecycle switch or a second production project. The new composition shares
the application's authentication, CSRF, JSON and ProblemDetails policies.
Task 8 switches the host composition, replaces migrations for the final model,
and removes the superseded route/contracts code after its callers move.
An additive Task 3 schema migration keeps the EF model and migration snapshot
consistent in the meantime; it performs no old-data conversion.

- [x] Add failing tests for creation from an unsaved valid plan with no AI;
      owned-template copy pinned to expectedVersion; owned-snapshot copy with
      cleared review; 404 family boundaries; raw omitted/null/false/zero/empty
      input behavior. Canonical source submissions are parent-accepted without a
      confirmation flag; enforce source identity/kind and preservation.
- [x] Add lenient-save and adoption tests. Missing/invalid answer associations
      produce diagnostics; unbounded/unsafe input fails. Generated material
      edits bump revisions and stale every dependent question. Replace source
      updates copied plan/input atomically, preserves published template,
      resolves again and invalidates dependencies. Plan/settings changes cannot
      keep content silently current; adoption cannot waive strict requirements.
- [x] Add release tests for counts/formats/keys/strict lengths/staleness/current
      review and active-operation blocking. A target-length mismatch alone must
      not block release; exact/range mismatches must block it. Assert
      plan/input/content/keys/policies/provenance and measurements are frozen,
      with unique sourceDraftId and sourceDraftRevision. Duplicate exact release
      returns the same snapshot; stale/different revision conflicts before
      writes. Deleting that snapshot leaves the draft terminal: exact release
      replay returns 410 Gone, never recreates content; deleted draft
      returns 404.
- [x] Add simultaneous save/release tests proving one revision wins. Released
      drafts reject edits; cloning starts a new draft. Template deletion retains
      independent drafts/snapshots; draft deletion retains its snapshot; family
      reset clears learning records and preserves accounts/configuration.
- [x] Run failing tests, implement direct DbContext short
      transactions/concurrency tokens and server-owned metadata. Derive ordinary
      diagnostics instead of adding competing persisted readiness flags. Use
      disposable databases with the new schema; do not implement old-data
      conversion or compatibility paths.
- [x] Run
      `dotnet test --filter 'FullyQualifiedName~ActivityDraftTests|FullyQualifiedName~ActivityReleaseTests|FullyQualifiedName~LibraryDeletionTests'`;
      require pass and zero provider calls for save/adopt/release/preview.

### Task 4: Implement the bounded durable worker

**Files:** Create GenerationOperation, GenerationOperationOptions,
GenerationOperationEndpoints and GenerationWorker from the map. Modify
LearningDbContext, Program.cs, appsettings.json and ActivityEndpoints. Create
`Tests/Integration/GenerationOperationTests.cs`, `GenerationRecoveryTests.cs`
and `GenerationRaceTests.cs`.

**Interfaces:** Use spec section 6 operation/status/cancel routes and finite
states. Worker dispatches only the four supported content actions through
task 2. One active-operation index/conditional draft update and unique
family/key index protect admission. Inject existing scoped services through
fresh scopes; no DbContext is shared with provider calls or between threads.

Continue the accepted Task 3 staging: `AddActivityGeneration` activates the
worker alongside the new API in the isolated host. Activation in `Program.cs`
and deployment configuration belongs to the gated cutover, without a runtime
enable flag. Task 4 removes the temporary block on manual saves during active
work; revision checks fence their late results. Reuse the existing AI service's
shared provider slots and one native family start limiter, acquiring a start
permit only after durable-key replay checks.

- [x] Add idempotency tests in exact order: authorized draft lookup, existing
      key comparison, then new-operation revision/admission checks. Replay after
      material acceptance/terminal status returns original operation despite a
      changed current revision. Same key/different original request returns 409;
      cross-family access returns 404. Stored fingerprint binds original
      revision, kind/target/instruction and separately captured effective-input
      hash.
- [x] Add budget tests for 32 global/four family/one draft, two shared provider
      slots, ten family starts/minute, two steps/operation and 2 MiB evidence.
      The 129th new operation fails safely; viewing, edits, release and existing
      key replay remain available. Explicit clone is unbilled and never
      automatic.
- [x] Add transactional acceptance tests: candidate + content + draft revision +
      step checkpoint + queued next stage commit together. Queued → calling is
      an atomic claim conditioned on active identity and matching revisions;
      cancel/edit before a successful claim produces no provider call. Material
      strict failure preserves old material and makes zero question calls.
      Question failure retains accepted material; explicit GenerateQuestions
      reuses it. No partial document appears when a checkpoint transaction
      fails.
- [x] Add deterministic race barriers for edit/Undo/source change/cancel/delete/
      reset before claim and while provider waits. Store late candidates as
      unapplied conflicts, clear active state and stop downstream. Cancellation
      wins locally before transport cancellation, retains terminal cancelled
      status and allows only known usage metadata to arrive later. No provider
      transaction stays open.
- [x] Test expected provider/validation failures terminate only their operation,
      release its active reference and let the next queued operation run. Saved
      material survives and the UI does not claim a full rollback. Distinguish
      user cancellation, provider timeout and host shutdown. Do not globally
      ignore unexpected BackgroundService exceptions or reuse a failed
      DbContext.
- [x] Add restart fixtures: queued resumes; accepted material + queued questions
      resumes only questions; calling without accepted checkpoint becomes
      unknown and is never replayed. Preserve known response metadata versus
      unknown usage. Repeated GET/poll/reload makes zero starts. Test lost
      start/release responses.
- [x] Test queued work after an engine/schema or nonsecret AI-profile change
      stops without a provider call and retains accepted content. Credential
      rotation alone remains resumable. No legacy engine registry or silent
      model switch is introduced to recover old operations.
- [x] Add seven-day artifact expiration tests with an injected clock. Purge at
      most 32 terminal artifacts per pass; preserve
      key/fingerprints/status/known usage for draft lifetime. An expired
      diagnostic is explicit; key replay never restarts work. Draft deletion
      removes tombstones and replay returns 404.
- [x] Run those failing integration tests, implement the single-process worker
      and short transitions, then rerun until all pass. Document the one-process
      deployment constraint; do not add distributed leases speculatively.

### Task 5: Replace the blueprint editor with plan chat and native controls

**Files:** Modify `Features/Ai/AiEndpoints.cs`, `Client/core/api/models.ts`,
`learning-api.ts` and `Client/app.routes.ts`. Under
`Client/features/activities/`, create
`plan-editor/plan-editor.{ts,html,spec.ts}`,
`template-chat/template-chat.{ts,html,spec.ts}`, `learning-plan.fixture.ts` and
`activity-workspace/activity-workspace.{ts,html,spec.ts}`. Route existing
template URLs and activity URLs directly to ActivityWorkspace with route
context; do not retain a pass-through TemplateEditor component. Modify
`Tests/Integration/AiAuthoringTests.cs` and shared settings fields as needed.

**Interfaces:** Author endpoint uses TemplateAuthoringInput/AuthoringReply and
returns app-computed PlanChange[], echoed requestId/baseRevision and metadata.
ActivityWorkspace owns raw plan/input, valid projection, source confirmation,
client revision, baseline, clarification and twenty-entry Undo. Presentation
children emit edits without owning another draft. Saved template publication
keeps expectedVersion.

Continue the approved isolated-composition staging through the frontend:
`contentFirstRoutes` loads ActivityWorkspace directly, while the deployed routes
keep their existing callers until the gated cutover. A native Angular test build
selects that composition; no runtime flag or compatibility reader is added.
The staged authoring status supplies the server-owned schema version for manual
plan creation. Task 5 owns the source/input submission gate; Task 6 adds the
draft-saving and generation actions that consume it. Keep legacy editor code
only while the deployed composition still uses it, then remove it at cutover.

- [x] Test initial proposal/refinement and successive clarification exchanges:
      at most one question per reply, one call per submitted parent message and
      no automatic follow-up call or mandatory clarification. Preserve original
      intent and answers until a proposal applies, then use the current plan as
      the agreed context. The six-turn/12,000-character request cap requires
      explicit consolidation when reached; it is not a lifetime conversation
      quota. Test further refinement after consolidation without silent context
      loss. Keep operative assumptions in the plan, identity preservation and
      computed removals. Retained fixed source cannot change through AI; direct
      source edits are explicit and confirmed.
- [x] Test workspace-only source confirmation: an unconfirmed AI-extracted
      source blocks publication, draft creation/update and generation before
      HTTP submission. Confirming or directly editing it enables submission; the
      payload contains canonical source text, no confirmation flag. Reloaded
      saved sources are already accepted; Undo tracks confirmation locally.
- [x] Test late proposal after typing/invalid input/Undo/cancel/route change
      never applies; identical proposal creates no history. Clarification
      retains its original request then expires on conflicting edits. A clean
      proposal applies locally with visible changes and no mandatory Accept
      page.
- [x] Test independent template Save: no generation, no activity mutation,
      duplicate success handled once, pending save briefly locks edits, stale
      publication preserves local input. Lost response offers checking the
      library; no automatic publication retry or claimed rollback.
- [x] Implement app-owned purpose/material/question/requested-choice controls,
      inline source confirmation and meaningful Hebrew change labels. No raw key
      editor or generated prompt textarea. AI-unavailable state allows direct
      editing/publication. Do not expose unrequested passage/story controls.
- [x] Use initialized, control-friendly form values rather than binding partial
      domain/HTTP records directly. Test the boundary mapping for blank optional
      numbers/selects, empty text, false and zero, and conditional control
      changes. Reuse the current native input helpers and Signal Forms; no new
      form engine.
- [x] Run `dotnet test --filter FullyQualifiedName~AiAuthoringTests` and
      `npm --prefix frontend test -- --watch=false`; require isolated passes.

### Task 6: Complete editable activity and operation UI

**Files:** Modify task 5's
`Client/features/activities/activity-workspace/activity-workspace.{ts,html,spec.ts}`.
Create `activity-document-editor/activity-document-editor.{ts,html,spec.ts}` and
`generation-status/generation-status.{ts,html,spec.ts}` under that feature.
Extend ActivityWorkspace using its PlanEditor/TemplateChat in the same feature;
no draft state moves or duplicates. Modify API models/LearningApi,
app.routes.ts, library files and
`Client/features/instances/instance-preview/instance-preview.{ts,html}`.

**Interfaces:** ActivityWorkspace owns raw local plan/input/content, saved
revision/baseline, client edit revision, operation ID and bounded Undo. API
methods map exactly to tasks 3–4; content editor emits allowed changes/selected
targets, status component polls through its owner and never starts work.

Continue the approved isolated composition: staged library and snapshot-preview
components consume the new contracts directly, while deployed legacy callers
remain until cutover. Share the read-only document presentation for snapshot and
reconciliation views. Extend the draft detail with engine-derived length
measurements so advisory targets and strict expectations are visible without a
client-side counting implementation.

- [x] Test generate from an unsaved valid plan creates/saves a draft before the
      operation. Required Save failure prevents generate/repair/adopt/release;
      unsaved invalid fields stay visible. Reload resumes the same operation and
      saved draft, without claiming local keystrokes or chat were persisted.
- [x] Test full document edits, explicit source replacement, answer
      invalidation, strict diagnostics, current-revision adoption and bounded
      content-only Undo. Restoring content creates a new save revision and never
      restores readiness, review/provenance/operation state. No ordinary edit
      uses AI.
- [x] Test operation success with unsaved local typing: retain local buffer,
      display available server result, require explicit reconcile/reload. A
      stale save preserves local input. Test lost responses, failed/unknown
      stages, expired candidates and explicit paid retry warning.
- [x] Implement four honest scoped actions, real stage statuses, cancel and
      diagnostic-only candidate viewing. Unparseable candidates cannot be copied
      into live content blindly; only parsed bounded content can be edited.
      ReplaceQuestion changes one whole question; ReplaceMaterial is
      generated-only. No automatic repair or extra approval between normal
      stages.
- [x] Implement explicit **סימון כמוכנה**, current-revision parent review and
      immutable preview/new-draft editing. Hide unfinished assignment/child
      actions. Show unsaved/saving/saved/error states and navigation warning.
- [x] Run Angular tests; check keyboard focus/live announcements, RTL mixed
      text, native labels/controls, narrow 360px layout and 200% text without a
      framework. Parent answers render as text and never enter any child-facing
      contract.

### Task 7: Integrate evaluation evidence and comparison

**Files:** Modify `Evaluation/EvaluationReport.cs`, `EvaluationRunner.cs`,
`EvaluationPlan.cs`, `EvaluationComparison.cs`, `EvaluationSummary.cs`,
`EvaluationRunStore.cs`, `EvaluationFiles.cs`, `EvaluationCapture.cs`,
`HebrewJudge.cs`, `EvaluationVersions.cs`, `cases.json`,
`hebrew-review-samples.json`, `wwwroot/app.js` and `wwwroot/index.html`. Modify
existing evaluation tests under `Tests/TaskEngine`, dashboard integration tests
and `frontend/e2e/evaluation-ui.test.mjs`.

**Interfaces:** A case selects Prompt or InitialPlan, never both. Prompt permits
at most three 4,000-character refinements; fixed-plan trials omit authoring.
This bounds the evaluation fixture and call budget, not workspace conversation.
Stage records carry role and engine/schema versions, exact effective
input/schema + stable hashes, raw output, candidate acceptance/application state
and optional usage. Skipped stages are explicit. Separate interpretation,
generation, replacement and end-to-end outcomes in the updated report format;
reuse one provider profile and judge.

- [x] Advance only affected EvaluationVersions values for contract/behavior
      changes; keep defaults, readers, messages and judge metadata derived. Test
      unsupported report rejection and check/judge comparison boundaries; an
      engine revision difference alone must not block a generation experiment.
- [x] Add fixture tests for author/refine/clarification, fixed-plan generation,
      supplied-source skip, scoped repair and early stop. All real attempts and
      evaluator-only 429 retries consume budget; planned totals sum actual
      applicable stages plus optional judge/calibration, not cases × two.
- [x] Preserve every failed/unapplied candidate and known partial usage; null
      means unknown. Capture exact input/schema evidence with fingerprints,
      source revisions, actual model/provider and engine revision; no content or
      answers in ordinary logs. Keep parent corrections/time-to-ready evidence.
- [x] Update comparison tests: matched plans/inputs/sources/settings and
      automatic-check rules for generator trials, matched edit sequences for
      authoring. Reject incompatible old report formats safely. If judge
      identity/config/prompt/ rubric/coverage or calibration differs, suppress
      judge-quality deltas while retaining independently valid
      deterministic/human comparisons.
- [x] Reuse TextLength and independent case expectations. Keep
      fixed/range/target meanings, including the historical 100–150 regression;
      authoring that drops a demand fails adherence. Update judge source paths
      and its owned version while preserving planted defects and auditing
      disputed Hebrew labels with humans.
- [x] Run existing evaluation test filters,
      `node --test frontend/e2e/evaluation-ui.test.mjs` and
      `./scripts/evaluate-ai.sh --case all`; require passes and a no-call
      preview. Retain dashboard startup/offline access, Host/Origin/CSRF/CSP,
      confirmation, cancellation and safe artifact loading.

Task 7 evidence: the evaluator now runs the shared structured engine without
changing the deployed API/Angular composition. Preview sums expected applicable
fixture stages; the hard call ceiling counts every actual attempt when authored
plans deviate. Reports use only the current owned format, with no historical
conversion. Independent case adherence, workflow completeness, generator inputs
and judge compatibility are checked separately. Shared per-call evidence retains
response provenance before validation, including rejected authoring.
No paid calls or human quality-gate claims accompany this milestone.

### Task 8: Verify the complete flow and perform the gated cutover

**Files:** Modify `frontend/e2e/ai-provider.mjs`, `parent-workflow.spec.ts`,
`http-boundaries.spec.ts`, `theme.spec.ts` and
`Tests/Integration/MigrationTests.cs`. Replace obsolete prototype migrations
under Persistence/Migrations with a fresh `InitialCreate` migration and model
snapshot for the final model. Update README, docs/product-specification.md,
docs/architecture.md, docs/ui-guide.md and touched contract comments only when
behavior is implemented.

**Delete after callers move:** `Engine/Models/TaskTemplateDefinition.cs`,
`TaskContent.cs`, `Engine/Validation/ParameterValidator.cs`,
`TemplateValidator.cs`, `TaskContentValidator.cs`,
`Engine/Ai/content.schema.json`, the retained legacy `blueprint.schema.json`,
`Features/Instances/TaskInstance.cs`, and the old TaskInput record from
TaskSettings. Delete obsolete
`Client/features/templates/template-editor/`, `ai-template-author/`,
`ai-template-form/`, `Client/features/instances/create-instance/` and
`instance-form/` after transferring valuable tests. Remove
`Evaluation/ContentWorkflowPrototype.cs` and temporary one-shot dispatch after
capturing the decision; retain comparison artifacts and supported stage
evidence.

Following the accepted Task 3 staging decision, also remove the superseded
`TemplateEndpoints` and `InstanceEndpoints` route composition and their schema-4
DTOs when the canonical plan/snapshot endpoints become the deployed composition.

**Interfaces:** One deployed content-first lifecycle; no compatibility reader,
dual production path or mutable historical TaskInstance.

- [ ] Add isolated browser acceptance: prompt → requested controls → generate
      without template Save → edit/replace question → Save draft → parent review
      → mark ready → frozen preview. Separately save a template and prove it
      remains independent. Include reload during generation and a later template
      change with unchanged released content.
- [ ] Add browser blockers/races: supplied bilingual source preserved;
      no-passage control; strict bad material prevents question call; question
      failure keeps material; answer deletion blocks release; unsaved
      typing/Undo survive late output; cancel/unknown/409 preserve work; no
      placeholder child delivery.
- [ ] Verify the initial migration on an empty disposable SQLite database,
      repeat startup without data loss and provision a parent through the
      existing command. Remove old-schema upgrade/preservation fixtures; retain
      fresh-installation, ownership and normal family-reset coverage. Automated
      tests never reset the user's database.
- [ ] Review prototype evidence against the pre-registered rubric/budget and
      record proceed/reconsider. If extra cost bought no useful benefit, stop
      final cutover and revise the split. If no live evidence was authorized,
      leave this gate visibly incomplete rather than inventing proof.
- [ ] Remove obsolete callers/contracts/UI only after isolated checks cover
      their meaningful boundaries. Update current docs and real fixture-derived
      call examples; remove planned-design banners when behavior actually lands.
- [ ] Run `./scripts/verify.sh`, `npm --prefix frontend run e2e` and
      `git diff --check`; require exit 0. Inspect 360px/200% text/keyboard/RTL,
      ownership, source fidelity, races and artifact limits. Review the diff for
      unrelated changes, credentials, DBs, keys or generated outputs.
- [ ] When local setup needs the new schema, stop this project's watchers and
      connections, delete only its configured local SQLite database and related
      journal/WAL files, then initialize from the new baseline. No backup or
      record preservation is required. Keep external configuration/credentials/
      keys, recreate a parent if needed and report the reset. This authorization
      does not require repeating approval or bypass the final value/test gates.
      Do not commit, deploy or delete unrelated data.

Child activation, assignment, attempts, scoring and reports require a later
separate vertical-slice plan. This plan prepares immutable self-contained
content and answer keys; the child slice owns scoring policy and its versions.

[design]: ../specs/2026-09-30-structured-templates-design.md
[versions]: ../specs/2026-09-30-structured-templates-design.md#version-ownership
[data-policy]: ../specs/2026-09-30-structured-templates-design.md#development-data-and-compatibility
