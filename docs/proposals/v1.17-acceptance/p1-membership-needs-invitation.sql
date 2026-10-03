-- P1 (condition 2, in the database): a membership comes into being — inserted, or re-activated from 'left' — only
-- for a person with a pending, unexpired invitation in that tenant, the emails compared normalized
-- (lower(btrim(...))). No exception: the bootstrapped owner (4.4) gets an invitation too (P1b), so every membership
-- comes from one. New chains (3.9 says "no new chains" for the provisioner): memberships -> invitations, users,
-- persons. (A first draft excepted "a tenant's first membership" with NOT EXISTS over memberships itself: 42P17,
-- infinite recursion — a policy may not read its own table.)
DROP POLICY memberships_provisioner_insert ON memberships;
CREATE POLICY memberships_provisioner_insert ON memberships
  FOR INSERT TO provisioner
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND EXISTS (
      SELECT 1
      FROM invitations i
      JOIN users u ON u.id = memberships.user_id
      JOIN persons p ON p.id = u.person_id
      WHERE i.tenant_id = memberships.tenant_id
        AND i.status = 'pending'
        AND i.expires_at > now()
        AND lower(btrim(i.email)) = lower(btrim(p.email))));

DROP POLICY memberships_provisioner_rejoin ON memberships;
CREATE POLICY memberships_provisioner_rejoin ON memberships
  FOR UPDATE TO provisioner
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND status = 'left')
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND status = 'active'
    AND EXISTS (
      SELECT 1
      FROM invitations i
      JOIN users u ON u.id = memberships.user_id
      JOIN persons p ON p.id = u.person_id
      WHERE i.tenant_id = memberships.tenant_id
        AND i.status = 'pending'
        AND i.expires_at > now()
        AND lower(btrim(i.email)) = lower(btrim(p.email))));

-- P1b (bootstrap through an invitation): the provisioner may create an invitation only in a tenant that has no
-- membership yet — its founding (4.4) — and only 'pending'. In any tenant with members it cannot, so it cannot mint
-- the invitation P1 asks for. New chain: invitations -> memberships.
GRANT INSERT ON invitations TO provisioner;
CREATE POLICY invitations_provisioner_found ON invitations
  FOR INSERT TO provisioner
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND status = 'pending'
    AND NOT EXISTS (SELECT 1 FROM memberships m WHERE m.tenant_id = invitations.tenant_id));
