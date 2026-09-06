# UX-1 — Skeleton shimmer amplitude — is a 6/255 gradient delta enough on the dark theme?

> Queue dossier for row **`UX-1`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.

| Field | Value |
|---|---|
| Status | 📋 |
| Plan | _plan TBD — **do not write one until the Designer has answered**; scope depends entirely on whether the answer is "new token," "retune the existing pair," or "leave it"_ |
| Spec / handoff | [GV-8 UAT `L-1`](../uat/2026-07-31-gv8-error-state/REPORT.md) · evidence: `uat/2026-07-31-gv8-error-state/screenshots/03-c2-frame-a-108ms.png` vs `04-c2-frame-b-224ms.png` |
| Depends on | — _(no code dependency; it is gated on a design answer, not on a row)_ |
| Branch | `feat/ux-skeleton-shimmer-amplitude` |

## Detail

**Skeleton shimmer amplitude — is a 6/255 gradient delta enough on the dark theme?**

**Design-led: get a Designer answer before a plan is written, and accept that this row may legitimately close as "no change."** From [GV-8 UAT `L-1`](../uat/2026-07-31-gv8-error-state/REPORT.md).

**The shimmer is NOT broken, and this row must not be re-filed as "the skeleton is broken"** — it demonstrably animates, on three independent kinds of evidence: `animationPlayState: running` with `Animation.currentTime` advancing **0 ms → 117 ms**; `backgroundPosition` moving `-200%` → `-174.531%`; and a CDP screencast in which **14.5 % of pixels changed** in the skeleton region between two frames **116 ms** apart, against a **0 %** change in a static control region of the same frame pair (which is what rules out compression noise and global repaint). `prefers-reduced-motion` was `false`, so the `animation: none` override was not in play.

**What is marginal is the amplitude.** `.skeleton-loading` (`src/Radio.Web/wwwroot/css/design-system.css:1075-1083`) is `linear-gradient(90deg, var(--surface-raised) 0%, var(--surface-overlay) 50%, var(--surface-raised) 100%)` at `background-size: 200% 100%`, and on this theme those tokens are `#141416` and `#1A1A1D` (`design-system.css:65`/`:67`) — `rgb(20,20,22)` → `rgb(26,26,29)`, a **6/255 stop-to-stop delta**, with **measured peak frame-to-frame change of 3/255**. Side by side the two captured frames read as identical to the eye.

**Scope is the whole app, not the texts pane — this is the point of the row.** `.skeleton-loading` is the shimmer primitive every skeleton composes with: `Skeleton.razor`'s seven shapes (NowPlaying, Radio, ListRow, DeviceRow, MetricTile, Visualizer, …) are mounted at **27 call sites across 6 pages** (`DeviceManagementPage`, `PlayHistoryPage`, `QueueHistoryPanel`, `RadioControlPanel`, `RadioPage`, `VisualizerPanel`), plus **38 raw `.skeleton-loading` nodes** in the phone panels.

**A one-line token change lands on all of them at once — which is precisely why this is a design-token decision and not a bug fix, and why it was correctly kept out of GV-8.**

**Questions for the Designer, none pre-decided here:** should the shimmer highlight get **its own token** rather than borrowing `--surface-overlay`, which exists to be a *surface* and is also used with `backdrop-filter: blur(20px)` (`design-system.css:184`) — i.e. it is not free to move? Is a wider delta still tasteful on a **wall-mounted kiosk in a dark room**, or does it start reading as a flashing band? Does the answer differ per shape — a full-bleed `Visualizer` block sweeping is a much larger moving area than a 16px `skeleton-text` bar.

**Constraint any fix must honour:** the `prefers-reduced-motion: reduce` override at `design-system.css:1683` sets `animation: none`, so reduced-motion users see the **static** gradient — widening the amplitude must not make that state worse.

**Judge it on the kiosk panel, not a laptop LCD:** the numbers above came from frames rendered on the box, and a 6/255 delta is exactly the range where two displays will not agree.
