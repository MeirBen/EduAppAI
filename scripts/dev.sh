#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"

public_domain=""
if (( $# )); then
  if [[ $# -ne 1 || $1 != --public ]]; then
    printf 'Usage: %s [--public]\n' "$0" >&2
    exit 1
  fi
  public_domain="blimp-extending-elaborate.ngrok-free.dev"
fi
tools=(dotnet node npm)
if [[ -n "$public_domain" ]]; then tools=(dotnet ngrok curl); fi
for tool in "${tools[@]}"; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "Missing $tool. Install the prerequisites listed in README.md, then try again." >&2
    exit 1
  fi
done
dotnet --version >/dev/null
if [[ -n "$public_domain" ]]; then
  if [[ ! -f artifacts/app/FamilyLearning.Api.dll || ! -f artifacts/app/wwwroot/index.html ]]; then
    printf 'Publish the app first with scripts/publish.sh.\n' >&2
    exit 1
  fi
  ngrok config check
  if curl --silent --max-time 1 http://127.0.0.1:5124/health >/dev/null; then
    printf 'Port 5124 is already serving an app. Stop it before starting this launcher.\n' >&2
    exit 1
  fi
else
  if [[ ! -x frontend/node_modules/.bin/ng ]]; then npm --prefix frontend ci; fi
  # Each development run starts with clean server logs.
  rm -rf -- "$repo_dir/logs"
  # Build before starting the API watcher: both projects share the backend dependency.
  dotnet build tools/FamilyLearning.Evaluation

  printf '\nStarting Family Learning with automatic reload.\n'
  printf 'App: https://localhost:4200\nAPI: http://localhost:5124\nEvaluation: http://127.0.0.1:5180\n'
  printf 'Evaluation starts without AI calls; confirm runs in its dashboard.\n'
  printf 'Press Ctrl+C to stop all three servers.\n\n'
fi

# Separate process groups let cleanup stop each service and its children together.
set -m
server_pids=()
cleanup() {
  trap - EXIT
  trap '' HUP INT TERM
  for pid in "${server_pids[@]}"; do
    # Use Ctrl+C semantics so dotnet watch exits instead of restarting its child.
    kill -INT -- "-$pid" 2>/dev/null || true
  done
  wait "${server_pids[@]}" 2>/dev/null || true
}
trap cleanup EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

if [[ -n "$public_domain" ]]; then
  ASPNETCORE_ENVIRONMENT=Production DOTNET_ENVIRONMENT=Production ASPNETCORE_HTTPS_PORT=443 \
    dotnet artifacts/app/FamilyLearning.Api.dll \
    --contentRoot "$repo_dir/artifacts/app" --urls http://127.0.0.1:5124 \
    --AllowedHosts "$public_domain" </dev/null &
  server_pids+=("$!")
  # Probe with the HTTPS metadata the trusted local proxy supplies.
  deadline=$((SECONDS + 30))
  until curl --fail --silent --max-time 1 -H "Host: $public_domain" -H 'X-Forwarded-Proto: https' \
    http://127.0.0.1:5124/health >/dev/null; do
    if ! kill -0 "${server_pids[0]}" 2>/dev/null; then wait "${server_pids[0]}"; exit 1; fi
    if (( SECONDS >= deadline )); then printf 'The app did not become ready within 30 seconds.\n' >&2; exit 1; fi
    sleep 0.2
  done
  printf '\nApp: https://%s\nPress Ctrl+C to stop the app and ngrok.\n\n' "$public_domain"
  ngrok http http://127.0.0.1:5124 --url "https://$public_domain" --inspect=false </dev/null &
  server_pids+=("$!")
else
  # The browser uses Angular's origin; the API watcher should not open a second browser tab.
  DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1 \
    dotnet watch --non-interactive --project backend/FamilyLearning.Api --launch-profile http </dev/null &
  server_pids+=("$!")
  npm --prefix frontend start </dev/null &
  server_pids+=("$!")
  DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Development}" \
    dotnet run --no-build --project tools/FamilyLearning.Evaluation --no-launch-profile -- --ui </dev/null &
  server_pids+=("$!")
fi

# If any server exits, stop the others and return its exit status.
wait -n "${server_pids[@]}"
