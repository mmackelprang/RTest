#!/usr/bin/env python3
"""bt_autoswitch_audit.py — Audit BT auto-switch behaviour from journalctl logs.

Reads journalctl text on stdin (or a file passed as argv[1]) and counts:

  a) `GetOrCreateSourceAsync(Bluetooth` invocations  (autoswitch attempts)
  b) `GetAudioCaptureDeviceAsync` retries             (capture acquisition attempts)
  c) `waiting for PW node` log lines                  (the "PipeWire BT node not found" log
     line emitted by LinuxBluetoothService at retry time — substring match accepts the
     exact production log "PipeWire BT node not found for ... (attempt n/m)")
  d) wall-clock duration spent in retry loops, in hours, computed from the
     timestamp of the first to the last "waiting" line per session (best-effort:
     parses journalctl's default `MMM DD HH:MM:SS` and short ISO timestamps).

Output (key=value, one per line):

  switches=<N>
  getcapture_invocations=<M>
  waiting_log_lines=<L>
  retry_loop_hours=<T>

Usage:
  journalctl -u radio-api --since '24 hours ago' -o cat | bt_autoswitch_audit.py
  bt_autoswitch_audit.py /path/to/journal.txt
"""

from __future__ import annotations

import re
import sys
from datetime import datetime, timezone
from typing import Iterable, Optional


# Substring patterns matched against each log line.
SWITCH_PATTERN = "GetOrCreateSourceAsync(Bluetooth"
# GetAudioCaptureDeviceAsync entry log + retry/probe log lines. We match the
# method name itself to count every invocation/retry.
GETCAPTURE_PATTERN = "GetAudioCaptureDeviceAsync"
# The production log line emitted by LinuxBluetoothService when the PW BT node
# is missing during retry. We also accept the plan's shorthand "waiting for PW node".
WAITING_PATTERNS = (
  "PipeWire BT node not found",
  "waiting for PW node",
)


# Common journalctl timestamp formats.
# Examples this matches (anchored at start of line):
#   "May 22 14:37:01"
#   "2026-05-22T14:37:01"
#   "2026-05-22 14:37:01"
_TIMESTAMP_REGEXES = (
  # Full ISO with optional T separator and optional fractional seconds + TZ
  re.compile(r"^(\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2})"),
  # Short syslog: "MMM dd HH:MM:SS"
  re.compile(r"^([A-Za-z]{3} +\d{1,2} \d{2}:\d{2}:\d{2})"),
)


def parse_timestamp(line: str) -> Optional[datetime]:
  """Best-effort timestamp parse. Returns naive UTC or None."""
  for rx in _TIMESTAMP_REGEXES:
    m = rx.match(line)
    if not m:
      continue
    raw = m.group(1).replace("T", " ")
    # Try ISO first
    try:
      return datetime.fromisoformat(raw)
    except ValueError:
      pass
    # Try syslog format. Year is missing — assume current year.
    try:
      now = datetime.now(timezone.utc)
      with_year = f"{now.year} {raw}"
      return datetime.strptime(with_year, "%Y %b %d %H:%M:%S")
    except ValueError:
      continue
  return None


def audit(lines: Iterable[str]) -> dict[str, float]:
  switches = 0
  getcapture = 0
  waiting = 0
  first_waiting: Optional[datetime] = None
  last_waiting: Optional[datetime] = None

  for line in lines:
    if SWITCH_PATTERN in line:
      switches += 1
    if GETCAPTURE_PATTERN in line:
      getcapture += 1
    if any(p in line for p in WAITING_PATTERNS):
      waiting += 1
      ts = parse_timestamp(line)
      if ts is not None:
        if first_waiting is None:
          first_waiting = ts
        last_waiting = ts

  retry_hours = 0.0
  if first_waiting is not None and last_waiting is not None:
    retry_hours = max(0.0, (last_waiting - first_waiting).total_seconds() / 3600.0)

  return {
    "switches": switches,
    "getcapture_invocations": getcapture,
    "waiting_log_lines": waiting,
    "retry_loop_hours": round(retry_hours, 4),
  }


def main(argv: list[str]) -> int:
  if len(argv) > 1 and argv[1] not in ("-", "/dev/stdin"):
    with open(argv[1], "r", encoding="utf-8", errors="replace") as fh:
      result = audit(fh)
  else:
    result = audit(sys.stdin)

  for k, v in result.items():
    print(f"{k}={v}")
  return 0


if __name__ == "__main__":
  raise SystemExit(main(sys.argv))
