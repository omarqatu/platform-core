\set ON_ERROR_STOP 0
\set QUIET 1
\pset pager off

-- ============ 1. The grant alone, with no policy: FORCE RLS gives zero rows, silently. ============
SET ROLE authenticator;
SELECT '1a. lookup with the grant but no policy (expect 0 rows)' AS case;
SELECT id, email FROM persons WHERE lower(btrim(email)) = 'layla@example.com';
RESET ROLE;

CREATE POLICY persons_authenticator_read ON persons
  FOR SELECT TO authenticator
  USING (true);

-- ============ 2. With the policy: the lookup works, and only through the two granted columns. ============
SET ROLE authenticator;
SELECT '2a. the lookup, as forgot issues it (expect layla)' AS case;
SELECT id, email FROM persons WHERE lower(btrim(email)) = 'layla@example.com';

SELECT '2b. the normalized match finds a stored address with case and a trailing space (expect khaled)' AS case;
SELECT id, email FROM persons WHERE lower(btrim(email)) = 'khaled@example.com';

SELECT '2c. SELECT * (expect: permission denied — the column grant, not the policy)' AS case;
SELECT * FROM persons;

SELECT '2d. a column outside the grant (expect: permission denied)' AS case;
SELECT id, full_name FROM persons;

SELECT '2e. a WHERE over an ungranted column (expect: permission denied — a WHERE reads)' AS case;
SELECT id FROM persons WHERE full_name = 'Layla Haddad';

SELECT '2f. the whole path: address -> person -> user, both tables in one statement (expect layla active)' AS case;
SELECT u.id, u.status FROM persons p JOIN users u ON u.person_id = p.id
 WHERE lower(btrim(p.email)) = 'layla@example.com';

SELECT '2g. an address with no account (expect 0 rows, no error)' AS case;
SELECT u.id FROM persons p JOIN users u ON u.person_id = p.id
 WHERE lower(btrim(p.email)) = 'nobody@example.com';

SELECT '2h. the token insert this role makes after the lookup (expect INSERT 0 1)' AS case;
INSERT INTO password_reset_tokens VALUES
  ('00000000-0000-0000-0000-0000000000d1', '00000000-0000-0000-0000-0000000000b1', 'hash-1', now(), now() + interval '30 min', NULL, '203.0.113.9');

SELECT '2i. the row count the whole of persons gives this role (the accepted risk, OPEN_ITEMS 56)' AS case;
SELECT count(*) AS every_person_is_readable FROM persons;

SELECT '2j. writing persons (expect: permission denied — SELECT alone is granted)' AS case;
UPDATE persons SET email = 'x@example.com' WHERE id = '00000000-0000-0000-0000-00000000a001';
RESET ROLE;

-- ============ 3. provider_accounts: the platform row, read by app_user with no tenant set. ============
SET ROLE app_user;
SELECT '3a. app_user with no app.tenant_id, before the policy exists (expect 0 rows)' AS case;
SELECT channel, credential_ref FROM provider_accounts WHERE tenant_id IS NULL;
RESET ROLE;

CREATE POLICY provider_accounts_read ON provider_accounts
  FOR SELECT TO app_user
  USING (tenant_id IS NULL
         OR tenant_id = (SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid));

SET ROLE app_user;
SELECT '3b. the same read with the policy and still no tenant (expect the platform row)' AS case;
SELECT channel, provider_code, credential_ref FROM provider_accounts WHERE tenant_id IS NULL;

SELECT '3c. with a tenant set, the platform row is still reachable (expect 1 row)' AS case;
BEGIN;
SET LOCAL app.tenant_id = '00000000-0000-0000-0000-0000000000e1';
SELECT count(*) AS visible FROM provider_accounts;
COMMIT;

SELECT '3d. app_user cannot write it (expect: permission denied)' AS case;
UPDATE provider_accounts SET credential_ref = 'stolen' WHERE tenant_id IS NULL;
RESET ROLE;

SELECT '3e. a second platform row for the same channel (expect: unique violation — NULLS NOT DISTINCT)' AS case;
INSERT INTO provider_accounts VALUES
  ('00000000-0000-0000-0000-0000000000c2', NULL, 'email', 'smtp', 'other@platform.test', 'Other', true, now());

-- ============ 4. provider_accounts as authenticator: the platform's row and nothing else. ============
GRANT SELECT ON provider_accounts TO authenticator;

SET ROLE authenticator;
SELECT '4a. the grant with no policy for this role (expect 0 rows)' AS case;
SELECT channel, credential_ref FROM provider_accounts;
RESET ROLE;

CREATE POLICY provider_accounts_authenticator_read ON provider_accounts
  FOR SELECT TO authenticator
  USING (tenant_id IS NULL);

-- A tenant's row, of the kind no path writes today, to show the policy and not the data is what hides it.
INSERT INTO provider_accounts VALUES
  ('00000000-0000-0000-0000-0000000000c3', '00000000-0000-0000-0000-0000000000e1', 'email', 'smtp',
   'tenant@platform.test', 'tenant-email', true, now());

SET ROLE authenticator;
SELECT '4b. with the policy: the platform row, as the sender reads it (expect 1 row, the platform one)' AS case;
SELECT tenant_id, channel, provider_code, sender_id, credential_ref FROM provider_accounts;

SELECT '4c. the tenant row exists and is still invisible to this role (expect 0 rows)' AS case;
SELECT id FROM provider_accounts WHERE tenant_id IS NOT NULL;

SELECT '4d. setting a tenant in the context changes nothing: the policy never reads one (expect 1 row)' AS case;
BEGIN;
SET LOCAL app.tenant_id = '00000000-0000-0000-0000-0000000000e1';
SELECT count(*) AS visible FROM provider_accounts;
COMMIT;

SELECT '4e. this role cannot write it either (expect: permission denied)' AS case;
UPDATE provider_accounts SET credential_ref = 'stolen' WHERE tenant_id IS NULL;
RESET ROLE;
