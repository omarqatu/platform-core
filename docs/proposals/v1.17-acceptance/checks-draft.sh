#!/usr/bin/env bash
# The ten checks (PLATFORM_CORE §3.6) on a database with the draft applied, two ways:
#   1. the repository's checks as they are (the v1.16 manifest, chains and grants): which checks the draft breaks;
#   2. the same checks with their inputs regenerated for the draft: the manifest by checks/core/generate-manifest.py
#      from a copy of the v1.16 document with the draft's policy texts in place of the three it replaces, the two new
#      chains declared, the new column grant declared.
# Usage (repository root, after run.sh applied the draft): checks-draft.sh
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../../.." && pwd)"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT

echo "== the checks as they are (v1.16 inputs), on the draft"
bash "$root/checks/core/run-checks.sh"; echo "(exit $?)"

cp -r "$root/checks/core" "$work/core"
python3 - "$root" "$here" "$work" <<'PY'
import json, re, sys
from pathlib import Path
root, here, work = map(Path, sys.argv[1:4])
doc = (root / "docs/PLATFORM_CORE_v1_16_FROZEN_EN.md").read_text(encoding="utf-8")
draft_sql = "\n".join((here / f).read_text(encoding="utf-8") for f in
                      ["p1-membership-needs-invitation.sql", "p2-invitation-records-its-acceptor.sql"])
for name, table in [("memberships_provisioner_insert", "memberships"), ("memberships_provisioner_rejoin", "memberships"),
                    ("invitations_provisioner_update", "invitations")]:
    new = re.search(rf"CREATE POLICY {name} ON {table}\b.*?;", draft_sql, re.S).group(0)
    doc, n = re.subn(rf"CREATE POLICY {name} ON {table}\b.*?;", lambda _: new, doc, flags=re.S)
    assert n >= 1, name
    print(f"draft document: {name} replaced ({n} occurrence(s))")
found = re.search(r"CREATE POLICY invitations_provisioner_found ON invitations\b.*?;", draft_sql, re.S).group(0)
anchor = re.search(r"CREATE POLICY invitations_provisioner_update ON invitations\b.*?;", doc, re.S)
doc = doc[:anchor.end()] + "\n\n" + found + doc[anchor.end():]
print("draft document: invitations_provisioner_found added")
(work / "draft-document.md").write_text(doc, encoding="utf-8")

chains = json.loads((work / "core/policy-chains.json").read_text(encoding="utf-8"))
chains["chains"] += [
    {"table": "memberships", "reads": ["invitations", "users", "persons"], "actor": "provisioner",
     "covered_by": {"invitations": ["invitations_provisioner_select"], "users": ["users_provisioner_select"],
                    "persons": ["persons_provisioner_select"]},
     "document": "draft v1.17 §3.10: memberships → invitations, users, persons (an invitation for the member's address)"},
    {"table": "invitations", "reads": ["memberships", "users", "persons"], "actor": "provisioner",
     "covered_by": {"memberships": ["memberships_provisioner_select"], "users": ["users_provisioner_select"],
                    "persons": ["persons_provisioner_select"]},
     "document": "draft v1.17 §3.10: invitations → memberships, users, persons (the acceptor's address)"}]
(work / "core/policy-chains.json").write_text(json.dumps(chains, ensure_ascii=False, indent=2), encoding="utf-8")

grants = json.loads((work / "core/grants.json").read_text(encoding="utf-8"))
grants["tables"]["invitations"]["provisioner"] = sorted(grants["tables"]["invitations"]["provisioner"] + ["INSERT"])
grants["columns"]["invitations"]["provisioner"]["UPDATE"] = sorted(grants["columns"]["invitations"]["provisioner"]["UPDATE"] + ["accepted_by"])
(work / "core/grants.json").write_text(json.dumps(grants, ensure_ascii=False, indent=2), encoding="utf-8")
PY
echo "== the manifest regenerated from the draft document (generate-manifest.py --diff, then written)"
python3 "$work/core/generate-manifest.py" "$work/draft-document.md" "PLATFORM_CORE draft v1.17" --diff
python3 "$work/core/generate-manifest.py" "$work/draft-document.md" "PLATFORM_CORE draft v1.17"

echo "== the checks with the draft's inputs, on the draft"
bash "$work/core/run-checks.sh"; status=$?
echo "== the draft's inputs, each check on its plant"
bash "$work/core/run-checks.sh" --self-test || status=1
exit $status
