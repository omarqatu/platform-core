#!/usr/bin/env bash
# Regenerates docs/schema.sql: the schema of a freshly migrated database, as pg_dump prints it.
#
#   scripts/dump-schema.sh                 write docs/schema.sql
#   scripts/dump-schema.sh <output-file>   write somewhere else (checks/local/check-schema-dump.sh uses this)
#
# The file is generated, never edited by hand. The source of truth is src/Migrations/Sql; docs/schema.sql is the
# result of applying all of it, in one file, for reading. Run this after adding a migration and commit the result;
# CI (check-schema-dump) fails while the committed file differs from a fresh dump.
#
# What is dumped: schema only (no rows), the public schema with its tables, domains, constraints, indexes, the
# procedure, every policy and every grant. Excluded: migrations_meta (the EF history table — bookkeeping, not schema).
# Not in any single-database dump: the roles themselves and the database-level grants (db/init/00-roles.sh and the
# head of 0003_grants.sql) — docs/DATABASE.md describes both.
#
# Determinism: the session time zone is UTC (PGTZ), the \restrict key is fixed, and the "Dumped from/by" version
# lines are removed, so the same schema gives the same file on any PostgreSQL 18 minor version.
#
# PG_DUMP overrides how pg_dump is reached. The default is the docker-compose service CI uses. Against the
# scripts/dev-up.sh container:
#   PG_DUMP='docker exec -i -e PGTZ=UTC platform-core-dev-postgres pg_dump -U migrator -d platform' scripts/dump-schema.sh
# Always against a database migrated from clean: a database with manual changes dumps those changes too.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="${1:-$root/docs/schema.sql}"
pg_dump_cmd="${PG_DUMP:-docker compose exec -T -e PGTZ=UTC postgres pg_dump -U migrator -d platform}"

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

$pg_dump_cmd --schema-only --exclude-schema=migrations_meta --restrict-key=platformcoreschema > "$tmp"

{
  echo '-- GENERATED FILE — do not edit. Regenerate with scripts/dump-schema.sh after a migration.'
  echo '-- The schema of a database migrated from clean (src/Migrations/Sql, applied in order), as pg_dump prints it.'
  echo '-- Schema only: no rows. Roles and database-level grants are not part of a database dump (docs/DATABASE.md).'
  echo '-- CI fails while this file differs from a fresh dump (checks/local/check-schema-dump.sh).'
  echo
  tr -d '\r' < "$tmp" | grep -v -E '^-- Dumped (from database|by pg_dump) version '
} > "$out"

echo "dump-schema: wrote $out"
