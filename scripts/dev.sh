#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
if [[ ! -d frontend/node_modules ]]; then npm --prefix frontend ci; fi
./scripts/dotnet.sh run --project backend/FamilyLearning.Api --launch-profile http &
api_pid=$!
npm --prefix frontend start &
web_pid=$!
cleanup() {
  kill "$api_pid" "$web_pid" 2>/dev/null || true
  wait "$api_pid" "$web_pid" 2>/dev/null || true
}
trap cleanup EXIT
trap 'exit 130' INT TERM
wait -n "$api_pid" "$web_pid"
