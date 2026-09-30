# `AUD-86` — a Diagnostics range that crosses a rollup tier undercounts its newest data

[← Builder Queue index](../BUILDER_QUEUE.md)

🟢 **P3.** Filed 2026-09-30 by the `AUD-82` pre-merge reviewer and confirmed by the `AUD-82` Builder with a code read. **Found, not fixed. Not measured on the box.**

## What the code does

- `GET /api/metrics/window` reads **one** bucket table, chosen by `resolution` (`SqliteMetricsRepository.GetWindowSummariesAsync`).
- The Diagnostics tab picks the table from the range (`DiagnosticsPanel.razor` `GetResolution`):

  | Range | Table read |
  |---|---|
  | 1h | Minute |
  | 24h | Hour |
  | 7d | Hour |
  | 30d | Day |

- The rollup **moves** data. Minute rows go to Hour after `RetentionMinuteData` (120 min), and Hour rows go to Day after `RetentionHourData` (48 h). Each piece of data is stored in exactly one table (`AUD-82` § Correction).

## What that implies

- **24h** reads Hour, but the newest ~2 h are still Minute rows. The tiles therefore leave out the most recent two hours, which is exactly where an operator is looking after a fault.
- **7d** reads Hour, which holds only the 2–48 h band. `UI-2` already recorded that 7d sees only 48 h (`queue/UI-2.md` § Assumptions 3). The missing newest 2 h is the new finding.
- **30d** reads Day, which lacks everything newer than ~48 h.

## Fix shapes (plan TBD)

- Have the window query read every table that overlaps `[start, end]`. The tiers never overlap in content, so `UNION ALL` then aggregating is correct. It remains one statement.
- Or make the panel request the finest table whose retention still covers the whole range.

The first keeps the `UI-2` load rule of one query per poll.

## Verification

On the box, compare a counter's 24h `sum` against the sum of its Minute rows plus its Hour rows over the same window. Today they should differ by the last ~2 h; after the fix they should match.
