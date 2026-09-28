#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
npm --prefix frontend ci
npm --prefix frontend run build
mkdir -p artifacts
publish_dir="$(mktemp -d "$repo_dir/artifacts/publish.XXXXXX")"
trap 'rm -rf "$publish_dir"' EXIT
# Publish into an empty directory: an older package can have newer file timestamps.
./scripts/dotnet.sh publish backend/FamilyLearning.Api -c Release -o "$publish_dir"
mkdir -p "$publish_dir/wwwroot"
cp -R frontend/dist/family-learning/browser/. "$publish_dir/wwwroot/"

# Keep default runtime data in place while replacing the generated package.
# Custom persistent storage must live outside artifacts/app.
mkdir -p artifacts/app
find artifacts/app -mindepth 1 -maxdepth 1 ! -name data -exec rm -rf -- {} +
cp -R "$publish_dir/." artifacts/app/
