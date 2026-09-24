-- check-schema-allowlist — a local repository check, not one of PLATFORM_CORE's
-- ten (see checks/local/README.md). Returns one row per violation; zero rows = pass.
-- Inputs (psql variables, comma-separated): schemas, exact_schemas, objects.
WITH allowed_schemas AS (
  SELECT unnest(string_to_array(:'schemas', ',')) AS nspname
), exact_schemas AS (
  SELECT unnest(string_to_array(:'exact_schemas', ',')) AS nspname
), allowed_objects AS (
  SELECT unnest(string_to_array(:'objects', ',')) AS name
), user_schemas AS (
  SELECT oid, nspname FROM pg_namespace
  WHERE nspname NOT IN ('pg_catalog', 'information_schema')
    AND nspname NOT LIKE 'pg\_toast%'
    AND nspname NOT LIKE 'pg\_temp\_%'
)
-- A schema that exists but is not allowlisted.
SELECT 'schema not in allowlist: ' || s.nspname
FROM user_schemas s
WHERE s.nspname NOT IN (SELECT nspname FROM allowed_schemas)

UNION ALL
-- An allowlisted schema that does not exist (drift in the other direction).
SELECT 'allowlisted schema missing: ' || a.nspname
FROM allowed_schemas a
WHERE a.nspname NOT IN (SELECT nspname FROM user_schemas)

UNION ALL
-- Any relation in an exact schema that is not allowlisted. Indexes and TOAST
-- tables belong to their table and are not listed separately.
SELECT 'object not in allowlist: ' || n.nspname || '.' || c.relname || ' (relkind ' || c.relkind::text || ')'
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname IN (SELECT nspname FROM exact_schemas)
  AND c.relkind NOT IN ('i', 'I', 't')
  AND n.nspname || '.' || c.relname NOT IN (SELECT name FROM allowed_objects)

UNION ALL
-- Any function or procedure in an exact schema.
SELECT 'object not in allowlist: ' || n.nspname || '.' || p.proname || '() (function)'
FROM pg_proc p
JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname IN (SELECT nspname FROM exact_schemas)
  AND n.nspname || '.' || p.proname NOT IN (SELECT name FROM allowed_objects)

UNION ALL
-- Any standalone type in an exact schema (row types of relations and array
-- types are covered by the relation or base type they belong to).
SELECT 'object not in allowlist: ' || n.nspname || '.' || t.typname || ' (type)'
FROM pg_type t
JOIN pg_namespace n ON n.oid = t.typnamespace
WHERE n.nspname IN (SELECT nspname FROM exact_schemas)
  AND t.typrelid = 0
  AND t.typcategory <> 'A'
  AND n.nspname || '.' || t.typname NOT IN (SELECT name FROM allowed_objects)

UNION ALL
-- An allowlisted object that does not exist.
SELECT 'allowlisted object missing: ' || o.name
FROM allowed_objects o
WHERE NOT EXISTS (
  SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
  WHERE n.nspname || '.' || c.relname = o.name
);
