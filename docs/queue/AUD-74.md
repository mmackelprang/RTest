# `AUD-74` — ducking "ends" during a new attack

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-28; found by the `AUD-26` reviewer (code read). Predates `AUD-26`.

When `StartDuckingAsync` cancels a release that is still fading, the cancelled release's `StopDuckingAsync` still raises the ducking-ended event. `AudioManager` handles that by restoring full volume — while the new duck's attack is still ramping down. Audible as a brief swell back to full volume at the start of an announcement that follows closely on another.

**Fix shapes:** suppress the ended event for a cancelled release, or number duck episodes so an ended event from an older episode is ignored. Test deterministically with an injectable `TimeProvider` / `FakeTimeProvider` (CLAUDE.md § Test Timing — count events, don't time them).

## Shipped

- **Fix shape chosen: numbered duck episodes.** `DuckingService` increments `_duckEpisode` (under
  `_lock`) whenever `StartDuckingAsync` begins a new episode. `StopDuckingAsync` records the episode
  when it starts a release and re-checks it after the fade; if a new episode began in between, the
  release is *superseded*.
- **Assumption (conservative): the departing source is still announced.** A superseded release
  raises `Transition: Ended` for the source that left, but with `IsDucking: true` — the shape a
  departure-while-others-remain already has — instead of being suppressed outright. `AudioManager`
  only restores volume on `IsDucking: false`, so the swell is gone, while any subscriber that keys
  on a source's `Ended` transition still hears about it.
- **Residual window, stated rather than hidden:** a new episode that starts after the post-fade
  check but before the raise (which runs outside the lock) is not seen. That window is a few
  instructions wide; the one closed spanned the whole release fade (500 ms shipped).
- `DuckingService` now takes an optional `TimeProvider` (default `TimeProvider.System`) for its
  fade-step delays; no DI change needed.
- Tests: `DuckingSupersededReleaseTests` — a release parked mid-fade on a `FakeTimeProvider` is
  cancelled by a new start and raises no `IsDucking:false`; an ordinary release still does.
