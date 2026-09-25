#!/usr/bin/env bash
# check-no-bulk-writes — a local repository check, not one of PLATFORM_CORE's
# ten checks (see checks/local/README.md). PLATFORM_CORE v1.16 §3.10, PROOF_SPEC
# v1.3 T4.16: no ExecuteUpdate/ExecuteDelete in application code. A bulk command
# bypasses the change tracker, so the automatic audit (§7) never sees it.
#
# Usage (from the repository root; needs no database):
#   checks/local/check-no-bulk-writes.sh              run the check over src/
#   checks/local/check-no-bulk-writes.sh --self-test  prove it fails on a plant
#
# The one declared exception is the password command (§3.8): a raw SQL command,
# not an EF bulk command, so it never matches. Any other exception is a line in
# bulk-writes-allowlist.txt (a path under src/), a project-owner decision.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
allowlist="$here/bulk-writes-allowlist.txt"

# scan <directory>: every call of ExecuteUpdate/ExecuteDelete (sync or async, generic or not) in a .cs file,
# outside bin/ and obj/, minus the allowlisted paths. Prints path:line:text.
scan() {
  local dir="$1" allowed
  allowed="$(tr -d '\r' < "$allowlist" | grep -vE '^\s*(#|$)' || true)"
  (cd "$dir" && grep -rnE --include='*.cs' --exclude-dir=bin --exclude-dir=obj \
      '\bExecute(Update|Delete)(Async)?[[:space:]]*[(<]' . || true) |
    sed 's|^\./||' |
    while IFS= read -r hit; do
      path="${hit%%:*}"
      if [ -n "$allowed" ] && grep -qxF "$path" <<< "$allowed"; then continue; fi
      echo "$hit"
    done
}

if [ "${1:-}" = "--self-test" ]; then
  # Plant both commands in a copy of src/ and require the check to report each. A check never seen
  # failing cannot be trusted for being green (PROOF_SPEC T2.2). The source tree is never edited.
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' EXIT
  (cd "$root/src" && tar --exclude=bin --exclude=obj -cf - .) | (cd "$work" && tar -xf -)
  mkdir -p "$work/Planted"
  cat > "$work/Planted/Plant.cs" <<'CS'
class Plant
{
    async Task Update(Core.CoreDbContext db) =>
        await db.Memberships.Where(m => m.Status == "active").ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, "disabled"));
    int Delete(Core.CoreDbContext db) => db.MembershipRoles.ExecuteDelete ();
}
CS
  output="$(scan "$work")"
  echo "$output"
  if grep -q "^Planted/Plant.cs:4:" <<< "$output" && grep -q "^Planted/Plant.cs:5:" <<< "$output" &&
     [ "$(grep -c . <<< "$output")" -eq 2 ]; then
    echo "PASS (self-test): check-no-bulk-writes detects both planted bulk commands."
    exit 0
  fi
  echo "FAIL (self-test): check-no-bulk-writes did not report exactly the two planted bulk commands."
  exit 1
fi

violations="$(scan "$root/src")"
if [ -n "$violations" ]; then
  echo "$violations"
  echo "FAIL: check-no-bulk-writes — ExecuteUpdate/ExecuteDelete in application code (PLATFORM_CORE 3.10, 7)."
  exit 1
fi
echo "PASS: check-no-bulk-writes — no ExecuteUpdate/ExecuteDelete in src/."
