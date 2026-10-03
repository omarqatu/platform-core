#!/usr/bin/env bash
# check-i18n-parity — a local repository check, not one of PLATFORM_CORE's ten checks (see checks/local/README.md).
# The keys of web/locales/ar.json are exactly the keys of a fresh extraction of web/src/ — none missing, none extra;
# web/locales/en.json is that extraction; every plural in ar.json gives the six Arabic forms.
# The logic is web/scripts/check-i18n-parity.mjs.
#
# Usage (from the repository root; needs node with web/'s packages installed — no database):
#   checks/local/check-i18n-parity.sh              run the check
#   checks/local/check-i18n-parity.sh --self-test  prove it fails on a missing key, an extra key, a short plural
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root/web" && node scripts/check-i18n-parity.mjs "$@"
