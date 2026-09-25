#!/usr/bin/env bash
# Check 9 (PLATFORM_CORE v1.14 §3.6, §3.5/6): the source of the scope. A static scan of the code:
# the second-axis variables (app.membership_id, app.scope_all, app.can_manage_scope, and since v1.16
# app.can_manage_members) may appear only
# in the files declared in checks/core/scope-variable-writers.json — the tenant-selection layer (T3)
# and the background-job layer (T7). Anywhere else, including a read from a request payload, a header
# or a claim, is a failure: a scope variable arriving from the client is self-escalation. Their only
# source is membership_scope, resolved per transaction.
# Scanned: application code and configuration (.cs, .cshtml, .razor, .json). Not the migration SQL,
# where the policies read these variables through current_setting as the document writes them.
#
# Usage: checks/core/check-09.sh [source-root]   (default: src)
set -euo pipefail

root="${1:-src}"
allowlist="$(dirname "$0")/scope-variable-writers.json"
pattern='app\.(membership_id|scope_all|can_manage_scope|can_manage_members)'

# The declared files: the string items of the "files" array.
allowed="$(tr -d '\r\n' < "$allowlist" | sed -n 's/.*"files"[[:space:]]*:[[:space:]]*\[\([^]]*\)\].*/\1/p' | grep -o '"[^"]*"' | tr -d '"' || true)"

while IFS= read -r hit; do
  [ -z "$hit" ] && continue
  file="${hit%%:*}"
  if ! grep -qxF "$file" <<< "$allowed"; then
    echo "Check 9: ${hit} — a second-axis variable outside the declared writers"
  fi
done < <(grep -rnE --include='*.cs' --include='*.cshtml' --include='*.razor' --include='*.json' "$pattern" "$root" || true)

exit 0
