#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
./scripts/dotnet.sh restore --locked-mode
./scripts/dotnet.sh build --no-restore
./scripts/dotnet.sh test --no-build
./scripts/dotnet.sh format --verify-no-changes --no-restore
npm --prefix frontend ci
npm --prefix frontend run format:check
npm --prefix frontend test -- --watch=false
npm --prefix frontend run build
