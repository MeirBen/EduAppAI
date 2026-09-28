# Family Learning Foundation Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement task by task.

**Goal:** Create a local repository containing an understandable, runnable first parent workflow.

**Architecture:** A single feature-organized API directly uses EF Core. The Angular
client consumes explicit DTOs; pure task-engine logic owns validation and generation.

**Tech Stack:** .NET 10, EF Core 10, SQLite, ASP.NET Identity, Angular 22, Ionic 9.

**Spec:** [Foundation design](../specs/2026-09-28-foundation-design.md).

## Global Constraints

- One production backend project; no generic repository or mediator.
- Empty educational data; immutable versions and frozen drafts.
- Parent cookies, CSRF and server-side ownership checks from the first API slice.
- Dynamic parameters: text, integer, select, boolean; question count 1–20.
- No AI provider, paid calls, public deployment, or remote repository in this increment.

## Review Focus

- Missing, null, mistyped, unknown and out-of-range JSON input must produce 400, not 500.
- Cross-family GUIDs must not expose templates or draft answer keys.
- Login/logout must refresh the CSRF token after the identity changes.
- Conflicting version publication must not overwrite an earlier definition.
- Deep-link refreshes must work while unknown `/api` paths remain JSON 404 responses.

### Task 1: Engine and project foundation

**Files:** `backend/FamilyLearning.Api/TaskEngine/*`, `tests/FamilyLearning.Api.Tests/*`,
solution/configuration files, and `scripts/dotnet.sh`.

**Interfaces:** `TaskTemplateDefinition`, `ParameterDefinition`, `TaskContent`,
`TemplateValidator.Validate`, `ParameterValidator.Validate`, `MathTaskGenerator.Generate`.

- [ ] Scaffold the solution and client with pinned dependencies and ignored local tooling.
- [ ] Write tests for missing/unknown/type-invalid parameters, defaults and bounds;
  seed 42 must produce the same multiplication content and valid answers.
- [ ] Observe failing tests; implement only the supported schema and math generator.
- [ ] Run `./scripts/dotnet.sh test`; expect green engine tests.

### Task 2: Persistent authenticated API

**Files:** `Features/Auth/*`, `Features/Templates/*`, `Features/Instances/*`,
`Infrastructure/*`, `Program.cs`, migrations and integration tests.

**Interfaces:** cookie login/logout/me/CSRF; template list/get/create/version;
instance list/get/create; all JSON DTOs scoped to the signed-in family.

- [ ] Write real SQLite tests for unauthenticated access, missing CSRF, login/logout,
  two-family ownership, empty state, invalid definitions and frozen saved drafts.
- [ ] Implement Identity, CLI parent provisioning, migrations and feature endpoint groups.
- [ ] Verify conflicting expected versions return 409 and old draft content is unchanged.
- [ ] Run `./scripts/dotnet.sh test`; expect all tests passing.

### Task 3: Angular parent workflow

**Files:** `frontend/src/app/core/*`, `features/*`, `dynamic-form/*`, PWA configuration.

**Interfaces:** relative `/api` calls matching Task 2; signal form state and explicit DTOs.

- [ ] Generate focused components/services using the Angular CLI.
- [ ] Add tests for dynamic parameter defaults/types, required fields and range handling.
- [ ] Implement sign-in, template empty/list/create, parameter entry and saved draft preview.
- [ ] Run `npm test -- --watch=false` and `npm run build`; expect passing tests/build.

### Task 4: Developer handoff and verification

**Files:** `README.md`, `docs/architecture.md`, `docs/roadmap.md`, `scripts/*`, CI.

- [ ] Document startup, parent provisioning, code-reading order and extension points.
- [ ] Add same-origin publish and local verification scripts.
- [ ] Check the parent workflow in a browser, including narrow-screen layout.
- [ ] Review the final code and fix material findings; run the full validation script.
- [ ] Commit the local scaffold and report exactly what is implemented and deferred.
