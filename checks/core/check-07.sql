-- Check 7 (PLATFORM_CORE v1.14 §3.6, §3.3): no single-column FK between two tenant_id tables.
-- For every FK whose child and parent both carry tenant_id, the child's tenant_id must be in the
-- key and paired with the parent's tenant_id (pg_constraint conkey / confkey). Test 21 covers the
-- UPDATE path at runtime.
SELECT 'Check 7: ' || con.conname || ' (' || child.relname || ' → ' || parent.relname
       || ') links two tenant_id tables without tenant_id in the key' AS violation
FROM pg_constraint con
JOIN pg_class child  ON child.oid  = con.conrelid
JOIN pg_class parent ON parent.oid = con.confrelid
JOIN pg_attribute ct ON ct.attrelid = con.conrelid  AND ct.attname = 'tenant_id' AND ct.attnum > 0 AND NOT ct.attisdropped
JOIN pg_attribute pt ON pt.attrelid = con.confrelid AND pt.attname = 'tenant_id' AND pt.attnum > 0 AND NOT pt.attisdropped
WHERE con.contype = 'f'
  AND NOT EXISTS (
    SELECT 1 FROM generate_subscripts(con.conkey, 1) i
    WHERE con.conkey[i] = ct.attnum AND con.confkey[i] = pt.attnum);
