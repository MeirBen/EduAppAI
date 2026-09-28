# Family Learning foundation

The supplied [product specification](../../product-specification.md) describes the destination.
This first increment provides a runnable, readable foundation and a complete parent workflow.
It is not the full MVP.

## Decisions

- One ASP.NET Core 8 production project, organized by feature; EF Core 8 and SQLite.
- One Angular 22 / Ionic 9 client with standalone components, signals, strict types,
  lazy routes, and a PWA application shell. API calls use relative URLs.
- ASP.NET Identity cookies, a command-line parent provisioning operation, server-side
  family ownership checks, CSRF validation, login lockout and rate limiting.
- No public registration, tokens in browser storage, repository wrappers, mediator,
  event bus, or speculative service layers.
- Published template definitions are immutable JSON snapshots. An edit creates a
  new version. Concurrent edits return a conflict instead of overwriting a version.
- A deterministic multiplication generator proves the parameterized template path.
  Static and AI generation are later increments, not nonfunctional selectable modes.
- Every new instance stores its own parameters and content. Retrieving it does not
  regenerate anything. This increment stops at a parent-only draft preview.
- Dynamic parameters support text, integer, select and boolean. The math generator
  specifically requires `difficulty` (easy/medium/hard) and `questionCount` (1–20).
- Initial educational data is empty. No sample templates are silently inserted.

## Implemented user journey

Provision a parent locally → sign in → empty template list → create a multiplication
blueprint → choose difficulty and question count in the generic form → generate a
draft → open the exact saved preview. A parent can see previously generated drafts.
Template version creation is exposed through the API; a visual version editor follows later.

## Boundaries

`Features/Auth`, `Features/Templates`, and `Features/Instances` contain HTTP operations
and their contracts. `TaskEngine` contains plain, testable models, validation, and
generation. `Infrastructure/Persistence` owns EF configuration and migrations.
The client mirrors features and contains one reusable parameter form.

Child profiles, device activation, assignment, sessions, scoring, the child player,
AI and reports remain on the roadmap. Do not create fake pages or success responses
for them. They will build on the frozen instance boundary.

## Errors and persistence

Use RFC ProblemDetails and field errors. Invalid parameters, unsupported schema
versions, malformed definitions and unknown generator names are rejected before
storage. Require authentication and scope every template/instance query to the
authenticated parent's family. Return 404 for missing or foreign records.
Use migrations; apply automatically only in Development. Production uses an explicit
`--migrate` operation. Store database and Data Protection keys under configurable data storage.

## Verification

Real SQLite integration tests cover cookies/CSRF, empty state, validation, cross-family
isolation, version conflicts, and frozen content after a template update. Unit tests
cover parameter boundaries and seeded math generation. Client tests exercise the
dynamic form and error handling; a browser check proves the visible parent workflow.
Build and publish the same-origin application and verify API misses stay API 404s.

## Alternatives considered

A directories-only skeleton is quicker but teaches little about actual data flow.
A full MVP would add too many simultaneous concepts. This small working slice keeps
the eventual architecture intact while making the first codebase approachable.
