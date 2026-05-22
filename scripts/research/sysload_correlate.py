#!/usr/bin/env python3
"""sysload_correlate.py — correlate audio events with host-load TSV.

Inputs:
  argv[1]  audio-event-list artifact (one event per line:
             event_ts<TAB>metric_value
           where event_ts is an ISO-8601 UTC timestamp parseable by
           datetime.fromisoformat (Z suffix tolerated).)
  argv[2]  sysload TSV produced by sysload_capture.sh

For each event, computes a 5-second pre-event window:
  - cpu_5s_median  = median of (cpu_user + cpu_sys) over the 5s window
  - io_5s_total    = sum of (disk_read_kbps + disk_write_kbps) / 1024 (MB)
  - log_rate_5s    = median log_lines_1s
  - ssh_5s_max     = max ssh_sessions

Classifies each event:
  quiet_host        cpu_5s_median <  70 AND log_rate_5s < 100 AND ssh_5s_max == 0
  load_correlated   otherwise

Outputs to stdout, tab-separated:
  event_ts<TAB>metric_value<TAB>cpu_5s_median<TAB>io_5s_total_mb<TAB>
    log_rate_5s_median<TAB>ssh_5s_max<TAB>classification

See docs/research/2026-05-22-bt-audio-stabilization.md §7 Idea #1.
"""

from __future__ import annotations

import argparse
import csv
import statistics
import sys
from datetime import datetime, timedelta, timezone


def _parse_iso(ts: str) -> datetime:
  """Parse ISO-8601 timestamps with a tolerant Z suffix."""
  s = ts.strip()
  if s.endswith("Z"):
    s = s[:-1] + "+00:00"
  dt = datetime.fromisoformat(s)
  if dt.tzinfo is None:
    dt = dt.replace(tzinfo=timezone.utc)
  return dt.astimezone(timezone.utc)


def load_sysload(path: str):
  rows = []
  with open(path, newline="", encoding="utf-8") as f:
    reader = csv.reader(f, delimiter="\t")
    header = next(reader, None)
    if header is None:
      return rows
    for parts in reader:
      if len(parts) < 15:
        continue
      try:
        wall = _parse_iso(parts[1])
      except Exception:
        continue
      try:
        cpu_user = float(parts[2] or 0)
        cpu_sys = float(parts[3] or 0)
        disk_read = float(parts[6] or 0)
        disk_write = float(parts[7] or 0)
        log_lines = int(parts[8] or 0)
        ssh_sessions = int(parts[9] or 0)
      except ValueError:
        continue
      rows.append({
        "wall": wall,
        "cpu_total": cpu_user + cpu_sys,
        "disk_kbps": disk_read + disk_write,
        "log_lines": log_lines,
        "ssh_sessions": ssh_sessions,
      })
  return rows


def load_events(path: str):
  events = []
  with open(path, newline="", encoding="utf-8") as f:
    for raw in f:
      line = raw.strip()
      if not line or line.startswith("#"):
        continue
      parts = line.split("\t")
      ts_str = parts[0]
      metric = parts[1] if len(parts) > 1 else ""
      try:
        ts = _parse_iso(ts_str)
      except Exception:
        continue
      events.append((ts, metric))
  return events


def window_around(rows, end_ts: datetime, window_seconds: int = 5):
  start_ts = end_ts - timedelta(seconds=window_seconds)
  return [r for r in rows if start_ts <= r["wall"] <= end_ts]


def classify(cpu_med: float, log_med: float, ssh_max: int) -> str:
  if cpu_med < 70 and log_med < 100 and ssh_max == 0:
    return "quiet_host"
  return "load_correlated"


def main() -> int:
  ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
  ap.add_argument("events", help="audio-event-list artifact")
  ap.add_argument("sysload", help="sysload TSV from sysload_capture.sh")
  ap.add_argument("--window", type=int, default=5, help="pre-event window (s)")
  args = ap.parse_args()

  sysload = load_sysload(args.sysload)
  events = load_events(args.events)

  writer = csv.writer(sys.stdout, delimiter="\t", lineterminator="\n")
  writer.writerow([
    "event_ts", "metric_value", "cpu_5s_median", "io_5s_total_mb",
    "log_rate_5s_median", "ssh_5s_max", "classification",
  ])

  for ts, metric in events:
    window = window_around(sysload, ts, args.window)
    if not window:
      cpu_med = 0.0
      io_total_mb = 0.0
      log_med = 0.0
      ssh_max = 0
    else:
      cpu_med = statistics.median(r["cpu_total"] for r in window)
      io_total_mb = sum(r["disk_kbps"] for r in window) / 1024.0
      log_med = statistics.median(r["log_lines"] for r in window)
      ssh_max = max(r["ssh_sessions"] for r in window)
    cls = classify(cpu_med, log_med, ssh_max)
    writer.writerow([
      ts.isoformat().replace("+00:00", "Z"),
      metric,
      f"{cpu_med:.2f}",
      f"{io_total_mb:.3f}",
      f"{log_med:.2f}",
      str(ssh_max),
      cls,
    ])

  return 0


if __name__ == "__main__":
  sys.exit(main())
