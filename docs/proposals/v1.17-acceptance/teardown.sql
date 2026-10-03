-- Removes everything the fixtures and the races committed (as migrator), so the next direction starts clean and
-- P3's index can be built (and alternative B's audit entries): the probe's tenants (P, and the bootstrap probes' B*), and every person on probe.test.
BEGIN;
DELETE FROM audit_log         WHERE tenant_id IN (SELECT id FROM tenants WHERE name LIKE 'Probe%');
DELETE FROM invitations       WHERE tenant_id IN (SELECT id FROM tenants WHERE name LIKE 'Probe%');
DELETE FROM membership_auth   WHERE tenant_id IN (SELECT id FROM tenants WHERE name LIKE 'Probe%');
DELETE FROM membership_roles  WHERE tenant_id IN (SELECT id FROM tenants WHERE name LIKE 'Probe%');
DELETE FROM scope_assignments WHERE tenant_id IN (SELECT id FROM tenants WHERE name LIKE 'Probe%');
DELETE FROM membership_scope  WHERE tenant_id IN (SELECT id FROM tenants WHERE name LIKE 'Probe%');
DELETE FROM memberships       WHERE tenant_id IN (SELECT id FROM tenants WHERE name LIKE 'Probe%');
DELETE FROM roles             WHERE tenant_id IN (SELECT id FROM tenants WHERE name LIKE 'Probe%');
DELETE FROM tenants           WHERE name LIKE 'Probe%';
DELETE FROM users             WHERE person_id IN (SELECT id FROM persons WHERE email ILIKE '%@probe.test%');
DELETE FROM persons           WHERE email ILIKE '%@probe.test%';
COMMIT;
