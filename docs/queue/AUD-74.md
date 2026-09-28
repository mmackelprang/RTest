# `AUD-74` — ducking "ends" during a new attack

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-28; found by the `AUD-26` reviewer (code read). Predates `AUD-26`.

When `StartDuckingAsync` cancels a release that is still fading, the cancelled release's `StopDuckingAsync` still raises the ducking-ended event. `AudioManager` handles that by restoring full volume — while the new duck's attack is still ramping down. Audible as a brief swell back to full volume at the start of an announcement that follows closely on another.

**Fix shapes:** suppress the ended event for a cancelled release, or number duck episodes so an ended event from an older episode is ignored. Test deterministically with an injectable `TimeProvider` / `FakeTimeProvider` (CLAUDE.md § Test Timing — count events, don't time them).
