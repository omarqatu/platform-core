#!/usr/bin/env bash
# Stops what scripts/dev-up.sh started: Vite, Api, and the development PostgreSQL container (with its data).
# Touches only its own processes (.dev/*.pid) and the container carrying its label.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
state="$root/.dev"
pg_container=platform-core-dev-postgres
pg_label=platform-core.dev
quiet=0
[ "${1:-}" = --quiet ] && quiet=1
say() { [ "$quiet" -eq 1 ] || echo "$@"; }

# Each process was started with setsid: its pid is its process group, so the group goes with it (npm -> vite,
# dotnet run -> Api).
stop_group() {
  local name="$1" file="$state/$1.pid" pid
  [ -f "$file" ] || return 0
  pid="$(cat "$file")"
  if kill -0 -- "-$pid" 2> /dev/null; then
    say "==> stopping $name ($pid)"
    kill -TERM -- "-$pid" 2> /dev/null || true
    for _ in $(seq 1 10); do kill -0 -- "-$pid" 2> /dev/null || break; sleep 1; done
    kill -KILL -- "-$pid" 2> /dev/null || true
  fi
  rm -f "$file"
}

stop_group web
stop_group api

if [ "$(docker inspect -f "{{index .Config.Labels \"$pg_label\"}}" "$pg_container" 2> /dev/null || true)" = 1 ]; then
  say "==> removing $pg_container"
  docker rm -f -v "$pg_container" > /dev/null
fi
say "stopped."
