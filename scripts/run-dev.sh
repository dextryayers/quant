#!/usr/bin/env bash
set -e
PORT=${1:-3737}
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ENGINE_DIR="$ROOT/engine/quant-engine"
GUI_PROJ="$ROOT/gui/Quant.Desktop/Quant.Desktop.csproj"

cargo run -p quant-engine --manifest-path "$ENGINE_DIR/Cargo.toml" -- --port "$PORT" &
ENGINE_PID=$!
trap "kill $ENGINE_PID" EXIT

for i in $(seq 1 30); do
  if curl -sf "http://127.0.0.1:$PORT/health" > /dev/null; then
    echo "Engine OK"
    break
  fi
  sleep 1
done

dotnet run --project "$GUI_PROJ" -- --engine-port "$PORT"
