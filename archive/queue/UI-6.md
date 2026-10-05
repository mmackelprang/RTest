# UI-6 — `AudioStateStore` notifies N subscribers and awaits one.

> Queue dossier for row **`UI-6`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.
>
> ⚠ **Directional words in the prose were written when every row shared one file.**
> *above*, *below* and *this file* may now point across files — most often at
> [`BUILDER_QUEUE_ARCHIVE.md`](../BUILDER_QUEUE_ARCHIVE.md) or a sibling in this
> directory. They were left verbatim rather than reworded, which would be a content edit.

| Field | Value |
|---|---|
| Status | ✅ [#596](https://github.com/mmackelprang/RTest/pull/596) — shipped 2026-09-07, archived in [`BUILDER_QUEUE_ARCHIVE.md`](../BUILDER_QUEUE_ARCHIVE.md) |
| Plan | *plan-in-the-row — the § Detail below was the handoff, which is the option this row offered* |
| Spec / handoff | [`PHN-1f` plan §6.2](../../design/plans/PHN-1f-the-wait-then-play-queue.md), and § *What the two reviewers found* below |
| Depends on | — |
| Branch | `fix/audio-state-store-multicast-notify` |

## Detail

**`AudioStateStore` notifies N subscribers and awaits one.** 🟡 **P2 — and PR 6 is NOT the deadline; see § *`UI-6` — the tiering argument, and the two counts the deferral note got wrong* below.** `AudioStateStore.NotifyAsync` (`src/Radio.Web/Services/AudioStateStore.cs:406-419`) does `await handler.Invoke()` on a multicast `Func<Task>`. `Delegate.Invoke` runs every handler but **returns only the last one's Task**, so every earlier subscriber runs to its first `await` and its continuation is never observed — the `try`/`catch` protects exactly one of N, and the other N−1 exceptions reach no log at all.

**Two more sites hand-roll the identical defect and are NOT fixed by fixing `NotifyAsync`:** `OnHubRadioStateChanged` (`:223-237`) and `OnHubSleepStateChanged` (`:239-245`) — and the second has **no `try`/`catch` at all**.

⭐ **A second, sharper half the deferral note did not name:** a subscriber that throws **synchronously** — before its first `await` — propagates straight out of `Invoke`, so **every handler registered after it never runs.** That is starvation, not just a lost log line, and `DuckingService`'s own raise guard (`DuckingService.cs:481-483`) documents the same shape as a known, accepted limitation for two subscribers.

**Fix:** iterate `GetInvocationList()`, await each, catch per subscriber; apply the same shape to the two hand-rolled sites.

**Est. 0.5 d.** ⚠ **The `UI-6` ID was assigned by the Builder** on the plan's own `UI-` suggestion — plan §6.2 left it *"for the owner to assign"*; rename freely.

---

## Builder addendum, 2026-09-07 (#596)

Appended rather than edited into the prose above, which this file keeps verbatim.

⚠ **The `DuckingService.cs:481-483` citation in § Detail is a stale anchor.** Those lines are
fade-parameter arithmetic (`CalculateFadeParameters`). The precedent the row means — *"This catches;
it does not resume the invocation list… That is accepted for two subscribers; anything more would
want a `GetInvocationList` loop and a reason"* — is at **`DuckingService.cs:550-552`**, with the
guarded raise at `:573-583`. `DuckingService` was **not** changed by this row: its events are
synchronous `EventHandler<T>`, so `Invoke` genuinely runs every handler and only the starvation half
could ever apply there.

✅ **A closer precedent existed than the row knew about.** `ConsolePlaybackState.cs:88-100` already
implemented exactly the prescribed fix, and `ConsolePlaybackStateTests.cs:57` already tested the
synchronous-starvation half. The fix here was made to match that shape rather than invent one.

⚠ **~17 further sites carry the same `await X.Invoke()` shape and are NOT in this row's scope** —
`AudioStateHubService` (14 sites), `RadioPanelToggleService.cs:64`, `DeviceDisplayStateService.cs:22`,
`PhoneUnreadState.cs:23`. Most of those events have a single subscriber today, where returning the
last handler's task is harmless. **For Planner:** `AudioStateHubService` is the one worth a row —
`AudioStateStore` is its only subscriber today, but nothing enforces that, so the defect is latent
rather than absent.
