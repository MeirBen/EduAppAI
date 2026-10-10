# Family Learning

**Describe an activity → it becomes a draft → create → review and edit → approve.**
The activity opens as a readable document with chat beside it. Incomplete work
saves itself for later; ask for changes or edit the content directly, then
approve the saved revision as a frozen snapshot for assignment.

The [product specification](docs/product-specification.md) defines the
product, the [chat design](docs/activity-chat-design.md) its AI execution and the
[architecture](docs/architecture.md) the code; deploy frontend and backend
together.

## Parent and child quick start

First complete [local setup](#start) or use a [published app](#publish).
Parent and child use separate browsers/devices. For a child on another device,
use the published app's HTTPS address, not `localhost`.

### Parent

1. Sign in with the parent account created during setup.
2. Open `/children` and add a child profile; grade and age are optional.
3. Describe an activity at `/activities/new`, review the settings and confirm
   any supplied text. Every change saves itself as a draft, without AI;
   **יצירת הפעילות** creates the content in one operation. Read it, then use chat
   for changes or **עריכה** for manual edits. **אישור הפעילות** freezes the
   reviewed revision; open its preview to select a child and assign it. Resume
   drafts or open approved activities from `/activities`.
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

Open <http://localhost:4200>. Parent passwords need 12–256 characters with
upper and lowercase letters, a number and a symbol. Each provisioned parent gets
a family; there is no default account or public registration.

`dev.sh` installs missing client dependencies, runs the API
and client reload watchers and starts the evaluation dashboard at
<http://127.0.0.1:5180>, without AI calls. **Ctrl+C** stops all three; restart
after configuration changes, and run `npm --prefix frontend ci` after dependency
changes.
Angular proxies `/api` to `http://localhost:5124`; use `localhost` consistently
for cookies. Development applies the checked-in migrations. The development
server listens only on this computer; for other devices, use a
[published app](#publish).

AI settings, evaluation, results and costs are in the [AI guide](docs/ai.md).

## Update dependencies

Stop the development watchers, then run either command from the repository root:

```bash
./scripts/updatedepsadvanced.sh       # Server, tests, evaluation tool and UI
npm --prefix frontend run deps:update # UI only
```

The server script restores the pinned local `dotnet-outdated` tool and updates
NuGet packages to stable releases. ASP.NET Core and EF Core stay within their
current major versions; other packages may receive major upgrades. It preserves
the target frameworks, regenerates `packages.lock.json` files and builds the
solution, then runs the UI updater. The UI script uses `npm-check-updates --peer`
to select the latest peer-compatible versions, removes `.angular`, `node_modules`
and `package-lock.json`, then runs `npm install` to regenerate the lockfile.

Both commands require network access and leave changes in place if a later step
fails. Review manifests and lockfiles together, resolve any breaking changes,
and run `./scripts/verify.sh` before using the updated dependencies.

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
behavior and 360px/200% text; [architecture](docs/architecture.md#tests) maps
the suites. Read the [comment rules](docs/commenting-guide.md) and
[UI guide](docs/ui-guide.md) before editing; open `FamilyLearning.sln` for
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

Library removal deletes unassigned snapshots and archives assigned ones.
**איפוס נתוני הלמידה** clears all family learning records and child access after
confirmation, keeping parent accounts and AI configuration. See
[retention and reset](docs/product-specification.md#retention-and-reset) for the
full deletion contract.

Development stores SQLite and Data Protection keys in the ignored
`backend/FamilyLearning.Api/data/`. Set `Storage__Directory` to an absolute path
to relocate them, using the same path for provisioning, migrations and runtime,
and keep keys with the database so cookies survive restarts.

### Activity-only cutover

A database from before the activity-only redesign (9 October 2026) refuses to
migrate while it holds learning records. With the app and watchers stopped,
`dotnet artifacts/app/FamilyLearning.Api.dll --activity-only-cutover` clears all
learning records and child access in one transaction, keeping parent accounts,
families, keys and AI configuration, then migrates. It starts no server, worker
or AI call, and once applied repeating it changes nothing.

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
When a hosting platform's proxy is the only route to the process, set
`ForwardedHeaders__ClientIpHeader` to the header in which that proxy sends the
client address. The host then accepts it and `X-Forwarded-Proto` from any
connecting address, so never set it on a directly reachable server. For another
remote proxy, configure its trusted address in `ForwardedHeadersOptions`,
following
[Microsoft's proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).

`/health` reports API process availability, not database or AI readiness. The
PWA service worker is enabled only in published builds; the development server
serves live changes.

### Container

The [Dockerfile](Dockerfile) builds the same package as an image for any
container host. It listens on port 8080 and applies pending migrations before
each start. Run one instance with persistent storage and set:

- `Storage__Directory`: the persistent mount, such as `/data`.
- `OPENROUTER_API_KEY`: the AI key, stored as a host secret.
- `ForwardedHeaders__ClientIpHeader`: the platform proxy's client-address
  header.
- `Serilog__WriteTo__File__Args__configure__0__Args__path` (optional): a log
  file pattern on persistent storage, such as `/data/logs/server-.jsonl`.

Provision the first parent from a shell in the running container, which
inherits these settings:

```bash
cd /app && dotnet FamilyLearning.Api.dll --create-parent you@example.com
```

### Fly.io

[fly.toml](fly.toml) runs the image on one always-on machine in Frankfurt with
a 1 GB volume at `/data`, logs on that volume and Fly's `Fly-Client-IP` header.
Install [flyctl](https://fly.io/docs/flyctl/install/) and sign in, then deploy
from the repository root:

```bash
fly apps create family-learning # If taken, choose another and update fly.toml
fly volumes create data --region fra --size 1 -y
read -rsp 'OpenRouter API key: ' key; echo
printf 'OPENROUTER_API_KEY=%s\n' "$key" | fly secrets import; unset key
fly deploy --ha=false
fly ssh console # Then provision the parent as in Container
```

Open `https://<app>.fly.dev`. Later releases need only `fly deploy`; `fly logs`
streams console output. Fly snapshots the volume daily and keeps snapshots for
five days. To use another host, delete `fly.toml` and follow [Container](#container).
