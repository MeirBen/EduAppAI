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

`dev.sh` installs missing client dependencies, runs the API
and client reload watchers and starts the evaluation dashboard at
<http://127.0.0.1:5180>, without AI calls. **Ctrl+C** stops all three; restart
after configuration changes, and run `npm --prefix frontend ci` after dependency
changes. Use
[`npm --prefix frontend run start:public`](#public-https-address) to run the
same development app through ngrok as well.
Angular proxies `/api` to `http://localhost:5124`; use `localhost` consistently
for cookies. Development applies the checked-in migrations.

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

`npm start` issues a trusted development certificate. Never share
`rootCA-key.pem`. For access from other devices, use the app's
[public HTTPS address](#public-https-address).

[mkcert]: https://github.com/FiloSottile/mkcert

AI settings, evaluation, results and costs are in the [AI guide](docs/ai.md).

### Public HTTPS address

An account-assigned ngrok domain provides the web app's stable HTTPS address.
ngrok manages its certificate and [supports SSE](https://ngrok.com/compare/cloudflare-tunnel)
for live updates. UI, API and event streams share that origin.

Follow ngrok's [setup instructions](https://ngrok.com/download/linux) and keep
the account token outside the repository. [dev.sh](scripts/dev.sh) owns the
app's assigned domain; `start:public` calls it with `--public`. The
[free plan](https://ngrok.com/docs/pricing-limits/free-plan-limits) has usage
limits and a browser warning; choose **Visit** to continue.

Start the development services and tunnel in one terminal:

```bash
npm --prefix frontend run start:public
```

The domain forwards to the same Angular server at <https://localhost:4200>.
`ng serve` rebuilds the frontend and `dotnet watch` reloads the API at port 5124;
Angular's existing proxy forwards API requests and live event streams. Both
addresses use the same database and AI configuration. The evaluation dashboard
stays on loopback port 5180. Publishing is not needed for this command.

**Ctrl+C**, closing the terminal or any service exiting stops all services and
ngrok. The host machine and launcher must stay running for the domain to work.
The app becomes available after the initial builds complete.

Open the HTTPS address and sign in. `/health` reports API process availability,
not database or AI readiness. The PWA service worker is enabled only in published
builds; the development server serves live changes.

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
its Watches view. `dev.sh` deletes `logs/` when it starts, so each
run begins with clean logs. The file sink runs in shared mode, so management
commands and other local instances append whole entries to the same file;
automated tests write logs under their own temporary storage.

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

The library removes individual drafts, templates or snapshots independently;
deleting a template keeps its activities. Removing a snapshot archives it when
assigned to a child, preserving existing work; unassigned snapshots are deleted.
**איפוס נתוני הלמידה** (reset learning
data) clears the family's templates, drafts, operations, snapshots, child
profiles, device access, assignments, answers and grades after confirmation,
keeping parent accounts and AI configuration. Child profile, separate-device
activation, assignment, resumable answer and parent grading/report APIs are
available. Submission automatically scores choices/numbers and freezes short text
for parent review. Parents finalize all pending grades once; reports retain the
original answers and awards. Parents manage profiles and devices at `/children`,
assign a frozen activity from its preview, and filter or review work at
`/assignments`. The child activation and activity-player screens remain planned.

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
publishing preserves `artifacts/app/data/`. Deploy with **Production**, HTTPS
and one API process. Keep the database and keys in the same persistent directory
across deployments, outside `wwwroot`. From the repository root, apply migrations
before starting; Production does not migrate automatically:

```bash
export Storage__Directory="$HOME/.local/share/family-learning"
dotnet artifacts/app/FamilyLearning.Api.dll --migrate
```

To use an existing database, set `Storage__Directory` to its directory instead;
stop its other API process first. For a new database, provision a parent with
`dotnet artifacts/app/FamilyLearning.Api.dll --create-parent you@example.com`
in the same terminal. Supply `Ai__ApiKey` or `OPENROUTER_API_KEY` to enable
generation; Production uses the [published AI profile](docs/ai.md#configuration)
and environment variables, not development user secrets.

The host processes `X-Forwarded-For` and `X-Forwarded-Proto` from one loopback
proxy before HTTPS redirection, authentication and rate limiting. Preserve the
original `Host` header and disable proxy buffering for `/api/library/changes`.
For a proxy on another machine, explicitly configure its trusted address in
`ForwardedHeadersOptions`, following
[Microsoft's proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).
