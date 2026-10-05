# `AUD-16` — retire the deprecated USB radio path now that RTL-SDR is the only supported tuner

[← Builder Queue index](../../docs/BUILDER_QUEUE.md)

🔵 **P3.** Filed 2026-09-08 by the `AUD-13` Planner, which found that the owner's deprecation is
**already implemented in behaviour** and that what remains is dead surface area, not a defect.

## Owner input this rests on

> *"The Radio USB connection has been deprecated so that the SDRRTL radio is really the only one
> supported."* — 2026-09-07

## What is already true, and therefore NOT this row

`AUD-13`'s planning established that the deprecation is live:

- `RadioFactory.cs:147` defaults to `RTLSDRCore`.
- `RadioFactory.cs:336-339` — `IsRF320Available()` opens with
  `if (string.IsNullOrWhiteSpace(usbPort)) return false;`, so a blank port means the RF320 is never
  offered or selected.
- `SystemConfigPage.razor:280` already ships the copy *"Leave empty if using RTL-SDR. Only set for
  RF320 USB audio capture."*

So **an empty `Devices:Radio:USBPort` is correct, documented configuration** — not a fault, and not
something to warn about. Anyone arriving here expecting to *make* the deprecation happen should stop
and re-read: it happened.

## What this row actually is

The RF320 USB capture path still exists in code, config schema, and UI. This row is the deliberate
decision about whether to **remove it** — and it is a scope question the plan must answer before
touching anything:

1. **Is the hardware genuinely gone?** The `RaddyRF320BT/` git submodule is still in the tree and
   `CLAUDE.md` describes it as the vintage radio protocol. Removing the audio path while the
   protocol submodule stays is either correct or half a change; decide which and say why.
2. **What is the blast radius?** `RadioAudioSource`, the `RadioFactory` branch, the `USBPort` config
   key, its `SystemConfigPage` field and copy, DTO surfaces, and any tests. Enumerate before
   estimating — this is the kind of removal that looks like an afternoon and touches thirty files.
3. **Deployed config.** ⚠ The SQLite store outranks both JSON layers and **an empty store entry still
   wins** (`DeviceOptionsResolver.cs:55-61`). If the key is removed, say what happens to a store row
   that still names it. `fingerprinting:fpcalcPath` is already sitting orphaned in that store from
   an earlier rename — that is the failure mode to avoid repeating.
4. **Or is the honest answer "leave it"?** Deprecated-but-working code that costs nothing is not
   automatically debt. The greenfield rule in `MEMORY.md` says no backward compatibility is owed,
   which makes removal *permitted*, not *required*. **A reasoned close as "keep, documented" is an
   acceptable outcome for this row.**

## Explicitly not this row

- **`AUD-13`** — the real defect there is the capture-device fallback binding the wrong jack on a
  **non-empty** port. Unrelated mechanism, and it must not be folded in.
- Anything about **vinyl**, which is USB-only and always will be per the same owner message.

## Verification

Whatever is removed, the gate is that RTL-SDR tuning still works end-to-end and no config path
throws on an existing deployed store. ⚠ Needs a box session; the tuner is hardware.

**Not auto-mergeable** if code is removed. Auto-mergeable if the outcome is documentation only.

## Owner ruling 2026-09-28 — remove it all (D-D)

Recorded in [`HANDOFF-GA-CLOSEOUT.md`](../handoffs/HANDOFF-GA-CLOSEOUT.md) §4: **remove all support for the
RaddyRF320BT device, including the `external/RaddyRF320BT` submodule. The RTL-SDR supersedes it.**
The row's "keep it, documented" close is withdrawn. Scope for the removal PR, sized 2026-09-28:
~25 files under `src/`, `tools/` and `tests/` reference it, the core ones being `RadioFactory.cs`, `RadioAudioSource.cs`, `USBAudioSourceBase.cs`,
`IRadioFactory.cs`, `IRadioControl.cs`, `RadioOptions.cs`, `DeviceOptions.cs`, `RadioDtos.cs`,
`RadioStateMapper.cs`, `RadioController.cs`, `SourceTypeHelper.cs`, `SoundFlowMasterMixer.cs`,
`IAudioSource.cs`; plus `.gitmodules`, the `RaddyRF320BT/` line in `CLAUDE.md` and `README.md`'s structure trees, and the design docs. Check the orphaned
SQLite store row question the original text raises before deleting the options type.

## ✅🔬 2026-09-30 — built (Builder, Phase 2i, batch B)

Branch `fix/aud-16-remove-rf320`. Everything named in the ruling is removed.

- **Code:** `RadioAudioSource`; the RF320 branch of `RadioFactory` and its availability check and port
  lookup (its constructor lost `IOptionsMonitor<DeviceOptions>`, `IAudioDeviceManager` and
  `DeviceOptionsResolver`; it is registered by type, so DI adjusts); `DeviceOptions.Radio` /
  `RadioDeviceOptions`; `DeviceOptionsResolver.GetRadioUSBPortAsync`; the API and Web
  `RadioDeviceOptionsDto`; the RF320 capabilities arm in `RadioController`.
- **UI:** the System Config Devices-tab "Radio USB Audio (RF320 only)" field, and the "Raddy RF320" option
  in the Radio tab's default-device dropdown. The `SourceTypeHelper` `"RF320"` arms are gone.
- **Config and repo:** `Devices:Radio` in `src/Radio.API/appsettings.json` and in the
  `deploy/debian-x64` seed; `.gitmodules` and `external/RaddyRF320BT`; `submodules: true` in
  `.github/workflows/build.yml`. README, `CLAUDE.md` (the solution tree's submodule line and the source
  lists), and the current design docs are updated. Dated history is left alone.
- **Kept on purpose:** `AudioSourceType.Radio`, with its numbering unchanged. The RTL-SDR reports it, and
  the box parses `AudioPreferences:CurrentSource|Radio` back by name at start-up. The
  `RadioOptions.DefaultDevice` key stays too. Any value other than `RTLSDRCore` is unavailable, and
  `GetDefaultDeviceType` falls back to the first available device.

### The deployed store (question 3 above)

The box keeps `devices:radio|{"usbPort":"/dev/ttyUSB0"}` and `devices:Radio|{"usbPort":""}` in SQLite,
plus `Devices:Radio:USBPort="AB13X"` in its own `appsettings.Production.json`. Deploys never overwrite
that file. **Nothing migrates them, and nothing needs to.** Two test files pin that they are inert:

- `OrphanedRadioDeviceConfigTests` (Infrastructure). It binds `DeviceOptions` through the real
  `AddSoundFlowAudio` over the real SQLite bridge holding the box's rows, and checks that Vinyl still
  binds `USB Microphone`. It also checks that `DeviceOptionsResolver` never reads either radio key.
- `DeviceConfigOrphanRadioTests` (Web). It uses `GET /api/configuration/devices` **captured verbatim from
  the box** (`a86349f`): `{"radio":{"usbPort":"/dev/ttyUSB0"},"vinyl":{"usbPort":"USB Microphone"},"cast":{"defaultDevice":""},"Radio":{"usbPort":""},"Vinyl":{"usbPort":"USB Microphone"},"Cast":{"defaultDevice":""}}`.
  The Devices tab still loads the stored Vinyl port, and a save writes no radio key.
  `POST /api/configuration/{section}` upserts one key at a time, so the orphan rows are left untouched.

**Mutation checks (this Builder, each run against the tests above, then reverted after committing):**
`ErrorOnUnknownConfiguration = true` on the `DeviceOptions` binding → the binding test fails with
*"properties were not found … 'radio'"*. Resolver reading `devices:Radio` for Vinyl → the resolver test
fails. A `Radio` property re-added to the Web `DeviceOptionsDto` → the save test fails.

### Gates (Windows dev host)

- `dotnet build RadioConsole.sln -c Release --no-incremental`: **46 warnings, 0 errors**, equal to the
  Windows baseline on `main` (`a86349f`), so the deleted code carried no `IDE0011`. Re-run after the
  review fixes: 46 / 0.
- `dotnet test RadioConsole.sln -c Release`: the only failures were the **six** known
  `SrcVariableResamplerTests` (`libsamplerate.so.0`). Per project: Core 186, AudioAnalysis 35, API 501,
  Web 1328, Metrics 28, RTLSDRCore 189, Configuration 115, Fingerprinting 112 (+1 skipped),
  Infrastructure 1912 passed / 6 failed / 2 skipped, Integration 31 (+2 skipped), Web.E2E 28.

### Review

A pre-merge reviewer was briefed to falsify the comment claims and hunt for leftovers. It found **no HIGH
and no MEDIUM**. Verified true: the orphan keys are ignored on bind and on Web load and untouched on save;
the resolver never reads them; `SDRRadioAudioSource` is the only production source of type `Radio`;
`RF320` is rejected as unavailable; and no submodule reference remains in csproj, sln, `Directory.Build.*`,
deploy, scripts, `.github` or `pack-local.ps1`. **LOW, all fixed in the PR:** the enum doc wrongly said
PlayHistory stores this enum (it stores `PlaySource`); `SYSTEMCONFIGURATION.md` said orphan rows are
never read (they are loaded, but match nothing); a stale `:268` line reference; README's RTL-SDR
device-volume cell; and two stale doc strings (Bridge example, AudioUAT P3-001). **Pre-existing, filed
as [`AUD-89`](../../docs/queue/AUD-89.md):** the Vinyl key-casing shadow, and the `/dev/ttyUSB1` DTO default.

## ✅ 2026-10-01 — shipped, deployed and agent-verified on the box (coordinator)

This closes the *"Not yet deployed"* section that stood here. The Builder's deploy had been refused by
the session's permission classifier, so it fell to the coordinator.

**Merge.** Owner instruction 2026-09-30: *"merge and deploy AUD-16"*. The coordinator rebased
`fix/aud-16-remove-rf320` onto `16cf71b` (`AUD-76` PR 2). Only the docs conflicted: the "next free"
lines and the queue banner. The code commits applied cleanly. Gates were re-run on the rebased head
`929f953`:

- `dotnet build RadioConsole.sln -c Release --no-incremental`: **46 warnings, 0 errors**.
- Full `dotnet test`: AudioAnalysis 35, Metrics 28, RTLSDRCore 224, Web.E2E 28, Core 186,
  Configuration 115, Web 1391, Fingerprinting 112 (+1 skipped) and API 509 all passed. Infrastructure:
  1949 passed, plus the six known `SrcVariableResamplerTests` failures (`libsamplerate`).
  IntegrationTests: 30 passed, plus the known live-network
  `NwsObservationIntegrationTests.RealNwsCall_ReturnsForecast_WithCurrentObservation`.

[#743](https://github.com/mmackelprang/RTest/pull/743) was squash-merged as **`a4bad7c`**.

**Deploy.** `a4bad7c` was deployed from a detached `origin/main` checkout at about 21:51 EDT on
2026-09-30. The deploy printed `Verified: API is running commit a4bad7c`,
`Verified: Web is running commit a4bad7c` and
`Kiosk is live (12 established connections to :5002, radio-kiosk.service=active)`. Both services'
`/api/health/version` report `a4bad7c`.

**UAT on the box (agent-verified, not owner-run).** Results against the checks this dossier listed:

1. *SHA and kiosk:* as above. `NRestarts` was not recorded in the evidence handed over; it was
   measured afterwards, see § Follow-up checks below.
2. *Sources and devices:* `/api/sources` lists the same five primary sources: Radio, Vinyl, FilePlayer,
   GenericUSB and Bluetooth. `GET /api/radio/devices` returns exactly one device, `RTLSDRCore`
   (count 1). The RF320 is gone.
3. *UI at 1920×720:* the previous Builder's Playwright probe (`uat_sources.py post`) was run against
   `http://radio:5002`.
   - The Devices tab has only one field, "Vinyl Audio Device: USB Microphone". The
     "Radio USB Audio (RF320 only)" field from the pre-change baseline is gone.
   - The Radio tab device dropdown has one option, "RTL-SDR". The baseline had RTL-SDR and Raddy RF320.
   - Neither tab mentions the RF320, and there were zero console errors.
   - The five source bubbles were not separately recorded.
4. *Source switching* via `POST /api/sources`, read from the file sink `radio-20260930.txt`:
   - Vinyl 21:52:22, FilePlayer 21:52:26, Bluetooth 21:52:34 and Radio 21:52:38 each logged
     `Successfully switched to source`.
   - Two switches interleaved with these, 21:52:29 → Radio and 21:52:35 → FilePlayer. They did not
     come from this check. ⛔ **Corrected 2026-10-01:** this said they came from *"a concurrent
     Builder's Cast UAT"*. That was wrong. Batch D (the Cast arc) made no box changes before 22:03
     EDT. Both switches came through `POST /api/sources`, and the log has no client attribution for
     them. They are **unattributed**. The most likely source is the owner at the panel, who was at the
     console sending feedback at that time.
   - The USB Audio refusal (as accepted in `AUD-13`) was not exercised in this pass; it was run
     afterwards, see § Follow-up checks below.
5. *RTL-SDR after the restart:* `/api/radio/state` reports frequency 92300000, band FM, signalStrength 100.
   The persisted `CurrentSource=Radio` was restored at start-up (`Activating persisted source: "Radio"`,
   then `Source "Radio" activated on startup`). The retune step was not run in this pass (no
   `POST /api/radio/frequency` to another station and back); it was run afterwards, see § Follow-up
   checks below.
6. *Start-up warnings 21:51:00–21:52:15:* only the usual set appeared. That is
   `Saved input device "capture-4" not found`, the GvMedia AuthKey notice, no connected BT device, the
   Kestrel address override, https-port, and two GC deadline misses. There was nothing about Radio
   device config or binding.

The owner's state is unchanged: SDR Radio 92.3 FM, volume 0.3, muted, output Soundbar, and no default
Cast device.

Nothing is left for the owner on this row.

### Follow-up checks — run by the coordinator 2026-09-30 ~22:2x EDT

The three checks recorded above as not run are now covered. The first is batch D's own measurement.
The coordinator ran the other two on 2026-09-30 at about 22:2x EDT, on `45a220e` (`AUD-54`, #748), which
contains `AUD-16`'s `a4bad7c`:

1. **`NRestarts=0` on `a4bad7c`.** Batch D measured it before its own deploy of `45a220e`, so `a4bad7c`
   ran without a restart. A reading taken after `45a220e`'s deploy would say nothing about `a4bad7c`.
2. **USB Audio refused.** `POST /api/sources {GenericUSB}` returned HTTP 500
   `{"error":"Failed to create source type GenericUSB","details":"Generic USB Audio: no capture device matches USBPort 'AB13X'"}`.
   The active source stayed Radio. That is the refusal accepted under `AUD-13`.
3. **SDR retune.** `POST /api/radio/frequency {"frequency":100100000}` returned 200, and the state
   read 100100000 with signal 100. Back to 92300000 returned 200, with signal 98.
