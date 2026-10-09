# Activity-only slice 3 implementation record

**Status:** implementation, verification, independent review and the approved
local learning-data cutover complete, 9 October 2026. The app remains stopped.

**Scope:** finish backend retirement and prepare the explicit fresh-start cutover
for the activity-only flow from [slice 1](2026-10-08-activity-only-slice-1.md) and
[slice 2](2026-10-09-activity-only-slice-2.md). The
[product specification](../../product-specification.md#activity-only-cutover)
and [chat design](../../activity-chat-design.md) remain authoritative. All three
slices ship together.

## Implemented

- Removed the template feature, endpoints, DTOs, entities, DbSets and model
  configuration. Activity creation accepts a plan with optional chat or an
  owned snapshot. Draft/snapshot DTOs and storage no longer carry template
  version provenance; strict JSON rejects removed fields, including null.
- Only Create, Revise and GenerateQuestions admit public generation operations.
  Removed GenerateMaterials, ReplaceMaterial and ReplaceQuestion admission,
  targetId/instruction request fields, obsolete worker branches and the unused
  writing fallback. Engine revision 40 fences the changed stored artifacts.
- Kept shared engine writing/replacement methods: Create, Revise and isolated
  evaluation use them. Evaluation already calls the engine directly; it needs
  no compatibility path or duplicate pipeline.
- Added the guarded `ActivityOnly` migration. It rejects any pre-cutover
  learning records before dropping template tables and provenance columns.
  Fresh databases migrate normally; historical migrations remain unchanged.
- Added `--activity-only-cutover` to the existing local management path. With
  the app and worker stopped, it migrates to the predecessor schema,
  transactionally clears all families' learning records and child access, then
  applies the new migration. It retains Identity, families, keys and external
  configuration, starts no HTTP server/worker and makes no AI call.
- Repeating the command after migration does nothing, preserving new work. A
  failed reset rolls back its deletes. If the subsequent schema update fails,
  learning records remain empty and an explicit rerun completes the migration.
  Ordinary migration, including development startup, never performs the reset.

## Ownership and cleanup

The existing feature endpoints, DbContext and durable worker retain ownership;
no new dependency, production project, repository or execution layer was added.
Family/child authorization, immutable reviewed/assigned content, answer-key
isolation, atomic Create, revision/idempotency fences and cancellation remain.

Removed publication-only tests and old migration-preservation expectations.
Moved auth, CSRF, validation, notifications and lifecycle tests to activity
routes. Generation helpers now drive one atomic Create; explicit manual-edit
recovery still uses GenerateQuestions. Current Revise/engine tests cover scoped
replacement. Removed unused imports and stale documentation; remaining template
references describe retirement, historical migrations or Angular markup.

## Verification

Automated verification uses isolated providers and disposable databases. No
paid AI calls or Git mutations were performed. The user stopped the development
server before backend/schema edits and full verification; the real reset below
was separately approved after verification and review.

- New retirement and cutover tests failed before implementation, then all 13
  passed. They cover retired routes/contracts, ordinary migration rejection,
  transactional rollback, retained accounts/keys, revoked child access and
  repeat-command safety across families.
- `scripts/verify.sh` passed: 668 backend, 249 Angular and 23 evaluation UI tests;
  build, formatting, Markdown and TypeScript checks passed.
- `scripts/publish.sh` and all 30 isolated browser tests passed.
- Fresh independent review found no critical or important code issues. Its
  stale documentation status finding is corrected. Active-reference searches
  confirmed that retained engine stage APIs have current callers. Final
  formatting, Markdown and `git diff --check` checks passed.

The final checks cover create/resume, atomic failure/recovery, approval,
assignment, family reset, fresh setup, child isolation, keyboard use and mobile
text scaling. They do not establish live-model educational quality. Dependencies
are unchanged; the 10 previously recorded development-dependency advisories
remain outside this slice's scope.

## Local cutover

The user explicitly approved the reset of `backend/FamilyLearning.Api/data`
after reviewing its counts. The verified published management command exited 0
with the app, worker and watchers stopped.

- All eight learning tables are empty, including the operation queue. Template
  tables and template provenance columns are absent; the current migration is
  `20261009124947_ActivityOnly`.
- Checksums of all Identity/family rows, Data Protection key files and
  configuration match their pre-cutover values. The one parent account and
  family remain; child access was cleared.
- SQLite integrity and foreign-key checks passed. No test content was inserted
  into real storage and the app was not restarted.

No identified slice-3 blocker remains. For another existing installation, follow
the [cutover procedure](../../../README.md#activity-only-cutover) with separate
reset coordination before running the matching frontend/backend release.

No old-plan conversion, compatibility reader or automatic startup reset is
needed. Further live AI evaluation requires an explicitly agreed budget.

## Final cleanup

The follow-up audit removed the unused manual-setup schema propagation and its
undo/form helpers, schema discovery from AI configuration status, an unused
format-label export and retired operation-stage/outcome labels. Existing test
callers use their canonical plan fixtures. Canonical plan/schema validation and
queue version fences remain intact.

A second stability pass derives the workspace baseline from its accepted draft
with Angular `computed`, removes the obsolete second-save branch and duplicate
empty-buffer helper, and reuses authoring limits in validation and output schemas.
Material identity checks no longer carry unused categories. Operation admission
reuses each owner's parsed plan, and Create no longer builds an unused undo
checkpoint. Existing ownership and concurrency boundaries remain unchanged.

Aligned TypeScript and XML comments with current plan ownership, operation
checkpoints, source acceptance, question identity and collection-sharing rules.
Consolidated historical evaluation evidence under `artifacts/evaluations/`,
preserving raw files by checksum and extracting unique frozen inputs before
removing old builds. Removed slice scratch files and superseded verification
logs; [evidence retention](../../ai.md#costs-and-retained-evidence) owns the policy.

Final verification passed again: 668 backend, 249 Angular, 23 evaluation UI and
30 browser tests, plus publishing and documentation checks. Independent review
has no unresolved findings. A read-only check reconfirmed the completed local
cutover and database integrity; no real data changed during cleanup.

Artifacts decreased from about 1.29 GiB to 32 MiB. All 2,106 retained original
evidence files matched their checksums; 112 embedded input copies became 51
unique preserved fixtures/schemas. The generated app was removed after browser
verification; publishing regenerates it. No paid calls or Git mutations occurred.
