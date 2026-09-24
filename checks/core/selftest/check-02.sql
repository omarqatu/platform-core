-- Plant for Check 2: tenant_isolation on roles drifted back to the v1.9 text (no NULLIF),
-- and an extra policy on a manifest table.
ALTER POLICY tenant_isolation ON roles
  USING (tenant_id = (SELECT current_setting('app.tenant_id', true)::uuid));
CREATE POLICY selftest_extra ON memberships FOR SELECT TO app_user USING (false);
