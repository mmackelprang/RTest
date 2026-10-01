# `AUD-91` — the band sweep and the BAND view follow the band selected in the radio control panel, not just FM

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2 (owner request, GA-nice; not GA-blocking unless the owner promotes it).** Filed 2026-10-01 by the
coordinator from the owner's request on the evening of 2026-09-30 (EDT), after `AUD-76` shipped.
**Not auto-mergeable:** the owner has to UAT it band by band on the box.

## What the owner asked

> *"note that the scan function for the radio seems to only scan the FM band. It should scan and adjust
> the display based on the selected band in the radio control panel."* — 2026-09-30

"The scan function" here is the BAND view's **Scan** and the timer sweep behind it ([`AUD-76`](AUD-76.md)).
The radio control panel's own seek-scan already stays inside the current band: `RadioReceiver`'s scan
steps and wraps on `_currentBand.MinFrequencyHz` / `MaxFrequencyHz` (`src/RTLSDRCore/RadioReceiver.cs:427`,
`:525`). The plan should confirm with the owner that this is the reading he meant before building.

## What is FM-only today (code read 2026-10-01, `main` at `a4bad7c`)

`AUD-76` was specified as an FM band map ([`AUD-76.md`](AUD-76.md) § *What the hardware allows*:
*"AM/shortwave: no direct-sampling support, so FM is the target"*). It shipped in two PRs: PR 1
[#745](https://github.com/mmackelprang/RTest/pull/745) (`6c33ec3`) and PR 2
[#746](https://github.com/mmackelprang/RTest/pull/746) (`16cf71b`). Every layer is FM-only:

- **Channel plan.** `src/RTLSDRCore/Sweep/FmChannelPlan.cs` has the 101 US FM channels, 87.9 to 107.9 MHz
  at 200 kHz. `BandMapService` sweeps `FmChannelPlan.Channels` on both paths
  (`src/Radio.Infrastructure/Audio/Services/BandMapService.cs:409`, `:437`, `:500`) and stamps
  `Band = "FM"` (`:564`).
- **Storage.** One file, `<Database:RootPath>/bandmap/fm.json`
  (`src/Radio.Infrastructure/Audio/Services/BandMapStore.cs:25`). The `BandMap` model and DTOs default
  `Band` to `"FM"` (`src/Radio.Core/Models/BandMap.cs:14`, `src/Radio.API/Models/BandMapDtos.cs:12`,
  `src/Radio.Web/Models/ApiModels.cs:407`).
- **Measurement.** `ChannelPowerMeter` sums FFT bin power from 8 kHz to 80 kHz either side of the
  tuned centre (`src/RTLSDRCore/Sweep/ChannelPowerMeter.cs:61-62`). That window is sized for a 200 kHz
  FM channel. The sweep captures at 240 kS/s (`src/RTLSDRCore/Sweep/DeviceSweepTuner.cs:19`).
- **Display and touch.** `src/Radio.Web/Services/FmBandMath.cs` hard-codes the plot (87.5 to 108.0 MHz,
  `:13-16`), the channel grid (`:19-25`) and the ±0.4 MHz snap (`:28`). `VisualizerPanel.razor` draws
  only FM station and preset markers (`IsFm`, `:670-690`), labels the canvas and the Scan button
  "FM band map" / "Scan the FM band" (`:53`, `:96`, `:128`), and reports *"Tuning … FM"* (`:803`).
- **Tap-to-tune forces FM.** The tap calls `SetFrequency` and relies on `BandPresets.FindBandForFrequency`
  picking FM for 87.9 to 107.9 MHz, because FM comes before VHF in `BandPresets.AllBands`
  (`VisualizerPanel.razor:759-760`, `src/RTLSDRCore/RadioReceiver.cs:362-371`).

## The bands the radio control panel offers

The panel reads `GET /api/radio/bands` (`RadioControlPanel.razor:993`), which projects
`BandPresets` by reflection (`src/Radio.Infrastructure/Services/RadioBandService.cs:27-52`, codes at
`:94-106`). The definitions are in `src/RTLSDRCore/Bands/BandPresets.cs`:

| Code | `BandPresets` | Range | Default step (allowed) | Modulation, bandwidth | Lines |
|---|---|---|---|---|---|
| AM | `AmBroadcast` | 530 – 1,710 kHz | 10 kHz (1, 9, 10 kHz) | AM, 10 kHz | `:14-25` |
| FM | `FmBroadcast` | 87.5 – 108 MHz | 100 kHz (50, 100, 200 kHz) | WFM, 200 kHz | `:30-41` |
| SW | `Shortwave` | 1.6 – 30 MHz | 5 kHz (1, 2.5, 5 kHz) | AM, 6 kHz | `:46-57` |
| AIR | `Aircraft` | 108 – 137 MHz | 25 kHz (8.333, 25 kHz) | AM, 8.333 kHz | `:62-73` |
| WB | `Weather` | 162.400 – 162.550 MHz | 25 kHz (5, 25 kHz) | NFM, 12.5 kHz | `:78-89` |
| VHF | `Vhf` | 30 – 300 MHz | 12.5 kHz (5 – 25 kHz) | NFM, 12.5 kHz | `:94-105` |

VHF overlaps FM, AIR and WB. `FindBandForFrequency` returns the first band in `AllBands` order (AM, FM,
SW, AIR, WB, VHF; `BandPresets.cs:110-118`, `:173-176`), so any frequency inside FM, AIR or WB resolves
to that band and never to VHF.

## Feasibility questions the plan must answer BEFORE building

These are recorded, not decided. Several of them could shrink the row to "FM plus the bands the
hardware can actually receive".

1. **Can this box receive AM and SW at all?** `RtlSdrDevice` reports a fixed tuning range of
   24 MHz to 1.766 GHz (`src/RTLSDRCore/Hardware/RtlSdrDevice.cs:103-104`). That range is hard-coded
   with `TunerType = "RTL2832U"` (`:102`); nothing asks the dongle which tuner it has. It matches an
   R820T-class tuner. `GET /api/radio/devices` on the box (read 2026-10-01) returns capabilities but no
   frequency range. All of AM (0.53 to 1.71 MHz) and SW from 1.6 to 24 MHz are below that floor.
   - RTLSDRCore has **no direct-sampling support**. Its P/Invoke surface
     (`RtlSdrDevice.cs:563-612`) has no `rtlsdr_set_direct_sampling` and no `rtlsdr_set_offset_tuning`.
     `AUD-76`'s dossier says the same.
   - `RadioReceiver.SetBand` clamps to the *band's* range and then tunes (`RadioReceiver.cs:605-638`).
     Only the custom-band path in `SetFrequency` checks the device range (`:374-380`). So selecting AM
     probably asks the tuner for 530 kHz. **Nobody has measured what the box does then:** whether
     `rtlsdr_set_center_freq` fails, or "succeeds" with no usable signal.
   - **Measure AM and SW reception on the box first.** Also find out which tuner the dongle actually has
     (librtlsdr names it when the device opens). If AM and SW cannot be received, sweeping them only
     draws noise. The honest answer may be to show "not receivable on this hardware" for those bands.
     Or adding direct sampling (Q-branch on an R820T, with antenna implications) may become its own
     row. That is an owner decision, not something to fold in here.
2. **Channel plan and step per band.** AM is 10 kHz in the US (119 channels; the 9 kHz step is for
   other regions). AIR is 25 kHz (1,161 channels) or 8.33 kHz (about 3,480). WB has 7 fixed NOAA
   channels, 162.400 to 162.550 MHz at 25 kHz. SW at 5 kHz is about 5,700 channels. **VHF is huge:**
   30 to 300 MHz at 12.5 kHz is about 21,600 channels. Does VHF get a whole-band sweep, a
   sub-range around the current frequency, or no map?
3. **Sweep duration per band.** `AUD-76` measured 101 FM channels in 18.9 s idle and 21.0 s live, about
   0.2 s per hop. At that rate WB takes about 1.5 s, AM about 25 s, AIR about 4 to 12 min, SW about
   19 min and VHF about 70 min. That is muted air on the live path. For the narrow bands, one capture at
   up to 2.4 MS/s covers many channels in a single FFT, so hops are not the only design. The plan must
   choose, and say what that does to the IQ-drop budget (`AUD-76` M3, and the `dropping IQ batch`
   warnings).
4. **The measurement per band.** The FM meter's window (8 to 80 kHz either side, DC excluded below
   8 kHz) cannot be reused as-is. For a 10 kHz AM or 8.33/25 kHz AIR channel it would exclude the
   carrier and integrate neighbours. Each band needs its own window and DC handling, and its own
   adjacent-channel aliasing check (`AUD-76` M3 was measured for FM only).
5. **Storage per band.** `bandmap/<band>.json` beside the existing `fm.json`, so the FM map survives
   unchanged. Is `GET /api/radio/bandmap` per band (`?band=`) or one map for the current band? What
   does a band with no scan yet show?
6. **The BAND view per band.** Axis range and units (kHz for AM, MHz elsewhere, matching
   `RadioBandService.FormatRange`, `:60-81`), tick spacing, preset ticks filtered to the shown band, the
   station marker, and the normalisation (`FmBandMath.NormalizeLevels`' 10 dB minimum span was tuned
   on FM). Which band does the view follow: the panel's selected band, or the radio's current band when
   the radio is not the active source?
7. **Tap-to-tune per band.** The snap window (±0.4 MHz on FM) and the "nearest channel" grid per band.
   The tap must also keep the shown band: a tap on a VHF map at 100 MHz would land in FM through
   `FindBandForFrequency` unless the plan tunes with an explicit band.
8. **Which band the timer sweeps.** Only the selected band? Every receivable band in turn while the
   dongle is idle? The plan should consider how stale a map can be and how long the dongle stays busy.
   A sweep must still never hold the dongle against the radio source (`SdrDeviceGate`).
9. **The live-sweep discipline from `AUD-76` must hold for every band.** That means the sweep-only
   mute (the user's mute is never written), no `FrequencyChanged`, retuning to `_currentFrequencyHz`
   in the `finally`, cancellation on a user tune, band, seek-scan or gain change, and gain-mode
   restore. Watch `AUD-90` (the 28 dB readout after a sweep): longer sweeps on more bands make it
   matter more. Also watch the `No audio samples captured after 15020ms` warning that a live sweep can
   cause (`AUD-76` PR 2 UAT): a sweep longer than 15 s will trigger it every time.

## Verification

Unit tests per band for the channel plan, the measurement window and the tap resolution, using the
fake device. On the box, for each band the plan keeps: a map whose peaks match known signals (FM
stations, the local NOAA channel, airport traffic if any); a requested live scan that is silent and
returns to the station; and switching to Radio mid-sweep, which must still play within a second.
**Owner UAT per band at the console.**

Suggested branch: `feat/aud-91-band-aware-sweep`.
