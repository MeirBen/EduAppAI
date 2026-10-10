# Activity chat design

How the activity canvas and its chat run AI work. The
[product specification](product-specification.md#activity-lifecycle) owns the
lifecycle and contracts; [architecture](architecture.md#durable-generation)
owns the worker and persistence.

## Experience

A ChatGPT-style canvas: the activity is the main, readable document, with chat
beside it on wide screens and a sheet over it on phones, in Hebrew and RTL. It
shows the activity itself, never code or JSON, and the parent changes it
through chat rather than a large form.

- **Read first.** The canvas is read-only by default. Edit opens titles,
  instructions, texts, questions/options, answers and points in the same
  buffer; edits save themselves and finishing a valid edit returns to reading.
  Settings stay a read-only summary.
- **Chat owns structure.** Adding, removing or reordering texts/questions and
  changing settings go through chat. Each text and question has an “ask about
  this” shortcut that fills and focuses the composer without sending.
- **First request.** Authoring proposes a plan or asks one clarification;
  required source text is collected and AI-extracted sources confirmed, then
  the draft and its conversation save themselves. One Create operation produces
  texts and questions.
- **Changes and answers.** A complete validated change applies at once and
  reports what changed; an answer or clarification changes nothing.
- **Manual text changes.** After a saved text edit, “update questions” or “the
  questions still fit” is offered from saved diagnostics, so it survives
  reload. Adoption still validates; typing calls no AI.
- **While working.** Pending edits save before AI starts, then editing pauses;
  the canvas stays readable with status and Stop. A failed or cancelled
  operation leaves saved content unchanged, including during first creation.
- **Undo and ready.** One-level undo covers the last successful chat change.
  Marking ready is an explicit action on the saved revision; ready activities
  are read-only, and editing one starts from a snapshot copy.

## Execution

The worker runs fixed stages: one planner call chooses a bounded change and
deterministic server code runs the rest. There is no agent loop, workflow
engine, dependency graph or automatic paid retry.

1. **Admit:** flush valid edits; check ownership, expected revision, operation
   key, optional target and rate/queue limits, with one active operation per
   unreleased draft. Append the parent turn and start the operation atomically.
   Same-key, same-payload replay returns it without another turn or call; a
   reused key with a different kind, revision, message, target or sources
   returns 409.
2. **Plan:** freeze the base plan, document and history. Validate the planner
   response against a detached working copy, assign new IDs on the server and
   derive effective requirements. Answers, clarifications and no-ops finish
   here, saving the reply and keeping content and undo.
3. **Scope:** `RevisionScope` merges requirement changes with one-off
   instructions using the table below; the model cannot narrow dependencies.
4. **Execute:** rewrite existing targets in plan order; run ideas, writing and
   polish once for new materials; then generate, append or replace questions.
   Intermediate results stay in operation artifacts, and rewrites get no
   polish.
5. **Apply:** validate the result, then save plan, document, completion notice,
   undo and terminal operation state in one transaction, fenced by draft
   revision and active-operation identity.

Before a draft has questions or generated text, Revise updates only its plan and
confirmed supplied sources, and Create starts generation; this follows from the
content, with no stored phase. Such changes express the request in the plan:
content edit lists stay empty, question scope is `none`, and `questionOrder` and
`document` are null. These changes and metadata-only edits validate as safe
drafts that keep incomplete-content diagnostics; content-producing operations
require complete validated output, and neither grants approval.

Shared requirements are goal, global guidance, topic, audience and difficulty.
Question requirements are count, formats, choice count and guidance. Length
measurement excludes supplied sources.

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
| Title or learner instructions       | Replace that text; keep the rest   |
| Supplied-source replacement         | Stale every generated text and all |
|                                     | questions until rewritten/adopted  |

For removal/reorder, `questionOrder` lists the surviving IDs in their new order;
omitted IDs are removed and the plan's count equals the list's length.
Survivors keep their IDs, prompts, options, answers and points, and only the
planner runs. At least one question and every required format must remain,
otherwise the planner clarifies; a count-only decrease must name the list.

Appending raises the count and may carry a one-off instruction for the
additions, such as “add a question about the treasure map.” It requests exactly
`newCount - oldCount` questions with the originals as read-only context; the
server assigns IDs and appends them, keeping every original question and the
title/instructions. Duplicate prompt/interaction pairs are rejected, and count,
format coverage and content size are checked on the combined document; the
additions alone need not cover every format. These preservation paths need
current valid content and unchanged other requirements; anything else rebuilds.

Effective fields compare by value: **every question-guidance change rebuilds
the questions**, and shared changes also rebuild questions when there are no
generated texts. A full rebuild absorbs selected replacements with their
instructions, and combined content/structure changes rebuild the whole set with
explicit removal and ordering instructions.

Writing and polish get explicit new-material targets, with existing texts as
read-only context. Each intermediate candidate's identity, source fidelity and
field bounds are validated, then combined length and complete materials before
questions, and the finished document before apply. A strict `totalLength` works
for one complete new batch or one rewrite beside unchanged texts; when it would
need several rewrites, or new writing beside retained generated texts, or a
removal that breaks it, the planner offers per-text ranges or an approximate
total before any content call. No extra text is rewritten and no range relaxed
silently.

Acceptance is rebased only for previously current content outside the affected
scope, keeping content, revisions and origin; this is not parent adoption, and
stale content is never accepted silently. Missing or invalid dependencies
outside scope need explicit creation, repair or adoption before content calls.
Metadata-only changes may keep diagnostics but never clear them or make a draft
ready. A supplied-source replacement stales every generated text because a
generated text reads the sources without recording which one it derives from.

## AI contract and context

The revision planner returns exactly one outcome:

- **Answer or clarification:** a Hebrew reply of at most 600 characters and no
  mutations. Refusals leave plan and document unchanged, including old
  unsupported requirements, with no cleanup or content calls.
- **Change:** the complete updated plan, bounded assumptions and one-off edits.
  Untouched fields are preserved and lasting requirements go in the plan. The
  application writes the completion notice from the committed changes, as
  statements the chat lists beside the assumptions.

Material edits are `[{ id, instruction }]`. Question edits are
`{ scope: none | selected | append | all, instruction, items }` with at most
three selected `{ id, instruction }` items; item instructions allow 500
characters and append/rebuild instructions `MessageLength`. `none` has no
instruction or items, `selected` one to three items, `append` and `all` none.
`append` needs a positive count increase, unchanged other requirements, no
material edits and no `questionOrder`, and its instruction applies only to the
additions. A nullable `questionOrder` is a nonempty, unique, ordered subset of
existing IDs for pure removal/reorder, with scope `none` and a matching count.
Combinations, duplicates and targets are validated against base and proposed
state, using request-owned ID enums.

A nullable `document` holds `{ title, instructions }`: the complete new text for
a field the parent asked to change and null for one that stays. It needs
existing content; each field is bounded at validation, and the document the
whole change produces passes the manual-edit checks, including the total content
limit, so a change that also removes questions gains their room. It applies
after every stage of its operation, so a rebuild in the same change keeps it;
like a manual edit, a later rebuild may rewrite it.

Instructions are self-contained after clarification: later calls never need to
interpret “yes” or earlier chat. Supplied sources can be explanation targets but
never rewrite targets; they keep their source kind and exact text, and a
transformation creates a separate generated material. Chat never adds a supplied
source: such a request is refused without a change, and the validator rejects
any new one. A supplied source changes only through the explicit
source-replacement save.

### Prompts and schemas

[`AiPrompts`](../backend/FamilyLearning.Api/TaskEngine/Ai/AiPrompts.cs),
[`AiSchemas`](../backend/FamilyLearning.Api/TaskEngine/Ai/AiSchemas.cs) and the
typed stage inputs change together behind one provider boundary.

- **Authoring** returns a plan or a clarification over the concrete plan schema;
  source confirmation stays in the application.
- **Revision** returns one nested outcome under an object root, composed from
  the same plan definitions; it proposes requirements and bounded edits, never
  execution steps or completion claims.
- **Ideas, writing and polish** name explicit new targets; retained texts are
  read-only context, and empty target sets make no call.
- **Text rewrite** returns one same-ID text from the updated requirements, with
  pending and final siblings distinguished.
- **Questions** receive the rebuild instruction and prior questions and
  recompute answers against final materials. **Additions** return exactly the
  count difference in `{ questions: [...] }`, with no title, instructions or
  IDs. **Replacement** returns one question whose ID the server keeps.

Schemas keep required fields, null branches and closed objects aligned with
typed deserialization, derive bounds from engine constants and follow the
[strict schema contract](ai.md#strict-schema-contract); an empty edit list
replaces an empty ID enum. The server validates outcome exclusivity, lengths,
IDs, source fidelity and cross-field rules in every response mode, since schema
acceptance is not semantic validation. Invalid or truncated output fails the
operation without a repair call, and no code path depends on the model.

Authoring and revision may change old requirements in response to the latest
request; content stages obey the resulting validated requirements, which one-off
instructions cannot override. Initial unsaved authoring correlates each request
with the local revision, ignores late replies after Stop, navigation or changed
input, saves nothing until the draft exists and never retries itself.

### Call context

`AiGenerationService` builds explicit payloads from typed stage inputs. Every
generation or edit call gets the effective goal, global guidance, topic,
audience and difficulty; text and idea calls also get the question requirements
the materials must support, never questions or answer keys.

- **Authoring:** message, optional base plan, the conversation window and
  parent-supplied text → plan or clarification.
- **Revision:** concrete plan, current document including answers,
  message/target and the conversation window with outcomes → one outcome.
- **Ideas:** new-material requirements, lengths, retained/supplied texts and
  bounded family idea history → ideas for the new batch.
- **Writing:** material requirements and context, new target IDs, the selected
  idea and scoped instructions → exactly those titles and bodies.
- **Polish:** new texts, audience/language rules, length-preservation
  instruction and retained/supplied context → minimal edits to new targets.
- **Text rewrite:** old title/body, updated material requirements and length,
  sibling texts and instruction → one same-ID text.
- **Questions:** question requirements, final materials, bounded prompt history
  and rebuild instruction → title, instructions and the ordered batch.
- **Question additions:** question requirements, final materials, existing
  title/instructions and prompts/options without keys, bounded prompt history,
  the additional count and an optional instruction → only new questions.
- **Question replacement:** question requirements, final materials, the target
  with answer/options/points, current siblings, learner instructions and the
  instruction → one complete question.

Context rules:

- Only authoring and revision receive conversation, through one rule,
  `ConversationWindow`: the newest turns, oldest first, up to `MaxContextTurns`
  and `ContextLength`, stopping at the first turn that would exceed the length.
  Before a draft exists the client sends its whole local conversation and
  authoring applies the window; a saved draft's operation captures it from the
  stored chat at admission. The current message is sent once, outside history;
  target and outcome context is included, and failed requests are not edits.
  Current state outranks history, and unresolved references are clarified.
- Only ideas receive family idea history, and full question generation and
  additions family prompt history; writing receives only the selected idea.
  Database identity, credentials, provenance, acceptance and operation evidence
  never reach the provider.
- Rebuilds receive the existing title/instructions and ordered question prompts,
  options and IDs without old keys, plus absorbed instructions, so requests such
  as “keep the first three” keep their meaning.
- Later stages use the latest validated working content, siblings awaiting a
  rewrite are marked pending, and question calls wait for all materials.
  History freezes at admission; stages never reread the live draft.
- Source text is data. Old conversation and novelty history are trimmed first;
  required sources, targets and constraints never are, and an oversized required
  payload is rejected against the request/schema byte limits before the call.

## Operations and recovery

The activity uses `Create` (complete missing content), `Revise` and
`GenerateQuestions` (the manual-edit offer). Create keeps current texts; stale
texts need explicit repair. `GenerationArtifacts` carries the working plan,
scope and progress, and `NextStage` follows the fixed sequence rather than a
stored queue.

New and rewritten targets are disjoint and at most four together. A revision
makes at most **eight** calls (planner, three rewrites, then ideas, writing and
polish for one new material, then questions) and Create at most **four**; with
no material work, at most three question replacements follow the planner.
`GenerationOperationOptions.StepLimit` is eight, within 2 MiB of evidence and a
16 KiB summary budget.

Saves, adoption, undo and release return 409 while an operation is active;
revision and active-operation checks cover races and other tabs. Only applied
plan/content changes advance the revision. Manual saves keep IDs, formats and
structure.

Cancellation commits before transport stops, and late output records usage
only; if completion committed first, Stop returns that outcome. A network error
proves neither cancellation nor rollback, so the client rereads saved state.
Compatible queued stages resume from checkpoints, and interrupted calls become
terminal unknown outcomes that are never retried. Failure, conflict,
cancellation and evidence overflow apply no working content, and deletion or
reset discards late results. At `DraftLimit` (128 operations per draft), AI is
unavailable while manual save and review keep working.

## Plan, chat and undo

The plan owns concrete settings, formats, counts, lengths and supplied source
text; stage inputs and fingerprints derive from it. A successful change saves
plan and document together.

- `ChatJson`: at most 100 turns with role, text, UTC time, optional target and
  operation reference; assistant turns may carry bounded assumptions. A notice
  is stored once as its `changes` statements with empty text, and the model's
  context receives them joined. Parent text uses `MessageLength`, imported
  authoring replies 1,000 characters, revision replies 600. The oldest
  completed exchanges drop first, and each operation appends one terminal reply
  or notice, including recovery and Stop.
- `UndoJson`: the pre-change plan/document and the resulting revision of the
  last successful changing Revise or GenerateQuestions, never Create. Undo works
  only at that revision with no active operation; it advances the revision,
  consumes the checkpoint and appends a notice. Unsaved edits disable it;
  manual save, adoption and release clear it; answers, no-ops and failures keep
  it.
- Both are parent-only: snapshots, child DTOs, library lists and change notes
  exclude them, and draft deletion or family reset removes them.

## API

Activity routes live under `/api/activity-drafts`; the full list is in
[architecture](architecture.md#ai-and-persistence).

| Request                   | Purpose                                     |
| ------------------------- | ------------------------------------------- |
| `POST /`                  | Create from a plan with bounded chat        |
| `POST {id}/operations`    | Start Create, Revise or GenerateQuestions   |
| `POST {id}/undo`          | Restore the previous content                |
| `POST {id}/adopt-content` | Confirm “questions still fit”, without AI   |
| `GET {id}`                | Read the draft with its chat and undo state |

Create and save accept the concrete plan with no separate input object.
Imported turns are validated without accepting client-supplied operation
identity. A lost operation start replays with the same key, while an explicit
new attempt gets a new key; uncertain creation or undo responses offer a
saved-state check, never automatic resubmission. Client limits come from
`EngineValidation` through `GET /api/limits`, and worker policies from
`GenerationOperationOptions`.

## Verification

Tests use the isolated provider and capture provider requests for every call
type. They cover the scope table, removal/reorder survivor equality, appends
with exact counts and duplicate rejection, last-question and last-format
conflicts, strict-length clarification and the eight-step bound; refusals with
identical saved state and no content calls, including “add explanations to the
answer key”; prompt/schema/candidate agreement in strict and prompt-schema
modes; pre-creation plan edits, source replacement and refused source
additions; and outcomes across failures, replay, cancellation races, restart,
deletion, limits, concurrent tabs, undo, release and family/child/snapshot
isolation. Browser tests cover reading/editing, targeting, reload-persistent
offers, keyboard and screen readers, RTL and 360px at 200% text. Live prompt
changes follow the [AI guide](ai.md#verification-and-future-changes).
