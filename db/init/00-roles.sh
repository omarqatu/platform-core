#!/bin/bash
# Cluster bootstrap, run once by the container's superuser on first init.
# Creates the five roles (PLATFORM_CORE 3.4) and the database owned by migrator.
# Everything after this runs as migrator, from the migration tool (PROOF_SPEC T0).
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v migrator_pw="$MIGRATOR_PASSWORD" \
  -v app_user_pw="$APP_USER_PASSWORD" \
  -v authenticator_pw="$AUTHENTICATOR_PASSWORD" \
  -v job_runner_pw="$JOB_RUNNER_PASSWORD" \
  -v provisioner_pw="$PROVISIONER_PASSWORD" \
  -v db_name="$PLATFORM_DB" <<'SQL'
-- migrator: owner, migrations only, the one conscious BYPASSRLS (3.4).
CREATE ROLE migrator      LOGIN PASSWORD :'migrator_pw'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION BYPASSRLS;

-- Application roles: not superusers, not owners, no BYPASSRLS (PROOF_SPEC 1/4).
CREATE ROLE app_user      LOGIN PASSWORD :'app_user_pw'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE authenticator LOGIN PASSWORD :'authenticator_pw'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE job_runner    LOGIN PASSWORD :'job_runner_pw'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE provisioner   LOGIN PASSWORD :'provisioner_pw'
  NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;

-- No GRANT ... TO between roles: no role is a member of another (Tests 9, 22).

CREATE DATABASE :"db_name" OWNER migrator;
SQL
