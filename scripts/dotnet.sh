#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
# An older local installation must not hide a compatible system SDK.
if [[ -x "$repo_dir/.tools/dotnet/dotnet" ]] && "$repo_dir/.tools/dotnet/dotnet" --version >/dev/null 2>&1; then
  export DOTNET_ROOT="$repo_dir/.tools/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi
exec dotnet "$@"
