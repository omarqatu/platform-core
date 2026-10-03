#!/usr/bin/env python3
"""Exports the API's error codes from their one source, src/Core/ApiErrorCodes.cs, to checks/local/api-error-codes.json
(the input of check-api-error-codes). A local repository check's input, not one of PLATFORM_CORE's ten.

Every `public const string Name = "code";` line of the class, in the file's order. Any other string literal in the
file is an error: the file holds codes and nothing else, so nothing in it escapes the export. With --check, compares
the export with the committed JSON and writes nothing; exits 1 if they differ.

Usage (repository root): checks/local/export-api-error-codes.py [--check]
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "src/Core/ApiErrorCodes.cs"
OUTPUT = Path(__file__).with_name("api-error-codes.json")
CONSTANT = re.compile(r'^\s*public const string (\w+) = "([a-z][a-z0-9_]*)";\s*(//.*)?$')


def codes() -> list[str]:
    found = []
    for number, line in enumerate(SOURCE.read_text(encoding="utf-8").splitlines(), 1):
        match = CONSTANT.match(line)
        if match:
            found.append(match.group(2))
        elif '"' in line and not line.lstrip().startswith("///"):
            raise SystemExit(f"{SOURCE.relative_to(ROOT)}:{number}: not a code constant: {line.strip()}")
    duplicates = sorted({c for c in found if found.count(c) > 1})
    if duplicates:
        raise SystemExit(f"duplicate codes: {', '.join(duplicates)}")
    if not found:
        raise SystemExit(f"no codes found in {SOURCE.relative_to(ROOT)}")
    return found


def main() -> None:
    text = json.dumps({"source": "src/Core/ApiErrorCodes.cs", "codes": codes()}, indent=2) + "\n"
    if "--check" in sys.argv[1:]:
        current = OUTPUT.read_text(encoding="utf-8") if OUTPUT.exists() else ""
        if current != text:
            print(f"FAIL: {OUTPUT.relative_to(ROOT)} is not the export of src/Core/ApiErrorCodes.cs. "
                  "Run checks/local/export-api-error-codes.py and commit the result.")
            sys.exit(1)
        print(f"PASS: {OUTPUT.relative_to(ROOT)} is the current export.")
        return
    OUTPUT.write_text(text, encoding="utf-8")
    print(f"wrote {OUTPUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
