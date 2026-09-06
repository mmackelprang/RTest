# UI-6 — `AudioStateStore` notifies N subscribers and awaits one.

> Queue dossier for row **`UI-6`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.

| Field | Value |
|---|---|
| Status | 📋 |
| Plan | *to be written — small enough for a plan-in-the-row if the owner prefers* |
| Spec / handoff | [`PHN-1f` plan §6.2](../../design/plans/PHN-1f-the-wait-then-play-queue.md), and § *What the two reviewers found* below |
| Depends on | — |
| Branch | `fix/audio-state-store-multicast-notify` |

## Detail

**`AudioStateStore` notifies N subscribers and awaits one.** 🟡 **P2 — and PR 6 is NOT the deadline; see § *`UI-6` — the tiering argument, and the two counts the deferral note got wrong* below.** `AudioStateStore.NotifyAsync` (`src/Radio.Web/Services/AudioStateStore.cs:406-419`) does `await handler.Invoke()` on a multicast `Func<Task>`. `Delegate.Invoke` runs every handler but **returns only the last one's Task**, so every earlier subscriber runs to its first `await` and its continuation is never observed — the `try`/`catch` protects exactly one of N, and the other N−1 exceptions reach no log at all.

**Two more sites hand-roll the identical defect and are NOT fixed by fixing `NotifyAsync`:** `OnHubRadioStateChanged` (`:223-237`) and `OnHubSleepStateChanged` (`:239-245`) — and the second has **no `try`/`catch` at all**.

⭐ **A second, sharper half the deferral note did not name:** a subscriber that throws **synchronously** — before its first `await` — propagates straight out of `Invoke`, so **every handler registered after it never runs.** That is starvation, not just a lost log line, and `DuckingService`'s own raise guard (`DuckingService.cs:481-483`) documents the same shape as a known, accepted limitation for two subscribers.

**Fix:** iterate `GetInvocationList()`, await each, catch per subscriber; apply the same shape to the two hand-rolled sites.

**Est. 0.5 d.** ⚠ **The `UI-6` ID was assigned by the Builder** on the plan's own `UI-` suggestion — plan §6.2 left it *"for the owner to assign"*; rename freely.
