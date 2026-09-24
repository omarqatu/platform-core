#!/usr/bin/env bash
# T0.3 [W] — Api's configuration and image contain no migrator connection string
# (PROOF_SPEC T0, spec item k; document Test 9).
# Usage: ci/check-api-has-no-migrator.sh <api-image-tag>   (run from the repository root)
set -euo pipefail

image="${1:?usage: $0 <api-image-tag>}"
failed=0

echo "== Api configuration files"
if grep -ril "migrator" src/Api --include='*.json'; then
  echo "FAIL: a migrator reference is present in Api configuration (listed above)."
  failed=1
fi

echo "== Api image: environment"
if docker image inspect "$image" --format '{{range .Config.Env}}{{println .}}{{end}}' | grep -i "migrator"; then
  echo "FAIL: the image environment references migrator."
  failed=1
fi

echo "== Api image: application files"
workdir="$(mktemp -d)"
container="$(docker create "$image")"
docker cp "$container:/app" "$workdir/app"
docker rm "$container" > /dev/null
if grep -ril "migrator" "$workdir/app"; then
  echo "FAIL: a migrator reference is present in the image's /app (listed above)."
  failed=1
fi
rm -rf "$workdir"

if [ "$failed" -ne 0 ]; then exit 1; fi
echo "PASS: no migrator connection string in Api's configuration or image."
