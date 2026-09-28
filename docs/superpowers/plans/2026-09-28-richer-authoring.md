# Richer Authoring Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task.

**Goal:** Make the existing parent template workflow support arithmetic and authored
mixed-question content with safe template revisions.

**Architecture:** Extend the current JSON envelope and add a pure content validator
and generation dispatcher. Reuse the existing version endpoint and frozen snapshots.
Use a focused Angular form with feature-local conversion helpers.

**Tech Stack:** .NET 8 / EF Core 8 / SQLite; Angular 22 / Signal Forms / Tailwind 4.

**Spec:** [Richer authoring design](../specs/2026-09-28-richer-authoring-design.md).

## Global constraints

- Existing JSON and multiplication behavior stay compatible; no database migration.
- Keep one production API project; no new packages, seeded data or paid AI calls.
- Hebrew UI, native controls, plain authored text, explicit LTR generated equations.
- All content and ownership checks run on the server before storage.
- Published versions and task snapshots remain immutable.
- Follow the repository commenting guide and keep docs with contract changes.

## Review focus

- Missing numeric points must not silently become zero; null nested values return 400.
- Edits must not erase custom math parameters or silently overwrite concurrent edits.
- Choice labels changed after selecting an answer must invalidate that answer.
- Nonmath numeric questions must retain authored punctuation and direction.
- Removing/reordering questions must preserve question IDs and the matching answers.

## Task 1: Task engine and HTTP contracts

**Files:** TaskEngine models/generators/validators; InstanceEndpoints;
tests/FamilyLearning.Api.Tests/TaskEngine and Integration.

**Interfaces:** `GenerationDefinition` adds optional `Content`; `TaskContentValidator.Validate`
returns field errors; `TaskGenerator.Generate(definition, parameters, seed)` returns
validated content for persistence.

- [x] Add failing arithmetic tests and HTTP static-content tests using real SQLite.
- [x] Run targeted tests and observe rejection of new modes/operations.
- [x] Extend mode validation, implement arithmetic operations, validate content, dispatch.
- [x] Cover malformed content, bounds, isolation and unchanged old drafts after edits.
- [x] Run the backend suite and formatting.

## Task 2: Parent authoring and editing

**Files:** frontend template feature, core API models/client, app routes, shared styles.

**Interfaces:** `TemplateDefinition.generation` is a discriminated union;
`LearningApi.publishTemplate(id, expectedVersion, definition)` uses the existing API.
Feature-local conversion preserves math parameter metadata and authored question IDs.

- [x] Add failing conversion/validation and browser workflow coverage.
- [x] Scaffold focused form/question components through the Angular CLI.
- [x] Reuse the create page for route-aware create/edit loading and a common form.
- [x] Add generated/static controls, passage/question authoring and bounded validation.
- [x] Add conflict handling that preserves local edits until explicit reload.
- [x] Run Angular tests/build and address compiler/type errors.

## Task 3: Generation, previews and verification

**Files:** template list, create-instance and instance-preview; e2e workflow; README,
architecture, roadmap and product specification.

**Interfaces:** Static drafts submit `{parameters:{}}`; saved preview renders each
interaction without assuming every prompt is a multiplication expression.

- [x] Update generic library wording and expose edit links.
- [x] Adapt task creation to static or operation-specific arithmetic guidance.
- [x] Render passages, choices, points and answers with correct direction.
- [x] Verify old draft immutability, edit conflicts and keyboard/mobile behavior.
- [x] Run `scripts/verify.sh`, `scripts/publish.sh` and isolated `npm run e2e`.
- [x] Review the complete diff, inspect screenshots and update implementation docs.

## Execution record

- Direction approved by the user: richer authoring first, maintaining the existing
  professional stack and code conventions. Work proceeds in the shared checkout.
- Initial inspection: clean working tree; existing tests cover ownership, CSRF,
  immutable snapshots and optimistic version conflicts. No schema changes needed.

- Backend RED→GREEN: nine new operation/difficulty cases and static mixed-content
  creation initially failed, then passed after engine/contract implementation.
- Frontend RED→GREEN: the authoring test initially found no template-type control;
  the finished form supports static authoring and rejects an incomplete question.
- Independent review found a mismatch between multiline/padded API choices and the
  line-based editor. Rejection tests reproduced it; server validation now requires
  trimmed single-line choices in this new mode.
- A second regression reproduced loss of a distinct task title on editing. The
  optional static task-title field now round-trips that title.
- Full verification after review fixes: `scripts/verify.sh` passed, including 56
  backend tests, 20 Angular tests, formatting and production builds.
- Final published-app Playwright run passed all three workflows, including mixed
  question creation, revision conflicts, old draft preservation and all new arithmetic
  operations. Mobile screenshots were inspected at 360px and 200% text scaling.
- No database migration or dependency upgrade was introduced. Changes remain in the
  shared working tree on `feature/richer-authoring` for review.
