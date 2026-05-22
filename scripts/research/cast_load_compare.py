#!/usr/bin/env python3
"""cast_load_compare.py — compare baseline vs post-change Cast probe artifacts
under both light and heavy load scenarios.

Computes:
  - Per-scenario silence_events/h and bufferAhead p5 deltas (after vs baseline)
  - Cross-scenario gap (heavy minus light) before and after — verifies the
    Plan-D acceptance criterion that the gap shrinks
  - PASS/FAIL against the §7 Idea #9 success criterion in
    docs/research/2026-05-21-cast-stutter-comparison.md

Inputs (positional):
  argv[1]  baseline_light artifact   — PROBE-CAST-AUDIO summary
  argv[2]  baseline_heavy artifact
  argv[3]  after_light artifact
  argv[4]  after_heavy artifact

Each input file is the stdout of either:
  - scripts/research/cast_audio_glitch.py (silence events per hour summary)
  - scripts/research/cast_dc_buffer_summarize.py (bufferAhead p5 / p50 / p95)
  - a merged artifact containing both

Expected format: simple `key: value` lines, one per row. Recognised keys:
  silence_events_per_hour, buffer_ahead_p5_s, buffer_ahead_p50_s,
  buffer_ahead_p95_s, radio_api_cpu_pct_cores_2_3, radio_web_cpu_pct_cores_0_1.
Unknown keys are ignored (the harness may append other diagnostic lines).

Success criterion (PASS requires all four):
  - Light load: after silence_events/h <= baseline_light + 1
  - Light load: after bufferAhead_p5 >= baseline_light - 0.5
  - Heavy load: after silence_events/h drops by >= 80 % vs baseline_heavy
  - Cross-scenario gap (heavy - light) on silence_events/h drops to <= +2

Reuses the bt_stall_compare.py output style (per-line metric + final
PASS/FAIL token on its own line).
"""

from __future__ import annotations

import sys
from typing import Optional


RECOGNISED_KEYS = {
  "silence_events_per_hour",
  "buffer_ahead_p5_s",
  "buffer_ahead_p50_s",
  "buffer_ahead_p95_s",
  "radio_api_cpu_pct_cores_2_3",
  "radio_web_cpu_pct_cores_0_1",
}


def load(path: str) -> dict[str, float]:
  values: dict[str, float] = {}
  with open(path, encoding="utf-8") as f:
    for line in f:
      line = line.strip()
      if not line or line.startswith("#"):
        continue
      if ":" not in line:
        continue
      key, _, rest = line.partition(":")
      key = key.strip()
      if key not in RECOGNISED_KEYS:
        continue
      try:
        values[key] = float(rest.strip().split()[0])
      except (ValueError, IndexError):
        continue
  return values


def _get(d: dict[str, float], k: str) -> Optional[float]:
  return d.get(k)


def _fmt(v: Optional[float]) -> str:
  return "n/a" if v is None else f"{v:.3f}"


def main() -> int:
  if len(sys.argv) < 5:
    print(
      f"Usage: {sys.argv[0]} <baseline_light.txt> <baseline_heavy.txt> "
      "<after_light.txt> <after_heavy.txt>",
      file=sys.stderr,
    )
    return 2

  bl = load(sys.argv[1])
  bh = load(sys.argv[2])
  al = load(sys.argv[3])
  ah = load(sys.argv[4])

  # Per-scenario silence_events/h
  bl_sev = _get(bl, "silence_events_per_hour")
  bh_sev = _get(bh, "silence_events_per_hour")
  al_sev = _get(al, "silence_events_per_hour")
  ah_sev = _get(ah, "silence_events_per_hour")

  # Per-scenario bufferAhead p5
  bl_p5 = _get(bl, "buffer_ahead_p5_s")
  al_p5 = _get(al, "buffer_ahead_p5_s")
  ah_p5 = _get(ah, "buffer_ahead_p5_s")

  print("== silence_events_per_hour ==")
  print(f"baseline_light: {_fmt(bl_sev)}")
  print(f"baseline_heavy: {_fmt(bh_sev)}")
  print(f"after_light:    {_fmt(al_sev)}")
  print(f"after_heavy:    {_fmt(ah_sev)}")

  print()
  print("== buffer_ahead_p5_s ==")
  print(f"baseline_light: {_fmt(bl_p5)}")
  print(f"after_light:    {_fmt(al_p5)}")
  print(f"after_heavy:    {_fmt(ah_p5)}")

  # Cross-scenario gap (heavy - light) on silence_events/h
  base_gap = (bh_sev - bl_sev) if bl_sev is not None and bh_sev is not None else None
  after_gap = (ah_sev - al_sev) if al_sev is not None and ah_sev is not None else None
  print()
  print("== cross-scenario gap (heavy - light) silence_events/h ==")
  print(f"baseline_gap: {_fmt(base_gap)}")
  print(f"after_gap:    {_fmt(after_gap)}")

  # Affinity-verification fields (post-change only)
  api_aff = _get(ah, "radio_api_cpu_pct_cores_2_3")
  web_aff = _get(ah, "radio_web_cpu_pct_cores_0_1")
  print()
  print("== affinity verification (heavy scenario, post-change) ==")
  print(f"radio_api_cpu_pct_cores_2_3: {_fmt(api_aff)}")
  print(f"radio_web_cpu_pct_cores_0_1: {_fmt(web_aff)}")

  # Pass criteria
  pass_light_silence = (
    al_sev is not None and bl_sev is not None and al_sev <= bl_sev + 1.0
  )
  pass_light_buffer = (
    al_p5 is not None and bl_p5 is not None and al_p5 >= bl_p5 - 0.5
  )
  pass_heavy_silence = (
    ah_sev is not None
    and bh_sev is not None
    and bh_sev > 0
    and ah_sev <= bh_sev * 0.2
  )
  pass_gap = after_gap is not None and after_gap <= 2.0

  print()
  print("== pass criteria ==")
  print(f"light_silence_no_regress (<=+1):        {pass_light_silence}")
  print(f"light_buffer_p5_no_regress (>=-0.5s):   {pass_light_buffer}")
  print(f"heavy_silence_drops_>=80pct:            {pass_heavy_silence}")
  print(f"cross_scenario_gap_<=2_per_hour:        {pass_gap}")

  ok = pass_light_silence and pass_light_buffer and pass_heavy_silence and pass_gap
  print()
  print("PASS" if ok else "FAIL")
  return 0 if ok else 1


if __name__ == "__main__":
  sys.exit(main())
