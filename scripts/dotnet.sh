#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -x "$repo_dir/.tools/dotnet/dotnet" ]]; then
  export DOTNET_ROOT="$repo_dir/.tools/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi
cd "$repo_dir"
exec dotnet "$@"
