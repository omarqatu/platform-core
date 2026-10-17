\pset pager off
\set ON_ERROR_STOP 0
SET ROLE authenticator;
SELECT '-- 1. the exact statement BeginResetAsync sends, for a seeded address (expect 1 row)' AS case;
SELECT u.id AS "Value"
  FROM persons p
  JOIN users u ON u.person_id = p.id
 WHERE lower(btrim(p.email)) = 'omar@seed.test'
   AND u.status = 'active';

SELECT '-- 2. the same for an address with no account (expect 0 rows, no error)' AS case;
SELECT u.id AS "Value" FROM persons p JOIN users u ON u.person_id = p.id
 WHERE lower(btrim(p.email)) = 'nobody@seed.test' AND u.status = 'active';

SELECT '-- 3. it uses the normalized unique index, not a scan' AS case;
EXPLAIN (COSTS OFF) SELECT u.id FROM persons p JOIN users u ON u.person_id = p.id
 WHERE lower(btrim(p.email)) = 'omar@seed.test' AND u.status = 'active';

SELECT '-- 4. a tracked EF read of persons would read every column (expect: permission denied)' AS case;
SELECT p.id, p.full_name, p.email, p.phone_e164, p.created_at FROM persons p LIMIT 1;

SELECT '-- 5. reading the outstanding tokens of a user, as the path does (expect 0 rows)' AS case;
SELECT id FROM password_reset_tokens WHERE user_id = (SELECT id FROM users LIMIT 1) AND used_at IS NULL;
RESET ROLE;

-- The supersession statement of BeginResetAsync: idempotent, so two requests racing on one address cannot turn
-- a 204 into a 500 and tell an attacker the address has an account (OPEN_ITEMS 51).
SELECT '-- A. two live tokens for a user, written as authenticator' AS case;
INSERT INTO password_reset_tokens (id, user_id, token_hash, requested_at, expires_at, used_at, request_ip)
SELECT gen_random_uuid(), u.id, 'h-' || g, now(), now() + interval '30 min', NULL, NULL
  FROM users u, generate_series(1,2) g WHERE u.username = 'omar';
SELECT '-- B. the supersession: a raw UPDATE with a WHERE, under UPDATE(used_at) + table SELECT' AS case;
UPDATE password_reset_tokens SET used_at = now()
 WHERE user_id = (SELECT id FROM users WHERE username = 'omar') AND used_at IS NULL;
SELECT '-- C. again: zero rows, and nothing raised — the race, made harmless' AS case;
UPDATE password_reset_tokens SET used_at = now()
 WHERE user_id = (SELECT id FROM users WHERE username = 'omar') AND used_at IS NULL;
SELECT '-- D. any other column is still refused (the column grant holds)' AS case;
UPDATE password_reset_tokens SET token_hash = 'x' WHERE used_at IS NOT NULL;
