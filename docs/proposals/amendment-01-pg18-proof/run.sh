#!/usr/bin/env bash
# Amendment 01 §4.1 on PostgreSQL 18: the 17 policies, verbatim from the document, on stand-in tables, both ways.
# Usage (repository root; docker): docs/proposals/amendment-01-pg18-proof/run.sh   — prints the result; compare it
# with result-2026-10-17.txt. Starts and removes its own container; touches nothing else.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../../.." && pwd)"
name="amendment-01-pg18-proof-$$"
docker run -d --name "$name" -e POSTGRES_PASSWORD=proof postgres:18 > /dev/null
trap 'docker rm -f "$name" > /dev/null' EXIT
until docker exec "$name" pg_isready -U postgres > /dev/null 2>&1; do sleep 1; done; sleep 2
psql() { docker exec -i "$name" psql -U postgres -X "$@"; }

psql -t -A -c 'SELECT version()'
psql -q -v ON_ERROR_STOP=1 < "$here/01-setup.sql"
# 02: the policies, extracted from the amendment exactly as checks/core/generate-manifest.py extracts them.
python3 - "$root/docs/PLATFORM_CORE_AMENDMENT_01_EN.md" <<'PY' | psql -q -v ON_ERROR_STOP=1
import re, sys
in_sql, current = False, []
for line in open(sys.argv[1], encoding="utf-8").read().split("\n"):
    if line.startswith("```sql"): in_sql = True; continue
    if line.startswith("```") and in_sql: in_sql = False; continue
    if not in_sql or (not current and not line.startswith("CREATE POLICY")): continue
    current.append(line)
    if re.sub(r"--.*", "", line).rstrip().endswith(";"): print("\n".join(current) + "\n"); current = []
PY
echo "policies on the stand-in tables: $(psql -t -A -c "SELECT count(*) FROM pg_policies WHERE schemaname = 'public'") (the 17 of §4.1, and 3 from 1.16)"
psql < "$here/03-behaviour.sql" 2>&1 | grep -v '^$'
psql < "$here/04-behaviour-more.sql" 2>&1 | grep -v '^$'
