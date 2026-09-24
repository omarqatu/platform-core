-- Plant for Check 6: the 1.11 trap — a column SELECT on credentials "to fix" an UPDATE ... WHERE —
-- and TEMP on the database.
GRANT SELECT (user_id) ON user_password_credentials TO app_user;
DO $$ BEGIN EXECUTE format('GRANT TEMPORARY ON DATABASE %I TO app_user', current_database()); END $$;
