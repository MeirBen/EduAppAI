# Activity-only slice 2 implementation plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement this
> plan task by task. Track completion with the checkboxes below.

**Goal:** replace the staged/template workspace with the activity canvas and
chat, including saved drafts, explicit creation and approval.

**Architecture:** build on the verified slice-1 engine/API. Keep one workspace
buffer and the existing API client/observer; read/edit views share that buffer,
and chat renders state and emits actions. Remove replaced UI and its exclusive
helpers instead of retaining a second flow.

**Tech stack:** installed Angular 22, standalone components, signals/Signal
Forms, Tailwind 4, Vitest and Playwright; existing .NET 8 API.

**Spec:**
[product lifecycle](../../product-specification.md#activity-lifecycle),
[chat design](../../activity-chat-design.md),
[UI guide](../../ui-guide.md#workspace-actions), and
[slice-1 record](2026-10-08-activity-only-slice-1.md).

## Global constraints

- No Git mutations or live paid AI calls. Any future live evaluation requires an
  agreed budget first; use isolated providers for this slice.
- Coordinate any dev-server interruption before execution, including
  verification that runs `npm ci`. No real data reset in slice 2.
- Hebrew/RTL, native accessible controls, theme tokens, 360px layout and 200%
  text; read limits from the existing server contract.
- Keep server ownership, revision checks, immutable snapshots and child
  answer-key isolation. Do not change engine prompts, operation semantics or
  dependencies.
- No legacy routes/adapters, new workflow framework, second chat store or
  parallel page phases. An edit-mode flag controls presentation only.
- Backend template APIs/storage, evaluation-caller retirement and coordinated
  learning-data cutover remain slice 3. The slices ship together.

## Review focus

- Unconfirmed or changed sources cannot silently become confirmed (task 1).
- Failed/conflicting saves preserve edits and block dependent AI (task 2).
- Removed targets never retarget or send automatically (task 3).
- Lost responses and Stop races reconcile without another AI call (task 3).
- Reload preserves recovery offers; snapshots stay immutable (tasks 2–5).

## Tasks

Paths below are relative to `frontend/src/app/` unless stated otherwise. For
tasks 1–4, add the named behavior checks to the listed specs, run them to
confirm the expected failure, implement the change, then rerun to green:
`npm --prefix frontend test -- --watch=false --include='<spec path relative to frontend>'`.

### 1. Prompt-first setup and an explicit saved checkpoint

**Files:**
`features/activities/activity-workspace/{activity-workspace.ts, activity-workspace.html,workspace-form.ts,activity-workspace.spec.ts}`
and
`features/activities/activity-setup/{activity-setup.ts,activity-setup.html, activity-setup.spec.ts}`.

**Interfaces:** reuse `LearningApi.authorPlan`, `LearningPlan`,
`ImportedChatTurn[]` and `WorkspaceForm`. Authoring owns only unsaved proposals;
the saved draft's chat becomes authoritative after creation.

- [ ] Test `requiresSourceConfirmationBeforeSaving`: missing/changed extracted
      text blocks the checkpoint; confirmed text round-trips exactly. Test
      `savesIncompleteDraftWithoutGeneration`: plan/chat persist, content may be
      incomplete, and saving makes no operation request.
- [ ] Replace editable requirement controls with a concise read-only summary.
      Keep bounded source input/confirmation in `ActivitySetup`;
      requirement/source kind changes use chat. Preserve local request/revision
      correlation, cancellation and the unsaved-navigation guard.
- [ ] Make **שמירת טיוטה** save the valid plan and imported conversation. Make
      **יצירת הפעילות** save first when necessary, then start one `Create`
      operation. Resume at `/activities/:activityId`; pre-content `Revise` does
      not implicitly create content. Test late authoring replies after changed
      input/Stop and a reload between saving and creation.

### 2. Reading canvas, fixed content editing and recovery

**Files:** workspace files above and `activity-lifecycle.spec.ts` beside them;
`features/activities/activity-document-view/{activity-document-view.ts, activity-document-view.html}`;
existing `activity-document-editor`, `activity-review`, `source-replacement` and
their affected specs.

**Interfaces:** `ActivityDocumentView.document: EditableActivity` is a
projection of the same `WorkspaceForm` used by the editor. Keep existing
save/adopt/release methods and the server's diagnostics/measurements as
authorities.

- [ ] Test `failedSaveKeepsEditingAndPreventsAi`: failed/409 saves retain every
      field and edit mode; success returns to reading. Invalid edits prevent AI;
      valid pending edits save before its operation starts.
- [ ] Render the document read-first; **עריכה** opens existing fixed-structure
      fields. Keep answer disclosures, diagnostics/lengths beside content and
      exact confirmed-source replacement. Settings and formats stay read-only.
- [ ] Test `questionRecoveryOfferSurvivesReload`: saved source/text diagnostics
      offer explicit `GenerateQuestions` or validated adoption without AI. Undo
      is unavailable with unsaved edits; retain server rules for
      clearing/consuming it.
- [ ] Keep **אישור הפעילות** explicit against a valid saved revision, with no AI
      or automatic assignment. Test frozen preview → snapshot copy → editable
      draft, preserving the original snapshot and assignments.

### 3. Adjacent activity chat, targeting and durable recovery

**Files:** rename
`features/activities/template-chat/{template-chat.ts, template-chat.html,template-chat.spec.ts}`
to matching `activity-chat` paths; workspace/reader files above;
`features/activities/activity-workspace/ {draft-observer.ts,draft-observer.spec.ts}`
and existing `generation-status`.

**Interfaces:** use `ActivityChatTurn`, `RevisionTarget` and
`StartGeneration.target/sources`. Add opt-in reader input `canAsk: boolean` and
output `asked: RevisionTarget`; snapshot previews leave it off. Workspace
`selectTarget(target: RevisionTarget): void` fills the request and calls
`ActivityChat.focusComposer(): void`; only the existing `sent` event starts
work.

- [ ] Test `targetShortcutOnlyFillsComposer`: each text/question sets the exact
      visible target, fills/focuses chat and sends nothing. Removing a target
      blocks submission until the parent clears/reselects it; never retarget by
      position.
- [ ] Rename/adapt the existing chat to show persisted turns, assumptions and
      committed notices. Pass message, optional target and explicitly confirmed
      added sources through `StartGeneration`; collect label/text in the chat's
      source input before admission. Source replacement stays the confirmed save
      from task 2. Test exact added-source payload and no unconfirmed
      submission.
- [ ] Put chat beside the readable canvas on wide screens and below it on
      phones. Retain suggestions that only fill, Enter/Shift+Enter/IME handling,
      typing status and Send/Stop focus. Preserve the request after
      failure/Stop.
- [ ] Test `unknownStartReusesIdenticalRequest` and
      `stopAfterCompletionShowsCommittedResult`: use the existing observer/key
      recovery; no automatic retry, duplicate turn or partial content
      application. While active, keep content readable and pause
      editing/save/adopt/undo/approval.
- [ ] Test dirty-buffer external save/deletion and chat-only updates: announce
      saved-state changes without discarding local edits. Uncertain
      create/save/undo/ approval responses offer a saved-state check, never
      blind resubmission. Keep truthful status, Stop and technical evidence;
      show the operation-capacity limit without disabling available manual
      save/review.

### 4. Activity-only routes/library and complete UI retirement

**Files:** `app.routes.ts`, `features/activities/activity.routes.ts`,
`features/library/activity-library/{activity-library.ts,activity-library.html, activity-library.spec.ts}`,
`core/api/{learning-api.ts,models.ts}`, `features/auth/parent-shell.html`,
`features/assignments/assignment-list.html`,
`features/instances/snapshot-preview/snapshot-preview.html`, workspace and
affected navigation tests/styles.

**Interfaces:** library `/activities`, new `/activities/new`, draft
`/activities/:activityId`; keep `/instances/:instanceId` for frozen previews.
Simplify `LearningApi.createActivity` to accept `plan: LearningPlan`,
`lifetime: DestroyRef` and optional `chat: ImportedChatTurn[]`, returning
`Promise<ActivityDetail>`; retain `copySnapshot`.

- [ ] Test library drafts/ready loading, independent errors, deletion and reset;
      assert no template request/action. Preserve reset confirmation and family
      SSE refresh. Update every app link and navigation test to `/activities`.
- [ ] Remove template routes, mode/provenance/publication state, library queries
      and frontend template DTO/API methods. Do not add redirects or
      compatibility readers. Keep the default new-activity route.
- [ ] After tasks 1–3 work, delete `plan-editor` UI/`length-fields`,
      `scoped-repair` and their exclusive tests; remove staged text/question
      buttons, four-step progress and old target-card actions. Remove frontend
      callers/types for `GenerateMaterials`, `ReplaceMaterial` and
      `ReplaceQuestion`, along with `ActivityReview.questionsNext` and its
      staged workflow copy.
- [ ] Remove abandoned form/projection helpers, settings controls, styles,
      imports and comments after checking remaining uses. Retain only form
      validation and projections required by the single buffer; do not replace
      them with a new store. Remove superseded tests while preserving useful
      behavioral coverage.

### 5. Verify the complete flow and update implementation docs

**Files:** rename `frontend/e2e/activities/plan-workspace.spec.ts` to
`activity-canvas.spec.ts`; update existing activity lifecycle, parent workflow,
navigation/assignment browser tests and `frontend/e2e/generate-draft.ts`;
`README.md`, `docs/architecture.md`, `docs/ui-guide.md` and this plan.

- [ ] Add isolated browser coverage for prompt/clarification → confirm sources →
      save/reopen → one Create → read/edit → targeted Revise → undo → approve →
      assign/copy. Exercise failure/recovery, keyboard announcements/focus, RTL
      and 360px/200% text. Keep family/child isolation and frozen-content
      regressions.
- [ ] Check running dev processes and coordinate any interruption before
      `scripts/verify.sh`. Then run `scripts/publish.sh` and
      `npm --prefix frontend run e2e` against disposable data/local providers.
      Require all commands to exit 0; fix failures before declaring the slice
      done.
- [ ] Review unused imports/helpers and `git diff --check`; search application
      code for retired template routes/actions and explain any legitimate
      remaining backend/evaluation references reserved for slice 3.
- [ ] Update current implementation/setup docs with the working activity-only
      flow, and replace this checklist with scope/results/remaining cutover
      work. Keep target specs authoritative; do not claim slice-3
      retirement/reset is done.

**Done:** the complete activity-only UI passes isolated verification, obsolete
UI is removed, and only the documented slice-3 cutover remains. No dev-server
interruption, real reset or live spend is implicit in finishing this plan.
