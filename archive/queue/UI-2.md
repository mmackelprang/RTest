# `UI-2` / `UI-4` / `UI-5` — Metrics leaves the nav; Settings → Diagnostics, without the fan-out

[← Builder Queue index](../BUILDER_QUEUE.md)

GA close-out §2h. Decisions **D11** (fold a trimmed diagnostics surface under Settings, kill the
fan-out) and **D13** (delete `/diagnostic`; the route name goes to the consolidated diagnostics).
`UX-2` (skeleton shimmer) is a decision, not a build, and is **not** in this change.

## What shipped

| Before | After |
|---|---|
| **Metrics** pill in the top-level nav → `/metrics` (`MetricsDashboardPage.razor`, 668 LOC) | No Metrics pill. **Settings → Diagnostics** is the last tab of `/system` (`DiagnosticsPanel.razor`) |
| `/metrics` route | **Deleted** — now 404 |
| `/diagnostic` | Already deleted by `UI-1` (#492); pinned as 404 here |
| — | **`/diagnostics`** opens Settings with the Diagnostics tab selected; the Settings pill lights on it |
| DevTray **Fingerprint events** → `/metrics` (would have broken) | → `/diagnostics` (`UI-5`'s `:272` half; its `:253` half shipped in #489) |

### The fan-out, measured

| | Old `/metrics` page | New Diagnostics tab |
|---|---|---|
| On open | 44 HTTP requests: preferences, keys, descriptors, snapshots, **up to 40 × history** | **3**: preferences, descriptors, one `/api/metrics/window` |
| Per poll | **41** (snapshots + up to 40 × history) | **1** window request (**2** while a tile is selected — its history, for the chart) |
| SQL per poll | snapshots ran **2 queries per key** (220 keys on the box = **440**, each counter summing all three rollup tables over all retained time) + 40 history queries | **1** statement (+1 with a selection) |
| Cadence | every 10 s, **whenever the page was mounted** | every **15 s**, **only while the Diagnostics tab is showing**; timer ticks never stack (a skipped user refresh is re-run) |

**Why 15 s:** radio-api flushes metric buckets every 60 s (`MetricsOptions.FlushIntervalSeconds`), so
anything faster re-reads identical rows. This follows `ENC-14`'s pattern — poll while visible, stop
when not, never fan out — without copying its 2 Hz, which suits a cheap in-memory encoder read and not
a SQLite scan.

**New endpoint:** `GET /api/metrics/window?start&end&resolution` → one row per metric with data in the
window (`Sum`, `SampleCount`, `Min`, `Max`, `LatestAverage`, `LatestTimestamp`, `BucketCount`, `Type`),
from one `ROW_NUMBER() … GROUP BY` statement in `SqliteMetricsRepository.GetWindowSummariesAsync`.

**Checked against real data:** a read-only backup of the box's `metrics.db` (2026-09-29): the window
statement returned the last hour (95 metrics with samples, plus 121 idle counters as 0 after the review fix) in single-digit ms, with **zero mismatches** against the old
per-key reduction (counter = window sum, gauge = newest bucket's average). The old snapshot
path on the same file was 440 statements.

## What was trimmed, and why

- **Per-tile sparklines** — they *were* the fan-out (one history request per tile). The chart for a
  **tapped** tile stays, with Count/Avg/Min/Max/StdDev; tap again to close it.
- **The snapshot call** — lifetime values, and 2 queries per key. Tile values now come from the window:
  a counter shows its **window total**, a gauge its **newest bucket average** — what the old page
  already displayed whenever it had a sparkline, now chosen by the metric's recorded type rather than
  by guessing from words in its key.
- **Per-route API counters (`api.requests.api.*`, 148 of 220 keys) are folded, not removed** — a
  *Show per-route API counters (N)* button reveals them. `api.requests.api` (total), `api.errors.*` and
  `api.request_duration_ms` stay visible.
- **Kept:** all five time ranges (5m/1h/24h/7d/30d, still saved to `ui.metrics.timeRange`), threshold
  tinting, descriptor units/names, the refresh button. Where the rows left a choice, more was kept.

## Assumptions (flag if wrong)

1. **"Engine state" and system stats are not duplicated into the Diagnostics tab.** They already sit
   on Settings → *System Stats* (and the DevTray). The Diagnostics tab is the metrics surface.
2. **A gauge with no samples in the chosen window is not shown** (the old page showed its lifetime
   snapshot) — a zero would be a claim about a value nothing measured. **A counter with no samples is
   shown as 0**, the healthy reading for errors and underruns (review finding; the first cut dropped
   those too).
3. **The 7d range still reads the Hour table, which only retains 48 h** — unchanged from the old
   page; not fixed here.
4. **Found, not fixed:** the snapshot endpoint (`GetAggregateAsync` for counters) sums `ValueSum`
   across the Minute, Hour **and** Day tables, so rolled-up data is counted more than once. Nothing in
   the UI calls it any more; it still backs `/api/metrics/snapshots` and `/aggregate`.
   ⛔ **Refuted 2026-09-30 by `AUD-82`:** the rollup *moves* rows (INSERT + DELETE in one transaction), so no
   data sits in two tables and nothing was counted twice. Both endpoints were deleted anyway (no callers) —
   see [`AUD-82`](AUD-82.md) § Correction.

## Tests

- **Added:** `DiagnosticsPanelTests` (3 reads on open; exactly one window request per poll at the
  poll interval via `FakeTimeProvider`; +1 history only for a selected tile; nothing after dispose;
  counter vs gauge tile value; per-route toggle; empty state), `MainLayoutNavTests` (no Metrics pill; Settings lit on
  `/diagnostics`), `SystemConfigPageTests` (Diagnostics tab exists; panel not mounted and metrics API
  never called on `/system`; `/diagnostics` opens the tab; leaving the tab unmounts it;
  `IsDiagnosticsRoute`; `/system` → `/diagnostics` on a mounted page), `DevTrayTests.FingerprintEventsCard_NavigatesToDiagnostics`,
  `ApiNotFoundPipelineTests.RemovedPageRoute_IsNotServed` (`/metrics`, `/diagnostic`),
  `SqliteMetricsRepositoryTests.GetWindowSummariesAsync_*` (3, incl. idle counter = 0), `MetricsControllerTests.GetWindow_*` (2).
- **Changed:** `ApiNotFoundPipelineTests.SpaDeepLinks` (`/metrics` → `/diagnostics`);
  `NavigationE2ETests.Navigation_MetricsPage_LoadsSuccessfully` → `Navigation_DiagnosticsPage_LoadsSuccessfully`.
- **Removed:** `MetricsDashboardPageTests` (7 markup-presence tests of the deleted page).
- **Mutation-checked:** restoring the per-metric history fan-out, re-adding the Metrics pill, dropping
  `/diagnostics` from the Settings pill, pointing DevTray back at `/metrics`, a 1 s poll, removing the
  SQL window filter, and removing the `/diagnostics` route each turned at least one test red.

## Owner UAT (after deploy — confirm `gitShaShort` matches `origin/main` first)

1. The top nav has **no Metrics pill**. Tap **Settings**: a **Diagnostics** tab is last in the left rail.
2. Open it: tiles grouped by category (AUDIO, BLUETOOTH, FINGERPRINT, SYSTEM, …); the header reads
   *Updates every 15 s while open*. No per-route API tiles until *Show per-route API counters* is tapped.
3. Tap a tile → chart with Count/Avg/Min/Max/StdDev; tap it again → chart closes. Switch 5m/1h/24h —
   tiles and chart follow; the choice survives a reload.
4. Open the DevTray → **Fingerprint events** → lands on Settings with Diagnostics selected and the
   Settings pill lit.
5. Known limitation: on `/diagnostics`, if you pick another Settings tab and then press the DevTray card,
   nothing happens (the address has not changed). Tapping the Diagnostics tab works.
6. `http://radio:5002/metrics` → not found. `http://radio:5002/diagnostics` → Settings on Diagnostics.
7. Optional load check: with the tab open, `api.requests.api.Metrics.window` should rise by ~4/min and
   `api.requests.api.Metrics.history` should not move unless a tile is selected; switch to another tab
   and both stop.

## ✅ Closed 2026-09-30 — agent-verified (not owner-run)

Merged as [#728](https://github.com/mmackelprang/RTest/pull/728), squash `dffb55f` (with `UI-4` and `UI-5`, which had no queue rows of their own). Verified in the agent pre-pass 2026-09-29 ~22:47–22:57 EDT against the box (`7dd34b5`, both services SHA-verified; console muted, so nothing judged by ear) — [`RETURN-CHECKLIST.md`](../uat/RETURN-CHECKLIST.md) evening batch §A: no Metrics pill in the top nav; Diagnostics is the last Settings tab; tiles grouped by category with the 15 s header; tap a tile → chart, tap again → closed; switching to 1h changed the tiles and survived a reload; `/diagnostics` opens the panel. **Not done:** the physical DevTray triple-tap (step 4 above). **Minor findings, recorded there and not filed:** the chart's y-axis runs negative (`-41.8 ms` on Request Duration); the "Requests Api" tile always reads 0 because `api.requests.api` counts only bare `/api` (the real counts are in the per-route keys). Archived.
