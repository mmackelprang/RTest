#!/usr/bin/env python3
"""bt_lifecycle_summarize.py — summarize per-cycle BT node detection/teardown latency.

Inputs:
  argv[1]  harness log artifact (output of bt_pair_unpair_harness.sh)
           Lines like:
             [YYYY-MM-DDTHH:MM:SSZ] Cycle N/M: BT power off
             [YYYY-MM-DDTHH:MM:SSZ] Cycle N/M: BT power on
  argv[2]  radio-side journal artifact, filtered to lines like:
             "PW capture node appeared for AA:BB:CC:DD:EE:FF"   (Plan B scrape path)
             "PW registry: BT node appeared id=NN address=AA:BB:CC:DD:EE:FF"
             "PW registry: BT node disappeared id=NN address=AA:BB:CC:DD:EE:FF"
             (one timestamp per line, format yyyy-mm-ddThh:mm:ss(.fff)?(Z|+hh:mm))

For each "BT power on" event in the harness, finds the next radio-side
"node appeared" event (within 60 s) and computes detection_latency_ms.
For each "BT power off" event, finds the next "node disappeared" event
(within 60 s) and computes teardown_latency_ms.

Output: per-cycle CSV on stdout PLUS a summary block of the form:

  cycles=N
  detection_latency_ms_p50=X, p95=Y
  teardown_latency_ms_p50=A, p95=B
  failed_detections=F
  failed_teardowns=T

Used as input to bt_lifecycle_compare.py (PASS/FAIL gate against plan E §7).
"""

from __future__ import annotations

import csv
import re
import sys
from datetime import datetime, timedelta, timezone

MATCH_WINDOW_SEC = 60

HARNESS_TS_RE = re.compile(
  r"^\[(?P<ts>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z)\]\s+Cycle\s+(?P<n>\d+)/\d+:\s+BT power\s+(?P<dir>on|off)"
)

# journalctl prefixes vary by --output flag. Accept both ISO-8601 and the
# short "Month DD HH:MM:SS host process[pid]:" syslog format by extracting
# the first ISO-8601 timestamp on the line if present; otherwise fall back.
ISO_TS_RE = re.compile(
  r"(?P<ts>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?)"
)

NODE_APPEARED_RE = re.compile(
  r"(?:PW capture node appeared for|PW registry:\s+BT node appeared.*?address=)"
  r"\s*(?P<addr>[0-9A-Fa-f:]{17})"
)
NODE_DISAPPEARED_RE = re.compile(
  r"PW registry:\s+BT node disappeared.*?address=\s*(?P<addr>[0-9A-Fa-f:]{17})"
)


def _parse_ts(raw: str) -> datetime | None:
  if not raw:
    return None
  s = raw.replace(' ', 'T')
  # Normalize "Z" → "+00:00" for fromisoformat compatibility
  if s.endswith('Z'):
    s = s[:-1] + '+00:00'
  try:
    dt = datetime.fromisoformat(s)
  except ValueError:
    return None
  if dt.tzinfo is None:
    dt = dt.replace(tzinfo=timezone.utc)
  return dt.astimezone(timezone.utc)


def load_harness_events(path: str):
  """Returns list of (ts, cycle_num, direction) tuples ordered by ts."""
  events = []
  with open(path, encoding='utf-8') as f:
    for line in f:
      m = HARNESS_TS_RE.match(line.strip())
      if not m:
        continue
      ts = _parse_ts(m.group('ts'))
      if ts is None:
        continue
      events.append((ts, int(m.group('n')), m.group('dir')))
  events.sort(key=lambda t: t[0])
  return events


def load_radio_events(path: str):
  """Returns list of (ts, kind, addr) tuples where kind in {'appeared','disappeared'}."""
  events = []
  with open(path, encoding='utf-8') as f:
    for line in f:
      ts_m = ISO_TS_RE.search(line)
      if ts_m is None:
        continue
      ts = _parse_ts(ts_m.group('ts'))
      if ts is None:
        continue
      app = NODE_APPEARED_RE.search(line)
      if app:
        events.append((ts, 'appeared', app.group('addr').upper()))
        continue
      dis = NODE_DISAPPEARED_RE.search(line)
      if dis:
        events.append((ts, 'disappeared', dis.group('addr').upper()))
  events.sort(key=lambda t: t[0])
  return events


def find_next(events, after_ts, kind, *, window=timedelta(seconds=MATCH_WINDOW_SEC)):
  """Return the first event matching kind whose ts is in (after_ts, after_ts+window], or None."""
  for ts, k, addr in events:
    if ts <= after_ts:
      continue
    if ts - after_ts > window:
      return None
    if k == kind:
      return (ts, k, addr)
  return None


def percentile(values, p):
  if not values:
    return None
  s = sorted(values)
  k = (len(s) - 1) * (p / 100.0)
  lo = int(k)
  hi = min(lo + 1, len(s) - 1)
  if lo == hi:
    return s[lo]
  return s[lo] + (s[hi] - s[lo]) * (k - lo)


def main() -> int:
  if len(sys.argv) < 3:
    print(f"Usage: {sys.argv[0]} <harness_log> <radio_journal_log>", file=sys.stderr)
    return 2

  harness = load_harness_events(sys.argv[1])
  radio = load_radio_events(sys.argv[2])

  if not harness:
    print("ERROR: no harness events parsed", file=sys.stderr)
    return 2

  writer = csv.writer(sys.stdout)
  writer.writerow([
    'cycle', 'event', 'harness_ts', 'radio_ts', 'latency_ms', 'addr'
  ])

  detection_lat = []
  teardown_lat = []
  failed_det = 0
  failed_tear = 0
  cycles = set()

  for ts, n, direction in harness:
    cycles.add(n)
    if direction == 'on':
      match = find_next(radio, ts, 'appeared')
      if match is None:
        failed_det += 1
        writer.writerow([n, 'detection', ts.isoformat(), '', '', ''])
        continue
      r_ts, _, addr = match
      lat_ms = int((r_ts - ts).total_seconds() * 1000)
      detection_lat.append(lat_ms)
      writer.writerow([n, 'detection', ts.isoformat(), r_ts.isoformat(), lat_ms, addr])
    else:  # 'off'
      match = find_next(radio, ts, 'disappeared')
      if match is None:
        failed_tear += 1
        writer.writerow([n, 'teardown', ts.isoformat(), '', '', ''])
        continue
      r_ts, _, addr = match
      lat_ms = int((r_ts - ts).total_seconds() * 1000)
      teardown_lat.append(lat_ms)
      writer.writerow([n, 'teardown', ts.isoformat(), r_ts.isoformat(), lat_ms, addr])

  print()
  print(f"cycles={len(cycles)}")
  det_p50 = percentile(detection_lat, 50)
  det_p95 = percentile(detection_lat, 95)
  tear_p50 = percentile(teardown_lat, 50)
  tear_p95 = percentile(teardown_lat, 95)
  print(f"detection_latency_ms_p50={det_p50 if det_p50 is not None else 'n/a'}, "
        f"p95={det_p95 if det_p95 is not None else 'n/a'}")
  print(f"teardown_latency_ms_p50={tear_p50 if tear_p50 is not None else 'n/a'}, "
        f"p95={tear_p95 if tear_p95 is not None else 'n/a'}")
  print(f"failed_detections={failed_det}")
  print(f"failed_teardowns={failed_tear}")
  return 0


if __name__ == '__main__':
  sys.exit(main())
