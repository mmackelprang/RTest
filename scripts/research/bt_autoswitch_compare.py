#!/usr/bin/env python3
"""bt_autoswitch_compare.py — Compare two bt_autoswitch_audit.py artifacts.

Reads a baseline and an "after" artifact (both in key=value form, as produced
by bt_autoswitch_audit.py) and produces:

  * per-metric deltas (after - baseline)
  * PASS/FAIL against the success criteria documented in plan task 8.

Success criteria (per the plan):
  * waiting_log_lines per cycle ≤ 5 — interpreted as "after" ≤ 5 * baseline cycles
    is fragile, so we adopt the simpler concrete rule:
      after.waiting_log_lines must be ≤ 5 * EXPECTED_CYCLES (default 48)
      AND after.waiting_log_lines must be < baseline.waiting_log_lines
  * retry_loop_hours must drop to 0
  * getcapture_invocations must NOT regress on happy path — i.e., if both runs
    used the same number of cycles, after.getcapture_invocations must be
    ≤ 2 * EXPECTED_CYCLES + baseline.getcapture_invocations * 0.1 tolerance.

Usage:
  bt_autoswitch_compare.py baseline.txt after.txt [--expected-cycles N]
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path


def parse_artifact(path: Path) -> dict[str, float]:
  out: dict[str, float] = {}
  for raw_line in path.read_text(encoding="utf-8", errors="replace").splitlines():
    line = raw_line.strip()
    if not line or "=" not in line:
      continue
    key, _, val = line.partition("=")
    key = key.strip()
    val = val.strip()
    try:
      out[key] = float(val)
    except ValueError:
      # Skip lines that aren't numeric — keep this tolerant
      continue
  return out


def main(argv: list[str]) -> int:
  parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
  parser.add_argument("baseline", type=Path)
  parser.add_argument("after", type=Path)
  parser.add_argument(
    "--expected-cycles",
    type=int,
    default=48,
    help="Number of pair/unpair cycles the harness ran (default: 48, matches plan task 8)",
  )
  args = parser.parse_args(argv[1:])

  base = parse_artifact(args.baseline)
  after = parse_artifact(args.after)

  print("Metric                      Baseline    After       Delta")
  print("-" * 60)
  keys = ["switches", "getcapture_invocations", "waiting_log_lines", "retry_loop_hours"]
  for k in keys:
    b = base.get(k, 0.0)
    a = after.get(k, 0.0)
    delta = a - b
    print(f"{k:<28}{b:<12}{a:<12}{delta:+}")

  # PASS/FAIL evaluation
  failures: list[str] = []

  # waiting_log_lines: must drop dramatically. Allow up to 5 per cycle.
  waiting_budget = 5 * args.expected_cycles
  after_waiting = after.get("waiting_log_lines", 0)
  base_waiting = base.get("waiting_log_lines", 0)
  if after_waiting > waiting_budget:
    failures.append(
      f"waiting_log_lines={after_waiting} exceeds budget of "
      f"{waiting_budget} (5 × {args.expected_cycles} cycles)"
    )
  if base_waiting > 0 and after_waiting >= base_waiting:
    failures.append(
      f"waiting_log_lines did not decrease: baseline={base_waiting} after={after_waiting}"
    )

  # retry_loop_hours: must be 0 (or very near it — allow 0.05 h ≈ 3 min tolerance for clock skew)
  after_retry = after.get("retry_loop_hours", 0.0)
  if after_retry > 0.05:
    failures.append(f"retry_loop_hours={after_retry} > 0.05 h tolerance")

  # getcapture_invocations: no happy-path regression
  base_getcap = base.get("getcapture_invocations", 0)
  after_getcap = after.get("getcapture_invocations", 0)
  getcap_budget = 2 * args.expected_cycles + max(0.0, base_getcap * 0.1)
  if after_getcap > getcap_budget:
    failures.append(
      f"getcapture_invocations={after_getcap} exceeds happy-path budget of "
      f"{getcap_budget:.0f} (2 × {args.expected_cycles} + 10% tolerance)"
    )

  print()
  if failures:
    print("FAIL")
    for f in failures:
      print(f"  - {f}")
    return 1

  print("PASS")
  return 0


if __name__ == "__main__":
  raise SystemExit(main(sys.argv))
