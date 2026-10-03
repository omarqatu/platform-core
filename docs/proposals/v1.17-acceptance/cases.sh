#!/usr/bin/env bash
# The probe's cases (PROOF_SPEC rule 11): each case runs ALONE, as its role, with only the context it names, in a
# transaction that is ALWAYS rolled back. Its result is the command tag of its last statement (rows=N, or the tag)
# or error=SQLSTATE. The expected result of each direction was written into the case before it ran.
#
# Three phases: baseline (v1.16 as migrated), draft (P1–P4), altb (alternative B: P3 and accept_invitation()).
# Each case states its baseline and draft results; under altb it expects its baseline result unless ALT names
# another — B changes no policy, so every direct write keeps its v1.16 result but for P3's.
#
# Usage: cases.sh baseline|draft|altb (PSQL_AS: the command that runs psql as a role, followed by the role name;
#                                      default: docker compose exec -T postgres psql -d platform -U)
set -uo pipefail
phase="${1:?baseline|draft|altb}"
psql_as="${PSQL_AS:-docker compose exec -T postgres psql -d platform -U}"
P=0b000000-0000-7000-8000-000000000001
pass=0; fail=0
# altb's expectation where it is not the baseline's.
declare -A ALT=(
  [E1]=error=23505
  [G1]=row=new [G2]=error=42501 [G3]=error=42501 [G4]=error=P0002 [G5]=error=P0002 [G6]=error=23505 [G7]=row=returned
  [G8]=error=42501 [G9]=error=P0002 [G10]=error=P0002 [G11]=error=42501 [G12]=row=no-tenant
  [G13]=error=42501 [G14]=error=42501 [G15]=error=42501 [G16]=error=42501
  [G17]="row=true;search_path=pg_catalog, public, pg_temp" [G18]=row=app_user,migrator
)

# accepted_by exists in the draft only (P2): the same acceptance, as each schema can express it.
ab() { [ "$phase" = draft ] && echo ", accepted_by = '$1'" || true; }
uuid() { python3 -c 'import uuid; print(uuid.uuid4())'; }

# c <id> <role> <expected baseline> <expected draft> <description>   (the SQL on stdin)
c() {
  local id="$1" role="$2" base="$3" draft="$4" what="$5" sql out got expected
  sql="$(cat)"
  expected="$base"; [ "$phase" = draft ] && expected="$draft"; [ "$phase" = altb ] && expected="${ALT[$id]:-$base}"
  out="$( { printf '\\set VERBOSITY sqlstate\nBEGIN;\n%s\nROLLBACK;\n' "$sql"; } | $psql_as "$role" -X -A -t -v ON_ERROR_STOP=1 2>&1)"
  if grep -qE '^(psql:.*)?ERROR:' <<< "$out"; then
    got="error=$(grep -oE 'ERROR: +[0-9A-Z]{5}' <<< "$out" | head -1 | awk '{print $2}')"
  else
    got="$(grep -vE '^(BEGIN|ROLLBACK|SET)$' <<< "$out" | grep -E '^(INSERT|UPDATE|DELETE|SELECT|DO|SET CONSTRAINTS|row=)' | tail -1)"
    case "$got" in
      "INSERT 0 "*) got="rows=${got#INSERT 0 }" ;;
      "UPDATE "*|"DELETE "*) got="rows=${got#* }" ;;
    esac
  fi
  if [ "$got" = "$expected" ]; then pass=$((pass + 1)); s=PASS; else fail=$((fail + 1)); s=FAIL; fi
  printf '%s  %-4s %-11s expected %-16s got %-16s %s\n' "$s" "$id" "$role" "$expected" "$got" "$what"
}

ctx() { echo "SET LOCAL app.tenant_id = '$1';"; }
member() { echo "INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES ('$(uuid)', '$1', '$2', 'active', now());"; }

echo "== $phase"
# --- A: a new membership (condition 2, condition 3: none / active)
c A1 provisioner rows=1 rows=1 "match (case and spaces differ): new membership" <<SQL
$(ctx $P) $(member $P 0b000000-0000-7000-8000-000000000201)
SQL
c A2 provisioner rows=1 error=42501 "no matching invitation (stranger2): new membership — refused by the DB alone" <<SQL
$(ctx $P) $(member $P 0b000000-0000-7000-8000-000000000203)
SQL
c A3 provisioner rows=1 error=42501 "matching invitation is expired" <<SQL
$(ctx $P) $(member $P 0b000000-0000-7000-8000-000000000207)
SQL
c A4 provisioner rows=1 error=42501 "matching invitation already accepted (consumed)" <<SQL
$(ctx $P) $(member $P 0b000000-0000-7000-8000-000000000208)
SQL
c A5 provisioner error=23505 error=23505 "an active membership exists: a second one" <<SQL
$(ctx $P) $(member $P 0b000000-0000-7000-8000-000000000204)
SQL
# --- B: the return path (condition 3: left)
c B1 provisioner rows=1 rows=1 "left + matching invitation: re-activation" <<SQL
$(ctx $P) UPDATE memberships SET status = 'active' WHERE id = '0b000000-0000-7000-8000-000000000305';
SQL
c B2 provisioner rows=1 error=42501 "left + no invitation: re-activation — refused by the DB alone" <<SQL
$(ctx $P) UPDATE memberships SET status = 'active' WHERE id = '0b000000-0000-7000-8000-000000000306';
SQL
c B3 provisioner rows=0 rows=0 "an active membership is not re-activated (USING status = 'left')" <<SQL
$(ctx $P) UPDATE memberships SET status = 'active' WHERE id = '0b000000-0000-7000-8000-000000000304';
SQL
# --- C: consuming the token (condition 2 bound to the token, condition 5 single use)
c C1 provisioner rows=1 rows=1 "the matcher joins, then consumes their own invitation" <<SQL
$(ctx $P) $(member $P 0b000000-0000-7000-8000-000000000201)
UPDATE invitations SET status = 'accepted' $(ab 0b000000-0000-7000-8000-000000000201) WHERE id = '0b000000-0000-7000-8000-000000000501' AND status = 'pending';
SQL
c C2 provisioner rows=1 error=42501 "a stranger with their OWN invitation joins, then consumes the matcher's token" <<SQL
$(ctx $P) $(member $P 0b000000-0000-7000-8000-000000000202)
UPDATE invitations SET status = 'accepted' $(ab 0b000000-0000-7000-8000-000000000202) WHERE id = '0b000000-0000-7000-8000-000000000501' AND status = 'pending';
SQL
c C3 provisioner rows=1 rows=0 "an accepted invitation accepted again (no status filter in the statement)" <<SQL
$(ctx $P) UPDATE invitations SET status = 'accepted' WHERE id = '0b000000-0000-7000-8000-000000000508';
SQL
c C4 provisioner rows=1 error=42501 "consumed with no acceptor recorded" <<SQL
$(ctx $P) $(member $P 0b000000-0000-7000-8000-000000000201)
UPDATE invitations SET status = 'accepted' WHERE id = '0b000000-0000-7000-8000-000000000501';
SQL
c C5 provisioner rows=1 error=42501 "a pending invitation set to 'revoked' by the provisioner" <<SQL
$(ctx $P) UPDATE invitations SET status = 'revoked' WHERE id = '0b000000-0000-7000-8000-000000000501';
SQL
# --- D: bootstrap (4.4) — through an invitation for the owner (P1b), in a tenant with no member yet
B=0b000000-0000-7000-8000-0000000006$(printf '%02d' $((RANDOM % 90 + 10)))
RB=0b000000-0000-7000-8000-0000000007$(printf '%02d' $((RANDOM % 90 + 10)))
OWNER=0b000000-0000-7000-8000-000000000203   # stranger2: no membership anywhere, no invitation
found() { cat <<F
$(ctx $B) INSERT INTO tenants (id, name, status, created_at) VALUES ('$B', 'Probe B', 'active', now());
INSERT INTO roles (id, tenant_id, code, name_ar, name_en, is_system, is_active) VALUES ('$RB', '$B', 'owner', 'مالك', 'Owner', true, true);
F
}
invite_owner() { echo "INSERT INTO invitations (id, tenant_id, email, role_id, token_hash, status, invited_by, expires_at, created_at, intended_scope_mode) VALUES ('$1', '$B', 'stranger2@probe.test', '$RB', 'probe-$1', 'pending', '$OWNER', now() + interval '1 day', now(), 'all');"; }
c D1 provisioner error=42501 rows=1 "bootstrap: the founding invitation for the owner" <<SQL
$(found)
$(invite_owner $(uuid))
SQL
I=$(uuid); M=$(uuid)
c D2 provisioner "SET CONSTRAINTS" "SET CONSTRAINTS" "bootstrap, whole: tenant, role, invitation (draft), membership, scope, accepted" <<SQL
$(found)
$( [ "$phase" = draft ] && invite_owner $I )
INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES ('$M', '$B', '$OWNER', 'active', now());
INSERT INTO membership_scope (id, tenant_id, membership_id, scope_mode) VALUES ('$(uuid)', '$B', '$M', 'all');
$( [ "$phase" = draft ] && echo "UPDATE invitations SET status = 'accepted', accepted_by = '$OWNER' WHERE id = '$I' AND status = 'pending';" )
SET CONSTRAINTS ALL IMMEDIATE;
SQL
c D3 provisioner rows=1 error=42501 "bootstrap with no invitation: the owner's membership" <<SQL
$(found)
$(member $B $OWNER)
SQL
c D4 provisioner error=42501 error=42501 "the provisioner creates an invitation in a tenant with members (P)" <<SQL
$(ctx $P) INSERT INTO invitations (id, tenant_id, email, role_id, token_hash, status, invited_by, expires_at, created_at, intended_scope_mode) VALUES ('$(uuid)', '$P', 'stranger2@probe.test', '0b000000-0000-7000-8000-000000000002', 'probe-d4', 'pending', '0b000000-0000-7000-8000-000000000209', now() + interval '1 day', now(), 'all');
SQL
I=$(uuid); M=$(uuid)
c D5 provisioner error=42501 error=42501 "bootstrap: a second invitation once the owner is a member" <<SQL
$(found)
$( [ "$phase" = draft ] && invite_owner $I )
INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES ('$M', '$B', '$OWNER', 'active', now());
$(invite_owner $(uuid))
SQL
c D6 provisioner error=42501 error=42501 "a founding invitation already 'accepted' (only 'pending' may be created)" <<SQL
$(found)
INSERT INTO invitations (id, tenant_id, email, role_id, token_hash, status, invited_by, expires_at, created_at, intended_scope_mode) VALUES ('$(uuid)', '$B', 'stranger2@probe.test', '$RB', 'probe-d6', 'accepted', '$OWNER', now() + interval '1 day', now(), 'all');
SQL
# --- E: one account per normalized address (the closing paragraph)
c E1 provisioner rows=1 error=23505 "a new person ' MATCH@probe.test' when match@probe.test exists" <<SQL
INSERT INTO persons (id, full_name, email, phone_e164, created_at) VALUES ('$(uuid)', 'dup', ' MATCH@probe.test', NULL, now());
SQL
c E2 provisioner rows=1 rows=1 "a new person with a new address" <<SQL
INSERT INTO persons (id, full_name, email, phone_e164, created_at) VALUES ('$(uuid)', 'new', 'new@probe.test', NULL, now());
SQL
# --- F: no membership without membership_scope (condition 4, T3.8) — deferred, so forced at the end
c F1 provisioner "SET CONSTRAINTS" error=23514 "a membership with no scope row, at commit" <<SQL
$(ctx $P) $(member $P 0b000000-0000-7000-8000-000000000201)
SET CONSTRAINTS ALL IMMEDIATE;
SQL
M=$(uuid)
c F2 provisioner "SET CONSTRAINTS" "SET CONSTRAINTS" "the full acceptance: membership, role, scope, auth, invitation" <<SQL
$(ctx $P) INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES ('$M', '$P', '0b000000-0000-7000-8000-000000000201', 'active', now());
INSERT INTO membership_roles (id, tenant_id, membership_id, role_id) VALUES ('$(uuid)', '$P', '$M', '0b000000-0000-7000-8000-000000000002');
INSERT INTO membership_scope (id, tenant_id, membership_id, scope_mode) VALUES ('$(uuid)', '$P', '$M', 'assigned');
INSERT INTO membership_auth (id, tenant_id, membership_id, provider, provider_config) VALUES ('$(uuid)', '$P', '$M', 'password', NULL);
UPDATE invitations SET status = 'accepted' $(ab 0b000000-0000-7000-8000-000000000201) WHERE id = '0b000000-0000-7000-8000-000000000501' AND status = 'pending';
SET CONSTRAINTS ALL IMMEDIATE;
SQL
c F3 migrator "SET CONSTRAINTS" error=23514 "even the owner role (migrator, BYPASSRLS) cannot commit a membership with no scope" <<SQL
$(member $P 0b000000-0000-7000-8000-000000000203)
SET CONSTRAINTS ALL IMMEDIATE;
SQL

# --- G: alternative B — public.accept_invitation(), called by app_user with app.user_id alone. Under baseline and
# draft it does not exist (42883): what v1.16 does with the same acceptance is the direct-write cases above.
U=0b000000-0000-7000-8000-0000000002
uctx() { echo "SET LOCAL app.user_id = '$1';"; }
call() { echo "SELECT 'row=' || CASE WHEN returned THEN 'returned' ELSE 'new' END FROM public.accept_invitation('$1', '$2');"; }
NOFN=error=42883
c G1 app_user $NOFN $NOFN "match (case and spaces differ): a new membership, its parts, the token consumed" <<SQL
$(uctx ${U}01) $(call $P tok-501)
SQL
c G2 app_user $NOFN $NOFN "the address does not match (stranger2, matcher's token) — refused by the DB, no application" <<SQL
$(uctx ${U}03) $(call $P tok-501)
SQL
c G3 app_user $NOFN $NOFN "a stranger holding their OWN invitation, with the matcher's token" <<SQL
$(uctx ${U}02) $(call $P tok-501)
SQL
c G4 app_user $NOFN $NOFN "the matching invitation has expired" <<SQL
$(uctx ${U}07) $(call $P tok-507)
SQL
c G5 app_user $NOFN $NOFN "the matching invitation is already accepted (consumed)" <<SQL
$(uctx ${U}08) $(call $P tok-508)
SQL
c G6 app_user $NOFN $NOFN "an active membership already exists" <<SQL
$(uctx ${U}04) $(call $P tok-504)
SQL
c G7 app_user $NOFN $NOFN "left + its own invitation: the return" <<SQL
$(uctx ${U}05) $(call $P tok-505)
SQL
c G8 app_user $NOFN $NOFN "left, but another's token (leaver2 with leaver's)" <<SQL
$(uctx ${U}06) $(call $P tok-505)
SQL
c G9 app_user $NOFN $NOFN "an unknown token" <<SQL
$(uctx ${U}01) $(call $P tok-nothing)
SQL
c G10 app_user $NOFN $NOFN "the token, with another tenant's id (T4.11)" <<SQL
$(uctx ${U}01) $(call 0b000000-0000-7000-8000-0000000000ff tok-501)
SQL
c G11 app_user $NOFN $NOFN "no app.user_id" <<SQL
$(call $P tok-501)
SQL
c G12 app_user $NOFN $NOFN "after accepting, no tenant is selected (condition 6): app.tenant_id is unset" <<SQL
$(uctx ${U}01) $(call $P tok-501)
SELECT 'row=' || coalesce(nullif(current_setting('app.tenant_id', true), ''), 'no-tenant');
SQL
c G13 provisioner $NOFN $NOFN "the provisioner cannot execute it" <<SQL
$(uctx ${U}01) $(call $P tok-501)
SQL
c G14 authenticator $NOFN $NOFN "the authenticator cannot execute it" <<SQL
$(uctx ${U}01) $(call $P tok-501)
SQL
c G15 job_runner $NOFN $NOFN "the job runner cannot execute it" <<SQL
$(uctx ${U}01) $(call $P tok-501)
SQL
c G16 app_user error=42501 error=42501 "app_user still cannot write a membership directly (B widens no table grant)" <<SQL
$(uctx ${U}01) $(member $P ${U}01)
SQL
c G17 migrator $NOFN $NOFN "the catalog: SECURITY DEFINER, search_path pinned" <<SQL
SELECT 'row=' || p.prosecdef || ';' || array_to_string(p.proconfig, ';') FROM pg_proc p WHERE p.oid = 'public.accept_invitation(uuid, text, text)'::regprocedure;
SQL
c G18 migrator $NOFN $NOFN "the catalog: who may execute it (no PUBLIC)" <<SQL
SELECT 'row=' || string_agg(g, ',' ORDER BY g) FROM (
  SELECT CASE WHEN a.grantee = 0 THEN 'PUBLIC' ELSE a.grantee::regrole::text END AS g
  FROM pg_proc p CROSS JOIN LATERAL aclexplode(p.proacl) a
  WHERE p.oid = 'public.accept_invitation(uuid, text, text)'::regprocedure AND a.privilege_type = 'EXECUTE') x;
SQL

echo "== $phase: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
