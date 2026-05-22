#!/usr/bin/env bash
# heavy_load_harness.sh — synthesize the "heavy load" scenario for the
# two-scenario probe protocol defined in
# docs/plans/2026-05-22-audio-thread-isolation.md Task 8.
#
# Runs three concurrent load sources for the requested duration and cleanly
# terminates them all on SIGINT/SIGTERM. The harness deliberately mirrors the
# real-world load pattern documented in MEMORY:
#   "audio distortion correlates with SSH activity — journalctl tailing,
#    sqlite DB queries, and web traffic all compete with the audio pipeline".
#
# Three concurrent loops:
#   1. `journalctl -f` to /dev/null — continuous log streaming, the dominant
#      contributor to the SSH-activity gap.
#   2. sqlite3 busy-loop against the metrics DB (one SELECT every 500 ms) —
#      simulates a dashboard or external tool polling for metrics.
#   3. curl loop hitting radio-web HTTP endpoints — simulates a user driving
#      the UI in the browser.
#
# Usage:
#   bash scripts/research/heavy_load_harness.sh <duration_seconds>
#
# Output: harness logs go to stderr; no stdout artifact. Pair with
# scripts/research/sysload_capture.sh to capture the resulting load.

set -eu

DURATION="${1:-60}"
if ! [[ "$DURATION" =~ ^[0-9]+$ ]]; then
  echo "Usage: $0 <duration_seconds>" >&2
  exit 2
fi

METRICS_DB="${METRICS_DB:-/opt/radio-console/data/metrics/metrics.db}"
WEB_URL="${WEB_URL:-http://localhost:5002}"

# Track child PIDs so we can stop them on SIGINT/SIGTERM/EXIT.
declare -a CHILD_PIDS=()

cleanup() {
  echo "heavy_load_harness: stopping ${#CHILD_PIDS[@]} child loop(s)..." >&2
  for pid in "${CHILD_PIDS[@]}"; do
    if kill -0 "$pid" 2>/dev/null; then
      kill -TERM "$pid" 2>/dev/null || true
    fi
  done
  # Give children a moment to drop, then force-kill any stragglers.
  sleep 1
  for pid in "${CHILD_PIDS[@]}"; do
    if kill -0 "$pid" 2>/dev/null; then
      kill -KILL "$pid" 2>/dev/null || true
    fi
  done
  echo "heavy_load_harness: cleanup complete" >&2
}
trap cleanup EXIT INT TERM

echo "heavy_load_harness: starting for ${DURATION}s (metrics_db=$METRICS_DB web=$WEB_URL)" >&2

# 1. journalctl -f — continuous log streaming
( journalctl -f > /dev/null 2>&1 ) &
CHILD_PIDS+=($!)

# 2. sqlite busy-loop — one SELECT every ~500 ms against metrics.gauges
if [ -r "$METRICS_DB" ]; then
  ( while true; do
      sqlite3 "$METRICS_DB" 'SELECT COUNT(*) FROM gauges' > /dev/null 2>&1 || true
      sleep 0.5
    done ) &
  CHILD_PIDS+=($!)
else
  echo "heavy_load_harness: WARN $METRICS_DB unreadable; skipping sqlite loop" >&2
fi

# 3. curl loop against radio-web — hit a few endpoints in rotation
( while true; do
    for path in / /home /api/audio/state /api/sources; do
      curl -sS -o /dev/null -m 5 "${WEB_URL}${path}" 2>/dev/null || true
      sleep 0.25
    done
  done ) &
CHILD_PIDS+=($!)

# Run for the requested duration. `sleep` is in the foreground so SIGINT
# propagates to us and triggers cleanup via the trap.
sleep "$DURATION"

# Normal exit — trap cleanup handles child termination.
echo "heavy_load_harness: duration elapsed" >&2
