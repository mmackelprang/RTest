# `AUD-99` — HF on the NESDR SMArt v5: on-box direct-sampling probe, then the device layer (direct sampling, tuner type, real range, tune failures)

[← Builder Queue index](../../docs/BUILDER_QUEUE.md)

✅ **UNBLOCKED 2026-10-11 by owner ruling: the step 0 outcome is an environment limit, not a fault.** Owner, relayed
by the coordinator on 2026-10-11: the AM antenna *"is the one that comes with the SDR dongle. It's unpowered and
passive"*. He hears no AM stations on a separate AM radio and no SW on other hardware either, so *"you probably
won't find a real station. Don't make tuning to a real station a requirement for developing. This same thing
goes for SW band"*. On the bias-tee: *"the antenna is **not** powered, so switching on the antenna supply won't
have any effect"*. **Do not enable it.**

**Step 0 outcome, as ruled:** the dongle side of the direct-sampling path is **confirmed**. Mode read back as 2,
tuner type 5 (R820T), USB product `NESDR SMArt v5`, and the Q input is live, about 30 dB above the I branch. **No
station is receivable locally**, which is an environment limit, not a fault. The 960 kHz carrier (28.8 MHz ÷ 30)
is a **known crystal-harmonic artifact**. Do not chase it.

**Acceptance replaces "a real station heard" for this row and for `AUD-101`–`AUD-103`:**
- **(a) Deterministic tests.** Mock-device tests show that direct sampling switches **on for AM/SW and off for
  every other band**, in both the radio path and the band-map sweep paths. `AUD-101` adds a synthetic AM
  carrier + tone, an off-channel interferer and a DC offset at the tuned centre through the full
  `RadioReceiver` chain, asserting tone recovery, adjacent-channel rejection and AGC level.
- **(b) On-box checks that need no station,** on the deployed SHA:
  - selecting AM and SW engages Q mode (log line or readback);
  - tunes return success;
  - audio is low-level noise rather than silence, an error or a crash;
  - no `PLL not locked` appears;
  - switching back to FM restores FM stereo with RDS;
  - BAND view AM/SW sweeps complete and render.

  Keep `journalctl` reads bounded. ⚠ Stopping `radio-api` also stops `radio-web`, so check both after every
  deploy.

_(Was, briefly on 2026-10-11:)_ 🚫 BLOCKED: the step 0 probe received nothing usable (see § *Step 0 results*
below).

🟡 **P2, first of the all-band rows** (`AUD-99` → `AUD-101` → `AUD-102` → `AUD-103`, with `AUD-104` after
`AUD-99`). Filed 2026-10-11 by the Planner from the owner's instruction to *"get AM and SW working throughout
the system"* now that the hardware `AUD-94` was waiting for is installed. The code facts below were read on
`main` at `2a2893d2`. The box facts are marked as measured or as reported by the owner.

## Hardware (installed 2026-10-11)

- **Dongle:** Nooelec **NESDR SMArt v5** on `radio`. `lsusb`: `0bda:2838`, iManufacturer `Nooelec`, iProduct
  `NESDR SMArt v5`, serial `56001418`. This is option C in
  [`known-issues-and-future-work.md` § All-band reception](../../docs/known-issues-and-future-work.md). FM plays on it
  today: `radio-api` opened it at 16:23 on 2026-10-11, logged as `Generic RTL2832U OEM`. That is librtlsdr's
  VID/PID name table (`rtlsdr_get_device_name`), not the EEPROM product string.
- **Antennas (owner, 2026-10-11):** the dongle is fed from an **HF/VHF diplexer**, with an **AM-type antenna on
  the HF leg** and an **FM antenna on the VHF leg**. The antenna is installed, so this is not an open question.
- **Expect an empty band.** The owner says there are few AM stations in the area. *"Most of the AM band is
  empty"* is the expected result, not a failure. Every AM check uses the **strongest local station the probe
  finds** as its reference.
- **Box tooling (measured read-only 2026-10-11):** `rtl-sdr 2.0.1-2build1` tools are installed (`rtl_fm`,
  `rtl_power`, `rtl_sdr`, `rtl_test`), along with `librtlsdr.so.2.0.1` and the unversioned `librtlsdr.so`
  symlink. `python3` is installed but **`numpy` is not**. `rtl_fm -E direct2` enables **Q-branch** direct
  sampling. `rtl_power -D` is a plain on/off flag. In the osmocom source that flag selects mode 1 (the I
  branch), which is believed to be the wrong branch for this dongle. Not verified: confirm it before using
  `rtl_power -D` for the survey.

## Step 0 — on-box feasibility probe (do this BEFORE writing code)

The library side is documented, but nobody has yet heard this dongle receive MW in Q mode. Prove it first. If
the probe fails, stop and report: every later row depends on it.

1. **Free the device.** `radio-api` holds the dongle while the radio source is active, and the idle band sweep
   opens it at other times. Run `sudo systemctl stop radio-api`. The console goes quiet and the kiosk shows the
   API as disconnected for the duration. Keep the stop short (a few minutes), and tell the owner beforehand if
   he may be listening.
2. **Power survey of 530–1710 kHz in Q mode.** Use a method that really selects mode 2. Options: `rtl_fm -E
   direct2 -M am -f <f> -s <rate>` stepped across the 10 kHz grid, recording a second at each step and taking
   its RMS. Or a short `ctypes` script against `librtlsdr.so` that calls `rtlsdr_set_direct_sampling(dev, 2)`
   and computes in-channel power from the IQ (pure Python, because there is no numpy; a few hundred samples per
   channel is enough). Record the strongest few carriers.
3. **Listen to the strongest one.** Run `rtl_fm -E direct2 -M am -f <strongest> -s 12k ...` piped to a short WAV
   file (or `aplay`, if the owner is present). Confirm by ear or by the recording that it is a station, not
   noise or a spur. If anything plays through the speakers, keep it to a few seconds at low volume.
4. **One SW spot check (time-of-day dependent).** Try one or two strong international broadcasters in the
   49 m (5.9–6.2 MHz), 31 m (9.4–9.9 MHz) or 25 m (11.6–12.1 MHz) band at a time they are known to propagate
   (49 m and 31 m in the evening and at night, 25 m in daytime). A silent SW band at the wrong hour is not a
   failure. Record the UTC time with the result.
5. **Restore and re-verify FM.** `sudo systemctl start radio-api`. Then check `curl -s
   http://radio:5000/api/radio/state` shows the radio back on its FM frequency, play FM for a moment, and
   confirm the kiosk reconnected (`./deploy/Deploy-ToLinux.ps1 -VerifyOnly` is the four-second check). **Do
   not leave the box with `radio-api` stopped.**
6. **Keep log reads bounded.** Use `journalctl -u radio-api --since '-10min' --no-pager`, never a tail.
   Journal volume correlates with audio distortion on this box.

Write the results (stations found, levels, UTC times, whether Q mode needed anything else) into this dossier
under a dated heading before starting step 1.

## Step 0 results — 2026-10-11, 21:02–21:06 UTC (Builder): ⛔ FAILED, nothing usable received; row blocked

**Verdict: in Q-mode direct sampling, this dongle and antenna received no AM station and no SW signal.** The only
carrier in the AM band is a **960 kHz spur**, consistent with a 28.8 MHz crystal harmonic. Per this row's rule (*"If the probe fails, stop and
report"*), no code was written and `AUD-101`–`AUD-103` stay blocked behind it.

**Method.** `radio-api` was stopped from 21:02:25 to 21:06:25 UTC (4 min). ⚠ **`radio-web` stops with it**
(its unit depends on the API) and **must be started separately**. `systemctl start radio-api` alone left it
`inactive`, which step 5 above does not say. A pure-Python `ctypes` probe against `librtlsdr.so` (no numpy)
opened the dongle once and called `rtlsdr_set_direct_sampling(dev, 2)`, confirmed by `get_direct_sampling` = 2 and
librtlsdr's `Enabled direct sampling mode, input 2`. It set 240 kS/s and, for each 10 kHz channel, tuned the DDC
to `f + 30 kHz`, mixed the carrier back to DC in software and averaged it over a 5 kHz box (and in a second
pass, a 500 Hz box). Every `set_center_freq` returned 0, and every read returned the full buffer. Identity as
reported by the library: tuner type 5 (`R820T`), USB strings `Nooelec` / `NESDR SMArt v5` / `56001418`.

**Results.**

| Check | Result |
|---|---|
| AM survey 530–1710 kHz, Q, RTL digital AGC off | Wideband IQ power 6.4–11.4 dB (the probe's dB is 10·log10 of the mean IQ power in ADC counts, so about 2–4 counts RMS); the input is nearly starved. One channel stands out: **960 kHz, +18.0 dB** over the median. Next: 1520 +7.0, 550 +6.8, 1370 +6.5, and a broad hump across 1320–1440 at +2.9 to +6.5 |
| Same survey, AGC on | Everything rises about 17 dB uniformly, with the same shape: 960 +18.2, 1520 +7.8, 1370 +6.9 |
| **Spur control: I branch** (assumed unwired on this board, not measured), 900–1020 kHz | **960 kHz at 24.3 dB, against 25.3 dB on Q**: the same absolute level on the branch the antenna does not feed. 28.8 MHz ÷ 30 = 960 kHz. **Consistent with a crystal harmonic, not a station.** The I floor is about 30 dB below Q, so the antenna does deliver broadband energy to Q |
| Narrow filter (500 Hz), AM, Q, AGC | A carrier keeps its power and noise drops: 960 rises to **+29 dB**, while the 1320–1440 hump stays at +5.1 to +7.3 (noise) |
| `rtl_fm -E direct2 -M am -s 12k`, 7 s each at 960, 1520, 1370, 550 and 1180 (empty control) | 100 ms block-RMS coefficient of variation 0.02–0.03 on all five (control 0.02); mean RMS 43–55, except 1370 at 93. ⚠ **This instrument was not shown to be sensitive:** the 960 recording's RMS (45) equals the control's (45) despite a +18 dB carrier there. Treat it as weak supporting evidence only |
| 1 kHz DFT of the whole ±110 kHz window around **600 kHz** (Q), and the same on I | Q (median 40.2): top bins +8.9 to +10.8 dB at 592, 580, 543, 552, 564, 535, 540 and 506 kHz. Two of them (580, 540) are on the 10 kHz grid, but at the same level as the off-grid bins, so they are indistinguishable from the expected maximum of about 220 noise bins. **Nothing at 600 kHz** (WSJS, listed as 5 kW in Winston-Salem; an external fact, not measured). I (median 13.5): top bin +7.8 |
| Same DFT around **1 MHz** (Q) | Only the 960 kHz spur (+40.4 dB) and a 968 kHz carrier (+17.4, unexplained). Nothing else on the AM grid |
| SW at **21:05 UTC** (17:05 EDT, daytime): ±20 kHz around WWV 2.5, 5, 10, 15 and 20 MHz, Q, AGC, 500 Hz filter | No carrier. The five readings in each run climb monotonically (cause not established; consistent with AGC settling after process start). Channel power tracks wideband at −27 to −28 dB at the start and end of every run, which is the 500 Hz / 240 kHz noise ratio (≈ −26.8 dB). The one exception is 15.010/15.020 MHz, at −25.0/−23.8 dB: up to 3.5 dB over the ratio, beside WWV rather than on it (15.000 is −25.5). 25 m / 31 m broadcasters were not scanned separately. A daytime null on 2.5/5 MHz is normal; 10 and 15 MHz WWV are usually audible in the eastern US at this hour |

**Restored and re-verified (from 21:06:25 UTC).** `radio-api` and then `radio-web` were started and are `active`.
`/api/radio/state` was back on FM 97.745 MHz, stereo, RDS `WSMW` with live RadioText, and `nowplaying` showed
`isPlaying: true`. `Deploy-ToLinux.ps1 -VerifyOnly` found the kiosk live with 4 established connections. Its SHA
"MISMATCH" (box `2a2893d`, `main` `8e8addb`) is docs-only: `git diff --name-only 2a2893d 8e8addb` touches only
`docs/` and `archive/`. The warnings in the bounded journal read are start-up lines (BT not connected, BT health
Degraded, `capture-4` missing, the GvMedia key, the Kestrel address override, https redirect) and the
shutdown errors seen at this stop (`Error stopping Bluetooth adapter` / `ObjectDisposedException` ×4, a
canceled capture). None names the SDR or the probe.

**What this does and does not show.**
- It shows that **the RF path does not deliver usable HF into the Q branch**. The Q branch is live (its floor
  sits 30 dB over I's), but no carrier on the AM grid rises above that floor other than the 960 kHz spur. The area is
  believed (external knowledge, not measured) to have several kW-class daytime AM stations.
- It does **not** show that direct sampling is impossible on this dongle. Untested causes, cheapest first:
  (a) the "AM-type antenna" may be a **passive loop meant to couple to a radio's ferrite bar**, or an **active
  loop that needs power**. Either one delivers almost nothing into 50 Ω. (b) The diplexer's HF/VHF legs may be
  swapped, or the HF leg may roll off below a few MHz. (c) Direct sampling on the RTL2832 has no front-end
  gain, so it needs a long wire (tens of feet) or an amplified antenna to hear MW. (d) The SMArt v5's
  software **bias-tee** could power an active antenna. ⚠ It was **deliberately not switched on**: 4.5 V DC into
  a diplexer and an unknown antenna is a hardware risk for the owner to rule on.
- **Suggested next step (owner):** confirm what the HF antenna is and how it is wired, or try a few metres of
  plain wire on the diplexer's HF port. Then re-run this probe. The scripts are short and can be re-created
  from the method above. A carrier at a known local frequency (600, 950, 1320, 1400 kHz…) that **does not
  also appear on the I branch** is the pass condition.

## Measured facts (code, `main` `2a2893d2`)

- **Fixed range.** `RtlSdrDevice.TunerMinFrequencyHz = 24_000_000` (`src/RTLSDRCore/Hardware/RtlSdrDevice.cs:19`)
  and `TunerMaxFrequencyHz = 1_766_000_000` (`:25`) are reported as `DeviceInfo.MinFrequencyHz/MaxFrequencyHz`
  (`:115-116`), and the tuner is never queried. `MockSdrDevice.cs:73` and `SdrDeviceFactory.cs:43` repeat the
  24 MHz floor, and `MockSdrDevice.SetFrequency` rejects anything below it (`:167`).
- **Tuner type is hard-coded.** `TunerType = "RTL2832U"` (`RtlSdrDevice.cs:114`), which is the demodulator chip,
  not the tuner. There is no P/Invoke for `rtlsdr_get_tuner_type`.
- **The product string is discarded.** `rtlsdr_get_device_usb_strings` fills a `product` buffer
  (`RtlSdrDevice.cs:101-104`) that is never stored. `DeviceInfo` has no `Product` field
  (`src/RTLSDRCore/Models/DeviceInfo.cs`). The product string (`NESDR SMArt v5`) is the only thing that tells
  this dongle apart from a generic R820T one with an unwired Q branch.
- **No direct-sampling or offset-tuning P/Invoke.** The `NativeMethods` region (`RtlSdrDevice.cs:571-625`) has no
  `rtlsdr_set_direct_sampling`, `rtlsdr_get_direct_sampling` or `rtlsdr_set_offset_tuning`. `ISdrDevice`
  (`src/RTLSDRCore/Hardware/ISdrDevice.cs`) has no HF-mode member.
- **Tune failures are swallowed.** `RadioReceiver.SetFrequencyInternal` (`src/RTLSDRCore/RadioReceiver.cs:436-470`)
  logs a Warning when `_device.SetFrequency` returns false and then **always returns true**. `Startup`
  (`:211-217`) and `SetBand` (`:778`) ignore the results of `SetSampleRate` and `SetFrequency`.
  `SDRRadioAudioSource.SetFrequencyAsync` (`src/Radio.Infrastructure/Audio/Sources/Primary/SDRRadioAudioSource.cs:370-378`)
  already throws `ArgumentOutOfRangeException` on false, but it can never see a false from the hardware path.
  ⚠ Once failures propagate, a *hardware* failure must not surface as a 400 "out of range": that reads as user
  error.
- **Three paths open or tune the device, and each needs the HF switch:**
  1. the radio: `RadioReceiver.Startup` (`:199-217`), `SetBand` (`:757-793`, which stops streaming, re-runs
     `SetupSignalProcessing` and sets the sample rate), and `SetFrequency` via `FindBandForFrequency` → `SetBand`
     (`:362-371`);
  2. the live band sweep inside the receiver: `RunLiveSweep` (`:1166-1240`, which sets gain and retunes in its
     `finally`) and `LiveSweepTuner.Tune` (`:1342`);
  3. the idle sweep: `DeviceSweepTuner.Open/Tune` (`src/RTLSDRCore/Sweep/DeviceSweepTuner.cs:44-63`), created by
     `BandMapService.RunIdle` (`src/Radio.Infrastructure/Audio/Services/BandMapService.cs:748`) from
     `CreateFirstRtlSdrOrNull` (`:1078-1083`).
- **librtlsdr behaviour to design around (osmocom 2.0.1):** with direct sampling on,
  `rtlsdr_set_center_freq` programs the RTL2832's IF/DDC instead of the tuner, and the tuner is put in standby,
  so tuner gain calls stop meaning anything. Each gain call that fails today logs at **Error**
  (`RtlSdrDevice.cs:345-349`, `:317-321`), and Error reaches journald. In HF mode, skip or neutralise tuner gain
  rather than log an error on every tune. Offset tuning is refused in direct-sampling mode. That is why the
  carrier offset is handled in software in `AUD-101`, not here.

## Scope

1. **P/Invoke:** `rtlsdr_set_direct_sampling(dev, int on)` (0 off, 1 I, 2 Q), `rtlsdr_get_direct_sampling`,
   `rtlsdr_get_tuner_type` (map the enum to a name: R820T covers R820T/R820T2/R860), and keep the **product**
   USB string in a new `DeviceInfo.Product`.
2. **An HF mode on `ISdrDevice`.** For example `bool SupportsHfDirectSampling { get; }` plus
   `bool SetHfMode(bool on)`, implemented by `RtlSdrDevice` and `MockSdrDevice`, plus any test fakes the suite
   uses. **On for AM and SW, off for every other band**, in all three paths above. The switch happens with
   streaming stopped (`SetBand` already stops it) and **before** the centre frequency is set. The live sweep
   must not leave the receiver in the wrong mode: check whether a live sweep can ever target a band other than
   the radio's (`BandMapService.PlanFor` `:1060-1064`). If it can, the live path refuses a sweep that needs the
   other mode. `AUD-103` owns the sweep plans, but the mode switch on all three paths belongs here.
3. **Which dongles get HF.** A generic R820T dongle's Q branch is not wired to the SMA (the `AUD-94` box), so
   HF cannot be assumed from the chip. Recommended: a config key (for example `RTLSDR:HfDirectSampling` =
   `Auto`|`Q`|`I`|`Off`, default `Auto`), where `Auto` turns on Q mode when the product string matches a
   known-good list (`NESDR SMArt v5`). Read it through the existing options and config-store path, and **log
   the decision once at startup** (Information on `radio-api` goes to the file sink only). ⚠ Do not put the
   box's value in `deploy/*/appsettings.Production.json` and expect it to arrive. That overlay is seed-only
   (`CLAUDE.md`).
4. **Real device range.** `DeviceInfo.MinFrequencyHz` comes from the HF decision: about 100 kHz with HF
   (Nooelec spec: 100 kHz – 1.75 GHz), 24 MHz without. Fix the mock's and the factory's copies to match.
   `BandSweepPlans.UnavailableReason` reads the static constants. Leave it reading them for now (`AUD-103` makes
   it device-based) but do not break it.
5. **Tune failures propagate.** `SetFrequencyInternal` returns the device result. `Startup` and `SetBand` fail
   (or report) when the HF switch, sample rate or tune is refused. The API maps a hardware tune failure to a
   server-side error (5xx, or a problem response that says the tuner refused), not to the existing
   `ArgumentOutOfRangeException` → 400. Check every caller of `SetFrequency`/`SetBand` for an assumption that it
   cannot fail, including the encoder's `PresetSelectorService` and `SourceSelectorService`.
6. **Diagnostics.** `/api/radio/devices` (or the existing device listing) shows the product, the real tuner type
   and whether HF is enabled. The startup log names the tuner. Remove the `TunerType = "RTL2832U"` claim.

## Out of scope

AM audio quality (`AUD-101`), band ranges and UI (`AUD-102`), sweep plans (`AUD-103`), fingerprinting
(`AUD-104`). An upconverter LO offset (options A/B) is not needed for this hardware.

## Gates

- `dotnet build RadioConsole.sln -c Release --no-incremental` at the Windows baseline of **46 warnings, 0
  errors** (32 on Linux). Write output to a file and read the per-project summary lines; never pipe to `tail`.
- `dotnet test` green apart from the known-failing set in `CLAUDE.md`.
- Unit tests with the mock and fakes: AM/SW tune → HF on before the frequency is set; FM/AIR/WB/VHF → HF off;
  FM → AM → FM round trip; a refused tune returns false and reaches the API as a server-side error; the idle
  sweep tuner and the live sweep respect the mode; `Auto` with and without a matching product string.

## On-box UAT

Deploy with `./deploy/Deploy-ToLinux.ps1` and check that `/api/health/version` on both services matches
`origin/main` (or the branch head) **before** anyone listens. Then:

- ⚠ **Replaced by owner ruling 2026-10-11** (see the top of this dossier): no real station is required. Selecting
  AM and SW engages Q mode (log line or readback), tunes return success, and the audio is low-level noise rather
  than silence, an error or a crash. _(Was: "Tune the reference AM station from Step 0 ... something of that
  station is audible.")_
- FM is unchanged: the owner's usual station, stereo and RDS behave as before, and nothing new appears in
  `journalctl -u radio-api --since '-10min'` at Warning or above.
- AM → FM → AM switching works repeatedly with no `PLL not locked!` on AM.
- An idle band sweep of FM still completes (`AUD-91`/`AUD-100` behaviour unchanged).

## Depends on

Nothing. ⛔ external: the hardware is installed. `AUD-94` is the parent row.

## Related

`AUD-94` (parent), `AUD-101`, `AUD-102`, `AUD-103`, `AUD-104`. `AUD-95` (a band-crossing frequency tune keeps
the old step) sits on the same `SetFrequency` → `SetBand` path. Keep its fix in mind; it is not part of this
row.

## Implementation notes (Builder, 2026-10-11)

Built on `feat/aud-99-hf-direct-sampling`. Box checks (§ *On-box UAT*, as amended by the owner ruling) are still
to run.

**Device layer (`RTLSDRCore`).**
- `RtlSdrDevice` P/Invokes `rtlsdr_set_direct_sampling`, `rtlsdr_get_direct_sampling` and `rtlsdr_get_tuner_type`.
  The tuner is read at `Open` (it needs a handle), mapped by `HfDirectSampling.TunerName` (5 = `R820T`, which
  covers R820T/R820T2/R860), and cached per dongle so later enumerations report it; before the first open in a
  process it reads `Unknown (not opened yet)`. The hard-coded `TunerType = "RTL2832U"` is gone.
- `DeviceInfo` gains `Product` (the EEPROM iProduct, e.g. `NESDR SMArt v5`) and `HfDirectSampling`
  (`Off`/`I`/`Q`). `MinFrequencyHz` is `HfDirectSampling.HfMinFrequencyHz` (100 kHz) when HF is not `Off`,
  otherwise `RtlSdrDevice.TunerMinFrequencyHz` (24 MHz). The mock's default info and `SdrDeviceFactory`'s mock
  entry are now one object (`MockSdrDevice.CreateDefaultDeviceInfo`): a simulated Q-branch dongle, 100 kHz floor.
  The mock's `SetFrequency` floor follows its HF state (24 MHz in tuner mode). `BandSweepPlans.UnavailableReason`
  still reads the static constants, unchanged.
- `ISdrDevice`: `SupportsHfDirectSampling`, `IsHfMode` (false while closed), `SetHfMode(bool)`, idempotent; on a
  device without HF, `SetHfMode(true)` is false and `SetHfMode(false)` is true. `RtlSdrDevice` reads the mode back
  after setting it and fails on a mismatch. Implemented on `RtlSdrDevice`, `MockSdrDevice`, `FakeSdrDevice`
  (RTLSDRCore.Tests) and the two band-map test devices; Moq mocks need nothing.
- In HF mode the tuner is in standby, so `SetGainMode`/`SetGain` on `RtlSdrDevice` and `MockSdrDevice` return true
  and log at Debug instead of calling librtlsdr. No Error per tune.

**Which bands, on which paths.** `HfBands.Requires(BandType)` is true for AM and Shortwave only (custom bands are
tuner bands). The receiver calls `ApplyHfModeFor(band)`, which makes no device call when the mode already
matches:
- `Startup`: open → `SetupSignalProcessing` → HF mode → sample rate → frequency → gain → streaming.
- `SetBand` (and `SetFrequency` → `FindBandForFrequency` → `SetBand`): stop streaming → HF mode → sample rate →
  frequency → resume. The HF switch comes before the band is recorded, so a refused switch leaves the receiver on
  its old band and frequency, streaming. A custom-band tune from HF mode switches HF off with streaming paused.
- Idle sweep: `DeviceSweepTuner(device, gain, hfMode)` switches the mode in `Open`, before the sample rate and any
  tune; `BandMapService.RunIdle` passes `HfBands.Requires(run.Plan)`.
- Live sweep: **refused, not switched.** `BandMapService.PlanFor` builds the plan of the band *asked for*, and the
  live path measures it with the radio's running receiver, so a request for FM while the radio plays AM does reach
  the live path today (the reverse, AM/SW sweeps, arrives with `AUD-103`'s plans). Switching the mode would mean
  stopping the playing radio's stream and reprogramming the dongle under it, so `RadioReceiver.RunLiveSweep`
  throws `InvalidOperationException` when the swept band needs the other mode (`LiveSweepModeConflict`), and
  `BandMapService` asks first through `ILiveBandSweeper.LiveSweepRefusal` and reports the new reason
  `radio-other-hf-mode` (409 from `BandMapController`, timer evaluations record it as a skip).

**Which dongles.** Config key **`RTLSDR:HfDirectSampling`** = `Auto` | `Q` | `I` | `Off`, **default `Auto`**, set
in `src/Radio.API/appsettings.json` (not in `deploy/*/appsettings.Production.json`). `Auto` is Q when the USB
product string is in `HfDirectSampling.KnownGoodProducts` (`NESDR SMArt v5`, compared case-insensitively after
trimming), otherwise Off. `RTLSDRCore` stays configuration-free: the setting is a constructor/factory argument.
`Radio.Infrastructure.Audio.Factories.SdrHfConfiguration.Read` reads it through `IConfiguration` (so a config-store
row overrides the JSON) each time a device is created, for both `RadioFactory` (the radio) and the DI-supplied
idle-sweep device factory. An invalid value is `Auto` plus one Warning.

**Tune failures.** `SetFrequencyInternal` returns the device's result. `Startup` fails (closes the device, state
`Error`) when the HF switch, the sample rate or the tune is refused. `SetBand` returns false for any of the three
or a failed resume. New `RadioReceiver.TrySetFrequency` returns `TuneResult` (`Success` / `OutOfRange` /
`Refused`); `SetFrequency` is `TrySetFrequency(...) == Success`. A hardware refusal is the new
**`Radio.Core.Exceptions.TunerRefusedException`** (derives from `InvalidOperationException`, which
`TuneInBandAsync` already threw for a receiver fault). `SDRRadioAudioSource` throws it from `SetFrequencyAsync`
(`Refused`), `SetBandAsync` (was `ArgumentException`) and `TuneInBandAsync` (was `InvalidOperationException`);
`ArgumentOutOfRangeException` stays for `OutOfRange` and out-of-band frequencies. **HTTP mapping:**
`RadioController` answers **503 `application/problem+json`**, title *"The tuner refused the request"*, detail the
exception message, on `POST /api/radio/frequency` (both forms), `POST /api/radio/band` and
`POST /api/radio/presets/{id}/load`. Out-of-range is still 400.

**Every caller of `SetFrequency`/`SetBand`, and what changed.**
| Caller | Change |
|---|---|
| `RadioController` frequency / band / preset load | catches `TunerRefusedException` → 503 (Information log) |
| `PresetSelectorService` (encoder preset recall) | catches it → failure card "The tuner refused that station", Information log; other exceptions unchanged |
| `SourceSelectorService` (encoder band commit) | catches it → failure card "&lt;band&gt;: the tuner refused", Information log |
| `RotaryEncoderActionRouter` and `/frequency/up`/`down` (`TuneFrequencyUp/Down`) | none: a step returns false at a band edge as well, so the result stays ignored; a refusal is logged at Warning by the receiver |
| `SDRRadioAudioSource.RestorePersistedRadioPreferencesAsync` | none: it runs before `Startup`, with the device closed, so nothing reaches hardware; it already catches and warns |
| Scan / seek (`ScanInternal`, `ScanStations`) | none: each hop's refusal is logged at Warning by the receiver and the scan carries on; no background loop can throw from it |
| Live sweep cleanup retune (`RunLiveSweep` `finally`) | none: unchanged device call |
| `RadioBandMemoryService` | none: it stores frequencies and does not tune |

The receiver logs a refusal once at Warning; callers that catch the exception log at Information, so a refusal is
one Warning line in journald, not three. (`RtlSdrDevice` itself still logs a native `set_center_freq` failure at
Error, as before.)

**Diagnostics.** `GET /api/radio/devices` gains `hardware[]`: `product`, `tunerType`, `hfDirectSampling`
(`Q`/`I`/`Off`), `hfEnabled`, `hfSetting`, `isHfMode`, `isInUse`, `min/maxFrequencyHz`, plus name, manufacturer,
serial and index (`IRadioFactory.GetSdrHardware`, a default interface member returning empty).

**Other decisions.**
- `MockSdrDevice.SetSampleRate` now also accepts librtlsdr's (225 kHz, 300 kHz] range. The receiver runs at
  240 kHz, which the mock's discrete list rejected; that was invisible while `Startup` ignored the result.
- `RadioReceiver` takes an optional Serilog `ILogger` (the old static logger is the default) so a test can assert
  on its log levels.

**Box checks for the Builder: the log lines.** `RTLSDRCore` is at Information in `src/Radio.API/appsettings.json`
(`"RTLSDRCore": "Information"`, not one of `LOG-2`'s Warning holds), and the API's console sink is Warning-only
(`LOG-11`), so **all of these are in the file sink, not the journal** (`/opt/radio-console/logs/radio-*.txt`):
- once per process, at the first open: `[INF] [RTLSDRCore.Hardware.RtlSdrDevice] RTL-SDR device 0: product NESDR SMArt v5, tuner R820T, HF direct sampling Q branch (RTLSDR:HfDirectSampling=Auto), lower limit 100… kHz`
- each switch to AM/SW: `[INF] [RTLSDRCore.Hardware.RtlSdrDevice] RTL-SDR device 0: HF direct sampling ON (Q branch, mode 2 read back); tuner in standby`
- each switch back: `… HF direct sampling OFF (mode 0 read back); tuner active`

What reaches the journal is Warning only: `The device refused …`, `… refused direct sampling mode …`,
`… reports direct sampling mode … after mode … was set`, `The {Band} band needs HF direct sampling, which this
device does not have …`. None of these should appear on the SMArt v5.

**librtlsdr's own stderr lines are expected.** `Enabled direct sampling mode, input 2` and `Disabled direct
sampling mode` are printed by librtlsdr itself, and stderr reaches journald, so each appears **once per band
change** into or out of AM/SW (not once per tune). They are not a fault.

### Pre-merge fixes (2026-10-11)

From the pre-merge review (checked against osmocom librtlsdr master) and the box UAT on the SMArt v5:

- **H1 — gain after HF off.** librtlsdr's HF-off branch runs the R820T's `init`, which resets its gain
  registers (box: signalStrength 100 after a cold FM start, 44 after FM → AM → SW → FM on the same station).
  Every switch that leaves the device in tuner mode now re-applies the gain mode, and the manual gain when auto
  is off, under the tune lock; the custom-band path also sets the sample rate again (`init` resets the IF filter).
- **M1 — no `PLL not locked!` on AM/SW → FM.** librtlsdr's HF-off branch ends with
  `rtlsdr_set_center_freq(dev, dev->freq)`, and `dev->freq` was still the HF frequency. The receiver now tunes
  the device to the new frequency while HF is still on (only the IF/DDC is programmed then), then switches HF
  off, then tunes as usual. A refused pre-tune is logged at Debug by the receiver (the device layer logs its own Error) and does not fail the switch. If HF off is then refused, the device is tuned back to the receiver's current frequency (re-review finding, fixed before merge).
- **M2 — half-done switch.** On any failed `SetHfMode`, `RtlSdrDevice` reads librtlsdr's recorded mode back (librtlsdr sets it even when a register write fails, so this is not a hardware read), sets the previous
  mode once more if the hardware is not in it, and caches the mode read back last. `ISdrDevice.SetHfMode`'s docs
  say what `IsHfMode` means after a failure.
- **M3 — start-up retry.** `Startup` tries the sample rate and the first tune once more before failing; no wait.
- **M4 — saved AM/SW on a device without HF.** `Startup` falls back to FM (band centre) with one Warning instead
  of failing.
- **S1 — HF was digital silence.** The squelch compared the raw IQ RMS (≈ 0.02 in direct sampling, no tuner
  gain) with 0.1 and always closed. On AM and SW the wideband squelch is no longer applied (`SquelchOpen`
  reports open); mute and the sweep silencing still apply; FM/AIR/WB/VHF are unchanged. `AUD-101` moves the level
  to the filtered channel and applies AGC.
- **L2 — mode at open.** `Open` reads the direct-sampling mode back (`rtlsdr_open` sets mode 1 itself for an
  unknown tuner); idempotence compares mode numbers.
- **L3 — custom band.** Streaming resumes after the tune, not before it.
- **L4 — 409 for a device without HF.** `TuneResult.Unsupported` / `RadioReceiver.TrySetBand` →
  `TunerCapabilityException` (a `TunerRefusedException`) → **409** problem+json *"The SDR device has no HF direct
  sampling"*. A transient refusal stays 503, out of range stays 400.
- **L5 — `IsInUse`.** `RadioFactory` forgets its radio source once it is disposed, so `GetSdrHardware` no
  longer reports a dongle in use after the source is gone.
- **L6 — version.** `RTLSDRCore` 1.0.0 → 1.1.0 (`ISdrDevice` gained abstract members).
- **L7 — threads and typo.** The device mode fields are volatile; `sweepCts =Cancellation…` typo fixed.

**Not in scope, noted:** `POST /api/radio/band` lands on a band-edge frequency (measured on the box: FM → 87.5
MHz, AM → 1710 kHz), not the band's remembered frequency. Band memory is used only by the encoder selectors and
the preferences; this predates `AUD-99` and is not changed here.

## Box UAT — 2026-10-11, 23:36–23:44 UTC (Builder), build `5cc20e5` deployed and SHA-verified on both services

No-station acceptance (owner ruling above). The console was muted by the owner throughout and was left muted, so
audio was measured at the fingerprint tap, which is **before** mute: on FM it read −16.6/−17.1 dBFS while muted,
which validates the instrument. `Radio.Infrastructure.Audio` was raised to Debug for the run (file sink only)
and reset afterwards.

| Check | Result |
|---|---|
| Deploy | `Verified: API is running commit 5cc20e5`, `Verified: Web is running commit 5cc20e5`, kiosk live (12 connections); `radio-api` and `radio-web` both `active` |
| Start-up decision (file sink, INF) | `product NESDR SMArt v5, tuner R820T, HF direct sampling Q branch (RTLSDR:HfDirectSampling="Auto"), lower limit 100.0 kHz`; `/api/radio/devices` agrees |
| Cold-start FM baseline | 97.745 MHz, signalStrength **100** (clip), stereo, RDS `WSMW` |
| AM (`POST /band`) | 200; `isHfMode: true`; `HF direct sampling ON ("Q" branch, mode 2 read back); tuner in standby` (INF); tap **−50.9 dBFS**, full 13 s captures, **0 silence chunks**. Low-level noise, not silence |
| SW (`POST /band`, then tune 9.6 MHz) | both 200; `isHfMode: true`; tap −50.4 / −47.5 dBFS (4 zero chunks during the retune, then 0) |
| SW → FM 97.745 | 200; `isHfMode: false`; signalStrength **100** (clip), equal to the cold start (it was 44 on `341f264`, before H1's fix); stereo, RDS `WSMW`; tap −19.5 / −16.1 dBFS |
| `PLL not locked` | **none** at the SW → FM switch (`341f264` printed two). One at the service start (19:37:34 EDT), which **predates this row**: every `radio-api` start in the last 30 h prints it, including `2a2893d` at 03:01 and 16:23. Not chased |
| FM live sweep | `completed`, path `live`, **101** channels in 20.9 s; radio back on 97.745 MHz in stereo with RDS |
| FM scan requested while AM plays | **409** *"The radio is playing a band that uses the other receiver mode…"* |
| AM ↔ FM, 5 switches in 2 s | all 200; 0 `PLL not locked`; one `Buffer underrun` Warning after the burst (none after the spaced switches; underruns also occur on this box without this row: 1 in the prior 30 h) |
| Journal (bounded) | Expected librtlsdr stderr: `Enabled direct sampling mode, input 2` / `Disabled direct sampling mode`, once per mode change. No new Warning or Error per tune |

**Not checked:** the panel's look on AM/SW (`AUD-102`'s scope), and listening by ear (the console was muted).
**Box left on:** FM 97.745 MHz, log levels reset, the UAT scripts removed from `/tmp`.

### Re-review of the fixes (2026-10-11) and deferrals

A second adversarial review of `341f264..bba19a4f` found **no HIGH**. Its one MEDIUM is fixed before merge: a
refused HF-off after the M1 pre-tune left the device's DDC on the new frequency while the receiver reported the
old one, so the device is now re-tuned to the receiver's frequency (tests:
`SetBand_FromAmToFm_RefusedHfOff_PutsTheDeviceBackOnTheAmFrequency`, and the device-frequency assert in
`CustomBand_FromAm_RefusedHfOff_IsRefused_AndResumesStreaming`). LOWs L-a, L-b and L-c (comments claiming a
"hardware" read-back, an unreachable idempotence claim, and the pre-tune log level) are corrected.
**Deferred, LOW, with reasons:**
- **L-d:** a request that gets 409 (a device without HF, asked for AM/SW) still pauses and resumes streaming and logs
  one Warning. It does not happen on this box (the SMArt v5 has HF), it predates the fixes, and it is cosmetic on a
  dongle without HF.
- **L-e:** the 409 title is written for the only capability that exists today (HF). Revisit if
  `TunerCapabilityException` is ever reused.
- **L-f:** `RtlSdrDevice.RecoverAfterFailedSwitch` has no unit test. It is native-only, so it was reviewed by
  reading.

## ⚠ Open before merge — one FM failure after AM, not reproduced (2026-10-12, 00:15–00:30 UTC, Builder)

✅ **OWNER RULING 2026-10-12: *"Merge + watchdog row."*** The fault is accepted as intermittent and possibly
pre-existing. #800 is merged. The watchdog and instrumentation are filed as [`AUD-106`](AUD-106.md), which carries the
evidence below and the reproduction attempts.

**What happened (build `5cc20e5`).** After about 7 minutes on AM (8 tunes 45 s apart, Debug logging on
`Radio.Infrastructure.Audio`), a `POST /api/radio/frequency` to 97.745 MHz at 00:15:19 UTC switched the radio
back to FM. That switch logged `HF direct sampling OFF (mode 0 read back); tuner active` and restarted
streaming. From then on FM had **no stereo pilot, no RDS (PI null) on 97.745, 94.7 or 92.3 MHz** for more than
two minutes, across three FM→FM retunes, while signalStrength still changed per station (100, 37, 100), so IQ
was flowing. A good return logs `RDS: Block sync acquired` about 0.4 s after streaming restarts; this one never
did. **A service restart (the deploy of `0028577` at about 00:20 UTC) cleared it.**

**Not reproduced on `0028577`, in about 50 transitions:**
- AM → 8 tunes 5 s apart → FM: RDS in 1 s.
- An FM live sweep (gain reads 28, see `AUD-90`) → AM → 3 tunes → FM: RDS in 1 s.
- The full replay, a 6-minute AM dwell with tunes 44 s apart and Debug logging on → FM: RDS in 1 s.
- 6 rounds of rapid AM ↔ FM ↔ SW switching: RDS in 1 s every round.

**Unknown:** whether this predates `AUD-99`. A log heuristic (an FM switch with no `Block sync acquired` within
5 s) proved invalid, because the decoder does not log a re-sync every time, so the old logs cannot answer it.
Candidates not ruled out: R820T/RTL2832 state left by the HF-off path (the `AUD-99` code) on an unlucky timing,
or an older intermittent receiver fault that band changes happen to expose.

**Not merged because of it.** The merge policy pauses on a risk to the FM path. **Recovery if it recurs:** restart
`radio-api` (and then start `radio-web`), or reselect the radio source.

## Nighttime skywave check (2026-10-12, 00:08–00:15 UTC) — inconclusive

The owner hears, on a separate AM radio at night: 1690, 1590, 1500, 1290, 1210, 1190, 1090, 1030, 960, 840, 750
and 710 kHz. Through the API on `5cc20e5`, listed channels (1090, 750, 1190, 710, 1030) and empty ones (1180,
1400, 1650) read the same `signalStrength` 1–2 and `rssiDbu` −59.4/−58.8: that meter is wideband on this build,
which is `AUD-101`'s gap. The tap readings were invalid: no capture ran during the sweep (the fingerprint
schedule had backed off), so every row showed the last FM value. An in-channel re-run of the step 0 probe
needs `radio-api` stopped, and the session's permission policy refused that. It was not worked around.
**Do this on `AUD-101`**, where the level comes from the filtered channel and the same API comparison needs no
outage. The 960 kHz entry is also the dongle's crystal spur (step 0: +18 dB over the median), so it is not
evidence unless it measures clearly above that.
