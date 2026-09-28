# Architecture

The [product specification](product-specification.md) defines the workflow and
contracts. This guide describes how the app implements them.

## Structure

One .NET 8 project uses ASP.NET Identity, EF Core and SQLite. Features own their
endpoints, DTOs and entities; use DbContext directly. `TaskEngine` owns shared
models, validators and AI operations. `Infrastructure` handles authentication,
provider registration and persistence. No repository or mediator wrappers.

Angular is standalone, strict and signal-based. `core` contains auth and API
access; feature folders contain screens; `dynamic-form` renders template-defined
fields. Native HTML and Tailwind provide the shell and controls. Angular proxies
`/api` during development; publication serves the client and API from one
process.

## AI and persistence

| Request                              | Result                             |
| ------------------------------------ | ---------------------------------- |
| `POST /api/ai/template-drafts`       | Validated, unsaved AI proposal     |
| `POST /api/templates`                | Template + revision 1, atomically  |
| `POST /api/templates/{id}/versions`  | New revision; conflict returns 409 |
| `POST /api/templates/{id}/instances` | Validated, saved AI task           |
| `GET /api/instances/{id}`            | Stored parent preview              |

`AiGenerationService` calls `IChatClient` and has no identity or database
access. `AiPrompts` versions authoring/generation instructions; `AiSchemas`
loads embedded JSON schemas. Requests use strict structured output without
tools. Responses must finish normally and pass size/depth, required-member,
unknown-field, numeric and domain validation before persistence. OpenRouter
registration stays in infrastructure.

Publication checks `expectedVersion`, an EF concurrency token and a unique
revision index. Generation loads an immutable revision before awaiting AI; no
database transaction stays open during the call. Tasks store resolved
parameters, content and provider/model/prompt-version/UTC metadata. SQLite
timestamp reads preserve UTC.

## Access and failures

Server-issued claims determine family ownership. Queries check ownership before
reading content or calling AI; missing and foreign records both return 404.
Cookies and CSRF protect parent operations. Authenticated API responses use
`no-store`. Parent preview DTOs include answers and cannot be reused for child
access.

AI requests have a 60-second timeout, two concurrent calls per process and ten
requests per family per minute. Cancellation reaches the provider and every exit
releases capacity. SDK retries are disabled. Failures return safe ProblemDetails
and save nothing; logs exclude provider error bodies. `/api/ai/status` checks
configuration without contacting the provider.

## Client state

Lazy routes use guards for navigation; the server remains the authorization
boundary. `AiTemplateAuthor` holds the prompt/proposal. `AiTemplateForm` edits a
copy through Signal Forms and converts it to the API contract on save. Errors
retain local edits; a replacement proposal resets feedback. `ParameterForm`
emits validated choices. Blank numbers stay distinct from zero; optional empty
text defaults stay explicit. Leaving an authoring/generation screen cancels its
pending write request.

The preview reads saved content. The PWA caches application assets only;
authenticated responses and task operations require a connection. See the
[UI guide](ui-guide.md) and [verification commands](../README.md#verify).

References: [IChatClient][chat], [structured output][output], [free
router][free], [Signal Forms][forms].

[chat]: https://learn.microsoft.com/en-us/dotnet/ai/ichatclient
[output]: https://openrouter.ai/docs/guides/features/structured-outputs
[free]: https://openrouter.ai/docs/guides/routing/routers/free-router
[forms]: https://angular.dev/guide/forms/signals/overview
