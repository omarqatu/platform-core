#!/usr/bin/env bash
# Amendment 02 stage (a): do Checks 2, 3 and 6 actually see the two catalog policies and their grants?
# Each case plants one violation on categories/subcategories inside a transaction that is rolled back, then runs
# one check with the same inputs run-checks.sh gives it, and prints what the check reported. An empty report is a
# check that does not see the object. Run from anywhere against a migrated database:
#   PSQL='docker compose exec -T postgres psql -U migrator -d platform' docs/proposals/a02a-classification-checks/run.sh
set -uo pipefail
core="$(cd "$(dirname "$0")/../../../checks/core" && pwd)"
psql_cmd="${PSQL:-docker compose exec -T postgres psql -U migrator -d platform}"

run() {
  local nn="$1" plant="$2"
  {
    echo 'BEGIN;'
    for v in manifest grants chains exemptions; do echo "SELECT set_config('check.$v', :'$v', true) AS ignored \\gset"; done
    echo "$plant"
    tr -d '\r' < "$core/check-$nn.sql"
    echo 'ROLLBACK;'
  } | $psql_cmd -X -q -A -t -v ON_ERROR_STOP=1 \
      -v manifest="$(tr -d '\r' < "$core/manifest.json")" -v grants="$(tr -d '\r' < "$core/grants.json")" \
      -v chains="$(tr -d '\r' < "$core/policy-chains.json")" -v exemptions="$(tr -d '\r' < "$core/rls-exemptions.json")"
}

case_() {
  local label="$1" nn="$2" plant="$3" out
  out="$(run "$nn" "$plant" 2>&1)"
  if [ -n "$out" ]; then echo "ok   $label"; sed 's/^/       /' <<< "$out"; else echo "MISS $label — Check $nn reported nothing"; fi
}

case_ "A. categories_read dropped → Check 3"          03 "DROP POLICY categories_read ON categories;"
case_ "B. subcategories_read dropped → Check 3"       03 "DROP POLICY subcategories_read ON subcategories;"
case_ "C. categories_read narrowed to app_user → Check 2" 02 "ALTER POLICY categories_read ON categories TO app_user;"
case_ "D. subcategories_read USING changed → Check 2" 02 "ALTER POLICY subcategories_read ON subcategories USING (is_active);"
case_ "E. an extra write policy on categories → Check 2" 02 "CREATE POLICY rogue ON categories FOR INSERT TO app_user WITH CHECK (true);"
case_ "F. app_user granted INSERT on categories → Check 6" 06 "GRANT INSERT ON categories TO app_user;"
case_ "G. provisioner's SELECT on subcategories revoked → Check 6" 06 "REVOKE SELECT ON subcategories FROM provisioner;"
case_ "I. job_runner granted SELECT on categories (deferred, OPEN_ITEMS 76) → Check 6" 06 "GRANT SELECT ON categories TO job_runner;"
case_ "H. RLS no longer forced on categories → Check 5" 05 "ALTER TABLE categories NO FORCE ROW LEVEL SECURITY;"
