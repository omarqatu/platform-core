-- Plant for Check 7: a single-column FK between two tenant_id tables (the 1.6 cross-tenant link).
CREATE TABLE selftest_c7 (id app_id PRIMARY KEY, tenant_id uuid NOT NULL, role_id uuid NOT NULL REFERENCES roles (id));
