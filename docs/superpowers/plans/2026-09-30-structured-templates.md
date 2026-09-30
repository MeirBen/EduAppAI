# Structured templates implementation plan

> For implementation: use `superpowers:executing-plans` task by task. Work
> sequentially because contracts, UI and evaluation share the same cutover. Do
> not commit, stage, branch or perform other Git mutations.

**Goal:** Replace the generated-blueprint step with one conversational template
workspace, stable native settings and validated generic task generation.

**Architecture:** The existing TaskEngine owns a typed plan, input resolution
and output assembly. Existing features retain HTTP, ownership and persistence.
The Angular route owns a reversible unsaved draft; AI edits that draft without
publishing it. No new service, framework or deployed compatibility path.

**Tech stack:** .NET 8, EF Core/SQLite, Microsoft.Extensions.AI/OpenRouter,
Angular 22.2 standalone components/Signal Forms, native HTML and Tailwind 4. Use
the existing pinned dependencies and isolated test tools.

**Spec:** [Structured templates and conversational editing][design]. The spec
owns product decisions and exact bounds; this plan owns implementation order.

## Global constraints

- No Git mutations, live paid calls, production deployment or data deletion
  while merely preparing this plan. Implementation must preserve unrelated work.
- Read README, architecture, UI and commenting guides before implementing.
  Update contracts' XML docs/JSDoc with their behavior.
- No separate blueprint screen or extra Accept button. Valid AI changes apply to
  the unsaved draft with actual changes and Undo; Save publishes explicitly.
  Confirming chat-extracted source text is an inline source-specific check.
- Retain one backend project, direct DbContext, immutable published revisions
  and task snapshots, and server-enforced family ownership.
- No subject-specific generators, model-generated UI/code, new agent framework,
  translation/proofreading loop, production retry loop or schema-4 adapter.
- Preserve provider registration/configuration, capacity, deadlines,
  cancellation, safe ProblemDetails, CSRF, evaluation isolation and bounded
  paid-call controls.
- Schema 5; authoring v23; generation v20; Hebrew review v9; report format 4;
  automatic checks 9; resolution/assembly policy 1; word-measurement version 1.
  Advance if semantics change.
- Respect all section 3 limits in the design. No universal question-count
  ceiling of 20, hidden word-count tolerance or promise of deterministic Hebrew
  quality.
- Use ResolvedTaskRequest as the single generation contract. No second compiler
  model, fixed question-slot scheduler, typed language framework or changed
  answer representation is required for this cutover.

## Review focus

These cross-boundary failures need explicit regression coverage:

1. A late proposal after typing, Undo or cancellation must never apply (task 5).
2. False, zero, omitted, null and empty inputs must remain distinct (tasks 2/4).
3. Accepted bilingual source strings must survive generation unchanged,
   including punctuation and newlines (tasks 2–5). Do not promise external-file
   byte preservation through browser controls.
4. A task form opened before publication must return 409 before a paid call;
   deletion during a call must still prevent persistence (task 4).
5. A successful clarification or unmapped refinement must not inflate evaluation
   passes or escape the call budget (task 6).

## File and interface map

Paths below use these exact repository-relative roots:

- `Engine`: `backend/FamilyLearning.Api/TaskEngine`
- `Features`: `backend/FamilyLearning.Api/Features`
- `Persistence`: `backend/FamilyLearning.Api/Infrastructure/Persistence`
- `Client`: `frontend/src/app`
- `Tests`: `tests/FamilyLearning.Api.Tests`
- `Evaluation`: `tools/FamilyLearning.Evaluation`

Keep small related records in their owning model file. Pure helpers below are
static functions, not injected service layers or interfaces with one
implementer. Use field-path error dictionaries consistently with existing
validation.

- `Engine/Models/LearningPlan.cs`: plan, materials, question requirements,
  numeric choices, custom controls and length expectations.
- `Engine/Models/TaskRequest.cs`: task input and resolved requirements. Retain
  shared `TaskSettings` in its existing file; remove its old TaskInput record.
- `Engine/Models/TaskDocument.cs`: generated materials/questions, saved snapshot
  and length measurements. Reuse existing answer/interaction semantics.
- `Engine/Models/TemplateAuthoring.cs`: authoring input/reply and PlanChange.
- `Engine/Validation/LearningPlanValidator.cs`: common plan invariants.
- `Engine/Validation/TaskDocumentValidator.cs`: generated/assembled output
  invariants, replacing TaskContentValidator at cutover.
- `Engine/TaskRequestResolver.cs`: defaults, applicability and selected values.
- `Engine/TaskAssembly.cs`: generated-material matching and source insertion.
- `Engine/TextLength.cs`: one versioned body-word measurement convention.
- `Engine/PlanChanges.cs`: identity normalization and deterministic comparison.
- Existing `Engine/Ai/*`: prompts, provider schemas and two AI operations.
- Existing feature endpoints: authorization, version checks and JSON snapshots.
- Existing template-editor route: workspace state. New `plan-editor` and
  `template-chat` presentation components replace old author/form components.
- Existing instance routes/forms and evaluation executable evolve in place.

Canonical interfaces to use across tasks:

```csharp
Dictionary<string, string[]> LearningPlanValidator.Validate(LearningPlan? plan);
TaskResolution TaskRequestResolver.Resolve(LearningPlan plan, TaskRequest input);
int TextLength.CountWords(string text);
LengthMeasurement[] TextLength.Measure(
    ResolvedTaskRequest request, TaskDocument document);
TaskSnapshot TaskAssembly.Assemble(
    ResolvedTaskRequest request, TaskDocument generated);
LearningPlan PlanChanges.AssignNewIds(
    LearningPlan proposal, LearningPlan? previous);
PlanChange[] PlanChanges.Compare(LearningPlan? previous, LearningPlan current);
Task<AiResult<AuthoringReply>> AiGenerationService.AuthorAsync(
    TemplateAuthoringInput input, CancellationToken ct);
Task<AiResult<TaskSnapshot>> AiGenerationService.GenerateAsync(
    ResolvedTaskRequest request, CancellationToken ct);
```

TaskResolution contains either `Value: ResolvedTaskRequest` or validation
`Errors`, never a partial successful request. ResolvedTaskRequest contains goal,
guidance, chosen settings, resolved materials, resolved question requirements,
selected scoped controls and optional total length. It contains no competing
defaults, adjustable flags, unresolved IDs/values, transcript or family
identity. Its expected generated IDs and structural rules drive schema, payload,
validation, assembly and measurement without rereading LearningPlan. Record
policyVersion 1 in the resolved input; it is evidence, not a user choice.

AssignNewIds returns a copy, rejects unknown non-null, wrong-category or
duplicate IDs with ArgumentException, and never mutates the submitted base.
AuthorAsync maps that failure to an application-authored invalid-output error
without exposing the exception message. Assemble rejects invalid output with the
existing safe AiGenerationException; it performs no persistence. Both direct
callers and AI tests exercise these rules.

AuthoringReply contains kind, definition, question and assumptions with exactly
one outcome. AiResult retains the existing metadata wrapper. PlanChange contains
kind, an internal field path and before/after values; the client translates
paths through app-owned labels and current/former material/control labels. Never
render internal paths as parent-facing text. Only plain strings appear in the
UI.

## Delivery sequence

Tasks 1–2 add independently testable domain units; task 3 proves direct editing
and publication state with isolated HTTP fixtures before connecting chat. Tasks
4–6 are a coordinated integration, not intermediate releases. Update affected
consumers in the same working change when a signature changes; do not add legacy
overloads to keep an unfinished cutover deployable. Task 7 is the
release-readiness gate.

### Task 1: Define the typed plan and its invariants

**Files:** Create the four model files and LearningPlanValidator from the map.
Create `Tests/TaskEngine/LearningPlanTests.cs`; modify shared
`Engine/Validation/TaskSettingsValidator.cs` only if required for reuse.

**Interfaces:** Produce LearningPlan and its validator. The design's section 3
is the authoritative field/invariant list, including new-null/existing-stable
IDs, explicit custom choices and material ownership.

- [ ] Add failing tests for a no-material numerical quiz, reading with one
      generated material, supplied bilingual text, a mixed-format task and a
      per-task-selectable format. Valid plans must have an empty error
      dictionary.
- [ ] Define reviewed canonical examples for reading, math, mixed formats and
      supplied bilingual source. Keep stable synthetic IDs and reuse these
      examples for pure tests and the generation-only evaluation cases in
      task 6.
- [ ] Add rejection tests for duplicate IDs across scopes, supplied-source
      length requirements, total plus material length, invalid format/default
      combinations, input bounds on fixed choices, invalid defaults, unknown
      fields and one-past-limit strings/arrays. An omitted irrelevant property
      and an allowed null must follow the chosen schema representation
      consistently.
- [ ] Run `dotnet test --filter FullyQualifiedName~LearningPlanTests`; confirm
      the new assertions fail before implementation.
- [ ] Implement immutable-facing records and focused validation. Defensively
      copy arrays/dictionaries when transferring mutable draft data. Reuse
      settings checks; do not parse semantic prose to infer length, subject or
      parameter bindings.
- [ ] Run the same test filter; require all tests to pass. Review errors against
      the native editor's field paths and the design's complete resource bounds.

### Task 2: Resolve requests and assemble measurable snapshots

**Files:** Create TaskRequestResolver, TaskAssembly, TextLength and PlanChanges
from the map. Create `Tests/TaskEngine/TaskRequestResolutionTests.cs`,
`TaskAssemblyTests.cs`, `TextLengthTests.cs` and `PlanChangesTests.cs`. Create
`Engine/Validation/TaskDocumentValidator.cs` using the existing content
invariants. Remove the old validator when the generation consumer moves; do not
leave two supported output contracts after integration.

**Interfaces:** Produce TaskResolution, ResolvedTaskRequest, TaskSnapshot and
the pure helper signatures in the map. TaskSnapshot stores content,
lengthMeasurements and measurementVersion 1.

- [ ] Add resolver assertions that omission uses a default, explicit false/zero
      survive, optional empty text stays empty, required blank text fails, an
      invalid option fails, and unknown/inapplicable/fixed overrides fail before
      generation. Assert explicit null and numeric strings fail. Test a stale
      choice-count override on a selected non-choice format rather than silently
      discarding it.
- [ ] Add assertions for one selected format versus a fixed mixture,
      insufficient mixed question count, allowed question bounds and arbitrary
      feasible positive counts above 20. Selected choice count is absent from
      non-choice generation input.
- [ ] Add assembly tests for reordered, duplicate, missing and foreign material
      IDs. Restore plan order; copy supplied strings unchanged. Enforce the
      final 8,000-character content budget including supplied text, answer keys
      and titles. Reject supplied-source IDs in generated output; test accepted
      whitespace, quotes, niqqud and supplementary Unicode unchanged.
- [ ] Add measurement tests: `שלום עולם` → 2, `שלום — עולם` → 2, `בעלי־חיים` →
      1, `don't` → 1, standalone emoji → 0. Cover niqqud, newlines and
      supplementary Unicode letters. Titles/questions never count; total scope
      sums generated bodies only. Target satisfaction is null, exact and range
      are booleans, and unmet length does not reject valid content.
- [ ] Add identity/change tests for initial null IDs, retained IDs on rename or
      scope move, rejected unknown/duplicate/wrong-category IDs, no label-based
      identity recovery, no input mutation, removed controls, reordered
      materials and identical-plan no-op. Comparison order must be stable.
- [ ] Add preflight tests for four 4,000-character sources, content's minimum
      required title/question/material text, int.MaxValue counts and checked
      derived arithmetic. Reject impossible inputs before count-sized allocation
      or provider use; retain distinct educational bounds and operational
      limits.
- [ ] Assert repeated resolution/assembly with identical inputs yields identical
      requirements/content/measurements. Exclude request IDs, generated object
      IDs and timestamps from claims about deterministic business behavior.
- [ ] Run `dotnet test --filter FullyQualifiedName~TaskEngine`; confirm new
      tests fail, implement the helpers, then run again and require a pass.
      Existing tests may keep old contract coverage until the coordinated
      integration removes it.

### Task 3: Prove the direct editor and publication state without AI

**Files:** Modify `Client/features/templates/template-editor/*`,
`Client/core/api/models.ts`, `learning-api.ts`; create
`Client/features/templates/plan-editor/plan-editor.{ts,html,spec.ts}` and
`Client/features/templates/learning-plan.fixture.ts`.

**Interfaces:** The route owns raw edits, canonical valid projection,
draftRevision, last saved baseline, publishedVersion, Undo and
source-verification state. PlanEditor emits edits; LearningApi owns HTTP. Use
isolated responses until task 4 connects the new backend contract; do not add a
fake production mode.

- [ ] Add failing tests that direct controls and Undo issue zero AI calls, Undo
      to saved content clears dirty state despite a newer draftRevision, and
      invalid edits remain visible and count as unsaved changes.
- [ ] Add save tests: pending publication disables editing, authoring and Undo;
      success updates baseline/version once; failure preserves draft; a lost
      response offers checking the library without claiming rollback or
      retrying.
- [ ] Add source tests: direct input establishes the accepted string; an
      extracted candidate is visibly unverified and cannot be published until
      confirmed or directly edited. Undo restores text and verification state
      together.
- [ ] Implement compact native sections and the bounded twenty-entry Undo
      history. Coalesce typing but advance draftRevision on every edit. Keep one
      state owner and dirty comparison against content, not a revision counter.
- [ ] Implement no-key parent controls, source confirmation inline and explicit
      Save, preserving validation/focus and unsaved navigation protection. An
      unavailable AI provider must not disable ordinary editing or saving.
- [ ] Run `npm --prefix frontend test -- --watch=false`; require isolated
      direct-control/publication tests to pass before adding chat in task 5.

### Task 4: Connect AI schemas, API and persistence

**Files:** Modify `Engine/Ai/AiGenerationService.cs`, `AiPrompts.cs`,
`AiSchemas.cs`, `template.schema.json`, `content.schema.json`. Modify
`Engine/Ai/AiGenerationOptions.cs` for named request/schema byte safeguards and
the corresponding configuration/registration checks. Also modify
`Tests/Fixtures/AiFixtures.cs`, `Tests/TaskEngine/AiDiagnosticsTests.cs`,
`AiCapacityTests.cs` and `Tests/Integration/OpenRouterConfigurationTests.cs`.
Update immediate API/evaluation consumers here and in task 6; no old overloads.

**Interfaces:** Replace AuthorAsync/GenerateAsync with the map's signatures.
AiSchemas supplies one authoring reply schema containing LearningPlan and
`ContentFor(ResolvedTaskRequest request)` for generated-only output.

- [ ] Add fake-provider tests capturing the actual outbound JSON. Assert schema
      5, authoring v23 and generation v20, preserved structural constraints, no
      tools, no family/revision/request metadata and no dropped SDK schema
      bounds. Test concurrent requests with different counts/formats/material
      IDs against the actual outbound HTTP schema after SDK conversion.
- [ ] Assert resolved generation input includes selected custom meanings and
      supplied sources, but no defaults, old conversation or placeholder
      references. A no-material math request must neither request passages nor
      length controls.
- [ ] Assert a new authoring proposal receives app IDs, a refinement preserves
      existing IDs, clarification makes no generation call, and invalid
      proposals cannot reach publication. Refusal/truncation/provider error
      remain safe failures.
- [ ] Reject conversational replacement of a retained fixed source or its source
      kind against the base. Preserve direct source edits submitted as a new
      base. Verify removals and additions appear in computed changes; never
      infer identity from matching text or labels.
- [ ] Implement authoring prompt priorities: preserve the current plan except
      requested edits; add choices only when requested; use typed requirements;
      ask one clarification for unsupported or materially ambiguous requests.
      Keep schema syntax in schemas and shared Hebrew/presentation rules in one
      prompt fragment. Operative assumptions must enter the plan, not only
      notes. Do not duplicate engine rules into every plan's guidance or promise
      that whole-plan chat preserves unrelated fields solely through
      instructions.
- [ ] Implement generation from resolved input through the unchanged
      RequestAsync transport. Validate generated structure, assemble supplied
      originals, validate final content and attach deterministic measurements.
      Clone request schemas; concurrent generations must not share mutable
      constraints.
- [ ] Exercise maximum-size resolved inputs, schemas and authoring envelopes
      with isolated wire fixtures. Record concrete
      MaxRequestBytes/MaxSchemaBytes defaults in options/configuration, test
      each boundary and one byte above, and enforce them before dispatch.
      Include schema duplication in the prompt and response format; do not guess
      token capacity from character counts.
- [ ] Run the affected fake-provider tests after the consumer cutover compiles.
      Require passes for both schema-enforced and JSON-only configured modes. No
      model or sampling change and no live request is part of this task.

#### API and persistence integration

**Files:** Modify `Features/Ai/AiEndpoints.cs`,
`Features/Templates/TemplateContracts.cs`, `TemplateEndpoints.cs`,
`Features/Instances/InstanceContracts.cs`, `InstanceEndpoints.cs`; modify
`Client/core/api/models.ts`, `learning-api.ts`. Add a tool-generated
`StructuredLearningPlans` migration under `Persistence/Migrations`. Update
`Tests/Integration/AiAuthoringTests.cs`, `ParentWorkflowTests.cs`,
`RequestValidationTests.cs`, `TaskSettingsTests.cs`, `LibraryDeletionTests.cs`,
`MigrationTests.cs` and affected fixture consumers.

**Interfaces:** Keep existing routes. AuthorTemplateRequest contains message,
baseDefinition, baseRevision, requestId and unresolved context.
AuthorTemplateResponse contains reply, changes, metadata and server-echoed
requestId/baseRevision. CreateInstanceRequest contains ExpectedVersion and
Input: TaskRequest. InstancePreview exposes ResolvedTaskRequest and
TaskSnapshot. TemplateDetail and CreateVersionRequest use LearningPlan.

- [ ] Add endpoint tests for request/context limits, malformed base plans, safe
      clarification, forged identities, unauthorized families and full
      validation on manual publication. Provider failure must not leak raw
      bodies/configuration. Exercise raw JSON omission/null/numeric-string cases
      before typed deserialization loses presence. Keep the request reader local
      to TaskRequest; .NET 8 nullable annotations do not enforce this policy.
- [ ] Add a stale-form test: publish revision 2, submit version-1 task settings,
      assert 409 and zero provider requests. Keep existing publication and
      deletion race tests; reading a saved task performs zero provider requests.
- [ ] Add a separate race test: start generation on revision 1, publish revision
      2 during the call, then assert the task saves against revision 1. Reset or
      deletion winning before persistence must still prevent that save.
- [ ] Add a persistence test: source text and measurement version remain
      identical after reload and a later template publication. Initial
      generation stores the resolved selections, assembled snapshot and
      generation metadata atomically.
- [ ] Run failing integration tests, then implement request validation before
      AI, one owned revision read, pure request resolution and snapshot
      persistence. Keep DB transactions closed during remote calls and existing
      deletion handling.
- [ ] Generate the migration with the existing EF tooling. Test upgrading a
      disposable schema-4 database: learning rows are cleared, identity/account
      data survives, and fresh creation works. Preserve historical migration
      files; a destructive data reset is not a reversible reconstruction of
      learning content.
- [ ] Update all client/API fixture DTOs and direct consumers, then run
      `dotnet test --filter FullyQualifiedName~Integration` and Angular
      type/build checks. Do not run the reset migration against the user's
      working database until implementation is verified and cutover
      prerequisites are satisfied.

### Task 5: Add chat to the workspace and complete task previews

**Files:** Modify `Client/features/templates/template-editor/*` and routes. Use
task 3's plan-editor and create `template-chat/template-chat.{ts,html,spec.ts}`
under that same templates feature. Replace the old ai-template-author and
ai-template-form directories. Modify
`Client/features/instances/instance-form/*`, `create-instance/*`,
`instance-preview/*`; retain shared task-settings and loading components. Move
relevant fixtures to `Client/features/templates/learning-plan.fixture.ts`.

**Interfaces:** TemplateEditor owns working draft, valid plan projection,
revision, pending request ID, published version, clarification context and Undo.
Extend task 3's state; do not create another authoritative draft in chat.
PlanEditor receives the editable plan and emits direct edits with app-owned IDs;
TemplateChat receives messages/status and emits send/cancel. Neither child calls
AI or saves a template. LearningApi remains the HTTP owner.

- [ ] Replace old screen tests with a test that sends the initial request, sees
      settings inside the same route, refines them, sees actual changes, undoes
      and saves. Assert no blueprint prompt textarea, field-key editor or Accept
      step for ordinary proposals. Keep the inline source-specific check.
- [ ] Add tests for stale response after typing, Undo, route change and explicit
      cancellation; an unchanged proposal adds no history. Invalid direct edits
      block AI/save while preserving text; request failure preserves the
      message.
- [ ] Assert Save is blocked during authoring, and authoring/edits/Undo are
      blocked during Save. Test that a save success is never discarded through
      the AI stale-reply guard. Preserve an unapplied stale proposal or discard
      it safely, but never auto-merge it.
- [ ] Add tests for a clarification followed by an answer: retain the original
      unresolved request, clear it after resolution, invalidate it after
      conflicting edits, and show consolidation guidance when the context cap
      would be exceeded.
- [ ] Apply valid proposals atomically to task 3's route-owned draft with actual
      changes and Undo. Guard every response by request identity and revision;
      keep assumptions visible but outside authoritative plan content. Newly
      extracted sources enter the unverified state from task 3.
- [ ] Add no section-scoped AI edit actions in this release. Whole-plan chat
      exposes every computed change; direct controls own exact local changes.
      Restrict exact/range length settings to explicit advanced choices and show
      only controls relevant to the plan or selected format.
- [ ] Implement unsaved navigation protection, ordinary loading/cancel states,
      focused Hebrew errors and a polite live announcement of applied changes.
      Direct edits remain possible during a call; save remains disabled then.
- [ ] Update task form resolution inputs and version envelope. Show requested
      and actual lengths beside the affected saved material or total, and a
      prominent unmet exact/range notice. Preserve plain-text/bidi-safe answer
      rendering. Derive needs-review/warnings from snapshots, with no new
      Approved lifecycle or generic educational-pass indicator.
- [ ] Run `npm --prefix frontend test -- --watch=false`; require new interaction
      tests and remaining tests to pass. No tests may depend on a live provider.

### Task 6: Align the existing evaluator with authoring and refinements

**Files:** Modify `Evaluation/EvaluationReport.cs`, `EvaluationRunner.cs`,
`EvaluationPlan.cs`, `EvaluationComparison.cs`, `EvaluationSummary.cs`,
`EvaluationRunStore.cs`, `EvaluationFiles.cs`, `HebrewJudge.cs`, `cases.json`,
`hebrew-review-samples.json` and affected `wwwroot/app.js`/`index.html` views.
Update evaluation tests under `Tests/TaskEngine`, dashboard integration tests
and `frontend/e2e/evaluation-ui.test.mjs`.

**Interfaces:** EvaluationCase accepts either Prompt or InitialPlan, never both.
Prompt cases permit an optional ordered Refinements array (at most three
messages, each at most 4,000 characters). InitialPlan cases validate a canonical
plan and fixed inputs before generation, with no authoring/refinement calls.
Embed the four canonical examples from task 1 in cases.json and reuse them in
isolated tests; do not create production seed templates or another fixture
store. EvaluationResult adds refinement steps and final normalized plan/snapshot
while retaining raw per-call output. Scenario input selectors use material
position or unique visible control label, resolved to app IDs before
TaskRequestResolver. Ambiguous/missing selectors fail clearly; never hardcode
random generated IDs.

- [ ] Add fixture-provider tests for author → edit → generate, clarification
      without generation, context-preserving refinement, unmapped selections,
      failed edit checkpoint and budget exhaustion before the next call.
      Complete scenario requests expect proposals; unexpected clarifications are
      adherence failures.
- [ ] Add generation-only tests asserting one provider request without a judge,
      identical resolved inputs across repetitions and early rejection of
      invalid fixed plans. Make completion/summary logic understand
      intentionally absent authoring, rather than treating it as an unfinished
      run.
- [ ] Change planned base calls to the sum, per repeated case, of one authoring
      call + refinement count + one generation call + optional judge; add
      calibration calls once. InitialPlan cases use one generation call plus an
      optional judge per repeat. All actual retry attempts consume the existing
      hard budget and retain their operation/sequence identity. Dry runs display
      the new totals.
- [ ] Update report reading/comparison for format 4/checks 9. Comparison rejects
      unequal refinement sequences or input selectors, and unsupported old
      formats produce a safe actionable message. Do not auto-convert saved
      reports. Compare fixed-plan identity/content, effective input/schema
      fingerprints and policy versions for generation-only trials; do not
      require newly authored plans to be identical in end-to-end authoring
      trials.
- [ ] Capture effective request/schema alongside SHA-256 fingerprints from
      stable serialization, raw output, normalized plan and assembled snapshot.
      Record actual model/provider when available and cost coverage; never
      synthesize missing evidence or log sensitive content in ordinary
      application logs.
- [ ] Add comparison tests where generation and judge change together: suppress
      Hebrew-finding deltas unless judge configuration, prompt, model identity,
      review coverage and passing calibrations match. Preserve independently
      comparable structural results. No second judge provider is added.
- [ ] Reuse TextLength for observed body counts. Keep case expectations
      independent of model-authored requirements: if a request asks for 350
      words and the model drops that requirement, evaluation must still report
      the missing requirement. Distinguish structural success, adherence, length
      observations and Hebrew review.
- [ ] Preserve the historical 100–150-word range failure as a range regression,
      not a target relabeling. Verify short output still fails adherence while
      structurally valid drafts keep their warning policy. Start a new baseline
      under the changed check/measurement versions.
- [ ] Update source paths/context in Hebrew review v9 and calibration fixtures,
      preserving intentional defects. Inspect plan labels/meanings/guidance and
      task prose separately; do not treat technical identifiers as misspelled
      Hebrew. Have a human verify disputed calibration labels; a clean judge
      response or failing calibration must never become proof of language
      quality.
- [ ] Refine the existing scenario set instead of adding near-duplicates. Cover
      reading with adjustable target, fixed range, combined two-material length,
      no-passage math, logic, objective short answers, selectable formats,
      supplied bilingual source and one explicit custom choice added by
      refinement.
- [ ] Run isolated evaluation tests, the dashboard command below and
      `./scripts/evaluate-ai.sh --case all`. Require passing tests and a no-call
      dry run. Retain dev.sh/dashboard startup, offline access,
      loopback/Host/Origin/CSRF/CSP protections, confirmation, cancellation and
      JSON artifact safety unchanged.

```bash
node --test frontend/e2e/evaluation-ui.test.mjs
```

### Task 7: Remove the old path and verify the complete experience

**Files:** Remove `Engine/Models/TaskTemplateDefinition.cs` and
`TaskContent.cs`, old TaskInput, ParameterValidator, TemplateValidator and
TaskContentValidator after their callers move. Remove obsolete author/form UI
and tests after preserving meaningful regressions. Update README,
`docs/product-specification.md`, `docs/architecture.md`, `docs/ui-guide.md`, and
touched XML/JSDoc. Modify `frontend/e2e/ai-provider.mjs`,
`parent-workflow.spec.ts`, `http-boundaries.spec.ts` and any affected theme
scenario.

**Interfaces:** One schema-5 path from authoring through evaluation and saved
preview. Existing public capabilities outside this cutover remain functional.

- [ ] Search production code, fixtures and current docs for old parameter keys,
      prose bindings, schema 4, contentBlocks, independent word counters and
      blueprint editor references. Remove replaced code; keep relevant
      historical migrations. Remove dead exports, unused styles and duplicated
      prompt rules, not valuable boundary tests merely to reduce line counts.
- [ ] Add isolated browser workflows for reading → chat refinement → Undo → save
      → task; no-passage math; supplied source preservation; a stale form; and
      an invalid/truncated response. Assert saved tasks survive later template
      edits.
- [ ] Check the workspace at 360px, enlarged text, RTL with English content and
      keyboard-only operation. Check focus, live status, cancellation, unsaved
      navigation and unavailable-AI editing. Capture only current artifact
      images.
- [ ] Update current product/architecture/UI docs to describe implemented
      behavior; remove their pending-design banner and stale prompt/key
      instructions. Correct evaluation call-count examples from the new fixture
      set; do not handwave costs.
- [ ] Run `./scripts/verify.sh`, then `npm --prefix frontend run e2e` and
      `git diff --check`. Require exit 0 for every command. Inspect the diff for
      unrelated edits, credentials, build outputs and compatibility scaffolding.
- [ ] After verification, stop development watchers, take a local backup and
      apply the explicit development-learning reset with normal migration
      tooling. Report the reset accurately. Do not commit, deploy or delete
      unrelated data.

The design review itself authorizes no migration or destructive command. Apply
the existing development-data reset decision only as part of the verified
implementation cutover; never edit already-applied migration history.

## Evidence and next decision

Passing isolated tests establishes contract, workflow and persistence behavior;
it does not establish model quality. When the user requests a live evaluation,
first show the selected scenarios, repeats and maximum billable calls. Run the
same bounded suite on the chosen provider profile, then review adherence,
natural Hebrew, semantic correctness, clarification rate, latency and cost.

Use that evidence to tune model/profile or prompts. Do not add a repair loop,
extra model or hidden normalization because mocked tests passed. Child-device
activation, assignments and grading remain the next independent product slice.

[design]: ../specs/2026-09-30-structured-templates-design.md
