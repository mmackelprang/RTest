#!/usr/bin/env python3
"""bt_resampler_compare.py — PASS/FAIL gate for Path D input-resampler.

Path D (docs/plans/2026-05-22-bt-input-resampler.md) routes the BT input
through libsamplerate's variable-rate SRC before BufferedSoundGenerator
sees it. The objective acceptance criteria are much stricter than Path C
because the resampler eliminates rate mismatch at the source — the
legacy time-domain compensation should drive toward zero events/hour:

  - D1: comp_events_per_hour drops by >= 90 % vs. the Path C baseline.
        (Ideally zero; the resampler is now doing the work.)
  - D2: underrun_events_per_hour stays at 0 (no buffer starvation).
  - D3: residual clock skew |ppm| < 50 (BT/speaker spec tolerance plus
        measurement noise; the resampler should remove the bulk of it).

Subjective acceptance ("underwater" artifact gone) is operator-judged
and reported as informational only.

Inputs (positional):
  1. baseline_drift   — bt_drift_analyze.py output, pre-Path-D (i.e.
                        Path C baseline; reuse /tmp/baseline_path_c.txt)
  2. after_drift      — bt_drift_analyze.py output, post-Path-D
                        (15-minute soak per plan Task 8)

Exit 0 on PASS (all D1-D3 pass), 1 on FAIL.

Plan reference: docs/plans/2026-05-22-bt-input-resampler.md §Task 6, §Task 8.
Mirrors the bt_drift_compare.py pattern (Path C acceptance gate).
"""

from __future__ import annotations

import argparse
import re
import sys

# Lines we parse out of bt_drift_analyze.py:
#   Underrun events:           13     (rate: 52.6/hour, 36000 samples/hour)
#   Compensation events:       10     (rate: 40.4/hour, 39000 samples/hour)
#   Estimated clock skew:      217 ppm  (...)
UNDERRUN_LINE_RE = re.compile(
    r"Underrun events:\s+(\d+)\s+\(rate:\s+([\d.]+)/hour,\s+([\d.]+)\s+samples/hour\)"
)
COMP_LINE_RE = re.compile(
    r"Compensation events:\s+(\d+)\s+\(rate:\s+([\d.]+)/hour,\s+([\d.]+)\s+samples/hour\)"
)
PPM_LINE_RE = re.compile(
    r"clock skew:\s+(-?[\d.]+)\s*ppm", re.IGNORECASE
)


def parse_drift_artifact(path: str) -> dict[str, float]:
    """Return a dict of metrics parsed from a bt_drift_analyze.py text dump.

    Keys: underrun_events, underrun_events_per_hour, underrun_samples_per_hour,
          comp_events, comp_events_per_hour, comp_samples_per_hour, ppm.
    Missing values default to 0.0.
    """
    out: dict[str, float] = {
        "underrun_events": 0.0,
        "underrun_events_per_hour": 0.0,
        "underrun_samples_per_hour": 0.0,
        "comp_events": 0.0,
        "comp_events_per_hour": 0.0,
        "comp_samples_per_hour": 0.0,
        "ppm": 0.0,
    }
    with open(path, encoding="utf-8") as f:
        for line in f:
            m = UNDERRUN_LINE_RE.search(line)
            if m:
                out["underrun_events"] = float(m.group(1))
                out["underrun_events_per_hour"] = float(m.group(2))
                out["underrun_samples_per_hour"] = float(m.group(3))
                continue
            m = COMP_LINE_RE.search(line)
            if m:
                out["comp_events"] = float(m.group(1))
                out["comp_events_per_hour"] = float(m.group(2))
                out["comp_samples_per_hour"] = float(m.group(3))
                continue
            m = PPM_LINE_RE.search(line)
            if m:
                out["ppm"] = float(m.group(1))
    return out


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Compare baseline (pre-Path-D / Path-C steady state) vs. post-Path-D "
            "drift metrics. Exit 0 on PASS (D1-D3), 1 on FAIL."
        )
    )
    parser.add_argument("baseline_drift", help="bt_drift_analyze.py output (baseline)")
    parser.add_argument("after_drift", help="bt_drift_analyze.py output (after Path D)")
    args = parser.parse_args()

    base = parse_drift_artifact(args.baseline_drift)
    after = parse_drift_artifact(args.after_drift)

    # --- D1: comp events drop >= 90 % vs baseline ---
    # Guard against divide-by-near-zero (baseline=0 would mean Path C wasn't
    # firing — unexpected per the research, but PASS the check trivially).
    base_comp = base["comp_events_per_hour"]
    after_comp = after["comp_events_per_hour"]
    if base_comp < 1.0:
        comp_drop_ratio = 0.0
        d1_pass = True
    else:
        comp_drop_ratio = after_comp / base_comp
        d1_pass = comp_drop_ratio <= 0.10  # i.e. >= 90 % reduction

    # --- D2: underrun events stay at 0 ---
    d2_pass = after["underrun_events_per_hour"] < 0.5  # < 1/hour rounding tolerance

    # --- D3: residual skew |ppm| < 50 ---
    d3_pass = abs(after["ppm"]) < 50.0

    overall_pass = d1_pass and d2_pass and d3_pass

    print("=== Path D objective acceptance ===")
    print(f"baseline comp events/hour:     {base_comp:.1f}")
    print(f"after    comp events/hour:     {after_comp:.1f}")
    print(f"baseline underrun events/hour: {base['underrun_events_per_hour']:.1f}")
    print(f"after    underrun events/hour: {after['underrun_events_per_hour']:.1f}")
    print(f"baseline clock skew (ppm):     {base['ppm']:.0f}")
    print(f"after    clock skew (ppm):     {after['ppm']:.0f}")
    print()
    print(
        f"D1 comp reduction ratio:  {comp_drop_ratio:6.3f}  "
        f"(target <= 0.10, i.e. >= 90% drop): {'PASS' if d1_pass else 'FAIL'}"
    )
    print(
        f"D2 underrun events/hour:  {after['underrun_events_per_hour']:6.1f}  "
        f"(target = 0): {'PASS' if d2_pass else 'FAIL'}"
    )
    print(
        f"D3 residual |ppm|:        {abs(after['ppm']):6.0f}  "
        f"(target < 50): {'PASS' if d3_pass else 'FAIL'}"
    )
    print()
    print(f"OVERALL (objective):     {'PASS' if overall_pass else 'FAIL'}")
    print()
    print("Subjective ('underwater' artifact gone): Mark UAT")
    print(
        "  If objective PASS but subjective FAIL: investigate quality-mode "
        "(SincMedium / SincBest) or kick off Phase 2 closed-loop ratio control."
    )
    return 0 if overall_pass else 1


if __name__ == "__main__":
    sys.exit(main())
