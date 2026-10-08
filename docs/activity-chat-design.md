# Activity chat design

**Status:** design for owner review, 8 October 2026. Not implemented. It
replaces the scattered AI entry points of the activity workspace with one editor
and one persistent chat. Read with the
[product specification](product-specification.md#current-workflow),
[architecture](architecture.md#ai-and-persistence), [AI guide](ai.md) and
[UI guide](ui-guide.md); those documents change in the same delivery slices.

## Why

**Core idea:** the chat builds the activity with the parent from first request
to ready, so the parent never has to find their way around a large form. The
parent says what they want; the chat keeps settings, texts and questions
consistent; the editor is for reading, checking and small hand edits.

Today each AI entry point changes one layer: the plan chat changes settings,
"שיפור בעזרת AI" under a text rewrites that text inside the old settings, and
the box under a question replaces one question. Layers drift: settings describe
one activity, the text another, and questions were built on an older text. The
parent then meets stale markers, "adopt" choices and regeneration buttons.

## Owner decisions

1. One editor and one chat per activity, from the first request to release.
2. The chat keeps the settings (the plan) in sync; the parent sees a read-only
   summary and never edits settings fields inside an activity.
3. A chat request applies at once to every affected part, and the reply says
   what changed. Undo reverts the last chat change until the parent edits by
   hand.
4. The first request creates the whole activity: settings, text and questions.
5. After a manual text edit, the chat offers to update the questions or keep
   them; no AI call runs without that click.
6. The chat history is saved with the draft.
7. Approach: the chat plans the change and the server runs the existing,
   measured stages (plan-then-execute), not one whole-activity rewrite.

## Experience

One page per activity: the editor (title, instructions, texts, questions,
answers and points, plus a read-only settings summary) and the chat panel beside
it on wide screens, below it on narrow ones, in RTL.

- **First request.** The parent describes the activity. Authoring proposes the
  plan or asks one clarifying question in the chat. Once a plan exists, the
  draft is created with the conversation so far, and one operation writes the
  text and questions.
- **Chat change.** "Make the story about pirates", "question 3 is too hard",
  "6 questions instead of 10": the chat replies in one or two Hebrew sentences
  and the activity updates as a whole. Each text and question has an "ask about
  this" shortcut that starts a message aimed at that item.
- **Questions about content.** "Why is this the answer?" gets a reply and no
  change.
- **Manual edit.** Editing text or questions by hand stays in the editor. If a
  text edit leaves questions out of date, the chat offers "update the questions"
  or "the questions still fit".
- **Undo.** One button reverts the last chat change while the activity is
  unchanged since; after a manual edit the chat explains why undo is gone.
- **Ready.** Marking the activity ready stays an explicit parent action with the
  existing release checks.

Templates keep their settings-only workspace and synchronous authoring; they use
the same chat component with a local, unsaved thread.

## Architecture

### Principles

- **Plan, then execute.** One structured-output call decides; deterministic
  server code executes with the existing stage calls. No tool-calling loop: the
  steps are predictable, so a workflow is cheaper to bound, test and make
  idempotent than an agent.
- **State is the truth.** The planner reads the current plan and content plus a
  short window of recent turns, never the whole transcript.
- **The server owns dependencies.** Which content a settings change affects is
  computed from the plan diff, not trusted to the model.
- **All or nothing.** An operation works on a copy and applies it in one save.
  If it fails, it applies its accepted work only when nothing would be stale;
  otherwise nothing changes. Settings and content never conflict.
- **Reuse.** Ideas, writing, polish, questions, single-text rewrite and
  single-question replacement keep their prompts, schemas and validators.
- **Framework-native.** `IChatClient` structured output (strict
  `json_schema`), EF Core with additive migrations, minimal-API endpoints with
  ProblemDetails, standalone signal-based Angular with native controls.

### One chat change

1. `POST activity-drafts/{id}/operations` with kind `Revise`, the parent
   message and an optional target (an existing material or question ID). The
   existing start limiter and queue limits apply. In one transaction the server
   appends the parent turn to the saved chat and queues the operation; the
   idempotency key makes a lost response safe to resend.
2. **Revise step.** The planner returns a reply, the full updated plan or one
   clarification, assumptions, and one-off content changes (see the contract
   below). Production validation accepts it: envelope, `PlanChanges.AssignNewIds`
   and plan validation, and target IDs that exist and are generated content.
3. **Scope.** `RevisionScope` (pure, in `TaskEngine`) merges the plan diff with
   the planner's list. It compares fields, not `PlanChanges` paths: the planner
   also fixes wording that names a changed value (a topic change edits the
   question guidance sentence that mentions the old topic), so a path alone
   would over-trigger.

   | Change                                      | Content work        |
   | ------------------------------------------- | ------------------- |
   | Name only                                   | None                |
   | Goal, guidance, topic, audience, difficulty | Rewrite all texts   |
   | Total length                                | Rewrite all texts   |
   | One material's definition changed           | Rewrite that text   |
   | Material added                              | Write it fresh      |
   | Material removed                            | Remove it           |
   | Question formats, choice count or count     | All questions       |
   | Question guidance wording only              | None, unless listed |
   | Planner lists a text                        | Rewrite it          |
   | Planner lists up to three questions         | Replace them        |

   Fresh writing is the measured first-text path (ideas, writing, polish).
   Question guidance text changes alone rebuild nothing: the planner restates
   app rules there (it replaced the unsupported answer-key sentence with
   "results only"), and the existing questions still fit. A guidance change
   that existing questions no longer satisfy must come with "all questions" in
   the planner's list; slice 1 measures that case before shipping.
   A rewrite is the existing single-text stage: it receives the current title
   and body under the updated settings, with the planner's instruction or, when
   none is listed, the parent's message. So "turn the story into a poem" keeps
   the plot, while "make it about pirates" yields a new story. Rewrites are not
   polished, as measured. Any text change rebuilds all questions, because
   questions depend on every material revision, and "all questions" absorbs
   single replacements. Content that needs no work is re-accepted under the new
   settings through the existing adoption rules; if it fails validation (for
   example a new strict length range), it is rewritten instead.

4. **Execute.** The worker runs the scoped steps in order on the operation's
   working copy (`GenerationArtifacts` gains the working plan and a pending
   step queue), checkpointing each step's evidence as today.
5. **Apply.** One save writes plan, input, document, the assistant reply and
   the undo copy, fenced by the draft revision. A clarification, or an answer
   that changes nothing, saves only the reply.

A manual save, undo or release clears the undo copy; a running operation keeps
manual saves out (409) as today.

### Planner contract

- **Prompt.** The authoring prompt, unchanged, plus one revision block. It tells
  the model to return the complete updated plan, keep untouched fields
  verbatim, record lasting requirements in the plan, list only one-off edits the
  plan does not express, never list supplied sources or questions that depend
  on a changed text, and reply in one or two short Hebrew sentences.
- **Input.** Message, optional target, base plan, current document (titles,
  bodies, questions with answers; no provenance), and at most
  `MaxContextTurns` recent turns within `ContextLength`.
- **Schema.** The template schema root plus `reply` (1–600 characters) and
  `changes`: materials `[{ id, instruction }]` and questions
  `{ scope: none | selected | all, items[≤3]: { id, instruction } }`. IDs are
  request-owned enums, as `AiSchemas` already builds for other stages.
- **Version.** A new stage name under `EngineVersions.Revision`; the bump
  follows the usual rule.

### Operations

- Public kinds become `Create` (write whatever generated text and questions are
  missing), `Revise` (chat change) and `GenerateQuestions` (the chat's "update
  the questions" button). Material and question replacement remain internal
  steps.
- The fixed `NextStage` chain becomes the stored pending queue. `StepLimit`
  rises to the bound the scope table allows: revise, ideas, writing, polish, up
  to four rewrites and one question step or three replacements (12).
- `DraftLimit` (128 operations per draft) stays; reaching it asks the parent to
  copy the activity, as today.
- Recovery, cancellation, the compatibility guard and artifact expiry are
  unchanged.

### Chat and undo storage

- `ActivityDraft.ChatJson`: turns with role (parent or assistant), text, UTC
  time and the operation they belong to. At most 100 stored turns, oldest
  dropped; parent text up to `MessageLength`, assistant text up to 600
  characters. Assistant failure and notice turns are application-written
  Hebrew, not model output.
- `ActivityDraft.UndoJson`: the plan, input and document before the last
  chat-started operation (`Revise` or `GenerateQuestions`), with the revision
  that operation produced. One level.
- Both are parent-only: they never enter snapshots, child DTOs, the library
  projection or change notes, and draft deletion and family reset remove them.

### Inputs fold into the plan

An activity created from a template folds the chosen settings, question format,
choice count, total length and control values into its own plan, so the plan is
the single truth the chat edits and a per-activity override can never mask a
chat change. Text the parent supplies per activity stays in the input: the chat
never edits supplied sources, and its source kind is part of the resolved
request. The template keeps its provenance link and is never changed. Existing
drafts fold the same way when their first `Revise` starts. The acceptance
fingerprint covers resolved values, not plan defaults, so folding leaves content
acceptance unchanged; a test proves it.

An added material is written with the existing texts as context: the writing
stage gains a mode that writes only absent materials instead of regenerating
the whole generated set, so a chat request such as "add a short poem too" never
rewrites the story.

### API

Routes are under `/api/activity-drafts`; `POST /api/ai/template-drafts` is
unchanged.

| Request                   | Change                                       |
| ------------------------- | -------------------------------------------- |
| `POST /`                  | Accepts the pre-draft chat turns             |
| `POST {id}/operations`    | The three kinds in [Operations](#operations) |
| `POST {id}/undo`          | New; expected revision                       |
| `POST {id}/adopt-content` | Kept for "the questions still fit"           |
| `GET {id}`                | Adds chat turns and undo availability        |

### Frontend

- `template-chat` becomes the shared assistant chat: saved thread for
  activities, local thread for templates, with item targeting.
- The activity workspace becomes editor plus chat; the read-only settings
  summary replaces the activity's plan editor and setup fields.
- Removed from activities: `scoped-repair` boxes, the four-step progress list,
  the text-then-questions stop, stale and adopt banners, and the unapplied-result
  panel (a conflicting change now applies nothing and the chat offers to resend).

## Ownership and bounds

Every route stays family-scoped through the draft; the planner call has no
identity or database access. All new limits live in `EngineValidation` and are
served through `GET limits`. Answer keys reach the planner (a server-side,
parent-owned call) but never child DTOs. The planner's targets are checked
against the draft before any step runs.

## Verification

- **Engine:** `RevisionScope` table cases, merge rules, and re-acceptance
  fallback; planner envelope and target validation.
- **API (isolated AI provider):** revise success, clarification, reply-only,
  failure with nothing applied, consistent-prefix apply for `Create`, revision
  fence conflict, undo allowed and refused, chat bounds, cross-family access
  denied, snapshots and child DTOs without chat or undo.
- **Angular:** chat thread, targeting, undo, manual-edit offer, loading, empty
  and error states, keyboard and screen-reader paths, RTL and narrow layout.
- **Browser:** the isolated end-to-end workflow test updated to the new flow.
- **AI:** the planner prompt is new, so it has a pre-registered measurement
  before it ships; see [planner evidence](#planner-evidence).

## Delivery slices

Each slice leaves the app working, tested and documented. Slices 1 and 2 bring
the chat to existing activities, slice 3 the first-request flow, and slice 4
removes what the new flow replaced.

1. Engine and API: planner stage, `RevisionScope`, step queue, all-or-nothing
   apply, folding at the first `Revise`, chat and undo storage, `Revise` and
   undo endpoints.
2. Frontend: shared chat, editor plus chat workspace, targeting, undo,
   manual-edit offer; remove `scoped-repair` and the progress list.
3. First request: draft creation from the first plan, `Create`, folding at
   template-based creation, absent-only writing.
4. Cleanup: retire public replacement kinds and the two-part flow, update the
   evaluation harness and the four guides.

A typical message costs about $0.05 (planner, one rewrite, questions); a reply
with no change costs about $0.01.

## Planner evidence

A prototype of the planner contract ran on three owner activities (grade-3
reading, numeric word problems and a story) with ten chat requests, twice each,
under a pre-registered reading (`artifacts/evaluations/revise-planner-2026-10-08/`,
20 calls, $0.26 of $0.30):

- Valid output 20/20 and the intended outcome 20/20: an easier replacement for
  a targeted question, pirates, medium difficulty, six questions, a story half
  as long, one of two near-identical questions replaced, a rhymed poem, the
  answer-key explanation refused with an assumption, a content question
  answered, and "thanks" changing nothing.
- Requests that should not touch settings returned identical plans, except the
  answer-key request, which replaced the stored unsupported sentence. Topic and
  difficulty changes also edited the question guidance sentence that named the
  old value (4 calls); the protocol had not listed that path for the topic
  request, so counted strictly it sits exactly at the registered limit. Scope
  therefore compares fields, and question guidance wording alone rebuilds
  nothing.
- Not yet measured: a question guidance change that existing questions no longer
  satisfy (for example "focus the questions on vocabulary").
- The planner listed only the two question replacements and expressed every
  other change in the plan, so server-derived scope carries most content work.
  A genre conversion arrives as a guidance change, which is why affected text is
  rewritten from its current body rather than written fresh.
- A planner call costs about $0.013; each message then costs whatever stages its
  scope runs.

The prototype measured planning only. Each delivery slice still measures its
own end-to-end behavior with isolated providers, and the production prompt must
match the tested block byte for byte.
