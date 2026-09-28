#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
npm --prefix frontend ci
npm --prefix frontend run build
./scripts/dotnet.sh publish backend/FamilyLearning.Api -c Release -o artifacts/app
# Replace only generated assets owned by this script, so obsolete hashed chunks do not accumulate.
rm -rf artifacts/app/wwwroot
mkdir -p artifacts/app/wwwroot
cp -R frontend/dist/family-learning/browser/. artifacts/app/wwwroot/
