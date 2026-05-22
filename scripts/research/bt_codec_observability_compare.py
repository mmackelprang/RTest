#!/usr/bin/env python3
"""bt_codec_observability_compare.py

Phase 1 / Plan C — pass/fail evaluator for the codec observability probe.

Reads two artifacts produced by `bt_codec_observability_probe.sh` (baseline +
after) and decides whether the after-change run satisfies the acceptance
criterion from the plan's Task 8:

  * events_emitted >= 3 in the after artifact
  * codec_log_lines >= 3 with at least 2 phones reporting a parseable codec
    name from {sbc, aac, aptx, aptx-hd, ldac, lhdc, vendor}
  * ui_codec_displayed = true

Exit code 0 = PASS, 1 = FAIL.

The baseline argument is optional but, when present, gates an additional
sanity check: baseline must report events_emitted < after.events_emitted
(i.e. main really did lack this emission). Pass `--no-baseline` to skip.

Usage:
  bt_codec_observability_compare.py <baseline.txt> <after.txt>
  bt_codec_observability_compare.py --no-baseline <after.txt>
"""

import re
import sys
from pathlib import Path
from typing import Dict, List, Tuple

PARSEABLE_CODECS = {
    "sbc",
    "aac",
    "aptx",
    "aptx-hd",
    "ldac",
    "lhdc",
    "vendor",  # explicit non-error vendor fallback
}

SUMMARY_PATTERN = re.compile(
    r"events_emitted=(?P<events>\d+).*?"
    r"codec_log_lines=(?P<lines>\d+).*?"
    r"ui_codec_displayed=(?P<ui>true|false).*?"
    r"per_phone_codec=(?P<per_phone>[^\n]*)",
    re.IGNORECASE,
)


def parse_artifact(path: Path) -> Dict[str, object]:
  """Locate the trailing summary line in the artifact and parse it."""
  text = path.read_text(encoding="utf-8", errors="replace")
  match = None
  # Prefer the last summary line if multiple exist.
  for candidate in SUMMARY_PATTERN.finditer(text):
    match = candidate
  if match is None:
    raise ValueError(f"No summary line found in {path}")
  per_phone_raw = match.group("per_phone").strip()
  per_phone: List[Tuple[str, str]] = []
  if per_phone_raw:
    for token in per_phone_raw.split(","):
      token = token.strip()
      if not token or "=" not in token:
        continue
      addr, codec = token.split("=", 1)
      per_phone.append((addr.strip(), codec.strip().lower()))
  return {
    "events_emitted": int(match.group("events")),
    "codec_log_lines": int(match.group("lines")),
    "ui_codec_displayed": match.group("ui").lower() == "true",
    "per_phone": per_phone,
  }


def evaluate(after: Dict[str, object], baseline: Dict[str, object] | None) -> Tuple[bool, List[str]]:
  reasons: List[str] = []
  ok = True

  events = after["events_emitted"]  # type: ignore[assignment]
  lines = after["codec_log_lines"]  # type: ignore[assignment]
  ui = after["ui_codec_displayed"]  # type: ignore[assignment]
  per_phone: List[Tuple[str, str]] = after["per_phone"]  # type: ignore[assignment]

  if events < 3:
    ok = False
    reasons.append(f"events_emitted={events} < 3")
  if lines < 3:
    ok = False
    reasons.append(f"codec_log_lines={lines} < 3")
  if not ui:
    ok = False
    reasons.append("ui_codec_displayed=false")

  parseable = sum(1 for _, codec in per_phone if codec in PARSEABLE_CODECS)
  if parseable < 2:
    ok = False
    reasons.append(
      f"per_phone_codec parseable count={parseable} < 2 (entries: {per_phone})"
    )

  if baseline is not None:
    base_events = baseline["events_emitted"]  # type: ignore[assignment]
    if base_events >= events:
      ok = False
      reasons.append(
        f"baseline events_emitted={base_events} not < after events_emitted={events} "
        "(baseline should have ZERO emissions on main)"
      )

  return ok, reasons


def main(argv: List[str]) -> int:
  args = argv[1:]
  baseline_path: Path | None = None
  if "--no-baseline" in args:
    args = [a for a in args if a != "--no-baseline"]
    if len(args) != 1:
      print(__doc__, file=sys.stderr)
      return 2
    after_path = Path(args[0])
  else:
    if len(args) != 2:
      print(__doc__, file=sys.stderr)
      return 2
    baseline_path = Path(args[0])
    after_path = Path(args[1])

  after = parse_artifact(after_path)
  baseline = parse_artifact(baseline_path) if baseline_path else None

  ok, reasons = evaluate(after, baseline)
  print(f"AFTER: {after}")
  if baseline is not None:
    print(f"BASELINE: {baseline}")
  if ok:
    print("PASS")
    return 0
  print("FAIL")
  for r in reasons:
    print(f"  - {r}")
  return 1


if __name__ == "__main__":
  sys.exit(main(sys.argv))
