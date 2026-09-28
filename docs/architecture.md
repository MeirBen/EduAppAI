# How the application fits together

There are two programs during development, and one process after publishing.
Angular serves the UI; ASP.NET Core 8 handles cookies, validation and persistence. In a
published build ASP.NET also serves Angular. SQLite is an embedded database file.

```text
frontend/src/app/
  core/             Auth, HTTP contracts, API client, errors
  features/         Parent sign-in, template authoring/revisions, draft creation/preview
  dynamic-form/     Four parameter types rendered from server metadata

backend/FamilyLearning.Api/
  Features/         HTTP endpoints, DTOs and feature entities
  TaskEngine/       Plain models, validation and deterministic generation
  Infrastructure/   Identity, EF Core, JSON snapshots and CSRF
  Program.cs        Composition and request pipeline

tests/FamilyLearning.Api.Tests/
  TaskEngine/       Pure validation and generation tests
  Integration/      Real application + isolated SQLite database
```

## Three different things

A **template** is a stable identity. Its **version** is an immutable blueprint.
A **task instance** is the exact content created from one version and a set of
parameters. A future **session** will record what a child did with that instance.
Do not put children's answers into a template or overwrite generated questions.

```text
POST /api/templates
  → validate definition
  → insert template + version 1 in one EF transaction

POST /api/templates/{id}/instances
  → find the parent's template and its current version
  → validate parameters and apply defaults
  → generate arithmetic in C# or copy authored static content
  → validate the content and answer keys
  → store parameters + exact content + seed as a Draft

GET /api/instances/{id}
  → read the saved content (no generation)
```

`TaskTemplate.CurrentVersion` is an EF concurrency token. Publishing a revision
requires `expectedVersion`; a stale edit gets 409. The unique `(TemplateId, Version)`
index also prevents duplicate version numbers. A previous instance still references
its original version. Lists return the most recent 100 items; pagination is a later
addition before this becomes limiting.

## Why only one backend project?

The application is small. Feature folders provide useful boundaries without six
assemblies, a mediator or generic repositories. Endpoint handlers directly use
`LearningDbContext`; the task engine has no EF dependency. Add an abstraction when
there are real implementations or a meaningful external boundary, such as `IChatClient`.

EF Core 8 owns relationships and transactions; versioned JSON owns dynamic content. JSON
is serialized explicitly through `StoredJson`, not by exposing EF entities. UTC
`DateTime` timestamps and integer points keep the SQLite model straightforward.

On .NET 8, request records mark mandatory JSON properties with `[JsonRequired]`.
That enforces presence; the validators separately reject explicit nulls and invalid
values. C# nullable annotations alone do not validate incoming JSON. Strict number
handling and rejection of unknown properties are configured once in `Program.cs`.

## Authentication and ownership

ASP.NET Identity hashes passwords, handles lockout and validates the authentication
cookie. The server issues the `family_id` and `Parent` claims; browser-supplied family
IDs are never accepted. Every feature query filters by the authenticated family.
Missing and foreign records both return 404.

The authentication cookie is HttpOnly and SameSite Strict, and Secure in Production.
A separate readable `XSRF-TOKEN` cookie is not an authentication credential: Angular
copies it into `X-XSRF-TOKEN`, which ASP.NET validates on writes. Sign-in
refreshes that token after the identity changes. After sign-out, the next login
obtains a new anonymous token; token refresh cannot prevent leaving private content.

The parent preview DTO includes correct answers. A future child endpoint **must**
project a different DTO with no answer keys, and scope access to the authenticated
child and assigned state. Do not reuse `InstancePreview` for the child player.

## Frontend flow

Routes are lazy. `Auth` owns only the current parent; it never stores credentials or
authentication tokens in local storage. `LearningApi` groups the few HTTP operations.
Components use signals for local state and resources for reads, without a global store.

The template editor routes to one reusable Signal Form for creation and revision.
It offers generated arithmetic or parent-authored content. A question-fields component
renders numeric, text and choice authoring controls; feature-local helpers convert
editor state to wire definitions while preserving IDs and existing math metadata.
Revision publication includes the version originally loaded. A 409 keeps the local
form; reloading the latest version is an explicit action that discards local edits.

The next screen uses `ParameterForm` to render the blueprint's parameters. Static
templates have an empty parameter list and copy their fixed content. Browser
validation improves feedback; the server always validates again.
For optional numeric fields, the form keeps input text until submission so that an
empty field does not accidentally turn into zero.

The UI is Hebrew-only with document-level RTL and Angular's `he-IL` locale.
Tailwind CSS 4 handles presentation; Ionic remains the application shell. The
[UI guide](ui-guide.md) documents styling, mixed-direction content and accessibility.
A small display helper translates only known legacy system labels and generated-math instructions;
immutable snapshots, arbitrary authored content and API enum values stay unchanged.

## Generation and content contracts

`GenerationDefinition` retains the original `mode`, `generator`, `fixedSettings`
JSON shape for deterministic templates. Static mode supplies `content` instead.
Mode validation rejects conflicting fields. Nullable fields are omitted when writing;
explicit nulls never bypass mode/content validation. This envelope preserves existing
clients' freedom to order JSON fields without a metadata-first polymorphic contract.

`TaskGenerator` dispatches to `math-v1` or returns the authored content for snapshot
serialization. Multiplication retains its original ranges and seeded algorithm.
Addition/subtraction use operands up to 10/50/100; subtraction orders the operands.
Division uses factors up to 5/10/12 and constructs an exactly divisible dividend.

`TaskContentValidator` checks plain-text blocks, unique question IDs, supported
interactions, numeric answers, choice membership, integer points and text/count bounds.
Required JSON properties distinguish omitted points from deliberately choosing zero.
Content is limited to 20 questions, four passages and 8,000 aggregate text characters;
individual limits are in the [authoring design](superpowers/specs/2026-09-28-richer-authoring-design.md).
No HTML is rendered from authored content and no content data is executed.

The parent preview includes `generationMode` from the instance's pinned template
version. That lets the UI format generated equations as LTR while preserving authored
numeric questions as written. Stored task content is never changed to add display metadata.

## Extension points

- Change the visible wording in a feature's `.html` file.
- Change theme tokens in `frontend/src/styles.css` and layout utilities in templates.
- Add a new deterministic generator only alongside its explicit settings, validation,
  tests and authoring UI. Do not execute code from template data.
- Keep new generation paths behind the small dispatcher and shared content validator.
  Preserve existing generator behavior or introduce an explicit new generator version.

## Documentation stays with the contract

Follow the [commenting guide](commenting-guide.md) for C# XML docs and TypeScript JSDoc.
The declarations explain preconditions, ownership, saved-data guarantees and failure
behavior; inline comments explain decisions such as transaction boundaries and CSRF
ordering. Keep these descriptions current when changing a contract, and update both
backend and frontend documentation when the HTTP shape or semantics change.

## Current limits

The product document is broader than this implementation. The application
has no child account, session model, scoring API, assignments, AI, reports or public
registration. Points and answer keys are authored and validated, but scoring execution
belongs to the forthcoming session workflow.
The application shell is installable, but offline task execution is not implemented.
There is no claim that the whole app is deployment-ready.

Framework references: [Angular compatibility](https://angular.dev/reference/versions),
[Identity configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-8.0),
[ASP.NET antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-8.0),
and [Ionic 9 changes](https://github.com/ionic-team/ionic-framework/blob/main/BREAKING.md).
