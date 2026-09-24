-- T2, layer 3 — grants: the §3.8 matrix of PLATFORM_CORE v1.14, literally, with its
-- column grants. Anything not granted here is unreachable, whatever the policies say.
-- Check 6 compares the catalog against checks/core/grants.json, which states the same.

-- ===== The database and the schema (§3.6, Check 6: "CONNECT and TEMP on the database",
-- "schema USAGE / CREATE"). Nothing through PUBLIC: each role gets exactly what it uses.
DO $$
BEGIN
  EXECUTE format('REVOKE ALL ON DATABASE %I FROM PUBLIC', current_database());
  EXECUTE format('GRANT CONNECT ON DATABASE %I TO app_user, authenticator, job_runner, provisioner', current_database());
END $$;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO app_user, authenticator, job_runner, provisioner;

-- ===== The matrix, row by row (§3.8)
GRANT SELECT ON tenants TO app_user;
GRANT INSERT ON tenants TO provisioner;
GRANT SELECT ON tenants TO job_runner;

GRANT SELECT ON persons TO app_user;
GRANT SELECT, INSERT ON persons TO provisioner;

GRANT SELECT ON users TO app_user;
GRANT UPDATE (last_login_at, language, theme) ON users TO app_user;
GRANT SELECT ON users TO authenticator;
GRANT SELECT, INSERT ON users TO provisioner;

-- UPDATE with no SELECT (1.11, 1.12): exactly (password_hash, updated_at).
GRANT UPDATE (password_hash, updated_at) ON user_password_credentials TO app_user;
GRANT SELECT ON user_password_credentials TO authenticator;
GRANT INSERT ON user_password_credentials TO provisioner;

GRANT SELECT ON memberships TO app_user;
GRANT UPDATE (status) ON memberships TO app_user;
GRANT SELECT, INSERT ON memberships TO provisioner;

GRANT SELECT, INSERT, DELETE ON membership_roles TO app_user;
GRANT INSERT ON membership_roles TO provisioner;

GRANT SELECT ON membership_auth TO app_user;
GRANT SELECT ON membership_auth TO authenticator;
GRANT INSERT ON membership_auth TO provisioner;

GRANT SELECT ON membership_scope TO app_user;
GRANT UPDATE (scope_mode) ON membership_scope TO app_user;
GRANT INSERT ON membership_scope TO provisioner;

-- No DELETE: an assignment is history that gets disabled, never erased.
GRANT SELECT, INSERT ON scope_assignments TO app_user;
GRANT UPDATE (active) ON scope_assignments TO app_user;

GRANT SELECT, INSERT ON invitations TO app_user;
GRANT UPDATE (status) ON invitations TO app_user;
GRANT SELECT ON invitations TO provisioner;
GRANT UPDATE (status) ON invitations TO provisioner;

GRANT SELECT, INSERT ON auth_attempts TO authenticator;

-- No UPDATE/DELETE for any application role (Test 12).
GRANT SELECT, INSERT ON audit_log TO app_user;
GRANT INSERT ON audit_log TO provisioner;

GRANT SELECT, INSERT, UPDATE, DELETE ON roles TO app_user;
GRANT SELECT, INSERT ON roles TO provisioner;

GRANT SELECT, INSERT, DELETE ON role_permissions TO app_user;
GRANT INSERT ON role_permissions TO provisioner;

GRANT SELECT ON tenant_modules TO app_user;
GRANT UPDATE (is_active) ON tenant_modules TO app_user;
GRANT INSERT ON tenant_modules TO provisioner;

-- The global catalogs: read only; writes are migrator's (seeding).
GRANT SELECT ON modules TO app_user, provisioner;
GRANT SELECT ON permissions TO app_user, provisioner;
GRANT SELECT ON role_templates TO provisioner;
GRANT SELECT ON role_template_permissions TO provisioner;
