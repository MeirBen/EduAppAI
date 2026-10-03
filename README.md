# Family Learning

**Parent prompt → editable plan → generated activity → parent review → frozen
snapshot.** Generate without saving a template, edit the content or replace one
question, then save the draft and mark the reviewed revision ready. Saving a
reusable template is separate; template edits publish immutable versions and
never change existing activities. See the
[product specification](docs/product-specification.md) for scope and
[architecture](docs/architecture.md) for implementation.

## Start

Use the **.NET 8 SDK** (selected by `global.json`) and a Node version allowed by
`engines.node` in [package.json](frontend/package.json); CI uses Node 24.

```bash
./scripts/create-parent.sh # First run only
./scripts/configure-ai.sh  # Stores your OpenRouter key; makes no paid call
./scripts/dev.sh
```

Open <http://localhost:4200>. Parent passwords need 12–256 characters with
upper and lowercase letters, a number and a symbol. Each provisioned parent gets
a family; there is no default account or public registration.

`dev.sh` installs missing client dependencies, runs the API and client reload
watchers and starts the evaluation dashboard at <http://127.0.0.1:5180>, without
AI calls. **Ctrl+C** stops all three; restart after configuration changes, and
run `npm --prefix frontend ci` after dependency changes.
Angular proxies `/api` to `http://localhost:5124`; use `localhost` consistently
for cookies. Development applies the initial migration to an empty database.

AI settings, evaluation, results and costs are in the [AI guide](docs/ai.md).

## Verify

```bash
./scripts/verify.sh
```

It checks locked restores, .NET builds, tests and XML docs, formatting,
Markdown, shell syntax, TypeScript, the dashboard tests, Angular tests and the
production build. No AI key is needed. For the isolated browser workflow:

```bash
./scripts/publish.sh
cd frontend
npx playwright install chromium # Linux CI: add --with-deps
npm run e2e
```

These tests use disposable data and a local AI provider to exercise the
published app's full activity lifecycle, recovery, conflicts, keyboard/RTL
behavior and 360px/200% text. Read the [comment rules](docs/commenting-guide.md)
and [UI guide](docs/ui-guide.md) before editing; open `FamilyLearning.sln` for
backend work.

## Data

The library deletes individual drafts, templates or snapshots independently;
deleting a template keeps its activities. **איפוס נתוני הלמידה** (reset learning
data) clears the family's templates, drafts, operations and snapshots after
confirmation, keeping the login and AI configuration.

Development stores SQLite and Data Protection keys in the ignored
`backend/FamilyLearning.Api/data/`. Set `Storage__Directory` to an absolute path
to relocate them, using the same path for provisioning, migrations and runtime,
and keep keys with the database so cookies survive restarts. Databases from
before the content-first cutover cannot be upgraded: stop the app, delete
`family-learning.db` with its `-wal`, `-shm` and `-journal` files, and run
`./scripts/create-parent.sh` again.

For model changes, stop `dev.sh` first, since its watcher can apply an
unfinished migration:

```bash
dotnet tool restore
dotnet ef migrations add YourChange --project backend/FamilyLearning.Api \
  --output-dir Infrastructure/Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project backend/FamilyLearning.Api
```

Never edit an applied migration; add a new one. Review migrations before
applying them, since they can remove learning data that a rollback cannot
restore. Back up with SQLite's backup API or `VACUUM INTO`, not by copying an
active database file.

## Publish

```bash
./scripts/publish.sh
```

`artifacts/app/` holds the API and client and needs the ASP.NET Core 8 runtime;
publishing preserves `artifacts/app/data/`. For a local preview:

```bash
export Storage__Directory="$PWD/backend/FamilyLearning.Api/data"
ASPNETCORE_ENVIRONMENT=Development dotnet artifacts/app/FamilyLearning.Api.dll \
  --contentRoot "$PWD/artifacts/app" --urls http://localhost:5124
```

Deploy with **Production**, HTTPS, one process and persistent storage for the
database and keys, with restricted directory permissions. Production does not
migrate automatically; apply migrations before starting:

```bash
Storage__Directory=/absolute/persistent/data dotnet \
  artifacts/app/FamilyLearning.Api.dll --migrate
```

Set `AllowedHosts` to the real hostnames. Behind a reverse proxy, configure its
trusted networks and process forwarded headers before HTTPS redirection,
authentication and rate limiting, following
[Microsoft's proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).
The PWA caches assets only, and `/health` reports process availability, not
database or AI readiness. Account recovery and the child flow are
[next steps](docs/product-specification.md#next-steps).
