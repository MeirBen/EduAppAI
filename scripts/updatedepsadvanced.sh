#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_dir"

dotnet tool restore

# Update related references together before restoring to avoid temporary downgrades.
# ASP.NET Core and EF Core must stay on the application's current runtime major.
dotnet tool run dotnet-outdated -- FamilyLearning.sln --upgrade --no-restore \
  --pre-release Never --version-lock Major \
  --include Microsoft.AspNetCore. --include Microsoft.EntityFrameworkCore.
dotnet tool run dotnet-outdated -- FamilyLearning.sln --upgrade --no-restore \
  --pre-release Never \
  --exclude Microsoft.AspNetCore. --exclude Microsoft.EntityFrameworkCore.

dotnet restore FamilyLearning.sln --force-evaluate -p:RestoreLockedMode=false
dotnet build FamilyLearning.sln --no-restore

printf '%s\n' 'Updating frontend dependencies...'
npm --prefix frontend run deps:update

printf '%s\n' 'Dependencies updated. Review the project and lockfile changes, then run ./scripts/verify.sh.'
