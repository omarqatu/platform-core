-- A1 (PLATFORM_CORE Amendment 01 §2.1, §2.2, §3, §4.1; OPEN_ITEMS 47) — password recovery and change:
-- the session security stamp on users, and the recovery-token table with its three layers.
-- The three policies below are the text of Amendment 01 §4.1, verbatim; they are proven on PostgreSQL 18 in
-- docs/proposals/amendment-01-pg18-proof (consume → 1 row, consume again → 0, un-consume → 0).

-- ===== §2.1 — users.security_stamp.
-- The application generates it (Guid.CreateVersion7) when a user is created and changes it on every password
-- change; it enters the session cookie and UnitOfWorkMiddleware compares it on every request. NOT NULL on a table
-- that has rows → a backfill for every row inside this migration, then a row-count check (v1.16 §3.4, condition 2).
-- No DEFAULT remains on the column: the application writes it on every insert (§3.5, Test 28a).
ALTER TABLE users ADD COLUMN security_stamp app_id;
UPDATE users SET security_stamp = uuidv7() WHERE security_stamp IS NULL;
ALTER TABLE users ALTER COLUMN security_stamp SET NOT NULL;

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM users WHERE security_stamp IS NULL)
     OR (SELECT count(DISTINCT security_stamp) FROM users) <> (SELECT count(*) FROM users) THEN
    RAISE EXCEPTION 'security_stamp: every user row must carry its own stamp (% rows, % distinct stamps)',
      (SELECT count(*) FROM users), (SELECT count(DISTINCT security_stamp) FROM users);
  END IF;
END $$;

-- §3 (the users row, Amendment 01): app_user's column UPDATE list gains security_stamp. The write goes through the
-- existing user_self_update policy — a user writes their own row — so the policy layer gains nothing here.
GRANT UPDATE (security_stamp) ON users TO app_user;

-- ===== §2.2 — password_reset_tokens: a global table beside auth_attempts.
-- No tenant_id, so §3.2 (an index starts with tenant_id) and §3.3 (the composite FK) do not apply; the FK to users
-- is the plain one the other identity tables carry. Only the token's hash is stored (SHA-256 of a 32-byte token).
-- The unique index on token_hash is this PR's, not the document's: it is the lookup path, and a second row carrying
-- the same hash would be a defect.
CREATE TABLE password_reset_tokens (
  id           app_id      PRIMARY KEY,
  user_id      uuid        NOT NULL REFERENCES users (id),
  token_hash   text        NOT NULL,
  requested_at app_ts      NOT NULL,
  expires_at   app_ts      NOT NULL,
  used_at      timestamptz NULL,
  request_ip   text        NULL
);

CREATE UNIQUE INDEX password_reset_tokens_token_hash_key ON password_reset_tokens (token_hash);
CREATE INDEX ix_password_reset_tokens_user_id ON password_reset_tokens (user_id);

ALTER TABLE password_reset_tokens ENABLE ROW LEVEL SECURITY;
ALTER TABLE password_reset_tokens FORCE ROW LEVEL SECURITY;

-- ===== §4.1 — authenticator, the recovery tokens. The three policies, verbatim.
CREATE POLICY password_reset_tokens_authenticator_select ON password_reset_tokens
  FOR SELECT TO authenticator
  USING (true);   -- the role sees every token; the match is by the hash in the query

CREATE POLICY password_reset_tokens_authenticator_insert ON password_reset_tokens
  FOR INSERT TO authenticator
  WITH CHECK (true);

CREATE POLICY password_reset_tokens_authenticator_use ON password_reset_tokens
  FOR UPDATE TO authenticator
  USING (used_at IS NULL)
  WITH CHECK (used_at IS NOT NULL);   -- the only write is consumption

-- ===== §3 — the grants of the new row: authenticator alone, and UPDATE on used_at alone.
GRANT SELECT, INSERT ON password_reset_tokens TO authenticator;
GRANT UPDATE (used_at) ON password_reset_tokens TO authenticator;

-- ===== §2.2 — retention: the same policy as auth_attempts (v1.16 §13/b), an operational cleanup as migrator.
-- Scheduled outside the application (docs/operations.md). 90 days, as for auth_attempts; the project owner's
-- figure, changeable by the owner's decision only. No application role holds DELETE on the table.
CREATE PROCEDURE prune_password_reset_tokens()
LANGUAGE sql
AS $$
  DELETE FROM password_reset_tokens WHERE requested_at < now() - interval '90 days';
$$;
-- Functions and procedures are executable by PUBLIC by default; this one is migrator's alone.
REVOKE ALL ON PROCEDURE prune_password_reset_tokens() FROM PUBLIC;
