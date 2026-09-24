-- Check 2 (PLATFORM_CORE v1.14 §3.6): the manifest, deparse against deparse (1.11), and the
-- NULLIF rule on every policy (1.10). Input: check.manifest (checks/core/manifest.json).
--   Inside the manifest: the table's policies = the manifest's set exactly — by name, command,
--     roles, permissive/restrictive class, USING and WITH CHECK. Missing, extra, drifted = failure.
--   Outside the manifest: permissive = tenant_isolation alone (the template); restrictive = empty,
--     or client_scope alone (the template). Check 8 decides when client_scope is mandatory.
--   Every expected policy is created as a reference under a temporary name on the same table, and
--   pg_get_expr of the actual policy is compared with the reference's. The runner rolls the whole
--   transaction back, so no reference survives. (It takes an exclusive lock on each table: run in
--   CI and on a test environment, never on a live production database.)
CREATE TEMP TABLE check_violations (violation text) ON COMMIT DROP;

DO $check$
DECLARE
  manifest jsonb := current_setting('check.manifest')::jsonb;
  tbl record;
  stmt text;
  expected record;
  actual record;
  reference record;
  names text[];
  in_manifest boolean;
  total int;
  wrapped int;
BEGIN
  -- Every manifest table must exist.
  FOR stmt IN SELECT jsonb_object_keys(manifest -> 'tables') LOOP
    IF to_regclass('public.' || quote_ident(stmt)) IS NULL THEN
      INSERT INTO check_violations VALUES (format('Check 2: manifest table %s does not exist', stmt));
    END IF;
  END LOOP;

  FOR tbl IN
    SELECT c.oid, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p') ORDER BY c.relname
  LOOP
    in_manifest := (manifest -> 'tables') ? tbl.relname;
    names := ARRAY[]::text[];

    FOR stmt IN
      SELECT s FROM jsonb_array_elements_text(manifest -> 'tables' -> tbl.relname) s WHERE in_manifest
      UNION ALL
      SELECT replace(manifest -> 'templates' ->> 'tenant_isolation', '<t>', tbl.relname) WHERE NOT in_manifest
      UNION ALL
      -- Outside the manifest, a client_scope that exists is held to its template (Check 8 requires it).
      SELECT replace(manifest -> 'templates' ->> 'client_scope', '<t>', tbl.relname)
      WHERE NOT in_manifest AND EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = tbl.oid AND p.polname = 'client_scope')
    LOOP
      SELECT substring(stmt FROM '^CREATE POLICY (\S+) ON ') AS name INTO expected;
      names := names || expected.name;

      SELECT * INTO actual FROM pg_policy WHERE polrelid = tbl.oid AND polname = expected.name;
      IF NOT FOUND THEN
        INSERT INTO check_violations VALUES (format('Check 2: %s is missing policy %s%s', tbl.relname, expected.name,
          CASE WHEN in_manifest THEN ' (manifest)' ELSE ' (template — the table is outside the manifest)' END));
        CONTINUE;
      END IF;

      BEGIN
        EXECUTE regexp_replace(stmt, '^CREATE POLICY \S+', 'CREATE POLICY ' || quote_ident('__ref_' || expected.name));
      EXCEPTION WHEN OTHERS THEN
        INSERT INTO check_violations VALUES (format('Check 2: %s — the expected %s cannot be created on this table: %s',
          tbl.relname, expected.name, SQLERRM));
        CONTINUE;
      END;
      SELECT * INTO reference FROM pg_policy WHERE polrelid = tbl.oid AND polname = '__ref_' || expected.name;

      IF actual.polcmd <> reference.polcmd THEN
        INSERT INTO check_violations VALUES (format('Check 2: %s.%s command drifted (%s, expected %s)', tbl.relname, expected.name, actual.polcmd, reference.polcmd));
      END IF;
      IF actual.polpermissive <> reference.polpermissive THEN
        INSERT INTO check_violations VALUES (format('Check 2: %s.%s class drifted (%s, expected %s)', tbl.relname, expected.name,
          CASE WHEN actual.polpermissive THEN 'permissive' ELSE 'restrictive' END,
          CASE WHEN reference.polpermissive THEN 'permissive' ELSE 'restrictive' END));
      END IF;
      IF (SELECT array_agg(x ORDER BY x) FROM unnest(actual.polroles) x) IS DISTINCT FROM (SELECT array_agg(x ORDER BY x) FROM unnest(reference.polroles) x) THEN
        INSERT INTO check_violations VALUES (format('Check 2: %s.%s roles drifted', tbl.relname, expected.name));
      END IF;
      IF pg_get_expr(actual.polqual, tbl.oid) IS DISTINCT FROM pg_get_expr(reference.polqual, tbl.oid) THEN
        INSERT INTO check_violations VALUES (format('Check 2: %s.%s USING drifted from the document', tbl.relname, expected.name));
      END IF;
      IF pg_get_expr(actual.polwithcheck, tbl.oid) IS DISTINCT FROM pg_get_expr(reference.polwithcheck, tbl.oid) THEN
        INSERT INTO check_violations VALUES (format('Check 2: %s.%s WITH CHECK drifted from the document', tbl.relname, expected.name));
      END IF;
    END LOOP;

    -- Anything not expected is extra (references excluded).
    INSERT INTO check_violations
    SELECT format('Check 2: %s has extra %s policy %s%s', tbl.relname,
                  CASE WHEN p.polpermissive THEN 'permissive' ELSE 'restrictive' END, p.polname,
                  CASE WHEN in_manifest THEN ' (not in the manifest)' ELSE ' (outside the manifest only the templates are allowed)' END)
    FROM pg_policy p
    WHERE p.polrelid = tbl.oid AND p.polname NOT LIKE '\_\_ref\_%' AND p.polname <> ALL (names);
  END LOOP;

  -- The NULLIF rule, on the deparsed form of every policy (1.10, 1.11): every current_setting(
  -- sits right after NULLIF(.
  FOR actual IN
    SELECT p.polname, c.relname, coalesce(pg_get_expr(p.polqual, p.polrelid), '') || ' ' || coalesce(pg_get_expr(p.polwithcheck, p.polrelid), '') AS expr
    FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = 'public' AND p.polname NOT LIKE '\_\_ref\_%'
  LOOP
    total := regexp_count(actual.expr, 'current_setting\(');
    wrapped := regexp_count(actual.expr, 'NULLIF\(current_setting\(');
    IF total <> wrapped THEN
      INSERT INTO check_violations VALUES (format('Check 2: %s.%s reads a context variable without NULLIF (%s of %s wrapped)',
        actual.relname, actual.polname, wrapped, total));
    END IF;
  END LOOP;
END
$check$;

SELECT violation FROM check_violations ORDER BY violation;
