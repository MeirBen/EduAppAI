# Application architecture

Family Learning is an AI-first parent authoring app. Parents describe ideas, review
reusable blueprints and generate tasks with new parameter choices. Every subject follows
the same path. There are no arithmetic generators, static authoring modes or seeded tasks.

During development Angular and ASP.NET run separately with a same-origin API proxy.
After publishing, one ASP.NET process serves the API and Angular assets. SQLite stores
Identity and family learning data. One production backend project keeps feature boundaries
clear without repository, mediator or unit-of-work wrappers.

```text
frontend/src/app/
  core/                       Authentication, API contracts and error feedback
  features/templates/         Prompt authoring, blueprint review, revisions, library
  features/instances/         Dynamic choices and frozen parent previews
  dynamic-form/               Metadata-driven text, integer, select and boolean inputs

backend/FamilyLearning.Api/
  Features/                   Authenticated HTTP endpoints and feature entities
  TaskEngine/Models/          Reusable blueprint and common task content
  TaskEngine/Validation/      Parameter, blueprint and content rules
  TaskEngine/Ai/              Two AI operations, versioned prompts and owned schemas
  Infrastructure/Ai/         OpenRouter registration behind IChatClient
  Infrastructure/Persistence/ Direct EF Core and explicit snapshot serialization
```

## Two AI operations, one domain

```text
POST /api/ai/template-drafts { prompt }
  → validate prompt bounds
  → IChatClient + authoring prompt + template JSON schema
  → strict parse + blueprint validation
  → return an UNSAVED proposal

POST /api/templates
  → validate the parent's reviewed definition
  → insert stable template + immutable version 1 atomically

POST /api/templates/{id}/instances
  → enforce family ownership
  → pin the current template version
  → validate choices and resolve defaults
  → IChatClient + generation prompt + task-content JSON schema
  → strict parse + content/answer/count validation
  → save exact parameters, content and generation metadata as Draft

GET /api/instances/{id}
  → enforce family ownership
  → return the stored snapshot; never call AI
```

The AI service has no database or identity dependency. It knows only the blueprint,
selected parameters, shared schemas and `IChatClient`. OpenRouter is configured at the
composition boundary. Adding reading, history, science or another subject changes data,
not the C# code or Angular component tree.

`AiSchemas` loads embedded application-owned JSON schemas. `AiPrompts` contains separately
versioned authoring/generation instructions. Requests use `ChatResponseFormat.ForJsonSchema`
and strict structured output, not JSON-only prompting. No tool definitions are supplied.
Responses must finish normally, fit the size bound and deserialize with required members,
strict numbers, bounded depth and unknown-member rejection. Domain validators then check
business rules, supported interactions, answers, points and the selected question count.

OpenRouter defaults to `openrouter/free`; paid model IDs are rejected. Infrastructure
accepts an explicit `:free` model, with no retry or fallback configuration. The official
OpenAI-compatible SDK is adapted through Microsoft.Extensions.AI. Its .NET 8-compatible
packages are pinned in lockfiles; no runtime or SDK upgrade is required.

## Immutable content and concurrency

Schema version 2 is the blueprint contract; template revision numbers are independent.
Generation instructions hold fixed requirements. `instanceParameters` hold the fields
a parent chooses each time. An optional `questionCountParameter` identifies a required
integer field bounded within 1–20; it has no reserved name or subject-specific behavior.

A published `TaskTemplateVersion` never changes. Revision publication requires
`expectedVersion`; EF concurrency and a unique template/version index reject a stale
writer with 409. The editor retains unsaved changes and only reloads on an explicit action.
Generation pins a version before awaiting the provider, so concurrent publication cannot
change the in-flight task's meaning. No DB transaction remains open during the AI call.

A saved `TaskInstance` is authoritative. It owns parameters, task content and metadata
(provider, actual returned model, prompt version and UTC timestamp). There is no seed or
generator registry. Reopening an instance only reads data. The AI-only migration removes
incompatible prototype templates and their tasks but preserves accounts and families.
Its data removal cannot be reversed by rolling the migration down.

## Boundaries and failure handling

ASP.NET Identity provides parent authentication. All ownership comes from server-issued
claims, never request IDs. Queries constrain the family before any provider call. Missing
and foreign items both return 404. CSRF protects writes; authenticated API responses are
`no-store`. Parent answer keys must never be included in a future child DTO.

The shared AI service permits two concurrent calls per process. A linked 60-second timeout
and request cancellation reach the provider; the semaphore is released on every exit.
A family is limited to ten generation requests per minute. SDK retries are disabled;
users explicitly retry. Invalid output saves nothing and returns safe ProblemDetails.
Raw provider messages are neither returned nor logged. Logs hold diagnostic metadata only.
Only learning prompts/blueprints/parameters enter provider messages, not user or family IDs.

If no API key exists, `/api/ai/status` reports that AI is unconfigured without contacting
OpenRouter. The prompt screen shows this state. Reading saved content and editing existing
blueprints do not depend on provider availability. There is no production mock or fallback.

## Angular workflow

Routes are lazy, standalone and guarded. Signals own local state; resources load data.
`AiTemplateAuthor` owns the prompt and transient proposal. `AiTemplateForm` owns a copy of
instructions and parameter metadata, using Signal Forms. A feature-local converter keeps
empty numeric inputs distinct from zero and emits only the public contract after validation.
Save, regenerate and cancel are explicit actions. Pending requests disable conflicting edits.

`ParameterForm` renders all four field types directly from the blueprint's labels/options.
The parent preview renders the shared content schema, with plain text, optional choice lists
and answer disclosures. There are no subject-specific labels, operation controls or
conditional math rendering. Future child interaction can use the same content shape with
answers removed server-side.

The interface is Hebrew and RTL, with logical spacing, locally bundled Heebo, isolated
numeric/email directions, native labels and keyboard focus. See [ui-guide.md](ui-guide.md).
Templates render text through Angular interpolation, never generated HTML.

## Verification and limits

`scripts/verify.sh` restores locked dependencies, builds, tests, checks XML documentation,
formats and builds Angular. HTTP tests use a unique SQLite database and scripted IChatClient.
The isolated browser harness runs a local OpenRouter-compatible HTTP server alongside the
published app. It checks the actual SDK's schema request, prompt → review → save → repeated
instances, stale revisions, failures, RTL and narrow layouts. No normal test calls live AI.

The current app ends at the parent preview. Child profiles, activation, assignment,
sessions, scoring and reports are future increments. Lists return at most 100 items.
The PWA caches application assets only; offline task execution is not implemented.

Primary references: [Microsoft IChatClient](https://learn.microsoft.com/en-us/dotnet/ai/ichatclient),
[structured output in .NET](https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/structured-output),
[OpenRouter structured outputs](https://openrouter.ai/docs/guides/features/structured-outputs),
[OpenRouter free router](https://openrouter.ai/docs/guides/routing/routers/free-router),
[Angular Signal Forms](https://angular.dev/guide/forms/signals/overview).
