# Product specification

Templates are AI-authored instructions and configurable fields, never
subject-specific generators. Every subject follows the same flow, starting from
an empty library:

```text
Parent prompt → AI proposal → parent review → published template
Published template + chosen parameters → AI content → validation → saved task
```

## Current workflow

1. Describe the goal, audience and requirements in a prompt.
2. Review the AI proposal: name, instructions and parameter definitions. Edit,
   regenerate or cancel. A proposal is not saved automatically.
3. Explicitly save the template. Later edits publish a new immutable revision.
4. Choose values for a task. The server resolves defaults, validates choices and
   asks AI for content using the selected template revision.
5. Validate and save the exact content, answers, parameters and generation
   metadata. The parent preview reads this snapshot without calling AI again.

Failures preserve local input and allow explicit retry. Successful regeneration
replaces the proposal. A stale publication returns 409; local edits remain until
an explicit reload. Saved tasks remain readable when AI is unavailable.

Parents can delete a draft, a template with its revisions/drafts, or all family
learning content, including items beyond the list limit. Deletion requires UI
confirmation and server-enforced ownership; accounts and AI configuration remain.
Unsaved proposals can be discarded and are never stored as drafts.

## Contracts

- **TaskTemplate:** family-owned identity pointing to the current revision.
- **TaskTemplateVersion:** immutable instructions and parameter definitions.
- **TaskInstance:** immutable generated content pinned to a template revision,
  initially saved with status `Draft`.

Schema version 2 describes the template JSON, independently of revision numbers.
See the [template contract][template] and [task contract][content] for exact
fields.

Parameters support text, integer, select and boolean values. Keys are unique,
case-sensitive Latin identifiers; labels and learning content may use any
language. Required values, defaults, bounds and options are validated on the
server. Preserve scalar types, including `false`, zero and explicit empty
optional-text defaults. An optional `questionCountParameter` binds to a required
integer field bounded within 1–20; generation must return that exact count. No
key has subject-specific meaning.

Supported task content is plain text with short-text, numeric or single-choice
questions. Each question has an ID, prompt, answer and integer points. Numeric
answers use invariant decimal text; choice answers match one option exactly. New
interaction types require an application change. AI cannot execute code or
invent UI controls; open-ended essay grading is outside the current scope.

Server limits:

- Template name/field label: 100 characters; instructions: 4,000; up to 16
  fields.
- Text parameters: at most 500 characters; select fields: 1–20 distinct options,
  each at most 100 characters.
- Tasks: 1–20 questions, 0–4 passages, 8,000 total text characters.
- Task title: 100; instructions: 1,000; passage: 4,000; prompt: 500;
  answer/option: 200 characters. Choice questions have 2–6 distinct options;
  points are 0–100.

## Boundaries

AI generates within application-owned schemas and controls. The application
validates and persists output, authorizes access and versions templates. Invalid
or unavailable responses produce errors, never substitute content. Parents
review educational correctness and age suitability.

OpenRouter model selection and optional fallback are configuration-driven. Free
and paid models are supported; paid use requires account credits. The application
does not retry requests. Send only learning inputs. Logs exclude prompts,
answers, identities, credentials, reasoning text and raw provider errors.

The server enforces family ownership, CSRF and atomic, concurrency-safe
publication. Generation pins its revision before AI. Parent answer keys must
never enter child responses. Hebrew/RTL UI uses native accessible controls and
renders learning content as text.

Implementation details live in [architecture](architecture.md), setup in the
[README](../README.md), and presentation rules in the [UI guide](ui-guide.md).
Automated tests use isolated databases and a test provider, never live AI.

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
5. Add account recovery, shared-parent access, backup/restore, deployment
   packaging and update notices. Add pagination when the 100-item lists become
   limiting.

Offline synchronization and native packaging remain deferred. Child sessions
must stay separate from parent authentication and use answer-free,
assignment-checked responses. Do not expose placeholder controls for unfinished
features.

### Child flow proposal

Not implemented. The agreed first target is a separately activated child device;
shared-browser switching is deferred. The recommended first release completes
one flow: parent creates a child profile, reviews and assigns an existing task,
activates the child's device, and sees the child's submitted result.

Keep the current project and feature structure. Existing parent API URLs can
remain compatible; add an independently authorized `/api/child` group when the
feature is built. Add feature folders as working functionality needs them;
renaming `Instances` or introducing empty services is unnecessary.

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
  options and points. Never reuse `InstancePreview` or send answer keys,
  generation instructions, parameters or model metadata. Generate nothing when
  assigning, opening, answering or reporting on a task.

**Records and completion:**

- Keep `TaskInstance` as frozen content. `Child` belongs to a family;
  `Assignment` links one child to one reviewed instance. `TaskSession` records
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
  are proposed changes to today's draft-only deletion behavior.
- Verify cross-family and sibling isolation, parent/child cookie separation,
  expired/replayed activation, revocation, missing CSRF, answer-free JSON,
  resume/conflict behavior, duplicate submission, withdrawal/deletion races,
  scoring edge cases and unchanged historical results. Test the full flow on
  narrow RTL screens and by keyboard with isolated data and providers.

Finalize the proposed grading, retry and retention rules with the child feature
design before adding tables or endpoints. They are product behavior, not a
reason to refactor today's working parent flow.

### Real-model evaluation

Contract tests do not measure educational quality. Before relying on generated
work for children, repeat a small set of representative Hebrew and bilingual
requests across subjects, ages and difficulty levels. Review language,
correctness, age fit, instruction adherence and answer ambiguity; record failures
as well as successes, the model/profile, prompt version, latency, token use and
actual cost when available. Use synthetic requests, never child identities.

Any evaluation runner should reuse the configured generation path and its
validators, remain outside normal CI, require an explicit live invocation and
bound the number of calls. It must not create learning records or copy the
prompts/provider implementation into a second engine. A Qwen-specific test
framework is unnecessary; model quality is still unverified by local tests.

[template]: ../backend/FamilyLearning.Api/TaskEngine/Models/TaskTemplateDefinition.cs
[content]: ../backend/FamilyLearning.Api/TaskEngine/Models/TaskContent.cs
[auth-schemes]: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/authorize-with-a-specific-scheme?view=aspnetcore-8.0
[csrf]: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-8.0
