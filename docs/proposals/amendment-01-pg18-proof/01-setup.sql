\set ON_ERROR_STOP 1
CREATE ROLE app_user; CREATE ROLE authenticator; CREATE ROLE provisioner; CREATE ROLE job_runner;
CREATE DOMAIN app_id AS uuid CHECK (VALUE <> '00000000-0000-0000-0000-000000000000');
CREATE DOMAIN app_ts AS timestamptz CHECK (VALUE >= '2000-01-01');
-- 1.16 shapes, reduced to what the policies read
CREATE TABLE tenants (id app_id PRIMARY KEY, name text NOT NULL, status text NOT NULL CHECK (status IN ('active','suspended')), created_at app_ts NOT NULL);
CREATE TABLE users (id app_id PRIMARY KEY, username text NOT NULL);
CREATE TABLE invitations (id app_id PRIMARY KEY, tenant_id uuid NOT NULL, email text NOT NULL, status text NOT NULL, invited_by uuid NULL, created_at app_ts NOT NULL);
CREATE TABLE scope_assignments (id app_id PRIMARY KEY, tenant_id uuid NOT NULL, membership_id uuid NOT NULL, scope_ref_id uuid NOT NULL, active boolean NOT NULL);
CREATE TABLE subscriptions (id app_id PRIMARY KEY, tenant_id uuid NOT NULL, scope_ref_id uuid NOT NULL, UNIQUE (tenant_id, id));
-- Amendment 01 §2
CREATE TABLE password_reset_tokens (id app_id PRIMARY KEY, user_id uuid NOT NULL REFERENCES users, token_hash text NOT NULL, requested_at app_ts NOT NULL, expires_at app_ts NOT NULL, used_at timestamptz NULL, request_ip text NULL);
CREATE TABLE plans (id app_id PRIMARY KEY, code text NOT NULL, name_ar text NOT NULL, name_en text NOT NULL, is_active boolean NOT NULL, UNIQUE (code));
CREATE TABLE platform_subscriptions (id app_id PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES tenants, plan_code text NOT NULL REFERENCES plans(code), status text NOT NULL CHECK (status IN ('active','suspended')), period_start date NOT NULL, paid_until date NOT NULL, grace_days int NOT NULL, created_at app_ts NOT NULL, updated_at app_ts NOT NULL, UNIQUE (tenant_id));
CREATE TABLE tenant_notification_settings (id app_id PRIMARY KEY, tenant_id uuid NOT NULL REFERENCES tenants, channel text NOT NULL, is_enabled boolean NOT NULL, reminder_days_before int[] NOT NULL, sender_display_name text NULL, created_at app_ts NOT NULL, updated_at app_ts NOT NULL, UNIQUE (tenant_id, channel));
CREATE TABLE provider_accounts (id app_id PRIMARY KEY, tenant_id uuid NULL REFERENCES tenants, channel text NOT NULL, provider_code text NOT NULL, sender_id text NOT NULL, credential_ref text NOT NULL, is_active boolean NOT NULL, created_at app_ts NOT NULL, UNIQUE NULLS NOT DISTINCT (tenant_id, channel));
CREATE TABLE notification_log (id app_id PRIMARY KEY, tenant_id uuid NOT NULL, subscription_id uuid NOT NULL, scope_ref_id uuid NOT NULL, channel text NOT NULL, reminder_day date NOT NULL, provider_message_id text NULL, status text NOT NULL CHECK (status IN ('pending','sent','failed')), attempted_at app_ts NOT NULL, sent_at timestamptz NULL, error text NULL,
  FOREIGN KEY (tenant_id, subscription_id) REFERENCES subscriptions (tenant_id, id), UNIQUE (tenant_id, subscription_id, channel, reminder_day));
DO $$ DECLARE t text; BEGIN FOREACH t IN ARRAY ARRAY['tenants','users','invitations','scope_assignments','subscriptions','password_reset_tokens','plans','platform_subscriptions','tenant_notification_settings','provider_accounts','notification_log'] LOOP
  EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', t); EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', t); END LOOP; END $$;
-- grants (Amendment 01 §3, the rows these tests touch)
GRANT SELECT, INSERT, UPDATE (used_at) ON password_reset_tokens TO authenticator;
GRANT SELECT ON plans TO app_user, provisioner, job_runner;
GRANT SELECT ON platform_subscriptions TO app_user;
GRANT INSERT, SELECT, UPDATE (status, paid_until, period_start, plan_code) ON platform_subscriptions TO provisioner;
GRANT INSERT, SELECT, UPDATE (status) ON tenants TO provisioner;
GRANT SELECT ON tenants TO job_runner;
GRANT INSERT, SELECT, UPDATE (status) ON invitations TO provisioner;
GRANT SELECT ON provider_accounts TO app_user;
GRANT SELECT, INSERT, UPDATE (is_enabled, reminder_days_before, sender_display_name) ON tenant_notification_settings TO app_user;
GRANT INSERT ON tenant_notification_settings TO provisioner;
GRANT SELECT, INSERT, UPDATE (status, provider_message_id, error, sent_at) ON notification_log TO app_user;
GRANT SELECT ON scope_assignments TO app_user;
-- 1.16 policies on the shared tables that the tests rely on
CREATE POLICY tenants_provisioner_insert ON tenants FOR INSERT TO provisioner
  WITH CHECK (id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));
CREATE POLICY tenants_for_jobs ON tenants FOR SELECT TO job_runner USING (status = 'active');
CREATE POLICY scope_assignment_read ON scope_assignments FOR SELECT TO app_user USING (true);
