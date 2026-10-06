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

`AiGenerationService` uses `IChatClient` without identity or database access.
Authoring, material ideas, material writing, questions and scoped replacements
are separate schema-constrained calls with no tools, automatic repair or retry.
Supplied sources bypass material AI and are copied exactly by `TaskAssembly`;
accepted materials survive a question failure, and a question replacement is
atomic over prompt, options and answer. `AiSchemas` builds request-owned schemas
with exact counts and allowed IDs, applying engine constants in code so they
cannot drift from the validators; prompts state limits from the same
`EngineValidation` constants. The OpenRouter adapter owns transport and
configuration and sends the schema through the SDK's native response format;
responses must finish normally and pass size, shape, numeric and domain
validation. See [AI configuration](ai.md#configuration).

Generated material starts with five bounded premise/structure ideas.
`MaterialIdeas` validates them and selects the lowest model-estimated overlap
with recent family ideas. Ties, the norm without relevant history, are broken by
an application-owned draw: the operation ID in the worker, so an operation
always selects the same idea while operations vary. Model estimates do not
guarantee novelty or quality. Only the selected idea enters the writer. The idea
is provenance, like the material's origin: manual edits keep it, and an AI
rewrite, which is written from an instruction rather than an idea, clears it.
Supplied sources and question-only operations skip ideas.

`GenerationHistoryReader` captures recent family content for every operation
after admission's idempotency checks: the current document, then at most 12
unreleased drafts and 12 snapshots, yielding up to eight distinct ideas and 12
question prompts clipped to 300 characters. Answers and identities are excluded.
The AI assesses relevance across similar topics, rather than relying on exact
subject labels. The history is frozen in operation artifacts; idea generation
receives only the ideas and question generation only the prompts. Concurrent
operations can share history; novelty is best effort, not uniqueness.

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
command. Tests use
disposable storage, the real `Program` composition and isolated providers;
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
rules with independent fixture adherence checks. Reports record explicit skips,
exact request and schema hashes, raw candidates and separate readiness outcomes;
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

`Features/Children` owns family profiles, one replaceable hashed activation slot
per child, and fixed 30-day device grants. Native cookie policies isolate the
Identity parent scheme from `Child`; session entry rejects active opposite-mode
cookies before changing CSRF tokens. Activation needs an anonymous browser and
a fresh identity-bound CSRF token afterward. No child endpoint uses AI.

`ChildAccess.FindAsync` reads current profile/grant state without EF tracking.
Writes acquire a short SQLite transaction before checking current access or
state, so committed disable/revocation wins over an earlier authentication.
Disabling invalidates pending codes and all grants; re-enabling restores neither.
Parent profile/device lists use SQL projection and bounded deterministic paging.
Family reset removes all owned sessions, assignments, child access and profiles
in its transaction before deleting their referenced content.

`Features/Assignments` owns one assignment per child/snapshot pair. Short write
transactions serialize creation, withdrawal and snapshot removal. Composite
foreign keys enforce family ownership and retain assigned snapshots. Replays
return the existing assignment before checking new-assignment eligibility.
Parent list projections read names and titles in SQL before paging; child reads
project explicit learner contracts without answer keys or generation metadata.
Read endpoints never start work. Session existence supplies `HasStarted` in SQL
without loading answer buffers into lists.

`TaskSession` uses its assignment ID as a restrictive primary/foreign key and
has its own concurrency revision. Child session APIs explicitly start, read,
save and submit complete answer buffers. Start/save leave assignment status and
revision unchanged; submission advances both revisions atomically. Every write
samples UTC and rechecks access after acquiring the transaction. Read-only
resume and idempotent start never reset submitted work.

`SessionValidation` bounds raw answers before recognizing a submission replay;
missing/blank values mean unanswered, and other text is preserved exactly.
`SessionScoring` compares validated numeric strings using their exact digits,
avoiding decimal rounding. Submission stores frozen awards and scoring policy
version 1 independently of generation revisions. Every answered short-text
question awaits parent review, including zero-point questions. Child session
responses expose saved answers, status, timestamps and a final total only when
complete, never keys or per-question awards. Identical submissions return their
stored result without changing revisions or timestamps.

Parent assignment `/result` and `/review` endpoints read the frozen document and
session evaluation. Review requires exactly the pending question IDs with bounded
integer awards and the session revision. A short transaction completes both the
assignment and session, recording the reviewer and UTC review time. Automatic
awards, answers, submission time and scoring policy stay unchanged. Raw validation
precedes replay; replay compares all original parent-graded rows, ignores their
order and returns the saved report even with an old positive revision. Different
grades cannot overwrite a completed result, and automatically completed work
cannot gain parent review metadata. These parent report contracts never serve
child routes. Archived content and disabled profiles retain their reports.

Snapshot archive time is mutable metadata; content JSON stays frozen. Ordinary
library lists hide archived rows, while owned parent previews, assignment reads
and generation history retain them. Removal keeps its 204 contract, and the UI
explains both outcomes because a concurrent assignment can require archiving.

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

Parent child-management and assignment pages own bounded list resources and
their local edit buffers. A paged child selector preserves its selection while
browsing profiles; assignment filtering includes disabled profiles for retained
history. Profile refreshes never replace unsaved edits. Activation codes exist
only in the owning page's memory and are cleared on selection or destruction.
Frozen previews create assignments and distinguish a new assignment from replay.

The parent result page reads frozen answers and evaluation without starting a
session. Pending grades begin blank, use each frozen question's integer bounds
and finalize together at the session revision. An unsuccessful write preserves
the local buffer and blocks resubmission until an explicit saved-result read.
Reading a completed result offers replacement of local grades after confirmation;
it never applies them implicitly. Completed reports are read-only. Route and
browser-close guards protect unsaved profile and grade edits. Feature-owned
conflict copy distinguishes profile, assignment and review state from template
publication; these requests never use AI.

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
