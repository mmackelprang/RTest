# `AUD-99` — HF on the NESDR SMArt v5: on-box direct-sampling probe, then the device layer (direct sampling, tuner type, real range, tune failures)

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2, first of the all-band rows** (`AUD-99` → `AUD-101` → `AUD-102` → `AUD-103`, with `AUD-104` after
`AUD-99`). Filed 2026-10-11 by the Planner from the owner's instruction to *"get AM and SW working throughout
the system"* now that the hardware `AUD-94` was waiting for is installed. The code facts below were read on
`main` at `2a2893d2`. The box facts are marked as measured or as reported by the owner.

## Hardware (installed 2026-10-11)

- **Dongle:** Nooelec **NESDR SMArt v5** on `radio`. `lsusb`: `0bda:2838`, iManufacturer `Nooelec`, iProduct
  `NESDR SMArt v5`, serial `56001418`. This is option C in
  [`known-issues-and-future-work.md` § All-band reception](../known-issues-and-future-work.md). FM plays on it
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

- Tune the reference AM station from Step 0. The radio is in Q mode and **something** of that station is
  audible. It may sound rough until `AUD-101` lands: this row proves the RF path, not audio quality.
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
