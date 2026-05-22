#!/usr/bin/env python3
"""bt_lifecycle_compare.py — PASS/FAIL gate for Plan E lifecycle latency.

Inputs:
  argv[1]  baseline classified artifact (output of bt_lifecycle_summarize.py)
  argv[2]  after classified artifact (same format)

Success criterion (Plan E §7, docs/plans/2026-05-22-pw-event-subscription.md):
  - detection_latency_ms_p95 ≤ 200 ms (after)
  - teardown_latency_ms_p95 ≤ 500 ms (after)
  - failed_detections == 0 (after)
  - failed_teardowns == 0 (after)

Reports per-metric PASS/FAIL plus an overall PASS/FAIL. Exit 0 on PASS, 1 on FAIL.
"""

from __future__ import annotations

import re
import sys

KEY_RE = re.compile(r'^(?P<key>[a-z_0-9]+)=(?P<value>.*)$')


def parse_summary(path: str) -> dict[str, str]:
  """Read the trailing 'key=value' summary block emitted by bt_lifecycle_summarize.py."""
  out: dict[str, str] = {}
  with open(path, encoding='utf-8') as f:
    for line in f:
      line = line.strip()
      if not line:
        continue
      # Treat lines with both 'p50=' and 'p95=' as compound (split on ', ').
      if 'p95=' in line:
        for part in [p.strip() for p in line.split(',')]:
          m = KEY_RE.match(part)
          if m:
            out[m.group('key')] = m.group('value').strip()
        continue
      m = KEY_RE.match(line)
      if m:
        out[m.group('key')] = m.group('value').strip()
  return out


def as_float(val: str | None) -> float | None:
  if val is None or val == 'n/a' or val == '':
    return None
  try:
    return float(val)
  except ValueError:
    return None


def as_int(val: str | None) -> int | None:
  if val is None or val == 'n/a' or val == '':
    return None
  try:
    return int(val)
  except ValueError:
    return None


def main() -> int:
  if len(sys.argv) < 3:
    print(f"Usage: {sys.argv[0]} <baseline_classified> <after_classified>", file=sys.stderr)
    return 2

  baseline = parse_summary(sys.argv[1])
  after = parse_summary(sys.argv[2])

  print('Baseline:')
  for k in ('cycles', 'detection_latency_ms_p50', 'detection_latency_ms_p95',
            'teardown_latency_ms_p50', 'teardown_latency_ms_p95',
            'failed_detections', 'failed_teardowns'):
    print(f"  {k}={baseline.get(k, 'n/a')}")
  print('After:')
  for k in ('cycles', 'detection_latency_ms_p50', 'detection_latency_ms_p95',
            'teardown_latency_ms_p50', 'teardown_latency_ms_p95',
            'failed_detections', 'failed_teardowns'):
    print(f"  {k}={after.get(k, 'n/a')}")

  det_p95 = as_float(after.get('detection_latency_ms_p95'))
  tear_p95 = as_float(after.get('teardown_latency_ms_p95'))
  failed_det = as_int(after.get('failed_detections')) or 0
  failed_tear = as_int(after.get('failed_teardowns')) or 0

  checks = {
    'detection_latency_ms_p95_<=_200': det_p95 is not None and det_p95 <= 200,
    'teardown_latency_ms_p95_<=_500': tear_p95 is not None and tear_p95 <= 500,
    'failed_detections_==_0': failed_det == 0,
    'failed_teardowns_==_0': failed_tear == 0,
  }

  print()
  for name, ok in checks.items():
    print(f"{name}: {'PASS' if ok else 'FAIL'}")

  overall = 'PASS' if all(checks.values()) else 'FAIL'
  print()
  print(overall)
  return 0 if overall == 'PASS' else 1


if __name__ == '__main__':
  sys.exit(main())
