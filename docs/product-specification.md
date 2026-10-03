# Product specification

The application implements a content-first activity workflow. Every subject uses
the same AI path; there are no subject-specific generators or seeded
educational records.

```text
Parent prompt → editable learning plan + activity choices
→ applicable material generation → questions → editable draft
→ parent review → immutable ready snapshot

Save template: independently publish the reusable plan at any point
```

## Current workflow

1. Describe the goal, audience and requirements. AI proposes a structured plan
   or asks one clarification; direct editing and Undo remain available.
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
existing drafts or snapshots.

The URL keeps the draft and operation for reload during generation; chat,
unsaved edits and Undo stay local. Cancellation preserves saved checkpoints. An
unknown provider outcome is terminal and never retried automatically, a lost
start response can be checked with its idempotency key, and a stale save returns
409 while preserving local input.

The library separates templates, drafts and snapshots, and deleting one keeps
content copied from it. Deleting a draft removes its operation history. A
confirmed family reset removes all owned learning records, including items
beyond list limits, keeping accounts and AI configuration. The server enforces
ownership on every read and write.

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
count from 1 to 20. Per-activity settings override plan defaults, and
feasibility and total content limits also bound the count.

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
material or as a combined total, never both: a target is advisory, while exact
counts and inclusive ranges are strict. Counted words contain a Unicode letter
or digit; body headings count, but titles, instructions, questions and answers
do not. An explicitly requested bounded input never becomes an output tolerance.
Transforming supplied text needs a separate generated material that preserves
the original. A failed strict material stage blocks questions and release.

Questions are numeric, short-text or single-choice, each with an application
ID, prompt, typed interaction, answer and integer points. Numeric answers use
invariant decimal text; choice answers exactly match an option. A fixed set of
formats means a mixture containing each at least once; a selectable format
means one format for the activity, with an allowed default. Exact per-format
quotas need clarification rather than approximation, and choice count applies
only to single-choice questions.

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

The app ends at the parent preview. Add these only as working features:

1. Evaluate correctness, Hebrew quality, reliability, latency and cost with
   representative tasks and human review, using the
   [evaluation harness](ai.md#using-the-evaluation-harness).
2. Add family-owned child profiles, expiring device activation and revocable
   access.
3. Add reviewed assignments and a generic child player; a `TaskSession`
   records answers, attempts, timing and server-scored results.
4. Report completed work and per-question results from saved sessions.
5. Add account recovery, shared-parent access, update notices, operational
   backup and restore, and pagination once 100-item lists limit use.

Offline synchronization and native packaging remain deferred. Child sessions
must stay separate from parent authentication and use answer-free,
assignment-checked responses.

### Child flow proposal

Not implemented. The first target is a separately activated child device;
shared-browser switching is deferred. The first release completes one flow: the
parent creates a child profile, assigns a reviewed task, activates the child's
device and sees the submitted result. It adds an independently authorized
`/api/child` group and keeps the parent contracts unchanged.

**Access and content:**

- Bind parent and child policies to separate authentication schemes so a child
  cookie never satisfies parent access ([scheme-specific
  authorization][auth-schemes]). Child identity, family and device grant come
  from the server; check assignment ownership, expiry and revocation on every
  request.
- A parent creates a short-lived, single-use activation credential stored as a
  hash, consumed atomically and rate-limited. It yields a separate HttpOnly
  cookie, Secure in production, and stays out of logs. Parents can revoke a
  device; children need no email or password.
- Child writes and activation need [CSRF protection][csrf] bound to the right
  identity. Reject activation in a browser with an active parent session.
- Child DTOs contain only learner-facing text, questions, options and points:
  never `SnapshotPreview`, answer keys, guidance, resolved inputs or model
  metadata. Assigning, opening, answering and reporting generate nothing.

**Records and completion:**

- `TaskSnapshot` stays frozen content. `Child` belongs to a family,
  `Assignment` links one child to one reviewed snapshot, and `TaskSession`
  records work on it; answers and the scored result become immutable on
  submission. Device grants are separate from sessions.
- Start with one resumable session and one final submission per assignment.
  Draft answers save with concurrency checks; final answers, per-question
  points, totals, scoring-policy version and completion commit together.
  Repeating a submission returns the saved result, different answers after
  completion conflict, and withdrawal and submission check assignment state in
  one transaction.
- Grade deterministically on the server from the frozen key: match a valid
  choice, compare invariant decimals, and compare short text after trimming and
  Unicode NFC normalization, preserving case, punctuation and vowel points. No
  synonym guessing or AI grading. Missing answers score zero, unknown or
  duplicate question IDs are invalid, and no percentage shows when max points is
  zero.
- Reports read saved results and never rescore with newer rules. Completion
  responses contain no answer key. Reattempts, detailed feedback and dashboards
  can follow.

**Retention and acceptance:**

- Archive, rather than delete, tasks referenced by assignments and their
  reports. Disabling a child revokes access but keeps results. Extend family
  reset, its confirmation and its transaction to learning history before
  enabling assignments.
- Verify family and sibling isolation, cookie separation, expired or replayed
  activation, revocation, missing CSRF, answer-free JSON, resume and conflict
  handling, duplicate submission, withdrawal and deletion races, scoring edge
  cases and unchanged history, on narrow RTL screens and by keyboard.

Finalize grading, retry and retention rules before adding child tables or
endpoints.

[template]: ../backend/FamilyLearning.Api/TaskEngine/Models/LearningPlan.cs
[content]: ../backend/FamilyLearning.Api/TaskEngine/Models/TaskDocument.cs
[auth-schemes]: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/authorize-with-a-specific-scheme?view=aspnetcore-8.0
[csrf]: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-8.0
