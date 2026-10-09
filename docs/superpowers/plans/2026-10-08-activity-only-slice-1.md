# Activity-only slice 1 implementation record

**Implemented 9 October 2026.** This records the completed engine/API slice and
its cleanup; it is not a pending implementation checklist. The next work is
slice 2 in the [chat design](../../activity-chat-design.md#delivery-and-verification).
The matching frontend/backend cutover remains a single release.

## Implemented scope

- The concrete `LearningPlan` owns settings, generated/supplied sources, question
  formats, choice count and lengths. Schema version is 2; engine revision is 39.
  Derived stage requirements remain typed, with effective-value fingerprints.
  Defaults, controls and per-activity overrides are removed.
- Activity authoring uses `/api/ai/activity-plans`. Revision planning returns
  exactly one bounded answer, clarification or change. Server validation owns
  IDs, confirmed source fidelity and cross-field rules. Prompts and schemas use
  concrete activity requirements, without repair calls or subject-specific
  generators.
- `RevisionScope` derives required work from effective changes. Existing text
  rewrites run in plan order; only new texts use ideas/writing/polish. Questions
  rebuild, append, replace or retain validated survivors. Unsupported strict
  total-length allocation across calls requests clarification before generation.
- Create/Revise use the existing durable worker. Intermediate artifacts never
  change saved content. Final plan/document, chat, undo and terminal status
  commit together. Create uses at most four calls; Revise at most eight. Failure,
  cancellation, unknown outcomes and conflicts do not partially apply content.
- Drafts persist parent-only chat (100 turns) and one-level undo. Only applied
  content changes advance revision. Undo requires the resulting revision and
  serializes its chat read/write with concurrent operations. Manual save,
  adoption and release clear undo; replies, no-ops and failures preserve it.
- Manual saves preserve IDs, order, question formats and option counts. Source
  replacement requires matching confirmed text in plan and document. Active
  operations reject save/adoption/undo/release with 409. Family ownership,
  immutable snapshots and child answer-key isolation remain enforced.
- The current Angular workspace uses the concrete contract, saved requirement
  locks, durable Revise/chat/undo and fixed-structure content editing. Unsaved
  setup retains correlated authoring and local undo. Family reset moved to
  `DELETE /api/learning-data` under the Library feature.

## Ownership and cleanup

`TaskEngine` owns pure contracts, validation, scope, assembly and AI context.
Activities owns persistence and workflow transitions through DbContext directly.
Angular workspace/form helpers own the editable buffer; child editors emit
changes without copied drafts or HTTP calls. No repository, mediator, parallel
AI stack or workflow framework was introduced.

Removed:

- Template-oriented authoring model/schema and control/override reconciliation.
- Control/choice editors and disabled manual add/remove/reorder controls, their
  exclusive handlers/styles, and tests for the retired features.
- Unapplied-candidate transfer, which could produce content manual saves reject.
  Raw operation evidence, diagnostics and copying remain available.
- Unreachable saved-undo fallback, saved-draft local undo accumulation, temporary
  question IDs, stale format/length overrides and false optional plan fields.
- Manual-save branches for adding/removing items or changing source kinds.
  Test fixtures now own their setup instead of keeping those production paths.
- Unused draft/snapshot input columns and unread operation input fingerprint.

`AddActivityConversation` adds chat/undo. `RemoveUnusedActivityState` removes
obsolete storage; neither migration converts old plan JSON. Existing applied
migrations were not rewritten. Verification uses disposable storage; no manual
migration or reset command was run against the user's database. Development
startup still applies migrations as documented in README.

The cleanup also corrected README, architecture and AI guidance that still
claimed default/override inputs or intermediate saved-content checkpoints.
Historical live-model evidence remains explicitly historical.

## Verification

Use `scripts/verify.sh`, `scripts/publish.sh` and
`npm --prefix frontend run e2e`. The browser suite uses the published app,
disposable data and a local provider. No live paid AI was used.

Coverage includes scope/refusal/context contracts, exact sources, append and
mixed formats, eight-call boundaries and every failure position, chat/undo,
concurrent tabs, recovery, ownership, frozen snapshots, child workflows,
migration/model parity, keyboard use and 360px/200% text. Cleanup regressions
cover absent override storage, rejection of mismatched source echoes and
readable evidence without candidate transfer.

Final cleanup verification passed on 9 October 2026:

- `scripts/verify.sh`: 671 backend tests, 264 Angular tests, dashboard tests,
  formatting, Markdown, type checks and production builds.
- `scripts/publish.sh` and all 30 published browser tests.
- App TypeScript checks with `noUnusedLocals` and `noUnusedParameters`.

Ten tests exclusive to retired structural/candidate-transfer code were removed;
remaining editing/evidence checks and cleanup regressions pass. No identified
slice-1 cleanup blocker remains for starting slice 2. Live-model quality was
not reevaluated; future paid calls require an explicit agreed budget.

## Remaining work

**Slice 2:** implement the reading/editing canvas and adjacent activity chat,
initial source confirmation, targeting and manual-edit recovery offer. Retire
still-used `/templates` navigation/routes/library/publication state, the
standalone scoped-repair forms, four-step progress and staged text/question
controls as their replacements become usable. Keep status/Stop and evidence.

**Slice 3:** retire template endpoints/entities/DbSets/provenance and the replaced
operation kinds after all callers migrate. Keep GenerateQuestions for explicit
manual-edit recovery. Perform the coordinated learning-data reset and schema
cutover, retaining parent accounts, families and AI configuration. Verify an
empty queue and fresh create/resume/approve/assign/reset flows before restart.

No Git mutations are authorized. Coordinate any dev-server interruption or real
data reset separately. This implementation does not provide old-plan readers,
conversion adapters or automatic startup resets. Null handling for incomplete
forms and version/profile fences for queued work are required current behavior,
not compatibility support.
