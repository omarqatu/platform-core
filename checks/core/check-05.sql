-- Check 5 (PLATFORM_CORE v1.14 §3.6): every table in public has RLS enabled and forced,
-- except those in the declared exemption list — a file in the repository
-- (checks/core/rls-exemptions.json, empty today), passed in as check.exemptions.
-- The document's query, with its $1 bound to that file.
SELECT 'Check 5: ' || q.relname || ' in public without RLS ENABLE + FORCE, and not exempt' AS violation
FROM (
SELECT c.relname FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind = 'r' AND n.nspname = 'public'
  AND (c.relrowsecurity = false OR c.relforcerowsecurity = false)
  AND c.relname <> ALL (ARRAY(SELECT jsonb_array_elements_text(current_setting('check.exemptions')::jsonb -> 'tables')))
) q;
