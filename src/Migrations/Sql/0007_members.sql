-- T4 (PROOF_SPEC v1.3) — PLATFORM_CORE v1.16 §3.10, §4.5, §3.9, §3.8: managing members in the database.
-- Every policy text below is the document's, extracted verbatim from its sql blocks (the extraction that also
-- generates checks/core/manifest.json). Findings and reproduction: the document's header and §3.10.

-- ===== D1 (§3.10) — core.members.manage, in the owner and admin templates; existing tenants' system roles
-- upgraded from the templates by migrator (one source of truth, 5). Data: the document states it in prose.
INSERT INTO permissions (id, module_id, code, name_ar, name_en)
SELECT uuidv7(), p.module_id, 'core.members.manage', 'إدارة الأعضاء', 'Manage members'
FROM permissions p WHERE p.code = 'core.scope.manage';

INSERT INTO role_template_permissions (template_id, permission_id)
SELECT t.id, p.id FROM role_templates t CROSS JOIN permissions p
WHERE t.code IN ('owner', 'admin') AND p.code = 'core.members.manage';

INSERT INTO role_permissions (id, tenant_id, role_id, permission_id)
SELECT uuidv7(), r.tenant_id, r.id, rtp.permission_id
FROM roles r
JOIN role_templates t ON t.code = r.code AND r.is_system
JOIN role_template_permissions rtp ON rtp.template_id = t.id
JOIN permissions p ON p.id = rtp.permission_id AND p.code = 'core.members.manage';

-- ===== D2 (§3.10, §4.1) — membership status: three values, in the constraints layer.
ALTER TABLE memberships ADD CONSTRAINT memberships_status_check
  CHECK (status IN ('active', 'disabled', 'left'));

-- ===== D3, D4 (§4.5), D6, D8 (§3.9) — the four texts v1.16 replaces.
DROP POLICY membership_tenant_update ON memberships;
DROP POLICY membership_self_leave ON memberships;
DROP POLICY invitations_insert ON invitations;
DROP POLICY invitations_tenant_update ON invitations;

CREATE POLICY membership_tenant_update ON memberships
  FOR UPDATE TO app_user
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND COALESCE((SELECT NULLIF(current_setting('app.can_manage_members', true), '')::boolean), false)
    AND id <> (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
    AND status IN ('active', 'disabled'))
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND COALESCE((SELECT NULLIF(current_setting('app.can_manage_members', true), '')::boolean), false)
    AND id <> (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
    AND status IN ('active', 'disabled'));

CREATE POLICY membership_self_leave ON memberships
  FOR UPDATE TO app_user
  USING (
    user_id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid)
    AND status = 'active')
  WITH CHECK (
    user_id = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid)
    AND status = 'left');

CREATE POLICY invitations_insert ON invitations
  FOR INSERT TO app_user
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND invited_by = (SELECT NULLIF(current_setting('app.user_id', true), '')::uuid)
    AND COALESCE((SELECT NULLIF(current_setting('app.can_manage_members', true), '')::boolean), false)
    AND status = 'pending'
    AND (intended_scope_mode = 'assigned'
         OR COALESCE((SELECT NULLIF(current_setting('app.can_manage_scope', true), '')::boolean), false)));

CREATE POLICY invitations_tenant_update ON invitations
  FOR UPDATE TO app_user
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND COALESCE((SELECT NULLIF(current_setting('app.can_manage_members', true), '')::boolean), false)
    AND status = 'pending')
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND COALESCE((SELECT NULLIF(current_setting('app.can_manage_members', true), '')::boolean), false)
    AND status = 'revoked');

-- ===== D5, D7, D9, D10 (§3.10) — the new policies.

CREATE POLICY membership_lock ON memberships
  FOR UPDATE TO app_user
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND id <> (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
    AND status = 'active'
    AND (COALESCE((SELECT NULLIF(current_setting('app.can_manage_members', true), '')::boolean), false)
         OR COALESCE((SELECT NULLIF(current_setting('app.can_manage_scope', true), '')::boolean), false)))
  WITH CHECK (false);   -- a row lock (FOR UPDATE), never a write

CREATE POLICY membership_roles_manage_insert ON membership_roles
  AS RESTRICTIVE FOR INSERT TO app_user
  WITH CHECK (
    COALESCE((SELECT NULLIF(current_setting('app.can_manage_members', true), '')::boolean), false)
    AND membership_id <> (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid));

CREATE POLICY membership_roles_manage_delete ON membership_roles
  AS RESTRICTIVE FOR DELETE TO app_user
  USING (
    COALESCE((SELECT NULLIF(current_setting('app.can_manage_members', true), '')::boolean), false)
    AND membership_id <> (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid));

CREATE POLICY memberships_provisioner_rejoin ON memberships
  FOR UPDATE TO provisioner
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND status = 'left')
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND status = 'active');

CREATE POLICY membership_roles_provisioner_select ON membership_roles
  FOR SELECT TO provisioner
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND EXISTS (SELECT 1 FROM memberships m
                WHERE m.id = membership_roles.membership_id AND m.status = 'left'));

CREATE POLICY membership_roles_provisioner_delete ON membership_roles
  FOR DELETE TO provisioner
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND EXISTS (SELECT 1 FROM memberships m
                WHERE m.id = membership_roles.membership_id AND m.status = 'left'));

CREATE POLICY membership_scope_provisioner_select ON membership_scope
  FOR SELECT TO provisioner
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND EXISTS (SELECT 1 FROM memberships m
                WHERE m.id = membership_scope.membership_id AND m.status = 'left'));

CREATE POLICY membership_scope_provisioner_rejoin ON membership_scope
  FOR UPDATE TO provisioner
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND EXISTS (SELECT 1 FROM memberships m
                WHERE m.id = membership_scope.membership_id AND m.status = 'left'))
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND EXISTS (SELECT 1 FROM memberships m
                WHERE m.id = membership_scope.membership_id AND m.status = 'left'));

CREATE POLICY scope_assignments_provisioner_select ON scope_assignments
  FOR SELECT TO provisioner
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND EXISTS (SELECT 1 FROM memberships m
                WHERE m.id = scope_assignments.membership_id AND m.status = 'left'));

CREATE POLICY scope_assignments_provisioner_disable ON scope_assignments
  FOR UPDATE TO provisioner
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND EXISTS (SELECT 1 FROM memberships m
                WHERE m.id = scope_assignments.membership_id AND m.status = 'left'))
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND EXISTS (SELECT 1 FROM memberships m
                WHERE m.id = scope_assignments.membership_id AND m.status = 'left')
    AND NOT active);

-- ===== §3.8 — the provisioner grants of D9 and D10 (return only). Nothing changes for app_user.
GRANT UPDATE (status) ON memberships TO provisioner;
GRANT SELECT, DELETE ON membership_roles TO provisioner;
GRANT SELECT, UPDATE (scope_mode) ON membership_scope TO provisioner;
GRANT SELECT, UPDATE (active) ON scope_assignments TO provisioner;
