#!/usr/bin/env bash
# T0.3 [W] — Api's configuration and image contain no migrator connection string
# (PROOF_SPEC T0, spec item k; document Test 9).
#
# Two layers (project-owner decision, T1 PR):
#   a. connection-string shapes, case-insensitive, spaces allowed around "=":
#      Username / User Name / User Id / User ID / Uid = migrator; a "migrator"
#      key (as under ConnectionStrings); ConnectionStrings__migrator.
#   b. the literal value of migrator's password, in any form, anywhere.
# Every file is searched as bytes and as UTF-16LE strings, since .NET keeps
# string literals in assemblies as UTF-16.
#
# Usage: MIGRATOR_PASSWORD=... ci/check-api-has-no-migrator.sh <api-image-tag>
#   API_CONFIG_DIR  the Api configuration directory to scan (default: src/Api)
set -euo pipefail

image="${1:?usage: $0 <api-image-tag>}"
password="${MIGRATOR_PASSWORD:?MIGRATOR_PASSWORD must be set to the migrator password}"
config_dir="${API_CONFIG_DIR:-src/Api}"

shapes='(user[[:space:]]*name|user[[:space:]]+id|uid)[[:space:]]*=[[:space:]]*migrator([^[:alnum:]_]|$)|"migrator"[[:space:]]*:|connectionstrings__migrator'

failed=0

# Prints why a stream matches, if it does.
scan() {
  local text="$1" label="$2"
  if grep -qiE -- "$shapes" <<< "$text"; then
    echo "FAIL: migrator connection-string shape in $label"
    failed=1
  fi
  if grep -qF -- "$password" <<< "$text"; then
    echo "FAIL: migrator's password in $label"
    failed=1
  fi
}

scan_tree() {
  local root="$1" prefix="$2"
  while IFS= read -r -d '' file; do
    # Bytes as text (NULs dropped), plus the UTF-16LE strings inside.
    scan "$(tr -d '\0' < "$file"; strings -a -el "$file")" "$prefix${file#"$root"}"
  done < <(find "$root" -type f -print0)
}

echo "== Api configuration files ($config_dir)"
while IFS= read -r -d '' file; do
  scan "$(cat "$file")" "$file"
done < <(find "$config_dir" -maxdepth 1 -type f -name '*.json' -print0)

echo "== Api image: environment"
scan "$(docker image inspect "$image" --format '{{range .Config.Env}}{{println .}}{{end}}')" "the image environment"

echo "== Api image: application files (/app)"
workdir="$(mktemp -d)"
trap 'rm -rf "$workdir"' EXIT
container="$(docker create "$image")"
docker cp "$container:/app" "$workdir/app"
docker rm "$container" > /dev/null
scan_tree "$workdir/app" "/app"

if [ "$failed" -ne 0 ]; then exit 1; fi
echo "PASS: no migrator connection string or password in Api's configuration or image."
