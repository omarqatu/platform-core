#!/usr/bin/env bash
# check-schema-allowlist — a local repository check, not one of PLATFORM_CORE's
# ten checks (see checks/local/README.md).
#
# Usage (from the repository root, after migrating):
#   checks/local/check-schema-allowlist.sh              run the check
#   checks/local/check-schema-allowlist.sh --self-test  prove it fails on drift
#
# PSQL overrides how psql is reached; the default uses the compose container.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
allowlist="$here/schema-allowlist.txt"
query="$here/check-schema-allowlist.sql"
psql_cmd="${PSQL:-docker compose exec -T postgres psql -U migrator -d platform}"

# Parse the allowlist (tolerating CRLF working copies).
entries="$(tr -d '\r' < "$allowlist" | grep -vE '^\s*(#|$)')"
schemas="$(awk '$1 == "schema" { print $2 }' <<< "$entries" | paste -sd, -)"
exact_schemas="$(awk '$1 == "schema" && $3 == "exact" { print $2 }' <<< "$entries" | paste -sd, -)"
objects="$(awk '$1 == "object" { print $2 }' <<< "$entries" | paste -sd, -)"

run() {
  $psql_cmd -X -q -A -t -v ON_ERROR_STOP=1 \
    -v schemas="$schemas" -v exact_schemas="$exact_schemas" -v objects="$objects"
}

if [ "${1:-}" = "--self-test" ]; then
  # Plant a rogue schema and a rogue object inside a transaction that is rolled
  # back, and require the check to report both. A check never seen failing
  # cannot be trusted for being green (PROOF_SPEC T2.2).
  output="$({
    echo "BEGIN;"
    echo "CREATE SCHEMA selftest_rogue_schema;"
    echo "CREATE TABLE migrations_meta.selftest_rogue_table (id int);"
    cat "$query"
    echo "ROLLBACK;"
  } | run)"
  echo "$output"
  if grep -q "schema not in allowlist: selftest_rogue_schema" <<< "$output" &&
     grep -q "object not in allowlist: migrations_meta.selftest_rogue_table" <<< "$output"; then
    echo "PASS (self-test): check-schema-allowlist detects both planted violations."
    exit 0
  fi
  echo "FAIL (self-test): check-schema-allowlist did not report the planted violations."
  exit 1
fi

violations="$(run < "$query")"
if [ -n "$violations" ]; then
  echo "$violations"
  echo "FAIL: check-schema-allowlist found the violations above."
  exit 1
fi
echo "PASS: check-schema-allowlist — schemas and migrations_meta contents match the allowlist."
