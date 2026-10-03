#!/usr/bin/env bash
# Condition 5 — two acceptances of the same token at once (PROOF_SPEC rule 11): two provisioner transactions start
# together, each holds what it wrote for 2 s before committing. Setup and the count afterwards run as migrator; every
# race is torn down after it (teardown.sql + fixtures.sql). "assert" is what the application does after consuming the
# token (CriticalWrite: zero rows → loud); "raw" is the same without it — what the database alone guarantees.
#
# Usage: races.sh baseline|draft     (PSQL_AS as in cases.sh)
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
phase="${1:?baseline|draft}"
psql_as="${PSQL_AS:-docker compose exec -T postgres psql -d platform -U}"
P=0b000000-0000-7000-8000-000000000001; INV=0b000000-0000-7000-8000-000000000501
pass=0; fail=0
ab() { [ "$phase" = draft ] && echo ", accepted_by = '$1'" || true; }
q() { $psql_as migrator -X -A -t -v ON_ERROR_STOP=1 -c "$1" 2>&1; }
reset() { $psql_as migrator -X -q -v ON_ERROR_STOP=1 < "$here/teardown.sql" >/dev/null && $psql_as migrator -X -q -v ON_ERROR_STOP=1 < "$here/fixtures.sql" >/dev/null; }

# accept <user> <assert|raw>: one acceptance of INV by <user>, holding its writes 2 s; prints COMMITTED or the SQLSTATE.
# assert: the token's UPDATE inside a DO block that raises when it touched no row (CriticalWrite's rows-affected check).
accept() {
  local user="$1" mode="$2" m s update consume out
  m="$(python3 -c 'import uuid;print(uuid.uuid4())')"; s="$(python3 -c 'import uuid;print(uuid.uuid4())')"
  update="UPDATE invitations SET status = 'accepted' $(ab "$user") WHERE id = '$INV' AND status = 'pending'"
  if [ "$mode" = assert ]; then
    consume="DO \$\$ BEGIN $update; IF NOT FOUND THEN RAISE EXCEPTION USING ERRCODE = 'P0002', MESSAGE = 'zero rows: consumed by another'; END IF; END \$\$;"
  else
    consume="$update;"
  fi
  out="$($psql_as provisioner -X -A -t -v ON_ERROR_STOP=1 2>&1 <<SQL
\set VERBOSITY sqlstate
BEGIN;
SET LOCAL app.tenant_id = '$P';
INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES ('$m', '$P', '$user', 'active', now());
INSERT INTO membership_scope (id, tenant_id, membership_id, scope_mode) VALUES ('$s', '$P', '$m', 'assigned');
$consume
SELECT pg_sleep(2);
COMMIT;
SQL
)"
  if grep -q 'ERROR:' <<< "$out"; then grep -oE 'ERROR: +[0-9A-Z]{5}' <<< "$out" | head -1 | awk '{print "error=" $2}'; else echo COMMITTED; fi
}

# race <id> <user1> <user2> <mode> <expected committed memberships for INV's address> <what>
race() {
  local id="$1" u1="$2" u2="$3" mode="$4" expected="$5" what="$6" r1 r2 got status
  reset
  [ -n "${SETUP:-}" ] && { setup_out="$(q "$SETUP")"; if grep -q ERROR <<< "$setup_out"; then
      got="setup refused: $(grep -oE 'ERROR: +[^ ]+.*' <<< "$setup_out" | head -1)"
      if [ "$expected" = "setup-refused" ]; then pass=$((pass + 1)); status=PASS; else fail=$((fail + 1)); status=FAIL; fi
      printf '%s  %-4s expected %-14s got %s — %s\n' "$status" "$id" "$expected" "$got" "$what"; return; fi; }
  ( accept "$u1" "$mode" > /tmp/race1 ) & ( sleep 0.3; accept "$u2" "$mode" > /tmp/race2 ) & wait
  r1="$(cat /tmp/race1)"; r2="$(cat /tmp/race2)"
  got="$(q "SELECT count(*) FROM memberships m WHERE m.tenant_id = '$P' AND m.user_id IN ('$u1', '$u2') AND m.created_at > now() - interval '1 minute'")"
  if [ "$got" = "$expected" ]; then pass=$((pass + 1)); status=PASS; else fail=$((fail + 1)); status=FAIL; fi
  printf '%s  %-4s expected %-3s memberships, got %-3s (first: %s; second: %s) — %s\n' "$status" "$id" "$expected" "$got" "$r1" "$r2" "$what"
}

echo "== races, $phase"
M1=0b000000-0000-7000-8000-000000000201
SETUP="" race R1 $M1 $M1 assert 1 "the same token, the same user, twice (application check on)"
SETUP="" race R2 $M1 $M1 raw    1 "the same token, the same user, twice (no application check)"
# A second person whose address differs from match@probe.test only in case — possible without P3 only.
M2=0b000000-0000-7000-8000-000000000299
TWIN="INSERT INTO persons VALUES ('0b000000-0000-7000-8000-000000000199', 'twin', 'MATCH@probe.test', NULL, now()); INSERT INTO users VALUES ('$M2', '0b000000-0000-7000-8000-000000000199', 'employee', 'probe-twin', 'active', 'ar', NULL, NULL);"
if [ "$phase" = baseline ]; then
  SETUP="$TWIN" race R3 $M1 $M2 assert 1 "the same token, two persons with the same address in another case (application check on)"
  SETUP="$TWIN" race R4 $M1 $M2 raw    2 "the same token, two persons with the same address in another case (no application check)"
else
  SETUP="$TWIN" race R3 $M1 $M2 assert setup-refused "the same token, two persons with the same address in another case"
  SETUP="$TWIN" race R4 $M1 $M2 raw    setup-refused "the same token, two persons with the same address in another case (no application check)"
fi
reset
echo "== races, $phase: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
