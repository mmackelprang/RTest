# `AUD-14` — a stale AVRCP watcher survives player re-attach and speaks for a torn-down source

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Found 2026-09-06 by the `AUD-12` investigation and confirmed by its plan. Filed as its own
row on that plan's §7.1 recommendation: **different layer, different blast radius, and it needs its
own disconnect/reconnect UAT.** Fixing `BluetoothAudioSource` alone is sufficient for `AUD-12`'s
symptom, so this is not a prerequisite — but it is what makes one of `AUD-12`'s guards necessary
rather than defensive.

## The defect

Two independent omissions in `LinuxBluetoothService.cs` that compose into a live-audio hazard.

**1. The dedup returns before the cleanup.** `AttachMediaPlayerAsync` short-circuits on
`_mediaPlayerPath == objectPath && _mediaPlayer != null` (`:2536-2540`), and that `return` at
`:2539` sits **above** `_playerPropertiesWatcher?.Dispose()` at `:2542`. So a re-attach at the same
D-Bus path keeps the previous watcher alive rather than replacing it.

**2. `_mediaPlayer` is never nulled.** `OnInterfaceRemoved:929-932` returns early for anything that
is not `Device1`, so a removed `MediaPlayer1` interface never clears the field. The dedup's second
condition is therefore permanently satisfied once set.

**Together:** after BlueZ tears down and re-adds `player0` at the same path — which is ordinary
behaviour across the pause/reconnect cycles `AUD-10` produces — a **stale watcher stays subscribed
to a dead path** and can deliver an AVRCP `Playing` to a source whose pipeline has already been torn
down.

**A second consequence, independent of the first:** the dedup also skips the "Get initial state"
read at `:2549-2556`. So a phone that is *already playing* when the player re-attaches never
delivers its status as an event at all. That is one of the two roads to `AUD-12`'s terminal `Ready`.

## Why this earns a row rather than a note

It is the reason `AUD-12`'s fix cannot simply widen its accept predicate. `AUD-12` ships
`(State == Stopped && HasCapturePath)` rather than bare `Stopped` **precisely because** a stale
watcher can raise `Playing` against a source that `OnDeviceDisconnected` has already stripped —
generator pulled from the mixer, `_captureDevice` and `SoundComponent` nulled (`:714-735`, before
the `Stopped` assignment at `:739`).

So `AUD-12` is defending against this row's behaviour at the consumer. Fixing it here removes the
hazard at the source. **Neither blocks the other, and `AUD-12` should not wait** — but if this ships
first, `AUD-12`'s guard becomes belt-and-braces rather than load-bearing, which is worth knowing
when reviewing it.

## Scope questions for the plan

1. **Order the dedup after the cleanup**, or make the dedup path dispose the old watcher explicitly.
   Say which, and why the other is worse.
2. **Clear `_mediaPlayer` / `_mediaPlayerPath` when the interface goes away.** `OnInterfaceRemoved`
   currently handles only `Device1`; establish what else it should handle and whether other cached
   state has the same leak. ⚠ A 2026-07-16 note already records *"`LinuxBluetoothService` lacks an
   `InterfacesRemoved` handler; never cleans up caches on device disconnect"* — check whether this
   is the same gap partially closed, and whether other caches are still open.
3. **Should the initial-state read happen on every attach**, dedup or not? It is cheap and it closes
   the "already playing on re-attach" hole directly.
4. **Blast radius is every `IBluetoothService` consumer**, not just `BluetoothAudioSource`.
   Enumerate them rather than assuming the audio source is the only one that can be misled by a
   stale watcher.

## Verification

Unit-testable in part — the dedup ordering and the field-clearing are pure logic over a mocked
D-Bus surface, and `MockBluetoothService` already exists.

The behavioural half needs the box **and the owner's phone**: connect, play, pause until the
transport drops, reconnect, and confirm exactly one watcher is live and the initial status is read.
⚠ Currently blocked — the owner's phone is unavailable.

⚠ This touches the live audio path on a device event. **Not auto-mergeable.**

---

## 🔬 2026-09-10 — BT was PAUSED on the box and the capture stream stayed ALIVE

The owner reported *"the BT audio is paused right now"* during the phone sitting. Measured in that
state, on `9ca42590`:

```
src=Bluetooth  playing=False  paused=True
🔬 PipeWire OnProcess: count=5185 -> 5654     (still climbing while PAUSED)
```

⭐ **The native capture stream keeps running and delivering callbacks through a pause.** It is not
torn down by the pause itself in this instance.

⚠ **This does NOT settle `AUD-11`'s question, and must not be quoted as if it does.** An earlier
sample the same day showed the node missing and a recovery firing
(*"BT device connected but capture stream missing (attempt 1/3)"*). **So the node survives some pauses
and not others**, and nobody has yet established what distinguishes them. ⛔ **Two samples, opposite
outcomes, no controlled variable.**

⚠ **Note also that `radio-api` restarted between the two samples** (a deploy), which is why the
`OnProcess` counter resets — **do not read the lower count as degradation.**

**The honest next step is the timestamped run**: note the wall-clock moment of each pause and each
resume, then read the log against it. Both this row and `AUD-10`/`AUD-11` are waiting on the same
measurement.

---

## ⭐⭐ MEASURED 2026-09-10 15:42 EDT — the owner timestamped a pause/resume and it FALSIFIES the premise

The owner paused and resumed at a noted wall-clock time, which is the controlled run
`AUD-10` / `AUD-11` / `AUD-14` had all been waiting on. Log window read **unfiltered**.

```
15:42:10.302  Audio source Bluetooth-… state changed from "Playing" to "Paused"     <- THE PAUSE
              (no stream stop, no generator disposal, no node teardown — nothing)
15:42:14.910  Audio source Bluetooth-… state changed from "Paused" to "Playing"     <- THE RESUME
15:42:15.292  ⚠️ Buffer underrun (Single): 50 underruns, 96000 zero samples in last 1.0s
15:42:18.355  🔬 PipeWire OnProcess: count=1546, interval min=8.99ms max=2036.88ms
15:42:21.844  PipeWire native stream stopped
15:42:21.857  Bluetooth device disconnected … reason="Unknown" (user-initiated: false)
15:42:21.857  BufferedSoundGenerator #6 disposed. received=2062812, output=2110144
15:42:21.857  Starting reconnection loop for B0:D5:FB:D2:0D:68 (max 20 attempts)
15:42:21.857  Device already connected — stopping reconnection loop
15:42:35.418  BT device connected but capture stream missing (attempt 1/3) — attempting recovery
```

### ⛔ PAUSE DOES NOT DESTROY THE NODE. RESUME IS WHAT BREAKS IT.

**Across the entire 4.6 s pause the stream was untouched** — no teardown, no disposal, callbacks
still arriving. ⛔ **The long-standing premise that *"pause on the handset destroys the node"* is
FALSIFIED by direct measurement.**

**Within 400 ms of the resume the buffer starves** — 50 underruns and 96,000 zero samples in one
second — then callbacks stall for **2036.88 ms**, and ~7 s later the phone drops the link with
`user-initiated: false`, i.e. **not the owner and not this service.**

⚠ **The last link is STRONGLY SUGGESTED, NOT PROVEN.** Underrun-storm → callback-stall → disconnect is
one coherent chain in one sample. ⛔ **Do not write it up as established causation from n=1.**
A repeat run is cheap now that the method exists.

### ⛔ THE RECOVERY IS DISARMED BY A STALE "CONNECTED" READING — a second, separable defect

The reconnection loop starts and **aborts in the same millisecond**, because BlueZ still reports the
device connected while the audio path is already gone.

⭐ **This is the same family as [`AUD-25`](AUD-25.md)'s phantom `isConnected` — but here it is
LOAD-BEARING: the stale reading switches the repair OFF.** `AUD-25` was filed as a cosmetic lie in a
status response; this shows the same class of untruth **disabling a recovery path**. ⚠ **Whoever plans
either row should read the other.**

⚠ **A different recovery DID fire 13.5 s later** (`capture stream missing (attempt 1/3)`), so the
system is not defenceless — but it is triggered by a **device-connected** event, not by the failure
itself, and it left ~14 s of silence.

### ⚠ Also measured, and worth not losing

- **Node discovery took 10 attempts (~10 s)** after connect: `Found PipeWire BT node … (attempt 10)`.
  ⚠ **Nobody has established whether that is normal.** It is not obviously a defect and it is not
  obviously fine.
- **The 15:41:43 disconnect was `reason="LocalHost" (user-initiated: true)`** — a *different* event
  from the 15:42:21 spontaneous one. ⛔ **Do not conflate them when reading this window.**
- ⭐ **`ApplyDeferredCaptureState` promoted `Ready → Playing` again** at 15:41:58 — `AUD-12`'s fix
  working, third sighting.

---

## ⛔⛔ CORRECTION 2026-09-10 — **RUN 2 FALSIFIES THE CAUSAL CLAIM RUN 1 PRODUCED. THE PAUSE IS THE TRIGGER, NOT THE RESUME.**

The section above concluded *"pause does not destroy the node — RESUME is what breaks it."* ⛔ **The
second half is WRONG.** A second timestamped run, with a **52-second** pause instead of 4.6 s,
separates two events that the first run's short pause had overlapped.

```
15:49:27.707  Playing -> Paused                                     <- THE PAUSE
15:49:31.623  ⚠️ Buffer underrun (Single): 1 underruns              <- ~4 s AFTER THE PAUSE
15:49:32.625  ⚠️ Buffer underrun (Single): 50 underruns, 96000 zero samples in last 1.0s
15:49:32.678  🔬 OnProcess: count=2749, interval min=7.66ms max=2036.17ms
15:49:42 -> 15:50:12   OnProcess interval now 19-23ms  (was 7-13ms) <- CALLBACK RATE HALVED
15:50:19.064  Paused -> Playing                                     <- THE RESUME
15:50:34.068  ⚠️ No audio data captured after 15004.2554ms and 1416 read attempts
15:50:49.084  ⚠️ No audio data captured after 15015.227ms and 1415 read attempts
```

### How run 1 misled, stated plainly

Re-read run 1 against this: its first underrun was at **15:42:14.290 — ~4 s after the pause at
15:42:10.302** — and the resume at 15:42:14.910 fell **between** that first underrun and the
50-underrun burst. ⛔ **The resume did not cause the burst; it merely happened in the middle of a
sequence already under way.** With a 4.6 s pause the two are inseparable. **The longer pause is what
made them distinguishable, and nothing about run 1's data was wrong — only the reading of it.**

### The corrected sequence, identical in BOTH runs

**pause → ~4 s → buffer starves → ~1 s later a 50-underrun burst → ~2036 ms callback stall →
callback interval roughly DOUBLES and stays there.**

⭐ **THE STALL FIGURE IS A CONSTANT, NOT NOISE: 2036.88 ms and 2036.17 ms across two runs — 0.71 ms
apart.** That is a **fixed timeout**, not random starvation. ⭐ **Grep for a 2000 ms constant on the
capture path; that is the cheapest lead on this row.**

### ⛔ What RESUME actually does: nothing

```
No audio data captured after 15004ms and 1416 read attempts
```
Twice, back to back. **The stream is ALIVE — `OnProcess` keeps counting — and delivering ZERO BYTES.**
Resume cannot recover because the capture is **running-but-empty**, not stopped.

⭐⭐ **THAT SIGNATURE IS ALREADY A KNOWN UNSOLVED BUG IN THIS PROJECT**, recorded as *"after days of
uptime + source switches, SoundFlow capture stops delivering audio. Generator in mixer but output=0."*
— and [`AUD-18`](AUD-18.md), *"the fingerprint tap returned ZERO bytes for 11½ hours."* ⭐ **Same shape,
and it is now REPRODUCIBLE ON DEMAND WITH A PAUSE BUTTON.** ⛔ **That is the single most valuable thing
in this row: a months-old intermittent has a deterministic trigger.**

### What survives from run 1

✅ **"Pause does not destroy the node" is STILL TRUE** — no teardown, no generator disposal at the
pause in either run. **The node survives; the STREAM degrades.** Those are different claims and only
the causal one was wrong.

✅ **The stale-reading abort reproduced** — `Starting reconnection loop (max 20 attempts)` /
`Device already connected — stopping reconnection loop`, same millisecond, at 15:48:51. **Second
sighting.**

⚠ **Still n=2 and still not proven end to end.** The link from underrun-storm to the *spontaneous
disconnect* seen in run 1 did not recur here — run 2's disconnect happened **before** the pause, not
after. ⛔ **Do not carry "the starvation causes the disconnect" forward; it now has one supporting
sample and one non-recurrence.**
