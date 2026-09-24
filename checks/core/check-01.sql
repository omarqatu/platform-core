-- Check 1 (PLATFORM_CORE v1.14 §3.6): every table with tenant_id has full RLS (ENABLE + FORCE).
-- The document's query, verbatim, as the inner select.
SELECT 'Check 1: ' || q.relname || ' carries tenant_id without RLS ENABLE + FORCE' AS violation
FROM (
SELECT c.relname FROM pg_class c
JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'tenant_id'
WHERE c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
  AND (c.relrowsecurity = false OR c.relforcerowsecurity = false)
) q;
