# Content-first activities implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` or `superpowers:executing-plans` to
> implement task by task. Steps use checkboxes. Repository instructions prohibit
> commits, staging, branching and every other Git mutation; the user handles
> Git.

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
- Schema 5; authoring v23; materials/questions/replace-material/replace-question
  v1; Hebrew review v9; report format 4; checks 9; resolution/assembly,
  validation and measurement policy 1. Temporary one-shot experiment:
  prototype-one-shot-v1.
- Retain answer.value, parent-only keys, family authorization, CSRF,
  ProblemDetails, UTC, cancellation and the existing adapter/profile.
- No destructive migration before comparative-value and isolated-test gates.
  Current product/architecture/README behavior remains current until cutover.

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

Create focused files, not a generic service framework:

- `Engine/Models/LearningPlan.cs`: Canonical plan, scoped controls,
  material/question/length requirements.
- `Engine/Models/TaskRequest.cs`: TaskRequest, TaskResolution,
  ResolvedTaskRequest and stage-input records.
- `Engine/Models/TaskDocument.cs`: Editable/frozen shared content shape,
  candidate batches, measurements and origin/dependency records.
- `Engine/Models/TemplateAuthoring.cs`: Proposal/clarification envelope and
  PlanChange.
- `Engine/Validation/LearningPlanValidator.cs`: Canonical plan and limits.
- `Engine/Validation/TaskDocumentValidator.cs`: Safe draft shape, strict
  candidate and release content checks.
- `Engine/TaskRequestResolver.cs`: Pure defaults, applicability, presence and
  effective requirements.
- `Engine/TaskAssembly.cs`: Authoritative source insertion and validated
  candidate application.
- `Engine/TextLength.cs`: Versioned Unicode word measurement.
- `Engine/PlanChanges.cs`: Proposal identity normalization and actual change
  calculation.
- `Features/Activities/ActivityDraft.cs`: Bounded draft entity and application
  revision.
- `Features/Activities/ActivityContracts.cs`: Parent draft/save/adopt/release
  DTOs.
- `Features/Activities/ActivityEndpoints.cs`: Owned draft CRUD, adoption and
  atomic release.
- `Features/Activities/ActivityDraftChanges.cs`: Content/source differences,
  app-owned revisions and conservative staleness.
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
contracts. Stage inputs contain the one resolved request plus accepted material
revisions and an app-selected target where relevant.

- [ ] Add failing canonical fixtures for generated Hebrew reading with an
      explicitly adjustable target/story type; no-material numeric questions;
      supplied bilingual source; fixed mixed formats; selectable format. Use
      synthetic developer fixtures, never production seed content.
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
- [ ] Run `dotnet test --filter FullyQualifiedName~TaskEngine`; observe new
      assertions fail, implement the pure units, then rerun until they pass.
      Verify input collections are not mutated and identical inputs/policy
      versions yield identical requirements/measurements, excluding IDs/time.

### Task 2: Prove scoped generation and the comparative prototype

**Files:** Modify `Engine/Ai/AiGenerationService.cs`, `AiPrompts.cs`,
`AiSchemas.cs`, `AiGenerationOptions.cs`, `template.schema.json`; create
`materials.schema.json`, `questions.schema.json` alongside them. Replacement
schemas specialize the same material/question definitions. Modify
`backend/FamilyLearning.Api/appsettings.json` and
`backend/FamilyLearning.Api/Infrastructure/Ai/OpenRouterRegistration.cs` for
named request/schema limits. Create
`Tests/TaskEngine/ContentGenerationTests.cs`; modify
`Tests/Fixtures/AiFixtures.cs`, `Tests/TaskEngine/AiDiagnosticsTests.cs`,
`Tests/TaskEngine/AiCapacityTests.cs`, and
`Tests/Integration/OpenRouterConfigurationTests.cs`. Create
`Evaluation/ContentWorkflowPrototype.cs`; modify
`Evaluation/EvaluationCommand.cs`, `EvaluationPlan.cs`, `EvaluationRunner.cs`,
`EvaluationReport.cs` and `cases.json`.

**Interfaces:** Implement the five AiGenerationService signatures. Prototype
consumes fixed LearningPlan/TaskRequest and a one-shot/split variant; both use
same source assembly, checks, provider configuration and evidence capture.

- [ ] Add isolated wire tests proving author/refine interprets only the plan;
      material stage returns generated bodies/titles only; question stage sees
      exact accepted materials and resolved settings; replacement receives only
      its authorized target plus needed context. No tools, family/operation
      metadata, unresolved defaults, hidden model reviewer or chain-of-thought
      request goes to the provider. Keep schema rules in schemas and shared
      semantic/presentation rules in one prompt fragment.
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
      control or recovery benefit requires reconsideration before migration. A
      passing mock or external STACK case is not evidence of Hebrew improvement.

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
plan/input/content. Release accepts expectedRevision/reviewedRevision and
returns snapshot ID/preview. TaskSnapshot uses existing `/api/instances/{id}`
previews.

- [ ] Add failing tests for creation from an unsaved valid plan with no AI;
      owned-template copy pinned to expectedVersion; owned-snapshot copy with
      cleared review; 404 family boundaries; raw omitted/null/false/zero/empty
      input behavior; source confirmation before generation/publication.
- [ ] Add lenient-save and adoption tests. Missing/invalid answer associations
      produce diagnostics; unbounded/unsafe input fails. Generated material
      edits bump revisions and stale every dependent question. Replace source
      updates copied plan/input atomically, preserves published template,
      resolves again and invalidates dependencies. Plan/settings changes cannot
      keep content silently current; adoption cannot waive strict requirements.
- [ ] Add release tests for counts/formats/keys/strict lengths/staleness/current
      review and active-operation blocking. Preserve advisory targets. Assert
      plan/input/content/keys/policies/provenance and measurements are frozen,
      with unique sourceDraftId and sourceDraftRevision. Duplicate exact release
      returns the same snapshot; stale/different revision conflicts before
      writes. Deleting that snapshot leaves the draft terminal: exact release
      replay returns 410 Gone, never recreates content; deleted draft
      returns 404.
- [ ] Add simultaneous save/release tests proving one revision wins. Released
      drafts reject edits; cloning starts a new draft. Template deletion retains
      independent drafts/snapshots; draft deletion retains its snapshot; family
      reset clears learning records and preserves accounts/configuration.
- [ ] Run failing tests, implement direct DbContext short
      transactions/concurrency tokens and server-owned metadata. Derive ordinary
      diagnostics instead of adding competing persisted readiness flags. Use
      disposable test schema creation while migration remains gated.
- [ ] Run
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

- [ ] Add idempotency tests in exact order: authorized draft lookup, existing
      key comparison, then new-operation revision/admission checks. Replay after
      material acceptance/terminal status returns original operation despite a
      changed current revision. Same key/different original request returns 409;
      cross-family access returns 404. Stored fingerprint binds original
      revision, kind/target/instruction and separately captured effective-input
      hash.
- [ ] Add budget tests for 32 global/four family/one draft, two shared provider
      slots, ten family starts/minute, two steps/operation and 2 MiB evidence.
      The 129th new operation fails safely; viewing, edits, release and existing
      key replay remain available. Explicit clone is unbilled and never
      automatic.
- [ ] Add transactional acceptance tests: candidate + content + draft revision +
      step checkpoint + queued next stage commit together. Queued → calling is
      an atomic claim conditioned on active identity and matching revisions;
      cancel/edit before a successful claim produces no provider call. Material
      strict failure preserves old material and makes zero question calls.
      Question failure retains accepted material; explicit GenerateQuestions
      reuses it. No partial document appears when a checkpoint transaction
      fails.
- [ ] Add deterministic race barriers for edit/Undo/source change/cancel/delete/
      reset before claim and while provider waits. Store late candidates as
      unapplied conflicts, clear active state and stop downstream. Cancellation
      wins locally before transport cancellation, retains terminal cancelled
      status and allows only known usage metadata to arrive later. No provider
      transaction stays open.
- [ ] Add restart fixtures: queued resumes; accepted material + queued questions
      resumes only questions; calling without accepted checkpoint becomes
      unknown and is never replayed. Preserve known response metadata versus
      unknown usage. Repeated GET/poll/reload makes zero starts. Test lost
      start/release responses.
- [ ] Add seven-day artifact expiration tests with an injected clock. Purge at
      most 32 terminal artifacts per pass; preserve
      key/fingerprints/status/known usage for draft lifetime. An expired
      diagnostic is explicit; key replay never restarts work. Draft deletion
      removes tombstones and replay returns 404.
- [ ] Run those failing integration tests, implement the single-process worker
      and short transitions, then rerun until all pass. Document the one-process
      deployment constraint; do not add distributed leases speculatively.

### Task 5: Replace the blueprint editor with plan chat and native controls

**Files:** Modify `Features/Ai/AiEndpoints.cs`, `Client/core/api/models.ts`,
`learning-api.ts`,
`Client/features/templates/template-editor/template-editor.{ts,html,spec.ts}`
and `Client/app.routes.ts`. Create
`Client/features/templates/plan-editor/plan-editor.{ts,html,spec.ts}`,
`template-chat/template-chat.{ts,html,spec.ts}` and `learning-plan.fixture.ts`.
Create
`Client/features/activities/activity-workspace/activity-workspace.{ts,html,spec.ts}`
as the single route state owner from the start; keep TemplateEditor a thin
template-route adapter that supplies initial context. Modify
`Tests/Integration/AiAuthoringTests.cs` and shared settings fields as needed.

**Interfaces:** Author endpoint uses TemplateAuthoringInput/AuthoringReply and
returns app-computed PlanChange[], echoed requestId/baseRevision and metadata.
ActivityWorkspace owns raw plan/input, valid projection, source confirmation,
client revision, baseline, clarification and twenty-entry Undo. TemplateEditor
and presentation children hold no competing draft; they only supply context or
emit events. Saved template publication keeps expectedVersion.

- [ ] Test initial proposal/refinement/one-question clarification, six-turn and
      12,000-character context cap, operative assumptions present in the plan,
      identity preservation and computed removals. Retained fixed source cannot
      change through AI; direct source edits are explicit and confirmed.
- [ ] Test late proposal after typing/invalid input/Undo/cancel/route change
      never applies; identical proposal creates no history. Clarification
      retains its original request then expires on conflicting edits. A clean
      proposal applies locally with visible changes and no mandatory Accept
      page.
- [ ] Test independent template Save: no generation, no activity mutation,
      duplicate success handled once, pending save briefly locks edits, stale
      publication preserves local input. Lost response offers checking the
      library; no automatic publication retry or claimed rollback.
- [ ] Implement app-owned purpose/material/question/requested-choice controls,
      inline source confirmation and meaningful Hebrew change labels. No raw key
      editor or generated prompt textarea. AI-unavailable state allows direct
      editing/publication. Do not expose unrequested passage/story controls.
- [ ] Run `dotnet test --filter FullyQualifiedName~AiAuthoringTests` and
      `npm --prefix frontend test -- --watch=false`; require isolated passes.

### Task 6: Complete editable activity and operation UI

**Files:** Modify task 5's
`Client/features/activities/activity-workspace/activity-workspace.{ts,html,spec.ts}`.
Create `activity-document-editor/activity-document-editor.{ts,html,spec.ts}` and
`generation-status/generation-status.{ts,html,spec.ts}` under that feature.
Extend the existing ActivityWorkspace using PlanEditor/TemplateChat in the same
route. TemplateEditor remains its thin adapter; no draft state moves or
duplicates. Modify API models/LearningApi, app.routes.ts, library files and
`Client/features/instances/instance-preview/instance-preview.{ts,html}`.

**Interfaces:** ActivityWorkspace owns raw local plan/input/content, saved
revision/baseline, client edit revision, operation ID and bounded Undo. API
methods map exactly to tasks 3–4; content editor emits allowed changes/selected
targets, status component polls through its owner and never starts work.

- [ ] Test generate from an unsaved valid plan creates/saves a draft before the
      operation. Required Save failure prevents generate/repair/adopt/release;
      unsaved invalid fields stay visible. Reload resumes the same operation and
      saved draft, without claiming local keystrokes or chat were persisted.
- [ ] Test full document edits, explicit source replacement, answer
      invalidation, strict diagnostics, current-revision adoption and bounded
      content-only Undo. Restoring content creates a new save revision and never
      restores readiness, review/provenance/operation state. No ordinary edit
      uses AI.
- [ ] Test operation success with unsaved local typing: retain local buffer,
      display available server result, require explicit reconcile/reload. A
      stale save preserves local input. Test lost responses, failed/unknown
      stages, expired candidates and explicit paid retry warning.
- [ ] Implement four honest scoped actions, real stage statuses, cancel and
      diagnostic-only candidate viewing. Unparseable candidates cannot be copied
      into live content blindly; only parsed bounded content can be edited.
      ReplaceQuestion changes one whole question; ReplaceMaterial is
      generated-only. No automatic repair or extra approval between normal
      stages.
- [ ] Implement explicit **סימון כמוכנה**, current-revision parent review and
      immutable preview/new-draft editing. Hide unfinished assignment/child
      actions. Show unsaved/saving/saved/error states and navigation warning.
- [ ] Run Angular tests; check keyboard focus/live announcements, RTL mixed
      text, native labels/controls, narrow 360px layout and 200% text without a
      framework. Parent answers render as text and never enter any child-facing
      contract.

### Task 7: Integrate evaluation evidence and comparison

**Files:** Modify `Evaluation/EvaluationReport.cs`, `EvaluationRunner.cs`,
`EvaluationPlan.cs`, `EvaluationComparison.cs`, `EvaluationSummary.cs`,
`EvaluationRunStore.cs`, `EvaluationFiles.cs`, `EvaluationCapture.cs`,
`HebrewJudge.cs`, `cases.json`, `hebrew-review-samples.json`, `wwwroot/app.js`
and `wwwroot/index.html`. Modify existing evaluation tests under
`Tests/TaskEngine`, dashboard integration tests and
`frontend/e2e/evaluation-ui.test.mjs`.

**Interfaces:** A case selects Prompt or InitialPlan, never both. Prompt permits
at most three 4,000-character refinements; fixed-plan trials omit authoring.
Stage records carry role/version/policy, exact effective input/schema + stable
hashes, raw output, candidate acceptance/application state and optional usage.
Skipped stages are explicit. Separate interpretation, generation, replacement
and end-to-end outcomes in format 4; reuse one provider profile and judge.

- [ ] Add fixture tests for author/refine/clarification, fixed-plan generation,
      supplied-source skip, scoped repair and early stop. All real attempts and
      evaluator-only 429 retries consume budget; planned totals sum actual
      applicable stages plus optional judge/calibration, not cases × two.
- [ ] Preserve every failed/unapplied candidate and known partial usage; null
      means unknown. Capture exact input/schema evidence with fingerprints,
      source revisions, actual model/provider and policy tags; no content or
      answers in ordinary logs. Keep parent corrections/time-to-ready evidence.
- [ ] Update comparison tests: matched plans/inputs/sources/settings and
      policies for generator trials, matched edit sequences for authoring.
      Reject incompatible old report formats safely. If judge
      identity/config/prompt/ rubric/coverage or calibration differs, suppress
      judge-quality deltas while retaining independently valid
      deterministic/human comparisons.
- [ ] Reuse TextLength and independent case expectations. Keep
      fixed/range/target meanings, including the historical 100–150 regression;
      authoring that drops a demand fails adherence. Update judge source
      paths/policy version while preserving planted defects and auditing
      disputed Hebrew labels with humans.
- [ ] Run existing evaluation test filters,
      `node --test frontend/e2e/evaluation-ui.test.mjs` and
      `./scripts/evaluate-ai.sh --case all`; require passes and a no-call
      preview. Retain dashboard startup/offline access, Host/Origin/CSRF/CSP,
      confirmation, cancellation and safe artifact loading.

### Task 8: Verify the complete flow and perform the gated cutover

**Files:** Modify `frontend/e2e/ai-provider.mjs`, `parent-workflow.spec.ts`,
`http-boundaries.spec.ts`, `theme.spec.ts` and
`Tests/Integration/MigrationTests.cs`. Generate a new `ContentFirstActivities`
migration under Persistence/Migrations. Update README,
docs/product-specification.md, docs/architecture.md, docs/ui-guide.md and
touched contract comments only when behavior is implemented.

**Delete after callers move:** `Engine/Models/TaskTemplateDefinition.cs`,
`TaskContent.cs`, `Engine/Validation/ParameterValidator.cs`,
`TemplateValidator.cs`, `TaskContentValidator.cs`,
`Engine/Ai/content.schema.json`, `Features/Instances/TaskInstance.cs`, and the
old TaskInput record from TaskSettings. Delete obsolete
`Client/features/templates/ai-template-author/`, `ai-template-form/`,
`Client/features/instances/create-instance/` and `instance-form/` after
transferring valuable tests. Remove `Evaluation/ContentWorkflowPrototype.cs` and
temporary one-shot dispatch after capturing the decision; retain comparison
artifacts and supported stage evidence.

**Interfaces:** One deployed schema-5 content-first lifecycle; no compatibility
reader, dual production path or mutable historical TaskInstance.

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
- [ ] Verify generated migration on disposable schema-4 data: clear learning
      records, preserve identities/accounts/configuration and create fresh new
      records. Keep historical migrations intact. No user's database is migrated
      during tests or while the comparative gate is unmet.
- [ ] Review prototype evidence against the pre-registered rubric/budget and
      record proceed/reconsider. If extra cost bought no useful benefit, stop
      destructive cutover and revise the split. If no live evidence was
      authorized, leave this gate visibly incomplete rather than inventing
      proof.
- [ ] Remove obsolete callers/contracts/UI only after isolated checks cover
      their meaningful boundaries. Update current docs and real fixture-derived
      call examples; remove planned-design banners when behavior actually lands.
- [ ] Run `./scripts/verify.sh`, `npm --prefix frontend run e2e` and
      `git diff --check`; require exit 0. Inspect 360px/200% text/keyboard/RTL,
      ownership, source fidelity, races and artifact limits. Review the diff for
      unrelated changes, credentials, DBs, keys or generated outputs.
- [ ] Only after both gates pass, stop development watchers, take a local backup
      and apply the already-authorized development-learning reset using normal
      migration tooling. Preserve accounts/configuration/keys and report what
      changed. Do not commit, deploy or delete unrelated data.

Child activation, assignment, attempts, scoring and reports require a later
separate vertical-slice plan. This plan prepares an immutable self-contained
snapshot and policy contract, not a claim that child delivery is available.

[design]: ../specs/2026-09-30-structured-templates-design.md
