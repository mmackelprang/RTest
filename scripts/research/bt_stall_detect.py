#!/usr/bin/env python3
"""bt_stall_detect.py — detect BT capture OnProcess stalls in journald output.

Reads journalctl text on stdin. Identifies gaps where the
"PipeWire OnProcess: ..." log line has been silent for >= --window seconds
despite the most recent "BluetoothAudioSource" state-line indicating an active
BT capture (Playing, Ready, or capture-active variants).

Output (stdout, one event per line, tab-separated):
  start_ts<TAB>end_ts<TAB>gap_seconds

Notes:
  - The OnProcess log line is emitted every ~10s when capture is healthy (see
    PipeWireNativeStream.cs OnProcess stat logging window).
  - Default --window is 60s, which catches single-emission misses while
    tolerating brief transient gaps; tune to the source data.
  - Timestamps are taken from the journalctl line prefix; use
    `--output=short-iso` or `--output=short-iso-precise` for ISO timestamps.

Used by the BT capture watchdog (Plan A) acceptance criteria and by the
Phase 1 baseline-vs-after comparison (bt_stall_compare.py).
"""

from __future__ import annotations

import argparse
import re
import sys
from datetime import datetime, timezone

# journalctl --output=short-iso prefix: "2026-05-22T12:34:56+0000"
ISO_TS_RE = re.compile(r"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:[+-]\d{2}:?\d{2}|Z)?)")
ON_PROCESS_RE = re.compile(r"PipeWire OnProcess", re.IGNORECASE)
BT_PLAYING_RE = re.compile(r"BluetoothAudioSource.*(state\s*==\s*Playing|state\s*=\s*Playing|"
                           r"capture (bridge|generator|stream).*(active|acquired|added))",
                           re.IGNORECASE)


def parse_ts(line: str):
  m = ISO_TS_RE.search(line)
  if not m:
    return None
  s = m.group(1)
  if s.endswith("Z"):
    s = s[:-1] + "+00:00"
  # Normalize "+0000" → "+00:00" for fromisoformat
  if re.search(r"[+-]\d{4}$", s):
    s = s[:-2] + ":" + s[-2:]
  try:
    dt = datetime.fromisoformat(s)
  except ValueError:
    return None
  if dt.tzinfo is None:
    dt = dt.replace(tzinfo=timezone.utc)
  return dt.astimezone(timezone.utc)


def main() -> int:
  ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
  ap.add_argument("--window", type=float, default=60.0,
                  help="minimum gap between OnProcess emissions to flag (s)")
  args = ap.parse_args()

  last_on_process: datetime | None = None
  bt_active: bool = False
  events = []

  for line in sys.stdin:
    ts = parse_ts(line)
    if ts is None:
      continue
    if BT_PLAYING_RE.search(line):
      bt_active = True
    if "BluetoothAudioSource" in line and "Stopped" in line:
      bt_active = False
      last_on_process = None
      continue
    if ON_PROCESS_RE.search(line):
      if last_on_process is not None and bt_active:
        gap = (ts - last_on_process).total_seconds()
        if gap >= args.window:
          events.append((last_on_process, ts, gap))
      last_on_process = ts

  for start, end, gap in events:
    sys.stdout.write(
      f"{start.isoformat().replace('+00:00','Z')}\t"
      f"{end.isoformat().replace('+00:00','Z')}\t{gap:.2f}\n"
    )

  return 0


if __name__ == "__main__":
  sys.exit(main())
