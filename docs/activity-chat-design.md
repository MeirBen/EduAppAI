# Activity chat design

**Status:** target design, 8 October 2026; implemented in slices 1–3. The
[slice-3 record](superpowers/plans/2026-10-09-activity-only-slice-3.md) tracks
verification and the separately coordinated data cutover. The
[product specification](product-specification.md#activity-lifecycle) owns the
activity-only lifecycle and template retirement. This document details the
canvas/chat flow and AI execution. Update current implementation documentation
alongside the code cutover.

## Experience

A ChatGPT-style canvas: the activity is the main, readable document, with chat
beside it on wide screens and below it on phones, in Hebrew and RTL. Show the
activity itself, not code or JSON. The parent describes changes in chat and
reviews the resulting activity without navigating a large form.

- **Read first.** The canvas is read-only by default. An explicit Edit action
  opens titles, instructions, text, questions/options, answers and points in
  the same workspace buffer. A successful manual save returns to the reading view;
  failed saves preserve the edits. Settings remain a read-only summary.
- **Chat owns structure.** Add, remove or reorder texts/questions and change
  settings through chat. Each text/question has an “ask about this” shortcut
  that fills and focuses the composer without sending.
- **First request.** Authoring proposes a plan or asks one clarification.
  Collect required source text and confirm AI-extracted sources, then save the
  draft and conversation. One Create operation produces text and questions.
- **Changes and answers.** Apply a complete validated change at once and report
  what changed. A content question or clarification changes nothing.
- **Manual text changes.** After saving, offer “update questions” or “the
  questions still fit” when needed. Derive this from saved diagnostics so it
  survives reload. Adoption still requires validation; typing calls no AI.
- **While working.** Save pending edits before AI starts, then pause editing.
  Keep the canvas readable, show status and allow Stop. A failed or cancelled
  operation leaves saved content unchanged, including during first creation.
- **Undo and ready.** Offer one-level undo for the last successful chat change.
  Marking ready remains an explicit parent action against the saved revision.
  Released activities remain read-only; editing starts from a snapshot copy.

The library contains drafts and ready activities. Remove template creation,
editing, saving/publication and reuse, including the workspace's template mode.
The activity owns its internal plan. Replace scoped AI boxes, the four-step
progress list, the text-then-questions stop and unapplied-result controls with
this canvas/chat flow. Keep field diagnostics, source input/replacement and
technical evidence accessible.

## Execution

Use the existing worker and AI stages. One planner call chooses a bounded
change; deterministic server code runs a fixed sequence. No agent loop, generic
workflow engine, dependency graph or automatic paid retry.

1. **Admit:** flush valid edits; check ownership, expected revision, operation
   key, optional target and existing rate/queue limits. Allow one active operation
   per unreleased draft. Append the parent turn and start the operation atomically;
   same-key/same-payload replay returns it without another turn or call. A reused
   key with different kind, revision, message, target or sources returns 409.
2. **Plan:** freeze the base plan, document and history. Validate the planner
   response against a detached working copy, assign new IDs on the server and
   derive its effective requirements. Answers, clarifications and no-ops finish
   here: save the reply, keep content and undo.
3. **Scope:** pure `RevisionScope` code merges requirement changes with one-off
   instructions using the table below. The model cannot narrow dependencies.
4. **Execute:** rewrite existing targets in plan order; run ideas, writing and
   polish once for new materials; then generate, append or replace questions. Save
   intermediate results only in operation artifacts. Rewrites get no polish.
5. **Apply:** validate the result, then save plan, document,
   completion notice, undo and terminal operation state in one transaction,
   fenced by draft revision and active-operation identity.

Before a draft contains questions or generated text, Revise updates only its
plan and confirmed supplied sources; Create starts generation. Derive this from
content, without another stored phase. Express the request in the plan; content
edit lists stay empty, question scope is `none` and `questionOrder` is null.
Validate these changes and metadata-only
edits as safe drafts, retaining incomplete-content diagnostics. Content-producing
operations require complete validated output; neither path grants parent approval.

Shared requirements are goal, global guidance, topic, audience and difficulty.
Question requirements include count, formats, choice count and guidance.
Text generation always excludes supplied sources.

| Change                              | Work                               |
| ----------------------------------- | ---------------------------------- |
| Plan name                           | Rename draft/library entry         |
| Material label                      | Rename setting; preserve content   |
| Shared requirements or total length | All generated texts and questions  |
| One material's requirements or edit | That generated text and questions  |
| Add generated material              | Write/polish new text; questions   |
| Remove/reorder materials            | Apply structure; questions         |
| Other question requirements         | All questions                      |
| Add questions, optionally focused   | Generate additions; keep originals |
| One-off question edits              | Replace up to three; else all      |
| Pure question removal/reorder       | Keep listed IDs and their content  |

For removal/reorder, `questionOrder` lists surviving IDs in their desired order;
omitted IDs are removed. Set the plan's count to that list's length. Preserve
survivors' IDs, prompts, options, answers and points. This needs only the planner
call. Require at least one question and coverage of every required format;
clarify conflicts instead of regenerating survivors or changing requirements.
A count-only decrease must provide this list; otherwise clarify before content
calls.

Appending increases the count and may include a one-off instruction for the
additions, such as “add a question about the treasure map.” Request exactly
`newCount - oldCount` new questions, with originals as read-only context.
The server assigns new IDs and appends the validated additions, preserving all
original questions and title/instructions. Reject duplicate prompt/interaction
pairs against originals and within additions; validate count, format coverage
and content size on the combined document. New questions
use allowed formats; the additions alone need not cover every required format.
These preservation paths require current valid content and unchanged other
requirements; other question changes use the full rebuild below.

Compare effective fields by value. **Every question-guidance change rebuilds
questions**; do not guess whether prose changes are cosmetic. Shared changes
also rebuild questions in activities with no generated texts. A full rebuild
absorbs selected replacements while retaining their instructions. Combined
question-content/structure changes rebuild the full set with explicit removal
and ordering instructions; do not claim to preserve unchanged questions there.

Give writing and polish explicit new-material targets; retain existing texts
as read-only context. Validate each intermediate candidate's identity, source
fidelity and field bounds, then validate combined length and complete materials
before questions. A strict total-length change must not fail halfway because
other texts still await rewriting. Validate the finished document before apply;
keep strict checks on each supported standalone stage.

**Slice 1 defers length allocation across calls.** A strict `totalLength` works
for one complete new batch or one rewrite with unchanged siblings. When it
would require multiple rewrites or new writing alongside retained generated
texts, return a clarification before content calls: offer per-text ranges or
an approximate total. Apply that choice only after the parent's reply. Material
removal must also leave the strict total valid or request clarification. Never
silently rewrite extra texts or relax the range; add allocation only if needed.

Rebase acceptance only for previously current content outside the affected
scope, preserving content, revisions and origin after validation. This is not
parent adoption. Never silently accept already stale content. Missing/invalid
dependencies outside scope require an explicit creation, repair or adoption
choice before content calls. Metadata-only changes may retain existing
diagnostics; they cannot clear them or make a draft ready. A supplied-source
replacement follows the same rule for untouched generated texts; only the
questions become stale. Label changes do not change content requirements.

## AI contract and context

The revision planner returns exactly one outcome:

- **Answer or clarification:** a Hebrew reply of at most 600 characters, no
  mutations. Refusals leave the entire plan/document unchanged, including
  old unsupported requirements; no opportunistic cleanup or content calls.
- **Change:** the complete updated plan, existing bounded assumptions and
  one-off edits. Preserve untouched fields; put lasting requirements in the
  plan. The application writes the completion notice from committed changes,
  as statements the chat lists, and displays the assumptions beside it.

Reuse the plan schema definitions. Material edits are `[{ id, instruction }]`.
Question edits are
`{ scope: none | selected | append | all, instruction, items }`, with at most
three selected `{ id, instruction }` items. Item instructions allow
500 characters; append/full-rebuild instructions use `MessageLength`. `none`
has no instruction/items; `selected` has one to three items; `append`/`all`
have no selected items. `append` requires a positive count increase, unchanged
other requirements, no material edits and no `questionOrder`; its optional
instruction applies only to additions, not persistent question guidance.
A nullable `questionOrder` is a nonempty, unique ordered subset of existing IDs
for pure removal/reorder; no separate removal field. It requires question-edit
scope `none` and a proposed count matching its length. Validate combinations,
duplicate IDs and targets against the base and proposed state. Use request-owned
ID enums where applicable.

Instructions must be self-contained after clarification: downstream calls must
not need to interpret “yes” or earlier chat. Plan name and document title are
separate fields. Direct title/instruction requests outside this contract point
to the editor; do not claim unsupported changes. Supplied sources can be
explanation targets but never rewrite targets. Preserve their source kind and
exact text; a transformation creates a separate generated material. Chat never
adds a supplied source: the parent's own texts come from setup, so a request to
add one is refused without a change, and the validator rejects any new one.
Replacing a supplied source uses the explicit source-replacement save, makes
questions stale and offers regeneration/adoption; the model never rewrites it.

### Prompt and schema changes

Adapt the existing [AI prompts](../backend/FamilyLearning.Api/TaskEngine/Ai/AiPrompts.cs),
[schema builders](../backend/FamilyLearning.Api/TaskEngine/Ai/AiSchemas.cs) and
typed stage inputs together. Keep one provider boundary and reuse existing
content schemas.

- **Authoring:** adapt `PlanAuthoring` to concrete activity requirements;
  remove reusable-template, control-builder and future-parameter instructions.
  Keep the plan/clarification envelope, backed by the simplified concrete plan
  schema in the product specification. Share planning constraints with revision;
  keep source confirmation in the application.
- **Revision:** add one activity-revision prompt,
  `activity-revision.schema.json` and typed candidate. Use a nested outcome
  union under an object root. Propose requirements and bounded edits, never
  execution steps or completion claims.
- **Ideas/writing/polish:** replace “all generated materials” instructions with
  explicit new targets; retained texts are read-only context. Reuse output
  shapes; pass only target IDs/counts to `MaterialsFor`. Skip empty target sets.
- **Text rewrite:** reuse the single-material output. Prompt with updated
  requirements and pending/final sibling distinction. The strict-total rule
  with unchanged siblings applies only after the slice-1 scope guard passes.
- **Questions:** full generation receives the rebuild instruction and prior
  questions. Preserve compatible title/instructions and requested question
  content; recompute answers against final materials. Add an append prompt/input
  and candidate using the existing question-item schema in `{ questions: [...] }`.
  Parameterize `QuestionsFor` with the output count: append returns exactly the
  count difference, with no title, instructions or IDs. Reuse per-question
  validation; enforce full count/format coverage on server-assembled content.
  Replacement keeps its existing single-question shape and server-owned ID.

Compose the revision schema from the shared concrete plan definitions; do not fork
the plan schema. Keep required fields, null branches and closed objects aligned
with typed deserialization. Derive bounds from engine constants and retain the
[validated schema forms](ai.md#strict-schema-contract). Use an
empty edit list when no valid targets exist, never an empty ID enum.

Validate outcome exclusivity, lengths, IDs, source fidelity and cross-field
rules on the server in every response mode. Schema acceptance is not semantic
validation. Invalid/truncated output fails the operation without a repair call.
Keep `RequestAsync`, response-format settings and evidence capture; do not add
model-specific paths. Bump `EngineVersions.Revision` for prompt/engine changes
and `SchemaVersion` for the simplified plan shape. The fresh-start cutover needs
no legacy plan parser.

Authoring/revision may change old requirements in response to the latest parent
request. Content-stage prompts instead obey the resulting validated requirements;
one-off instructions cannot override them. Keep that distinction explicit in
shared prompt fragments. Initial unsaved authoring uses request/local-revision
correlation: ignore late replies after Stop, navigation or changed local input.
It saves nothing until the parent creates the draft and never retries itself.

### Call context

Build explicit payloads in `AiGenerationService` from typed stage inputs. Keep
full server-side state for validation; send relevant requirements and content,
without duplicate source bodies. Generation/edit calls share the effective goal,
global guidance, topic, audience and difficulty, plus the context below.
Text/idea calls also receive the question requirements the materials must
support, without generated questions or answer keys.

- **Authoring:** message, optional base plan, the conversation window and
  parent-supplied text → plan or clarification.
- **Revision:** concrete plan, current document including answers,
  message/target and the conversation window with outcomes → one planner outcome.
- **Ideas:** new-material requirements, lengths, retained/supplied
  texts and bounded family idea history → ideas for the new batch.
- **Writing:** material requirements/context, new target IDs, selected idea
  and scoped instructions → exactly those titles/bodies.
- **Polish:** new texts, audience/language rules, length-preservation instruction
  and retained/supplied context → minimal edits to new targets only.
- **Text rewrite:** old target title/body, updated material requirements
  and length, sibling texts and target instruction → one same-ID text.
- **Questions:** question requirements, final validated materials,
  bounded prompt history and rebuild instruction → title, instructions and the
  complete ordered question batch.
- **Question additions:** question requirements, final materials, existing
  title/instructions and prompts/options without keys, bounded prompt history,
  additional count and optional scoped instruction → only new questions.
- **Question replacement:** question requirements, final materials, target
  including answer/options/points, current siblings, learner instructions and
  target instruction → one complete question; the server retains its ID.

Context rules:

- Only authoring/revision receive conversation, and both receive the same
  window: the newest turns, oldest first, up to `MaxContextTurns` and
  `ContextLength`, stopping at the first turn that would exceed the length.
  `ConversationWindow` is that one rule. Before a draft exists, the client sends
  its whole local conversation and authoring applies the window; a saved draft's
  operation captures the window from its stored chat at admission. Older turns
  stay visible but leave the context. Include target/outcome context; failed
  requests are not applied edits. Send the current message once, excluding it
  from history. Current state takes precedence; clarify unresolved references
  rather than guess.
- Only ideas receive family idea history; full question generation and additions
  receive family prompt history. Writing receives only the selected idea.
  Text calls receive no question answers. Keep database identity, credentials,
  provenance, acceptance and operation evidence out of provider payloads.
- Rebuilds receive existing title/instructions and ordered question prompts,
  options and IDs as reference, without old keys. Include absorbed instructions
  so requests such as “keep the first three” retain their meaning. New answers
  follow final materials.
- Later stages use the latest validated working content. Mark sibling texts
  awaiting rewrite as pending context; question calls wait for all materials.
  Freeze history at admission and never reread the live draft for stage inputs.
- Treat source text as data. Trim old conversation/novelty history first; never
  truncate required sources, target context or constraints to fit. Reject an
  oversized required payload against the configured request/schema byte limits
  before calling the provider.

Evaluate the final production payloads; isolated tests do not establish live
model acceptance.

## Implementation boundaries

- **Engine:** concrete plan validation, scope and assembly checks in `TaskEngine`;
  extend existing stage records and explicit AI payload/schema builders.
- **Persistence:** the existing activity endpoints, worker and DbContext own
  admission, checkpoints and commits. Keep transactions outside AI calls.
- **Frontend:** one workspace buffer and existing observer/API client; shared
  chat renders turns and emits actions. A feature-local chat session owns composer,
  targets and unsaved authoring; the workspace owns buffer and durable
  transitions. Read/edit views use the same state.
  Remove template routing, publication/version state and library queries.
- **Scaffolding:** simplify existing contracts/storage for the
  [fresh-start cutover](product-specification.md#activity-only-cutover), adding
  only necessary helpers. No new project, repository/mediator layer, executor
  hierarchy, second chat store or parallel AI stack. Keep comments with behavior.

### Operations and recovery

The activity UI uses `Create` (complete missing content), `Revise`, and
`GenerateQuestions` (the manual-edit offer). Create preserves current existing
texts; stale texts require explicit repair. Extend `GenerationArtifacts` with
working plan, scope and progress cursor. `NextStage` follows the fixed
sequence, not an arbitrary stored queue.

New and rewritten targets are disjoint, at most four combined. Maximum calls:
**eight** for revision (planner + three rewrites + ideas/writing/polish for one
new material + questions); **four** for Create after authoring. With no material
work, at most three question replacements follow the planner.
Raise `GenerationOperationOptions.StepLimit` from **3 to 8**, including the
planner step. Update storage/worker checks and the current fourth-step rejection
test to accept eight and reject nine. Keep the 2 MiB evidence and 16 KiB summary
bounds; test eight bounded summaries and atomic failure on evidence overflow.

Reject manual saves, adoption, undo and release with 409 while active. This is a
behavior change: current saves are allowed to fence generation. Retain revision
and active-operation concurrency checks for races and other tabs. Only applied
plan/content changes advance content revision; chat/status changes do not.
Manual saves validate the permitted content fields and preserve IDs, question
formats and structure; source replacement is an explicit confirmed-source action.

Cancellation commits before transport stops; late output records usage only.
If completion committed first, Stop returns that completed outcome. A network
error proves neither cancellation nor rollback: reread saved operation state.
Compatible queued stages resume from checkpoints; interrupted calling stages
become terminal unknown outcomes. Failure, conflict, cancellation and evidence
overflow apply no working content. Deletion/reset discards late results.
Recovery never retries an uncertain call.
Keep all other queue, evidence and retention bounds and the single-process host.
At `DraftLimit` (128), explain that AI is unavailable while manual save/review
remain available; do not promise an unimplemented draft-copy feature.

### Plan, chat and undo

The activity plan owns concrete settings, formats, counts, lengths and supplied
source text. Derive stage inputs/fingerprints from it; remove `TaskRequest`
override maps and separately saved input JSON. Keep stable retained IDs and
exact supplied text. A successful change saves the plan and document together;
there is no template, default/override reconciliation or legacy normalization.

- `ChatJson`: at most 100 turns with role, text, UTC time, optional target and
  operation reference; assistant turns may include bounded assumptions. A
  notice is stored once as its `changes` statements with empty text; the model's
  context receives them joined, exactly as the earlier single-text notice.
  Parent text uses `MessageLength`; imported authoring replies retain their
  existing 1,000-character limit, revision replies/notices use 600. Drop oldest
  completed exchanges. Reuse operation status and append one terminal reply/notice
  per operation, including recovery and Stop.
- `UndoJson`: pre-change plan/document and the resulting revision for
  the last successful changing Revise or GenerateQuestions. No Create undo.
  Restore only at that revision with no active operation; advance revision,
  consume undo and append a notice. Unsaved edits disable it; manual save,
  adoption or release clears it. Answers, no-ops and failures do not replace it.
- Both fields are parent-only and excluded from snapshots, child DTOs, library
  projections and change notes. Draft deletion/family reset removes them.

### API

Activity routes remain under `/api/activity-drafts`; undo is the new activity
action. Other rows extend existing contracts. Template retirement also renames
authoring and reset routes below; this is not the complete API list.

| Request                   | Purpose and change                    |
| ------------------------- | ------------------------------------- |
| `POST /`                  | Create from plan; accept bounded chat |
| `POST {id}/operations`    | Start AI work; add operation kinds    |
| `POST {id}/undo`          | New: restore the previous content     |
| `POST {id}/adopt-content` | Confirm “questions still fit”; no AI  |
| `GET {id}`                | Load draft; also return chat and undo |

Reuse these separate handlers for creation, queued AI work, restoration,
parent confirmation and reads. Keep existing manual-save and status/cancel
routes and family ownership checks. Initial authoring moves from
`POST /api/ai/template-drafts` to `POST /api/ai/activity-plans`; family reset moves
from `DELETE /api/templates` to `DELETE /api/learning-data`. Migrate callers before
removing template APIs and creation's template ID/version fields. Create/save
accept the concrete plan without a separate input object. Validate imported turns
without accepting client-supplied operation identity. Replay a lost operation
start with the same key; an explicit new attempt gets a new key. Uncertain
creation/undo responses offer a saved-state check, not automatic resubmission.
Client limits belong in `EngineValidation`/`GET limits`; worker policies stay
in `GenerationOperationOptions`. Parent answer keys never enter child DTOs.

## Delivery and verification

1. Engine/API: concrete activity plan/schema, authoring, planner, scope, context
   projections, atomic Create/Revise, new-material-only writing/polish, chat and
   undo. Add new operation kinds alongside current ones during development.
2. Canvas/chat: reading/editing, initial creation and source confirmation,
   targeting, status, undo and manual-edit offer. Remove replaced controls only
   when the complete activity flow works. The library and workspace expose only
   activities/drafts; retire template UI and publication state.
3. Cutover: migrate all UI/evaluation callers before retiring old operation
   kinds and template routes/contracts. Remove template/override persistence,
   perform the coordinated fresh start and update implementation guides.
   Keep queued-stage version/profile guards and the assignment/child contracts.

Acceptance checks (freeze the AI cases before slice-1 implementation):

- Scope table, removal/reorder with survivor equality, append with/without a
  focused instruction, exact addition count, no original edits, duplicate rejection,
  one addition to mixed formats, maximum final count and stale-content rejection.
  Include last-question/last-required-format conflicts and combined changes.
- Concrete-plan fingerprints, zero/empty allowed values, mixed/missing sources,
  supported strict lengths, deferred-scope clarification and eight-step bounds.
- Refusal regression: “add explanations to the answer key,” including a plan
  with an old unsupported explanation requirement. Require a refusal, identical
  saved plan/document and no content calls. Test server rejection of a
  reply carrying edits and evaluate the final prompt against this frozen case.
- Captured provider requests for every call type: required context present,
  unrelated/private fields absent, self-contained follow-ups, latest working
  texts used and absorbed instructions retained. Use the isolated provider.
- Prompt/schema/candidate agreement in strict and prompt-schema modes;
  zero/max targets, duplicate/foreign IDs, invalid outcome combinations,
  unknown fields, oversized/truncated replies and activity-oriented authoring.
- Pre-creation plan edits without generation, source replacement, refused source
  additions,
  rejected model source edits, local authoring correlation and same-key/different-
  payload rejection before calls.
- Outcomes including no-ops, every failure stage, replay, stale targets,
  cancellation/completion races, restart, deletion, evidence/chat limits,
  concurrent tabs, undo, release and family/child/snapshot isolation.
- Template-free create/resume/approve, retired template/control/override fields,
  renamed authoring/reset callers, fresh-database setup and parent-account
  preservation. Later draft edits must leave assigned snapshots/results intact.
- Read/edit switching, failed saves, targeting, reload-persistent offers,
  keyboard/focus/screen readers, RTL and 360px layout at 200% text size.

Run `scripts/verify.sh` for changes; implementation workflow changes also run
publication and the isolated browser suite. Coordinate the full verification
run with the parent: its `npm ci` can interrupt `scripts/dev.sh`/`ng serve`.
For doc-only review while development is active, run installed formatting and
Markdown checks without reinstalling dependencies. Freeze cases and acceptance
criteria before authorized paid evaluation of the final prompts and schemas.
