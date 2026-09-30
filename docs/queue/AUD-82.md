# `AUD-82` — `/api/metrics/snapshots` counts rolled-up data more than once

[← Builder Queue index](../BUILDER_QUEUE.md)

🟢 **P3.** Filed 2026-09-29 by the `UI-2` builder, found while replacing the Metrics page's fan-out. **Found, not fixed.**

The snapshot endpoint's aggregation reads rolled-up buckets in a way that counts the same data at more than one resolution, so its counter totals are inflated. After `UI-2` the UI no longer calls it (Diagnostics uses the new single-query `GET /api/metrics/window`, verified against the per-key calculation on a copy of the box's `metrics.db` with zero mismatches). Remaining callers, if any, should be found first; if there are none, deleting the endpoint is a legitimate fix.

---

## ⛔ Correction 2026-09-30 — the double count this row was filed for does not exist

**Refuted by code read and by the box's own tables. Deleting the endpoints went ahead anyway, because they had no callers; it does not fix a defect.**

- **The rollup moves rows; it does not copy them.** `RollupMinuteToHourAsync` does an INSERT into `MetricData_Hour` and then a `DELETE FROM MetricData_Minute WHERE Timestamp < @Cutoff`, both in one transaction (`src/Radio.Metrics/Repositories/SqliteMetricsRepository.cs:396-425` before this PR). `RollupHourToDayAsync` does the same one tier up (`:479-508`). Every writer takes `_transactionLock` and clears `_currentTransaction` before releasing it, so each rollup owns and commits its own transaction, and a failed DELETE rolls the INSERT back with it.
- **The only production writer is the Minute table.** `BufferedMetricsCollector` passes `MetricResolution.Minute` to `SaveBucketsAsync` (`BufferedMetricsCollector.cs:164-170`).
- **Measured on `radio` 2026-09-30, read-only against `/opt/radio-console/data/metrics.db`:**
  - 0 Minute rows older than 150 min.
  - 0 Hour rows older than 50 h.
  - Minute starts at 19:36Z.
  - Hour runs from 2026-09-28 22:00Z to 2026-09-30 19:00Z.
  - Day ends at 2026-09-28 00:00Z.
  - Each increment is stored in exactly one table.

  *(`/opt/radio-console/data/metrics/metrics.db` also exists. It is an empty file from May and is not the live database.)*
- The pre-merge reviewer was briefed to falsify this and could not. So the old `/snapshots` counter value was a correct lifetime total, capped by day retention (365 d). `UI-2`'s "found, not fixed" note (`queue/UI-2.md` § Assumptions 4) was the origin of the claim, and it now carries a pointer here.

## ✅ SHIPPED — [#738](https://github.com/mmackelprang/RTest/pull/738) — both uncalled all-time endpoints deleted

**No caller remained**, checked across `src`, `tests`, `tools`, `scripts`, `deploy`, `.razor` and `.js`, and in the RotaryPhone repo:

| Deleted | Why it could go |
|---|---|
| `GET /api/metrics/snapshots` | Nothing has called it since `UI-2`. `DiagnosticsPanelTests` asserts zero requests to it. |
| `GET /api/metrics/aggregate` | It backs onto the same `GetAggregateAsync`. Its only client method deserialised a `MetricAggregateDto` that the API never returned (the API returns a bare `double`), and it sent a `start`/`end` that the API ignored. |
| `IMetricsReader.GetCurrentSnapshotsAsync` / `GetAggregateAsync` and their implementations | Only the two routes above used them. **`Radio.Metrics` is bumped 1.2.0 → 2.0.0** because public interface members were removed. |
| `MetricsApiService.GetMetricSnapshotsAsync` / `GetMetricAggregateAsync` (Web) | Nothing called either. |
| The two controller tests for the deleted actions | — |

**Docs changed:**
- `design/API_REFERENCE.md`:
  - The two sections are removed.
  - A `GET /api/metrics/window` reference section is added. `UI-2` had never added one.
  - The examples now use `/window`.
- `design/METRICS.md`:
  - The endpoint table is corrected.
  - The stale Auto-Refresh section is rewritten. It had described the old 10 s `/metrics` page; the panel now polls every 15 s with one request.
- `src/Radio.Metrics/README.md`.

**Found, filed as `AUD-86`:** a `/window` request reads one resolution table, so the Diagnostics 24h range (Hour table) misses the newest ~2 h, which are still Minute rows. The 7d range sees only the 2–48 h band. The deleted all-time endpoint was the only read that crossed tiers.

## ✅ Deployed and agent-verified on `radio` 2026-09-30

- **Deployed:** `main` at `af9bc2b` (squash of #738).
  - `Deploy-ToLinux.ps1` exited 0 and printed:
    - `Verified: API is running commit af9bc2b`
    - `Verified: Web is running commit af9bc2b`
    - `Kiosk is live (12 established connections to :5002, radio-kiosk.service=active)`
  - `NRestarts=0` before and after.
- **On the box:**
  - `GET /api/metrics/snapshots?keys=system.cpu_usage_percent` → **404**.
  - `GET /api/metrics/aggregate?key=system.cpu_usage_percent` → **404**.
  - `GET /api/metrics/keys` → 200.
  - `GET /api/metrics/window` (last hour, Minute) → **200**, 214 summaries.
  - `GET http://localhost:5002/diagnostics` → 200.
- **Audio state:** restored by the service on restart. SDR 92.3 was Playing, volume 0.3, muted, as before the deploy.

**Nothing is left for the owner.** Ready to archive.
