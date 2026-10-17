#!/usr/bin/env bash
# check-password-policy — a local repository check, not one of PLATFORM_CORE's ten
# (see checks/local/README.md). OPEN_ITEMS 62 and 65.
#
# The password rule's minimum is one number, and the server is the only place that
# enforces it (PasswordPolicy in src/Core/Identity). The interface repeats it so the
# screens can say the rule before the round trip: once as a constant it compares
# against, and once inside each sentence that states it — in English in the code and
# in Arabic in web/locales/ar.json, because errorMessage() is formatted with no
# values at every call site and the number cannot be interpolated.
#
# Drift does not weaken the rule: the server still refuses what the rule refuses. It
# makes the product wrong in a worse way — a screen that says one number while the
# server enforces another reads as broken, not as policy. This check asserts those
# occurrences agree, and nothing more.
#
# Usage (from the repository root; needs no database and no node_modules):
#   checks/local/check-password-policy.sh              compare the occurrences
#   checks/local/check-password-policy.sh --self-test  prove it fails on a plant
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"

# scan <root>: prints "<number> <where>" per occurrence, or a "MISSING ..." line when a
# place that must state the number does not. The caller decides; this only reports.
scan() {
  python3 -I - "$1" <<'PY'
import json
import re
import sys
from pathlib import Path

root = Path(sys.argv[1])


def one(where, text, pattern):
    """The single number `pattern` finds in `text`, or a MISSING line."""
    found = {m.group(1) for m in re.finditer(pattern, text)}
    if len(found) != 1:
        print(f"MISSING {where}: expected exactly one match of /{pattern}/, found {len(found)}")
        return
    print(f"{found.pop()} {where}")


def read(relative):
    path = root / relative
    if not path.exists():
        print(f"MISSING {relative}: the file does not exist")
        return None
    return path.read_text(encoding="utf-8")


# 1. The server's number — the one that decides.
if (text := read("src/Core/Identity/PasswordPolicy.cs")) is not None:
    one("src/Core/Identity/PasswordPolicy.cs (MinimumLength)", text,
        r"public const int MinimumLength = (\d+);")

# 2. The interface's constant — what the screens compare against.
if (text := read("web/src/password/policy.ts")) is not None:
    one("web/src/password/policy.ts (MINIMUM_PASSWORD_LENGTH)", text,
        r"export const MINIMUM_PASSWORD_LENGTH = (\d+);")

# 3-4. The number inside each English sentence that states the rule.
if (text := read("web/src/i18n/apiErrors.ts")) is not None:
    one("web/src/i18n/apiErrors.ts (errors.password_too_short)", text,
        r"[Aa]t least (\d+) characters")
if (text := read("web/src/components/messages.ts")) is not None:
    one("web/src/components/messages.ts (common.password.rule)", text,
        r"[Aa]t least (\d+) characters")

# 5-6. And inside each Arabic sentence, where a wrong number is just as wrong.
if (text := read("web/locales/ar.json")) is not None:
    try:
        messages = json.loads(text)
    except json.JSONDecodeError as error:
        print(f"MISSING web/locales/ar.json: not valid JSON ({error})")
        messages = {}
    for key in ("errors.password_too_short", "common.password.rule"):
        if key not in messages:
            print(f"MISSING web/locales/ar.json ({key}): the message is absent")
            continue
        one(f"web/locales/ar.json ({key})", messages[key], r"(\d+)")
PY
}

report() {
  local output="$1"
  echo "$output" | sed 's/^/  /'
  if grep -q '^MISSING ' <<< "$output"; then
    echo "FAIL: check-password-policy — the rule's number is not stated where it must be (OPEN_ITEMS 62, 65)."
    return 1
  fi
  local numbers
  numbers="$(awk '{print $1}' <<< "$output" | sort -u)"
  if [ "$(grep -c . <<< "$numbers")" -ne 1 ]; then
    echo "FAIL: check-password-policy — the occurrences disagree: $(tr '\n' ' ' <<< "$numbers")."
    echo "  The server's PasswordPolicy.MinimumLength decides; bring the others to it (OPEN_ITEMS 65)."
    return 1
  fi
  echo "PASS: check-password-policy — the rule's minimum is $numbers in all $(grep -c . <<< "$output") places."
  return 0
}

if [ "${1:-}" = "--self-test" ]; then
  # A plant in a copy of the six files: the interface says one number while the server says
  # another, which is exactly the broken product this check exists to catch. A check never
  # seen failing cannot be trusted for being green (PROOF_SPEC T2.2). The source is never edited.
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' EXIT
  for file in src/Core/Identity/PasswordPolicy.cs web/src/password/policy.ts web/src/i18n/apiErrors.ts \
              web/src/components/messages.ts web/locales/ar.json; do
    mkdir -p "$work/$(dirname "$file")"
    cp "$root/$file" "$work/$file"
  done
  sed -i 's/export const MINIMUM_PASSWORD_LENGTH = [0-9]*;/export const MINIMUM_PASSWORD_LENGTH = 8;/' \
    "$work/web/src/password/policy.ts"

  echo "--- planted: the interface's constant says 8, the server still decides ---"
  if report "$(scan "$work")"; then
    echo "FAIL (self-test): check-password-policy passed on a planted mismatch."
    exit 1
  fi
  echo "PASS (self-test): check-password-policy detects a number that drifted from the server's."
  exit 0
fi

report "$(scan "$root")"
