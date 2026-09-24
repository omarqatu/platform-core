-- Plant for Check 10: a policy whose expression reads a table no chain declares (invitations → roles).
CREATE POLICY selftest_chain ON invitations FOR SELECT TO app_user
  USING (EXISTS (SELECT 1 FROM roles r WHERE r.id = invitations.role_id AND r.is_active));
