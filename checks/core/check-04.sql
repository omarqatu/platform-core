-- Check 4 (PLATFORM_CORE v1.14 §3.6): no policy applies to PUBLIC (a policy without TO).
-- The document's query, verbatim, as the inner select.
SELECT 'Check 4: policy ' || q.polname || ' on ' || q.relname || ' applies to PUBLIC' AS violation
FROM (
SELECT c.relname, p.polname FROM pg_policy p
JOIN pg_class c ON c.oid = p.polrelid WHERE p.polroles = '{0}'
) q;
