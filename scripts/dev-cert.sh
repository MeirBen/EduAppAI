#!/usr/bin/env bash
# Issues the client's development certificate from mkcert's local certificate authority, so
# browsers that trust it open the app on localhost and on this computer's network addresses
# without warnings. npm runs it before every start, so the certificate follows address changes.
set -euo pipefail
cert_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/frontend/.certs"

if ! command -v mkcert >/dev/null 2>&1; then
  echo "Missing mkcert. Install it once (Ubuntu: sudo apt install mkcert libnss3-tools), then run: mkcert -install" >&2
  exit 1
fi
if [[ ! -f "$(mkcert -CAROOT)/rootCA.pem" ]]; then
  echo "Run 'mkcert -install' once to create and trust the local certificate authority." >&2
  exit 1
fi

# hostname -I lists addresses as plain words, so word splitting is intended.
names=(localhost 127.0.0.1 ::1 $(hostname -I 2>/dev/null || true))
mkdir -p "$cert_dir"
mkcert -cert-file "$cert_dir/dev.pem" -key-file "$cert_dir/dev-key.pem" "${names[@]}"
