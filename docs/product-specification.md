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

OpenRouter uses only free models and may fall back to another configured free
model on provider errors. The application does not retry requests. Send only
learning inputs. Logs exclude prompts, answers, identities, credentials,
reasoning text and raw provider errors.

The server enforces family ownership, CSRF and atomic, concurrency-safe
publication. Generation pins its revision before AI. Parent answer keys must
never enter child responses. Hebrew/RTL UI uses native accessible controls and
renders learning content as text.

Implementation details live in [architecture](architecture.md), setup in the
[README](../README.md), and presentation rules in the [UI guide](ui-guide.md).
Automated tests use isolated databases and a test provider, never live AI.

## Next steps

The current app ends at the parent preview. Add these only as working features:

1. Evaluate correctness, schema reliability and latency with free OpenRouter
   models.
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

[template]: ../backend/FamilyLearning.Api/TaskEngine/Models/TaskTemplateDefinition.cs
[content]: ../backend/FamilyLearning.Api/TaskEngine/Models/TaskContent.cs
