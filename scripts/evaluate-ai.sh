#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"
DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Development}" \
  dotnet run --project tools/FamilyLearning.Evaluation --no-launch-profile -- "$@"
