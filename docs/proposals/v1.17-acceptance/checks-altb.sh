#!/usr/bin/env bash
# The ten checks (PLATFORM_CORE §3.6) on a database carrying alternative B (P3 and accept_invitation()), two ways:
#   1. the repository's checks as they are (the v1.16 manifest, chains and grants): which checks B breaks;
#   2. the same checks with the one input B needs: public.accept_invitation in grants.json's executable_routines
#      (Check 6). The manifest and the declared chains are the v1.16 ones, unchanged — B adds no policy and no chain.
# Usage (repository root, on a database where run.sh altb ran — it leaves B applied): checks-altb.sh
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../../.." && pwd)"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT

echo "== the checks as they are (v1.16 inputs), on B"
bash "$root/checks/core/run-checks.sh"; echo "(exit $?)"

cp -r "$root/checks/core" "$work/core"
python3 - "$work" <<'PY'
import json, sys
from pathlib import Path
path = Path(sys.argv[1]) / "core/grants.json"
grants = json.loads(path.read_text(encoding="utf-8"))
grants["executable_routines"] = sorted(grants["executable_routines"] + ["public.accept_invitation"])
path.write_text(json.dumps(grants, ensure_ascii=False, indent=2), encoding="utf-8")
print("B's inputs: grants.json executable_routines =", grants["executable_routines"])
PY
for f in manifest.json policy-chains.json; do
  cmp -s "$root/checks/core/$f" "$work/core/$f" && echo "B's inputs: $f unchanged from v1.16"
done

echo "== the checks with B's inputs, on B"
bash "$work/core/run-checks.sh"; status=$?
echo "== B's inputs, each check on its plant"
bash "$work/core/run-checks.sh" --self-test || status=1
exit $status
