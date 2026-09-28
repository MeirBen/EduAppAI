# Architecture

The [product specification](product-specification.md) defines the workflow and
contracts. This guide describes how the app implements them.

## Structure

One .NET 8 project uses Identity, EF Core and SQLite. Features own endpoints,
DTOs and entities and use DbContext directly. `TaskEngine` owns models,
validators and AI operations; `Infrastructure` owns auth, provider registration
and persistence. `Program.cs` composes them. No repository or mediator wrappers.

Angular is standalone, strict and signal-based. `core` owns auth/API access;
`library`, `templates` and `instances` own their screens, controls and tests;
`shared` owns reusable presentation and form helpers. Native HTML and Tailwind
provide the UI. Development proxies `/api`; the published host serves both apps.

Add feature endpoints to `ApiConfiguration`'s group to inherit strict JSON,
ProblemDetails, parent authorization and CSRF. Only sign-in and token issuance
allow anonymous access. Kestrel bounds bodies to 256 KiB, accommodating the
template contract even with escaped Unicode; validators enforce field limits.

## AI and persistence

| Request                              | Result                                |
| ------------------------------------ | ------------------------------------- |
| `POST /api/ai/template-drafts`       | Validated, unsaved AI proposal        |
| `POST /api/templates`                | Template + revision 1, atomically     |
| `POST /api/templates/{id}/versions`  | New revision; conflict returns 409    |
| `POST /api/templates/{id}/instances` | Validated, saved AI task              |
| `GET /api/instances/{id}`            | Stored parent preview                 |
| `DELETE /api/instances/{id}`         | Deletes one saved task                |
| `DELETE /api/templates/{id}`         | Deletes template, revisions and tasks |
| `DELETE /api/templates`              | Clears the family's learning library  |

`AiGenerationService` calls `IChatClient` without identity or database access.
`AiSchemas` loads embedded schemas for both the system message and strict output
format. The SDK adapts some constraints; validators remain authoritative.
OpenRouter requires parameter support. No tools are sent. Responses must finish
normally and pass size/depth, required-member, unknown-field, numeric and domain
validation before persistence.

`AiPrompts` separates fixed teaching requirements from variable fields and refers
to keys, not labels. Resolved values override defaults in prose, including false,
zero and empty text. Prompt changes advance the metadata's prompt version.
Both prompts share concise language-quality guidance, including grammatical
Hebrew and Hebrew script. This guides the model; validators check structure and
bounds, not fluency. Prompt changes apply to new AI output, never rewrite saved
snapshots.

Publication atomically saves a revision and current pointer, guarded by
`expectedVersion`, an EF concurrency token and a unique revision index. Joined
reads resolve family ownership and revision together. Generation pins that
immutable revision before AI; no transaction stays open during the call. Tasks
save parameters, content and provider/model/prompt-version/UTC metadata.

Deletion/reset removes tasks, revisions and templates in one transaction.
Publication or generation finishing after deletion returns 404 and saves nothing.

## Access and failures

Server claims determine family ownership; missing and foreign records both
return 404 before content reads or AI calls. Authenticated API responses use
`no-store`. Parent preview DTOs contain answers and must not serve child access.

AI has a configurable three-minute deadline, two concurrent calls per process
and ten requests per family per minute. Cancellation reaches the provider and
always releases capacity. Transport timeout adds five seconds so application
cancellation wins. The default free router chooses a compatible model; requests
enable low-effort reasoning and leave sampling to the provider. Reasoning and
sampling settings belong to the OpenRouter adapter; generation owns schemas and
validation. Configuration can pin a free model or override generation settings.
Excluding reasoning from responses alone does not reduce computation. Requests
allow up to 8192 output tokens, shared with reasoning when enabled. SDK retries
are disabled. Failures return safe ProblemDetails without saving, including 429
for provider rate limits. AI response logs contain metadata, elapsed time and
token counts. Provider failures log only exception type and HTTP status.
`/api/ai/status` checks configuration without a call.

Production requires explicit migrations and HTTPS; tests cover migration,
redirect, HSTS, secure-cookie and CSRF behavior. `/health` reports process
availability only. Deployment requirements live in [README](../README.md#publish).

## Client state

`LearningApi` owns URLs/contracts. Reads create `httpResource` in the caller's
injection context and cancel on route changes/destruction. Check `hasValue()`
before reading; render errors independently. Writes use HttpClient through
`requestResult`, bound to the caller's lifetime, without automatic retries.
Cancellation does not guarantee server rollback.

Route guards cancel superseded session/token checks. Sign-in belongs to its
page and cannot redirect after destruction; the server authorizes requests.
`AiTemplateAuthor` holds proposals; `AiTemplateForm` edits copies with Signal
Forms and converts them on save. Errors retain edits; new proposals reset
feedback. `ParameterForm` emits validated choices: empty text stays explicit,
while blank numbers/selects are omitted for server defaults.

Previews read snapshots. The PWA caches assets only; API calls need a connection.
See the [UI guide](ui-guide.md) and [verification commands](../README.md#verify).

References: [IChatClient][chat], [structured output][output], [free
router][free], [Signal Forms][forms].

[chat]: https://learn.microsoft.com/en-us/dotnet/ai/ichatclient
[output]: https://openrouter.ai/docs/guides/features/structured-outputs
[free]: https://openrouter.ai/docs/guides/routing/routers/free-router
[forms]: https://angular.dev/guide/forms/signals/overview
