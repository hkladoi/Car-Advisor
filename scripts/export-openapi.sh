#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
ROOT_DIR=$(dirname "$SCRIPT_DIR")
OUTPUT_DIR="$ROOT_DIR/packages/contracts/openapi"
WORK_DIR="$ROOT_DIR/.tmp/openapi"
mkdir -p "$OUTPUT_DIR" "$WORK_DIR"

dotnet run --project "$ROOT_DIR/apps/api/src/Api/VietnamCarPlatform.Api.csproj" --configuration Release --no-build --urls http://127.0.0.1:5099 >"$WORK_DIR/api.out.log" 2>"$WORK_DIR/api.err.log" &
API_PID=$!
trap 'kill "$API_PID" 2>/dev/null || true' EXIT INT TERM

attempt=0
until curl --fail --silent --show-error http://127.0.0.1:5099/swagger/v1/swagger.json --output "$OUTPUT_DIR/v1.json"; do
  attempt=$((attempt + 1))
  if [ "$attempt" -ge 120 ]; then
    cat "$WORK_DIR/api.err.log" >&2 || true
    exit 1
  fi
  sleep 0.5
done

echo "Generated $OUTPUT_DIR/v1.json"
