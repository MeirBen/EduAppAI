# Activity-only slice 2 implementation record

**Status:** complete and verified, 9 October 2026.

**Scope:** the activity canvas/chat built on the verified slice-1 engine/API.
The [product specification](../../product-specification.md) and
[chat design](../../activity-chat-design.md) remain authoritative. Backend
retirement and the coordinated data cutover are covered by the
[slice-3 record](2026-10-09-activity-only-slice-3.md). The slices ship together.

## Implemented

- Prompt-first authoring with clarification, a read-only settings summary and
  exact source confirmation. Editing source text requires confirmation again.
  Saving persists the plan and bounded conversation without starting AI;
  incomplete drafts reopen at `/activities/:activityId`.
- One explicit Create saves first and completes missing content atomically.
  Content opens for reading; Edit uses the same fixed-structure buffer.
  Successful manual saving returns to reading. Failed/conflicting saves keep
  all edits and prevent dependent AI.
- Chat beside the canvas on wide screens and below it on phones. Text/question
  shortcuts select stable IDs, fill and focus the composer without sending.
  Removed targets block sending until cleared. Added sources require explicit
  confirmation and retain exact text through clarification, failure and reload,
  until incorporated or removed. Expired operation evidence requires re-entry;
  recovery never overwrites unsent local work. Persisted turns include committed
  notices and assumptions; suggestions only fill the request.
- One Stop action, readable content while work runs, durable Undo and exact
  operation-key recovery. Active work pauses editing, save, adoption and
  approval, including open source replacement fields. Failure/Stop retains the
  request; uncertain outcomes never trigger automatic calls. External changes
  preserve local edits and offer reload.
- Saved diagnostics and word counts beside content. Source/text edits offer
  question regeneration or validated adoption, including after reload.
  Explicit approval freezes a valid saved revision without AI or assignment.
  Editing approved content creates a new draft from its immutable snapshot.
- An activity-only library at `/activities`, with drafts, approved snapshots
  and one new-activity entry. Independent loading/errors, confirmed deletion,
  family change notifications and reset remain intact.

## Ownership and cleanup

`ActivityWorkspace` owns one plan/document buffer and all requests; the existing
observer owns reads and reconciliation. Reading, editing and chat components
render input and emit actions. Server-validated settings retain their canonical
shape; only editable sources need local plan validation. No new dependency,
backend project, store, repository or workflow abstraction was introduced.

Removed:

- Template routes, publication/version/provenance UI state, library queries,
  frontend template DTOs and API methods; no legacy redirects or adapters.
- The plan editor, length/settings controls, scoped-repair forms, staged
  generation actions, four-step progress and their exclusive tests/styles.
- Checkbox/count conversion state for read-only settings, old reconciliation,
  single-target operation projection and unused helpers/imports. Remaining
  plan buffer helpers live with the workspace that owns them.
- Duplicate cancellation controls and the source-confirmation path that cleared
  unsaved chat history. One bounded authoring thread becomes server-owned after
  saving. Navigation warnings also protect unsent text and clarification work.

Hebrew copy uses simple action names: **יצירת הפעילות**, **שמירת טיוטה**,
**עריכה**, **אישור הפעילות**. Settings collapse once content exists; technical
operation evidence stays under its disclosure. Focus follows the action that
owned it, and chat cannot steal focus from newly created content.

## Verification

The suite uses disposable databases and a local provider; no paid calls or real
learning-data reset. The user stopped their development server before full
verification; it remains stopped. No Git mutations were performed.

- `scripts/verify.sh`: 671 backend, 249 Angular and 23 dashboard tests,
  formatting, Markdown, TypeScript and production builds passed.
- `scripts/publish.sh` and all 30 isolated browser tests passed.
- Fresh independent code review found two source-lifecycle issues, a missing
  source-replacement lock and an unused Undo helper. All four are fixed, with
  seven regression cases; no review findings were deferred.
- Strict unused-local/parameter compilation, obsolete UI/route searches, icon
  usage and `git diff --check` passed.

Dependencies are unchanged. `npm audit` still reports 10 development-dependency
findings (6 high, 1 moderate, 3 low); dependency remediation is outside this
slice's scope. These are separate from the four resolved code-review findings.

Coverage includes source fidelity/confirmation, saved conversation, read/edit
switching, failed saves, target identity, reload-persistent recovery, Stop and
lost-response races, external edits/deletion, immutable previews/copies,
family/child isolation, keyboard focus, RTL and 360px/200% text. Retired feature
checks were removed; useful lifecycle coverage was migrated to the canvas.

## Review focus

- Unconfirmed or changed sources cannot silently become confirmed (task 1).
- Failed/conflicting saves preserve edits and block dependent AI (task 2).
- Removed targets never retarget or send automatically (task 3).
- Lost responses and Stop races reconcile without another AI call (task 3).
- Reload preserves recovery offers; snapshots stay immutable (tasks 2–5).

## Slice 3

Backend retirement and the guarded cutover command are implemented; see the
[slice-3 record](2026-10-09-activity-only-slice-3.md) for verification and the
separately coordinated learning-data reset. Shared engine stages remain in use
by Create, Revise and isolated evaluation. No old-plan conversion, compatibility
reader or automatic startup reset was added.

Coordinate any dev-server interruption or real reset separately. Ask for an
explicit budget before any live AI evaluation; isolated verification does not
establish live-model quality.
