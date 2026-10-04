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
if [[ -n "$public_domain" ]]; then tools+=(ngrok); fi
for tool in "${tools[@]}"; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "Missing $tool. Install the prerequisites listed in README.md, then try again." >&2
    exit 1
  fi
done
dotnet --version >/dev/null
if [[ -n "$public_domain" ]]; then
  ngrok config check
fi
if [[ ! -x frontend/node_modules/.bin/ng ]]; then npm --prefix frontend ci; fi
# Each development run starts with clean server logs.
rm -rf -- "$repo_dir/logs"
# Build before starting the API watcher: both projects share the backend dependency.
dotnet build tools/FamilyLearning.Evaluation

printf '\nStarting Family Learning with automatic reload.\n'
printf 'App: https://localhost:4200\nAPI: http://localhost:5124\nEvaluation: http://127.0.0.1:5180\n'
printf 'Evaluation starts without AI calls; confirm runs in its dashboard.\n'
printf 'Press Ctrl+C to stop all services.\n\n'

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

# The browser uses Angular's origin; the API watcher should not open a second browser tab.
DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1 \
  dotnet watch --non-interactive --project backend/FamilyLearning.Api --launch-profile http </dev/null &
server_pids+=("$!")
# Allow the app's domain for Vite's live-reload WebSocket.
__VITE_ADDITIONAL_SERVER_ALLOWED_HOSTS="$public_domain" npm --prefix frontend start </dev/null &
server_pids+=("$!")
DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Development}" \
  dotnet run --no-build --project tools/FamilyLearning.Evaluation --no-launch-profile -- --ui </dev/null &
server_pids+=("$!")

if [[ -n "$public_domain" ]]; then
  printf 'Public app: https://%s (available after the initial build)\n\n' "$public_domain"
  # Logging disables ngrok's interactive dashboard, which cannot own this launcher's terminal.
  ngrok http https://localhost:4200 --url "https://$public_domain" --inspect=false --log=stdout </dev/null &
  server_pids+=("$!")
fi

# If any server exits, stop the others and return its exit status.
wait -n "${server_pids[@]}"
