# BT-to-Cast clock-skew measurement & architectural analysis

**Status**: complete (measurement + architectural analysis).
**Author**: Mark + Claude, 2026-05-22.
**Motivation**: While streaming BT → Cast on `mmack@radio`, Mark reported "underwater" / "slow" audio at irregular but recurring intervals. The cast/BT research arc's Phase 1+2 instrumentation didn't catch this — it was built for FM-BT-3 (silent capture quiescence), not for clock-rate mismatch artifacts. This doc captures what we measured, the architecture analysis that explains it, and the implications for fix selection.

## Source data

Live measurements taken after PR #400 (BufferedSoundGenerator observability) deployed to `radio`. Probe: `scripts/research/bt_drift_analyze.py` ingesting `journalctl -u radio-api -o short-iso`. Two consecutive windows:

```
15-minute window (post-deploy):
  Underrun events:      13       (52.6/hour, 36 KB samples/hour)
  Compensation events:  10       (40.4/hour, 39 KB samples/hour)
  Net buffer deficit:   20.8 samples/sec
  Estimated clock skew: ~217 ppm

5-minute window (most recent):
  Underrun events:      3        (37.2/hour)
  Compensation events:  10       (124.1/hour)
  Net buffer deficit:   37.5 samples/sec
  Estimated clock skew: ~391 ppm
```

The 5-minute window has higher per-hour rates because the 15-minute window includes early post-deploy time before all metrics fully started flowing. The ~217 ppm vs ~391 ppm range is the same phenomenon viewed across two window scales; steady-state is closer to ~250–400 ppm.

## Reference frames

- **BT A2DP spec tolerance**: ±20 ppm crystal accuracy for the encoder's clock
- **Measured skew on this session**: 217–391 ppm — **10–20× over spec**
- **Implication**: this is not raw phone-clock drift alone; another contributor is in play

## Audible mechanism

`BufferedSoundGenerator<float>.CompensateClockDrift` (`src/Radio.Infrastructure/Audio/SoundFlow/BufferedSoundGenerator.cs:477-545`) triggers when the buffer drains below 15 % of capacity. It "rewinds" `_readPos` by `deficit` samples (capped at 10 ms / 480 samples), causing the most-recent 10 ms of audio to be replayed.

To the listener, replaying 10 ms of audio sounds like a brief pitch slide / time-stretch / **"underwater"**. At ~120 compensation events / hour observed, that's roughly **one ~10 ms stretch every 30 seconds** — exactly Mark's reported cadence.

When even the 10 ms-per-2 s compensation can't keep up, the buffer hits zero and `Buffer underrun (Single)` warns — zero-fill silence is inserted. That's the "second tier" symptom — brief silence punctuating the stretches.

## Architecture — the consumer is the master mixer, not Cast

A correction to the original Phase 2 plan framing: when I drafted four mitigation options ("Path A/B/C/D"), Path C was named "Cast consumer paces to producer rate" — assuming the Cast HM HTTP stream is the consumer of BT samples. Re-reading the audio graph:

```
BT phone (clock A)
  ↓ pw_stream → OnProcess delivers samples
BufferedSoundGenerator<float> (ring buffer)
  ↓ Process(buffer) — pulled at PlaybackDevice clock rate
MasterMixer.Process — modifiers chain
  ↓ TappedOutputStream.WriteToOutputTap (a Modifier)
  ↓ also → PlaybackDevice → local speaker (clock B, but volume=0 if Cast selected)
TappedOutputStream ring buffer
  ↓ HttpStreamOutput.HandleClientAsync — read at wall-clock pace
LAME MP3 encode
  ↓ HTTP/1.1 chunked
Chromecast (clock C, decodes + plays via its own clock)
```

The **consumer of BT samples is the MasterMixer**, driven by **PlaybackDevice's clock** (a fixed crystal-locked local-speaker clock; we cannot change its rate). The Cast HTTP stream sits **downstream of the master mixer** as a tap — it cannot affect how fast the mixer pulls from BT.

So **Path C as originally framed** ("Cast consumer paces to producer rate") **cannot affect the BT-input underrun rate**. Whatever pacing changes we make at the Cast HTTP layer would only change the TappedOutputStream consumption pattern, not the BT BufferedSoundGenerator's drain rate.

The skew is **between clock A (phone) and clock B (local speaker)** — two independent crystals that are off by 217–391 ppm. There is no software fix at the "Cast pacing" layer for that.

## What CAN actually mitigate the BT-input underrun

Three real options, ordered roughly by effort:

1. **Refine the existing `CompensateClockDrift` algorithm** — smaller per-event corrections more frequently, with crossfade smoothing. The 10 ms duplication is hard to mask because the discontinuity at the rewind point is abrupt. A 1-2 ms duplication done 5× more often, with a linear crossfade across a few samples, can drop the per-event artifact below the perceptual threshold. This is a **reinterpretation of "Path C"** — an algorithmic improvement, not a routing change.

2. **Real variable-rate resampling** ("Path D") — apply a proper sample-rate converter on the BT input that runs at `phone_rate / speaker_rate` ratio (~0.99975 in this case). Eliminates audible stretches entirely. Needs a real SRC library (libsamplerate, libsoxr, or `Mathnet.Numerics`-based implementation). Higher CPU cost (~5-10 % on a slow core for stereo 48 kHz audio) and ~1-3 ms added latency. Best long-term fix.

3. **Skip compensation entirely; accept underruns as occasional silence** ("Path B" from the original menu) — turn off `CompensateClockDrift`. Listener hears occasional brief silence instead of stretched audio. Probably worse-sounding than even the current state.

## What we should NOT do (rejected)

- **"Cast HM HTTP reader paces to TappedOutputStream fill level"** — could be done, but addresses the wrong layer. Would change Cast-side dynamics without touching BT-input underrun. Marked here so a future researcher doesn't propose this thinking it'll help.
- **"Sync the local speaker clock to BT"** — physical clock is crystal-locked at the hardware level; no software path to retune it.
- **"Run the master mixer at the BT effective rate"** — mixer rate is dictated by PlaybackDevice clock; cannot decouple within SoundFlow's model.

## Decision

Mark requested in-conversation: ship Path C first (refined algorithm), evaluate impact on the metrics + listener subjective experience, then decide on Path D if needed.

**Path C plan**: `docs/plans/2026-05-22-bt-drift-compensation-refinement.md` (companion document).

**Acceptance criteria (from research data)**: per the measurement-discipline pattern established in the cast/BT research arc, success is defined as:
- **Primary subjective**: Mark reports the "underwater" feel is no longer perceptible during BT → Cast playback
- **Primary objective**: `audio.buffer.drift_compensation_total` events/hour can increase (smaller events = more events is fine), but `audio.buffer.drift_compensation_samples_total` should stay roughly flat (same total samples redistributed across more events)
- **Secondary**: `audio.buffer.underrun_total` events/hour drops by ≥50 % (smoother compensation should let the buffer recover before hitting zero more often)
- **Probe**: re-run `bt_drift_analyze.py` against a fresh 15-minute window pre vs post change

If Path C does not achieve subjective improvement: Path D (real resampler) becomes the next plan.
