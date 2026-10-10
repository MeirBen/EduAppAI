# Architecture

This guide describes the activity-only engine/API, canvas/chat and guarded
fresh-start migration. The [product specification](product-specification.md) and
[chat design](activity-chat-design.md) own the contracts. Deploy the matching
frontend/backend together and coordinate the [data cutover](../README.md#activity-only-cutover).

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
Kestrel bounds bodies to 3 MiB to allow 100 imported chat turns, including
escaped Hebrew JSON; validators enforce the smaller field and aggregate limits.

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
independently owns report, check and judge versions. Draft revisions are separate
concurrency counters.

## AI and persistence

All routes are under `/api`; writes enforce CSRF.

| Request                                   | Result                     |
| ----------------------------------------- | -------------------------- |
| `GET limits`                              | Server content limits      |
| `POST ai/activity-plans`                  | Unsaved proposal           |
| `GET activity-drafts`, `GET instances`    | Library pages              |
| `POST activity-drafts`                    | Named draft; retry replays |
| `PUT activity-drafts/{id}`                | Save a revision            |
| `POST activity-drafts/{id}/operations`    | Idempotent start           |
| `POST activity-drafts/{id}/adopt-content` | Accept stale content       |
| `POST activity-drafts/{id}/release`       | Review and freeze          |
| `POST activity-drafts/{id}/undo`          | Restore prior content      |
| `GET instances/{id}`                      | Frozen parent preview      |
| `DELETE activity-drafts/{id}`             | Draft and operations       |
| `DELETE instances/{id}`                   | Archive or delete snapshot |
| `DELETE learning-data`                    | Family learning reset      |
| `GET library/changes`                     | Change notes (SSE)         |

`EngineValidation` names every client-visible limit once; validators, their
messages, prompts and `ContentLimits` (served by `GET limits`) all read those
constants, so the client never hard-codes a limit.

`LearningPlan` is the sole authority for concrete settings, material sources,
question formats and lengths. `TaskRequestResolver` derives typed stage
requirements and effective-value fingerprints; no defaults, controls or input
overrides are stored. IDs and provenance belong to the application.

`AiGenerationService` uses `IChatClient` without identity/database access.
Authoring, revision planning, ideas, material writing, polish, questions, append
and scoped replacements use schema-constrained calls without tools or retry. The
polish is the only automatic edit: one minimal-edit call over freshly written
text in the writing schema, which `TaskAssembly` accepts only for the same IDs
under strict material checks, bumping only edited revisions. Supplied sources
are never edited, and questions are not polished. `TaskAssembly` copies supplied
sources exactly. `RevisionScope` derives required changes, preserves unaffected
valid content and requests clarification for unsupported length allocation.
Question replacement is atomic over prompt, options and key. `AiSchemas` builds
request-owned schemas with exact counts, allowed IDs and the same
`EngineValidation` constants used by prompts and validators. The OpenRouter
adapter owns transport/configuration and sends the native SDK response format.
Responses must finish normally and pass size, shape, numeric and domain checks;
question text fields are trimmed first, the only normalization applied; see [AI
configuration](ai.md#configuration).

`AiPrompts` composes stage/shared rules; `MathPromptGuidance` owns authoring-time
math interpretation. Schema descriptions own source roles and answer-format
capabilities. All subjects use the same stages and authoritative validators.

`MaterialIdeas` validates five bounded ideas and selects the lowest estimated
overlap with recent family ideas. An application-owned draw breaks ties: the
worker's operation ID keeps selection stable within an operation. Only the
selected idea reaches the writer; manual edits and the polish retain it as
provenance, an AI rewrite that changes the text clears it. Supplied sources and
question-only operations skip ideas.

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

`Features/Activities` owns the saved plan/document, chat, one-level undo,
content identity, adoption and release. Engine validators derive diagnostics.
Manual edits preserve item IDs/order, formats and option counts; requirements
change through Revise. Confirmed source replacement updates plan and document
atomically, leaving the questions and generated texts stale. Editable DTOs
exclude provenance and acceptance, and child/snapshot DTOs exclude chat and undo.

A changing Revise or GenerateQuestions stores one prior plan/document checkpoint.
Undo requires its resulting revision, restores content, advances revision and
consumes the checkpoint. Manual save, adoption and release clear it; replies,
no-ops and failures preserve it. Undo serializes its chat read/write with
operation admission/completion even when a reply leaves content revision alone.

An EF concurrency token guards each draft write. One `SaveChanges` creates the
snapshot and marks the draft terminal, and a unique source-draft index prevents
duplicate releases; replay returns the original snapshot, or 410 if it was
deleted. Source draft/snapshot IDs are detached provenance, so draft deletion
never erases snapshots. Family reset deletes learning records atomically while
keeping accounts.

Checked-in migrations add conversation storage and remove unused input columns,
the unread operation fingerprint and template tables/provenance. The activity-only
migration requires empty learning records before any schema change. Ordinary
migration, including development startup, rejects populated old data without
deleting it. The explicit
`--activity-only-cutover` command runs without the server/worker, transactionally
clears learning records at the predecessor schema, then migrates; it is a no-op
once applied. It never reads/converts old plan or operation JSON. Accounts,
families, external AI configuration and keys stay intact. Historical migrations
remain unchanged. Development applies safe migrations; Production requires a
management command. Tests use disposable storage, real `Program` composition
and isolated providers; worker tests disable polling to drive transitions.

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
checkpoint and disposes them before each AI call. `AiCapacity` owns the two
provider slots: planning replies busy when both are in use, while the worker
waits for a slot before claiming a stage, so local load delays accepted work but
never fails it.

Starts share one per-family rate limiter with authoring. Owned key replay comes
before revision, active-state and budget checks. Admission atomically enforces
queue limits, captures required context and appends the parent chat turn.
One active operation locks save, adoption, undo and release with 409.
Cancellation commits before transport is signalled; late output cannot apply.
Draft deletion cascades operation evidence and keys.

Create generates missing texts through ideas/writing/polish, then questions.
Revise first plans a reply, clarification or bounded change; existing rewrites
run in plan order, new texts alone receive writing/polish, then questions are
rebuilt, appended or replaced. Intermediate checkpoints hold private working
artifacts only. Final success applies plan, document, chat notice, undo and
terminal status in one transaction under the active-operation/revision fences.
Failures and cancellation leave saved content unchanged. Only applied content
changes advance the draft revision; replies and status transitions do not.

Create, Revise and the explicit GenerateQuestions recovery action are the only
public operation kinds. Writing/replacement methods remain engine stages shared
by these operations and isolated evaluation, never separate admission paths.

On restart, engine/schema/profile-matching queued stages resume; uncheckpointed
calling steps become unknown and are never retried. Profile fingerprints exclude
credentials, so key rotation alone does not invalidate queued work.

Operation artifacts are bounded to 2 MiB, with eight steps and a reserved 16 KiB
summary budget. After seven terminal days, startup/hourly cleanup expires up to
32 bulky artifacts per pass, retaining keys, outcomes and known usage until
draft deletion. `GenerationOperationOptions` owns these bounds.

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
preserves its current metadata; details stay parent-only. Profile deletion
checks revision/history, device-record deletion checks inactivity, and reset
removes owned dependents before referenced content, all within transactions.

`Features/Assignments` owns one assignment per child/snapshot pair. Transactions
serialize creation, withdrawal, restore and snapshot removal; composite foreign
keys enforce family ownership and retention. Replay precedes new-create eligibility.
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

AI permits two concurrent calls per process (`AiCapacity`) and ten requests per
family per minute; a call holds its slot until it ends, and cancellation reaches
the provider. The transport
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
protect edits. Conflict copy belongs to its feature.

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

`ActivityWorkspace` owns `/activities/new`, `/activities/:activityId` and one
plan/document buffer. Its `workspace-form` owns buffer transitions and
`plan-projection` validates editable source input; read-only settings retain
the canonical server shape. Child editors use the owner's
Signal Forms and emit changes; they do not copy drafts or issue HTTP requests.
Settings are read-only; chat changes requirements. The canvas starts in reading
mode, and an explicit edit opens fixed titles, instructions, bodies, questions,
options, answers and points. Edits save themselves while the editor stays
open; finishing a valid edit returns to reading. Field problems and server word
counts stay beside content.

Every local change schedules one autosave after a short pause; it writes only a
valid buffer and runs through the same serialized write that creation, chat,
adoption and approval flush, so two writes never share an expected revision.
Autosave waits while a chat request is authored. The first save creates the
draft under an identity the page chose, and locks the page like any draft
request, so no edit or authoring request crosses it; a retry after a lost
response replays that draft instead of adding another. Later saves never lock
the fields. A response replaces the buffer only when nothing was typed since it
was sent; newer edits stay and save next. A conflict pauses autosave until a
saved version replaces the buffer. Leaving first saves a waiting valid change.

`ChatSession` owns local authoring, composer and targets; workspace
buffer/revision fences still control whether proposals apply. Unsaved setup
uses correlated authoring and bounded local undo. Its single bounded
conversation goes whole with each planning request, where the engine's
`ConversationWindow` picks what the model sees, and is imported when the draft
is created; subsequent chat and undo come from the saved draft, windowed by the
same rule. Setup source confirmation preserves exact text and
must be repeated after an edit. Chat shortcuts set an ID-based target and focus
the composer without sending; removed targets block submission until cleared.
An admitted request leaves the composer; a failed or stopped one returns to it,
also after a reload. Native navigation warnings protect edits that cannot save
yet and unsent messages.

Creation, chat, adoption and approval flush one valid checkpoint; failed saves
preserve edits and prevent dependent AI. One Create completes missing content
atomically. During work the canvas stays readable, editing pauses and chat
provides Stop. Failed or cancelled requests remain available for an explicit
new attempt. The saved source/text diagnostics offer question regeneration or
validated adoption, including after reload. Approval requires a complete,
valid, clean saved revision, freezes it without AI and never assigns it.

Polling reads operation status before its draft. Only a confirmed revision
with an unchanged local-edit fence applies automatically; other content is
offered for explicit reload. Lost start responses retain the exact key/payload
for reconciliation. Failed candidates remain technical evidence and cannot
replace the buffer. Chat restores focus only for actions taken within it;
creation transfers focus to the new content when its button disappears.

`core/api/library-changes` owns each page's native change stream, pauses it while
hidden and exposes a refused connection for explicit retry. The library's two
resources refresh once running reads settle; one failed list does not hide the
others. Confirmed deletion updates only its list, while reset clears both.

`draft-observer` owns one read lifecycle for SSE hints, active-operation polling
and explicit reloads. Hints during a read coalesce into one trailing read. Writes
cancel older background reads synchronously; explicit reconciliation survives
background suspension. Identity changes and page destruction cancel all reads.
Operation metadata follows even when content revision is unchanged. External
content is offered for explicit reload; locally followed generation retains its
edit fence and Undo behavior. Terminal operations stop polling but remain
readable on later hints for diagnostics and late usage.

The `/activities` library separates drafts and approved snapshots; a snapshot
copy creates a new draft without AI. The PWA caches assets only; an open tab
checks for a newer build on each return and offers a reload, which each page's
unload warning still guards, and a cache that can no longer load asks for one.
One Playwright suite tests the published app against a local provider, with
service workers blocked so routing observes every request; only the update test
lets the worker meet a second build. See the
[UI guide](ui-guide.md) and [verification](../README.md#verify). References:
[IChatClient][chat], [structured output][output], [Signal Forms][forms].

[chat]: https://learn.microsoft.com/en-us/dotnet/ai/ichatclient
[output]: https://openrouter.ai/docs/guides/features/structured-outputs
[forms]: https://angular.dev/guide/forms/signals/schemas
