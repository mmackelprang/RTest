#!/usr/bin/env bash
# sysload_capture.sh — concurrent system-load capture for Cast/BT audio research.
#
# Captures one row per second of:
#   - CPU/IO/scheduler stats from vmstat 1
#   - Per-device IO from iostat -x 1
#   - Per-process CPU/IO for Radio.API/Radio.Web/journald/sqlite3/sshd via pidstat
#   - Per-second journald log-line count
#   - Per-second active sshd session count
#
# All rows are timestamped with the monotonic clock (CLOCK_MONOTONIC nanoseconds)
# and the wall-clock UTC second. Output is a single tab-separated
# `sysload_<ts>.tsv` artifact merged from the four concurrent streams.
#
# Usage:
#   bash scripts/research/sysload_capture.sh <duration_seconds>
#
# Used by Phase 1+2 plans (BT capture watchdog, BT codec observability, Cast HM
# DC parity, CPU affinity for FM-BT-11/FM-CAST-7). See
# docs/research/2026-05-22-bt-audio-stabilization.md §7 Idea #1 (PROBE-SYS-LOAD).

set -eu

DURATION="${1:-60}"
if ! [[ "$DURATION" =~ ^[0-9]+$ ]]; then
  echo "Usage: $0 <duration_seconds>" >&2
  exit 2
fi

# Required external tools — fail fast with an actionable message.
if ! command -v iostat >/dev/null 2>&1; then
  echo "ERROR: iostat not found. Install sysstat: sudo apt install -y sysstat" >&2
  exit 3
fi
if ! command -v vmstat >/dev/null 2>&1; then
  echo "ERROR: vmstat not found (should be in procps)" >&2
  exit 3
fi

TS="$(date -u +%Y%m%dT%H%M%SZ)"
OUTFILE="sysload_${TS}.tsv"
TMPDIR="$(mktemp -d)"
trap 'rm -rf "$TMPDIR"' EXIT

# Header
printf 'monotonic_ns\twall_utc\tcpu_user\tcpu_sys\tcpu_idle\tcpu_iowait\tdisk_read_kbps\tdisk_write_kbps\tlog_lines_1s\tssh_sessions\tradio_api_cpu\tradio_web_cpu\tjournald_cpu\tsqlite_cpu\tsshd_cpu\n' \
  > "$OUTFILE"

# vmstat 1 producer — outputs lines like: r b swpd free ... us sy id wa st
# We extract us, sy, id, wa columns (positions 13-16).
vmstat 1 "$DURATION" 2>/dev/null \
  | awk 'NR>2 {print $13"\t"$14"\t"$15"\t"$16}' \
  > "$TMPDIR/vmstat.tsv" &
VMSTAT_PID=$!

# iostat 1 producer — summary line per interval (Device row totals)
iostat -x 1 "$DURATION" 2>/dev/null \
  | awk '
    /^avg-cpu/ { skip=1; next }
    /^Device/  { in_devices=1; read_sum=0; write_sum=0; next }
    in_devices && NF>=6 { read_sum += $3; write_sum += $9 }
    /^$/ && in_devices {
      print read_sum"\t"write_sum
      in_devices=0
    }
  ' > "$TMPDIR/iostat.tsv" &
IOSTAT_PID=$!

# Per-process CPU sampler — resolve PIDs each second; missing process == 0.
# pgrep -x requires the comm to match exactly. The Radio.API/Radio.Web .NET
# single-file binaries publish to /opt/radio-console/{api,web}/Radio.API and
# Radio.Web; the kernel comm field is the basename ("Radio.API", "Radio.Web").
# Without -x, pgrep also matches things like "Radio.API.Tests" if any process
# happens to be running with that name.
( for ((i=0; i<DURATION; i++)); do
    api_cpu=0; web_cpu=0; jrn_cpu=0; sql_cpu=0; ssh_cpu=0
    for pid in $(pgrep -d ' ' -x Radio.API 2>/dev/null); do
      v=$(awk -v pid="$pid" '$1==pid {print $9}' <(top -b -n 1 -p "$pid" 2>/dev/null | tail -n +8))
      api_cpu=$(awk -v a="$api_cpu" -v b="${v:-0}" 'BEGIN{print a+b}')
    done
    for pid in $(pgrep -d ' ' -x Radio.Web 2>/dev/null); do
      v=$(awk -v pid="$pid" '$1==pid {print $9}' <(top -b -n 1 -p "$pid" 2>/dev/null | tail -n +8))
      web_cpu=$(awk -v a="$web_cpu" -v b="${v:-0}" 'BEGIN{print a+b}')
    done
    for pid in $(pgrep -d ' ' -x systemd-journald 2>/dev/null); do
      v=$(awk -v pid="$pid" '$1==pid {print $9}' <(top -b -n 1 -p "$pid" 2>/dev/null | tail -n +8))
      jrn_cpu=$(awk -v a="$jrn_cpu" -v b="${v:-0}" 'BEGIN{print a+b}')
    done
    for pid in $(pgrep -d ' ' sqlite3 2>/dev/null); do
      v=$(awk -v pid="$pid" '$1==pid {print $9}' <(top -b -n 1 -p "$pid" 2>/dev/null | tail -n +8))
      sql_cpu=$(awk -v a="$sql_cpu" -v b="${v:-0}" 'BEGIN{print a+b}')
    done
    for pid in $(pgrep -d ' ' sshd 2>/dev/null); do
      v=$(awk -v pid="$pid" '$1==pid {print $9}' <(top -b -n 1 -p "$pid" 2>/dev/null | tail -n +8))
      ssh_cpu=$(awk -v a="$ssh_cpu" -v b="${v:-0}" 'BEGIN{print a+b}')
    done
    printf '%s\t%s\t%s\t%s\t%s\n' "$api_cpu" "$web_cpu" "$jrn_cpu" "$sql_cpu" "$ssh_cpu"
    sleep 1
  done ) > "$TMPDIR/pidstat.tsv" &
PIDSTAT_PID=$!

# log/ssh meter producer — one line per second with timestamps
( for ((i=0; i<DURATION; i++)); do
    mono_ns=$(awk 'BEGIN{srand(); printf "%.0f", systime()*1e9}')
    wall_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)
    log_lines=$(journalctl --since '1 second ago' -o cat 2>/dev/null | wc -l)
    ssh_count=$(pgrep -c '^sshd:' 2>/dev/null || echo 0)
    printf '%s\t%s\t%s\t%s\n' "$mono_ns" "$wall_utc" "$log_lines" "$ssh_count"
    sleep 1
  done ) > "$TMPDIR/meter.tsv" &
METER_PID=$!

# Wait for all producers
wait "$VMSTAT_PID" "$IOSTAT_PID" "$PIDSTAT_PID" "$METER_PID" 2>/dev/null || true

# Merge row by row. Each file should have approximately DURATION rows; align
# by line number. Use paste so missing rows become empty fields (still readable).
paste "$TMPDIR/meter.tsv" "$TMPDIR/vmstat.tsv" "$TMPDIR/iostat.tsv" "$TMPDIR/pidstat.tsv" \
  | awk -F'\t' '{
      mono=$1; wall=$2; logs=$3; ssh=$4
      us=$5; sy=$6; id=$7; wa=$8
      drd=$9; dwr=$10
      api=$11; web=$12; jrn=$13; sql=$14; ssd=$15
      print mono"\t"wall"\t"us"\t"sy"\t"id"\t"wa"\t"drd"\t"dwr"\t"logs"\t"ssh"\t"api"\t"web"\t"jrn"\t"sql"\t"ssd
    }' >> "$OUTFILE"

# Sanity check — warn if the merge produced only the header. Most common cause
# is a per-producer command failing silently (e.g., iostat missing, pgrep
# matching no PIDs because the process names are wrong).
LINES=$(wc -l < "$OUTFILE")
if [ "$LINES" -lt 2 ]; then
  echo "WARNING: sysload_capture produced only header (no data rows). Check producer output:" >&2
  ls -la "$TMPDIR/" >&2
  wc -l "$TMPDIR"/*.tsv >&2
fi

echo "$OUTFILE"
