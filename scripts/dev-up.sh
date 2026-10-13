#!/usr/bin/env bash
# Development only: the whole stack on this machine, for manual testing in the browser.
#   PostgreSQL 18 (a fresh container, 127.0.0.1:5432) -> migrations, then the seed contract, as migrator (as CI does)
#   -> Api on http://127.0.0.1:5080 (its three connection strings only, never migrator's: T0.3)
#   -> Vite dev on http://localhost:5173, proxying /api to Api.
# Every password here is a development value (docker-compose.yml, appsettings.Development.json); never a real secret.
# The database is recreated on every run: the seed contract runs on a clean database only.
# Stop everything with scripts/dev-down.sh. Logs: .dev/*.log.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
state="$root/.dev"

pg_container=platform-core-dev-postgres
pg_label=platform-core.dev
api_url=http://127.0.0.1:5080
web_origin=http://localhost:5173

fail() { echo "dev-up: $*" >&2; exit 1; }

# --- 1. Prerequisites: report what is missing, with its install command; never install anything. ---
missing=0
if ! command -v docker > /dev/null; then
  echo "missing: Docker — install Docker Engine (https://docs.docker.com/engine/install/ubuntu/) or enable Docker Desktop's WSL integration" >&2
  missing=1
elif ! docker info > /dev/null 2>&1; then
  echo "missing: a running Docker daemon — start it (sudo service docker start, or Docker Desktop)" >&2
  missing=1
fi
sdk="$(jq -r .sdk.version global.json 2> /dev/null || sed -n 's/.*"version": *"\([^"]*\)".*/\1/p' global.json)"
if ! command -v dotnet > /dev/null; then
  echo "missing: the .NET SDK $sdk (global.json) — sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0" >&2
  missing=1
elif ! dotnet --version > /dev/null 2>&1; then
  echo "missing: a .NET SDK matching global.json ($sdk, rollForward latestFeature) — sudo apt-get install -y dotnet-sdk-10.0" >&2
  missing=1
fi
# web/: Node 22 as CI (.github/workflows/ci.yml); react-router needs 22.22 or later. A Linux Node, not Windows' npm
# under /mnt. Ubuntu's own package where it is recent enough (26.04: 22.22.1).
node_install='sudo apt-get update && sudo apt-get install -y nodejs npm'
node_version="$(command -v node > /dev/null && node -p 'process.versions.node' || true)"
if [ -z "$node_version" ]; then
  echo "missing: Node.js 22 (Linux) — $node_install" >&2
  missing=1
else
  IFS=. read -r node_major node_minor _ <<< "$node_version"
  if [ "$node_major" -lt 22 ] || { [ "$node_major" -eq 22 ] && [ "$node_minor" -lt 22 ]; }; then
    echo "missing: Node.js 22.22 or later (found $node_version) — $node_install" >&2
    missing=1
  fi
  case "$(command -v npm || true)" in
    "" | /mnt/*) echo "missing: a Linux npm (found: '$(command -v npm || true)') — $node_install" >&2; missing=1 ;;
  esac
fi
[ "$missing" -eq 0 ] || fail "install the prerequisites above, then run again."

# --- A previous run of this script, if any, is stopped first. ---
"$root/scripts/dev-down.sh" --quiet
mkdir -p "$state"

port_in_use() { ss -Hltn "sport = :$1" | grep -q .; }
for port in 5432 5080 5173; do
  if port_in_use "$port"; then
    holder="$(docker ps --filter "publish=$port" --format '{{.Names}}' 2> /dev/null | head -n1)"
    fail "port $port is already in use${holder:+ (container $holder)}; stop it and run again."
  fi
done

# --- 2. PostgreSQL 18, bound to 127.0.0.1 only; the roles from db/init, as docker-compose.yml. ---
echo "==> PostgreSQL 18 ($pg_container, 127.0.0.1:5432)"
docker run -d --name "$pg_container" --label "$pg_label=1" \
  -p 127.0.0.1:5432:5432 \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=postgres_dev \
  -e PLATFORM_DB=platform \
  -e MIGRATOR_PASSWORD=migrator_dev \
  -e APP_USER_PASSWORD=app_user_dev \
  -e AUTHENTICATOR_PASSWORD=authenticator_dev \
  -e JOB_RUNNER_PASSWORD=job_runner_dev \
  -e PROVISIONER_PASSWORD=provisioner_dev \
  -v "$root/db/init:/docker-entrypoint-initdb.d:ro" \
  --health-cmd 'pg_isready -h 127.0.0.1 -U postgres -d platform' \
  --health-interval 2s --health-timeout 3s --health-retries 30 \
  postgres:18 > /dev/null
for _ in $(seq 1 60); do
  status="$(docker inspect -f '{{.State.Health.Status}}' "$pg_container")"
  [ "$status" = healthy ] && break
  sleep 1
done
[ "$status" = healthy ] || { docker logs "$pg_container" | tail -n 30; fail "PostgreSQL did not become healthy."; }

# --- 3. Migrations, then the seed contract: as migrator, the way CI does. ---
echo "==> Build"
dotnet build PlatformCore.slnx -v quiet -nologo > "$state/build.log" 2>&1 \
  || { tail -n 30 "$state/build.log"; fail "build failed (.dev/build.log)."; }

echo "==> Migrate (migrator)"
ConnectionStrings__migrator='Host=127.0.0.1;Port=5432;Database=platform;Username=migrator;Password=migrator_dev' \
  dotnet run --project src/Migrations --no-build

echo "==> Seed the contract (migrator)"
docker exec -i "$pg_container" psql -X -q -v ON_ERROR_STOP=1 -U migrator -d platform < tests/seed/seed-contract.sql

# --- 4. Api: its own three connection strings (appsettings.Development.json), never migrator's (T0.3). ---
echo "==> Api ($api_url)"
setsid env -u ConnectionStrings__migrator -u MIGRATOR_PASSWORD \
  ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="$api_url" \
  Security__AllowedOrigins__0="$web_origin" \
  Session__RequireHttps=false \
  dotnet run --project src/Api --no-build --no-launch-profile \
  > "$state/api.log" 2>&1 < /dev/null &
echo $! > "$state/api.pid"
for _ in $(seq 1 60); do
  code="$(curl -s -o /dev/null -w '%{http_code}' "$api_url/api/tenants" || true)"
  [ "$code" = 401 ] && break
  kill -0 "$(cat "$state/api.pid")" 2> /dev/null || break
  sleep 1
done
[ "$code" = 401 ] || { tail -n 30 "$state/api.log"; fail "Api did not start (.dev/api.log)."; }

# --- 5. Vite dev on 5173, its proxy forwarding /api to Api. ---
echo "==> Vite dev ($web_origin)"
if [ ! -f web/node_modules/.package-lock.json ] || [ web/package-lock.json -nt web/node_modules/.package-lock.json ]; then
  (cd web && npm ci --no-audit --no-fund)
fi
# exec: the background subshell becomes setsid itself, so $! is the new process group (dev-down.sh stops it whole).
(cd web && API_URL="$api_url" exec setsid npm run dev > "$state/web.log" 2>&1 < /dev/null) &
echo $! > "$state/web.pid"
for _ in $(seq 1 60); do
  curl -s -o /dev/null "$web_origin/" && break
  kill -0 "$(cat "$state/web.pid")" 2> /dev/null || break
  sleep 1
done
curl -s -o /dev/null "$web_origin/" || { tail -n 30 "$state/web.log"; fail "Vite did not start (.dev/web.log)."; }

cat << EOF

Ready: $web_origin   (open exactly this origin; 127.0.0.1:5173 is refused by the CSRF check)
Seed users: omar, sara, khaled, layla, rami, nour — password <username>-seed-password (test values).
Logs: .dev/api.log, .dev/web.log.   Stop: scripts/dev-down.sh
EOF
