#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
for script in scripts/*.sh; do bash -n "$script"; done
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build
dotnet format --verify-no-changes --no-restore
npm --prefix frontend ci
npm --prefix frontend run format:check
npm --prefix frontend run lint:md
npm --prefix frontend run typecheck:e2e
node --test frontend/e2e/evaluation-ui.test.mjs
frontend/node_modules/.bin/prettier --check "tools/FamilyLearning.Evaluation/wwwroot/*"
npm --prefix frontend test -- --watch=false
npm --prefix frontend run build
