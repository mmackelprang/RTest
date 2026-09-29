# `AUD-81` — the console cannot change a Cast speaker's volume

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-29 by the `AUD-80` builder while tracing Cast volume.

## The finding

Cast audio is taken from the output tap, **before** the master volume is applied, and nothing sets `GoogleCastOutput.Volume` after construction. So moving the console's volume does not change what the Cast speaker plays; only the speaker (or Google Home) can. `AUD-80`'s restore therefore remembers volumes set **on the speaker**, which is the only place a Cast volume can be set today.

## Owner decision

Should the console's volume control drive a connected Cast speaker? If yes: route master-volume changes to `GoogleCastOutput.SetCastVolumeAsync` while Cast is the output (through `AUD-80`'s per-device store, so the value persists), and decide whether the local speakers mute or stay at their own level while casting.
