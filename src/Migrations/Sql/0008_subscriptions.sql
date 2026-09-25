-- T5 (PROOF_SPEC v1.3) — the first scoped module: Modules.Subscriptions (PLATFORM_CORE v1.16 §3.1, §3.2, §3.3, §3.8,
-- §4.8, §5, §9). Module tables, owned by the module; this migration tool is their only writer of schema.

-- ===== §5 — the module and its permissions (the project owner's decision in T5: subscriptions.read for the
-- clients and subscriptions lists, subscriptions.write for creating a subscription, core.audit.read for the audit
-- log's surface). Data seeded by migrator; the templates carry them, and existing tenants' system roles are
-- upgraded from the templates — one source of truth (5), as D1 did in 0007.
INSERT INTO modules (id, code, name_ar, name_en, icon, display_order, is_active)
VALUES (uuidv7(), 'subscriptions', 'الاشتراكات', 'Subscriptions', NULL, 1, true);

INSERT INTO permissions (id, module_id, code, name_ar, name_en)
SELECT uuidv7(), m.id, p.code, p.name_ar, p.name_en
FROM modules m JOIN (VALUES
  ('subscriptions', 'subscriptions.read',  'عرض العملاء والاشتراكات', 'View clients and subscriptions'),
  ('subscriptions', 'subscriptions.write', 'إنشاء الاشتراكات',        'Create subscriptions'),
  ('core',          'core.audit.read',     'عرض سجل التدقيق',         'View the audit log')
) AS p (module_code, code, name_ar, name_en) ON p.module_code = m.code;

INSERT INTO role_template_permissions (template_id, permission_id)
SELECT t.id, p.id FROM role_templates t JOIN permissions p
  ON (p.code IN ('subscriptions.read', 'core.audit.read') AND t.code IN ('owner', 'admin', 'operator', 'viewer'))
  OR (p.code = 'subscriptions.write' AND t.code IN ('owner', 'admin', 'operator'));

INSERT INTO role_permissions (id, tenant_id, role_id, permission_id)
SELECT uuidv7(), r.tenant_id, r.id, rtp.permission_id
FROM roles r
JOIN role_templates t ON t.code = r.code AND r.is_system
JOIN role_template_permissions rtp ON rtp.template_id = t.id
JOIN permissions p ON p.id = rtp.permission_id AND p.code IN ('subscriptions.read', 'subscriptions.write', 'core.audit.read');

-- Row-count confirmation (§3.4, condition 2): 1 module, 3 permissions, 4 + 3 + 4 template grants.
DO $$
BEGIN
  IF (SELECT count(*) FROM modules WHERE code = 'subscriptions') <> 1
     OR (SELECT count(*) FROM permissions WHERE code IN ('subscriptions.read', 'subscriptions.write', 'core.audit.read')) <> 3
     OR (SELECT count(*) FROM role_template_permissions rtp JOIN permissions p ON p.id = rtp.permission_id
         WHERE p.code IN ('subscriptions.read', 'subscriptions.write', 'core.audit.read')) <> 11 THEN
    RAISE EXCEPTION 'T5 catalog row counts are wrong';
  END IF;
END $$;

-- ===== The module's tables (PROOF_SPEC T5). Keys and timestamps as §2: app_id keys, app_ts timestamps, no
-- DEFAULT, identity or generated column.
-- clients is a scoped entity whose own id is its scope_ref_id (T5: "a generated column or a synonym, decided and
-- justified in the PR"). A synonym: an ordinary column the application writes equal to id, held by a CHECK. A
-- generated column is excluded by §2 and Test 28a (no attgenerated anywhere in public). The column carries the
-- name the second template reads, so client_scope applies to clients in its standard text (§3.1, Check 8).
CREATE TABLE clients (
  id           app_id PRIMARY KEY,
  tenant_id    uuid   NOT NULL,
  scope_ref_id uuid   NOT NULL,
  name         text   NOT NULL,
  created_at   app_ts NOT NULL,
  CONSTRAINT clients_scope_ref_id_is_id CHECK (scope_ref_id = id)
);

CREATE TABLE subscriptions (
  id           app_id PRIMARY KEY,
  tenant_id    uuid   NOT NULL,
  scope_ref_id uuid   NOT NULL,
  service_name text   NOT NULL,
  ends_on      date   NOT NULL,
  created_at   app_ts NOT NULL
);

-- §3.3: the composite FK within the module — a subscription's client is in the subscription's own tenant (Test 21,
-- the scope_ref_id part). scope_assignments.scope_ref_id in the core stays without an FK: the entity's home is not
-- settled here (§9).
ALTER TABLE clients ADD CONSTRAINT clients_tenant_fkey FOREIGN KEY (tenant_id) REFERENCES tenants (id);
ALTER TABLE clients ADD CONSTRAINT clients_tenant_id_id_key UNIQUE (tenant_id, id);
ALTER TABLE subscriptions ADD CONSTRAINT subscriptions_tenant_fkey FOREIGN KEY (tenant_id) REFERENCES tenants (id);
ALTER TABLE subscriptions ADD CONSTRAINT subscriptions_client_fkey
  FOREIGN KEY (tenant_id, scope_ref_id) REFERENCES clients (tenant_id, id);

-- §3.2: every index starts with tenant_id.
CREATE INDEX ix_clients_tenant_scope_ref_id ON clients (tenant_id, scope_ref_id);
CREATE INDEX ix_subscriptions_tenant_scope_ref_id ON subscriptions (tenant_id, scope_ref_id);

-- ===== §3.1 — both templates, verbatim (the first for every module table; the second because each table carries
-- scope_ref_id — Check 8). client_scope is FOR ALL on purpose: narrowing reads is intended (the 1.12 rule).

ALTER TABLE clients ENABLE ROW LEVEL SECURITY;
ALTER TABLE clients FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON clients
  FOR ALL TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY client_scope ON clients
  AS RESTRICTIVE FOR ALL TO app_user
  USING (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = clients.tenant_id
        AND sa.scope_ref_id  = clients.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active))
  WITH CHECK (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = clients.tenant_id
        AND sa.scope_ref_id  = clients.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active));

ALTER TABLE subscriptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE subscriptions FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON subscriptions
  FOR ALL TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

CREATE POLICY client_scope ON subscriptions
  AS RESTRICTIVE FOR ALL TO app_user
  USING (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = subscriptions.tenant_id
        AND sa.scope_ref_id  = subscriptions.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active))
  WITH CHECK (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = subscriptions.tenant_id
        AND sa.scope_ref_id  = subscriptions.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active));

-- ===== §3.8 — module tables: SELECT, INSERT, UPDATE, DELETE for app_user (within RLS); nothing for any other role.
GRANT SELECT, INSERT, UPDATE, DELETE ON clients TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON subscriptions TO app_user;
