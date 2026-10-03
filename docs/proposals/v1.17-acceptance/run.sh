#!/usr/bin/env bash
# The rule-11 run, both directions, on a migrated and seeded database (as CI builds it):
# baseline (v1.16 as migrated) → teardown → the variant applied as migrator → the same cases and races (and, for B,
# what it commits). One variant per database: the draft changes policies that cannot be cleanly taken back.
#   draft — P1, P2, P3, P4
#   altb  — alternative B: P3 and accept_invitation()
# Usage (repository root): docs/proposals/v1.17-acceptance/run.sh draft|altb     (PSQL_AS as in cases.sh)
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
variant="${1:?draft|altb}"
psql_as="${PSQL_AS:-docker compose exec -T postgres psql -d platform -U}"
as_migrator() { $psql_as migrator -X -q -v ON_ERROR_STOP=1 < "$1"; }
status=0
case "$variant" in
  draft) parts="p1-membership-needs-invitation p2-invitation-records-its-acceptor p3-one-account-per-address p4-membership-has-scope" ;;
  altb)  parts="p3-one-account-per-address b-accept-invitation" ;;
  *) echo "unknown variant: $variant"; exit 2 ;;
esac

$psql_as migrator -X -A -t -c "SELECT 'PostgreSQL ' || current_setting('server_version')"
as_migrator "$here/teardown.sql" && as_migrator "$here/fixtures.sql" || exit 2
bash "$here/cases.sh" baseline || status=1
bash "$here/races.sh" baseline || status=1
as_migrator "$here/teardown.sql" || exit 2

echo "== applying $variant as migrator"
for p in $parts; do
  if as_migrator "$here/$p.sql"; then echo "applied: $p"; else echo "FAILED to apply: $p"; exit 2; fi
done

as_migrator "$here/fixtures.sql" || exit 2
bash "$here/cases.sh" "$variant" || status=1
bash "$here/races.sh" "$variant" || status=1
[ "$variant" = altb ] && { bash "$here/effects.sh" || status=1; }
as_migrator "$here/teardown.sql" || exit 2
echo "== rule-11 run ($variant): $([ $status -eq 0 ] && echo 'every case as expected, both directions' || echo 'UNEXPECTED RESULTS above')"
exit $status
