# Architecture

The [product specification](product-specification.md) defines the workflow and
contracts. This guide describes their implementation.

## Structure

One .NET 8 project uses Identity, EF Core and SQLite. Features own endpoints,
DTOs and entities and use DbContext directly. `TaskEngine` owns pure resolution,
assembly, validation and AI contracts; `Infrastructure` owns authentication,
provider registration and persistence. `Program.cs` composes the single deployed
API and durable worker. No repository, mediator or compatibility layers.

Angular is standalone, strict and signal-based. `core` owns auth/API access;
`activities` owns the editable workspace, `library` its lists, and `instances`
frozen parent previews. `shared` contains reusable presentation and form helpers.
Native HTML and Tailwind provide the UI. Development proxies `/api`; the
published host serves both apps.

Feature endpoints join `ApiConfiguration.MapApplicationApi` to inherit strict
JSON, ProblemDetails, parent authorization and CSRF. Only sign-in and token
issuance allow anonymous access. Kestrel bounds bodies to 256 KiB, accommodating
escaped Hebrew JSON; validators enforce smaller field and aggregate limits.

## Maintenance and version ownership

Use clear domain names, focused methods and explicit data flow. Keep state and
mutations in their owning feature; share actual rules through the existing engine.
Prefer supported native .NET, EF Core and Angular APIs. Extract helpers for a
shared responsibility, not line-count reduction. Avoid speculative abstractions,
pass-through layers, dependencies, silent coercion and fixes that suppress errors
or weaken checks. Remove obsolete callers, rules and comments in the same change.
Verify ownership, resource limits, concurrency, cancellation and failure behavior
with isolated tests. Contract/scope changes need an explicit decision rather than
an undocumented workaround.

[EngineVersions](../backend/FamilyLearning.Api/TaskEngine/EngineVersions.cs) owns
the plan schema version and shared engine revision (prompts, resolution, assembly,
validation and measurement). Stage labels derive from that revision; the server
supplies schema version to Angular. Behavior-preserving refactors do not bump it.
An engine revision alone does not stale accepted content, but current validation
still applies and queued work retains its profile/version compatibility guard.

[EvaluationVersions](../tools/FamilyLearning.Evaluation/EvaluationVersions.cs) owns
report, automatic-check and judge versions independently. Change only affected
semantics; a changed measurement/check policy also advances its check version.
No counters are duplicated in clients, schemas or prompts. Per-template versions
and draft concurrency revisions are separate. Preserve historical evidence without
compatibility readers; see the [AI guide](ai.md) for decisions and evaluation.

## AI and persistence

All routes below are under `/api`; write endpoints enforce CSRF.

| Request                                   | Result                |
| ----------------------------------------- | --------------------- |
| `POST ai/template-drafts`                 | Unsaved proposal      |
| `POST templates`                          | Template version 1    |
| `POST templates/{id}/versions`            | Publish a version     |
| `POST activity-drafts`                    | New editable draft    |
| `PUT activity-drafts/{id}`                | Save a revision       |
| `POST activity-drafts/{id}/operations`    | Idempotent start      |
| `POST activity-drafts/{id}/adopt-content` | Accept stale content  |
| `POST activity-drafts/{id}/release`       | Review and freeze     |
| `GET instances/{id}`                      | Frozen parent preview |
| `DELETE activity-drafts/{id}`             | Draft and operations  |
| `DELETE instances/{id}`                   | One snapshot          |
| `DELETE templates/{id}`                   | Template and versions |
| `DELETE templates`                        | Family learning reset |

`LearningPlan` describes shared settings, scoped controls, material sources and
question requirements. `TaskRequest` supplies per-activity choices.
`TaskRequestResolver` validates and resolves defaults into a self-contained input;
false, zero and explicit empty optional text survive. IDs and content provenance
belong to the application. Controls represent only explicitly requested additional
choices; fixed requirements remain in scoped guidance.

`AiGenerationService` uses `IChatClient` without identity or database access.
Authoring, material generation, question generation and scoped replacements have
separate schema-constrained calls. Supplied sources bypass material AI and are
copied exactly by `TaskAssembly`. Accepted material checkpoints survive question
failure. No automatic repair or retry is made. A question replacement is atomic
over its prompt, options and answer; sibling content stays unchanged.

`AiSchemas` builds request-owned schemas with exact counts and allowed IDs.
The same full schema goes into the prompt and, in schema mode, the provider
response format. The OpenRouter adapter uses the SDK's native response-format
option, preserving schema constraints. It owns transport and configuration;
the engine owns prompts and validation. No tools are sent. Responses must finish
normally and pass size/depth, required-member, unknown-field, numeric and domain
validation. See [AI configuration](ai.md#configuration).

`TextLength` measures material bodies only. Exact/range requirements block stage
acceptance and release when unmet; targets are advisory. Supplied source bytes
are never adjusted to meet a generated-material length requirement.
`TaskAssembly` owns current-input fingerprints, acceptance and readiness.
Syntactic validity does not prove fluent language, true statements or correct
answers; release requires the parent's explicit review of the saved revision.

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

Publication saves a version and current pointer atomically with an EF concurrency
token and unique version index. Activities copy their plan and resolved input;
template provenance is not a live dependency. The native `InitialCreate`
migration owns the final model. Development initializes an empty database;
Production requires an explicit management command before worker startup.
There is no prototype-data conversion. Tests use disposable storage and the real
`Program` composition, with isolated providers; worker state-machine tests
disable only automatic polling so they can drive transitions deterministically.

## Durable generation

`AddActivityGeneration` activates one `GenerationWorker` in `Program.cs`.
Run this deployment in **one API process**. SQLite owns the queue and key
tombstones; multiple replicas need a different claim/lease design. The hosted
worker uses short fresh DbContext scopes for admission, claim and checkpoint,
and disposes them before calling the shared AI service. Its single content call
shares the service's two provider slots with synchronous authoring.

New starts share one native per-family rate limiter with plan authoring.
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

## Evaluation

`tools/FamilyLearning.Evaluation` is a separate developer executable referencing
the engine and adapter and is not published with the API. CLI and `--ui` use the
same validated plan, runner, judge and JSON reports, without application database
or identity services. The runner emits structured progress and checkpoints calls;
summary/comparison derive evidence without provider calls. Cases select bounded
interpretation/refinement or a fixed plan, then applicable material, question and
explicit scoped replacement stages. The evaluator uses shared TaskAssembly,
resolution and TextLength rules; it keeps independent fixture adherence checks.
The current report format records explicit skips, exact request/schema hashes,
raw candidates, application state, source revisions and separate readiness
outcomes. Historical formats are rejected. Generator input compatibility and
judge-quality compatibility are assessed independently, so a judge change does
not invalidate deterministic or human evidence. Evaluation calls are
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
contracts](ai.md#using-the-evaluation-harness).

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

`ActivityWorkspace` opens at the application entry point and owns template and
activity URLs. AI status supplies the server-owned schema version for manual
plans. Each parent message makes one correlated authoring request. Clarification
context remains until a proposal applies or the parent explicitly consolidates
it. Local edits, Undo and cancellation invalidate pending responses before
transport cancellation.

The workspace owns one initialized form buffer for plan and per-activity input,
derived canonical projections, source confirmation and twenty coalesced Undo
entries. PlanEditor and TemplateChat edit the owner's Signal Forms/emit events;
they own no copied draft or HTTP requests. An AI-extracted fixed source requires
local confirmation; direct source edits accept the exact edited string. Only
canonical text crosses the API boundary. Template publication briefly locks
editing and uses expectedVersion; it never writes an activity or generates
content. Failed publication preserves local input and offers a library check.
The workspace also owns editable document fields and a saved revision/baseline.
Generate, replace, adopt and release flush a valid draft checkpoint first. Native
fields retain invalid keystrokes; bounded incomplete answers remain server draft
diagnostics. Undo saves editable content as a new revision, excluding acceptance,
review and operation metadata. Source replacement requires an explicit local
confirmation before either template or activity submission.
Changing a saved material's source kind creates a new material identity and
removes the old content/input; the server still forbids changing an existing
material's kind.

Read-only polling reads operation status before its draft checkpoint and stops at
a terminal status. This order includes the final commit. A changed client revision
or dirty buffer prevents automatic application; the workspace offers the server
result for inspection and explicit reload. Lost start responses retain their exact
operation key/request for explicit replay. The route URL retains the draft and
operation IDs for reload, while chat/Undo/unsubmitted fields remain local.
Candidates pass a bounded editable-field mapping before explicit transfer to the
editor; raw output stays diagnostic text. The existing engine supplies saved
length measurements, including advisory targets, without client-side counting.

The library distinguishes editable drafts, reusable templates and frozen
snapshots. Snapshot preview is parent-only and read-only; an explicit copy creates
a new draft without an AI call. The PWA caches assets only; API calls need a
connection. One Playwright suite tests the published composition against a local
provider, plus intercepted race cases with service workers blocked so browser
routing can observe every request.

See the [UI guide](ui-guide.md) and [verification commands](../README.md#verify).
References: [IChatClient][chat], [structured output][output], [Signal Forms][forms].

[chat]: https://learn.microsoft.com/en-us/dotnet/ai/ichatclient
[output]: https://openrouter.ai/docs/guides/features/structured-outputs
[forms]: https://angular.dev/guide/forms/signals/schemas
