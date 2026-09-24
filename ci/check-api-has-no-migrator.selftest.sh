#!/usr/bin/env bash
# Negative test for ci/check-api-has-no-migrator.sh (T0.3): every planted case
# must make the check fail, and the clean image must pass. Each case is built as
# a derived image and a temporary config directory; the source tree is never
# edited.
#
# Usage: MIGRATOR_PASSWORD=... ci/check-api-has-no-migrator.selftest.sh <api-image-tag>
set -euo pipefail

base="${1:?usage: $0 <api-image-tag>}"
password="${MIGRATOR_PASSWORD:?MIGRATOR_PASSWORD must be set to the migrator password}"
check="$(cd "$(dirname "$0")" && pwd)/check-api-has-no-migrator.sh"

work="$(mktemp -d)"
images=()
cleanup() {
  rm -rf "$work"
  if [ "${#images[@]}" -gt 0 ]; then docker rmi -f "${images[@]}" > /dev/null; fi
}
trap cleanup EXIT

failures=0

# expect <fail|pass> <name> <config-dir> <image>
expect() {
  local want="$1" name="$2" config="$3" image="$4" output status
  set +e
  output="$(API_CONFIG_DIR="$config" bash "$check" "$image" 2>&1)"
  status=$?
  set -e
  if { [ "$want" = fail ] && [ "$status" -ne 0 ]; } || { [ "$want" = pass ] && [ "$status" -eq 0 ]; }; then
    echo "ok   — $name: $(grep -E '^(FAIL|PASS)' <<< "$output" | head -1)"
  else
    echo "FAIL — $name: expected the check to $want, it exited $status"
    echo "$output" | sed 's/^/       /'
    failures=$((failures + 1))
  fi
}

# derive <name> <dockerfile-lines...>: an image on top of the base, with $work/<name> as context.
derive() {
  local name="$1"; shift
  local context="$work/$name"
  mkdir -p "$context"
  printf 'FROM %s\n' "$base" > "$context/Dockerfile"
  printf '%s\n' "$@" >> "$context/Dockerfile"
  docker build -q -t "selftest-$name" "$context" > /dev/null
  images+=("selftest-$name")
}

# plant <name> <json-fragment>: a config dir whose appsettings.json carries the fragment,
# and an image whose /app/appsettings.json is that same file.
plant() {
  local name="$1" fragment="$2"
  local config="$work/$name-config"
  mkdir -p "$config" "$work/$name"
  printf '{\n  "ConnectionStrings": {\n    %s\n  }\n}\n' "$fragment" > "$config/appsettings.json"
  cp "$config/appsettings.json" "$work/$name/appsettings.json"
  derive "$name" "COPY appsettings.json /app/appsettings.json"
}

clean_config="$work/clean-config"
mkdir -p "$clean_config"

# 1. The original planted string from T0.
plant original '"migrator": "Host=x;Username=migrator"'
expect fail "1. original planted string" "$work/original-config" selftest-original

# 2. The same, upper case with spaces around "=".
plant spaced '"MIGRATOR" : "Host=x; USER ID = MIGRATOR"'
expect fail "2. upper case and spaces" "$work/spaced-config" selftest-spaced

# 3. The password alone, inside an arbitrary binary file.
mkdir -p "$work/random-file"
{ head -c 512 /dev/urandom; printf '%s' "$password"; head -c 512 /dev/urandom; } > "$work/random-file/random.bin"
derive random-file "COPY random.bin /app/random.bin"
expect fail "3. password in a random file" "$clean_config" selftest-random-file

# 4. The password alone, in an environment variable of the image.
derive password-env "ENV UNRELATED_SETTING=$password"
expect fail "4. password in an environment variable" "$clean_config" selftest-password-env

# 5. The .NET environment key for a migrator connection string.
derive key-env "ENV ConnectionStrings__migrator=Host=x"
expect fail "5. ConnectionStrings__migrator in the environment" "$clean_config" selftest-key-env

# 6. The password as a UTF-16 string, the way .NET embeds literals in assemblies.
mkdir -p "$work/utf16"
printf '%s' "$password" | iconv -f UTF-8 -t UTF-16LE > "$work/utf16/Literal.dll"
derive utf16 "COPY Literal.dll /app/Literal.dll"
expect fail "6. password as a UTF-16 literal" "$clean_config" selftest-utf16

# Back to green: the real image and the real configuration.
expect pass "clean image and configuration" "src/Api" "$base"

if [ "$failures" -ne 0 ]; then
  echo "FAIL (self-test): $failures case(s) did not behave as expected."
  exit 1
fi
echo "PASS (self-test): every planted case fails the T0.3 check, and the clean image passes."
