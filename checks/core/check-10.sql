-- Check 10 (PLATFORM_CORE v1.14 §3.6, §4.6): policy chains. Input: check.chains
-- (checks/core/policy-chains.json).
--   (a) Every table a policy expression references — from pg_depend (classid = pg_policy,
--       refclassid = pg_class), excluding the policy's own table (1.11) — is a declared chain.
--   (b) Every declared chain's inner table carries a read policy (permissive, SELECT or ALL) that
--       covers the chain's intended actor — the policy named in the declaration.
--   (c) The declared limit: no view and no function outside the system schemas inside a policy
--       expression, since pg_depend cannot see what they read.
WITH chains AS (SELECT current_setting('check.chains')::jsonb AS j),
declared AS (
  SELECT c ->> 'table' AS tbl, r AS reads
  FROM chains, jsonb_array_elements(chains.j -> 'chains') c, jsonb_array_elements_text(c -> 'reads') r
),
scoped AS (SELECT DISTINCT polrelid FROM pg_policy WHERE polname = 'client_scope'),
edges AS (
  SELECT DISTINCT p.polrelid, own.relname AS tbl, p.polname, ref.relname AS reads, ref.relkind
  FROM pg_depend d
  JOIN pg_policy p ON p.oid = d.objid
  JOIN pg_class own ON own.oid = p.polrelid
  JOIN pg_class ref ON ref.oid = d.refobjid
  WHERE d.classid = 'pg_policy'::regclass AND d.refclassid = 'pg_class'::regclass
    AND d.refobjid <> p.polrelid
),
coverage AS (
  SELECT cov.key AS inner_table, pol AS policy, c ->> 'actor' AS actor, coalesce(c ->> 'table', 'resolution step ' || (c ->> 'step')) AS chain
  FROM chains, jsonb_array_elements((chains.j -> 'chains') || (chains.j -> 'resolution')) c,
       jsonb_each(c -> 'covered_by') cov, jsonb_array_elements_text(cov.value) pol
)
SELECT violation FROM (
  SELECT format('Check 10: undeclared chain %s → %s (policy %s)', e.tbl, e.reads, e.polname) AS violation
  FROM edges e
  WHERE NOT EXISTS (
    SELECT 1 FROM declared d
    WHERE d.reads = e.reads
      AND (d.tbl = e.tbl OR (d.tbl = '<scoped table>' AND e.polrelid IN (SELECT polrelid FROM scoped))))
  UNION ALL
  SELECT format('Check 10: chain %s — %s has no read policy %s covering %s', c.chain, c.inner_table, c.policy, c.actor)
  FROM coverage c
  WHERE NOT EXISTS (
    SELECT 1 FROM pg_policy p
    WHERE p.polrelid = to_regclass('public.' || quote_ident(c.inner_table))
      AND p.polname = c.policy AND p.polpermissive AND p.polcmd IN ('r', '*')
      AND (SELECT oid FROM pg_roles WHERE rolname = c.actor) = ANY (p.polroles))
  UNION ALL
  SELECT format('Check 10: policy %s on %s reads view %s — pg_depend cannot see through it', e.polname, e.tbl, e.reads)
  FROM edges e WHERE e.relkind IN ('v', 'm')
  UNION ALL
  SELECT format('Check 10: policy %s on %s calls %s.%s() — only system functions may appear in a policy', p.polname, own.relname, n.nspname, f.proname)
  FROM pg_depend d
  JOIN pg_policy p ON p.oid = d.objid
  JOIN pg_class own ON own.oid = p.polrelid
  JOIN pg_proc f ON f.oid = d.refobjid
  JOIN pg_namespace n ON n.oid = f.pronamespace
  WHERE d.classid = 'pg_policy'::regclass AND d.refclassid = 'pg_proc'::regclass
    AND n.nspname NOT IN ('pg_catalog', 'information_schema')
) v
ORDER BY violation;
