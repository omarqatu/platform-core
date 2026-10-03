-- The probe's fixtures, committed as migrator: one organization of its own ("Probe", P) and persons on probe.test.
-- Everything is removed by teardown.sql. Ids are fixed, so the cases can name them.
--   matcher   — no membership; a pending invitation for '  Match@Probe.TEST ' (case and spaces differ)
--   stranger  — no membership; their OWN pending invitation (stranger@probe.test) — the P2 case
--   stranger2 — no membership, no invitation
--   member    — an active membership, and a pending invitation
--   leaver    — a 'left' membership, and a pending invitation (the return path)
--   leaver2   — a 'left' membership, no invitation
--   expired   — no membership; an invitation past its expiry
--   done      — no membership; an invitation already accepted
--   owner     — P's active owner (P has memberships: no bootstrap exception applies to it)
BEGIN;
INSERT INTO tenants (id, name, status, created_at) VALUES ('0b000000-0000-7000-8000-000000000001', 'Probe', 'active', now());
INSERT INTO roles (id, tenant_id, code, name_ar, name_en, is_system, is_active)
  VALUES ('0b000000-0000-7000-8000-000000000002', '0b000000-0000-7000-8000-000000000001', 'viewer', 'مطّلع', 'Viewer', true, true);

INSERT INTO persons (id, full_name, email, phone_e164, created_at) VALUES
  ('0b000000-0000-7000-8000-000000000101', 'matcher',   'match@probe.test',     NULL, now()),
  ('0b000000-0000-7000-8000-000000000102', 'stranger',  'stranger@probe.test',  NULL, now()),
  ('0b000000-0000-7000-8000-000000000103', 'stranger2', 'stranger2@probe.test', NULL, now()),
  ('0b000000-0000-7000-8000-000000000104', 'member',    'member@probe.test',    NULL, now()),
  ('0b000000-0000-7000-8000-000000000105', 'leaver',    'leaver@probe.test',    NULL, now()),
  ('0b000000-0000-7000-8000-000000000106', 'leaver2',   'leaver2@probe.test',   NULL, now()),
  ('0b000000-0000-7000-8000-000000000107', 'expired',   'expired@probe.test',   NULL, now()),
  ('0b000000-0000-7000-8000-000000000108', 'done',      'done@probe.test',      NULL, now()),
  ('0b000000-0000-7000-8000-000000000109', 'owner',     'owner@probe.test',     NULL, now());
INSERT INTO users (id, person_id, user_type, username, status, language, theme, last_login_at)
  SELECT ('0b000000-0000-7000-8000-0000000002' || right(p.id::text, 2))::uuid, p.id, 'employee', 'probe-' || p.full_name, 'active', 'ar', NULL, NULL
  FROM persons p WHERE p.email LIKE '%@probe.test';

INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES
  ('0b000000-0000-7000-8000-000000000309', '0b000000-0000-7000-8000-000000000001', '0b000000-0000-7000-8000-000000000209', 'active', now()),
  ('0b000000-0000-7000-8000-000000000304', '0b000000-0000-7000-8000-000000000001', '0b000000-0000-7000-8000-000000000204', 'active', now()),
  ('0b000000-0000-7000-8000-000000000305', '0b000000-0000-7000-8000-000000000001', '0b000000-0000-7000-8000-000000000205', 'left',   now()),
  ('0b000000-0000-7000-8000-000000000306', '0b000000-0000-7000-8000-000000000001', '0b000000-0000-7000-8000-000000000206', 'left',   now());
INSERT INTO membership_scope (id, tenant_id, membership_id, scope_mode)
  SELECT ('0b000000-0000-7000-8000-0000000004' || right(m.id::text, 2))::uuid, m.tenant_id, m.id, 'assigned'
  FROM memberships m WHERE m.tenant_id = '0b000000-0000-7000-8000-000000000001';

INSERT INTO invitations (id, tenant_id, email, role_id, token_hash, status, invited_by, expires_at, created_at, intended_scope_mode) VALUES
  ('0b000000-0000-7000-8000-000000000501', '0b000000-0000-7000-8000-000000000001', '  Match@Probe.TEST ',  '0b000000-0000-7000-8000-000000000002', 'probe-501', 'pending',  '0b000000-0000-7000-8000-000000000209', now() + interval '7 days', now(), 'assigned'),
  ('0b000000-0000-7000-8000-000000000502', '0b000000-0000-7000-8000-000000000001', 'stranger@probe.test',  '0b000000-0000-7000-8000-000000000002', 'probe-502', 'pending',  '0b000000-0000-7000-8000-000000000209', now() + interval '7 days', now(), 'assigned'),
  ('0b000000-0000-7000-8000-000000000504', '0b000000-0000-7000-8000-000000000001', 'member@probe.test',    '0b000000-0000-7000-8000-000000000002', 'probe-504', 'pending',  '0b000000-0000-7000-8000-000000000209', now() + interval '7 days', now(), 'assigned'),
  ('0b000000-0000-7000-8000-000000000505', '0b000000-0000-7000-8000-000000000001', 'leaver@probe.test',    '0b000000-0000-7000-8000-000000000002', 'probe-505', 'pending',  '0b000000-0000-7000-8000-000000000209', now() + interval '7 days', now(), 'assigned'),
  ('0b000000-0000-7000-8000-000000000507', '0b000000-0000-7000-8000-000000000001', 'expired@probe.test',   '0b000000-0000-7000-8000-000000000002', 'probe-507', 'pending',  '0b000000-0000-7000-8000-000000000209', now() - interval '1 day',  now() - interval '8 days', 'assigned'),
  ('0b000000-0000-7000-8000-000000000508', '0b000000-0000-7000-8000-000000000001', 'done@probe.test',      '0b000000-0000-7000-8000-000000000002', 'probe-508', 'accepted', '0b000000-0000-7000-8000-000000000209', now() + interval '7 days', now(), 'assigned');
COMMIT;
