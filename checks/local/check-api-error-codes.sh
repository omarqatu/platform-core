#!/usr/bin/env bash
# check-api-error-codes — a local repository check, not one of PLATFORM_CORE's ten checks (see
# checks/local/README.md). The API returns codes, never text, and the web interface translates every one:
#   1. checks/local/api-error-codes.json is the current export of src/Core/ApiErrorCodes.cs, the codes' one source;
#   2. no code is written as a string literal elsewhere in src/ (.cs, outside bin/ and obj/), except the paths in
#      api-error-codes-literal-allowlist.txt;
#   3. every code has its entry errors.<code> in web/src/i18n/apiErrors.ts, and every entry but errors.unknown has its
#      code (web/scripts/check-api-error-codes.mjs).
#
# Usage (from the repository root; needs python3, and node with web/'s packages installed — no database):
#   checks/local/check-api-error-codes.sh              run the check
#   checks/local/check-api-error-codes.sh --self-test  prove it fails on each plant
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
allowlist="$here/api-error-codes-literal-allowlist.txt"

# literals <directory>: every code from the export written as a "literal" in a .cs file outside ApiErrorCodes.cs,
# bin/ and obj/, minus the allowlisted paths. Prints path:line:text.
literals() {
  local dir="$1" allowed pattern
  allowed="$(tr -d '\r' < "$allowlist" | grep -vE '^\s*(#|$)' || true)"
  pattern="\"($(python3 -c 'import json,sys; print("|".join(json.load(open(sys.argv[1]))["codes"]))' "$here/api-error-codes.json"))\""
  (cd "$dir" && grep -rnE --include='*.cs' --exclude-dir=bin --exclude-dir=obj --exclude=ApiErrorCodes.cs "$pattern" . || true) |
    sed 's|^\./||' |
    while IFS= read -r hit; do
      path="${hit%%:*}"
      if [ -n "$allowed" ] && grep -qxF "$path" <<< "$allowed"; then continue; fi
      echo "$hit"
    done
}

if [ "${1:-}" = "--self-test" ]; then
  # A check never seen failing cannot be trusted for being green (PROOF_SPEC T2.2). The source tree is never edited.
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' EXIT
  (cd "$root/src" && tar --exclude=bin --exclude=obj -cf - .) | (cd "$work" && tar -xf -)
  mkdir -p "$work/Planted"
  printf 'class Plant\n{\n    object Refuse() => new { error = "not_permitted" };\n}\n' > "$work/Planted/Plant.cs"
  output="$(literals "$work")"
  echo "$output"
  status=0
  if grep -q '^Planted/Plant.cs:3:' <<< "$output" && [ "$(grep -c . <<< "$output")" -eq 1 ]; then
    echo "PASS (self-test): check-api-error-codes detects the planted literal code."
  else
    echo "FAIL (self-test): check-api-error-codes did not report exactly the planted literal code."
    status=1
  fi
  (cd "$root/web" && node scripts/check-api-error-codes.mjs --self-test) || status=1
  exit "$status"
fi

python3 "$here/export-api-error-codes.py" --check
violations="$(literals "$root/src")"
if [ -n "$violations" ]; then
  echo "$violations"
  echo "FAIL: check-api-error-codes — an API error code written as a literal; use ApiErrorCodes (src/Core/ApiErrorCodes.cs)."
  exit 1
fi
echo "PASS: check-api-error-codes — every code in src/ comes from ApiErrorCodes."
cd "$root/web" && node scripts/check-api-error-codes.mjs
