# Product specification

The application implements a content-first activity workflow.
Every subject uses the same AI path; there are no subject-specific generators
or seeded educational records.

```text
Parent prompt → editable learning plan + activity choices
→ applicable material generation → questions → editable draft
→ parent review → immutable ready snapshot

Save template: independently publish the reusable plan at any point
```

## Current workflow

1. Describe the goal, audience and requirements. AI proposes a structured plan
   or asks one clarification. Direct editing and Undo remain available.
2. Review shared settings, material sources, question formats and explicitly
   requested controls. Confirm exact source text extracted from a prompt.
   Fixed and per-activity supplied sources retain their original text.
3. Generate without publishing a template. The server saves a draft checkpoint,
   then runs applicable stages. Supplied sources and question-only plans skip
   material generation. Accepted materials remain available if questions fail.
4. Edit text, answers, options and points, or explicitly regenerate a material,
   question or all questions. Save preserves bounded incomplete drafts and shows
   diagnostics. Generation never silently overwrites later local/server edits.
5. Review the current saved content and answer keys, then mark it ready. Release
   requires current accepted content, matching requirements and complete answers.
   The resulting snapshot is read-only. Copy it to a new draft to make changes.

Saving a template publishes only the reusable plan. It does not save activity
edits, generate content or release a snapshot. Later versions never change
existing drafts or snapshots.

The URL retains the draft and operation for reload during generation. Chat,
unsaved edits and Undo are local. Cancellation preserves saved checkpoints.
An unknown provider outcome is terminal and is not retried automatically.
A lost start response can be checked with the original idempotency key.
A stale save returns 409 and preserves local input for explicit reconciliation.

The library separates templates, editable drafts and frozen snapshots. Deleting
one does not delete independent content copied from it. Deleting a draft removes
its operation history. Confirmed family reset removes all owned learning records,
including items beyond list limits, while retaining accounts and AI configuration.
All reads and writes enforce ownership on the server.

## Contracts

- **LearningPlan:** goal, guidance, shared defaults, scoped controls, up to four
  material definitions and question requirements.
- **TaskTemplate / TaskTemplateVersion:** family-owned reusable identity and
  immutable plan versions.
- **ActivityDraft:** mutable saved plan, input and editable document with a
  concurrency revision. Content identity, provenance and acceptance are server-owned.
- **GenerationOperation:** durable idempotent start, stage checkpoints, safe
  failure evidence and cancellation/recovery state.
- **TaskSnapshot:** immutable reviewed plan, resolved input, content and answer
  keys, independent of later template or draft deletion.

The [plan contract][template] and [document contract][content] define stored
content. Engine/schema versions are independent of template and draft revisions.

Shared settings are topic and audience (required, up to 200 characters),
difficulty (`easy`, `medium` or `hard`, relative to the audience), and a
question count from 1 to 20. Per-activity settings take precedence over plan
defaults. Feasibility and total content limits also bound the requested count.

Controls support text, integer, select and boolean values in plan, material or
question scope. They have application-owned stable IDs and human-readable
meaning. AI adds them only for explicitly requested per-activity choices.
Fixed requirements stay in guidance. Omitted values resolve defaults; false,
zero and explicit empty optional text remain distinct. Required values, types,
bounds and options are validated before AI calls.

Material sources are generated, fixed verbatim text, or text supplied per
activity. Length requirements apply only to generated bodies: a target is
advisory; exact and range constraints are strict. Counted words contain a Unicode
letter or digit; punctuation-only tokens are excluded. Body headings count;
material titles, task instructions, questions and answers do not. There is no
length control for question-only activities. A failed strict material stage
cannot trigger question generation or release.

Questions support numeric, short-text and single-choice answers. Each has an
application ID, prompt, typed interaction, answer and integer points. Numeric
answers use bounded invariant decimal text; choice answers exactly match an
option. Replacing a question replaces its complete answer-bearing unit.
Changing its requirements or source can make content stale; explicit editing
or adoption is required. Validators do not judge educational truth.

For fixed question formats, multiple formats mean a mixture containing each at
least once; the total must be feasible and `defaultFormat` is null. Selectable
format means one allowed format for the whole activity, with an allowed default.
Exact per-format quotas are unsupported and require clarification rather than
silent approximation. Choice count is applicable only to single-choice questions.

Length requirements use either individual generated bodies or their combined
total, never overlapping scopes. Exact/target values may expose an explicitly
requested bounded input; ranges have fixed inclusive endpoints. Input bounds do
not become an output tolerance. Supplied text transformations need a separate
generated material, preserving the original source.

Omission resolves defaults once; explicit null, numeric strings, unknown IDs,
inapplicable fields and fixed overrides fail validation. Optional empty text,
false and zero survive unchanged. Materials and controls have distinct app-owned
identities: renaming retains identity; new AI proposals use null IDs and may not
invent existing identities or alter a retained fixed source through authoring.

Authoring permits one proposal or one focused clarification per submitted parent
message, with visible assumptions reflected in the plan. Each request is bounded
to a 4,000-character message and six unresolved turns / 12,000 characters; this
is not a lifetime conversation limit. A full pending context requires explicit
consolidation, not silent truncation. The app computes actual plan changes.

Generated content records its accepted effective input; questions depend on all
materials actually sent. Editing sources invalidates dependencies. Explicit
adoption updates acceptance without rewriting origin or waiving strict checks.
Changing/deleting a choice leaves an invalid answer diagnostic rather than
automatically selecting a replacement. Scoped repair can proceed beside unrelated
incomplete draft fields; release still checks the complete saved document.

Server limits include:

- Plans: 24,000 serialized characters; name/labels 100; goal 500; shared
  guidance 4,000; material/question guidance 1,000.
- Up to 16 controls across all scopes; text values up to 500 characters; select
  controls have 1–20 distinct options, each up to 100 characters.
- Documents: 0–4 materials and 8,000 total text characters; at least one complete
  question is required for release, while bounded incomplete drafts can be saved.
- Title 100; instructions 1,000; material body 4,000; prompt 500; answer/option
  200 characters. Choice questions have 2–6 distinct options; points are 0–100.

## Boundaries

AI proposes content inside application-owned schemas. The engine validates,
resolves and assembles it. Features authorize, persist and manage concurrency.
The parent remains responsible for language, correctness and suitability.

One API process owns the durable worker. It performs one call per applicable
stage without automatic retries or hidden repairs. Expected failures terminate
the operation; accepted checkpoints remain. Queued compatible stages can resume
after restart; a call with no committed outcome becomes unknown.

Parent-only DTOs contain answer keys and must never serve a child client.
Hebrew/RTL screens use native accessible controls and render content as text.
There is no child-delivery button, assignment, scoring or report placeholder.

OpenRouter model/fallback configuration is external to the domain. Logs exclude
prompts, answers, credentials, reasoning and raw provider errors. Automated
verification uses disposable databases and isolated providers. The
[cutover decision](ai.md#design-and-cutover-decision)
accepts editing/recovery benefits alongside measured reading reliability,
cost and latency drawbacks; it does not establish better Hebrew quality.

Implementation details live in [architecture](architecture.md), setup in the
[README](../README.md), and presentation rules in the [UI guide](ui-guide.md).

## Next steps

The current app ends at the parent preview. Add these only as working features:

1. Evaluate correctness, Hebrew quality, schema reliability, latency and cost
   with representative tasks on the configured model.
2. Add family-owned child profiles, expiring device activation and revocable
   access.
3. Add reviewed assignments and a generic child player. A `TaskSession`
   references a saved task and records answers, attempts, timing and
   server-scored results. Define normalization, retry and concurrency rules
   before implementing scoring.
4. Report completed work and per-question results from saved sessions.
5. Add account recovery, shared-parent access and update notices. Establish
   operational backup/restore; packaging and deployment requirements are in the
   [README](../README.md#publish). Add pagination when 100-item lists become limiting.

Offline synchronization and native packaging remain deferred. Child sessions
must stay separate from parent authentication and use answer-free,
assignment-checked responses. Do not expose placeholder controls for unfinished
features.

### Child flow proposal

Not implemented. The agreed first target is a separately activated child device;
shared-browser switching is deferred. The recommended first release completes
one flow: parent creates a child profile, reviews and assigns an existing task,
activates the child's device, and sees the child's submitted result.

Keep the current feature structure and parent API contracts. Add an independently
authorized `/api/child` group when implementing the child flow.

**Access and content:**

- Bind parent and child policies to their respective authentication schemes;
  a child cookie must never satisfy parent access. The child identity, family
  and device grant come from the server, not submitted IDs. Check assignment
  ownership, grant expiry and revocation on each request. See
  [scheme-specific authorization][auth-schemes].
- A parent creates a short-lived, single-use activation credential for one
  child. Store its hash, consume it atomically, rate-limit activation, and issue
  a separate HttpOnly cookie, Secure in production. Keep credentials out of
  logs. Parents can revoke a device; child access needs no email or password.
- Cookie-based child writes and activation need [CSRF protection][csrf], with
  tokens bound to the correct identity. Reject activation in a browser with an
  active parent session; separate route names do not isolate shared cookies.
- Build explicit child DTOs containing only learner-facing text, questions,
  options and points. Never reuse `SnapshotPreview` or send answer keys,
  generation guidance, resolved inputs or model metadata. Generate nothing when
  assigning, opening, answering or reporting on a task.

**Records and completion:**

- Keep `TaskSnapshot` as frozen content. `Child` belongs to a family;
  `Assignment` links one child to one reviewed snapshot. `TaskSession` records
  work on that assignment; its answers and scored result become immutable on
  submission. Device grants are separate from learning sessions.
- Start with one resumable session and one final submission per assignment.
  Save bounded draft answers with concurrency checks. Commit final answers,
  per-question points, totals, scoring-policy version and completion together.
  Repeating the same submission returns the saved result; different answers
  after completion conflict. Withdrawal and submission must check assignment
  state in the same transaction, including concurrent requests.
- Grade deterministically on the server using the frozen answer key. Proposed
  initial rules: match a valid frozen choice; compare invariant decimal values;
  compare short text after trimming and Unicode NFC normalization, preserving
  case, punctuation and Hebrew vowel points. No synonym guessing or AI grading.
  Missing answers earn zero; unknown or duplicate question IDs are invalid.
  Show earned/max points, with no percentage when max points is zero.
- Parent reports read saved results, never rescore history using newer rules.
  Child completion responses may acknowledge submission but contain no answer
  key. Reattempts, detailed child feedback and aggregate dashboards can follow.

**Retention and acceptance:**

- Ordinary deletion must retain tasks referenced by assignments and their
  reports; use archival for those records. Disabling a child revokes access
  while retaining results. Before enabling assignments, extend explicit family
  reset, its confirmation and its transaction to cover learning history. These
  are proposed changes to today's independent draft/snapshot deletion behavior.
- Verify cross-family and sibling isolation, parent/child cookie separation,
  expired/replayed activation, revocation, missing CSRF, answer-free JSON,
  resume/conflict behavior, duplicate submission, withdrawal/deletion races,
  scoring edge cases and unchanged historical results. Test the full flow on
  narrow RTL screens and by keyboard with isolated data and providers.

Finalize grading, retry and retention rules before adding child tables or endpoints.

### Real-model evaluation

Contract tests do not measure educational quality. Before relying on generated
work for children, repeat a small set of representative Hebrew and bilingual
requests across subjects, ages and difficulty levels. Review language,
correctness, age fit, instruction adherence and answer ambiguity; record failures
as well as successes, the model/profile, prompt version, latency, token use and
actual cost when available. Use synthetic requests, never child identities.

Use the [developer evaluation harness](ai.md#using-the-evaluation-harness) for
bounded, explicitly requested live runs and offline comparisons. Keep calibration
health, generated language findings and human scores separate. It creates no
learning records and never corrects app output. Human review remains authoritative.

[template]: ../backend/FamilyLearning.Api/TaskEngine/Models/LearningPlan.cs
[content]: ../backend/FamilyLearning.Api/TaskEngine/Models/TaskDocument.cs
[auth-schemes]: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/authorize-with-a-specific-scheme?view=aspnetcore-8.0
[csrf]: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-8.0
