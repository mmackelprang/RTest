# `AUD-80` — reconnecting to a Cast device does not keep the volume; it comes back loud

[← Builder Queue index](../../docs/BUILDER_QUEUE.md)

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

## Shipped — 2026-09-29 (branch `fix/aud-5-80-ui-15-cast-volume`, with `AUD-5` and `UI-15`)

### Root cause, measured on the box (file sink, read-only)

**"Always pretty loud" is `GoogleCast.DefaultVolume` (0.7), pushed on every start.** Every connect on
2026-09-29 (16:27:18, 16:33:54, 16:49:50, 16:55:26, 16:57:13) is followed within seconds by
`Synced volume from Cast device: 70 % (initial: false)`, then the owner stepping it down on the speaker
(60, 50, 40, 30 %). `SyncVolumeAfterStartAsync` pushed the output's own `Volume` — which nothing sets after
construction, so it is always `DefaultVolume` — and did not baseline the echo filter, so the device's
confirmation arrived as an **external** change and was written (and persisted) as master volume. The
initial-sync adoption the row describes was real too, but it was overwritten by the 70 % echo seconds later.

⚠ **Finding, not fixed here: the console cannot change the Cast speaker's volume at all.** Nothing assigns
`GoogleCastOutput.Volume` after construction (so `OnVolumeChanged` → `SetCastVolumeAsync` never runs in
production), and the Cast audio is tapped **before** the master mixer's volume
(`SoundFlowAudioEngine.UpdatePlaybackDeviceVolume` applies it only to the local playback device). While
casting, the console slider moves only the (muted) local level. **The row's verification step "set the
speaker to 25 % from the console" is therefore not performable** — set it on the speaker or in Google Home.
Wiring console volume to the speaker is a candidate new row, not part of this fix.

### The duplicate master-volume keys (code + read-only `sqlite3` on the box)

| Key | Value on box | Last written | Written by | Read by |
|---|---|---|---|---|
| `AudioPreferences:MasterVolume` | 12 | 2026-09-29 21:05 UTC | `AudioPreferencePersistence.PersistVolumePreferencesAsync` | `AudioPreferencePersistence.RestoreVolumePreferencesAsync` (startup) |
| `audiopreferences:masterVolume` | 75 | **2026-03-10** | `ConfigurationController.UpdateConfigurationSection` — the System Config page's "Audio Preferences" save (section lower-cased, keys camelCased by JSON) | only that page, through `GET /api/configuration/audiopreferences`, whose prefix match is case-insensitive and returns **both** keys |

**The runtime is already consistent: it writes and reads only the PascalCase key.** The lower-case one is a
six-month-old shadow the runtime never reads, so it is **not** the cause of "loud" (the 70 % push is). The
same shadow exists for `currentOutput` and `currentSource`. The SQLite key column is case-sensitive, which is
what lets both exist.

**Deliberately not changed:** the generic `ConfigurationController` write path. Making it reuse an existing
key's casing would turn the System Config page's stale DTO into a live editor of the runtime's persisted
volume/source/output — a behaviour change for every section, outside this row. The new key this row adds,
`AudioPreferences:CastDeviceVolumes`, uses the runtime's casing.

**One-off box cleanup (described, NOT run — owner or coordinator):** stop nothing; with `sqlite3`:

```sql
-- /opt/radio-console/data/config/configuration.db — back it up first
DELETE FROM Config_sqlite WHERE Key IN
  ('audiopreferences:masterVolume', 'audiopreferences:currentOutput', 'audiopreferences:currentSource');
```

Leave `audiopreferences:hiddenSources` — it has no PascalCase twin and `SourcesController` reads it through
`IOptionsMonitor<AudioPreferences>`. Opening and **saving** the System Config page's Audio Preferences section
will recreate the lower-case rows; that is pre-existing and harmless to the runtime.

### What landed

- **`ICastDeviceVolumeStore` / `ConfigStoreCastDeviceVolumeStore`** (`src/Radio.Infrastructure/Audio/Outputs/CastDeviceVolumeStore.cs`):
  last volume per Cast device keyed by `ChromecastDeviceInfo.Id`, persisted as one JSON object under
  `AudioPreferences:CastDeviceVolumes` (one entry because a device id is a URI containing `:`).
- **`GoogleCastOutput`**: on connect, a remembered volume is pushed to the device; a device never seen keeps
  its own level, which is remembered; with neither (read failed, nothing remembered) the device is left
  alone. The after-start push re-applies that level instead of `DefaultVolume`, and every push baselines
  the echo filter first. A level set on the speaker is remembered.
- ⚠ **Deviation from the dossier's recommendation, stated for veto:** a never-seen device does **not** get the
  console's master volume. Master volume is the local speakers' level (casting never reads it) and on this box
  it has been overwritten by the 70 % echo on every connect, so pushing it would reproduce the bug on the first
  connect to any new speaker. `GoogleCast.DefaultVolume` is no longer pushed to any device.
- With `AUD-5`, the console slider no longer snaps to the speaker on connect; it follows the speaker's
  external changes as before.

**Migration:** nothing is remembered yet after deploy, so the first reconnect to each speaker keeps whatever
level the speaker is at (no 70 % push) and remembers it; from then on reconnects restore it.

**Keying caveat:** `Id` is the device URI (`https://<ip>/`), so a DHCP address change makes a speaker look new
(it then keeps its own level — the safe fallback).

## ✅ Closed 2026-09-30 — owner UAT passed; box cleanup done

Merged as [#725](https://github.com/mmackelprang/RTest/pull/725), squash `1bb8b34` (with `AUD-5` and `UI-15`). Owner UAT 2026-09-30 ([`RETURN-CHECKLIST.md`](../uat/RETURN-CHECKLIST.md) evening batch §C): *"Cast volume works now."*

**The one-off cleanup above was run on 2026-09-30.** A `.backup` was taken first to `/opt/radio-console/data/config/configuration.db.bak-20260930-aud80` and verified (`integrity_check` ok, 134 rows). Before deleting, `Key` was confirmed to be `TEXT PRIMARY KEY` with the default (BINARY, case-sensitive) collation, so the correctly-cased live `AudioPreferences:*` keys could not match. Exactly three rows were deleted, all dated 2026-03-10: `audiopreferences:masterVolume` (75), `audiopreferences:currentOutput` (empty), `audiopreferences:currentSource` (Radio). 131 rows remain. `audiopreferences:hiddenSources` was kept deliberately, as advised above.

Follow-up: `AUD-81` — the owner ruled on 2026-09-30 that the console volume should drive the Cast speaker; its design must account for this row's per-device store. Archived.
