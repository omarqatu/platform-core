#!/usr/bin/env python3
"""Generates checks/core/manifest.json from the governing document, by extraction (Check 2's input).

Every CREATE POLICY inside a ```sql block of the document's body (before the first "## Appendix"), verbatim: the two
templates (ON <t>) under "templates", every other one under its table, in the document's order. A policy written
twice with different text is an error — the document must say one thing. With --diff, prints what changes against
the current manifest, table by table, and writes nothing.

Usage: checks/core/generate-manifest.py docs/PLATFORM_CORE_v1_16_FROZEN_EN.md "PLATFORM_CORE v1.16" [--diff]
"""
import json
import re
import sys
from pathlib import Path

MANIFEST = Path(__file__).with_name("manifest.json")


def statements(document: str):
    in_sql, current = False, []
    for line in document.replace("\r", "").split("\n"):
        if line.startswith("## Appendix"):
            break
        if line.startswith("```sql"):
            in_sql = True
            continue
        if line.startswith("```") and in_sql:
            in_sql = False
            if current:
                raise SystemExit(f"unterminated statement: {current[0]}")
            continue
        if not in_sql:
            continue
        if not current and not line.startswith("CREATE POLICY"):
            continue
        current.append(line)
        code = re.sub(r"--.*", "", line).rstrip()
        if code.endswith(";"):
            yield "\n".join(current)
            current = []


def main():
    source, document_name = sys.argv[1], sys.argv[2]
    diff = "--diff" in sys.argv[3:]
    templates, tables, seen = {}, {}, {}
    for stmt in statements(Path(source).read_text(encoding="utf-8")):
        _, _, name, _, table = stmt.split()[:5]
        key = (table, name)
        if key in seen:
            if seen[key] != stmt:
                raise SystemExit(f"{name} ON {table} is written twice, with different text")
            continue
        seen[key] = stmt
        if table == "<t>":
            templates[name] = stmt
        else:
            tables.setdefault(table, []).append(stmt)

    # Update, never drop: a policy the manifest holds that the document states outside a ```sql block (the explicit
    # tenant_isolation of 1.13's decision 1, tenants_for_jobs in §8) is kept, and reported.
    current = json.loads(MANIFEST.read_text(encoding="utf-8"))
    name_of = lambda s: s.split()[2]
    merged, report = {}, []
    for table in list(current["tables"]) + [t for t in tables if t not in current["tables"]]:
        extracted = {name_of(s): s for s in tables.get(table, [])}
        kept = []
        for s in current["tables"].get(table, []):
            if name_of(s) in extracted:
                if extracted[name_of(s)] != s:
                    report.append(f"{table}: replaced {name_of(s)}")
                kept.append(extracted.pop(name_of(s)))
            else:
                report.append(f"{table}: kept     {name_of(s)} (not in a sql block of the document)")
                kept.append(s)
        for name, s in extracted.items():
            report.append(f"{table}: added    {name}")
            kept.append(s)
        merged[table] = kept
    for name in templates:
        if templates[name] != current["templates"].get(name):
            report.append(f"template {name}: changed")
    print("\n".join(report))
    if diff:
        return

    current["_comment"] = re.sub(r"PLATFORM_CORE v[\d.]+", document_name, current["_comment"])
    current["document"] = document_name
    current["templates"] = {**current["templates"], **templates}
    current["tables"] = merged
    MANIFEST.write_text(json.dumps(current, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{MANIFEST}: {len(current['templates'])} templates, {sum(map(len, merged.values()))} policies on "
          f"{len(merged)} tables ({sum(map(len, tables.values()))} from sql blocks of the document)")


main()
