-- t1_probe — a test-only tenant table for T1 (project-owner decision, T1 PR).
-- Created and dropped by the test fixture within a single run, as migrator.
-- It never enters src/Migrations or any permanent migration script.
--
-- The policy is the tenant_isolation template of PLATFORM_CORE v1.10 §3.1,
-- verbatim: the same text every T2 table will carry.
CREATE TABLE public.t1_probe (
  id        uuid PRIMARY KEY DEFAULT uuidv7(),
  tenant_id uuid NOT NULL,
  label     text NOT NULL,
  counter   integer NOT NULL DEFAULT 0
);

ALTER TABLE t1_probe ENABLE ROW LEVEL SECURITY;
ALTER TABLE t1_probe FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON t1_probe
  FOR ALL TO app_user
  USING (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid))
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

-- The 3.8 grant for module tables: SELECT, INSERT, UPDATE, DELETE (within RLS).
GRANT SELECT, INSERT, UPDATE, DELETE ON t1_probe TO app_user;
