#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
read -r -p 'Parent email: ' parent_email
exec "$repo_dir/scripts/dotnet.sh" run --project "$repo_dir/backend/FamilyLearning.Api" --no-launch-profile -- --create-parent "$parent_email"
