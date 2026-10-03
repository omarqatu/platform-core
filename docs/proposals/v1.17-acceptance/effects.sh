#!/usr/bin/env bash
# Alternative B — what accept_invitation() commits, read back afterwards as migrator (not what the function reports):
# the new-membership path and the return path. Each runs on fresh fixtures; torn down after.
#
# Usage: effects.sh      (PSQL_AS as in cases.sh; alternative B applied)
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
psql_as="${PSQL_AS:-docker compose exec -T postgres psql -d platform -U}"
P=0b000000-0000-7000-8000-000000000001; U=0b000000-0000-7000-8000-0000000002; ROLE=0b000000-0000-7000-8000-000000000002
pass=0; fail=0
q() { $psql_as "$1" -X -A -t -v ON_ERROR_STOP=1 -c "$2" 2>&1; }
reset() { $psql_as migrator -X -q -v ON_ERROR_STOP=1 < "$here/teardown.sql" >/dev/null && $psql_as migrator -X -q -v ON_ERROR_STOP=1 < "$here/fixtures.sql" >/dev/null; }

# state <user> <invitation>: the membership and its parts, the token, and the audit entries — one line.
state() {
  q migrator "
    SELECT concat_ws(' ',
      'membership=' || m.status,
      'roles=' || coalesce((SELECT string_agg(r.code, ',' ORDER BY r.code) FROM membership_roles mr JOIN roles r ON r.id = mr.role_id WHERE mr.membership_id = m.id)::text, ''),
      'scope=' || coalesce((SELECT string_agg(s.scope_mode, ',') FROM membership_scope s WHERE s.membership_id = m.id)::text, ''),
      'auth=' || coalesce((SELECT string_agg(a.provider, ',') FROM membership_auth a WHERE a.membership_id = m.id)::text, ''),
      'active_assignments=' || coalesce((SELECT count(*) FROM scope_assignments sa WHERE sa.membership_id = m.id AND sa.active)::text, ''),
      'invitation=' || coalesce((SELECT status FROM invitations WHERE id = '$2')::text, ''),
      'audit=' || coalesce((SELECT string_agg(l.action || ':' || l.entity_type, ',' ORDER BY l.id) FROM audit_log l WHERE l.tenant_id = '$P' AND l.actor_id = '$1' AND l.actor_type = 'user')::text, ''),
      'token_hash_masked=' || coalesce((SELECT bool_and(l.old_value ->> 'token_hash' = '[masked]' AND l.new_value ->> 'token_hash' = '[masked]')
                               FROM audit_log l WHERE l.tenant_id = '$P' AND l.entity_type = 'invitations' AND l.actor_id = '$1')::text, ''))
    FROM memberships m WHERE m.tenant_id = '$P' AND m.user_id = '$1'"
}

check() {
  local id="$1" expected="$2" got="$3" what="$4"
  if [ "$got" = "$expected" ]; then pass=$((pass + 1)); s=PASS; else fail=$((fail + 1)); s=FAIL; fi
  printf '%s  %-3s %s\n      expected %s\n      got      %s\n' "$s" "$id" "$what" "$expected" "$got"
}

echo "== effects, alternative B"
reset
call="$(q app_user "BEGIN; SET LOCAL app.user_id = '${U}01'; SELECT returned FROM public.accept_invitation('$P', 'tok-501', '203.0.113.7'); COMMIT;")"
check X1 "membership=active roles=viewer scope=assigned auth=password active_assignments=0 invitation=accepted audit=insert:memberships,insert:membership_roles,insert:membership_scope,insert:membership_auth,update:invitations token_hash_masked=true" \
  "$(state ${U}01 0b000000-0000-7000-8000-000000000501)" "a new membership: every part in the invitation's role and mode, the token consumed, one audit entry per row (call: $(tr '\n' ' ' <<< "$call"))"

# The return: the leaver held an older role, an 'all' scope and an active assignment — all replaced in the binding order.
reset
setup="$(q migrator "
  INSERT INTO roles (id, tenant_id, code, name_ar, name_en, is_system, is_active) VALUES ('0b000000-0000-7000-8000-000000000003', '$P', 'old', 'قديم', 'Old', false, true);
  INSERT INTO membership_roles (id, tenant_id, membership_id, role_id) VALUES ('0b000000-0000-7000-8000-000000000803', '$P', '0b000000-0000-7000-8000-000000000305', '0b000000-0000-7000-8000-000000000003');
  UPDATE membership_scope SET scope_mode = 'all' WHERE membership_id = '0b000000-0000-7000-8000-000000000305';
  INSERT INTO scope_assignments (id, tenant_id, membership_id, scope_ref_id, assignment_role, active, reason)
    VALUES ('0b000000-0000-7000-8000-000000000903', '$P', '0b000000-0000-7000-8000-000000000305', '0b000000-0000-7000-8000-000000000999', 'lead', true, NULL);")"
grep -q ERROR <<< "$setup" && echo "effects: the return's setup was refused: $setup"
call="$(q app_user "BEGIN; SET LOCAL app.user_id = '${U}05'; SELECT returned FROM public.accept_invitation('$P', 'tok-505'); COMMIT;")"
check X2 "membership=active roles=viewer scope=assigned auth= active_assignments=0 invitation=accepted audit=delete:membership_roles,update:membership_scope,update:scope_assignments,insert:membership_roles,update:memberships,update:invitations token_hash_masked=true" \
  "$(state ${U}05 0b000000-0000-7000-8000-000000000505)" "the return: the same row, old role deleted, scope set to the invitation's mode, assignment disabled, re-activated last (call: $(tr '\n' ' ' <<< "$call"))"

reset
echo "== effects, alternative B: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
