# Family Learning

**Parent prompt → editable plan → generated activity → parent review → frozen
snapshot.**
Generate without saving a template, edit the content or replace one question,
then save the draft and mark the reviewed revision ready. Saving a reusable
template is a separate action; later template edits publish immutable versions
and never change existing activities. See the [core
specification](docs/product-specification.md) for scope
and next steps, and [architecture](docs/architecture.md) for implementation.

## Start

Use **.NET 8 SDK** and a Node version allowed by `engines.node` in
[package.json](frontend/package.json), with npm. CI uses Node 24. `global.json`
selects an installed stable 8.0 SDK. Dependencies and lockfiles pin the stack.

```bash
./scripts/create-parent.sh # First run only
./scripts/configure-ai.sh  # Enter your OpenRouter key
./scripts/dev.sh
```

Open <http://localhost:4200>. The parent password is hidden during setup and
must have 12–256 characters with upper/lowercase, a number and a symbol. Each
provisioned parent gets a family. There is no default account or public
registration.

`dev.sh` installs missing client dependencies, runs the API/client reload
watchers
and starts the evaluation dashboard at <http://127.0.0.1:5180> without AI calls.
**Ctrl+C** stops all three. Restart after configuration or evaluation-tool
changes.
Run `npm --prefix frontend ci` after dependency changes. Angular proxies `/api`
to `http://localhost:5124`. Use `localhost` consistently for cookies.
Development
applies the current initial migration automatically on an empty database. The
content-first cutover uses a fresh database; see [Data](#data) for local setup.

## AI guide

The [AI guide](docs/ai.md) is the single reference for current settings,
evaluation commands, measured results, costs, cutover decisions and unresolved
failures. Tuning is closed; generated content still needs parent review, and
better educational quality than one-shot generation has not been demonstrated.

The active profile lives in
[appsettings.json](backend/FamilyLearning.Api/appsettings.json).
Use `./scripts/configure-ai.sh` to store the key in development user secrets,
then restart. This setup makes no paid call.

## Verify

```bash
./scripts/verify.sh
```

Checks locked restores, .NET builds/tests/XML docs, source/config formatting,
Markdown, shell syntax, browser-test and harness type checks, isolated dashboard
tests, Angular tests and production build. TypeScript rejects unused
locals/parameters. No AI key is needed.
Test hosts use fixed configuration without file watchers.

For the isolated browser workflow:

```bash
./scripts/publish.sh
cd frontend
npx playwright install chromium
npm run e2e
```

Tests use disposable data and a local AI provider. They exercise the published
app's complete activity lifecycle, independent template
publication, immutable previews, source fidelity, strict blockers, recovery,
conflicts, keyboard/RTL behavior and 360px/200% text. Screenshots go to the
ignored
`.superpowers/sdd/2026-09-30-structured-templates/` verification workspace. On
Linux CI, install browser libraries with
`npx playwright install --with-deps chromium`.

Before editing, read the [comment rules](docs/commenting-guide.md) and
[UI guide](docs/ui-guide.md). Open `FamilyLearning.sln` for backend development.

## Data

Delete individual drafts, templates or frozen snapshots from the library.
Their contents are independent: deleting a template does not erase its
activities. **איפוס נתוני הלמידה**
(reset learning data) clears your family's templates, versions, drafts,
operations
and snapshots, including items beyond the 100-item list limits, after
confirmation; your login and AI configuration remain.

Development stores SQLite and Data Protection keys in
`backend/FamilyLearning.Api/data/`, ignored by Git. Set `Storage__Directory` to
an absolute path to relocate them; use the same path for provisioning,
migrations and runtime. Retain keys with the database so cookies survive
restarts.

The cutover replaces all prototype migrations with one `InitialCreate` baseline;
it has no old-schema upgrade path. For an existing prototype installation, stop
this project's watchers/connections, remove only its configured
`family-learning.db` and matching `-wal`, `-shm` and `-journal` files, then run
`./scripts/create-parent.sh`. Keep configuration, credentials and Data
Protection
keys. This reset also removes local accounts. Automated tests use disposable
storage and never reset your database.

For subsequent model changes:

```bash
dotnet tool restore
dotnet ef migrations add YourChange \
  --project backend/FamilyLearning.Api \
  --output-dir Infrastructure/Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project
backend/FamilyLearning.Api
```

Stop `dev.sh` before creating or editing migrations: its reload watcher can
apply
an unfinished migration. Once applied, keep migration files unchanged and add a
new migration for corrections.

Review migrations before applying them. Migrations can remove incompatible
learning data; rolling back cannot restore deleted content. Back up using
SQLite's backup API or `VACUUM INTO`, not by copying an active database file.

## Publish

```bash
./scripts/publish.sh
```

`artifacts/app/` contains the API and client and requires the ASP.NET Core 8
runtime. Publishing preserves default `artifacts/app/data/`; keep custom
persistent storage outside the package. For a local preview:

```bash
export Storage__Directory="$PWD/backend/FamilyLearning.Api/data"
ASPNETCORE_ENVIRONMENT=Development dotnet artifacts/app/FamilyLearning.Api.dll \
  --contentRoot "$PWD/artifacts/app" --urls http://localhost:5124
```

Deployment uses **Production**, HTTPS, one process and persistent storage for
both database and keys. Restrict data-directory permissions. Apply migrations
explicitly before starting; Production does not apply them automatically:

```bash
Storage__Directory=/absolute/persistent/data dotnet \
  artifacts/app/FamilyLearning.Api.dll --migrate
```

The PWA caches assets only; task operations require a connection. Before a live
deployment, set `AllowedHosts` to the real hostnames and verify HTTPS.
Behind a reverse proxy, configure its trusted addresses/networks. Process
forwarded headers before HTTPS redirection, authentication and rate limiting;
follow
[Microsoft's proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).
Also verify storage permissions, backup/restore and operational monitoring.
`/health` checks process availability, not database or AI readiness. Account
recovery and the child flow remain in the [next
steps](docs/product-specification.md#next-steps).
