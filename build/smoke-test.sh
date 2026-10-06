#!/usr/bin/env bash
# Starts a packaged Qtoxide and checks it is still running after 15 s (catches missing libraries,
# crashes at start-up, unsigned macOS binaries...). Usage: build/smoke-test.sh <path to executable>
set -euo pipefail
exe="$1"
export QTOXIDE_HOME="$(mktemp -d)"
"$exe" > smoke.log 2>&1 &
pid=$!
sleep 15
if kill -0 "$pid" 2>/dev/null; then
  echo "Qtoxide is running (pid $pid)"
  kill "$pid" || true
  test -d "$QTOXIDE_HOME/profiles" || { echo "data folder not created"; exit 1; }
else
  echo "Qtoxide exited early:"; cat smoke.log; exit 1
fi
