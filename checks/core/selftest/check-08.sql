-- Plant for Check 8: a scoped table with no client_scope, and client_scope on scope_assignments
-- itself (the wrong "fix" of 1.14).
CREATE TABLE selftest_c8 (id app_id PRIMARY KEY, tenant_id uuid NOT NULL, scope_ref_id uuid NOT NULL);
CREATE POLICY client_scope ON scope_assignments
  AS RESTRICTIVE FOR ALL TO app_user
  USING (
    COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
    OR EXISTS (
      SELECT 1 FROM scope_assignments sa
      WHERE sa.tenant_id     = scope_assignments.tenant_id
        AND sa.scope_ref_id  = scope_assignments.scope_ref_id
        AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
        AND sa.active));
