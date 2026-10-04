# Live Change Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` to implement the
> reviewed tasks. Checkboxes track implementation and verification. The user
> handles Git.

**Status:** Implemented and verified, 4 October 2026.
Scope covers the Angular library, draft
editor, generation progress and immutable task snapshots, plus their server
queries, writes, worker transitions and family change stream.

**Goal:** Make live updates reliable with fewer competing readers, clear
ownership and no accidental replacement of local work.

**Architecture:** Keep content-free SSE invalidation, authoritative HTTP reads
and polling only for a followed running generation operation. Consolidate draft
observation inside the activity feature; keep editing decisions in the workspace
and publication beside server commits.

**Tech stack:** Existing .NET 8, ASP.NET Core, EF Core/SQLite, Angular 22.2,
HttpClient/httpResource, signals, RxJS, native EventSource, xUnit and Playwright.
No new runtime dependency or migration.

**Spec:** The design and behavior contract below, together with
[architecture](architecture.md), [UI guidance](ui-guide.md) and
[commenting guidance](commenting-guide.md).

## Original assessment

The transport choice is appropriate. The main problem is overlapping ownership
of reads and reconciliation in the editor. Fix this before building more live
workflows on top of it; a transport rewrite would leave the important races
unsolved.

1. **High — cancellation can regress in the UI.** `activity-workspace.ts`
   applies every polling status while `cancelGeneration` independently applies
   its POST response and another draft GET. A targeted component probe held an
   old poll until after cancellation, then returned `calling`: the Cancel button
   returned. This does not restart server generation.
2. **High — external operation starts are ignored.** `draft-elsewhere.ts` keeps
   only `revision`; `ActivityDraft.StartOperation` changes `ActiveOperationId`
   without changing content revision. A second probe returned that change and
   generation remained enabled. Server admission still protects the draft, but
   the UI does not follow the operation until a manual reload.
3. **Medium — terminal stream failures are silent.**
   `LearningApi.libraryChanges` completes its inner stream on `CLOSED`, with no
   visible failure or explicit reconnect. Updates can stop until visibility
   changes or navigation recreates it. Reconnecting network errors are a
   different state.
4. **Medium — some worker commits are unannounced.**
   `GenerationWorker.ClaimAsync` commits `queued` to `calling` without publishing;
   `PurgeAsync` expires diagnostics without publishing. Polling masks the claim
   gap for the initiating tab; terminal diagnostics can remain stale.
5. **Medium — one list failure hides all library sections.**
   `activity-library.html` gates all three sections on one aggregate
   loading/error state, including otherwise available content.
6. **Improvement — reads overlap and can carry large payloads.** Active polls
   read the complete operation, including artifacts bounded to 2 MiB, then the
   complete draft. SSE checks, manual reload and cancellation reads have
   separate lifecycles. Consolidate these before adding another API contract.

Additional lifecycle gaps found by inspection: hiding the page stops the poll
timer but not its already-running inner HTTP chain; any poll error ends that
subscription. `receiveCheckpoint` compares only the accepted revision, not an
already-offered newer result. The implementation tests below must pin these
boundaries instead of relying on timing or a numeric ordering of statuses.

The existing baseline passed `scripts/verify.sh`. Both targeted review probes
failed their intended behavior assertions; their temporary spec was removed.
That review changed no production code. Implementation and verification below use
only isolated providers; no paid AI calls are needed.

## Keep

- `Features/Library/LibraryChanges`: family-only fan-out, capacity-one channels,
  coalesced content-free hints, 16 streams per family and five-minute lifetime.
  Its process-local delivery matches the application's single-process worker.
- Initial SSE catch-up, reconnect catch-up, visibility handling and a pending
  refresh after an in-flight read. A hint means state **may** have changed;
  duplicate hints are valid. Do not remove the initial hint to save one GET.
- The endpoint publication filter and explicit worker publication. These are
  different transaction owners, not accidental duplication. Publish after
  successful commits; do not infer domain changes from a DbContext interceptor.
- `httpResource` for library lists and HttpClient for explicit writes. Keep the
  small `pageVisible` and `whenIdle` helpers where useful. Plain `exhaustMap`
  alone would drop a trailing invalidation that arrived during a read.
- Workspace buffer, accepted checkpoint, pending newer checkpoint, Undo and
  local edit fence. They have different meanings; reducing them to one object
  would lose protection for unsaved work.
- Immutable template versions and released task content. List membership can
  change; snapshot content is not a live editable entity.

## Design and ownership

| Owner                | Responsibility                                       |
| -------------------- | ---------------------------------------------------- |
| Server writers       | Transactions, ownership, post-commit publication     |
| `Features/Library`   | Bounded family invalidation delivery                 |
| `library-changes.ts` | EventSource lifetime and connection state            |
| `draft-observer.ts`  | Current draft reads, coalescing and cancellation     |
| `ActivityWorkspace`  | Commands, edit protection and content reconciliation |
| `ActivityLibrary`    | Three list resources and section presentation        |

The stream owns no content or auth redirects. The observer owns no forms,
Undo, focus or writes. The workspace delegates background request machinery;
the library does not acquire generation state or a second entity cache.

### Behavior contract

1. Keep at most one background read chain for the current draft. Hints arriving
   during it request one trailing read. Poll every two seconds only while the
   followed operation is running; stop the timer at terminal state, but keep SSE
   available for later metadata/usage changes.
2. Beginning a workspace command must synchronously invalidate/cancel its older
   background read. Resume observation after the command settles, including on
   failure. Use HttpClient unsubscription and component lifetime; do not rely
   only on a later signal effect to fence a just-arriving response.
3. Explicit reload and post-cancellation reconciliation use the same read logic
   while background observation is suspended. A hidden page pauses background
   work, including its in-flight reads. Showing it performs catch-up. Destroying
   the page or changing draft identity disposes the old reader and stream.
4. For a known operation, read its status before the draft. A terminal response
   must be followed by a fresh draft read, so its final committed content cannot
   be missed. These GETs are not an atomic database snapshot. If the returned
   draft names another operation, follow that identity rather than treating the
   old operation as current.
5. Reconcile operation identity/status and release/deletion metadata even when
   content revision is unchanged. Content revisions remain content concurrency
   tokens, not notification counters. Do not rank statuses: `calling` materials
   to `queued` questions is a valid transition within one operation.
6. Keep external content changes as an explicit reload offer, even for a clean
   buffer. Discovering an external operation updates progress and busy controls;
   it does not grant permission to replace the form. Preserve automatic results
   only for the existing locally followed generation flow and its edit fence.
7. Accepted/offered content never moves backward: compare a candidate with both
   the accepted and pending revisions. A metadata-only update must not reset
   Undo, dirty state or focus. A draft 404 preserves local text and shows deletion;
   an operation 404 must not be misreported as draft deletion.
8. A read failure leaves local work intact and exposes a retry. A later hint,
   visibility return or explicit retry may read again; no failed GET should kill
   the entire observer. Keep the two-second cadence for a running operation
   after transient network/5xx failures; stop it on other HTTP failures.
   Writes and AI starts are never automatically retried by observation.
9. Stream `CONNECTING` leaves reconnection to EventSource. Terminal `CLOSED`
   exposes an unavailable state and explicit retry. Do not guess the HTTP status
   from EventSource's error event or add auth behavior here. On retry, reread
   current state and reconnect; the initial server hint still closes the gap.
10. A failed list shows a section error/retry while successful lists remain
    usable. Keep available rows during ordinary resource reloads. Do not create
    a parallel stale-data cache to retain the failed resource's previous value.

The current DTOs cannot identify an operation that starts and finishes entirely
between two observations when its final content revision is unchanged and
`ActiveOperationId` is again null. This plan follows current state and known
operations; complete cross-device operation history is a separate product/API
requirement, not something SSE hints can guarantee.

## Global constraints

- No Git mutations, paid AI calls, migrations, package upgrades, auth changes,
  new global store, generic event bus, SignalR, outbox or repository layer.
- Preserve server family/child boundaries, parent-only answer keys, immutable
  content, bounded validation, cancellation, UTC and existing HTTP contracts.
- Keep Hebrew/RTL, native accessible controls, keyboard focus and mobile/text
  scaling. Connection problems belong beside current refresh/error controls;
  do not expose SSE, polling or HTTP jargon in product copy.
- Keep form and generation rules in their existing owners. Do not restructure
  unrelated portions of the large workspace solely to reduce its line count.
- Change comments and architecture documentation with the owning code. Remove
  replaced implementations and callers in the same task; no compatibility
  wrappers around internal helpers that no longer have consumers.

## Review focus

Directly test these cases in the owning task:

1. Commit visible before notification, family isolation and coalescing (Task 1).
2. Native reconnect versus permanently closed stream and page teardown (Task 2).
3. Poll/cancel ordering, same-revision metadata and valid stage transitions
   (Task 3).
4. Local edits, competing checkpoints, identity changes and partial read failures
   (Task 3).
5. One failed list, deletion during refresh and two-tab convergence (Task 4).

## Implementation tasks

### Task 1: Complete server publication at existing commit boundaries

**Modify:** `backend/FamilyLearning.Api/Features/Activities/GenerationWorker.cs`
and the change-note section of `docs/architecture.md`.

**Tests:** `tests/FamilyLearning.Api.Tests/Integration/LibraryChangeTests.cs`
and `GenerationRecoveryTests.cs`; reuse `GenerationHarness`.

**Interfaces:** Keep `LibraryChanges.Publish(Guid familyId)` and the SSE wire
format unchanged. No new event types, operation fields or database counter.

- [x] Add a test that holds the isolated provider after a successful claim:
      consume the start hint, run the worker, observe another hint, then assert
      GET returns `calling` before allowing provider completion. It must fail
      against the current missing publication.
- [x] Add retention coverage: advance the test clock, purge, observe a family
      hint and assert diagnostics expired while outcome/usage remain. Keep
      existing bounded-batch assertions.
- [x] Publish after the successful claim commit. For purge, project only IDs
      and family IDs, retain the 32-row bound and publish distinct selected
      families after a successful update when rows changed. Do not materialize
      artifacts. Concurrent deletion may produce a harmless redundant hint;
      do not add transactions merely to make hints exact.
- [x] Expand publication coverage for successful save/start/cancel/release,
      template publication/deletion, snapshot deletion and reset; rejected and
      foreign-family writes must not notify the caller's stream. Exercise
      checkpoint, recovery and late cancellation usage publication. Test the
      capacity-one channel directly for a burst; do not count exact SSE frames.
- [x] Run `dotnet test --filter 'FullyQualifiedName~LibraryChangeTests|FullyQualifiedName~GenerationRecoveryTests|FullyQualifiedName~GenerationRaceTests'`.
      All targeted tests must pass.

### Task 2: Give the existing stream an explicit recoverable lifetime

**Create:** `frontend/src/app/core/api/library-changes.ts` and its `.spec.ts`.

**Modify:** `core/api/learning-api.ts`, `core/api/event-source.fixture.ts`,
`features/library/activity-library/activity-library.ts` and its HTML,
`features/activities/activity-workspace/activity-workspace.ts` and its HTML,
and `draft-elsewhere.ts` in the workspace folder.
All frontend paths in this task are under `frontend/src/app/`.

**Interface:** An injection-context factory `libraryChanges()` returns
`changes: Observable<void>`,
`state: Signal<'paused' | 'connecting' | 'connected' | 'unavailable'>` and
`reconnect(): void`. One page owns one instance and one subscription; no root
singleton connection, reference counting or global connection registry.
The workspace creates its instance and passes its `changes` observable into
`draftElsewhere`; Task 3 replaces that consumer without opening another stream.

- [x] Add failing tests for `CLOSED` becoming unavailable, explicit reconnect
      creating exactly one new source, `CONNECTING` retaining native retry,
      visibility catch-up and disposal closing the source. Extend the fake only
      with the native lifecycle events these tests need.
- [x] Move EventSource handling out of LearningApi into this owner, retaining
      `/api/library/changes?ngsw-bypass`. Wire both consumers directly and remove
      the old method and transport-only imports; do not keep a forwarding layer.
- [x] Add a short Hebrew connection status and retry beside existing page
      refresh controls. Retrying must refresh visible state as well as reopen
      the stream; it must not cause navigation, generation or write replay.
- [x] Run `npm --prefix frontend test -- --watch=false`. The stream and existing
      library/lifecycle tests must pass, including no second connection or busy
      retry loop after a terminal error.

### Task 3: Consolidate draft observation and fence commands

**Create:** `frontend/src/app/features/activities/activity-workspace/draft-observer.ts`
and focused `.spec.ts` coverage for request lifetime/order.

**Modify:** `activity-workspace.ts`, its template where status requires it, and
`activity-lifecycle.spec.ts` in that same folder.

**Remove:** `draft-elsewhere.ts`, `operation-polling.ts` after migrating their
callers. Remove `pollRefresh` and the separate polling constructor effect.

**Interface:** `observeDraft` is a component-owned injection-context helper.
Its inputs are the current draft/operation IDs, background eligibility,
command-busy state, the page's
Task 2 change observable, and callbacks for an observation or read error.
An observation is
`{ draft: ActivityDetail; operation?: GenerationOperation }`. Expose only
`suspend(): void` to synchronously cancel obsolete background work and
`read(acknowledged?: GenerationOperation): Promise<DraftObservation>` for explicit
reload/reconciliation. The user approved reusing the cancellation response,
avoiding a duplicate operation GET. Background eligibility is separate from
identity so a released draft can still be explicitly reloaded. Internal
SSE and timer triggers use that same query path. Keep request subscriptions and
pending refresh state here, and existing editing/operation presentation signals
in the workspace; do not add a copied domain store.

- [x] Add regression tests that hold a poll, acknowledge cancellation, then
      attempt late delivery; Cancel must stay absent and no AI start occurs.
      Add a same-revision external `activeOperationId` response; controls become
      busy, status is fetched and the existing form/Undo remain intact.
- [x] Implement one bounded read lifecycle using the Task 2 stream and existing
      HttpClient APIs. Wire `suspend()` at the beginning of `runDraftRequest`,
      before awaiting any write. Commands retain ownership of writes; observer
      busy state pauses only background reads, not their explicit `read()`.
      Background callbacks cannot apply an interrupted request. Prefer native
      unsubscription over a new generic request/version framework.
- [x] Route background observations, explicit reload and cancellation results
      through the workspace's reconciliation policy with an explicit distinction
      between applying an approved reload and offering external content. Update
      operation metadata independently of content revision; keep local-start
      recovery keys, edit fencing, Undo and focus in the workspace.
- [x] Add tests for `calling/materials` to `queued/questions`, terminal status
      followed by a fresh checkpoint, same-revision failure/late usage, newer
      pending revision followed by an older candidate, local edits during reads,
      external release/deletion, and an operation 404 with an existing draft.
- [x] Test a burst during an in-flight read produces one trailing read; hidden,
      destroyed or changed-identity pages cancel old reads; a transient read
      failure remains recoverable. Preserve the existing lost-start-response
      test: observation must never invent or replay a new start key.
- [x] Run `npm --prefix frontend test -- --watch=false`; the observer and all
      workspace cases must pass. Remove obsolete comments, helpers and imports;
      do not leave both old and new read paths active.

### Task 4: Contain library failures and verify the complete flow

**Modify:** `frontend/src/app/features/library/activity-library/activity-library.ts`,
its HTML/spec, `frontend/e2e/parent-workflow.spec.ts`,
`frontend/e2e/activities/activity-lifecycle.spec.ts`,
`docs/architecture.md` and relevant live-update comments.

**Interfaces:** Retain the three existing `httpResource` instances, list APIs,
confirmed local deletion updates and the Task 2 stream. No combined library DTO
or shared draft/list cache.

- [x] Add a test where one background list GET fails and the other two succeed;
      successful sections and links must remain usable. Render each section
      from its own resource state; a failed request is not an empty list.
- [x] Preserve coalesced refresh and focus after deletion. Test a pending list
      request during confirmed deletion plus a later server hint. Let Angular's
      resource update cancel its obsolete load; do not add a second tombstone or
      revision system. Correct comments that currently imply no later refetch.
- [x] Extend the isolated two-page workflow: external save/start/cancel/complete,
      edit protection, release/deletion, hidden-page catch-up and stream failure
      followed by retry. Assert convergence and preserved text, not sleep-based
      timing or exact notification counts. Use the local provider only.
- [x] Run `./scripts/verify.sh`, then `./scripts/publish.sh` to refresh the
      browser test build, then `npm --prefix frontend run e2e`. Require all
      checks to pass. Record request counts during a held operation and a burst:
      one current read chain, one pending refresh, no hidden/background writes,
      no timer after a terminal operation and no leaked stream on navigation.
- [x] Review the final diff for orphaned helpers, duplicated request/state paths,
      ownership leaks, misleading comments and unnecessary abstractions. Keep
      unrelated auth, AI prompts and child-flow implementation untouched.

## Approved review correction

The final review found that a local edit fence alone cannot identify which writer
produced a newer draft. The user approved tightening the existing operation
revision contract: `ExpectedRevision` advances after each accepted checkpoint,
and after cancellation only when the draft still matches that operation. An
external save never becomes an operation-owned revision. This changes no field,
endpoint or schema and keeps start-key fingerprints unchanged.

The workspace applies content automatically only when both the local edit fence
and operation revision match. A checkpoint committed between the status and draft
GETs stays offered until a later status confirms ownership. The same pending
revision can then be accepted; external saves and cancellation after an external
save keep the explicit reload boundary. Existing terminal rows with older revision
metadata remain safe: their content is offered for explicit reload.

Regression tests reproduced the unannounced final/cancellation revisions, external
save race and a stale reload button after deletion before their fixes. Browser
coverage also exposed a test-helper race; a second generation now waits for the
new operation identity rather than matching the previous URL.

## Verification and cleanup

- `scripts/verify.sh`: passed; 478 backend, 179 Angular and 23 dashboard tests,
  locked restores, builds, format checks, Markdown lint and TypeScript checks.
- Local `scripts/publish.sh` and all 27 isolated Playwright workflows: passed,
  including two-page generation/cancellation/release, edits, visibility catch-up,
  stream retry, deletion and existing authentication/keyboard/mobile regressions.
- A held draft read plus 20 hints produces exactly one trailing read; cancellation
  reuses its acknowledgement, hiding cancels background HTTP, terminal status
  stops the timer, and disposal closes the stream. These counts and lifetimes are
  asserted in the stream/observer tests with controlled events and clocks.
- Independent final review's external-save finding was reproduced and fixed with
  server/UI regressions. The deletion-after-offer failure also passed after its
  fix. Removed the old draft/polling helpers, transport method, dead reload branch
  and redundant cancellation wiring; persistent loading regions follow UI guidance.

Generic 502–504 error wording remains a separate, unapproved correction. The
pre-existing Markdown lint dependency advisory remains outside this change at the
user's request. No paid AI calls or Git mutations were made.

## Deferred choices

SSE-only operation progress would remove a timer but make stream availability
the only automatic progress path. Full event payloads/SignalR would enlarge the
protocol and ownership surface. Keep the current hybrid model with one reader.

A lightweight operation status projection may reduce artifact transfer, but
introduces another response contract and detail-fetch lifecycle. First measure
payloads after duplicate reads are removed; add it only if the measured cost
justifies it. Do not optimize by fetching a draft only when status changes:
content or metadata may change while status remains the same.

No new logging platform is required. Existing worker events remain server-owned;
connection/read failures become visible and testable in the UI. Do not log draft
content, answer keys, prompts or credentials to diagnose refresh behavior.

## Framework references

Angular documents cancellation through HTTP unsubscription, which is the basis
for disposing obsolete reads here: [HttpClient requests](https://angular.dev/guide/http/making-requests).
Keep resource reads and explicit writes separate as described in
[httpResource](https://angular.dev/guide/http/http-resource); use
[resource lifecycle](https://angular.dev/guide/signals/resource) rather than a
parallel list cache.

Native reconnection and terminal connection failure have different behavior in
the [EventSource standard](https://html.spec.whatwg.org/multipage/server-sent-events.html).
The connection state exposes that distinction without replacing the
browser's SSE implementation.
