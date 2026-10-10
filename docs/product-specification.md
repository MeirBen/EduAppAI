# Product specification

Parents create learning activities by describing them in chat; each activity
saves itself as a draft and becomes a frozen, assignable snapshot only on
explicit approval. Children complete assigned work on their own devices and
parents grade it. Every subject uses the same generic AI path; there are no
subject-specific generators or seeded educational records.

**Describe an activity → it becomes a draft → create → review and edit → approve.**

The [architecture](architecture.md) describes the implementation, the
[chat design](activity-chat-design.md) the AI execution, the
[UI guide](ui-guide.md) the presentation and the [AI guide](ai.md) model
configuration and evidence.

## Activity lifecycle

1. **New activity:** describe the goal, audience and requirements in chat. AI
   proposes requirements or asks one focused clarification. Show a concise
   summary, collect required source text and confirm AI-extracted sources.
2. **Draft:** once requirements and source inputs are valid, the activity and
   its chat become a draft by themselves, before any generation. A draft may
   have missing texts/questions or unresolved content diagnostics; saving makes
   no AI call. Every later valid change saves itself after a short pause.
   Setup before this point uses the navigation guard. While the draft has no
   questions or generated text, chat updates its requirements without
   generating content; Create stays explicit.
3. **Create:** one parent-started operation generates the needed texts and
   questions and applies the complete validated result atomically; a failed or
   cancelled operation leaves the saved draft unchanged.
4. **Refine or resume:** read the activity with chat beside it. Chat changes
   structure and requirements; explicit Edit opens content fields, whose edits
   save themselves. Starting AI first saves any pending edit, and editing pauses
   while it runs. The parent's own texts come only from setup; chat never adds
   one. Saved drafts reopen with their chat, status and available undo.
5. **Approve:** review the saved content and answer keys, then explicitly mark
   the activity ready. Approval validates and freezes the saved revision, makes
   no AI call and never assigns work. Ready activities are read-only and can be
   assigned; editing one starts a new draft from its snapshot, leaving existing
   assignments and results unchanged.

The library has **drafts** and **ready activities**, with one New activity entry
point. There are no templates or reusable definitions: a learning plan belongs
to its activity.

### Workspace and changes

The activity is a readable canvas, with chat beside it on desktop and as a
sheet over it on phones, in Hebrew/RTL with accessible native controls and one
state for reading and editing. Titles, instructions, wording, options, answers
and points stay manually editable; adding, removing or reordering content and
changing requirements belong to chat, which can also rewrite the title or the
learner instructions on request, such as removing their vowel marks; a lasting
rule for those two fields regenerates no content. Settings are a summary, not a
form.

- Answers, clarifications, refusals and no-ops change no activity content.
- Adding questions, optionally with a focused instruction, generates only the
  additions and updates the question count atomically. Removal and reorder keep
  the surviving questions' content and make no content-generation call.
- Broader changes regenerate dependent content. The server derives scope from
  effective requirements and explicit edits; the model cannot suppress needed
  dependent work, and text changes never silently leave old questions current.
- After manual source/text edits, offer to update questions or confirm that
  they still fit. Confirmation makes no AI call and must pass validation.
- One-level undo covers the last successful chat change or question update,
  including structure changes, but not initial creation. It restores plan and
  document together under a new revision; a saved manual edit, adoption and
  approval clear it.

### Saving and recovery

One AI operation at a time is allowed per draft. Saves, adoption, undo and
approval are blocked while it runs, with server revision checks for races and
other tabs. Intermediate AI results stay in operation evidence; only final
success changes saved content. Stop commits cancellation before stopping
transport; a completion that already committed stays successful. Unknown
outcomes are never retried automatically. Reusing the operation key checks a
lost start response; an uncertain save or approval is checked against saved
state. Conflicts keep local edits and pause autosave until the saved version is
loaded.

An uncertain first draft save keeps setup paused until the parent checks its
saved state. Recovery keeps the original request and draft identity, so it
cannot silently discard a later requirement change or create a duplicate.
A definitely rejected setup remains editable.

The URL identifies the saved draft and operation. Chat and undo are parent-only
draft state; invalid field edits stay local until fixed. Another device's save
or deletion is announced without silently replacing the current buffer. Draft
deletion removes its operation history and chat, and late AI output cannot
recreate it. Library deletion and family reset follow the
[retention rules](#retention-and-reset). The server enforces ownership on every
read and write.

## Contracts

- **LearningPlan:** the activity's concrete settings, goal/guidance, up to four
  material definitions and question requirements ([contract][plan]); it has no
  identity or publication of its own.
- **ActivityDraft:** the mutable saved plan and editable [document][content]
  with a concurrency revision; content identity, provenance and acceptance are
  server-owned.
- **GenerationOperation:** durable idempotent start, stage checkpoints, safe
  failure evidence and cancellation/recovery state.
- **TaskSnapshot:** the immutable reviewed plan, content and answer keys of a
  ready activity, independent of later draft edits or deletion.

Draft and ready are the visible activity states; generation status is
operational, not another product type.

Shared settings are topic and audience (required, up to 200 characters),
difficulty (`easy`, `medium` or `hard`, relative to the audience) and a question
count from 1 to 20. A request that names no difficulty defaults to `easy`
through third grade and `medium` above, disclosed as an assumption. The plan and
the displayed count always agree; feasibility and the total content limit also
bound the count. Requirements are concrete values for this activity, not
adjustable defaults.

`settings` hold concrete values; materials hold their source, guidance and
length; questions hold formats, choice count and guidance. Extra subject
requirements belong to scoped guidance. `documentGuidance` holds lasting rules
for only the learner title and instructions, such as niqqud or tone: changing it
rewrites those two fields and regenerates nothing, while a rule about texts,
questions or answers belongs in their guidance and rebuilds what depends on it.
The AI chooses where a request belongs; the server derives what each field
rebuilds. The plan has no controls, selectable
defaults, flags or override maps, and no separately stored input: the server
derives typed stage inputs and fingerprints from the saved plan.

`name` is the parent's label and the document `title` the learner heading; the
library lists a draft by its title once content has one. A material has an ID,
label, source, guidance, optional length and source text: text is required for
`supplied` and null for `generated`. Question requirements hold the allowed
formats, guidance and one concrete choice count (2–6 when single-choice is
included, otherwise null).

Types and bounds are validated before AI calls; unknown fields, IDs and
inapplicable values are rejected, and null appears only where the contract
allows it. Renaming keeps material identity. AI proposals use null IDs for new
items and may not invent identities or alter a supplied source.

Supplied text belongs to this activity and stays verbatim; transforming it
creates a separate generated material. Length requirements apply only to
generated bodies, per material or as one total, never both: an approximate
target is advisory, while an inclusive range whose minimum is below its maximum
is strict. Exact word counts are not offered, since generation cannot meet them
reliably; a request for one becomes a target. Counted words contain a Unicode
letter or digit; body headings count, titles, instructions, questions and
answers do not. A strict total that would have to be split across several AI
calls is clarified instead: per-text ranges or an approximate total, with the
saved draft unchanged until the parent chooses. A failed strict material blocks
questions and release.

A new generated text first gets five ideas with distinct causal or explanatory
structures; the server picks the one least overlapping bounded recent family
content, drawing among ties, and checkpoints that choice before writing.
Questions also receive bounded previous prompts to vary their targets. Topic,
source fidelity and prescribed practice outrank novelty; variety is not
guaranteed. Improving one text follows the parent's instruction within the
requirements; changing topic or requirements goes through chat and updates
dependent content. Hebrew is written without niqqud, except full niqqud for
beginning readers up to second grade or on request; a plan records niqqud only
when the parent asks. Prose is split into paragraphs of a few sentences; poems
and dialogue keep their lines.

Questions are numeric, short-text or single-choice, each with an application
ID, prompt, typed interaction, answer and integer points. Numeric answers use
invariant decimal text; choice answers exactly match an option. Several selected
formats mean a mixture containing each at least once; one format applies to all
questions. Exact per-format quotas need clarification, and choice count applies
only to single-choice questions. A calculation or comparison is a whole prompt,
option or answer; inside sentences, including texts and instructions,
operations and relations are written in words, so everything displays in order
and no `<` or `>` sits beside Hebrew words, where it would display reversed.
Surrounding whitespace in generated question text is removed before validation;
nothing else in a response is repaired.

Authoring gives one proposal or one focused clarification per parent message,
with assumptions reflected in the plan. The chat is one conversation from the
first message: each request carries the message (up to 4,000 characters) and the
newest turns within six turns and 12,000 characters. Older turns stay visible
but leave the context, so a lasting requirement belongs in the plan, which every
request sends whole; before the first plan the conversation is the only record.
Required sources and constraints are never truncated. The server writes every
assistant reply and computes the actual plan changes.

For math, explicit operand/result ranges, operations, fractions, remainders and
precision take precedence over inferred teaching choices. Grade and difficulty
guide problem selection without inventing single-digit limits. An unqualified
elementary "up to N" bounds given numbers and results, disclosed as an editable
assumption. Incompatible requirements need clarification. Reading passages and
math scenarios shared across questions are materials; short independent word
problems keep their givens in the prompt, by the source's role rather than the
subject. Questions name the quantity and unit sought and use an answer format
that can express it: numeric input holds one finite decimal, not a fraction
expression or a quotient/remainder pair.

Generated content records its accepted effective input, and questions depend on
every material sent with them. Editing a source or requirement makes dependent
content stale until it is edited or explicitly adopted; adoption never rewrites
its origin or waives strict checks. Replacing a supplied source stales the
questions and every generated text. Changing a choice leaves an answer
diagnostic instead of picking a replacement. Missing or stale dependencies
outside an operation's scope need explicit repair or adoption before content
calls; approval checks the whole saved document. Validators never judge
educational truth.

Server limits:

- Plans: 24,000 serialized characters; names and labels 100; goal 500; shared
  guidance 4,000; material and question guidance 1,000.
- Questions: 1–20 per activity; strict AI schemas carry the exact count up to a
  configured endpoint limit ([AI guide](ai.md#strict-schema-contract)).
- Documents: 0–4 materials and 8,000 total characters; release needs at least
  one complete question.
- Title 100; instructions 1,000; material body 4,000; prompt 500; answer and
  option 200. Choice questions have 2–6 distinct options; points are 0–100.
- Lists: 25 items per page by default, at most 100, newest first with an
  identifier tie-break, so every activity, assignment and report stays
  reachable.

## Boundaries

AI proposes schema-constrained content; the engine validates, resolves and
assembles it; features own authorization, persistence and concurrency. Parents
review language, correctness and suitability. Parent answer-bearing DTOs never
serve children. Screens render plain text with native accessible controls and
real workflows, not placeholders.

## Child flow

Children use their own browser or device. Assignments use immutable reviewed
snapshots, and child management and learning make no AI calls. Shared-browser
switching, self-registration, repeated attempts, AI grading, hints, answer-key
delivery and gamification are out of scope.

### Profiles and device access

- A child belongs to one family and has a trimmed, nonblank display name of
  1–100 characters; names need not be unique, and no email, password or date of
  birth is required. Parents can rename, disable and re-enable their children.
  Disabling revokes all device access and pending activation codes while
  keeping work; re-enabling needs fresh activation and never restores a grant.
- The parent creates an activation code for a named device (trimmed, nonblank,
  1–100 characters). Following RFC 8628 user codes, it is eight
  cryptographically random letters from the vowel-free alphabet
  `BCDFGHJKLMNPQRSTVWXZ`, shown as `XXXX-XXXX` and read regardless of case,
  spaces or dashes. Ten-minute expiry, single use and the redemption rate limits
  make guessing impractical; the stored SHA-256 hash keeps codes out of the
  database but does not resist offline guessing of so short a code. A code is
  displayed once and sent in the request body, never a URL, log or browser
  storage. A new code invalidates the child's previous unconsumed one. After a
  lost activation response, check for a child session first; otherwise request
  a new code rather than replaying the consumed one.
- Redemption consumes the code and creates its grant in one transaction, so
  concurrent redemption succeeds once. Invalid, expired and used codes give the
  same response. Redemption is limited to ten attempts per minute per source IP
  and 120 per minute for the process, without queuing; issuance to ten per
  minute per parent.
- A grant lasts 30 days from activation without sliding renewal. The child
  cookie is HttpOnly, SameSite Strict, Secure in production, survives browser
  restarts and expires with the grant; clearing cookies needs fresh activation.
  The parent can revoke individual devices, and disconnecting on the child
  device revokes its grant.
- The installed app remembers the last successful parent or child entry. Lost
  child access returns to activation inside the app. If that preference and
  both sessions are absent, the app offers child activation and parent login.
  The preference grants no access and does not switch an active identity.
- Parent and child policies use separate named
  [authentication schemes][auth-schemes]: neither cookie authorizes the other's
  APIs. Activation is refused with an active parent or child session, and parent
  sign-in with an active child session, naming the session to close; identities
  are never merged or replaced silently.
- Child identity, family and grant are server-owned. Every child request checks
  that profile and grant are still valid, and writes recheck them inside the
  transaction that changes state, so a revocation committed first stops a later
  save or submission even after authentication passed.
- Child activation and writes use [CSRF protection][csrf], with a fresh
  identity-bound token after activation. Token issuance refuses an active
  opposite-mode session rather than replacing its token. A transient outage is
  not an expired session: show retry feedback without claiming that
  activation, saving or submission succeeded.

### Optional grade and age

Parents may record **כיתה** (school grade) and **גיל** (completed years).

- Both start unset and can be cleared independently; neither implies the other
  or learning ability. Grade is trimmed free text up to 100 characters; age is
  an integer from 0 to 120. The API publishes these bounds.
- Entering or changing age records the server UTC date; unrelated edits keep
  it and clearing age clears it. Age never increments automatically and needs no
  birthday or confirmation. Details are parent-only.
- Create/update accepts optional `details` with required nullable `grade` and
  `age`; omitting it on update keeps stored details. Edits use the profile
  revision, and every workflow works without these fields.
- Profile changes never rewrite saved content, assignments, answers or grades,
  or trigger generation. Prefilling an audience from grade or age is future
  work: it needs an explicit parent action and review, sends only the selected
  educational context, never names or profile IDs, and informs vocabulary,
  reading level and difficulty without certifying suitability.

### Profile and device cleanup

- Profiles show creation and last-update UTC times; secondary times, including
  when age last changed, sit in a collapsed “פרטי זמנים” disclosure.
- A profile can be deleted only without assignments, including withdrawn ones.
  Deletion checks ownership, revision and history in one transaction and removes
  device grants and activation codes; concurrent assignment cannot lose history.
  Profiles with history can be disabled.
- Active devices offer revocation; revoked or expired entries can then be
  removed. The server rechecks ownership and inactivity, and removal never
  deletes assignments, answers or results.

### Assignments and learner-facing content

- An assignment links one family-owned child to one family-owned reviewed
  `TaskSnapshot`. Creation rejects disabled children and archived snapshots;
  one assignment per child/snapshot pair is allowed, and replaying creation
  returns it, even when withdrawn, so a delayed duplicate never undoes a later
  withdrawal. A deliberate repeat needs a new reviewed snapshot.
- Assignment state is `assigned`, `withdrawn`, `awaiting-review` or
  `completed`; starting or saving work keeps it `assigned`. The parent may
  withdraw only `assigned` work, keeping its history. An explicit restore at
  the current revision undoes a withdrawal, with its saved session, under the
  same eligibility as creation; repeating either request is harmless and a
  stale one conflicts. Restore acts only on withdrawn work and submission only
  on assigned work, so the two never both succeed; withdrawal and submission are
  serialized. There are no due dates or automatic expiry.
- The child inbox separates available from submitted work.
- Child responses explicitly project the title, instructions, material IDs,
  titles and bodies, question IDs, prompts, interaction types, options and
  possible points, plus only the child's own saved answers and status. A child
  never receives `TaskDocument` or `SnapshotPreview`: answer keys, guidance,
  resolved inputs, provenance and model metadata stay server/parent-only on
  every success and error path, including completion and history.
- Every read and write enforces ownership. Missing, foreign-family and
  sibling-owned IDs return the same 404. Requests cannot choose a child, family,
  score or question definition. Authenticated responses are not cached, and
  learning data stays out of browser storage and the service-worker cache.

### Working session and submission

- Each assignment has one resumable `TaskSession`, created explicitly and
  idempotently on start; read-only endpoints never create work. Server UTC
  start, last-save and submission times describe elapsed wall-clock time, not
  engagement.
- The child edits a local buffer and saves explicitly, seeing saving, saved,
  unsaved and failed states. Refresh restores the last acknowledged checkpoint,
  leaving with unsaved edits warns, and there is no background saving or
  offline queue. Reading material stays accessible while answering, with native
  labeled controls, Hebrew/RTL, keyboard access, 360px width and 200% text.
- A save sends the complete answer collection and `expectedRevision`; an
  acknowledged write advances the revision. The collection is bounded by the
  snapshot's question count (at most 20) and each value by 200 characters.
  Collection, revision and each entry's question ID and string value are
  required; nulls, duplicate or unknown IDs and unknown JSON members are
  invalid. An omitted question or a blank value means unanswered; nonblank text
  is kept exactly. Incomplete numeric text may be saved, invalid choice values
  may not, and length/count limits apply first.
- A stale write returns 409 and keeps the child's text, with an explicit reload
  of saved work. After a lost response, check saved state before another write;
  a submission is never replayed automatically or reported as failed.
  Navigation cancels client transport, not a committed save.
- Submission is explicit, confirming when answers are missing, and sends the
  final complete collection and expected revision without needing a prior save.
  Validation, answer freezing, scoring, timestamps and the state change commit
  together.
- Submitting identical answers again returns the saved outcome even with an old
  revision; different answers after submission return 409. Answers compare by
  question ID regardless of order, blank as unanswered, otherwise exactly. A
  revision mismatch before submission stays a conflict.
- Withdrawn work returns 410 and stops editing with an explicit message; lost
  access asks for activation, missing work returns 404, and temporary failures
  offer a saved-state check without discarding the buffer or blaming the parent
  login or AI.
- Submission freezes answers: work becomes `completed` when no parent grades
  are needed, otherwise `awaiting-review`. Neither state accepts edits,
  withdrawal or another attempt, and start/resume return it unchanged.

### Elapsed activity time

Elapsed time derives from server UTC `StartedAtUtc` and `SubmittedAtUtc`, with
no browser stopwatch or stored duration. While work is open the player shows the
same interval from `StartedAtUtc` and the device clock, for display only; a
device clock behind the server reads as zero.

- The first explicit start begins the interval; previews, reopening, refreshing
  and resuming never reset it. Submission freezes it, and retries, later visits
  and grading never extend it.
- The duration is labelled as including breaks, closed tabs and offline gaps.
  Before submission show the start time and an unsubmitted state; missing or
  inconsistent timestamps show an unavailable duration, never a guess. Copy and
  units are in the [UI guide](ui-guide.md#parent-learning-management).
- Elapsed time never changes grades or infers ability. Engagement tracking,
  countdowns and time limits are future decisions.

### Scoring and parent review

Choices and numbers score automatically; the parent reviews **every nonblank
short-text answer**, so valid alternative wording is never silently marked
wrong. There is no AI grading or synonym matching.

At submission:

- **Unanswered, any type:** zero points, no pending grade.
- **Single choice:** one frozen option; matching the key earns full points,
  another option zero.
- **Numeric:** sent without surrounding spaces, in the invariant decimal
  grammar: an optional sign, digits and an optional decimal point followed by
  digits, with no commas, exponent, NaN or overflow. Values compare exactly,
  ignoring leading integer zeros, trailing fractional zeros and the sign of
  zero, without rounding: `+02.00` equals `2`, a tiny nonzero fraction does not
  equal `0`. Equal values earn full points, another valid number zero, and
  invalid nonblank input blocks submission.
- **Short text:** kept exactly; points stay unset until parent review, even when
  it matches the key or is worth zero points.

Results and review:

- Submission stores the scoring-policy version and automatic awards; reports
  never rescore against changed rules or draft content. A pending result shows
  the automatic subtotal, possible total and outstanding review count, without a
  final total or percentage.
- The parent reviews each submitted text beside the frozen question, source
  material and expected answer, which is a reference rather than a required
  string, and enters integer points from zero to the question's possible
  points; partial credit is allowed and automatic awards are read-only.
- One request finalizes all pending grades with the session revision. Each
  pending question appears exactly once; missing, duplicate, automatic or
  out-of-range grades are rejected. Reviewer, UTC review time, grades, final
  result and `completed` state save in one transaction.
- Completed results never change: repeating the same grades returns them,
  different grades conflict, and automatically completed work never gains a
  review. Grade drafts stay local until finalization, with an unsaved-work
  warning. Corrections, reopening and written feedback are future features.
- Parent reports show the child, activity, answers, frozen keys, automatic and
  manual awards and timestamps; final totals appear only once grading is
  complete, without a percentage when possible points are zero. The child's
  history shows receipt, review status and the final total when available,
  without keys or per-question correctness.

### Retention and reset

- `TaskSnapshot` content stays immutable. Removing an assigned snapshot from
  the library archives it: it takes no new assignments, while existing work and
  reports keep it. Unassigned snapshots are deleted. The confirmation names
  archiving or deletion accurately; assignment and removal share
  transaction-safe checks and restrictive foreign keys, so a race cannot leave
  dangling work.
- Withdrawn assignments and submitted results are kept; the parent sees
  withdrawn work only under its own filter. Disabling a child revokes access
  without deleting history, and approving a new activity never changes an
  existing assignment or result.
- The explicit family reset is the one destructive exception. Its confirmation
  names children, device access, assignments, answers, grades and content, all
  deleted in one transaction, while the family, parent accounts and AI
  configuration stay. Old child cookies and activation codes then cannot reach
  or recreate deleted work. Reset must cover every family-owned learning record
  a schema change adds.

### Acceptance

Verify the complete assign → activate → save/resume → submit → review loop on
the real application with disposable data, including after restart. The
[test coverage](architecture.md#tests) maps these requirements to suites:

- Family/sibling and parent/child scheme isolation, mixed-cookie rejection,
  anonymous endpoint allowlists and missing or wrong-identity CSRF.
- Activation expiry, replay and concurrent redemption, grant expiry,
  revocation, disable/re-enable, cancellation and lost-response recovery.
- Exact nested child-response allowlists on success and error and in
  working/pending/completed states; zero child-flow AI calls.
- Start/save conflicts across tabs and devices, malformed and blank answers,
  numeric precision boundaries, reordered submission replay and lost
  acknowledgements.
- Submission, withdrawal, revocation, reset and assignment/deletion races,
  concurrent grading, atomic writes and immutable historical scores.
- Paging, archive access, restart persistence and complete reset without
  affecting another family.
- Keyboard and screen-reader labels, error focus, RTL and mixed-language
  content, narrow screens and enlarged text per the [UI checks](ui-guide.md#check).

## Next steps

- Before giving generated activities to children, review representative ones
  with the family for accuracy, age suitability and answer quality; software
  checks and isolated tests do not establish this.
- Before inviting other families, add account recovery, tested backup/restore
  and per-family AI spending quotas; until then the OpenRouter key limit bounds
  spend.
- Shared-parent onboarding, dashboards, offline synchronization and native
  packaging are deferred.
- Shared 502–504 errors should use general server feedback unless the response
  identifies an AI-specific problem.
- Recheck the Markdown lint development advisories (`braces`, `katex`,
  `smol-toml`, all through `markdownlint-cli2`) on its next release: `braces`
  has no fix, the `katex` fix is outside its declared range, and the repository
  has no TOML configuration for `smol-toml` to parse.

[plan]: ../backend/FamilyLearning.Api/TaskEngine/Models/LearningPlan.cs
[content]: ../backend/FamilyLearning.Api/TaskEngine/Models/TaskDocument.cs
[auth-schemes]: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/authorize-with-a-specific-scheme?view=aspnetcore-8.0
[csrf]: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-8.0
