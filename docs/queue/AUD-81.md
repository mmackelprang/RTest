# `AUD-81` — the console cannot change a Cast speaker's volume

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-29 by the `AUD-80` builder while tracing Cast volume.

## The finding

Cast audio is taken from the output tap, **before** the master volume is applied, and nothing sets `GoogleCastOutput.Volume` after construction. So moving the console's volume does not change what the Cast speaker plays; only the speaker (or Google Home) can. `AUD-80`'s restore therefore remembers volumes set **on the speaker**, which is the only place a Cast volume can be set today.

## Owner decision

Should the console's volume control drive a connected Cast speaker? If yes: route master-volume changes to `GoogleCastOutput.SetCastVolumeAsync` while Cast is the output (through `AUD-80`'s per-device store, so the value persists), and decide whether the local speakers mute or stay at their own level while casting.

## ✅ Owner ruling 2026-09-30 — the console volume drives the Cast speaker

Owner, 2026-09-30 ([`RETURN-CHECKLIST.md`](../uat/RETURN-CHECKLIST.md) evening batch §D): *"I'd like the console volume to be able to change the cast volume."* The row is now buildable. It belongs with the casting work (the Cast arc, 2c).

**The ruling decides whether, not how. The design must settle:**

1. **How master volume maps to the speaker.** Master volume is today the local speakers' level, which casting never reads (Cast audio is tapped before it). Does the console's slider set the speaker's level directly while Cast is the output, or scale it?
2. **The interaction with `AUD-80`'s per-device remembered volume** (`AudioPreferences:CastDeviceVolumes`). Today it records levels set on the speaker and restores them on reconnect; once the console can set the level, the console's changes must be remembered there too, and a reconnect must not fight the slider.
3. **The echo filter.** Every push to the speaker baselines the echo filter first (`AUD-80`), and `AUD-5` still lets external speaker changes reach master volume. A console → speaker → console loop is the obvious hazard; the design must show it cannot oscillate or re-persist an echo as master (the original `AUD-80` bug).
4. Whether the local speakers mute or keep their own level while casting (from the question above).
