-- Check 3 (PLATFORM_CORE v1.14 §3.6): the required core policies exist as declared in the
-- manifest. Part of executing Check 2, called out separately because its absence means either
-- PII leakage or blocked login. Existence only; Check 2 compares their text.
SELECT format('Check 3: required policy %s on %s does not exist', m.policy, m.tbl) AS violation
FROM (
  SELECT t.key AS tbl, substring(s FROM '^CREATE POLICY (\S+) ON ') AS policy
  FROM jsonb_each(current_setting('check.manifest')::jsonb -> 'tables') t,
       jsonb_array_elements_text(t.value) s
) m
WHERE NOT EXISTS (
  SELECT 1 FROM pg_policy p
  WHERE p.polrelid = to_regclass('public.' || quote_ident(m.tbl)) AND p.polname = m.policy);
