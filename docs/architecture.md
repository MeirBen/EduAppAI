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
Each task request uses its chosen question count as the schema's minimum and
maximum array length. The request owns this schema; concurrent calls cannot alter
each other's constraints. Server validation still checks the returned count.
The OpenRouter adapter owns transport and configuration; generation owns prompts,
schemas and validation. In schema mode the adapter supplies the schema through
the SDK's native response-format option, preserving constraints that MEAI's
OpenAI subset conversion would turn into descriptions.
See [AI configuration](../README.md#ai-configuration) for
model capabilities, output modes, reasoning, sampling and limits. Tests exercise
these with fixed local model IDs, independently of the active model. No tools
are sent. Responses must finish normally and pass size/depth, required-member,
unknown-field, numeric and domain validation before persistence.

`AiPrompts` separates reusable template design from task generation, sharing
language and presentation rules. Schemas describe field constraints; prompts
explain task semantics and cross-field priorities. Authoring requests concise
task instructions ordered by goal, content, additional parameters and questions,
omitting inapplicable parts and repeated engine rules. Generation has no prior-task
history, so fresh content is requested without a cross-run uniqueness guarantee.
Extra fields require an explicit request for per-task input; fixed requirements
remain in instructions. This is an authoring policy, not a natural-language rule
in the validator. The evaluator checks expected field counts for synthetic cases.
Resolved values override stale defaults, including false, zero and empty text.
`TaskSettings` owns topic, audience, difficulty and question count.
Templates store these under `generation.defaults`; each
task submits a complete `TaskInput` with chosen settings and additional
parameters. `TaskSettingsValidator` validates both template defaults and task
settings. The endpoint resolves dynamic defaults and persists the input with
the output. The AI request includes instructions, chosen settings, parameter
definitions and resolved values; template settings defaults are not sent
again. Shared settings take precedence over stale prose; dynamic fields cannot
reuse their keys.

Text length is instructional guidance. Production validation owns structural
safety, answer consistency and exact requested question counts; the evaluator
owns word-count measurement and case adherence. Domain failures expose
application-authored field messages through typed ProblemDetails, without
correction calls or parsing prose.

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
save resolved settings and parameters, content, and
provider/model/prompt-version/UTC metadata.

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
Failures describe the failed AI call through safe ProblemDetails, including 429
for provider rate limits. Callers own persisted-state recovery; shared AI errors
do not claim that earlier work was or was not saved. Output-limit failures use
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
feedback. Both forms use `TaskSettingsFields` and its shared Signal Forms
schema for common choices. `InstanceForm` preloads template defaults and emits
a complete input; it owns additional parameter controls. Clearing required
text, integer or select inputs is invalid. Cleared optional text stays
explicit; blank optional numbers/selects are omitted so the server can resolve
defaults. Successful publication or task creation replaces its form with a
saved-result link; delayed or failed navigation cannot repeat the write or AI
generation.

Previews read snapshots. The PWA caches assets only; API calls need a connection.
See the [UI guide](ui-guide.md) and [verification commands](../README.md#verify).

References: [IChatClient][chat], [structured output][output], [Signal Forms][forms].

## Staged activity persistence

The content-first API is implemented separately in
`ApiConfiguration.MapContentFirstApi` and exercised by an isolated integration
host. The deployed host still selects `MapApplicationApi` until the accepted
cutover gate; it has no runtime lifecycle switch or schema compatibility reader.

`Features/Activities` owns saved plan/input/document checkpoints, server-owned
content identity and revision updates, explicit adoption, and release. Shared
TaskEngine validators derive diagnostics and readiness. Direct edits never call
AI or republish the source template. The editable DTO excludes provenance and
acceptance metadata; changing a source updates its canonical plan/input and
document together. Questions remain stale until edited or adopted; removing a
source clears its dependent questions' acceptance while preserving their origin.

An application-managed EF concurrency token guards each draft write. One
`SaveChanges` transaction creates the immutable snapshot and marks the draft
terminal, with a unique source-draft index preventing duplicate releases. Exact
release replay returns the original snapshot; if deleted, it returns 410.
Template and source-draft IDs are detached provenance, so independent deletion
does not erase snapshots. Family reset deletes owned learning records atomically
and preserves accounts. No scoring policy or child DTO is introduced here.

The additive migration creates draft/snapshot tables without converting old
learning data. The final cutover task replaces prototype migrations with the
clean initial model. Integration tests use disposable databases and real
authentication/CSRF policies. Persistence-only tests have no provider; worker
tests register an isolated provider.

`AddActivityGeneration` activates one `GenerationWorker` with the staged API.
Run this deployment in **one API process**. SQLite owns the queue and key
tombstones; multiple replicas need a different claim/lease design. The hosted
worker uses short fresh DbContext scopes for admission, claim and checkpoint,
and disposes them before calling the shared AI service. Its single content call
shares the service's two provider slots with synchronous authoring.

New starts share one native per-family rate limiter with existing AI routes.
Owned key replay precedes revision, active-state and budget checks. Admission
atomically enforces global/family/draft queue limits. Manual saves remain
available during generation; any revision change fences the result, which stays
an unapplied diagnostic candidate. Cancellation commits its terminal state and
revision before signaling transport. Draft deletion cascades operation evidence
and key tombstones; it cannot restore deleted work.

Each claim captures immutable stage input. A successful checkpoint saves content,
candidate, usage and queued next stage together. Expected AI failures terminate
only their operation. Unexpected worker/database defects retain the host's normal
stop behavior. Restart resumes compatible queued stages, while uncheckpointed
calling steps become unknown and are never replayed. Profile fingerprints exclude
credentials, so key rotation alone does not invalidate queued work.

Operation artifacts are bounded to 2 MiB and two steps. After seven terminal days,
startup/hourly cleanup expires at most 32 bulky artifacts per pass, retaining keys,
fingerprints, outcomes and known usage until draft deletion. Null usage/cost stays
unknown. Retention uses `TimeProvider`; purging selects IDs without loading the
bulky artifacts. These policies and polling configuration live in
`GenerationOperationOptions`; no distributed scheduler or retry loop is added.

[chat]: https://learn.microsoft.com/en-us/dotnet/ai/ichatclient
[output]: https://openrouter.ai/docs/guides/features/structured-outputs
[forms]: https://angular.dev/guide/forms/signals/schemas
