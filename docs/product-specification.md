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
3. Generate without publishing a template. The server saves a draft checkpoint,
   then runs applicable stages; supplied sources and question-only plans skip
   material generation, and accepted materials survive a question failure.
4. Edit text, answers, options and points, or regenerate one material, one
   question or all questions. Incomplete drafts save with diagnostics, and
   generation never silently overwrites later edits.
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
count from 1 to 20. Per-activity settings override template defaults; inside an
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
only to single-choice questions. A calculation is a whole prompt, option or
answer; inside sentences, including texts and instructions, operations are
written in words, so everything displays in order.

Authoring gives one proposal or one focused clarification per parent message,
with assumptions reflected in the plan. A request carries up to 4,000
characters plus six unresolved turns or 12,000 characters; a full context needs
explicit consolidation, never silent truncation. The app computes the actual
plan changes.

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

AI proposes content inside application-owned schemas; the engine validates,
resolves and assembles it, and features authorize, persist and manage
concurrency. The parent remains responsible for language, correctness and
suitability. Parent DTOs contain answer keys and must never serve a child
client. Screens are Hebrew/RTL with native accessible controls and render
content as text, with no child-delivery, assignment, scoring or report
placeholders. The [cutover decision](ai.md#design-and-cutover-decision) traded
measured reliability, cost and latency for editing and recovery; it does not
establish better Hebrew. Implementation is in [architecture](architecture.md),
setup in the [README](../README.md) and presentation in the
[UI guide](ui-guide.md).

## Next steps

The implemented app ends at the parent preview. The next milestone is the
child flow specified below; its [implementation plan](child-flow-plan.md)
keeps the work in reviewed, independently verified tasks. Neither this section
nor the plan means the child features have shipped.

Alongside that work, review representative saved evaluation outputs for
correctness, Hebrew, suitability and answer quality before handing the
activities to children. Use concrete findings to guide further AI tuning.
Before inviting other families, add account recovery and tested backup/restore.
Shared-parent onboarding, dashboards, broad library pagination, update notices,
offline synchronization and native packaging remain later work.

Deferred maintenance from the live-update work:

- Correct shared 502–504 feedback to use general server messages unless the
  response identifies an AI-specific problem.
- Review the `braces` advisory (GHSA-vfj7-8cjw-p6xm) in the Markdown lint tool's
  development dependencies before choosing a dependency change.

## Child flow — next milestone

**Design for review, 3 October 2026; not implemented.** This milestone completes
one loop: the parent creates a child profile, assigns a reviewed activity,
activates a separate child device and sees the submitted answers and results.
It extends the current application and immutable snapshots. Creating a profile,
assigning, opening, answering, grading and reporting make no AI calls.

The first release uses a separate browser/device for the child. Shared-browser
parent/child switching, self-registration, repeated attempts on an assignment,
AI grading, hints, answer-key delivery and gamification are outside this
milestone. All existing question types remain available.

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
- Keep withdrawn assignments and submitted results. Disabling a child revokes
  access rather than deleting its history. Releasing a new snapshot or changing
  a template never changes an existing assignment or result.
- The explicit family reset remains the destructive exception. Its confirmation
  must name children, device access, assignments, answers, grades and content.
  Delete all of them in one transaction, including records beyond list limits;
  keep the family, parent accounts and AI configuration. After reset, old child
  cookies and activation codes cannot access or recreate deleted work. Update
  reset before exposing any child records, and extend it in each schema task.

### Acceptance

The milestone is ready for a family pilot when the parent can assign reviewed
work, activate a separate device, the child can save/resume/submit, and the
parent can review short text and see stable results after a server restart.
Verify these with the real application and disposable data:

- Family/sibling isolation; parent/child cookie separation; mixed-cookie
  activation/sign-in rejection; missing and wrong-identity CSRF; anonymous
  endpoint allowlist.
- Expired, replayed and concurrently redeemed activation; grant expiry,
  browser restart, revocation and disable/re-enable; cancellation and lost
  activation response.
- Complete child-response field allowlists, including nested content, errors,
  working sessions, pending review and completed history; no AI calls.
- Save conflicts, two tabs/devices, start races, reordered submission replay,
  missing/null/blank answers, exact numeric comparison at precision boundaries,
  and a lost submission acknowledgement.
- Submission versus withdrawal/revocation/reset; assignment versus deletion;
  concurrent parent grading; no partial writes or changed historical scores.
- Pagination past 100 records; archived-content access through assignments;
  restart persistence; reset clears every owned child record and no other family.
- Keyboard and screen-reader labels, focus after errors, RTL/mixed-language
  content, narrow screens and enlarged text; existing parent workflows pass.

[template]: ../backend/FamilyLearning.Api/TaskEngine/Models/LearningPlan.cs
[content]: ../backend/FamilyLearning.Api/TaskEngine/Models/TaskDocument.cs
[auth-schemes]: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/authorize-with-a-specific-scheme?view=aspnetcore-8.0
[csrf]: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-8.0
