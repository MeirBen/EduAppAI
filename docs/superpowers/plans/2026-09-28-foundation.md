# Family Learning Foundation Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement task by task.

**Goal:** Create a local repository containing an understandable, runnable first parent workflow.

**Architecture:** A single feature-organized API directly uses EF Core. The Angular
client consumes explicit DTOs; pure task-engine logic owns validation and generation.

**Tech Stack:** .NET 8, EF Core 8, SQLite, ASP.NET Identity, Angular 22, Ionic 9.

**Spec:** [Foundation design](../specs/2026-09-28-foundation-design.md).

## Global Constraints

- One production backend project; no generic repository or mediator.
- Empty educational data; immutable versions and frozen drafts.
- Parent cookies, CSRF and server-side ownership checks from the first API slice.
- Dynamic parameters: text, integer, select, boolean; question count 1–20.
- No AI provider, paid calls, public deployment, or remote repository in this increment.
- Maintain contract docs and decision comments using the [commenting guide](../../commenting-guide.md).

## Review Focus

- Missing, null, mistyped, unknown and out-of-range JSON input must produce 400, not 500.
- Cross-family GUIDs must not expose templates or draft answer keys.
- Login refreshes CSRF for the authenticated identity; the next login obtains the anonymous token after logout.
- Conflicting version publication must not overwrite an earlier definition.
- Deep-link refreshes must work while unknown `/api` paths remain JSON 404 responses.

### Task 1: Engine and project foundation

**Files:** `backend/FamilyLearning.Api/TaskEngine/*`, `tests/FamilyLearning.Api.Tests/*`,
solution/configuration files, and `scripts/dotnet.sh`.

**Interfaces:** `TaskTemplateDefinition`, `ParameterDefinition`, `TaskContent`,
`TemplateValidator.Validate`, `ParameterValidator.Validate`, `MathTaskGenerator.Generate`.

- [x] Scaffold the solution and client with pinned dependencies and ignored local tooling.
- [x] Write tests for missing/unknown/type-invalid parameters, defaults and bounds;
  seed 42 must produce the same multiplication content and valid answers.
- [x] Observe failing tests; implement only the supported schema and math generator.
- [x] Run `./scripts/dotnet.sh test`; expect green engine tests.

### Task 2: Persistent authenticated API

**Files:** `Features/Auth/*`, `Features/Templates/*`, `Features/Instances/*`,
`Infrastructure/*`, `Program.cs`, migrations and integration tests.

**Interfaces:** cookie login/logout/me/CSRF; template list/get/create/version;
instance list/get/create; all JSON DTOs scoped to the signed-in family.

- [x] Write real SQLite tests for unauthenticated access, missing CSRF, login/logout,
  two-family ownership, empty state, invalid definitions and frozen saved drafts.
- [x] Implement Identity, CLI parent provisioning, migrations and feature endpoint groups.
- [x] Verify conflicting expected versions return 409 and old draft content is unchanged.
- [x] Run `./scripts/dotnet.sh test`; expect all tests passing.

### Task 3: Angular parent workflow

**Files:** `frontend/src/app/core/*`, `features/*`, `dynamic-form/*`, PWA configuration.

**Interfaces:** relative `/api` calls matching Task 2; signal form state and explicit DTOs.

- [x] Generate focused components/services using the Angular CLI.
- [x] Add tests for dynamic parameter defaults/types, required fields and range handling.
- [x] Implement sign-in, template empty/list/create, parameter entry and saved draft preview.
- [x] Run `npm test -- --watch=false` and `npm run build`; expect passing tests/build.

### Task 4: Developer handoff and verification

**Files:** `README.md`, `docs/architecture.md`, `docs/roadmap.md`, `scripts/*`, CI.

- [x] Document startup, parent provisioning, code-reading order and extension points.
- [x] Add same-origin publish and local verification scripts.
- [x] Check the parent workflow in a browser, including narrow-screen layout.
- [x] Review the final code and fix material findings; run the full validation script.
- [x] Commit the local scaffold and report exactly what is implemented and deferred.


## Execution notes

- Worked in the requested, newly created desktop repository. No remote was configured.
- Installed a local .NET 8.0.425 SDK under ignored `.tools`, preserving the system SDK.
- Final review was performed by an independent read-only reviewer. Quoted integer JSON
  handling and successful-logout behavior were tightened with regression tests.
- Test storage overrides use early host settings; every authenticated fixture asserts
  its connection path. Temporary integration/browser databases are isolated and removed.
- Added a real simultaneous version-publication test: one request succeeds, the other
  gets 409, and the current version advances only once.
- Child access, sessions, scoring, static generation, AI and reports remain intentionally
  outside this first slice. The code and README name those boundaries explicitly.
- CSRF is refreshed after sign-in; the next login refreshes the anonymous token after
  logout. A failed token refresh cannot prevent leaving a signed-out private view.
