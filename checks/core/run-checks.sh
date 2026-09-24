#!/usr/bin/env bash
# The ten CI checks of PLATFORM_CORE v1.14 §3.6, numbered as the document numbers them.
#
#   checks/core/run-checks.sh              every check must return no violation
#   checks/core/run-checks.sh --self-test  every check must fail on its planted violation (PROOF_SPEC T2.2)
#
# Each SQL check runs as migrator in a transaction that is always rolled back: Checks 2 and 8 create
# reference policies, and the self-test plants its violation (checks/core/selftest/check-NN.sql) in the
# same transaction. Inputs are files in the repository (§3.6): the manifest, the grants matrix, the
# chain table and the exemption list. PSQL overrides how psql is reached.
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
psql_cmd="${PSQL:-docker compose exec -T postgres psql -U migrator -d platform}"
mode="${1:-}"

names=(
  [1]="Check 1 — RLS on every tenant table"
  [2]="Check 2 — the manifest, deparse against deparse; the NULLIF rule"
  [3]="Check 3 — the required core policies exist"
  [4]="Check 4 — no policy applies to PUBLIC"
  [5]="Check 5 — RLS on every table in public, exemptions from a file"
  [6]="Check 6 — grants against the matrix, every privilege surface"
  [7]="Check 7 — composite FKs between tenant tables"
  [8]="Check 8 — second-axis coverage, and its inverse"
  [9]="Check 9 — the source of the scope (static)"
  [10]="Check 10 — declared policy chains (pg_depend)"
)

run_sql_check() {
  local n="$1" plant="${2:-}"
  local nn; nn="$(printf '%02d' "$n")"
  {
    echo 'BEGIN;'
    echo "SELECT set_config('check.manifest', :'manifest', true) AS ignored \\gset"
    echo "SELECT set_config('check.grants', :'grants', true) AS ignored \\gset"
    echo "SELECT set_config('check.chains', :'chains', true) AS ignored \\gset"
    echo "SELECT set_config('check.exemptions', :'exemptions', true) AS ignored \\gset"
    [ -n "$plant" ] && tr -d '\r' < "$plant"
    tr -d '\r' < "$here/check-$nn.sql"
    echo 'ROLLBACK;'
  } | $psql_cmd -X -q -A -t -v ON_ERROR_STOP=1 \
        -v manifest="$(tr -d '\r' < "$here/manifest.json")" \
        -v grants="$(tr -d '\r' < "$here/grants.json")" \
        -v chains="$(tr -d '\r' < "$here/policy-chains.json")" \
        -v exemptions="$(tr -d '\r' < "$here/rls-exemptions.json")" 2>&1
  return "${PIPESTATUS[1]}"
}

run_check_9() {
  local root="${1:-src}"
  bash "$here/check-09.sh" "$root"
}

failures=0
for n in 1 2 3 4 5 6 7 8 9 10; do
  nn="$(printf '%02d' "$n")"
  if [ "$mode" = "--self-test" ]; then
    if [ "$n" -eq 9 ]; then
      # Plant a controller that reads app.scope_all from a request header, in a copy of the tree.
      tree="$(mktemp -d)"; cp -r src "$tree/src"
      mkdir -p "$tree/src/Api/Selftest"
      tr -d '\r' < "$here/selftest/check-09.cs" > "$tree/src/Api/Selftest/Planted.cs"
      output="$(cd "$tree" && bash "$here/check-09.sh" src)"; status=$?
      rm -rf "$tree"
    else
      output="$(run_sql_check "$n" "$here/selftest/check-$nn.sql")"; status=$?
    fi
    if [ "$status" -eq 0 ] && [ -n "$output" ]; then
      echo "ok   — ${names[$n]}: fails on its plant ($(wc -l <<< "$output") violation(s)): $(head -1 <<< "$output")"
    else
      echo "FAIL — ${names[$n]}: did not fail on its plant (exit $status)"
      [ -n "$output" ] && sed 's/^/       /' <<< "$output"
      failures=$((failures + 1))
    fi
  else
    if [ "$n" -eq 9 ]; then output="$(run_check_9 src)"; status=$?; else output="$(run_sql_check "$n")"; status=$?; fi
    if [ "$status" -eq 0 ] && [ -z "$output" ]; then
      echo "PASS — ${names[$n]}"
    else
      echo "FAIL — ${names[$n]} (exit $status)"
      sed 's/^/       /' <<< "$output"
      failures=$((failures + 1))
    fi
  fi
done

if [ "$failures" -ne 0 ]; then
  echo "FAILED: $failures check(s)."
  exit 1
fi
if [ "$mode" = "--self-test" ]; then
  echo "PASS (self-test): every one of the ten checks fails on its planted violation."
else
  echo "PASS: Checks 1-10 are green."
fi
