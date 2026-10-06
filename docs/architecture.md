# Architecture

The [product specification](product-specification.md) defines the workflow and
contracts; this guide describes their implementation.

## Structure

One .NET 8 project uses Identity, EF Core and SQLite. Features own endpoints,
DTOs and entities and use DbContext directly. `TaskEngine` owns pure resolution,
assembly, validation and AI contracts; `Infrastructure` owns authentication,
provider registration and persistence. `Program.cs` composes the API and the
durable worker. There are no repository, mediator or compatibility layers.

Angular is standalone, strict and signal-based: `core` owns auth and API access,
`activities` the editable workspace, `library` the lists, `instances` frozen
previews and `shared` reusable presentation and form helpers. Development
proxies `/api`; the published host serves both.

Feature endpoints join `ApiConfiguration.MapApplicationApi` to inherit strict
JSON, ProblemDetails and CSRF. Sibling parent and child groups select explicit
cookie schemes; only sign-in, child activation and token issuance are anonymous.
Kestrel bounds bodies to 256 KiB to allow escaped Hebrew
JSON; validators enforce the smaller field and aggregate limits.

Keep state and mutations in their owning feature, share rules through the
engine, prefer native .NET, EF Core and Angular APIs, and remove obsolete
callers, rules and comments in the same change. A contract or scope change needs
an explicit decision, not a workaround.

[EngineVersions](../backend/FamilyLearning.Api/TaskEngine/EngineVersions.cs)
owns the plan schema version and the engine revision behind prompts,
resolution, assembly, validation and measurement; behavior-preserving refactors
do not bump it. A revision change alone never stales accepted content, but
current validation applies and queued work keeps its compatibility guard.
[EvaluationVersions](../tools/FamilyLearning.Evaluation/EvaluationVersions.cs)
independently owns report, check and judge versions. Template versions and draft
revisions are separate concurrency counters.

## AI and persistence

All routes are under `/api`; writes enforce CSRF.

| Request                                   | Result                     |
| ----------------------------------------- | -------------------------- |
| `GET limits`                              | Server content limits      |
| `POST ai/template-drafts`                 | Unsaved proposal           |
| `POST templates`                          | Template version 1         |
| `POST templates/{id}/versions`            | Publish a version          |
| `POST activity-drafts`                    | New editable draft         |
| `PUT activity-drafts/{id}`                | Save a revision            |
| `POST activity-drafts/{id}/operations`    | Idempotent start           |
| `POST activity-drafts/{id}/adopt-content` | Accept stale content       |
| `POST activity-drafts/{id}/release`       | Review and freeze          |
| `GET instances/{id}`                      | Frozen parent preview      |
| `DELETE activity-drafts/{id}`             | Draft and operations       |
| `DELETE instances/{id}`                   | Archive or delete snapshot |
| `DELETE templates/{id}`                   | Template and versions      |
| `DELETE templates`                        | Family learning reset      |
| `GET library/changes`                     | Change notes (SSE)         |

`EngineValidation` names every client-visible limit once; validators, their
messages, prompts and `ContentLimits` (served by `GET limits`) all read those
constants, so the client never hard-codes a limit.

`LearningPlan` holds shared settings, scoped controls, material sources and
question requirements; `TaskRequest` supplies per-activity choices.
`TaskRequestResolver` resolves defaults into a self-contained input in which
false, zero and explicit empty text survive. IDs and provenance belong to the
application.

`AiGenerationService` uses `IChatClient` without identity/database access.
Authoring, ideas, material writing, questions and scoped replacements use
separate schema-constrained calls, without tools, automatic repair or retry.
`TaskAssembly` copies supplied sources exactly; accepted materials survive a
question failure. Question replacement is atomic over prompt, options and key.
`AiSchemas` builds request-owned schemas with exact counts, allowed IDs and the
same `EngineValidation` constants used by prompts and validators. The OpenRouter
adapter owns transport/configuration and sends the native SDK response format.
Responses must finish normally and pass size, shape, numeric and domain checks;
question text fields are trimmed first, the only normalization applied; see
[AI configuration](ai.md#configuration).

`AiPrompts` composes stage/shared rules; `MathPromptGuidance` owns authoring-time
math interpretation. Schema descriptions own source roles and answer-format
capabilities. All subjects use the same stages and authoritative validators.

`MaterialIdeas` validates five bounded ideas and selects the lowest estimated
overlap with recent family ideas. An application-owned draw breaks ties: the
worker's operation ID keeps selection stable within an operation. Only the
selected idea reaches the writer; manual edits retain it as provenance, AI
rewrites clear it. Supplied sources and question-only operations skip ideas.

After admission's idempotency checks, `GenerationHistoryReader` captures the
current document, then at most 12 unreleased drafts and 12 snapshots, yielding
up to eight distinct ideas and 12 prompts clipped to 300 characters. It excludes
answers/identities. The AI assesses topic relevance; no exact subject labels
are required. Frozen operation artifacts send ideas only to idea generation and
prompts only to question generation. Concurrent operations may share history;
model estimates guarantee neither uniqueness nor quality.

`TextLength` measures material bodies only. Unmet strict ranges block
acceptance and release; targets are advisory, and supplied sources are
never adjusted. `TaskAssembly` owns input fingerprints, acceptance and
readiness. Valid structure does not prove correct content, so release requires
the parent's review of the saved revision.

`Features/Activities` owns saved plan, input and document checkpoints,
content identity, adoption and release; engine validators derive diagnostics.
Direct edits never call AI or republish the template, and the editable DTO
excludes provenance and acceptance. A source change updates plan, input and
document together, leaving dependent questions stale until edited or adopted.

An EF concurrency token guards each draft write. One `SaveChanges` creates the
snapshot and marks the draft terminal, and a unique source-draft index prevents
duplicate releases; replay returns the original snapshot, or 410 if it was
deleted. Template and draft IDs are detached provenance, so deletions never
erase snapshots, and family reset deletes learning records atomically while
keeping accounts. Publication saves a version and its current pointer atomically
under a concurrency token and unique index.

Additive migrations extend `InitialCreate`; existing content is preserved.
Development applies migrations; Production requires the explicit management
command. Tests use disposable storage, real `Program` composition and isolated
providers;
worker tests disable automatic polling to drive transitions deterministically.

## Change notes

`Features/Library` streams server-sent change notes to the family that made a
write, after its commit. Successful library route writes publish through one
endpoint filter; worker claims, checkpoints, recovery and diagnostic expiry
publish after their commits. A note carries no content, so clients reread what
they show. Each stream holds at most one
pending note, so changes coalesce, and its first note covers anything committed
before it subscribed. A family may hold 16 streams. Each ends after five minutes
or at shutdown, and reconnecting re-runs authentication. Like the queue,
delivery is process-local.

## Durable generation

`AddActivityGeneration` runs one `GenerationWorker`, so deploy **one API
process**: SQLite owns the queue and key tombstones, and replicas would need a
lease design. The worker uses short DbContext scopes for admission, claim and
checkpoint and disposes them before each AI call; its single content call shares
the service's two provider slots with authoring.

Starts share one per-family rate limiter with authoring. Owned key replay comes
before revision, active-state and budget checks, and admission atomically
enforces global, family and draft queue limits. Saves stay available during
generation; any revision change fences the result into an unapplied diagnostic
candidate. Cancellation commits its terminal state before signaling transport.
Draft deletion cascades operation evidence and keys.

Each claim captures immutable stage input. The idea stage checkpoints the
selected idea and queues writing without advancing the draft's content revision.
A content checkpoint saves content, candidate, usage and the next stage
together. `ExpectedRevision` advances with each accepted checkpoint;
cancellation advances it only when no other writer has changed the draft. This
identifies the revisions owned by the operation without changing the original
start-key fingerprint. Expected AI failures end only their operation. On
restart, compatible queued stages resume, while uncheckpointed calling steps
become unknown and are never replayed. Profile fingerprints exclude credentials,
so key rotation keeps queued work valid.

Operation artifacts are bounded to 2 MiB and three steps. After seven terminal
days, startup and hourly cleanup expire up to 32 bulky artifacts per pass,
keeping keys, fingerprints, outcomes and known usage until draft deletion.
`GenerationOperationOptions` holds these policies; there is no distributed
scheduler or retry loop.

## Evaluation

`tools/FamilyLearning.Evaluation` is a developer executable referencing the
engine and adapter, never published with the API or given its database or
identity services. CLI and dashboard share the validated plan, runner, pinned
judge client and JSON reports, and reuse the engine's assembly, resolution and `TextLength`
rules with independent fixture adherence checks, including exact recalculation
of bare calculation prompts and a reversed-sign display check. Reports record
explicit skips, exact request and schema hashes, raw candidates and separate
readiness outcomes;
older formats are rejected. Evaluation may retry HTTP 429 three times within its
call budget; production never inherits these retries.

The dashboard is a loopback-only ASP.NET host with static files. Its coordinator
owns one cancellable run, waits for the final checkpoint on shutdown and uses
the application's AI registration in an isolated service provider, so invalid
configuration disables live runs but not offline routes. The artifact store maps
validated run IDs under its root, rejects links and serializes review edits.
Host and Origin checks, antiforgery, CSP and plain-text rendering protect the
paid-run boundary. Usage and report contracts are in the
[AI guide](ai.md#using-the-evaluation-harness).

## Child device access

`Features/Children` owns profiles, one replaceable hashed activation slot per
child and fixed device grants. Named Identity/`Child` cookie policies isolate
access; session entry rejects active opposite-mode cookies before changing CSRF
tokens. Activation needs an anonymous browser and a fresh identity-bound token
afterward. See the [access contract](product-specification.md#profiles-and-device-access)
for grant terms and revocation behavior. No child endpoint uses AI.

`ChildAccess.FindAsync` queries current profile/grant state without EF tracking.
Writes sample UTC and recheck access/state after acquiring a short SQLite
transaction, so committed access loss wins over earlier authentication. Lists
use SQL projection and bounded deterministic paging. Profile `details` omission
preserves older-client metadata; details stay parent-only. Profile deletion
checks revision/history, device-record deletion checks inactivity, and reset
removes owned dependents before referenced content, all within transactions.

`Features/Assignments` owns one assignment per child/snapshot pair. Transactions
serialize creation, withdrawal and snapshot removal; composite foreign keys
enforce family ownership and retention. Replay precedes new-create eligibility.
Lists project names/titles and session existence (`HasStarted`) in SQL without
loading answer buffers. Child reads use explicit learner contracts; read-only
endpoints never start work.

`TaskSession` uses the assignment ID as a restrictive primary/foreign key and
has its own concurrency revision. Start/save leave assignment status/revision
unchanged; submission advances both atomically. Resume/start never resets
submitted work. `SessionValidation` bounds raw input before recognizing replay;
blank/missing means unanswered, other text stays exact. `SessionScoring` compares
validated numeric digits without decimal rounding and stores frozen awards with
policy version 1, independent of generation revisions. Nonblank short text,
including zero-point work, awaits parent review. Child sessions expose saved
answers, status, timestamps and a completed final total, never keys or per-item
awards. Identical submissions preserve stored results/revisions/timestamps.

Parent `/result` and `/review` read frozen content/evaluation. Review requires
exact pending IDs, bounded integer awards and the session revision. One
transaction completes assignment/session and records reviewer/time while
preserving automatic awards, answers, submission time and scoring policy.
Validate raw input before replay; compare the original parent-graded rows,
ignoring order, even with an old positive revision. Completed grades cannot
change; automatically completed work cannot acquire review metadata. Parent
report DTOs never serve children. Archives/disabled profiles retain reports.

Snapshot archive time is mutable metadata; content JSON is frozen. Library
lists hide archived rows; parent previews, assignment reads and generation
history retain them. Removal keeps its 204 contract; UI copy covers deletion
and archiving because concurrent assignment can change the outcome.

## Access and failures

Server claims determine family ownership; missing and foreign records both
return 404 before content reads or AI calls. Authenticated responses use
`no-store`. Parent preview DTOs contain answers and must not serve child access.

AI permits two concurrent calls per process and ten requests per family per
minute; cancellation reaches the provider and releases capacity. The transport
timeout exceeds the application deadline by five seconds so the deadline wins.
SDK retries are disabled, and OpenRouter's optional fallback covers provider
errors, not failed validation. Failures are safe ProblemDetails, including 429
for provider rate limits and `urn:family-learning:ai-output-limit` for output
limits, and never claim whether earlier work was saved. Error bodies inside HTTP
200 and malformed SDK responses become safe provider failures. Logs record
response metadata, finish reason, size, timing, tokens and failure categories,
never prompts, answers or reasoning. `/api/ai/status` checks configuration
without a call.

Serilog integrates through ASP.NET Core's logging provider; application code uses
`ILogger<T>`, fixed message templates and native scopes. Worker events use
source-generated `LoggerMessage` methods with stable event IDs. One request
completion event owns duration, status, request correlation and unexpected
exceptions; the framework's duplicate exception-handler event is suppressed.
Generation scopes carry only operation/draft IDs and stage, and outcome events
follow successful database commits. File limits, levels and sink settings are in
the server's `appsettings.json`; see [server logs](../README.md#server-logs).

Management commands compose only persistence and authentication, so migrations
and provisioning work without AI configuration. Tests cover migration, HTTPS
redirect, HSTS, secure cookies and CSRF; deployment is in the
[README](../README.md#publish).

## Client state

`App` contains only the root outlet. Parent and child route trees have separate
shells and authentication guards; `PageShell` shares the responsive frame, theme,
skip link and navigation loader without owning either identity. Parent URLs remain
unchanged. Child navigation never loads parent identity, limits or content APIs.

`LearningApi`, `ParentChildrenApi` and `AssignmentApi` own parent URLs and
contracts. Reads create `httpResource` in the
caller's injection context and cancel on route change or destruction; check
`hasValue()` before reading and render errors independently. Writes go through
`requestResult`, bound to the caller's lifetime, without retries; cancellation
does not guarantee a server rollback. Changing route parameters destroys the
page and cancels its writes, while query and fragment changes keep its edits.
Guards check the session on every private navigation and cancel superseded
checks. Matching guards resolve access redirects before unsaved-work confirmation;
explicit sign-out runs only after the outgoing page accepts navigation. A valid
server session bypasses login, and successful sign-in replaces its history entry.
Failed access checks use a separate retry page without requesting credentials.
The request token loads once per sign-in and the server's
`ContentLimits` once per tab, both before a private page renders; forms, caps
and copy read the limits through `Limits`. The server still authorizes and
validates every request.

Parent management pages own bounded list resources and local buffers. The paged
child selector retains selection; assignment filters include disabled profiles.
Refreshes preserve unsaved edits. Activation codes live only in page memory and
clear on selection/destruction. Frozen previews distinguish new assignments
from replay. Result pages read frozen answers/evaluation without starting work;
pending grades start blank, use frozen integer bounds and finalize together.
Failed writes preserve grades and block resubmission until an explicit saved
read; loading completed grades requires confirmation. Route/browser-close guards
protect edits. Conflict copy belongs to its feature, not template/AI errors.

`ChildAuth` supplies learner identity and answer-length limit. Cancellable guards
route 401 to activation and availability errors to retry. Activation sends a code
once, clears it and refreshes identity-bound CSRF before navigation; ambiguous
failures offer a session check. Disconnect follows accepted unsaved-work guards
and revokes the grant.

`ChildApi` uses learner-only contracts. The inbox is read-only; `ChildPlayer`
explicitly starts/resumes and owns raw answers, acknowledged revision and dirty
state. Signal Forms retain unfinished numeric input on save and validate grammar
on submit using the child limit. No writes retry. Uncertain/conflicting writes
require a session read before another write; loading newer answers is explicit.
A terminal receipt locks editing even if local text stays visible. Withdrawal
and lost access also lock writes with child-specific feedback. Navigation/close
warnings and lifetime cancellation protect work; answers are never persisted
in a browser cache.

`ActivityWorkspace` owns the template and activity URLs, one form buffer for
plan and per-activity input, derived canonical projections, source confirmation
and twenty coalesced Undo entries; its pure buffer transitions live in
`workspace-form`, and plan projections in `plan-projection`. The projections own
every form rule, and one `validateTree` attaches each issue to its field. ActivitySetup,
PlanEditor, TemplateChat, SourceReplacement and the document editor edit the
owner's Signal Forms and emit events; ActivityReview, GenerationStatus and
UnappliedResult present state and emit explicit actions. None owns a copied
draft or HTTP request, and phases, summaries and parent-language diagnostics
are derived, never stored. Each parent message
makes one correlated authoring request; local edits, Undo and cancellation
invalidate pending responses.

Generate, replace, adopt and release first flush a valid checkpoint. An
AI-extracted fixed source needs local confirmation, and changing a material's
source kind creates a new material identity. Template publication briefly locks
editing, uses `expectedVersion` and never writes an activity. Polling reads the
operation status before the draft checkpoint so a terminal result includes its
final commit, and pauses while the page is hidden. Only a revision confirmed by
the operation and its unchanged local edit fence is applied automatically; other
content stays an explicit reload offer. A checkpoint between the two GETs waits
for a later status read to confirm ownership. Lost start responses keep their key
and request for replay, and candidates pass a bounded editable-field mapping
before transfer.

`core/api/library-changes` owns each page's native change stream, pauses it while
hidden and exposes a refused connection for explicit retry. The library's three
resources refresh once running reads settle; one failed list does not hide the
others. Confirmed deletion updates only its list, while reset clears all three.

`draft-observer` owns one read lifecycle for SSE hints, active-operation polling
and explicit reloads. Hints during a read coalesce into one trailing read. Writes
cancel older background reads synchronously; explicit reconciliation survives
background suspension. Identity changes and page destruction cancel all reads.
Operation metadata follows even when content revision is unchanged. External
content is offered for explicit reload; locally followed generation retains its
edit fence and Undo behavior. Terminal operations stop polling but remain
readable on later hints for diagnostics and late usage. Template editing relies
on its publication conflict check.

The library separates drafts, templates and snapshots; a snapshot copy creates
a new draft without AI. The PWA caches assets only. One Playwright suite tests
the published app against a local provider, with service workers blocked so
routing observes every request. See the [UI guide](ui-guide.md) and
[verification](../README.md#verify). References: [IChatClient][chat],
[structured output][output], [Signal Forms][forms].

[chat]: https://learn.microsoft.com/en-us/dotnet/ai/ichatclient
[output]: https://openrouter.ai/docs/guides/features/structured-outputs
[forms]: https://angular.dev/guide/forms/signals/schemas
