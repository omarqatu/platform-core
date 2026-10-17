#!/usr/bin/env bash
# check-manifest-extraction — a local repository check, not one of PLATFORM_CORE's ten checks (see
# checks/local/README.md). checks/core/manifest.json (Check 2's input) is extracted from the governing documents —
# PLATFORM_CORE v1.16 and its amendments, each in Arabic and English — never edited by hand (Amendment 01 §4.1,
# OPEN_ITEMS 50). This check fails while the committed manifest is not the extraction, and while the two languages
# of a document state different policies.
#
# Usage (from the repository root; python3; needs no database):
#   checks/local/check-manifest-extraction.sh              run the check
#   checks/local/check-manifest-extraction.sh --self-test  prove it fails on each plant
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
generator="$root/checks/core/generate-manifest.py"

# extraction <root>: exit 0 when <root>'s manifest is its documents' extraction. Prints what differs.
extraction() {
  python3 "$generator" --check --root "$1" 2>&1 | grep -v '^pending (not built): '
  return "${PIPESTATUS[0]}"
}

if [ "${1:-}" = "--self-test" ]; then
  # Each plant goes into a fresh copy of the inputs; the repository is never edited. A check never seen failing
  # cannot be trusted for being green (PROOF_SPEC T2.2).
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' EXIT
  fresh() {
    rm -rf "$work/tree" && mkdir -p "$work/tree/docs" "$work/tree/checks/core"
    cp "$root"/docs/PLATFORM_CORE_v1_16_FROZEN*.md "$root"/docs/PLATFORM_CORE_AMENDMENT_*.md "$work/tree/docs/"
    cp "$root/checks/core/manifest.json" "$root/checks/core/amendment-policies-built.json" "$work/tree/checks/core/"
  }
  failures=0
  expect_failure() {
    local name="$1" pattern="$2" output status
    output="$(extraction "$work/tree")"; status=$?
    if [ "$status" -ne 0 ] && grep -q -- "$pattern" <<< "$output"; then
      echo "ok   — $name: $(grep -m1 -- "$pattern" <<< "$output")"
    else
      echo "FAIL — $name: not reported (exit $status)"
      [ -n "$output" ] && sed 's/^/       /' <<< "$output"
      failures=$((failures + 1))
    fi
  }

  fresh
  if ! extraction "$work/tree" > /dev/null; then
    echo "FAIL — the unplanted copy does not pass; nothing below would mean anything"
    exit 1
  fi

  # (a) The manifest edited by hand: a policy no document states.
  fresh
  python3 - "$work/tree/checks/core/manifest.json" <<'PY'
import json, sys
path = sys.argv[1]
manifest = json.load(open(path, encoding="utf-8"))
manifest["tables"]["roles"].append("CREATE POLICY selftest_extra ON roles\n  FOR SELECT TO app_user\n  USING (true);")
json.dump(manifest, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
PY
  expect_failure "(a) a policy added to the manifest by hand" "roles: removed  selftest_extra"

  # (b) The translation drifted from the Arabic: one condition changed in the English alone.
  fresh
  sed -i "s/WITH CHECK (status IN ('sent', 'failed'));/WITH CHECK (status IN ('sent'));/" \
    "$work/tree/docs/PLATFORM_CORE_AMENDMENT_01_EN.md"
  expect_failure "(b) the English amendment drifted from the Arabic" "drifted from docs/PLATFORM_CORE_AMENDMENT_01.md"

  # (c) A built name the amendment does not state.
  fresh
  python3 - "$work/tree/checks/core/amendment-policies-built.json" <<'PY'
import json, sys
path = sys.argv[1]
built = json.load(open(path, encoding="utf-8"))
built["01"].append("plans.selftest_missing")
json.dump(built, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
PY
  expect_failure "(c) amendment-policies-built.json names a policy the amendment does not state" "plans.selftest_missing"

  # (d) An amendment edited without regenerating the manifest: a built policy is listed and the manifest regenerated
  # (a consistent state), then its text changes in both languages and the manifest is left as it was.
  fresh
  python3 - "$work/tree/checks/core/amendment-policies-built.json" <<'PY'
import json, sys
path = sys.argv[1]
built = json.load(open(path, encoding="utf-8"))
built["01"].append("notification_log.notification_log_status_forward")
json.dump(built, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
PY
  python3 "$generator" --root "$work/tree" > /dev/null
  if ! extraction "$work/tree" > /dev/null; then
    echo "FAIL — (d) the regenerated copy does not pass before the edit"
    failures=$((failures + 1))
  fi
  sed -i "s/WITH CHECK (status IN ('sent', 'failed'));/WITH CHECK (status IN ('sent'));/" \
    "$work/tree/docs/PLATFORM_CORE_AMENDMENT_01.md" "$work/tree/docs/PLATFORM_CORE_AMENDMENT_01_EN.md"
  expect_failure "(d) a built amendment policy edited, the manifest not regenerated" \
    "notification_log: replaced notification_log_status_forward"

  if [ "$failures" -ne 0 ]; then
    echo "FAIL (self-test): $failures plant(s) not reported."
    exit 1
  fi
  echo "PASS (self-test): check-manifest-extraction fails on each of its four plants."
  exit 0
fi

if ! output="$(extraction "$root")"; then
  echo "$output"
  echo "FAIL: check-manifest-extraction — checks/core/manifest.json is not the extraction of the documents."
  echo "      Run checks/core/generate-manifest.py and commit the manifest with the change that caused it."
  exit 1
fi
echo "PASS: check-manifest-extraction — $output."
