-- M2/a (PLATFORM_CORE Amendment 01 §2.11, §3, §4.1, §5.4; OPEN_ITEMS 43, 86) — the bootstrap invitation: the `owner`
-- invitation `src/Ops create-tenant` writes as provisioner, for a tenant that has no user yet, with no human inviter.
-- The policy below is the text of Amendment 01 §4.1, verbatim. Proven on PostgreSQL 18.6 before this file was
-- written, in both directions (the PR records the six cases).

-- ===== §2.11 — invited_by accepts NULL: "a bootstrap invitation, with no human inviter".
-- The column widens, it does not narrow: every existing row already holds a value, so there is nothing to backfill
-- and no row-count check to make. No FK names the column, so nothing else moves with it.
--
-- Why this opens nothing to app_user: invitations_insert (v1.16 §3.9) requires invited_by = app.user_id. A NULL
-- invited_by makes that comparison NULL — not false, and not true — and a WITH CHECK that is not true refuses the
-- row. So app_user still writes no invitation without an inviter, with every session variable set, and the only
-- writer of a NULL inviter is the provisioner policy below, which requires it.
ALTER TABLE invitations ALTER COLUMN invited_by DROP NOT NULL;

-- ===== §3 — provisioner gains INSERT on invitations (the `owner` invitation in create-tenant, §5.4). It already
-- holds SELECT and UPDATE (status) there, for the acceptance path.
GRANT INSERT ON invitations TO provisioner;

-- ===== §4.1 — the provisioner's insert, verbatim: its own tenant, pending, and no inviter — so the provisioner path
-- forges no inviter, and writes no invitation already accepted.
CREATE POLICY invitations_provisioner_insert ON invitations
  FOR INSERT TO provisioner
  WITH CHECK (tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
              AND status = 'pending'
              AND invited_by IS NULL);   -- a bootstrap invitation: the provisioner path forges no inviter (§2.11)
