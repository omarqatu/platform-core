-- The seed contract (PROOF_SPEC v1.2 §7), seeded by migrator before every Conformance run.
-- T2 seeds the core part: tenants, persons, memberships with their roles, scopes and assignments.
-- T5 adds the module rows: the clients A–D, X, Y and their subscriptions — their ids were fixed from T2
-- because the assignments point at them (scope_ref_id is an opaque id in the core, §3.3, 9).
-- Conformance finds everything by name (tenant name, username), never by these ids.
-- Keys and timestamps are written explicitly (PLATFORM_CORE v1.14 §2). Run on a clean database.
--
-- Passwords (T3): TEST VALUES ONLY, published here so any implementation under test can log in. Each
-- seed user's password is <username>-seed-password (omar-seed-password, sara-seed-password, ...).
-- The hashes below are this implementation's (PBKDF2-HMAC-SHA512, 210,000 iterations — Core.Identity);
-- another implementation stores the same passwords in its own format. Never a real credential.
\set ON_ERROR_STOP on

DO $seed$
DECLARE
  client_a CONSTANT uuid := '0199c000-0000-7000-8000-0000000000a1';
  client_b CONSTANT uuid := '0199c000-0000-7000-8000-0000000000a2';
  client_c CONSTANT uuid := '0199c000-0000-7000-8000-0000000000a3';
  client_d CONSTANT uuid := '0199c000-0000-7000-8000-0000000000a4';
  client_x CONSTANT uuid := '0199c000-0000-7000-8000-0000000000b1';
  client_y CONSTANT uuid := '0199c000-0000-7000-8000-0000000000b2';
  t_alamin uuid := uuidv7();
  t_maan uuid := uuidv7();
  t_suspended uuid := uuidv7();
  scope_manage uuid;
  person uuid;
  usr uuid;
  m uuid;
  rec record;
  pw_hash text;
BEGIN
  IF EXISTS (SELECT 1 FROM tenants) THEN
    RAISE EXCEPTION 'the seed contract runs on a clean database: tenants already exist';
  END IF;

  INSERT INTO tenants (id, name, status, created_at) VALUES
    (t_alamin, 'Al-Amin', 'active', now()),
    (t_maan, 'Maan', 'active', now()),
    (t_suspended, 'Suspended', 'suspended', now());

  -- Each tenant's base roles, copied from the templates the way bootstrap does (§4.4, 5).
  INSERT INTO roles (id, tenant_id, code, name_ar, name_en, is_system, is_active)
  SELECT uuidv7(), t.id, rt.code, rt.name_ar, rt.name_en, true, true
  FROM tenants t CROSS JOIN role_templates rt;
  INSERT INTO role_permissions (id, tenant_id, role_id, permission_id)
  SELECT uuidv7(), r.tenant_id, r.id, rtp.permission_id
  FROM roles r JOIN role_templates rt ON rt.code = r.code JOIN role_template_permissions rtp ON rtp.template_id = rt.id;

  -- Rami holds core.scope.manage through a custom role beside operator (§7: "operator / assigned + core.scope.manage").
  SELECT id INTO scope_manage FROM permissions WHERE code = 'core.scope.manage';
  INSERT INTO roles (id, tenant_id, code, name_ar, name_en, is_system, is_active)
    VALUES (uuidv7(), t_alamin, 'scope-manager', 'مدير النطاق', 'Scope manager', false, true);
  INSERT INTO role_permissions (id, tenant_id, role_id, permission_id)
    SELECT uuidv7(), t_alamin, r.id, scope_manage FROM roles r WHERE r.tenant_id = t_alamin AND r.code = 'scope-manager';

  -- Persons and memberships: (username, full name, tenant, roles, scope mode, assigned clients).
  FOR rec IN
    SELECT * FROM (VALUES
      ('omar',   'Omar',   t_alamin, ARRAY['owner'],                     'all',      ARRAY[]::uuid[]),
      ('omar',   'Omar',   t_maan,   ARRAY['operator'],                  'assigned', ARRAY[]::uuid[]),
      ('sara',   'Sara',   t_alamin, ARRAY['admin'],                     'all',      ARRAY[]::uuid[]),
      ('khaled', 'Khaled', t_alamin, ARRAY['operator'],                  'assigned', ARRAY[client_a, client_b]),
      ('layla',  'Layla',  t_alamin, ARRAY['viewer'],                    'all',      ARRAY[]::uuid[]),
      ('rami',   'Rami',   t_alamin, ARRAY['operator', 'scope-manager'], 'assigned', ARRAY[client_a]),
      ('nour',   'Nour',   t_maan,   ARRAY['owner'],                     'all',      ARRAY[]::uuid[])
    ) AS s (username, full_name, tenant_id, role_codes, scope_mode, clients)
  LOOP
    SELECT u.id INTO usr FROM users u WHERE u.username = rec.username;
    IF usr IS NULL THEN
      person := uuidv7();
      usr := uuidv7();
      INSERT INTO persons (id, full_name, email, phone_e164, created_at)
        VALUES (person, rec.full_name, rec.username || '@seed.test', NULL, now());
      INSERT INTO users (id, person_id, user_type, username, status, language, theme, last_login_at)
        VALUES (usr, person, 'employee', rec.username, 'active', 'ar', NULL, NULL);
      -- <username>-seed-password, hashed (test values only — the header).
      pw_hash := CASE rec.username
        WHEN 'omar'   THEN 'pbkdf2-sha512$210000$To2DKiQdIccrJ9aE107OVA==$ZNnPHK1u+DqrSlpWyQmJWQ89yNLi5fCoJwrUJsPGdR/MHLF/dJ6DcB0cJqwD0WGvMlTfMVjOvqgfuhbl6D+AvA=='
        WHEN 'sara'   THEN 'pbkdf2-sha512$210000$O/MqlIa02J6twbQagaKHRg==$lcKAp/lvKT1zT72fZ/abuNpC7Qs9d3HeG+ZV4M4CZDSMfmUsrnLBEA4DJzD+EL1BhpUsv93gOHUZTxPskRCjvA=='
        WHEN 'khaled' THEN 'pbkdf2-sha512$210000$PFHH0vZSoMvcu7TleHkKwQ==$O7tkRkJVM313PvRr8xSptHYQA1Ax0y1E+a1/4YRtjYJcCzgETPN+MUZ6XzkEDYt8O0F9kCXnBixOjpSQh9OM8Q=='
        WHEN 'layla'  THEN 'pbkdf2-sha512$210000$6KEStUC5oNOe5AMxrkV9rQ==$uyRnxsEV7Era+7QoriRzc9DtuY4v9dX9CQCeMEYM/iruZb/GceHDuZ7bPFOhFYyp5OsSFiQjvN8ff5HskSQmhQ=='
        WHEN 'rami'   THEN 'pbkdf2-sha512$210000$FqL/WuP4hvJKdjWQqEOtGg==$bNzKTePWNbShj9ey3TxOj5dJ5GC7qJzjmkBxLlccTS8Mq1bexJXxFfIgRmZBinMhQKxtBVCqmvyPKeLF+GrITQ=='
        WHEN 'nour'   THEN 'pbkdf2-sha512$210000$+5HiaQTG+s03xYt/BWXW+Q==$LRpZqPoSzsrpIfFCZdIV4pss6yQll+n9sBTQHGn5YCKpBJNnWtv4/3z4vvBaAZFqu5a8H8C5AtemKim0ch2JJg=='
      END;
      INSERT INTO user_password_credentials (user_id, password_hash, updated_at)
        VALUES (usr, pw_hash, now());
    END IF;

    m := uuidv7();
    INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES (m, rec.tenant_id, usr, 'active', now());
    INSERT INTO membership_roles (id, tenant_id, membership_id, role_id)
      SELECT uuidv7(), rec.tenant_id, m, r.id FROM roles r WHERE r.tenant_id = rec.tenant_id AND r.code = ANY (rec.role_codes);
    INSERT INTO membership_auth (id, tenant_id, membership_id, provider, provider_config)
      VALUES (uuidv7(), rec.tenant_id, m, 'password', NULL);
    -- (v1.15) No attribution columns on the scope surface: the audit log attributes (§4.1, 7).
    INSERT INTO membership_scope (id, tenant_id, membership_id, scope_mode)
      VALUES (uuidv7(), rec.tenant_id, m, rec.scope_mode);
    INSERT INTO scope_assignments (id, tenant_id, membership_id, scope_ref_id, assignment_role, active, reason)
      SELECT uuidv7(), rec.tenant_id, m, c, 'contributor', true, 'seed contract'
      FROM unnest(rec.clients) c;
  END LOOP;

  -- (T5) The module rows: Al-Amin's clients A–D, 5 subscriptions each; Maan's X, Y, 3 each (§7). A client's
  -- scope_ref_id is its own id (the synonym, 0008_subscriptions.sql).
  INSERT INTO clients (id, tenant_id, scope_ref_id, name, created_at) VALUES
    (client_a, t_alamin, client_a, 'A', now()), (client_b, t_alamin, client_b, 'B', now()),
    (client_c, t_alamin, client_c, 'C', now()), (client_d, t_alamin, client_d, 'D', now()),
    (client_x, t_maan, client_x, 'X', now()), (client_y, t_maan, client_y, 'Y', now());
  INSERT INTO subscriptions (id, tenant_id, scope_ref_id, service_name, ends_on, created_at)
    SELECT uuidv7(), c.tenant_id, c.id, c.name || ' service ' || n, DATE '2027-01-01' + n, now()
    FROM clients c CROSS JOIN LATERAL generate_series(1, CASE WHEN c.tenant_id = t_maan THEN 3 ELSE 5 END) n;

  -- Row-count confirmation (§3.4, condition 2).
  IF (SELECT count(*) FROM tenants) <> 3
     OR (SELECT count(*) FROM persons) <> 6
     OR (SELECT count(*) FROM memberships) <> 7
     OR (SELECT count(*) FROM roles) <> 13
     OR (SELECT count(*) FROM membership_roles) <> 8
     OR (SELECT count(*) FROM membership_scope) <> 7
     OR (SELECT count(*) FROM scope_assignments) <> 3
     OR (SELECT count(*) FROM clients) <> 6
     OR (SELECT count(*) FROM subscriptions) <> 26 THEN
    RAISE EXCEPTION 'seed contract row counts are wrong: tenants %, persons %, memberships %, roles %, membership_roles %, membership_scope %, scope_assignments %, clients %, subscriptions %',
      (SELECT count(*) FROM tenants), (SELECT count(*) FROM persons), (SELECT count(*) FROM memberships),
      (SELECT count(*) FROM roles), (SELECT count(*) FROM membership_roles), (SELECT count(*) FROM membership_scope),
      (SELECT count(*) FROM scope_assignments), (SELECT count(*) FROM clients), (SELECT count(*) FROM subscriptions);
  END IF;
END
$seed$;

SELECT 'seed contract: ' || (SELECT count(*) FROM tenants) || ' tenants, ' || (SELECT count(*) FROM persons) || ' persons, '
       || (SELECT count(*) FROM memberships) || ' memberships, ' || (SELECT count(*) FROM roles) || ' roles, '
       || (SELECT count(*) FROM scope_assignments) || ' assignments, ' || (SELECT count(*) FROM clients) || ' clients, '
       || (SELECT count(*) FROM subscriptions) || ' subscriptions' AS seeded;
