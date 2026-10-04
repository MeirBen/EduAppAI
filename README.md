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

Open <https://localhost:4200>. Parent passwords need 12–256 characters with
upper and lowercase letters, a number and a symbol. Each provisioned parent gets
a family; there is no default account or public registration.

`dev.sh` installs missing client dependencies, runs the API and client reload
watchers and starts the evaluation dashboard at <http://127.0.0.1:5180>, without
AI calls. **Ctrl+C** stops all three; restart after configuration changes, and
run `npm --prefix frontend ci` after dependency changes.
Angular proxies `/api` to `http://localhost:5124`; use `localhost` consistently
for cookies. Development applies the initial migration to an empty database.

The client uses HTTPS, since browsers allow the APIs the app needs only on
`localhost` or HTTPS. Trust a development certificate authority once per
computer with [mkcert][mkcert], then restart the browser:

```bash
sudo apt install mkcert libnss3-tools # Ubuntu
# Chrome on Linux reads trusted authorities from this database.
if [ ! -d ~/.pki/nssdb ]; then
  mkdir -p ~/.pki/nssdb
  certutil -d sql:$HOME/.pki/nssdb -N --empty-password
fi
mkcert -install
```

`npm start` then issues a trusted certificate for `localhost` and this
computer's network addresses. To use a phone on the same network, install
`$(mkcert -CAROOT)/rootCA.pem` on it once as a CA certificate (Android:
**Settings → Security → Encryption & credentials → Install a certificate → CA
certificate**) and open `https://<network address>:4200`. Never share
`rootCA-key.pem`.

[mkcert]: https://github.com/FiloSottile/mkcert

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

## Server logs

Serilog writes console output and structured JSON Lines to `logs/server-YYYYMMDD.jsonl`
at the repository root. Outside the repository, relative paths use the deployed
application's content root. Logs stay outside `wwwroot` and are ignored by Git.
The workspace configures **Log Viewer** (`berublan.vscode-log-viewer`) to follow
`logs/server-*.jsonl`, including rolled files; open **Family Learning API** in
its Watches view. `scripts/dev.sh` deletes `logs/` when it starts, so each run
begins with clean logs. The file sink runs in shared mode, so management commands
and other local instances append whole entries to the same file; automated tests
write logs under their own temporary storage.

Configure levels, output, rolling and retention in
[`backend/FamilyLearning.Api/appsettings.json`](backend/FamilyLearning.Api/appsettings.json)
under `Serilog`. Files roll daily or at 20 MiB, retaining the latest 14 files
(not necessarily 14 days). To use persistent deployment storage, override
`Serilog__WriteTo__File__Args__configure__0__Args__path` with an absolute filename
pattern such as `/var/log/family-learning/server-.jsonl`. Restart after sink changes;
existing minimum-level settings support configuration reload.
Failures before the host logger is created are reported to the console.

Successful API writes and generation outcomes are Information; HTTP 4xx responses,
recoverable AI call failures and interrupted generation are Warning. HTTP 5xx
responses and unexpected failures are Error; host termination is Fatal. Successful
GET/HEAD requests (including health checks and operation polling) and static assets
are Debug-only. Requests carry `RequestId` matching the ProblemDetails `traceId`;
trace/span IDs and scoped `OperationId`, `DraftId` and `Stage` connect related
events. Logs exclude request bodies, query strings, credentials and learning
content; keep deployment access restricted.

In JSON, `@m` contains the rendered message for quick reading; structured values
remain separate properties and `@i` identifies the message template. `@t` is UTC,
and an omitted `@l` means Information.

File writes use a bounded 10,000-event background queue, flushed on orderly
shutdown. If full, new events are dropped to preserve request throughput; sink
failures and drops appear on standard error. Abrupt process termination can lose
queued events. These are operational logs, not an audit ledger.

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
Disable proxy response buffering for the `/api/library/changes` event stream.
The PWA caches assets only, and `/health` reports process availability, not
database or AI readiness. Account recovery and the child flow are
[next steps](docs/product-specification.md#next-steps).
