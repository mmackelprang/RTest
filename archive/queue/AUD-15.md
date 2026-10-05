# `AUD-15` — the BT audio buffer runs empty, 55 underruns and climbing

[← Builder Queue index](../../docs/BUILDER_QUEUE.md)

🟠 **P1.** Found 2026-09-07 while investigating a Bluetooth *disconnect* (which turned out to be a
RotaryPhone HFP boundary violation — see below). **This is a different defect and must not be
conflated with that one.**

## ✅ Owner UAT passed 2026-09-29 — and a correction to the diagnosis below

**Shipped as [#714](https://github.com/mmackelprang/RTest/pull/714) (squash `28d5a1b`), deployed `24b6ce7`.** Owner, after a Bluetooth session on the box: *"No dropouts mid-song."*

⛔ **Correction: the "underruns every few minutes" in the diagnosis below were song changes.** Every underrun on BOTH builds fell within ~1 s of an AVRCP track change — the phone pauses the A2DP stream between tracks. **Neither build had a single mid-song underrun.** The drift explanation was drawn from spacing that was really song length; correlating with `Created BT play history entry` lines is what showed it. The mid-song defect this row was filed for (2026-09-07) was already gone with `AUD-39`.

| | old build, 10:55–11:29 | new build, 15:57–16:11 |
|---|---|---|
| underruns mid-song | 0 | 0 |
| underruns per track change | 1–4 | exactly 1 |
| zero samples per track change | 1,500–3,000 (15–30 ms) | 896–1,920 (9–20 ms) |
| buffer at report | 1,176–2,566 (≈ empty) | 9,212–9,854 (the 9,600 target) |

The fix still earns its place: the buffer now sits at its 100 ms target instead of near empty, so a late packet mid-song has margin, and each track-change gap is shorter and single.

**Follow-up in the same arc:** those track-change underruns now log at **Debug** instead of a journal Warning — `LinuxBluetoothService` stamps an AVRCP title|artist change on the generator (`NotifyProducerBoundary`), and an underrun line whose every underrun is within ±2 s of it is classified as the phone pausing between tracks. Counters and metrics unchanged.

## Diagnosis 2026-09-29 — two causes, neither the callback

**Still happening, milder.** The owner's BT session 2026-09-29 10:55–11:29 (return-checklist item 3): 9 underrun Warnings in ~34 min, the buffer at **1,176–2,566 of 384,000 samples (12–27 ms)** rather than 0, ~2,000 zero samples per 3–4 min window.

**The callback is no longer the suspect.** Same session: OnProcess execution max 0.10–0.36 ms typical, 8.40 ms worst; intervals 9–12 ms. `AUD-39` fixed what this row was filed under.

**Cause 1 — the startup cushion is silence, and it is spent before audio arrives.** `StartCaptureSubprocess` pre-filled 0.5 s of silence, but the mixer drains it while the capture stream is still connecting. Generator #3 created 10:57:53; stream started (resampler init) 10:57:58.958; **first underrun 10:57:59**. Real audio began with no margin.

**Cause 2 — Path D's resampler ratio is static, and nothing refills the buffer.** `InputResamplerInitialRatio` = 1.00025 (one phone's measured skew); `SrcVariableResampler.SetRatio` had **no callers**; drift compensation is disabled on the resampler path (`LinuxBluetoothService.cs:1573`). The Path D plan deferred closed-loop control as "Phase 2" (`docs/plans/2026-05-22-bt-input-resampler.md:644`) while setting its own acceptance at **0 underruns/hour**. The session's drain (~9 samples/s ≈ 100 ppm) is a residual ratio error with no loop to correct it.

**Fix (branch `fix/aud-15-bt-ring-buffer`):**
- `BufferedSoundGenerator` priming (opt-in): hold playback — silence out, nothing consumed — until the target of **real** audio is buffered; re-arm after a genuine run-dry; a hold is not an underrun.
- `BufferLevelRatioController`: P + slow I on the buffer error, 1 s averaging, base ± 500 ppm (0.9 cents), ≤ 20 ppm/update, anti-windup; fed from `OnProcess` on the resampler's thread, skipped while priming.
- `BluetoothOptions.CaptureTargetBufferMs` = 100 turns both on. Once a minute: `BT resampler control: ratio=…, buffer avg=… ms` (Information; the `Platform.Bluetooth` namespace is held at Warning by `LOG-2`, so raise it to see the line).
- Tests: 8 controller (incl. a 30-min 100 ppm plant that must settle at target and never run dry — it runs dry at −7,680 samples with the gains zeroed), 7 priming (2 fail with the hold removed).

**Box verification (the gate):** a sustained BT session with **no `Buffer underrun` Warnings** after the initial prime, and the control line showing the buffer average near 100 ms. Not auto-mergeable.

**Noticed, not fixed:** on the `pw-record` fallback path (no `object.serial`), drift compensation is also disabled when `UseInputResampler` is true, yet that path has no resampler — so it has no drift correction at all. Priming still applies there.

## The evidence

From the `radio-api` file sink, four samples inside ~32 seconds of ordinary BT playback:

```
11:22:24 [WRN] Buffer underrun (Single): 1 underruns,  574 zero samples in last 13.5s (buffer: 0/384000, total underruns: 47)
11:22:36 [WRN] Buffer underrun (Single): 2 underruns,  488 zero samples in last 11.7s (buffer: 0/384000, total underruns: 49)
11:22:38 [WRN] Buffer underrun (Single): 2 underruns, 1274 zero samples in last  2.3s (buffer: 0/384000, total underruns: 51)
11:22:56 [WRN] Buffer underrun (Single): 4 underruns,  736 zero samples in last 17.4s (buffer: 0/384000, total underruns: 55)
```

**`buffer: 0/384000` on every sample** — the ring buffer is not merely low, it is *empty* at the
moment of measurement. `zero samples` is silence being substituted for audio that did not arrive,
so this is audible: brief dropouts during Bluetooth playback.

The counter climbed **47 → 55 in about 32 seconds**, i.e. this is continuous, not a one-off.

## The likely mechanism — inferred, not proven

Alongside the underruns, the PipeWire capture callback is reporting unhealthy timing:

```
11:22:22 PipeWire OnProcess: count= 9349, interval min= 7.17ms max=14.17ms, bursts=2, execution max= 1.01ms
11:22:32 PipeWire OnProcess: count=10285, interval min= 1.11ms max=21.94ms, bursts=2, execution max= 9.44ms
11:22:42 PipeWire OnProcess: count=11222, interval min= 3.04ms max=19.79ms, bursts=2, execution max=14.20ms
11:22:52 PipeWire OnProcess: count=12160, interval min= 8.23ms max=13.14ms, bursts=2, execution max= 1.97ms
```

**Callback execution reaching 14.2 ms, and inter-callback intervals to 21.9 ms**, against a quantum
that `CLAUDE.md` § *PipeWire Quantum Tuning* documents as 512 frames / 10.67 ms. A callback that
takes longer than the quantum cannot keep the buffer fed.

⚠ **The jump from 1.01 ms to 14.20 ms execution across consecutive samples is the interesting
number**, and it is not explained. `CLAUDE.md` records that this box is resource-constrained and
that load correlates with audible distortion, so CPU contention is the obvious suspect — but
**"obvious suspect" is not a diagnosis, and this row must not start from the assumption.** The
first task is to find out what the callback is doing during a 14 ms execution.

## Deliberately not assumed

- **Not the same as `AUD-10`.** That row is the A2DP *transport* dying on pause. Here the transport
  is healthy and the audio simply arrives too late.
- **Not the RotaryPhone HFP issue.** That was the *disconnect* the owner reported on the same day
  (boundary doc entry 2026-09-07). Same logs, different defect.
- **Not necessarily new.** `total underruns: 55` is cumulative since process start; nothing here
  establishes when it began or whether it correlates with a deploy. Establishing that is cheap and
  worth doing first.

## Scope questions for the plan

1. **What runs inside `OnProcess`?** `MEMORY.md` records a standing constraint: *"Do NOT use
   `PwStreamFlags.RtProcess` — OnProcess callback calls `AddSamples` which takes a lock; blocking
   the RT thread stalls PipeWire."* A lock on the callback path is exactly the shape that produces a
   14 ms execution under contention. Verify what `AddSamples` contends with.
2. **Is the underrun counter's own arithmetic right?** The samples report "1 underruns … total 47"
   then "2 underruns … total 49" — the deltas are consistent here, but a counter that is read in a
   log message is a claim worth checking against the code before it is used as evidence.
3. **What is the actual quantum in effect**, and does it match the 512 / 10.67 ms the docs claim?
   ⚠ `MEMORY.md`: **do not** use `pw-metadata clock.force-quantum` to change it — it disrupts the
   graph and forces BT reconnection.
4. **Does this affect other capture sources** or only Bluetooth? The underrun label says
   `(Single)`, which suggests a specific generator; confirm what that scopes to.

## Verification

Partly measurable off-box: whatever runs in the callback can be profiled or reasoned about in the
repo. But the underruns themselves are a live-box phenomenon and the fix must be confirmed there —
the counter should stop climbing during sustained BT playback.

⚠ Touches the live audio path. **Not auto-mergeable.**
