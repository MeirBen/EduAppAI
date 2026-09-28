# Architecture

The [product specification](product-specification.md) defines the workflow and
contracts. This guide describes how the app implements them.

## Structure

One .NET 8 project uses ASP.NET Identity, EF Core and SQLite. Features own their
endpoints, DTOs and entities; use DbContext directly. `TaskEngine` owns shared
models, validators and AI operations. `Infrastructure` handles authentication,
provider registration and persistence. No repository or mediator wrappers.

Angular is standalone, strict and signal-based. `core` contains auth and API
access. Each feature owns its screens, controls and tests: `library` lists and
removes saved content, `templates` owns prompt authoring and the create/edit
`TemplateEditor`, and `instances` owns parameter choices, generation and previews.
`shared` holds reusable presentation controls and form helpers.
Native HTML and Tailwind provide the shell and controls. Angular proxies
`/api` during development; publication serves the client and API from one
process.

`Program.cs` composes the host. `Infrastructure/Web/ApiConfiguration` owns shared
JSON, ProblemDetails, rate limits and route policies. Every feature API inherits
parent authorization and CSRF protection; only sign-in and token issuance
explicitly allow anonymous requests. Add endpoints to this group.

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

`AiGenerationService` calls `IChatClient` and has no identity or database
access. `AiPrompts` versions authoring/generation instructions; `AiSchemas`
loads embedded JSON schemas. Each request includes the full schema in its system
message and uses it for the strict response format. The SDK adapts some
constraints into provider-facing descriptions; server validators remain
authoritative. OpenRouter routing requires support for the requested parameters.
No tools are sent. Responses must finish normally and pass size/depth, required-
member, unknown-field, numeric and domain validation before persistence.
OpenRouter registration stays in infrastructure.

Authoring prompts separate fixed teaching requirements from editable field
defaults, bounds and options. Instructions refer to parameter keys; labels are
display text. Generation prompts instruct the model to prioritize resolved
values over stale defaults in prose, including explicit false, zero and empty
optional text. Prompt changes advance the generation metadata's prompt version;
saved snapshots remain immutable.

Publication checks `expectedVersion`, an EF concurrency token and a unique
revision index. Generation loads an immutable revision before awaiting AI; no
database transaction stays open during the call. Tasks store resolved
parameters, content and provider/model/prompt-version/UTC metadata. SQLite
timestamp reads preserve UTC.

Template deletion and library reset remove tasks, revisions and templates in one
transaction. They preserve accounts, other families and AI configuration.
Publication or generation finishing after its template was deleted returns 404
and saves nothing.

## Access and failures

Server-issued claims determine family ownership. Queries check ownership before
reading content or calling AI; missing and foreign records both return 404.
Cookies and CSRF protect parent operations. Authenticated API responses use
`no-store`. Parent preview DTOs include answers and cannot be reused for child
access.

AI requests have a configurable three-minute deadline, two concurrent calls per
process and ten requests per family per minute. Cancellation reaches the
provider and every exit releases capacity. The transport timeout allows five
seconds beyond that deadline so application cancellation wins. OpenRouter
requests use low reasoning effort where supported and exclude reasoning from
responses; exclusion alone does not reduce reasoning time. SDK retries are
disabled. Failures return safe ProblemDetails and save nothing. Successful
responses log elapsed time, reported token counts and generation metadata;
logs exclude prompts, content, reasoning text and provider error bodies.
`/api/ai/status` checks configuration without contacting the provider.

## Client state

`LearningApi` owns URLs and transport contracts. Its read methods create Angular
`httpResource` instances in the calling component's injection context. Route
changes and component destruction cancel reads. Read values only after
`hasValue()`; render errors independently. Writes use explicit HttpClient calls
through `requestResult`, which binds cancellation to the caller's lifetime.
Never use reactive resources or automatic retries for writes.

Lazy route guards return a session/token check whose reads are cancelled when
navigation is superseded. Sign-in requests belong to the login page and cannot
redirect after it closes. The server remains the authorization boundary.
`AiTemplateAuthor` holds the prompt/proposal. `AiTemplateForm` edits a
copy through Signal Forms and converts it to the API contract on save. Errors
retain local edits; a replacement proposal resets feedback. `ParameterForm`
emits validated choices. Blank numbers stay distinct from zero; optional empty
text defaults stay explicit. Cancellation stops the browser request but does not
guarantee that a server write was rolled back.

Clearing optional text sends an explicit empty string; blank numeric and select
inputs are omitted so the server can resolve their defaults.

The preview reads saved content. The PWA caches application assets only;
authenticated responses and task operations require a connection. See the
[UI guide](ui-guide.md) and [verification commands](../README.md#verify).

References: [IChatClient][chat], [structured output][output], [free
router][free], [Signal Forms][forms].

[chat]: https://learn.microsoft.com/en-us/dotnet/ai/ichatclient
[output]: https://openrouter.ai/docs/guides/features/structured-outputs
[free]: https://openrouter.ai/docs/guides/routing/routers/free-router
[forms]: https://angular.dev/guide/forms/signals/overview
