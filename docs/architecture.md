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
`AiSchemas` supplies embedded schemas for the prompt and provider output format.
The OpenRouter adapter owns transport and configuration; generation owns prompts,
schemas and validation. See [AI configuration](../README.md#ai-configuration) for
model capabilities, output modes, reasoning, sampling and limits. Tests exercise
these with fixed local model IDs, independently of the active model. No tools
are sent. Responses must finish normally and pass size/depth, required-member,
unknown-field, numeric and domain validation before persistence.

`AiPrompts` separates reusable template design from task generation, sharing
language and presentation rules. Schemas describe field constraints; prompts
explain task semantics and cross-field priorities. Templates retain task-specific
requirements and exact parameter references, rather than repeating engine rules.
Resolved values override stale defaults, including false, zero and empty text.
Optional reviewed `contentWordCount` bounds override prose length instructions.
`TaskContentValidator` owns counting and enforcement; evaluation reuses the same
counter. Domain failures expose application-authored field messages and measured
counts through a typed ProblemDetails response, without a correction call or
parsing requirements from prose.

Each generation operation makes one AI call. Validators enforce structure and
bounds, not fluency or truth. Parents can edit proposed instructions; the app
does not translate or proofread them automatically. Saved content is immutable.
Prompt changes advance the metadata's prompt version and affect new output only.

`tools/FamilyLearning.Evaluation` is a separate developer executable referencing
the engine and adapter and is not published with the API. CLI and `--ui` use the
same validated plan, runner, judge and JSON reports, without application database
or identity services. The runner emits structured progress and checkpoints calls;
summary/comparison derive evidence without provider calls. Evaluation calls are
paced and may retry HTTP 429 at most three times within the hard call budget.
Each attempt is retained; waiting is cancellable and excluded from request
deadlines and provider latency. Production calls do not inherit these retries.

The developer dashboard is a loopback-only ASP.NET host with static HTML/CSS/JS.
Its coordinator owns one cancellable run and waits for the final checkpoint on
shutdown. It owns an isolated service provider using the same application AI
registration and validators. Configuration failures disable live runs without
blocking offline routes or exposing invalid settings; restarting reloads the
configuration. The artifact store maps validated run IDs under a configured root,
rejects links, and serializes atomic human-review edits. Polling reads immutable
progress snapshots; history and comparison reread authoritative `run.json`.
Host/Origin checks, antiforgery, CSP and plain-text rendering protect the local
paid-run boundary. See [evaluation usage and report
contracts](../README.md#hebrew-ai-evaluation).

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

AI permits two concurrent calls per process and ten requests per family per
minute. Cancellation reaches the provider and always releases capacity.
Transport timeout adds five seconds to the application deadline so its
cancellation wins. SDK retries are disabled; OpenRouter's optional fallback
handles provider errors, not failed app validation.
Failures return safe ProblemDetails without saving, including 429 for provider
rate limits. Output-limit failures use
`urn:family-learning:ai-output-limit` so the UI can distinguish them from invalid
JSON without displaying provider text. The transport checks HTTP 200 bodies for
provider errors; the adapter normalizes missing or malformed SDK responses to
safe provider failures. AI response logs record metadata, finish reason, size,
elapsed time and token counts before output validation.
Rejections log a failure category; transport failures log exception type, status,
prompt version and elapsed time. Prompts, answers and reasoning text stay out of
logs. `/api/ai/status` checks configuration without a call.

Local management commands compose only persistence and authentication; migrations
and account provisioning work even with incomplete AI configuration. Production
requires explicit migrations and HTTPS; tests cover migration, redirect, HSTS,
secure-cookie and CSRF behavior. `/health` reports process availability only.
Deployment requirements live in [README](../README.md#publish).

## Client state

`LearningApi` owns URLs/contracts. Reads create `httpResource` in the caller's
injection context and cancel on route changes/destruction. Check `hasValue()`
before reading; render errors independently. Writes use HttpClient through
`requestResult`, bound to the caller's lifetime, without automatic retries.
Cancellation does not guarantee server rollback.
Changing route parameters destroys the old page and cancels its pending writes;
query and fragment changes preserve the current page and its edits.

Route guards cancel superseded session/token checks. Sign-in belongs to its
page and cannot redirect after destruction; the server authorizes requests.
`AiTemplateAuthor` holds proposals; `AiTemplateForm` edits copies with Signal
Forms and converts them on save. Errors retain edits; new proposals reset
feedback. `ParameterForm` preloads defaults; clearing required text, integer or
select inputs is invalid. Cleared optional text stays explicit; blank optional
numbers/selects are omitted so the server can resolve defaults.
Successful publication or task creation replaces its form with a saved-result
link; delayed or failed navigation cannot repeat the write or AI generation.

Previews read snapshots. The PWA caches assets only; API calls need a connection.
See the [UI guide](ui-guide.md) and [verification commands](../README.md#verify).

References: [IChatClient][chat], [structured output][output],
[Qwen prompting guidance][prompting], [Signal Forms][forms].

[chat]: https://learn.microsoft.com/en-us/dotnet/ai/ichatclient
[output]: https://openrouter.ai/docs/guides/features/structured-outputs
[prompting]: https://docs.qwencloud.com/developer-guides/accuracy-tuning/text-generation
[forms]: https://angular.dev/guide/forms/signals/overview
