-- Plant for Check 1: a table carrying tenant_id with no RLS at all.
CREATE TABLE selftest_c1 (id app_id PRIMARY KEY, tenant_id uuid NOT NULL);
