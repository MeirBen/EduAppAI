# Product specification

**Target redesign, 8 October 2026:** the parent product centers on activities,
saved drafts and explicit approval. The engine/API, canvas/chat and backend
retirement are implemented in slices 1–3. The
[slice-3 record](superpowers/plans/2026-10-09-activity-only-slice-3.md) tracks
verification and the separately coordinated data cutover. The
[architecture](architecture.md) describes current code.
The implemented child flow remains in force. Every subject uses the same generic
AI path; there are no subject-specific generators or seeded educational records.
This is a fresh-start cutover: existing learning data need not be exported,
converted or supported by the new contracts.

```text
Describe an activity → create and refine its draft → approve when ready
                                               ↘ it saves itself; resume later
```

## Activity lifecycle

1. **New activity:** describe the goal, audience and requirements in chat. AI
   proposes requirements or asks one focused clarification. Show a concise
   summary, collect required source text and confirm AI-extracted sources.
2. **Draft:** once requirements and source inputs are valid, the activity and
   its chat become a draft by themselves, before any generation. A draft may
   have missing texts/questions or unresolved content diagnostics; saving makes
   no AI call. Every later valid change saves itself after a short pause.
   Initial setup before this checkpoint uses the navigation guard.
   While the draft has no questions or generated text, chat can update its
   requirements without generating content; Create remains explicit.
3. **Create:** one parent-started operation generates the needed texts and
   questions. Apply the complete validated result atomically; a failed or
   cancelled operation keeps the saved draft unchanged. The parent does not
   orchestrate separate writing, polish and question steps.
4. **Refine or resume:** read the activity with chat alongside it. Chat changes
   structure and requirements; explicit Edit opens content fields, whose edits
   save themselves. Starting AI first saves any pending edit, and editing pauses
   while it runs. The parent's own texts come only from setup; chat never adds
   one. Saved drafts can
   be reopened with their chat, status and available undo.
5. **Approve:** review the saved content and answer keys, then explicitly mark
   the activity ready. Approval validates and freezes the saved revision; it
   makes no AI call and never assigns work automatically. Ready activities are
   read-only and can be assigned to children. Editing one starts a new draft
   from its snapshot, preserving existing assignments and results.

The library has **drafts** and **ready activities**, with one New activity entry
point. There is no template catalog, template editor, template publication,
template version history or “create from template” flow. An internal learning
plan belongs to its activity; it is not a separately saved reusable product.

### Workspace and changes

The activity is a readable canvas by default, with chat beside it on desktop
and as a sheet over it on phones. Use Hebrew/RTL, accessible native controls and
the same state for read/edit views. Titles, instructions, wording, options,
answers and points remain manually editable; adding/removing/reordering content
and changing requirements belong to chat, which can also rewrite the title or
the learner instructions on request, such as removing their vowel marks.
Settings are a summary, not a template form.

- Answers, clarifications, refusals and no-ops change no activity content.
- Add questions with an optional focused instruction: generate only additions,
  preserve originals and update the activity's question count atomically.
  Removal/reorder uses validated surviving IDs after the planner call; it
  preserves their content and makes no content-generation call.
- Broader changes regenerate dependent content. The server derives scope from
  effective requirements and explicit edits; the model cannot suppress needed
  dependent work. Text changes never silently leave old questions current.
- After manual source/text edits, offer to update questions or confirm that
  they still fit. Confirmation makes no AI call and must pass validation.
- Keep one-level undo for the last successful chat change or question update,
  including structure changes, but not initial creation. It restores plan and
  document together under a new revision; a saved manual edit, adoption and
  approval clear it.

The [activity chat design](activity-chat-design.md) owns the execution matrix,
prompt/schema/context contracts, operation bounds and rollout checks.

### Saving and recovery

One active AI operation is allowed per draft. Saves, adoption, undo and approval
are blocked while it runs, with server revision checks for races and other tabs.
Intermediate AI results stay in operation evidence; only final success changes
saved content. Stop commits cancellation before stopping transport; a completion
that already committed remains successful. Unknown outcomes are never retried
automatically. Reuse the operation key to check a lost start response; check
saved state after an uncertain save or approval. Conflicts preserve local edits
and pause autosave until the saved version is loaded.

The URL identifies the saved draft and operation. Chat and undo are parent-only
draft state; invalid field edits stay local until fixed. Another device's
save/deletion is
announced without silently replacing the current buffer. Draft deletion removes
its operation history and chat, and late AI output cannot recreate it.

Library deletion and family reset follow the [retention rules](#retention-and-reset).
Reset remains an explicit family action independent of template routes. The
server enforces ownership on every read and write.

## Contracts

- **LearningPlan:** the activity's concrete settings, goal/guidance, up to four
  material definitions and question requirements. Simplify the
  [current contract][plan]; it has no independent identity or publication.
- **ActivityDraft:** mutable saved plan and editable
  [document][content] with a concurrency revision; content identity, provenance
  and acceptance are server-owned.
- **GenerationOperation:** durable idempotent start, stage checkpoints, safe
  failure evidence and cancellation and recovery state.
- **TaskSnapshot:** immutable reviewed plan, content and answer
  keys for a ready activity, independent of later draft edits/deletion.

Reuse these engine/storage boundaries. Draft and ready are the visible
activity states; generation status is operational, not another product type.
Retire `TaskTemplate`/`TaskTemplateVersion` and their active dependencies through
the cutover below; do not replace them with another reusable-definition layer.

Shared settings are topic and audience (required, up to 200 characters),
difficulty (`easy`, `medium` or `hard`, relative to the audience) and a question
count from 1 to 20. When a parent's request names no difficulty, authoring
defaults to `easy` through third grade and `medium` above, disclosed as an
assumption. Resolve choices into one effective set of activity requirements;
the plan and displayed count must agree. Feasibility and total content limits
also bound the count. New requirements are concrete values for
this activity, not adjustable defaults for future activities.

Use one canonical plan: `settings` hold concrete values; materials hold their
source, guidance and length; questions hold formats, choice count and guidance.
Extra subject requirements belong to scoped guidance. Remove custom control
definitions, selectable defaults, adjustable flags and per-activity override
maps. There is no separately persisted `TaskRequest`/input JSON to synchronize.
The server derives typed stage inputs and fingerprints from the saved plan.

Keep `name` as the parent library label and document `title` as the learner
heading. A material has an ID, label, source, guidance, optional length and
source text: text is required for `supplied` and null for `generated`. Question
requirements contain the allowed formats, guidance and one concrete choice
count (2–6 when single-choice is included, otherwise null). Length is either
an approximate integer count or a strict lower/upper range, never adjustable.

Validate types and bounds before AI calls; reject unknown fields, IDs and
inapplicable values. Null is allowed only where the contract permits it.
Renaming keeps material identity. New AI proposals use null new IDs and may
not invent existing identities or alter a retained supplied source.

Material sources are `generated` or `supplied`; supplied text belongs directly
to this activity and stays verbatim. Length requirements apply only to generated
bodies, either per material or as a combined total, never both. An approximate
target is advisory,
while an inclusive range, whose minimum is below its maximum, is strict. Exact
word counts are not offered, since generation cannot meet them reliably; a
request for one becomes a target. Counted words contain a Unicode letter or
digit; body headings count, but titles, instructions, questions and answers do
not. The first slice defers strict total-length changes that need allocation
across several AI calls; clarify per-text ranges or an approximate total as
defined in the chat design, preserving the saved draft until the parent chooses.
Transforming supplied text needs a separate generated material that preserves
the original. A failed strict material stage blocks questions and release.
New material first gets five ideas with distinct causal or explanatory structures.
The server selects the lowest model-estimated overlap with bounded recent family
content, drawing among ties, and checkpoints that choice before writing.
Questions also receive bounded previous prompts to vary evidence targets where
the learning requirements permit. Topic, source fidelity and prescribed practice
take priority over novelty; variety is not guaranteed. Improving one text
follows the parent's instruction within the activity's requirements; changing
topic or other requirements goes through chat and updates dependent content. Hebrew
is written without niqqud, except full niqqud for beginning readers up to second
grade or on request; a plan records niqqud only when the parent asks for it.
Prose is split into paragraphs of a few sentences; poems and dialogue keep their
lines.

Questions are numeric, short-text or single-choice, each with an application
ID, prompt, typed interaction, answer and integer points. Numeric answers use
invariant decimal text; choice answers exactly match an option. Multiple selected
formats mean a mixture containing each at least once; one selected format
applies to all questions. There is no separate format-selection mode. Exact per-format
quotas need clarification rather than approximation, and choice count applies
only to single-choice questions. A calculation or comparison is a whole prompt,
option or answer; inside sentences, including texts and instructions,
operations and relations are written in words, so everything displays in order
and no `<` or `>` sits beside Hebrew words, where it would display reversed.
Surrounding whitespace in generated question text is removed before validation;
nothing else in a response is repaired.

Authoring gives one proposal or one focused clarification per parent message,
with assumptions reflected in the plan. The chat is one conversation from the
first message: before and after the draft is saved, a request carries up to
4,000 characters plus the newest turns that fit six turns and 12,000
characters. Older turns stay visible but leave the context, so a lasting
requirement belongs in the plan, which every request sends whole. Before the
first plan exists the conversation is the only record: after four
clarifications without a plan, or sooner when long texts fill the 12,000
characters, the next request no longer carries the first one. One window rule
for every request is the accepted trade-off. Never truncate required
sources or constraints. The server writes every assistant reply and computes
the actual plan changes.

For math, explicit operand/result ranges, operations, fractions, remainders and
precision take precedence over inferred teaching choices. Grade and difficulty
guide problem selection without inventing mandatory single-digit limits. An
unqualified elementary "up to N" is proposed as a bound on given numbers and
results, with that interpretation disclosed as an editable assumption.
Incompatible requirements need clarification. Reading passages and mathematical
scenarios requested as separate context or shared across questions are materials;
short independent word problems may keep their givens in the question prompt.
This distinction follows the source's role, not the subject. Questions identify
the quantity and unit sought and use an answer format that can express it.
Numeric input holds one finite decimal, not a fraction expression or a
quotient/remainder pair.

Generated content records its accepted effective input, and questions depend on
every material sent with them. Editing a source or requirement makes dependent
content stale until it is edited or explicitly adopted; adoption never rewrites
its origin or waives strict checks. Changing a choice leaves an answer
diagnostic instead of picking a replacement. Missing or stale dependencies
outside an operation's scope need explicit repair/adoption before content calls;
approval checks the whole saved document.
Validators never judge educational truth.

Server limits:

- Plans: 24,000 serialized characters; names and labels 100; goal 500; shared
  guidance 4,000; material and question guidance 1,000.
- Questions: 1–20 per activity; strict AI schemas carry the exact count up to a
  configured endpoint limit ([AI guide](ai.md#strict-schema-contract)).
- Documents: 0–4 materials and 8,000 total characters; release needs at least
  one complete question.
- Title 100; instructions 1,000; material body 4,000; prompt 500; answer and
  option 200. Choice questions have 2–6 distinct options; points are 0–100.

## Boundaries

AI proposes schema-constrained content; the engine validates, resolves and
assembles it; features own authorization, persistence and concurrency. Parents
review language, correctness and suitability. Parent answer-bearing DTOs never
serve children. Hebrew/RTL screens render plain text with native accessible
controls and real workflows, not placeholders.

See [architecture](architecture.md) for implementation, [README](../README.md)
for setup, [UI guide](ui-guide.md) for presentation and the
[AI guide](ai.md#design-and-cutover-decision) for quality evidence and trade-offs.

## Activity-only cutover

- Remove the template library, creation/editing routes, Save template action,
  publication/version APIs and template-specific workspace state. Keep one
  activity workspace, one chat and one saved draft as the source of truth.
- Adapt authoring and revision to the same concrete plan schema. Remove the
  parameter-builder UI, control/override contracts and duplicated input storage;
  keep generic generation, content validators and the existing worker.
- Creation accepts an activity plan or a ready snapshot for editing. Remove
  template IDs/version checks and their provenance columns and tables/entities.
- Move initial authoring to `POST /api/ai/activity-plans` and family reset to
  `DELETE /api/learning-data` before retiring template routes. Preserve family
  ownership, CSRF, rate limits, reset confirmation and transactional deletion.
  Update application, evaluation and test callers together.
- Start with empty learning records at the coordinated cutover. No template
  export, conversion, legacy readers or old-operation compatibility path is
  required. Keep parent accounts, family identity and AI configuration. Use a
  reviewed EF migration/reset; application startup must never silently erase data.
- Cut over only after draft creation/resume, atomic AI changes, approval,
  assignment, reset and fresh-database setup pass the isolated acceptance
  checks. Ship the matching frontend/backend together; implementation slices
  are not independent releases. Stop the running app/worker for the explicit
  learning-data reset and schema update. Verify account/configuration retention
  and an empty queue before restart. Update implementation/setup docs on release.

## Next steps

For an existing installation, coordinate its data cutover using the
[slice-3 verification record](superpowers/plans/2026-10-09-activity-only-slice-3.md).
The existing parent/child [acceptance record](child-flow-plan.md) remains baseline
evidence. Isolated redesign checks do not establish live-model quality; review
representative generated activities with the family before giving them to children.

Before inviting other families, add account recovery and tested backup/restore.
Shared-parent onboarding, dashboards, broad library pagination, update notices,
offline synchronization and native packaging remain deferred.

Deferred maintenance:

- Use general server feedback for shared 502–504 errors unless the response
  identifies an AI-specific problem.
- Review `braces` advisory GHSA-vfj7-8cjw-p6xm in Markdown lint development
  dependencies before choosing a dependency change.

## Child flow

**Implemented and verified, 6 October 2026:** profiles/device access, assignment,
resumable work, parent grading and both route shells; see the
[acceptance record](child-flow-plan.md). Assignments use immutable reviewed
snapshots; child-management and learning operations make no AI calls.

Children use a separate browser/device. Shared-browser switching,
self-registration, repeated assignment attempts, AI grading, hints, answer-key
delivery and gamification are outside this milestone. All existing question
types remain available.

### Profiles and device access

- A child belongs to one family and has a trimmed, nonblank display name of
  1–100 characters.
  Names need not be unique. No email, password or date of birth is required.
  Parents can rename, disable and re-enable their own children. Disabling
  revokes all device access and pending activation codes while preserving work.
  Re-enabling requires fresh activation; it never restores a revoked grant.
- The parent creates an activation code for a named device (trimmed, nonblank,
  1–100 characters).
  Following RFC 8628 user codes, it is eight cryptographically random letters
  from the vowel-free alphabet `BCDFGHJKLMNPQRSTVWXZ`, shown as `XXXX-XXXX` so a
  child can type it, and read regardless of case, spaces or dashes. Ten-minute
  expiry, single use and the redemption rate limits make guessing impractical;
  the stored SHA-256 hash keeps codes out of the database but does not resist
  offline guessing of so short a code. It is displayed once and sent in the
  activation request body, never a URL, log or browser storage. Issuing a new
  code invalidates that child's
  previous unconsumed code. After a lost activation response, check for a valid
  child session first; if none exists, request a new code rather than replaying
  the consumed one.
- Consume a code and create its device grant in one transaction; concurrent
  redemption succeeds once. Invalid, expired and used codes give the same
  safe response. Rate-limit redemption to ten attempts per minute per source
  IP and 120 per minute for the single process, without queuing; parent code
  issuance is limited to ten per minute per authenticated parent.
- A device grant lasts 30 days from activation without sliding renewal. The
  separate child authentication cookie is HttpOnly, SameSite Strict, Secure in
  production, persists across browser restarts, and expires with the grant.
  Activation explains that this browser keeps access for up to 30 days, until
  disconnected or revoked; clearing browser cookies requires fresh activation.
  The parent can revoke individual devices; disconnecting on the child device
  revokes that grant too.
- Bind parent and child policies to separate named authentication schemes,
  using [scheme-specific authorization][auth-schemes]. A child cookie never
  authorizes parent APIs, and a parent cookie never authorizes child APIs.
  Reject activation with an active parent or child session, and reject parent
  sign-in with an active child session. Explain which session must be closed;
  never silently merge or replace identities.
- Child identity, family and grant are server-owned. Every child request checks
  that the profile and grant remain valid; writes recheck them inside the same
  short transaction as the state change. Revocation committed first prevents a
  later save or submission, including requests that passed authentication
  before revocation.
- Child activation and writes use [CSRF protection][csrf], including a fresh
  identity-bound request token after activation. The existing parent token
  flow must continue to work. Token issuance refuses an active opposite-mode
  session rather than replacing that session's request token. A transient
  outage is not an expired session:
  display retry feedback without falsely reporting successful activation,
  saving or submission.

### Optional grade and age

Parents may record **כיתה** (school grade) and **גיל** (completed years).

- Both default to unset and can be cleared independently. Neither implies the
  other or learning ability. Grade is trimmed free text up to 100 characters;
  age is an integer from 0 to 120. The API publishes these bounds.
- Record server UTC when age is entered or changed; unrelated edits preserve
  that date, clearing age clears it. Do not increment age automatically or
  require a birthday or separate confirmation control. Details are parent-only.
- Create/update accepts optional `details` with required nullable `grade` and
  `age`. Omission on update preserves stored details for older clients. All
  edits use the profile revision; all workflows work without these fields.
- Future audience prefilling requires an explicit parent action and review.
  Send only selected educational context, never names or profile IDs. Save the
  resolved audience in the draft/snapshot. Grade and age may inform vocabulary,
  reading level, instruction complexity and difficulty, not certify suitability.
- Profile changes never rewrite saved content, assignments, answers or grades,
  or trigger generation. Recording details adds no AI calls, grading or automatic
  tuning.

### Profile and device cleanup

- Profiles expose creation and last-update UTC times; legacy update times remain
  unknown until the next save. Secondary timestamps use a collapsed “פרטי זמנים”
  disclosure, including the date age was last changed.
- A parent can delete a profile only if it has no assignments, including withdrawn
  assignments. Deletion checks ownership and revision in the same transaction as
  the history check and removes device grants and activation codes. Concurrent
  assignment creation cannot lose history. Profiles with history can be disabled.
- Active devices offer access revocation. Revoked or expired device entries can
  then be removed from the list. The server rechecks ownership and inactivity;
  removal never deletes assignments, answers or results.
- Library cleanup follows [retention rules](#retention-and-reset); no duplicate
  cleanup screen or automatic deletion is introduced.

### Assignments and learner-facing content

- An assignment links one family-owned child to one family-owned reviewed
  `TaskSnapshot`. New creation rejects disabled children and archived snapshots;
  an existing owned assignment may still be returned on replay. One
  assignment per child/snapshot pair is allowed, including after withdrawal;
  replaying creation returns that assignment, even when withdrawn, so a delayed
  duplicate never undoes a later withdrawal. A deliberate future repeat needs
  a new reviewed snapshot in this milestone.
- Assignment state is `assigned`, `withdrawn`, `awaiting-review` or `completed`.
  Starting or saving work keeps it `assigned`. The parent may withdraw only an
  `assigned` item, preventing further child access while retaining its history.
  An explicit restore at the current revision undoes a withdrawal, with the
  saved session, under the same eligibility as new creation (enabled child,
  active snapshot); repeating either request is harmless, and a stale one of
  either returns a conflict. Restore acts only on withdrawn work and submission
  only on assigned work, so the two never both succeed.
  Withdrawal and submission are serialized: whichever commits first wins.
  There are no due dates or automatic assignment expiry in this milestone.
- The child inbox separates available work from submitted work. New collections
  use bounded pagination (25 items per page, maximum 100) so older assignments
  and reports remain reachable. Existing library lists keep their current
  behavior; adding these pages does not require a library-wide redesign.
- Child responses explicitly project the title, instructions, material IDs,
  titles and bodies, question IDs, prompts, interaction types, options and
  possible points. Include only the child's own saved answers and status where
  needed. Never serialize `TaskDocument` or `SnapshotPreview` to a child:
  answer keys, guidance, resolved inputs, provenance and model metadata stay
  server/parent-only. These are forbidden response fields on every success and
  error path, including completion and history.
- Enforce assignment ownership on every read/write. Missing, foreign-family
  and sibling-owned IDs return the same 404. Answers cannot choose a child,
  family, score or question definition through request data. Authenticated
  responses are not cached; learning data stays out of browser persistent
  storage and the service-worker cache.

### Working session and submission

- Each assignment has one resumable `TaskSession`, created explicitly and
  idempotently on start. Opening a read-only endpoint does not create work.
  Record server UTC start, last-save and submission times; these describe
  elapsed wall-clock time, not measured engagement or active learning time.
- The child edits a local buffer and explicitly saves progress. Show saving,
  saved, unsaved and failed states. Refresh restores the last acknowledged
  checkpoint; warn before leaving with unsaved edits. Automatic/background
  saving and offline queues are deferred. Keep reading material accessible
  while answering, with native labeled controls, Hebrew/RTL layout, keyboard
  access, 360px width and 200% text support.
- A save sends the complete answer collection and `expectedRevision`; an
  acknowledged write advances a server revision. Bound the collection by the
  snapshot's question count (at most 20) and each value by 200 characters.
  Require the collection, revision and each entry's question ID and string value;
  null collections, entries or values are invalid. Duplicate/unknown IDs and
  unknown JSON members are invalid. An omitted question, empty string or
  whitespace-only value means unanswered. Preserve nonblank text exactly;
  incomplete numeric text may be saved for correction, but invalid choice values
  may not. Length/count limits apply before treating any value as unanswered.
- A stale write returns 409 and keeps the child's local text visible. Offer an
  explicit reload of saved work. After a lost response, check the saved state
  before another write; do not automatically replay a submission or claim it
  failed. Navigation/disconnect cancels client transport, not a committed save.
- Submission is explicit, with confirmation when answers are missing. It sends
  the final complete answer collection and expected revision; it need not rely
  on a preceding successful save. Final validation, answer freezing, automatic
  scoring, timestamps and the assignment/session state change commit together.
- Submitting identical answers again returns the saved outcome even with an
  old revision; different answers after submission return 409. Compare answers
  by question ID regardless of request order, treating absent/blank values as
  unanswered; otherwise compare the preserved strings exactly. A revision
  mismatch before submission remains a conflict.
- An owned withdrawn assignment returns 410 and stops editing with an explicit
  withdrawal message. Lost access asks for activation; missing work returns 404.
  Temporary failures offer a saved-state check without discarding the local
  buffer or describing the problem as a parent-login or AI failure.
- Submission freezes answers. It becomes `completed` immediately if no parent
  grades are needed, otherwise `awaiting-review`. Neither state accepts answer
  edits, withdrawal or another attempt. Start/resume calls return this saved
  state and never reset it.

### Elapsed activity time

Derive elapsed time from server UTC `StartedAtUtc` and `SubmittedAtUtc` in
assignment details/results; no browser stopwatch or stored duration counter. The
player shows the same interval live while work is open, computed from
`StartedAtUtc` and the device clock for display only; a device clock behind the
server reads as zero.

- The first explicit player start begins the interval. Previews/read-only
  requests do not start it; reopening, refreshing and resuming do not reset it.
- Submission freezes the interval; retries, later visits and parent grading
  never extend it. Include start/submission times for context.
- Label the duration as including breaks, closed tabs and offline gaps, not
  active study. Before submission show the start time and an unsubmitted state;
  missing or inconsistent timestamps show unavailable duration, never a guess.
  Exact copy and display units live in the [UI guide](ui-guide.md#parent-learning-management).
- Never use elapsed time to change grades or infer ability. Active engagement,
  pause tracking, countdowns and time limits need separate future decisions.

### Scoring and parent review

The first release uses automatic scoring for choices and numbers and parent
review for **all nonblank short-text answers**. Valid alternative wording must
not be silently marked wrong. There is no AI grading or synonym matching.

At submission:

- **Unanswered, any type:** Zero points, no pending parent grade.
- **Single choice:** Must be one frozen option; exact match to the frozen key
  earns full points, another option earns zero.
- **Numeric:** The player sends numeric answers without surrounding spaces.
  Use the existing invariant decimal grammar: an optional leading
  sign, digits and an optional decimal point followed by digits. No commas,
  exponent, NaN or overflow. Compare the exact decimal values expressed by the
  strings, ignoring leading integer zeros, trailing fractional zeros and the sign
  of zero, without rounding or tolerance. `+02.00` equals `2`; a tiny nonzero
  fraction does not equal `0`.
  Equal values earn full points; another valid number earns zero. Invalid
  nonblank input blocks submission. Existing key-validation rules stay unchanged.
- **Short text:** Preserve the answer; points stay unset until parent review,
  even if it matches the key or the question is worth zero points.

For stored results and parent review:

- The server stores the scoring-policy version and automatic awards at
  submission. Reports never rerun scoring against changed rules or draft content.
  A pending result shows the automatic subtotal, possible total and outstanding
  review count, clearly labeled; it has no final total or percentage.
- The parent reviews the submitted text alongside the frozen question, source
  material and expected answer. The expected answer is a reference, not a
  mandatory exact string. Enter integer points from zero through the question's
  possible points; partial credit is allowed. Automatic awards are read-only.
- Finalize all pending grades in one request with the session revision.
  Require each pending question exactly once; reject missing, duplicate,
  automatic-question or out-of-range grades. Save the reviewer identity, UTC
  review time, grades, final result and `completed` state in one transaction.
- Concurrent or repeated finalization cannot overwrite a completed result.
  Repeating the same grade set returns it; different grades after completion
  conflict. Automatically completed work cannot acquire a parent review after
  the fact. Grade drafts remain local until finalization, with an unsaved-work
  warning. Grade corrections/reopening and written feedback are later features.
- Parent reports show the child, activity, submitted answers, frozen keys,
  automatic/manual awards and timestamps. Final totals appear only when all
  grading is complete; if possible points are zero, display points without a
  percentage. Child completion/history shows receipt, review status and final
  total when available, without keys or per-question correctness feedback.

### Retention and reset

- `TaskSnapshot` content stays immutable. Removing an assigned snapshot from
  the library archives it; it cannot receive new assignments but existing work
  and reports retain it. Unassigned snapshots may still be permanently deleted.
  The UI names archiving/deletion accurately before confirmation. Assignment
  creation and snapshot removal share transaction-safe ownership/state checks
  and restrictive foreign keys, so a race cannot leave dangling work.
- Keep withdrawn assignments and submitted results; the parent's list shows
  withdrawn work only under its own status filter. Disabling a child revokes
  access rather than deleting its history. Approving a new activity revision
  never changes an existing assignment or result.
- The explicit family reset remains the destructive exception. Its confirmation
  must name children, device access, assignments, answers, grades and content.
  Delete all of them in one transaction, including records beyond list limits;
  keep the family, parent accounts and AI configuration. After reset, old child
  cookies and activation codes cannot access or recreate deleted work. Update
  reset whenever a schema change adds family-owned learning records.

### Acceptance

Use the real application with disposable data to verify the complete
assign → activate → save/resume → submit → parent review loop after restart.
The [acceptance record](child-flow-plan.md#task-7-full-flow-acceptance-and-documentation-cutover)
maps these requirements to regression suites:

- Family/sibling and parent/child scheme isolation, mixed-cookie rejection,
  anonymous endpoint allowlists and missing/wrong-identity CSRF.
- Activation expiry/replay/concurrent redemption, persistent grant expiry,
  revocation, disable/re-enable, cancellation and lost-response recovery.
- Exact nested child-response allowlists on success/error and working/pending/
  completed states; zero child-flow AI calls.
- Start/save conflicts across tabs/devices, malformed/blank answers, numeric
  precision boundaries, reordered submission replay and lost acknowledgements.
- Submission/withdrawal/revocation/reset and assignment/deletion races,
  concurrent grading, atomic writes and immutable historical scores.
- Paging beyond 100, archive access, restart persistence and complete reset
  without affecting another family; existing parent workflows remain valid.
- Keyboard/screen-reader labels, error focus, RTL/mixed-language content,
  narrow screens and enlarged text per the [UI checks](ui-guide.md#check).

[plan]: ../backend/FamilyLearning.Api/TaskEngine/Models/LearningPlan.cs
[content]: ../backend/FamilyLearning.Api/TaskEngine/Models/TaskDocument.cs
[auth-schemes]: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/authorize-with-a-specific-scheme?view=aspnetcore-8.0
[csrf]: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-8.0
