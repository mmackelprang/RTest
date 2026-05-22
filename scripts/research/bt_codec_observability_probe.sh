#!/usr/bin/env bash
# bt_codec_observability_probe.sh
#
# Phase 1 / Plan C — BT codec observability probe.
#
# Sequentially exercises a list of Bluetooth phones against the radio-api
# service and records, per phone, whether the new "BluetoothCodec:" log line
# emits, whether `bluetooth.a2dp.*` gauges appear in metrics.db, and how the
# codec name matches `bluetoothctl info`.
#
# Output (stdout) is a single-line summary record consumed by
# `bt_codec_observability_compare.py`:
#
#   events_emitted=<N>, codec_log_lines=<L>, ui_codec_displayed=<bool>, \
#     per_phone_codec=<addr1=sbc,addr2=aac,...>
#
# Detail logs go to stderr.
#
# Usage:
#   bt_codec_observability_probe.sh --duration <seconds_per_phone> \
#                                   --phones <addr1,addr2,...> \
#                                   [--metrics-db <path>] \
#                                   [--service <unit>] \
#                                   [--ui-url <http://host:5002/api/bluetooth/status>]
#
# Designed to be run on the `radio` host (Ubuntu N100 with TP-Link hci0 BT).
# Acceptance criterion: events_emitted >= 3 with parseable codec names for
# >= 2 of 3 phones matching `bluetoothctl info`.

set -euo pipefail

DURATION=60
PHONES=""
METRICS_DB="/opt/radio-console/data/metrics/metrics.db"
SERVICE="radio-api"
UI_URL="http://localhost:5002/api/bluetooth/status"
ADAPTER_MAC="78:20:51:F5:FB:A7"  # TP-Link UB500 (Music/A2DP per boundary doc)

while [[ $# -gt 0 ]]; do
  case "$1" in
    --duration) DURATION="$2"; shift 2 ;;
    --phones) PHONES="$2"; shift 2 ;;
    --metrics-db) METRICS_DB="$2"; shift 2 ;;
    --service) SERVICE="$2"; shift 2 ;;
    --ui-url) UI_URL="$2"; shift 2 ;;
    --adapter) ADAPTER_MAC="$2"; shift 2 ;;
    -h|--help)
      sed -n '2,30p' "$0" >&2
      exit 0
      ;;
    *)
      echo "Unknown arg: $1" >&2
      exit 2
      ;;
  esac
done

if [[ -z "$PHONES" ]]; then
  echo "ERROR: --phones <addr1,addr2,...> is required" >&2
  exit 2
fi

log() { echo "[$(date -Is)] $*" >&2; }

# Always pin the adapter for the dual-BT setup before any bluetoothctl call.
bt_select_adapter() {
  bluetoothctl select "$ADAPTER_MAC" >/dev/null 2>&1 || true
}

# Disconnect every paired+connected device on the music adapter (best-effort).
bt_disconnect_all() {
  bt_select_adapter
  local conn
  while read -r conn; do
    [[ -z "$conn" ]] && continue
    log "Disconnecting $conn"
    bluetoothctl disconnect "$conn" >/dev/null 2>&1 || true
  done < <(bluetoothctl devices Connected 2>/dev/null | awk '{print $2}')
}

# Get the codec ID/name BlueZ reports for the given device via bluetoothctl info.
# Echoes "<codecHex>=<friendly>" e.g. "0=sbc" / "2=aac" / "ff=aptx".
bt_codec_from_bluetoothctl() {
  local addr="$1"
  bt_select_adapter
  # Example output:
  #   Codec: 0x00 (0)
  local raw
  raw="$(bluetoothctl info "$addr" 2>/dev/null | grep -iE 'Codec' | head -n1 || true)"
  if [[ -z "$raw" ]]; then
    echo "?=unknown"
    return
  fi
  local hex
  hex="$(echo "$raw" | grep -oE '0x[0-9A-Fa-f]+' | head -n1 | sed 's/0x//')"
  local friendly
  case "${hex:-}" in
    00|0) friendly="sbc" ;;
    02|2) friendly="aac" ;;
    FF|ff) friendly="vendor" ;;
    "") friendly="absent" ;;
    *) friendly="other-0x${hex}" ;;
  esac
  echo "${hex:-?}=${friendly}"
}

# Pull the count + most recent codec value from journalctl for this phone window.
codec_log_lines_since() {
  local since="$1"
  journalctl -u "$SERVICE" --since "$since" --no-pager 2>/dev/null \
    | grep -c 'BluetoothCodec:' || true
}

# Pull most recent gauge values from metrics.db (best-effort; tolerates missing DB).
metrics_codec_summary() {
  if [[ ! -r "$METRICS_DB" ]]; then
    echo "metrics_db_unreadable"
    return
  fi
  sqlite3 "$METRICS_DB" "SELECT metric, value FROM metrics_gauges WHERE metric LIKE 'bluetooth.a2dp.%' ORDER BY id DESC LIMIT 6;" 2>/dev/null \
    | tr '\n' ';' || echo "query_failed"
}

# Try to fetch CodecName from the BluetoothStatusDto via the API (Web port 5002 → API 5000).
ui_codec_displayed() {
  if command -v curl >/dev/null 2>&1; then
    local body
    body="$(curl -fsS --max-time 5 "$UI_URL" 2>/dev/null || true)"
    if [[ "$body" == *'"codecName"'* ]] || [[ "$body" == *'"CodecName"'* ]]; then
      # Did it actually have a value (not null)?
      if echo "$body" | grep -qE '"codecName"\s*:\s*"[A-Za-z]'; then
        echo "true"
        return
      fi
      if echo "$body" | grep -qE '"CodecName"\s*:\s*"[A-Za-z]'; then
        echo "true"
        return
      fi
    fi
  fi
  echo "false"
}

events_emitted=0
codec_log_lines_total=0
per_phone=()
ui_seen_at_least_once="false"

IFS=',' read -r -a PHONE_LIST <<<"$PHONES"
bt_select_adapter

for addr in "${PHONE_LIST[@]}"; do
  addr="${addr// /}"
  [[ -z "$addr" ]] && continue
  log "=== Phone $addr ==="

  bt_disconnect_all
  sleep 2

  start_iso="$(date -Is)"
  log "Connecting $addr ..."
  if bluetoothctl connect "$addr" >/dev/null 2>&1; then
    log "Connected $addr; waiting ${DURATION}s for codec emission"
  else
    log "WARN: connect failed for $addr — continuing (will still inspect logs)"
  fi

  sleep "$DURATION"

  lines="$(codec_log_lines_since "$start_iso")"
  lines="${lines//[^0-9]/}"
  lines="${lines:-0}"
  codec_log_lines_total=$((codec_log_lines_total + lines))

  if [[ "$lines" -gt 0 ]]; then
    events_emitted=$((events_emitted + 1))
  fi

  ui_now="$(ui_codec_displayed)"
  if [[ "$ui_now" == "true" ]]; then
    ui_seen_at_least_once="true"
  fi

  ref_codec="$(bt_codec_from_bluetoothctl "$addr")"
  log "Phone $addr: log_lines=$lines, ui=$ui_now, bluetoothctl_codec=$ref_codec"
  metrics_snapshot="$(metrics_codec_summary)"
  log "Phone $addr: metrics_snapshot=$metrics_snapshot"

  per_phone+=("${addr}=${ref_codec##*=}")
done

per_phone_csv="$(IFS=,; echo "${per_phone[*]:-}")"

# Final summary line on stdout — consumed by the compare script.
echo "events_emitted=${events_emitted}, codec_log_lines=${codec_log_lines_total}, ui_codec_displayed=${ui_seen_at_least_once}, per_phone_codec=${per_phone_csv}"
