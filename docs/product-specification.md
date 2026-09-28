# Family Learning: product and technical specification

Version 4 · AI-only direction · 2026-09-28

This specification reflects the parent's clarified product direction: **a parent describes
an idea, AI creates a reusable template, and AI generates new tasks from that template.**
There are no subject-specific C# generators, static-template modes or preloaded exercises.
The earlier arithmetic prototype is superseded. Existing prototype learning data may be
removed during this transition; parent accounts remain.

## Product idea

The family starts with an empty library. A parent describes what they want to teach,
for whom, and how. The system proposes reusable instructions and the fields that should
vary each time. The parent reviews and edits that proposal before saving it.

For example, a parent requests reading comprehension for a particular age. The template
can expose theme, difficulty, passage length and question count. The parent first chooses
dinosaurs, easy, short and five questions; later they choose space, medium and eight.
Both tasks come from the same template, with fresh content. Science, vocabulary, history,
math and other subjects use the same contracts and generator. Adding a subject requires
no new C# generator or Angular page.

The supported interactions bound what “any task” means: text passages with short text,
numeric or single-choice answers. New interaction types need an intentional application
change; AI cannot invent controls or execute generated code.

## Core model

```text
Parent prompt → unsaved template proposal → parent review → TaskTemplateVersion
                                                               ↓
                                                     Instance Parameters
                                                               ↓
                                                        AI generation
                                                               ↓
                                                     validated TaskInstance
                                                               ↓
                                                       parent preview
                                                               ↓ future
                                               assignment → TaskSession → result
```

- **TaskTemplate:** stable family-owned identity and current revision number.
- **TaskTemplateVersion:** immutable blueprint. Publishing edits creates another revision.
- **Instance Parameters:** validated choices for one use, with defaults resolved by the server.
- **TaskInstance:** frozen parameters, generated content, answers and generation metadata,
  pinned to the exact template revision. Reopening it never invokes AI.
- **TaskSession:** future child answers, timing, attempts and server-scored results. Sessions
  reference an instance; they never replace template instructions or task content.

## Template authoring

The primary creation screen is a natural-language prompt, not a template-type menu.
The authoring request returns a proposal and does not write a template to the database.
Parents can edit its name, instructions, field labels, keys, types, defaults, limits and
options; request a replacement proposal; cancel; or explicitly save.

A failed request keeps the prompt and existing proposal. Regeneration explicitly replaces
local edits. A stale revision publication returns 409 and preserves local edits until
the parent explicitly loads the latest version.

Schema version 2 defines a blueprint independently from its publication revision:

```json
{
  "schemaVersion": 2,
  "name": "הבנת הנקרא",
  "instanceParameters": [
    { "key": "theme", "label": "נושא", "type": "text", "required": true,
      "default": "חלל", "maxLength": 100 },
    { "key": "count", "label": "מספר שאלות", "type": "integer", "required": true,
      "default": 5, "min": 1, "max": 20 }
  ],
  "generation": {
    "instructions": "צרו קטע קריאה בנושא theme עם count שאלות הבנה.",
    "questionCountParameter": "count"
  }
}
```

The instructions hold fixed teaching requirements. Parameters capture choices for each
new task. Field keys are unique Latin identifiers; labels and content may use the learning
language. The interface is Hebrew and RTL. Text, integer, select and boolean parameters
use native accessible controls, driven entirely by metadata.

`questionCountParameter` optionally names a required integer parameter bounded within
1–20. The server enforces the selected count exactly. Without a binding, instructions
may specify a fixed count; the global 1–20 limit still applies. No parameter key has
subject-specific meaning in application code.

## Two distinct AI operations

1. **Authoring:** parent prompt → reusable blueprint and dynamic fields.
2. **Generation:** published blueprint + resolved choices → new task content.

Application-owned system prompts and JSON schemas are version-controlled separately.
`Microsoft.Extensions.AI.IChatClient` is the provider boundary. OpenRouter's free router
is the default; an explicitly configured `:free` model is also allowed. No paid models,
automatic retries, live evaluations in CI, function tools or generated code execution.
Provider-specific configuration stays in infrastructure, not in educational contracts.

The application sends only the submitted learning request or blueprint and selected
parameters. It does not add parent email, family IDs, child identity, authentication
cookies or other family data. It logs only provider/model/prompt-version/time diagnostics,
not prompts, answers, raw provider errors or reasoning.

AI output is untrusted. Requests use structured output with the application's JSON schema.
The server strictly parses the response, rejects unknown fields, validates structure and
business rules, and checks the question count before saving anything. Refused, truncated,
invalid or unavailable responses produce localized ProblemDetails; there is no pretend
fallback content. The parent still reviews educational correctness and age suitability.

## Common task content

Task content contains a title, optional instructions, text blocks and questions. Every
question has a stable local ID, prompt, interaction, answer and integer points.

- `numeric-input`: invariant decimal answer without exponent/group formatting.
- `text-input`: short, objectively checkable answer. Essay grading is outside this increment.
- `single-choice`: 2–6 distinct options and an answer that exactly matches one option.

Content limits: 1–20 questions, 0–4 passages, 8,000 aggregate text characters. Titles are
at most 100 characters, instructions 1,000, each passage 4,000, prompts 500 and answers
or options 200. Points are integers in 0–100. Plain text is rendered as text, never HTML.

Blueprint limits: name 100, generation instructions 4,000, up to 16 fields, field label
100, text input limit up to 500, and 1–20 distinct select options of at most 100 characters.
Input and output limits apply on the server regardless of client validation.

## Ownership, publication and persistence

Every template and instance belongs to a family. The server obtains family identity from
authenticated claims and checks ownership before reading content or invoking AI. Missing
and foreign records both return 404. Writes require CSRF protection.

A template and its first version are saved atomically. Revision publication uses an
expected version, an EF concurrency token and a unique template/version index. A generation
request pins the version before calling AI; publication in another tab cannot alter it.
Only validated content is persisted, initially with status `Draft`.

Parent previews contain answer keys. Future child APIs must use a separate DTO that omits
answers and checks both child ownership and assignment. AI never controls authorization,
status transitions, scoring, persistence or versioning.

## Intended family learning loop

After the authoring foundation, add child profiles and revocable device activation,
explicit parent assignment, a generic child player for the supported interactions,
authoritative server scoring and reports from recorded sessions.

Keep child access separate from parent sessions. Use expiring activation codes; parents
can revoke a device. Starting, answering and completing a session must tolerate retries
and concurrent requests. Define answer normalization, retry rules and scoring policy
explicitly before shipping them. No inferred or AI-controlled score updates.

Parents should review tasks before assignment, inspect completed work and reuse successful
templates with new parameters. Start reports with completed tasks and per-question
results; expand only from stored evidence. Do not expose placeholder assignment or scoring
buttons before these behaviors work.

## Stack and operational boundaries

Use one production .NET 8 backend with feature folders, ASP.NET Identity, EF Core and
SQLite. Use DbContext directly; no repository, unit-of-work or mediator wrappers.
The standalone strict Angular app uses signals and native controls, Ionic for the shell,
and Tailwind theme tokens. Same-origin publication serves both API and client.

AI is bounded to two concurrent calls per process, 60 seconds per call and ten generation
requests per family per minute. Request bodies are bounded. Cancellation reaches the
provider; capacity is released on failure. Requests are never retried automatically.
A missing provider key is explicitly visible. Existing saved content remains readable
when AI is unavailable.

Do not seed educational content, commit secrets/databases/generated outputs or make paid
AI calls. The user handles all Git operations. Keep setup local, credentials outside the
repository, and database migrations explicit in Production. No offline task execution,
public registration, child flow or deployment-readiness claims are made by this increment.

## Acceptance criteria

- A parent creates a reusable reading, science or vocabulary template from a prompt.
- No template exists until the parent reviews and explicitly saves the proposal.
- Edited fields render dynamically and defaults preserve their actual scalar types.
- Two different parameter selections produce different task content from one blueprint.
- Invalid output, wrong counts and provider failures save nothing and allow explicit retry.
- Reloading a saved preview preserves content and never regenerates it.
- New template revisions leave old instances pinned and unchanged; stale edits remain local.
- Another family cannot read or generate from the template.
- Hebrew RTL, keyboard controls and 360px/200% text layouts work throughout the flow.
- Automated checks use isolated databases and a test-only provider, never live AI or user data.
