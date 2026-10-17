-- A3 (PLATFORM_CORE Amendment 01 §2.6, §3, §4.1, §5.1; OPEN_ITEMS 44, 51, 53) — the e-mail channel and the
-- sessionless address lookup that `POST /api/auth/password/forgot` resolves.
-- Both policies below are the text of Amendment 01 §4.1, verbatim. They were run on a real engine before this
-- migration was written: docs/proposals/a3-forgot-pg-proof (the column grant refuses SELECT *, a WHERE over an
-- ungranted column and every write; the policy is what turns the grant into rows; the platform provider row is
-- readable by app_user with no app.tenant_id set, and NULLS NOT DISTINCT refuses a second one per channel).

-- ===== §3 (the persons row, Amendment 01) — authenticator reads the address, and nothing else.
-- `forgot` starts with no session, and the address lives on persons. The grant is two columns: a statement that
-- reads any other column of the table — SELECT *, or a WHERE over full_name — is refused with 42501, so the name
-- and the phone are out of this role's reach by the grant, not by the policy.
-- Amends v1.16 §4.3-a/2 (the role's reads were a closed list of three) and §4.3-a/3 (a fourth resolution table);
-- §4.3-a/4 is untouched — this is a read, and the role still writes nothing to the identity tables.
GRANT SELECT (id, email) ON persons TO authenticator;

-- The policy is USING (true): the role reads every row of persons, not only the one being resolved, because the
-- subject is not known until the lookup the policy would govern (the accepted risk, OPEN_ITEMS 56 — stated in the
-- amendment's §3 justification). The containment is the role's own: a separate connection and role, no session,
-- a surface confined to the authentication path, and the column grant above.
CREATE POLICY persons_authenticator_read ON persons
  FOR SELECT TO authenticator
  USING (true);   -- the two columns (id, email) are confined by the grant, not by the policy; the surface is structurally confined to the authentication path

-- ===== §2.6 — provider_accounts: the platform's delivery account, with a reserved per-tenant slot.
-- tenant_id IS NULL is the platform's row. The per-tenant override is a reserved slot at this stage: no path
-- writes this table, and no application role holds INSERT, UPDATE or DELETE on it (§3). The rows are seeded by
-- migrator — tests/seed/seed-contract.sql for dev and the tests, docs/operations.md for a real deployment.
-- credential_ref names a key in the Api/Worker settings; the secret itself is never in the database (§2.6).
CREATE TABLE provider_accounts (
  id             app_id  PRIMARY KEY,
  tenant_id      uuid    NULL REFERENCES tenants (id),
  channel        text    NOT NULL,
  provider_code  text    NOT NULL,
  sender_id      text    NOT NULL,
  credential_ref text    NOT NULL,
  is_active      boolean NOT NULL,
  created_at     app_ts  NOT NULL,
  CONSTRAINT provider_accounts_channel_check CHECK (channel IN ('email', 'whatsapp', 'sms', 'telegram')),
  -- Without NULLS NOT DISTINCT, UNIQUE does not prevent two platform rows for one channel: NULL <> NULL (§2.6).
  CONSTRAINT provider_accounts_tenant_channel_key UNIQUE NULLS NOT DISTINCT (tenant_id, channel)
);

ALTER TABLE provider_accounts ENABLE ROW LEVEL SECURITY;
ALTER TABLE provider_accounts FORCE ROW LEVEL SECURITY;

-- ===== §4.1 — the two read policies, both outside the template.
CREATE POLICY provider_accounts_read ON provider_accounts
  FOR SELECT TO app_user
  USING (tenant_id IS NULL
         OR tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));   -- the platform's row, or the current tenant's (§2.6)

-- The sessionless send path (`forgot`, §5.1) belongs to authenticator, so that role reads which account to send
-- with. The platform's row alone: this role has no tenant in its context, and a per-tenant row — if one is ever
-- written — is out of its reach without anyone having to revisit the grant. Permissive, because it is the whole
-- of this role's access to the table and not a narrowing of another policy; and it reads no current_setting, so
-- the NULLIF rule has nothing to apply to. Amends v1.16 §3.4 (the authenticator row), §4.3-a/2 and §4.3-a/3 — §0.
CREATE POLICY provider_accounts_authenticator_read ON provider_accounts
  FOR SELECT TO authenticator
  USING (tenant_id IS NULL);   -- the platform's row alone: this role has no tenant in its context, and it reads no current_setting, so the NULLIF rule has nothing to apply to

-- ===== §3 — the grants of the new row: app_user and authenticator read, nobody writes. Writes are migrator's,
-- seeding. The authenticator grant is whole-table and the narrowing is the policy's, over rows (§3).
GRANT SELECT ON provider_accounts TO app_user;
GRANT SELECT ON provider_accounts TO authenticator;
