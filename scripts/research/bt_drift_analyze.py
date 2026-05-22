#!/usr/bin/env python3
"""bt_drift_analyze.py — quantify BufferedSoundGenerator underrun + clock-drift compensation.

Reads journald text from stdin (or --input <file>) and emits a single summary
block with underrun + drift-compensation event rates plus an estimated clock-skew
in parts-per-million.

Matches two journal line shapes produced by
src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs:

  ⚠️ Buffer underrun (<Type>): <N> underruns, <M> zero samples in last <T>s
    (buffer: <buffered>/<capacity>, total underruns: <total>)

  🔄 Clock drift compensation (<Type>): <N> events, <M> duplicated samples in
    last <T>s (buffer: <level>→<new>/<capacity>, total compensated: <total>)

Both throttle to once per ~5 s in production, so the per-burst <N>/<M>/<T> values
are summed across the input window. The "total" field is sanity-checked against
the running sum.

PPM math (see notes in `--help`): with stereo audio, the effective sample-rate
is `sample_rate_hz * channels`. The net buffer deficit rate is
`(net_deficit_samples / window_seconds)`, and the ppm estimate is
`deficit_per_sec / effective_sample_rate * 1e6`.

Usage:
  journalctl -u radio-api --since "1 hour ago" | python3 bt_drift_analyze.py
  python3 bt_drift_analyze.py --input radio_journal.log --sample-rate 48000 --channels 2
"""

from __future__ import annotations

import argparse
import re
import sys
from datetime import datetime

# journalctl --output=short-iso prefix: "2026-05-22T12:34:56+0000"
ISO_TS_RE = re.compile(
    r"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:[+-]\d{2}:?\d{2}|Z)?)"
)

# "Buffer underrun (Single): 5 underruns, 2048 zero samples in last 4.8s ... total underruns: 82)"
UNDERRUN_RE = re.compile(
    r"Buffer underrun \([^)]+\):\s+(\d+)\s+underruns,\s+(\d+)\s+zero samples in last\s+([\d.]+)s.*?"
    r"total underruns:\s+(\d+)",
    re.IGNORECASE,
)

# Two compensation shapes we accept:
#  (new, throttled)  "Clock drift compensation (Single): 3 events, 480 duplicated samples in last 5.1s ... total compensated: 12480"
#  (old, single-line) "Clock drift compensation: duplicated 160 samples (buffer: ... total compensated: 1280)"
COMP_BURST_RE = re.compile(
    r"Clock drift compensation \([^)]+\):\s+(\d+)\s+events,\s+(\d+)\s+duplicated samples in last\s+([\d.]+)s.*?"
    r"total compensated:\s+(\d+)",
    re.IGNORECASE,
)
COMP_SINGLE_RE = re.compile(
    r"Clock drift compensation:\s+duplicated\s+(\d+)\s+samples.*?total compensated:\s+(\d+)",
    re.IGNORECASE,
)


def parse_ts(line: str):
  m = ISO_TS_RE.search(line)
  if not m:
    return None
  raw = m.group(1).replace("Z", "+00:00")
  try:
    return datetime.fromisoformat(raw)
  except ValueError:
    return None


def main() -> int:
  ap = argparse.ArgumentParser(
      description="Summarize underrun + drift-compensation events from radio-api journal text",
  )
  ap.add_argument("--input", type=str, default=None,
                  help="Path to journal text file (default: stdin)")
  ap.add_argument("--sample-rate", type=int, default=48000,
                  help="Audio sample rate in Hz (default: 48000)")
  ap.add_argument("--channels", type=int, default=2,
                  help="Channel count for effective sample-rate math (default: 2)")
  args = ap.parse_args()

  src = open(args.input, "r", encoding="utf-8", errors="replace") if args.input else sys.stdin

  underrun_events = 0
  underrun_samples = 0
  underrun_total_seen = 0
  comp_events = 0
  comp_samples = 0
  comp_total_seen = 0
  first_ts = None
  last_ts = None

  for line in src:
    ts = parse_ts(line)
    if ts is not None:
      if first_ts is None:
        first_ts = ts
      last_ts = ts

    m = UNDERRUN_RE.search(line)
    if m:
      underrun_events += int(m.group(1))
      underrun_samples += int(m.group(2))
      underrun_total_seen = max(underrun_total_seen, int(m.group(4)))
      continue

    m = COMP_BURST_RE.search(line)
    if m:
      comp_events += int(m.group(1))
      comp_samples += int(m.group(2))
      comp_total_seen = max(comp_total_seen, int(m.group(4)))
      continue

    m = COMP_SINGLE_RE.search(line)
    if m:
      # Pre-throttle / legacy compensation log: one event per line.
      comp_events += 1
      comp_samples += int(m.group(1))
      comp_total_seen = max(comp_total_seen, int(m.group(2)))

  if args.input:
    src.close()

  if first_ts is None or last_ts is None or first_ts == last_ts:
    print("ERROR: no parseable timestamps in input (use journalctl --output=short-iso)",
          file=sys.stderr)
    return 1

  window_secs = (last_ts - first_ts).total_seconds()
  window_hours = window_secs / 3600.0

  underrun_rate_hr = underrun_events / window_hours if window_hours > 0 else 0
  underrun_samples_rate_hr = underrun_samples / window_hours if window_hours > 0 else 0
  comp_rate_hr = comp_events / window_hours if window_hours > 0 else 0
  comp_samples_rate_hr = comp_samples / window_hours if window_hours > 0 else 0

  # Net buffer-deficit rate: compensation samples are duplicated to PREVENT
  # underrun, so on a perfectly tracked clock the system would have run dry by
  # exactly `comp_samples - underrun_samples` (compensated samples patched the
  # gap; underrun samples are zero-fill where compensation couldn't keep up).
  # We treat compensation_samples + underrun_samples as the total samples
  # the consumer would otherwise have starved for.
  total_deficit_samples = comp_samples + underrun_samples
  deficit_per_sec = total_deficit_samples / window_secs if window_secs > 0 else 0
  effective_rate = args.sample_rate * args.channels
  ppm = (deficit_per_sec / effective_rate) * 1e6 if effective_rate > 0 else 0

  # Format duration as "<H>h <M>m"
  dur_h = int(window_secs // 3600)
  dur_m = int((window_secs % 3600) // 60)

  print(f"Window: {first_ts.isoformat()} -> {last_ts.isoformat()}  "
        f"(duration: {dur_h}h {dur_m}m, {window_secs:.0f}s)")
  print(f"Underrun events:           {underrun_events:<6d}  "
        f"(rate: {underrun_rate_hr:.1f}/hour, {underrun_samples_rate_hr:.0f} samples/hour)")
  print(f"  cumulative-total seen:   {underrun_total_seen}")
  print(f"Compensation events:       {comp_events:<6d}  "
        f"(rate: {comp_rate_hr:.1f}/hour, {comp_samples_rate_hr:.0f} samples/hour)")
  print(f"  cumulative-total seen:   {comp_total_seen}")
  print(f"Net buffer deficit:        {total_deficit_samples} samples "
        f"({deficit_per_sec:.2f} samples/sec)")
  print(f"Estimated clock skew:      {ppm:.1f} ppm "
        f"(assuming effective_rate = {effective_rate} Hz "
        f"= {args.sample_rate} Hz × {args.channels} ch)")
  return 0


if __name__ == "__main__":
  sys.exit(main())
