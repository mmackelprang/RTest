#!/usr/bin/env python3
"""bt_drift_compare.py — PASS/FAIL gate for Path C drift-compensation refinement.

Inputs (positional):
  1. baseline_drift   — output of bt_drift_analyze.py BEFORE the refinement
  2. after_drift      — output of bt_drift_analyze.py AFTER the refinement
  3. baseline_metrics — sqlite3 pipe-delimited "key|sum" rows BEFORE
  4. after_metrics    — same, AFTER

The metric snapshots come from:

  ssh mmack@radio "sqlite3 /opt/radio-console/data/metrics.db \\
    \"SELECT md.Key, SUM(mm.ValueSum) FROM MetricData_Minute mm \\
     JOIN MetricDefinitions md ON mm.MetricId=md.Id \\
     WHERE md.Key LIKE 'audio.buffer.%' \\
       AND mm.Timestamp > strftime('%s','now','-15 minutes')*1000 \\
     GROUP BY md.Key;\""

Success criteria (objective only — subjective UAT is operator-run):
  - C2.events:   after.comp_events_per_hour / baseline.comp_events_per_hour >= 3.0
                 (smaller-more-frequent: many more events at ~1/5th the size)
  - C2.samples:  after.comp_samples_total / baseline.comp_samples_total in [0.8, 1.2]
                 (same total compensation, redistributed across more events)
  - C3.underrun: after.underrun_events_per_hour / baseline.underrun_events_per_hour <= 0.5
                 (smaller-more-frequent compensation should hold the buffer further
                 from zero on average)

Exit 0 on PASS, 1 on FAIL. The subjective "underwater" UAT is reported as
informational only — the operator is the authority on that criterion.

Plan reference: docs/plans/2026-05-22-bt-drift-compensation-refinement.md §5.
"""

from __future__ import annotations

import argparse
import re
import sys

# Lines we parse out of bt_drift_analyze.py:
#   Underrun events:           13     (rate: 52.6/hour, 36000 samples/hour)
#   Compensation events:       10     (rate: 40.4/hour, 39000 samples/hour)
UNDERRUN_LINE_RE = re.compile(
    r"Underrun events:\s+(\d+)\s+\(rate:\s+([\d.]+)/hour,\s+([\d.]+)\s+samples/hour\)"
)
COMP_LINE_RE = re.compile(
    r"Compensation events:\s+(\d+)\s+\(rate:\s+([\d.]+)/hour,\s+([\d.]+)\s+samples/hour\)"
)


def parse_drift_artifact(path: str) -> dict[str, float]:
    """Return a dict with keys:
      underrun_events, underrun_events_per_hour, underrun_samples_per_hour,
      comp_events, comp_events_per_hour, comp_samples_per_hour
    """
    out = {
        "underrun_events": 0.0,
        "underrun_events_per_hour": 0.0,
        "underrun_samples_per_hour": 0.0,
        "comp_events": 0.0,
        "comp_events_per_hour": 0.0,
        "comp_samples_per_hour": 0.0,
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
    return out


def parse_metrics_artifact(path: str) -> dict[str, float]:
    """Parse pipe-delimited 'key|sum' rows from sqlite output."""
    out: dict[str, float] = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or "|" not in line:
                continue
            key, _, value = line.partition("|")
            key = key.strip()
            value = value.strip()
            if not key:
                continue
            try:
                out[key] = float(value)
            except ValueError:
                continue
    return out


def ratio(after: float, before: float, floor: float = 0.1) -> float:
    """Return `after / max(before, floor)` — `floor` guards divide-by-near-zero."""
    return after / max(before, floor)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Compare baseline vs post-change drift metrics for Path C acceptance."
    )
    parser.add_argument("baseline_drift", help="bt_drift_analyze.py output, baseline")
    parser.add_argument("after_drift", help="bt_drift_analyze.py output, after refinement")
    parser.add_argument("baseline_metrics", help="sqlite key|sum dump, baseline")
    parser.add_argument("after_metrics", help="sqlite key|sum dump, after refinement")
    args = parser.parse_args()

    base_d = parse_drift_artifact(args.baseline_drift)
    after_d = parse_drift_artifact(args.after_drift)
    base_m = parse_metrics_artifact(args.baseline_metrics)
    after_m = parse_metrics_artifact(args.after_metrics)

    # --- Criterion C2.events: comp events/hour >= 3x baseline ---
    comp_ratio = ratio(
        after_d["comp_events_per_hour"], base_d["comp_events_per_hour"], floor=0.1
    )
    c2_events_pass = comp_ratio >= 3.0

    # --- Criterion C2.samples: comp samples/hour stays within +/-20% ---
    # Prefer the metric-derived value (cumulative counter) over the log-derived
    # value because the counter is exact and the log-derived value can be off
    # if the log throttle drops events.
    base_samples = base_m.get(
        "audio.buffer.drift_compensation_samples_total",
        base_d["comp_samples_per_hour"],
    )
    after_samples = after_m.get(
        "audio.buffer.drift_compensation_samples_total",
        after_d["comp_samples_per_hour"],
    )
    sample_ratio = ratio(after_samples, base_samples, floor=1.0)
    c2_samples_pass = 0.8 <= sample_ratio <= 1.2

    # --- Criterion C3: underrun events/hour drops by >=50% (ratio <= 0.5) ---
    underrun_ratio = ratio(
        after_d["underrun_events_per_hour"],
        base_d["underrun_events_per_hour"],
        floor=0.1,
    )
    c3_pass = underrun_ratio <= 0.5

    overall_pass = c2_events_pass and c2_samples_pass and c3_pass

    print("=== Path C objective acceptance ===")
    print(f"baseline window comp events/hour:     {base_d['comp_events_per_hour']:.1f}")
    print(f"after window comp events/hour:        {after_d['comp_events_per_hour']:.1f}")
    print(f"baseline window underrun events/hour: {base_d['underrun_events_per_hour']:.1f}")
    print(f"after window underrun events/hour:    {after_d['underrun_events_per_hour']:.1f}")
    print(f"baseline comp samples (metric/log):   {base_samples:.0f}")
    print(f"after comp samples (metric/log):      {after_samples:.0f}")
    print()
    print(
        f"C2.events  comp ratio:    {comp_ratio:6.2f}x  "
        f"(target >= 3.0): {'PASS' if c2_events_pass else 'FAIL'}"
    )
    print(
        f"C2.samples sample ratio:  {sample_ratio:6.2f}x  "
        f"(target 0.80-1.20): {'PASS' if c2_samples_pass else 'FAIL'}"
    )
    print(
        f"C3         underrun ratio:{underrun_ratio:6.2f}x  "
        f"(target <= 0.50): {'PASS' if c3_pass else 'FAIL'}"
    )
    print()
    print(f"OVERALL (objective):     {'PASS' if overall_pass else 'FAIL'}")
    print()
    print("Subjective (audible 'underwater' artifact gone): Mark UAT")
    print(
        "  If objective PASS but subjective FAIL: Path D (real variable-rate "
        "resampler) is the next plan."
    )
    return 0 if overall_pass else 1


if __name__ == "__main__":
    sys.exit(main())
