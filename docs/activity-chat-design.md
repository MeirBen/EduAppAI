# Activity chat design

**Status:** revised design, 8 October 2026; not implemented. This replaces the
activity workspace's separate AI controls with one activity canvas and one chat.
The [product specification](product-specification.md#current-workflow),
[architecture](architecture.md#ai-and-persistence), [AI guide](ai.md) and
[UI guide](ui-guide.md) change alongside implementation.

## Experience

A ChatGPT-style canvas: the activity is the main, readable document, with chat
beside it on wide screens and below it on phones, in Hebrew and RTL. Show the
activity itself, not code or JSON. The parent describes changes in chat and
reviews the resulting activity without navigating a large form.

- **Read first.** The canvas is read-only by default. An explicit Edit action
  opens titles, instructions, text, questions, answers and points in the same
  workspace buffer. A successful manual save returns to the reading view;
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

Templates keep settings-only authoring and local chat/undo. Activities replace
scoped AI boxes, the four-step progress list, the text-then-questions stop and
unapplied-result controls with this canvas/chat flow. Keep field diagnostics,
source input/replacement and technical evidence accessible.

## Execution

Use the existing worker and AI stages. One planner call chooses a bounded
change; deterministic server code runs a fixed sequence. No agent loop, generic
workflow engine, dependency graph or automatic paid retry.

1. **Admit:** flush valid edits; check ownership, expected revision, operation
   key, optional target and existing rate/queue limits. Allow one active operation
   per unreleased draft. Append the parent turn and start the operation atomically;
   key replay returns it without another turn or call.
2. **Plan:** freeze the base plan, input, document and history. Normalize a
   detached working copy, validate the planner response with
   `PlanChanges.AssignNewIds` and resolve its effective requirements. Answers,
   clarifications and no-ops finish here: save the reply, keep content and undo.
3. **Scope:** pure `RevisionScope` code merges requirement changes with one-off
   instructions using the table below. The model cannot narrow dependencies.
4. **Execute:** rewrite existing targets in plan order; run ideas, writing and
   polish once for new materials; then generate or replace questions. Save
   intermediate results only in operation artifacts. Rewrites get no polish.
5. **Apply:** validate the complete result, then save plan, input, document,
   completion notice, undo and terminal operation state in one transaction,
   fenced by draft revision and active-operation identity.

Shared requirements are goal, global guidance/controls, topic, audience and
difficulty. Question requirements include count, formats, choice count,
guidance and controls. Text generation always excludes supplied sources.

| Change                              | Work                              |
| ----------------------------------- | --------------------------------- |
| Plan name                           | Rename draft/library entry        |
| Shared requirements or total length | All generated texts and questions |
| One material's requirements or edit | That generated text and questions |
| Add generated material              | Write/polish new text; questions  |
| Remove/reorder materials            | Apply structure; questions        |
| Question requirements               | All questions                     |
| One-off question edits              | Replace up to three; else all     |
| Pure question reorder               | Reorder IDs; preserve content     |

Compare effective fields by value. **Every question-guidance change rebuilds
questions**; do not guess whether prose changes are cosmetic. Shared changes
also rebuild questions in activities with no generated texts. A full rebuild
absorbs selected replacements while retaining their instructions. Combined
question-content/order changes rebuild the full set with the ordering request.

Give writing and polish explicit new-material targets; today they process the
whole generated set. Validate each intermediate candidate's identity, source
fidelity and field bounds, then validate combined length and complete materials
before questions. A strict total-length change must not fail halfway because
other texts still await rewriting. Validate the finished document before apply;
keep existing standalone-path checks during migration.

For partial work under `totalLength`, derive temporary length budgets from
preserved/finalized texts, reserving room for pending targets. Do not count
pending old bodies as final or persist these budgets as plan requirements.
Reject an impossible remaining budget before the next call; never silently
rewrite extra texts or relax the parent's range.

Rebase acceptance only for previously current content outside the affected
scope, preserving content, revisions and origin after validation. This is not
parent adoption. Never silently accept already stale content. Missing/invalid
dependencies outside scope require an explicit creation, repair or adoption
choice before content calls. Metadata-only changes may retain existing
diagnostics; they cannot clear them or make a draft ready.

## AI contract and context

The revision planner returns exactly one outcome:

- **Answer or clarification:** a Hebrew reply of at most 600 characters, no
  mutations. Refusals must not rewrite unrelated old requirements.
- **Change:** the complete updated plan, existing bounded assumptions and
  one-off edits. Preserve untouched fields; put lasting requirements in the
  plan. The application writes the completion notice from committed changes
  and displays the assumptions beside it.

Reuse the plan schema definitions. Material edits are `[{ id, instruction }]`.
Question edits are `{ scope: none | selected | all, instruction, items }`, with
at most three selected `{ id, instruction }` items. Item instructions allow
500 characters; a full-rebuild instruction uses `MessageLength`. `none` has no
instruction/items; `selected` has one to three items; `all` has no selected items.
A nullable `questionOrder` is a complete, unique list of existing IDs for a pure
reorder only. Validate combinations, duplicate IDs and targets against the base
and proposed state. Use request-owned ID enums where applicable.

Instructions must be self-contained after clarification: downstream calls must
not need to interpret “yes” or earlier chat. Plan name and document title are
separate fields. Direct title/instruction requests outside this contract point
to the editor; do not claim unsupported changes. Supplied sources can be
explanation targets but never rewrite targets. Preserve retained source kinds
and exact text, including per-activity sources; a transformation creates a
separate generated material. New supplied sources require explicit input and
confirmation.

### Prompt and schema changes

Adapt the existing [AI prompts](../backend/FamilyLearning.Api/TaskEngine/Ai/AiPrompts.cs),
[schema builders](../backend/FamilyLearning.Api/TaskEngine/Ai/AiSchemas.cs) and
typed stage inputs together. Keep one provider boundary and reuse existing
content schemas.

- **Authoring:** keep `PlanAuthoring` and `template.schema.json` for
  plan/clarification. Share applicable planning constraints with revision;
  keep source confirmation in the application.
- **Revision:** add one activity-revision prompt,
  `activity-revision.schema.json` and typed candidate. Use a nested outcome
  union under an object root. Propose requirements and bounded edits, never
  execution steps or completion claims.
- **Ideas/writing/polish:** replace “all generated materials” instructions with
  explicit new targets; retained texts are read-only context. Reuse output
  shapes; pass only target IDs/counts to `MaterialsFor`. Skip empty target sets.
- **Text rewrite:** reuse the single-material output. Prompt with updated
  requirements, temporary length budget and pending/final sibling distinction;
  remove the assumption that every sibling stays unchanged.
- **Questions:** full generation receives the rebuild instruction and prior
  questions. Preserve compatible title/instructions and requested question
  content; recompute answers against final materials. Replacement keeps its
  existing single-question shape and server-owned ID.

Compose the revision schema from the existing plan definitions; do not fork
the plan schema. Keep required fields, null branches and closed objects aligned
with typed deserialization. Derive bounds from engine constants and retain the
[existing schema compatibility rules](ai.md#strict-schema-contract). Use an
empty edit list when no valid targets exist, never an empty ID enum.

Validate outcome exclusivity, lengths, IDs, source fidelity and cross-field
rules on the server in every response mode. Schema acceptance is not semantic
validation. Invalid/truncated output fails the operation without a repair call.
Keep `RequestAsync`, response-format settings and evidence capture; do not add
model-specific paths. Bump `EngineVersions.Revision` for changed prompts and
provider contracts; this alone does not change the saved plan's schema version.

### Call context

Build explicit payloads in `AiGenerationService` from typed stage inputs. Keep
full server-side state for validation; send relevant requirements and content,
without duplicate source bodies. Generation/edit calls share the effective goal,
global guidance/controls, topic, audience and difficulty, plus the context below.
Text/idea calls also receive the question requirements the materials must
support, without generated questions or answer keys.

- **Authoring:** message, optional base plan, bounded unresolved turns and
  parent-supplied text → plan or clarification.
- **Revision:** normalized plan, current document including answers,
  message/target and bounded recent turns with outcomes → one planner outcome.
- **Ideas:** new-material definitions/controls, lengths, retained/supplied
  texts and bounded family idea history → ideas for the new batch.
- **Writing:** material requirements/context, new target IDs, selected idea
  and scoped instructions → exactly those titles/bodies.
- **Polish:** new texts, audience/language rules, length-preservation instruction
  and retained/supplied context → minimal edits to new targets only.
- **Text rewrite:** old target title/body, updated material requirements/controls
  and length, sibling texts and target instruction → one same-ID text.
- **Questions:** question requirements/controls, final validated materials,
  bounded prompt history and rebuild instruction → title, instructions and the
  complete ordered question batch.
- **Question replacement:** question requirements, final materials, target
  including answer/options/points, current siblings, learner instructions and
  target instruction → one complete question; the server retains its ID.

Context rules:

- Only authoring/revision receive conversation, bounded by `MaxContextTurns`
  and `ContextLength`. Include target/outcome context; failed requests are not
  applied edits. Send the current message once, excluding it from history.
  Current state takes precedence; clarify unresolved references rather than guess.
- Only ideas receive family idea history; only full question generation receives
  family prompt history. Writing receives the selected idea, not rejected ones.
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

These are required adaptations to existing payloads, not claims that current
stage methods already support them. Evaluate the final production form.

## Implementation boundaries

- **Engine:** pure normalization, scope and assembly checks in `TaskEngine`;
  extend existing stage records and explicit AI payload/schema builders.
- **Persistence:** the existing activity endpoints, worker and DbContext own
  admission, checkpoints and commits. Keep transactions outside AI calls.
- **Frontend:** one workspace buffer and existing observer/API client; shared
  chat renders turns and emits actions. Read/edit views use the same state.
- **Scaffolding:** add only necessary contracts/helpers and an additive storage
  migration. No new project, repository/mediator layer, executor hierarchy,
  second chat store or parallel AI stack. Keep comments with their behavior.

### Operations and recovery

The activity UI uses `Create` (complete missing content), `Revise`, and
`GenerateQuestions` (the manual-edit offer). Create preserves current existing
texts; stale texts require explicit repair. Extend `GenerationArtifacts` with
working plan/input, scope and progress cursor. `NextStage` follows the fixed
sequence, not an arbitrary stored queue.

New and rewritten targets are disjoint, at most four combined. Maximum calls:
**eight** for revision (planner + three rewrites + ideas/writing/polish for one
new material + questions); **four** for Create after authoring. With no material
work, at most three question replacements follow the planner.

Reject manual saves, adoption, undo and release with 409 while active. This is a
behavior change: current saves are allowed to fence generation. Retain revision
and active-operation concurrency checks for races and other tabs. Only applied
plan/content changes advance content revision; chat/status changes do not.

Cancellation commits before transport stops; late output records usage only.
If completion committed first, Stop returns that completed outcome. A network
error proves neither cancellation nor rollback: reread saved operation state.
Compatible queued stages resume from checkpoints; interrupted calling stages
become terminal unknown outcomes. Failure, conflict, cancellation and evidence
overflow apply no working content. Deletion/reset discards late results.
Recovery never retries an uncertain call.
Keep existing queue, evidence and retention bounds and the single-process host.
At `DraftLimit` (128), explain that AI is unavailable while manual save/review
remain available; do not promise an unimplemented draft-copy feature.

### Plan, chat and undo

Fold chosen settings, formats, counts, lengths and controls into the activity's
own plan. Rebuild `TaskRequest.Settings` and clear folded overrides after every
proposed plan change. Preserve retained source input, IDs, control order and
false/zero/empty values; remove input for deleted items. Never change the template.
Existing drafts normalize in the working copy and persist only on a successful
change. Require equal resolved values and fingerprints for normalization alone.

- `ChatJson`: at most 100 turns with role, text, UTC time, optional target and
  operation reference; assistant turns may include bounded assumptions.
  Parent text uses `MessageLength`; imported authoring replies retain their
  existing 1,000-character limit, revision replies/notices use 600. Drop oldest
  completed exchanges. Reuse operation status and append one terminal reply/notice
  per operation, including recovery and Stop.
- `UndoJson`: pre-change plan/input/document and the resulting revision for
  the last successful changing Revise or GenerateQuestions. No Create undo.
  Restore only at that revision with no active operation; advance revision,
  consume undo and append a notice. Unsaved edits disable it; manual save,
  adoption or release clears it. Answers, no-ops and failures do not replace it.
- Both fields are parent-only and excluded from snapshots, child DTOs, library
  projections and change notes. Draft deletion/family reset removes them.

### API

Routes remain under `/api/activity-drafts`. **Only undo is a new endpoint.**
The other rows extend existing contracts; this is not the complete API list.

| Request                   | Purpose and change                     |
| ------------------------- | -------------------------------------- |
| `POST /`                  | Create draft; also accept bounded chat |
| `POST {id}/operations`    | Start AI work; add operation kinds     |
| `POST {id}/undo`          | New: restore the previous content      |
| `POST {id}/adopt-content` | Confirm “questions still fit”; no AI   |
| `GET {id}`                | Load draft; also return chat and undo  |

Reuse these separate handlers for creation, queued AI work, restoration,
parent confirmation and reads. Keep existing manual-save and status/cancel
routes, template authoring and family ownership checks. Validate imported turns
without accepting client-supplied operation identity. Replay a lost operation
start with the same key; an explicit new attempt gets a new key. Uncertain
creation/undo responses offer a saved-state check, not automatic resubmission.
Client limits belong in `EngineValidation`/`GET limits`; worker policies stay
in `GenerationOperationOptions`. Parent answer keys never enter child DTOs.

## Delivery and verification

1. Engine/API: planner, scope, context projections, atomic Create/Revise,
   new-material-only writing/polish, normalization, chat and undo. Add new
   operation kinds alongside current ones.
2. Canvas/chat: reading/editing, initial creation and source confirmation,
   targeting, status, undo and manual-edit offer. Remove replaced controls only
   when the complete activity flow works; preserve the template workflow.
3. Cutover: migrate all UI/evaluation callers before retiring old operation
   kinds; update the four guides. Old stored operations remain readable, with
   the compatibility guard preventing incompatible queued execution.

Acceptance checks:

- Scope table, pure/combined reorder, normalization fingerprints, false/zero/
  empty values, mixed/missing sources, shared strict lengths and call bounds.
- Captured provider requests for every call type: required context present,
  unrelated/private fields absent, self-contained follow-ups, latest working
  texts used and absorbed instructions retained. Use the isolated provider.
- Prompt/schema/candidate agreement in strict and prompt-schema modes;
  zero/max targets, duplicate/foreign IDs, invalid outcome combinations,
  unknown fields, oversized/truncated replies and unchanged authoring contracts.
- Outcomes including no-ops, every failure stage, replay, stale targets,
  cancellation/completion races, restart, deletion, evidence/chat limits,
  concurrent tabs, undo, release and family/child/snapshot isolation.
- Read/edit switching, failed saves, targeting, reload-persistent offers,
  keyboard/focus/screen readers, RTL and 360px layout at 200% text size.

Run `scripts/verify.sh` for changes; implementation workflow changes also run
publication and the isolated browser suite. Paid evaluation requires a budget;
freeze cases and acceptance criteria before evaluating new prompts or schemas.

## Evidence

The original prototype used three activities, each with one generated text and
existing questions: 20 planner calls, $0.2598664, all passing its registered
validation and assistant-reviewed intent checks. This established a useful
starting contract, not execution correctness or general reliability. Retained
records: `artifacts/evaluations/revise-planner-2026-10-08/`.

The review added four probes for **$0.0810189 of the authorized $0.30**, with no
unknown costs:

- Both vocabulary-focus requests changed question guidance but returned
  `scope: none`. The old rule would retain old questions despite promising a
  change. Hence question-guidance changes always rebuild questions.
- A supplied-source fixture with no generated-material targets was accepted
  with the original schema and with an alternative empty-target schema, once
  each. No special provider workaround was justified by this sample.

Records: `artifacts/evaluations/activity-chat-review-2026-10-08-jp61rjew/`.
The review also passed 56 existing race, recovery, resolution and assembly tests;
these check the current baseline, not this unimplemented design. The final
planner/context contracts still need evaluation. End-to-end latency and cost
remain unmeasured; preserve old evidence rather than rewriting it to fit changes.
