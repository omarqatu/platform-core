-- Plant for Check 4: a policy written without TO — it applies to PUBLIC.
CREATE POLICY selftest_public ON audit_log FOR SELECT USING (true);
