#!/usr/bin/env bash
# The rule-11 run of the v1.17 acceptance draft, both directions, on a migrated and seeded database (as CI builds it):
# baseline (v1.16 as migrated) → teardown → the draft applied as migrator (p1–p4) → the same cases and races.
# Usage (repository root): docs/proposals/v1.17-acceptance/run.sh     (PSQL_AS as in cases.sh)
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
psql_as="${PSQL_AS:-docker compose exec -T postgres psql -d platform -U}"
as_migrator() { $psql_as migrator -X -q -v ON_ERROR_STOP=1 < "$1"; }
status=0

$psql_as migrator -X -A -t -c "SELECT 'PostgreSQL ' || current_setting('server_version')"
as_migrator "$here/teardown.sql" && as_migrator "$here/fixtures.sql" || exit 2
bash "$here/cases.sh" baseline || status=1
bash "$here/races.sh" baseline || status=1
as_migrator "$here/teardown.sql" || exit 2

echo "== applying the draft as migrator"
for p in p1-membership-needs-invitation p2-invitation-records-its-acceptor p3-one-account-per-address p4-membership-has-scope; do
  if as_migrator "$here/$p.sql"; then echo "applied: $p"; else echo "FAILED to apply: $p"; exit 2; fi
done

as_migrator "$here/fixtures.sql" || exit 2
bash "$here/cases.sh" draft || status=1
bash "$here/races.sh" draft || status=1
as_migrator "$here/teardown.sql" || exit 2
echo "== rule-11 run: $([ $status -eq 0 ] && echo 'every case as expected, both directions' || echo 'UNEXPECTED RESULTS above')"
exit $status
