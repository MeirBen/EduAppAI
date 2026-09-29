#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"

for tool in dotnet node npm; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "Missing $tool. Install the prerequisites listed in README.md, then try again." >&2
    exit 1
  fi
done
dotnet --version >/dev/null
if [[ ! -x frontend/node_modules/.bin/ng ]]; then npm --prefix frontend ci; fi
# Build before starting the API watcher: both projects share the backend dependency.
dotnet build tools/FamilyLearning.Evaluation

printf '\nStarting Family Learning with automatic reload.\n'
printf 'App: http://localhost:4200\nAPI: http://localhost:5124\nEvaluation: http://127.0.0.1:5180\n'
printf 'Evaluation starts without AI calls; confirm runs in its dashboard.\n'
printf 'Press Ctrl+C to stop all three servers.\n\n'

# Separate process groups let cleanup stop the watchers and their child servers together.
set -m
server_pids=()
cleanup() {
  trap - EXIT INT TERM
  for pid in "${server_pids[@]}"; do
    # Use Ctrl+C semantics so dotnet watch exits instead of restarting its child.
    kill -INT -- "-$pid" 2>/dev/null || true
  done
  wait "${server_pids[@]}" 2>/dev/null || true
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# The browser uses Angular's origin; the API watcher should not open a second browser tab.
DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1 \
  dotnet watch --non-interactive --project backend/FamilyLearning.Api --launch-profile http </dev/null &
server_pids+=("$!")
npm --prefix frontend start </dev/null &
server_pids+=("$!")
DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Development}" \
  dotnet run --no-build --project tools/FamilyLearning.Evaluation --no-launch-profile -- --ui </dev/null &
server_pids+=("$!")

# If any server exits, stop the others and return its exit status.
wait -n "${server_pids[@]}"
