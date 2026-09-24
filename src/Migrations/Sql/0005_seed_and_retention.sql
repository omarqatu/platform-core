-- T2, migrator seeding and retention (PROOF_SPEC v1.1 T2, spec items n and b).
-- Keys and timestamps are written explicitly in VALUES (PLATFORM_CORE v1.14 §2).

-- ===== The global catalogs (§5)
INSERT INTO modules (id, code, name_ar, name_en, icon, display_order, is_active)
VALUES (uuidv7(), 'core', 'النواة', 'Core', NULL, 0, true);

-- Spec item n: the scope-management permission (§4.8, 3.5/6-c).
INSERT INTO permissions (id, module_id, code, name_ar, name_en)
SELECT uuidv7(), m.id, 'core.scope.manage', 'إدارة النطاق', 'Manage scope'
FROM modules m WHERE m.code = 'core';

-- The base roles bootstrap copies into every new tenant (§4.4, 5; 1.13).
INSERT INTO role_templates (id, code, name_ar, name_en) VALUES
  (uuidv7(), 'owner',    'مالك',  'Owner'),
  (uuidv7(), 'admin',    'مدير',  'Admin'),
  (uuidv7(), 'operator', 'مشغّل', 'Operator'),
  (uuidv7(), 'viewer',   'مطّلع', 'Viewer');

-- Spec item n: core.scope.manage granted by default to owner and admin.
INSERT INTO role_template_permissions (template_id, permission_id)
SELECT t.id, p.id
FROM role_templates t CROSS JOIN permissions p
WHERE t.code IN ('owner', 'admin') AND p.code = 'core.scope.manage';

-- Every data migration ends with an explicit row-count confirmation (§3.4, condition 2).
DO $$
BEGIN
  IF (SELECT count(*) FROM modules) <> 1
     OR (SELECT count(*) FROM permissions) <> 1
     OR (SELECT count(*) FROM role_templates) <> 4
     OR (SELECT count(*) FROM role_template_permissions) <> 2 THEN
    RAISE EXCEPTION 'seed row counts: modules %, permissions %, role_templates %, role_template_permissions % (expected 1, 1, 4, 2)',
      (SELECT count(*) FROM modules), (SELECT count(*) FROM permissions),
      (SELECT count(*) FROM role_templates), (SELECT count(*) FROM role_template_permissions);
  END IF;
END $$;

-- ===== Spec item b: auth_attempts retention (§4.1, 13/b).
-- A migrator procedure, scheduled outside the application (docs/operations.md).
-- 90 days is the project owner's figure, changeable by the owner's decision only.
CREATE PROCEDURE prune_auth_attempts()
LANGUAGE sql
AS $$
  DELETE FROM auth_attempts WHERE created_at < now() - interval '90 days';
$$;
-- Functions and procedures are executable by PUBLIC by default; this one is migrator's alone.
REVOKE ALL ON PROCEDURE prune_auth_attempts() FROM PUBLIC;
