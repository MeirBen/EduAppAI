# How the foundation fits together

There are two programs during development, and one process after publishing.
Angular serves the UI; ASP.NET handles cookies, validation and persistence. In a
published build ASP.NET also serves Angular. SQLite is an embedded database file.

```text
frontend/src/app/
  core/             Auth, HTTP contracts, API client, errors
  features/         Parent sign-in, templates, draft creation/preview
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
  → generate multiplication questions in C#
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

EF owns relationships and transactions; versioned JSON owns dynamic content. JSON
is serialized explicitly through `StoredJson`, not by exposing EF entities. UTC
`DateTime` timestamps and integer points keep the SQLite model straightforward.

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

The manual template editor creates a multiplication blueprint. The next screen is
generic: `ParameterForm` reads the blueprint's field definitions and produces typed
values. Browser validation improves feedback; the server always validates again.
For optional numeric fields, the form keeps input text until submission so that an
empty field does not accidentally turn into zero.

## Changes you can make first

- Change the visible wording in a feature's `.html` file.
- Change spacing and colors in `frontend/src/styles.scss`.
- Adjust the multiplication ranges in `MathTaskGenerator`; update its tests with the
  corresponding behavior. Existing drafts intentionally keep their old questions.
- Add a new deterministic generator only alongside its explicit settings, validation,
  tests and authoring UI. Do not execute code from template data.
- Add static content next using a separate generation definition and content validator.
  When multiple modes exist, introduce a small generator dispatcher.

## Limits of this increment

The original product document is broader than this implementation. This foundation
has no child account, session model, scoring API, assignments, AI, reports or public
registration. Version creation exists in the API; its visual editor is deferred.
The application shell is installable, but offline task execution is not implemented.
There is no claim that the whole app is deployment-ready.

Framework references: [Angular compatibility](https://angular.dev/reference/versions),
[Identity configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0),
[ASP.NET antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0),
and [Ionic 9 changes](https://github.com/ionic-team/ionic-framework/blob/main/BREAKING.md).
