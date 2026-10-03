-- P2 (condition 2, bound to the token): P1 accepts ANY matching invitation of the tenant, so a person holding their
-- own pending invitation could consume another's token (and its role). The invitation records who accepted it, and
-- may become 'accepted' only from 'pending', only with an acceptor whose normalized email is the invitation's and
-- who holds an active membership in the tenant. Single use (condition 5) is in the USING clause too.
ALTER TABLE invitations ADD COLUMN accepted_by uuid;
ALTER TABLE invitations ADD CONSTRAINT invitations_accepted_by_fkey FOREIGN KEY (accepted_by) REFERENCES users (id);
GRANT UPDATE (accepted_by) ON invitations TO provisioner;

DROP POLICY invitations_provisioner_update ON invitations;
CREATE POLICY invitations_provisioner_update ON invitations
  FOR UPDATE TO provisioner
  USING (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND status = 'pending')
  WITH CHECK (
    tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    AND status = 'accepted'
    AND EXISTS (
      SELECT 1
      FROM memberships m
      JOIN users u ON u.id = m.user_id
      JOIN persons p ON p.id = u.person_id
      WHERE m.tenant_id = invitations.tenant_id
        AND m.user_id = invitations.accepted_by
        AND m.status = 'active'
        AND lower(btrim(p.email)) = lower(btrim(invitations.email))));
