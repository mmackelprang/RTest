# `AUD-95` — a frequency-only tune into another band keeps the old band's step

[← Builder Queue index](../BUILDER_QUEUE.md)

🟢 **P3.** Filed 2026-10-01 by the `AUD-91` Builder. **The symptom was MEASURED on `radio` (deployed
`590c40d`) during `AUD-91`'s feasibility pass; the mechanism is read from code on
`feat/aud-91-band-aware-sweep`.**

## What was seen

After a WB tune followed by `POST /api/radio/frequency` to an FM frequency, `/api/radio/state` reported
band FM and **`step: 25000`**, WB's default. FM's default is 100 kHz. The step buttons then move FM by
25 kHz.

## Why

- `SDRRadioAudioSource.SetFrequencyAsync` (`src/Radio.Infrastructure/Audio/Sources/Primary/SDRRadioAudioSource.cs:359-367`)
  calls `RadioReceiver.SetFrequency`.
- `RadioReceiver.SetFrequency` (`src/RTLSDRCore/RadioReceiver.cs:362-371`) finds the band for the
  frequency with `BandPresets.FindBandForFrequency` and calls `SetBand`, so the **receiver's** band (range,
  modulation, bandwidth) changes.
- The source's `_frequencyStep` is reset to the band default only by `SetBandAsync` (`:510`) and by
  `AUD-91`'s `TuneInBandAsync` (`:548`). `SetFrequencyAsync` leaves it alone.

Callers that can cross bands this way:

- `POST /api/radio/frequency` without `band` (the radio control panel's and Radio page's direct
  frequency entry: `RadioControlPanel.razor:1272`, `RadioPage.razor:404`).
- Preset recall: `RadioController.cs:803` and the encoder's `PresetSelectorService.cs:453`. A preset on
  another band is the likely real-world trigger.
- `SourceSelectorService.cs:264`.

Not affected: start-up restore (`SDRRadioAudioSource.cs:947`), which restores the saved step right after
(`:952-954`). The `AUD-91` BAND view's non-FM tap, which tunes with an explicit band.

## Fix options

1. In `SetFrequencyAsync`, compare the receiver's band before and after the tune and reset
   `_frequencyStep` to the new band's `DefaultStepHz` when it changed. A tune inside the same band keeps a
   user-chosen step.
2. Resolve the band first and route a band-crossing tune through `TuneInBandAsync`.

Either way, a user-set step within the same band must survive a frequency tune.

## Verification

A unit test on `SDRRadioAudioSource` with the fake receiver: WB → FM frequency tune leaves step 100 kHz;
FM 100 kHz step set to 200 kHz, then an FM-to-FM frequency tune keeps 200 kHz. On the box (muted): WB,
then a preset on FM, then `/api/radio/state` reads `step: 100000`.
