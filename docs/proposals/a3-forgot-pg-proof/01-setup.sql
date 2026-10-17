-- A3: the sessionless address lookup. Stand-in tables carrying only what the path reads, with Amendment 01's
-- grants and policy, so each statement below is the real engine's answer and not a guess.
CREATE ROLE authenticator LOGIN;
CREATE ROLE app_user LOGIN;

CREATE TABLE persons (
  id uuid PRIMARY KEY, full_name text NOT NULL, email text NOT NULL UNIQUE, phone_e164 text, created_at timestamptz NOT NULL);
CREATE UNIQUE INDEX persons_email_normalized_key ON persons (lower(btrim(email)));

CREATE TABLE users (
  id uuid PRIMARY KEY, person_id uuid NOT NULL REFERENCES persons (id), username text NOT NULL UNIQUE,
  status text NOT NULL, security_stamp uuid NOT NULL);

CREATE TABLE password_reset_tokens (
  id uuid PRIMARY KEY, user_id uuid NOT NULL REFERENCES users (id), token_hash text NOT NULL UNIQUE,
  requested_at timestamptz NOT NULL, expires_at timestamptz NOT NULL, used_at timestamptz, request_ip text);

CREATE TABLE provider_accounts (
  id uuid PRIMARY KEY, tenant_id uuid, channel text NOT NULL, provider_code text NOT NULL, sender_id text NOT NULL,
  credential_ref text NOT NULL, is_active boolean NOT NULL, created_at timestamptz NOT NULL,
  UNIQUE NULLS NOT DISTINCT (tenant_id, channel));

ALTER TABLE persons            ENABLE ROW LEVEL SECURITY; ALTER TABLE persons            FORCE ROW LEVEL SECURITY;
ALTER TABLE users              ENABLE ROW LEVEL SECURITY; ALTER TABLE users              FORCE ROW LEVEL SECURITY;
ALTER TABLE password_reset_tokens ENABLE ROW LEVEL SECURITY; ALTER TABLE password_reset_tokens FORCE ROW LEVEL SECURITY;
ALTER TABLE provider_accounts  ENABLE ROW LEVEL SECURITY; ALTER TABLE provider_accounts  FORCE ROW LEVEL SECURITY;

-- v1.16 §4.3-a: authenticator's existing reads. Amendment 01 §3: the new, column-narrowed read on persons.
GRANT SELECT ON users TO authenticator;
GRANT SELECT (id, email) ON persons TO authenticator;
GRANT SELECT, INSERT ON password_reset_tokens TO authenticator;
GRANT UPDATE (used_at) ON password_reset_tokens TO authenticator;
GRANT SELECT ON provider_accounts TO app_user;

CREATE POLICY users_authenticator_read ON users FOR SELECT TO authenticator USING (true);
CREATE POLICY password_reset_tokens_authenticator_select ON password_reset_tokens FOR SELECT TO authenticator USING (true);
CREATE POLICY password_reset_tokens_authenticator_insert ON password_reset_tokens FOR INSERT TO authenticator WITH CHECK (true);

INSERT INTO persons VALUES
  ('00000000-0000-0000-0000-00000000a001', 'Layla Haddad', 'layla@example.com', '+961', now()),
  ('00000000-0000-0000-0000-00000000a002', 'Khaled Nassar', 'Khaled@Example.com ', NULL, now());
INSERT INTO users VALUES
  ('00000000-0000-0000-0000-0000000000b1', '00000000-0000-0000-0000-00000000a001', 'layla', 'active', gen_random_uuid()),
  ('00000000-0000-0000-0000-0000000000b2', '00000000-0000-0000-0000-00000000a002', 'khaled', 'disabled', gen_random_uuid());
INSERT INTO provider_accounts VALUES
  ('00000000-0000-0000-0000-0000000000c1', NULL, 'email', 'smtp', 'no-reply@platform.test', 'Notifications:Email:Platform', true, now());
