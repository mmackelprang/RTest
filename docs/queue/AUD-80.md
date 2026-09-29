# `AUD-80` — reconnecting to a Cast device does not keep the volume; it comes back loud

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-29 from the owner: *"whenever the console reconnects to a cast device, the volume doesn't persist from before. It's always set pretty loud. Let's queue a fix for this."*

## What the code does today (read 2026-09-29, `main` at `56b6639`)

- On every connect, `GoogleCastOutput.SyncInitialVolumeAsync` (`GoogleCastOutput.cs:1552-1580`, called from `:643`) **reads the speaker's current level** and raises `CastVolumeChanged` with `IsInitialSync = true`, whose subscriber sets — and persists — `AudioManager.MasterVolume` (`AudioStateUpdateService`; the mechanism is documented at `GoogleCastOutput.cs:95-104`). **The console adopts the speaker's volume; it never restores its own.** After a reconnect you get whatever the speaker is sitting at.
- The Cast output's own volume starts at `GoogleCast.DefaultVolume` (`:216`, fallback 0.7; the shipped `appsettings.json` has 0.7 and 0.8 in two sections), and `OnVolumeChanged` pushes any change straight to the device (`:226-229`).

## What the box holds (config store, 2026-09-29)

```
AudioPreferences:MasterVolume | 30
audiopreferences:masterVolume | 75
Radio:LastDeviceVolume        | 50
radio:defaultDeviceVolume     | 50
ui.playback:volume            | 0.13
```

⚠ **The master volume is stored twice under keys that differ only by case, with different values (30 and 75).** The same duplicate-by-case shape exists for `devices:*` (`AUD-13`). Which one a restore reads decides whether "restored" means 30 % or 75 % — the latter would match "always pretty loud". Establish which key the persistence path writes and which it reads before changing anything.

## The owner decision this implements

Reconnecting should put the speaker back at the volume the console last had for it (or last had overall — see below), not adopt the speaker's level and not a hard-coded default.

## Scope questions for the plan

1. Per device or global? Remembering the last volume **per Cast device** (keyed by device id) is the natural reading when there are several speakers (the box found 7); a single console master volume is simpler. Recommend per device, falling back to the master volume for a device never seen.
2. The initial sync must then PUSH the remembered volume to the device instead of adopting the device's. Keep adopting when there is nothing remembered.
3. Fix or de-duplicate the case-duplicated `audiopreferences` keys; a one-off cleanup on the box plus making the store case-consistent.
4. Interaction with `AUD-5` (a stale connection's initial sync can persist its volume as master): both touch the initial-sync path; `AUD-5`'s "the subscriber ignores `IsInitialSync`" half and this row's "push instead of adopt" should be designed together, and `AUD-5` should land first or with this row.

## Verification

Set the speaker to 25 % from the console, disconnect Cast, reconnect: the speaker plays at 25 %, and the console's volume shows 25 %. Repeat across a `radio-api` restart.
