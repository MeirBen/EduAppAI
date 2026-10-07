# Product specification

The application implements a content-first activity workflow. Every subject uses
the same AI path; there are no subject-specific generators or seeded
educational records.

```text
Parent prompt → editable learning plan + activity choices
→ applicable material ideas and writing → questions → editable draft
→ parent review → immutable ready snapshot

Save template: independently publish the reusable plan at any point
```

## Current workflow

1. Describe the goal, audience and requirements. AI proposes a structured plan
   or asks one clarification; direct editing and Undo remain available. A
   request built on a text the learner works with always includes that text.
2. Review shared settings, material sources, question formats and explicitly
   requested controls, and confirm exact source text extracted from a prompt.
   Supplied sources keep their original text.
3. Create the activity in two parent-started parts, without publishing a
   template: first the generated text, then its questions. Generated text gets
   one automatic polish for wording, level and clear errors before the parent
   reviews, edits or rewrites it and asks for questions; supplied sources are
   never edited, and plans without generated text start with questions. Each
   part saves a draft checkpoint, and accepted text survives a question failure.
4. Edit text, answers, options and points, or regenerate one material, one
   question, all questions or stale text. Incomplete drafts save with
   diagnostics, and generation never silently overwrites later edits.
5. Review the saved content and answer keys, then mark it ready. Release needs
   current accepted content, matching requirements and complete answers. The
   snapshot is read-only; copy it to a new draft to make changes.

Saving a template publishes only the reusable plan; it never saves activity
edits, generates content or releases a snapshot, and later versions never change
existing drafts or snapshots. Template editing defines only the plan and its
defaults; activities are created from a saved version.

The URL keeps the draft and operation for reload during generation; chat,
unsaved edits and Undo stay local. Cancellation preserves saved checkpoints. An
unknown provider outcome is terminal and never retried automatically, a lost
start response can be checked with its idempotency key, and a stale save returns
409 while preserving local input.

The library separates templates, drafts and snapshots, and deleting one keeps
content copied from it. Deleting a draft removes its operation history. A
confirmed family reset removes all owned learning records, including items
beyond list limits, keeping accounts and AI configuration. The server enforces
ownership on every read and write. An open library follows changes saved on
other devices, and an open draft says when another device saved or deleted it,
loading a newer version only on request.

## Contracts

- **LearningPlan:** goal, guidance, shared defaults, scoped controls, up to four
  material definitions and question requirements ([contract][template]).
- **TaskTemplate / TaskTemplateVersion:** family-owned reusable identity and
  immutable plan versions.
- **ActivityDraft:** mutable saved plan, input and editable
  [document][content] with a concurrency revision; content identity, provenance
  and acceptance are server-owned.
- **GenerationOperation:** durable idempotent start, stage checkpoints, safe
  failure evidence and cancellation and recovery state.
- **TaskSnapshot:** immutable reviewed plan, resolved input, content and answer
  keys, independent of later template or draft deletion.

Shared settings are topic and audience (required, up to 200 characters),
difficulty (`easy`, `medium` or `hard`, relative to the audience) and a question
count from 1 to 20. When a parent's request names no difficulty, authoring
defaults to `easy` through third grade and `medium` above, disclosed as an
assumption. Per-activity settings override template defaults; inside an
activity they are its plan defaults, which a saved template keeps. Feasibility
and total content limits also bound the count. A value the parent may change
per activity is bounded only by these application limits.

Controls hold text, integer, select or boolean values in plan, material or
question scope, with stable application-owned IDs and a human-readable meaning.
AI adds them only for explicitly requested per-activity choices; fixed
requirements stay in guidance. Values are validated before AI calls.

Omission resolves defaults once; explicit null, numeric strings, unknown IDs,
inapplicable fields and fixed overrides are invalid, while false, zero and
empty optional text survive. Renaming keeps a material's or control's identity.
New AI proposals use null IDs and may not invent existing identities or alter a
retained fixed source.

Material sources are generated, fixed verbatim text, or text supplied per
activity. Length requirements apply only to generated bodies, either per
material or as a combined total, never both: an approximate target is advisory,
while an inclusive range, whose minimum is below its maximum, is strict. Exact
word counts are not offered, since generation cannot meet them reliably; a
request for one becomes a target. Counted words contain a Unicode letter or
digit; body headings count, but titles, instructions, questions and answers do
not.
Transforming supplied text needs a separate generated material that preserves
the original. A failed strict material stage blocks questions and release.
New material first gets five ideas with distinct causal or explanatory structures.
The server selects the lowest model-estimated overlap with bounded recent family
content, drawing among ties, and checkpoints that choice before writing.
Questions also receive bounded previous prompts to vary evidence targets where
the learning requirements permit. Topic, source fidelity and prescribed practice
take priority over novelty; variety is not guaranteed. Improving one text
follows the parent's instruction within the activity's requirements; a different
topic or other choice is changed in the activity choices and regenerated. Hebrew
is written without niqqud, except full niqqud for beginning readers up to second
grade or on request; a plan records niqqud only when the parent asks for it.
Prose is split into paragraphs of a few sentences; poems and dialogue keep their
lines.

Questions are numeric, short-text or single-choice, each with an application
ID, prompt, typed interaction, answer and integer points. Numeric answers use
invariant decimal text; choice answers exactly match an option. A fixed set of
formats means a mixture containing each at least once; a selectable format
means one format for the activity, with an allowed default. Exact per-format
quotas need clarification rather than approximation, and choice count applies
only to single-choice questions. A calculation or comparison is a whole prompt,
option or answer; inside sentences, including texts and instructions,
operations and relations are written in words, so everything displays in order
and no `<` or `>` sits beside Hebrew words, where it would display reversed.
Surrounding whitespace in generated question text is removed before validation;
nothing else in a response is repaired.

Authoring gives one proposal or one focused clarification per parent message,
with assumptions reflected in the plan. A request carries up to 4,000
characters plus six unresolved turns or 12,000 characters; a full context needs
explicit consolidation, never silent truncation. The app computes the actual
plan changes.

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
diagnostic instead of picking a replacement. Scoped repair can run beside
unrelated incomplete fields, but release checks the whole saved document.
Validators never judge educational truth.

Server limits:

- Plans: 24,000 serialized characters; names and labels 100; goal 500; shared
  guidance 4,000; material and question guidance 1,000.
- Controls: up to 16 across all scopes; text values up to 500 characters; select
  controls have 1–20 distinct options of up to 100 characters.
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

## Next steps

The parent/child milestone and approved enhancements passed software acceptance;
see the [acceptance record](child-flow-plan.md). Next, run the family sanity test
with separate browsers and review representative generated content for
correctness, Hebrew, suitability and answer quality before giving it to children.
Use concrete findings to guide further AI tuning.

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
  Use 16 cryptographically random bytes encoded as base64url, valid for ten
  minutes, displayed once and stored only as a SHA-256 hash. Send it in the
  activation request body, never a URL, log or browser storage. Copy/paste is
  sufficient for this release. Issuing a new code invalidates that child's
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
  replaying creation returns that assignment. A deliberate future repeat needs
  a new reviewed snapshot in this milestone.
- Assignment state is `assigned`, `withdrawn`, `awaiting-review` or `completed`.
  Starting or saving work keeps it `assigned`. The parent may withdraw only an
  `assigned` item, preventing further child access while retaining its history.
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
  buffer or describing the problem as a parent-login, template or AI failure.
- Submission freezes answers. It becomes `completed` immediately if no parent
  grades are needed, otherwise `awaiting-review`. Neither state accepts answer
  edits, withdrawal or another attempt. Start/resume calls return this saved
  state and never reset it.

### Elapsed activity time

Derive elapsed time from server UTC `StartedAtUtc` and `SubmittedAtUtc` in
assignment details/results; no browser stopwatch or stored duration counter.

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
- **Numeric:** Use the existing invariant decimal grammar: an optional leading
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
  submission. Reports never rerun scoring with a changed rule or live template.
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
  access rather than deleting its history. Releasing a new snapshot or changing
  a template never changes an existing assignment or result.
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

[template]: ../backend/FamilyLearning.Api/TaskEngine/Models/LearningPlan.cs
[content]: ../backend/FamilyLearning.Api/TaskEngine/Models/TaskDocument.cs
[auth-schemes]: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/authorize-with-a-specific-scheme?view=aspnetcore-8.0
[csrf]: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-8.0
