# `AUD-82` — `/api/metrics/snapshots` counts rolled-up data more than once

[← Builder Queue index](../BUILDER_QUEUE.md)

🟢 **P3.** Filed 2026-09-29 by the `UI-2` builder, found while replacing the Metrics page's fan-out. **Found, not fixed.**

The snapshot endpoint's aggregation reads rolled-up buckets in a way that counts the same data at more than one resolution, so its counter totals are inflated. After `UI-2` the UI no longer calls it (Diagnostics uses the new single-query `GET /api/metrics/window`, verified against the per-key calculation on a copy of the box's `metrics.db` with zero mismatches). Remaining callers, if any, should be found first; if there are none, deleting the endpoint is a legitimate fix.
