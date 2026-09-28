#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
read -rsp 'OpenRouter API key (hidden): ' openrouter_key
printf '\n'
if [[ -z "$openrouter_key" ]]; then
  printf 'No key entered; configuration unchanged.\n' >&2
  exit 1
fi
# JSON goes through stdin: the key is absent from command arguments and shell history.
printf '%s' "$openrouter_key" |
  node -e 'let key = ""; process.stdin.on("data", chunk => key += chunk); process.stdin.on("end", () => process.stdout.write(JSON.stringify({ "Ai:ApiKey": key })));' |
  dotnet user-secrets set --project "$repo_dir/backend/FamilyLearning.Api"
unset openrouter_key
printf 'Saved outside the repository. Restart the development server to connect AI.\n'
