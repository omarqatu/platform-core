-- Check 6 (PLATFORM_CORE v1.14 §3.6): grants against the §3.8 matrix, widened (1.8) to every
-- privilege surface. Input: check.grants (checks/core/grants.json). Compared in both directions,
-- for the application roles and PUBLIC (migrator is the owner and is not compared).
WITH g AS (SELECT current_setting('check.grants')::jsonb AS j),
grantees AS (
  SELECT r.oid, r.rolname FROM pg_roles r, g WHERE r.rolname IN (SELECT jsonb_array_elements_text(g.j -> 'roles'))
  UNION ALL SELECT 0, 'PUBLIC'
),
app_roles AS (SELECT oid, rolname FROM grantees WHERE oid <> 0),
system_schema AS (
  SELECT n.oid FROM pg_namespace n
  WHERE n.nspname IN ('pg_catalog', 'information_schema') OR n.nspname LIKE 'pg\_toast%' OR n.nspname LIKE 'pg\_temp\_%'
),

-- Tables (table-level ACLs).
actual_tables AS (
  SELECT c.relname AS tbl, ge.rolname AS grantee, a.privilege_type AS priv
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
  CROSS JOIN LATERAL aclexplode(coalesce(c.relacl, acldefault('r', c.relowner))) a
  JOIN grantees ge ON ge.oid = a.grantee
  WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
),
expected_tables AS (
  SELECT t.key AS tbl, r.key AS grantee, p AS priv
  FROM g, jsonb_each(g.j -> 'tables') t, jsonb_each(t.value) r, jsonb_array_elements_text(r.value) p
),
-- Columns (column-level ACLs: GRANT UPDATE (col) and any column SELECT — 1.11 forbids those on credentials).
actual_columns AS (
  SELECT c.relname AS tbl, att.attname AS col, ge.rolname AS grantee, a.privilege_type AS priv
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
  JOIN pg_attribute att ON att.attrelid = c.oid AND att.attnum > 0 AND NOT att.attisdropped AND att.attacl IS NOT NULL
  CROSS JOIN LATERAL aclexplode(att.attacl) a
  JOIN grantees ge ON ge.oid = a.grantee
  WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
),
expected_columns AS (
  SELECT t.key AS tbl, col AS col, r.key AS grantee, p.key AS priv
  FROM g, jsonb_each(g.j -> 'columns') t, jsonb_each(t.value) r, jsonb_each(r.value) p, jsonb_array_elements_text(p.value) col
),
-- The database: CONNECT and TEMP.
actual_database AS (
  SELECT ge.rolname AS grantee, a.privilege_type AS priv
  FROM pg_database d CROSS JOIN LATERAL aclexplode(coalesce(d.datacl, acldefault('d', d.datdba))) a
  JOIN grantees ge ON ge.oid = a.grantee
  WHERE d.datname = current_database()
),
expected_database AS (
  SELECT p.key AS priv, r AS grantee FROM g, jsonb_each(g.j -> 'database') p, jsonb_array_elements_text(p.value) r
),
-- Schemas: USAGE and CREATE, on every non-system schema (migrations_meta included).
actual_schemas AS (
  SELECT n.nspname AS nsp, ge.rolname AS grantee, a.privilege_type AS priv
  FROM pg_namespace n CROSS JOIN LATERAL aclexplode(coalesce(n.nspacl, acldefault('n', n.nspowner))) a
  JOIN grantees ge ON ge.oid = a.grantee
  WHERE n.oid NOT IN (SELECT oid FROM system_schema)
),
expected_schemas AS (
  SELECT s.key AS nsp, r AS grantee, p.key AS priv
  FROM g, jsonb_each(g.j -> 'schemas') s, jsonb_each(s.value) p, jsonb_array_elements_text(p.value) r
)
SELECT violation FROM (
  SELECT 'Check 6: unexpected ' || priv || ' on ' || tbl || ' for ' || grantee AS violation
  FROM (SELECT * FROM actual_tables EXCEPT SELECT * FROM expected_tables) x
  UNION ALL
  SELECT 'Check 6: missing ' || priv || ' on ' || tbl || ' for ' || grantee
  FROM (SELECT * FROM expected_tables EXCEPT SELECT * FROM actual_tables) x
  UNION ALL
  SELECT 'Check 6: unexpected column ' || priv || ' on ' || tbl || '(' || col || ') for ' || grantee
  FROM (SELECT * FROM actual_columns EXCEPT SELECT * FROM expected_columns) x
  UNION ALL
  SELECT 'Check 6: missing column ' || priv || ' on ' || tbl || '(' || col || ') for ' || grantee
  FROM (SELECT * FROM expected_columns EXCEPT SELECT * FROM actual_columns) x
  UNION ALL
  SELECT 'Check 6: unexpected ' || priv || ' on the database for ' || grantee
  FROM (SELECT grantee, priv FROM actual_database EXCEPT SELECT grantee, priv FROM expected_database) x
  UNION ALL
  SELECT 'Check 6: missing ' || priv || ' on the database for ' || grantee
  FROM (SELECT grantee, priv FROM expected_database EXCEPT SELECT grantee, priv FROM actual_database) x
  UNION ALL
  SELECT 'Check 6: unexpected ' || priv || ' on schema ' || nsp || ' for ' || grantee
  FROM (SELECT * FROM actual_schemas EXCEPT SELECT * FROM expected_schemas) x
  UNION ALL
  SELECT 'Check 6: missing ' || priv || ' on schema ' || nsp || ' for ' || grantee
  FROM (SELECT * FROM expected_schemas EXCEPT SELECT * FROM actual_schemas) x
  UNION ALL
  -- EXECUTE on functions and procedures outside the system schemas (approved list: executable_routines).
  SELECT 'Check 6: ' || ge.rolname || ' can execute ' || n.nspname || '.' || p.proname || '()'
  FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
  CROSS JOIN LATERAL aclexplode(coalesce(p.proacl, acldefault('f', p.proowner))) a
  JOIN grantees ge ON ge.oid = a.grantee
  WHERE n.oid NOT IN (SELECT oid FROM system_schema) AND a.privilege_type = 'EXECUTE'
    AND n.nspname || '.' || p.proname NOT IN (SELECT jsonb_array_elements_text(g.j -> 'executable_routines') FROM g)
  UNION ALL
  -- Sequences: any privilege for an application role or PUBLIC (approved list: sequences).
  SELECT 'Check 6: ' || ge.rolname || ' has ' || a.privilege_type || ' on sequence ' || n.nspname || '.' || c.relname
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
  CROSS JOIN LATERAL aclexplode(coalesce(c.relacl, acldefault('s', c.relowner))) a
  JOIN grantees ge ON ge.oid = a.grantee
  WHERE c.relkind = 'S' AND n.oid NOT IN (SELECT oid FROM system_schema)
    AND n.nspname || '.' || c.relname NOT IN (SELECT jsonb_array_elements_text(g.j -> 'sequences') FROM g)
  UNION ALL
  -- Views and materialized views: a common bypass surface — reads a hidden table as its owner.
  SELECT 'Check 6: view ' || n.nspname || '.' || c.relname || ' is not approved'
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
  WHERE c.relkind IN ('v', 'm') AND n.oid NOT IN (SELECT oid FROM system_schema)
    AND n.nspname || '.' || c.relname NOT IN (SELECT jsonb_array_elements_text(g.j -> 'views') FROM g)
  UNION ALL
  -- DEFAULT PRIVILEGES: silently grant on future objects.
  SELECT 'Check 6: default privileges defined by ' || pg_get_userbyid(d.defaclrole) || ' for object type ' || d.defaclobjtype::text
  FROM pg_default_acl d
  WHERE (SELECT jsonb_array_length(g.j -> 'default_privileges') FROM g) = 0
  UNION ALL
  -- Role memberships: no application role is a member of migrator or provisioner (Test 22).
  SELECT 'Check 6: ' || m.rolname || ' is a member of ' || t.rolname
  FROM app_roles m CROSS JOIN pg_roles t
  WHERE t.rolname IN ('migrator', 'provisioner') AND m.oid <> t.oid AND pg_has_role(m.oid, t.oid, 'MEMBER')
) v
ORDER BY violation;
