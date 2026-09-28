#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
read -r -p 'Parent email: ' parent_email
exec dotnet run --project backend/FamilyLearning.Api --no-launch-profile -- --create-parent "$parent_email"
