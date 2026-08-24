#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
ROOT_DIR=$(dirname "$SCRIPT_DIR")
ENV_FILE="$ROOT_DIR/.env"
SECRET_FILE="$ROOT_DIR/docs/CODEX-SECRETS.local.md"
TEMP_DIR="$ROOT_DIR/.tmp"
VENV_DIR="$ROOT_DIR/.venv"
PYTHON_BIN="$VENV_DIR/bin/python"

require_command() {
  command -v "$1" >/dev/null 2>&1 || { echo "Missing prerequisite: $1" >&2; exit 1; }
}

merge_local_secrets() {
  [ -f "$SECRET_FILE" ] || return 0
  merge_tmp=$(mktemp)
  trap 'rm -f "$merge_tmp"' EXIT HUP INT TERM
  awk '
    FNR == NR {
      if ($0 ~ /^[A-Z][A-Z0-9_]*=.+$/) {
        key=$0; sub(/=.*/, "", key); value[key]=substr($0, length(key)+2)
      }
      next
    }
    /^[A-Z][A-Z0-9_]*=/ {
      key=$0; sub(/=.*/, "", key)
      if (key in value) { print key "=" value[key]; used[key]=1; next }
    }
    { print }
    END { for (key in value) if (!(key in used)) print key "=" value[key] }
  ' "$SECRET_FILE" "$ENV_FILE" > "$merge_tmp"
  mv "$merge_tmp" "$ENV_FILE"
  trap - EXIT HUP INT TERM
  echo "Merged non-empty local secret settings into .env (values hidden)."
}

require_command docker
require_command node
require_command npm
require_command dotnet
require_command python3
docker compose version >/dev/null

if [ ! -f "$ENV_FILE" ]; then
  cp "$ROOT_DIR/.env.example" "$ENV_FILE"
  echo "Created .env from .env.example."
fi
merge_local_secrets

if [ "${SKIP_INSTALL:-0}" != "1" ]; then
  npm ci --prefix "$ROOT_DIR"
  dotnet restore "$ROOT_DIR/VietnamCarPlatform.sln"
  (cd "$ROOT_DIR" && dotnet tool restore)
  if [ ! -x "$PYTHON_BIN" ]; then
    python3 -c 'import sys; raise SystemExit(0 if sys.version_info >= (3, 12) else 1)' || {
      echo "Python 3.12 or newer is required." >&2
      exit 1
    }
    python3 -m venv "$VENV_DIR"
  fi
  "$PYTHON_BIN" -m pip install -r "$ROOT_DIR/workers/ingestion/requirements.txt"
elif [ ! -x "$PYTHON_BIN" ]; then
  echo "SKIP_INSTALL=1 requires an existing .venv." >&2
  exit 1
fi

docker compose --project-directory "$ROOT_DIR" config --quiet
docker compose --project-directory "$ROOT_DIR" up --build --detach --wait
docker compose --project-directory "$ROOT_DIR" exec -T postgres psql -U vcp -d vietnam_car_platform < "$ROOT_DIR/scripts/verify-v1.1-schema.sql"
mkdir -p "$TEMP_DIR"
docker compose --project-directory "$ROOT_DIR" run --rm --no-deps ingestion-worker \
  python -m ingestion.cli discover-source \
  --registry /app/data/source-registry.v1.json \
  --templates /app/data/discovery-query-templates.v2.json \
  --brand Toyota --data-type price > "$TEMP_DIR/v2.1-discovery.json"
"$PYTHON_BIN" "$ROOT_DIR/scripts/verify_v2_1_discovery.py" "$TEMP_DIR/v2.1-discovery.json"
docker compose --project-directory "$ROOT_DIR" run --rm --no-deps ingestion-worker \
  python -m ingestion.cli validate-parser-registry \
  --registry /app/data/source-registry.v1.json \
  --parsers /app/data/parser-registry.v2.json

if [ "${SKIP_SEED:-0}" != "1" ]; then
  uid_gid="$(id -u):$(id -g)"
  registry=/app/data/source-registry.v1.json
  dsn="host=/var/run/postgresql dbname=vietnam_car_platform user=vcp"

  docker compose --project-directory "$ROOT_DIR" run --rm --no-deps ingestion-worker python -m ingestion.cli validate-seed --registry "$registry" --seed /app/data/seed/v1.2-initial-vehicles.json
  docker compose --project-directory "$ROOT_DIR" run --rm --no-deps --user "$uid_gid" --volume "$TEMP_DIR:/app/.tmp" ingestion-worker python -m ingestion.cli fetch-seed --registry "$registry" --seed /app/data/seed/v1.2-initial-vehicles.json --manifest /app/.tmp/v1.2-snapshots.json
  docker compose --project-directory "$ROOT_DIR" run --rm --no-deps --volume "$TEMP_DIR:/app/.tmp:ro" ingestion-worker python -m ingestion.cli publish-seed --registry "$registry" --seed /app/data/seed/v1.2-initial-vehicles.json --manifest /app/.tmp/v1.2-snapshots.json --dsn "$dsn"

  docker compose --project-directory "$ROOT_DIR" run --rm --no-deps ingestion-worker python -m ingestion.cli validate-registration-seed --registry "$registry" --seed /app/data/seed/v1.5-registration-rules.json
  docker compose --project-directory "$ROOT_DIR" run --rm --no-deps --user "$uid_gid" --volume "$TEMP_DIR:/app/.tmp" ingestion-worker python -m ingestion.cli fetch-registration-seed --registry "$registry" --seed /app/data/seed/v1.5-registration-rules.json --manifest /app/.tmp/v1.5-registration.json
  docker compose --project-directory "$ROOT_DIR" run --rm --no-deps --volume "$TEMP_DIR:/app/.tmp:ro" ingestion-worker python -m ingestion.cli publish-registration-seed --registry "$registry" --seed /app/data/seed/v1.5-registration-rules.json --manifest /app/.tmp/v1.5-registration.json --dsn "$dsn"

  docker compose --project-directory "$ROOT_DIR" run --rm --no-deps ingestion-worker python -m ingestion.cli validate-energy-seed --registry "$registry" --seed /app/data/seed/v1.6-energy.json
  docker compose --project-directory "$ROOT_DIR" run --rm --no-deps --user "$uid_gid" --volume "$TEMP_DIR:/app/.tmp" ingestion-worker python -m ingestion.cli fetch-energy-seed --registry "$registry" --seed /app/data/seed/v1.6-energy.json --manifest /app/.tmp/v1.6-energy.json
  docker compose --project-directory "$ROOT_DIR" run --rm --no-deps --volume "$TEMP_DIR:/app/.tmp:ro" ingestion-worker python -m ingestion.cli publish-energy-seed --registry "$registry" --seed /app/data/seed/v1.6-energy.json --manifest /app/.tmp/v1.6-energy.json --dsn "$dsn"

  docker compose --project-directory "$ROOT_DIR" run --rm --no-deps --user "$uid_gid" --volume "$TEMP_DIR:/app/.tmp" ingestion-worker python -m ingestion.cli fetch-real-world-consumption --registry "$registry" --manifest /app/.tmp/v3.3-real-world.json
  docker compose --project-directory "$ROOT_DIR" run --rm --no-deps --volume "$TEMP_DIR:/app/.tmp:ro" ingestion-worker python -m ingestion.cli publish-real-world-consumption --registry "$registry" --manifest /app/.tmp/v3.3-real-world.json --dsn "$dsn"

  for verification in verify_v1_3_catalog.py verify_v1_4_web.py verify_v1_5_onroad.py verify_v1_6_energy.py verify_v1_7_affordability.py verify_v1_8_financing.py verify_v1_9_compare.py verify_v1_10_admin.py verify_v1_final.py verify_v3_3_real_world.py verify_v3_4_search.py; do
    "$PYTHON_BIN" "$ROOT_DIR/scripts/$verification"
  done
fi

curl --fail --silent --show-error http://localhost:8080/health/live >/dev/null
curl --fail --silent --show-error http://localhost:8080/health/ready >/dev/null
curl --fail --silent --show-error http://localhost:3000 >/dev/null
docker compose --project-directory "$ROOT_DIR" ps
if [ "${SKIP_SEED:-0}" = "1" ]; then
  echo "Bootstrap complete: migrations and health checks passed (official seed refresh skipped)."
else
  echo "Bootstrap complete: migrations, official data and health checks passed."
fi
