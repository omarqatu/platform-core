-- Check 8 (PLATFORM_CORE v1.14 §3.6): second-axis coverage.
--   (a) Every table carrying scope_ref_id has a restrictive client_scope, except scope_assignments —
--       the table the template reads (1.14). The document's query, verbatim.
--   (b) The inverse (1.14): client_scope present on scope_assignments = failure (infinite recursion,
--       42P17, on every scoped table). The document's query, verbatim.
--   (c) Every client_scope, TO app_user, matches the second template deparse against deparse on the
--       same table (Check 2's mechanism). Input: check.manifest (its templates).
CREATE TEMP TABLE check_violations (violation text) ON COMMIT DROP;

INSERT INTO check_violations
SELECT 'Check 8: ' || q.relname || ' carries scope_ref_id without a restrictive client_scope'
FROM (
SELECT c.relname FROM pg_class c
JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'scope_ref_id'
WHERE c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
  AND c.relname <> 'scope_assignments'   -- (1.14) the table the template reads
  AND NOT EXISTS (
    SELECT 1 FROM pg_policy p
    WHERE p.polrelid = c.oid AND p.polname = 'client_scope'
      AND p.polpermissive = false)
) q;

INSERT INTO check_violations
SELECT 'Check 8: client_scope is present on scope_assignments — the table the template reads (infinite recursion)'
FROM (
SELECT polname FROM pg_policy
WHERE polrelid = 'scope_assignments'::regclass AND polname = 'client_scope'
) q;

DO $check$
DECLARE
  template text := current_setting('check.manifest')::jsonb -> 'templates' ->> 'client_scope';
  tbl record;
  actual record;
  reference record;
BEGIN
  FOR tbl IN
    SELECT c.oid, c.relname FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
    WHERE p.polname = 'client_scope' AND c.relname <> 'scope_assignments'
  LOOP
    SELECT * INTO actual FROM pg_policy WHERE polrelid = tbl.oid AND polname = 'client_scope';
    BEGIN
      EXECUTE replace(replace(template, 'CREATE POLICY client_scope ON', 'CREATE POLICY __ref_client_scope ON'), '<t>', tbl.relname);
    EXCEPTION WHEN OTHERS THEN
      INSERT INTO check_violations VALUES (format('Check 8: %s — the template cannot be created on this table: %s', tbl.relname, SQLERRM));
      CONTINUE;
    END;
    SELECT * INTO reference FROM pg_policy WHERE polrelid = tbl.oid AND polname = '__ref_client_scope';
    IF actual.polpermissive
       OR actual.polcmd <> reference.polcmd
       OR actual.polroles <> reference.polroles
       OR pg_get_expr(actual.polqual, tbl.oid) IS DISTINCT FROM pg_get_expr(reference.polqual, tbl.oid)
       OR pg_get_expr(actual.polwithcheck, tbl.oid) IS DISTINCT FROM pg_get_expr(reference.polwithcheck, tbl.oid) THEN
      INSERT INTO check_violations VALUES (format('Check 8: %s.client_scope does not match the second template', tbl.relname));
    END IF;
  END LOOP;
END
$check$;

SELECT violation FROM check_violations ORDER BY violation;
