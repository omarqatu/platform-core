#!/usr/bin/env bash
# Local check: docs/schema.sql is the current dump of the migrated schema (not one of PLATFORM_CORE's ten checks —
# checks/local/README.md). Needs a database migrated from clean, the same one the catalog checks run on.
#
#   checks/local/check-schema-dump.sh              the committed file must equal a fresh dump
#   checks/local/check-schema-dump.sh --self-test  the comparison must fail on a planted difference
#
# PG_DUMP is passed through to scripts/dump-schema.sh (how pg_dump is reached).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
committed="$root/docs/schema.sql"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

fresh="$work/fresh.sql"
bash "$root/scripts/dump-schema.sh" "$fresh" > /dev/null

# compare <expected-file> <fresh-file>: 0 when identical, 1 with the diff printed otherwise.
compare() {
  if diff -u --label "docs/schema.sql (committed)" --label "fresh dump" "$1" "$2" > "$work/diff"; then
    return 0
  fi
  cat "$work/diff"
  return 1
}

if [ "${1:-}" = "--self-test" ]; then
  [ -f "$committed" ] || { echo "check-schema-dump self-test: docs/schema.sql is missing"; exit 1; }
  # 1. A copy equal to the fresh dump must pass.
  cp "$fresh" "$work/same.sql"
  compare "$work/same.sql" "$fresh" > /dev/null || { echo "check-schema-dump self-test: identical files reported as different"; exit 1; }
  # 2. A planted drift — one policy line edited in a copy — must fail and name the line.
  sed '0,/^CREATE POLICY /s//CREATE POLICY planted_drift_/' "$fresh" > "$work/planted.sql"
  if compare "$work/planted.sql" "$fresh" > "$work/planted.out"; then
    echo "check-schema-dump self-test: a planted difference was not reported"
    exit 1
  fi
  grep -q 'planted_drift_' "$work/planted.out" || { echo "check-schema-dump self-test: the report does not show the planted line"; exit 1; }
  echo "PASS: check-schema-dump fails on its plant (self-test)."
  exit 0
fi

if [ ! -f "$committed" ]; then
  echo "check-schema-dump: docs/schema.sql is missing — run scripts/dump-schema.sh and commit it."
  exit 1
fi
if compare "$committed" "$fresh"; then
  echo "PASS: docs/schema.sql is the current schema dump."
else
  echo
  echo "check-schema-dump: docs/schema.sql differs from the migrated schema — run scripts/dump-schema.sh and commit it."
  exit 1
fi
