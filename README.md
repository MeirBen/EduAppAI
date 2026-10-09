# Family Learning

**Describe an activity → save a draft → create → review and edit → approve.**
The activity opens as a readable document with chat beside it. Save incomplete
work and return later, ask for changes or edit the content directly, then
approve the saved revision as a frozen snapshot for assignment.

The [product specification](docs/product-specification.md) and
[chat design](docs/activity-chat-design.md) define the activity-only flow.
[Slice 1](docs/superpowers/plans/2026-10-08-activity-only-slice-1.md) implements
the engine/API; [slice 2](docs/superpowers/plans/2026-10-09-activity-only-slice-2.md)
implements the canvas/chat and removes template UI.
[Slice 3](docs/superpowers/plans/2026-10-09-activity-only-slice-3.md) retires the
backend template contracts and records the verified local cutover. Deploy
the matching frontend/backend together; see the [architecture](docs/architecture.md).

## Parent and child quick start

First complete [local setup](#start) or use a [published app](#publish).
Parent and child use separate browsers/devices. For a child on another device,
use the app's [public HTTPS address](#public-https-address), not `localhost`.

### Parent

1. Sign in with the parent account created during setup.
2. Open `/children` and add a child profile; grade and age are optional.
3. Describe an activity at `/activities/new`, review the settings and confirm
   any supplied text. **שמירת טיוטה** saves without AI; **יצירת הפעילות** creates
   the content in one operation. Read it, use chat for changes or **עריכה** for
   manual edits, and save. **אישור הפעילות** freezes the reviewed revision;
   open its preview to select a child and assign it. Resume drafts or open
   approved activities from `/activities`.
4. At `/children`, select that child, name the device and create an activation
   code. Give the child the displayed activation address and code; the code
   works once and expires after ten minutes.
5. After submission, open `/assignments` to see the work and elapsed time.
   Grade any pending short-text answers and finalize the review to show the
   final total.

### Child

1. Open the activation address (`/child/activate`) in the separate browser and
   enter the code. No child email or password is needed.
2. Open an assigned activity from `/child` and answer the questions. Use
   **שמירת התשובות** before leaving; reopening resumes the last saved answers.
3. Choose **הגשת העבודה** when finished. Submission locks the answers and shows
   a receipt; the final total appears once any parent review is complete.

Access lasts up to 30 days from activation. Disconnect, expiry, revocation or
cleared cookies require a new code. Parents manage access at `/children`;
disabling a profile revokes all its devices and pending codes, and re-enabling
requires fresh activation. See the [child-flow contracts](docs/product-specification.md#child-flow)
for scoring, profile details and history-preserving cleanup.

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
behavior and 360px/200% text. The [child-flow acceptance checklist](docs/child-flow-plan.md#task-7-full-flow-acceptance-and-documentation-cutover)
records isolation, persistence, concurrency and migration coverage. Read the
[comment rules](docs/commenting-guide.md) and [UI guide](docs/ui-guide.md) before
editing; open `FamilyLearning.sln` for backend work.

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

Library removal deletes unassigned snapshots and archives assigned ones.
**איפוס נתוני הלמידה** clears all family learning records and child access after
confirmation, keeping parent accounts and AI configuration. See
[retention and reset](docs/product-specification.md#retention-and-reset) for the
full deletion contract.

Development stores SQLite and Data Protection keys in the ignored
`backend/FamilyLearning.Api/data/`. Set `Storage__Directory` to an absolute path
to relocate them, using the same path for provisioning, migrations and runtime,
and keep keys with the database so cookies survive restarts. The activity-only
redesign requires a coordinated fresh start for learning records; existing plan
JSON is not converted. Keep parent accounts, families and AI configuration, and
follow the [cutover contract](docs/product-specification.md#activity-only-cutover).
Do not reset data as part of ordinary startup.

### Activity-only cutover

For an existing installation, coordinate the reset and stop the app, worker and
development watchers first. Build and verify the matching app before running:

```bash
export Storage__Directory="/absolute/path/to/existing/storage"
dotnet artifacts/app/FamilyLearning.Api.dll --activity-only-cutover
```

This one-time command clears all families' drafts, operation evidence, templates,
snapshots, children/device access, assignments, answers and grades, then removes
template tables and provenance columns. Parent accounts, families, Data
Protection keys and AI configuration remain. It starts no HTTP server, worker
or AI call. A failed learning reset rolls back its deletes; if the later schema
update fails, learning records remain empty and rerunning finishes the update.

Ordinary `--migrate` and development startup reject a populated pre-cutover
database without deleting records. Fresh databases migrate normally. Once the
cutover migration is applied, repeating `--activity-only-cutover` changes
nothing, including activities created afterward. Confirm account/configuration
retention and an empty learning queue before restarting the app.

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
