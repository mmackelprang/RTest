#!/usr/bin/env python3
"""bt_stall_compare.py — compare baseline vs post-change classified BT stalls.

Inputs:
  argv[1]  baseline classified-event artifact (output of sysload_correlate.py)
  argv[2]  after classified-event artifact (same format)

Computes per-class counts and PASS/FAIL against the success criterion from
docs/plans/2026-05-22-bt-capture-watchdog.md Task 9:

  - quiet_host events drop to <= 1 over the soak window
  - load_correlated events do not regress (delta <= 0 OR within tolerance)

Outputs a brief report to stdout with per-class deltas and a final PASS/FAIL.
"""

from __future__ import annotations

import csv
import sys


def load(path: str):
  rows = []
  with open(path, newline="", encoding="utf-8") as f:
    reader = csv.reader(f, delimiter="\t")
    header = next(reader, None)
    if header is None:
      return rows
    for parts in reader:
      if len(parts) < 7:
        continue
      rows.append({
        "event_ts": parts[0],
        "metric_value": parts[1],
        "cpu_5s_median": float(parts[2] or 0),
        "io_5s_total_mb": float(parts[3] or 0),
        "log_rate_5s_median": float(parts[4] or 0),
        "ssh_5s_max": int(parts[5] or 0),
        "classification": parts[6],
      })
  return rows


def summarize(rows):
  quiet = sum(1 for r in rows if r["classification"] == "quiet_host")
  load = sum(1 for r in rows if r["classification"] == "load_correlated")
  return quiet, load


def main() -> int:
  if len(sys.argv) < 3:
    print(f"Usage: {sys.argv[0]} <baseline.tsv> <after.tsv>", file=sys.stderr)
    return 2

  baseline = load(sys.argv[1])
  after = load(sys.argv[2])

  b_quiet, b_load = summarize(baseline)
  a_quiet, a_load = summarize(after)

  d_quiet = a_quiet - b_quiet
  d_load = a_load - b_load

  print(f"baseline: quiet_host={b_quiet}, load_correlated={b_load}, total={b_quiet+b_load}")
  print(f"after:    quiet_host={a_quiet}, load_correlated={a_load}, total={a_quiet+a_load}")
  print(f"delta:    quiet_host={d_quiet:+d}, load_correlated={d_load:+d}")

  # Success criterion: quiet_host <= 1 after AND load_correlated did not regress.
  pass_quiet = a_quiet <= 1
  pass_load = d_load <= 0
  result = "PASS" if (pass_quiet and pass_load) else "FAIL"
  print(f"quiet_host_after_<=1: {pass_quiet}")
  print(f"load_correlated_not_regressed: {pass_load}")
  print(result)
  return 0 if result == "PASS" else 1


if __name__ == "__main__":
  sys.exit(main())
