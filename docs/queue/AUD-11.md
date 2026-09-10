# `AUD-11` — the BT capture stream silently falls back to the line-in when its target disappears

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** **Observed live on `radio` 2026-09-06.** This is the row that makes `AUD-10` *silent*
instead of loud, and it is the more dangerous of the two.

## Symptom

When the `bluez_input` node vanishes (see `AUD-10`), `radio-bt-stream` does not fail, stop, or log
anything. **PipeWire re-links it to the built-in analog capture** and it carries on happily
recording an unplugged jack.

Measured during the incident — the same stream, before and after:

```
# Healthy — reading from the phone:
radio-bt-stream:input_FL  <- bluez_input.B0_D5_FB_D2_0D_68.2:output_FL   [active]

# After the BT node vanished — reading the line-in, still [active]:
radio-bt-stream:input_FL  <- alsa_input.pci-0000_00_1f.3.analog-stereo:capture_FL   [active]
```

Every downstream indicator stays green. The graph shows an active capture, `Radio.API` shows an
active playback stream to the speakers, and the console reports a working Bluetooth source. The
only evidence anything is wrong is that the room is quiet.

## Mechanism — the likely cause is already documented in this repo

⚠ **Corrected 2026-09-06 after the plan was written — the paragraph below was the filer's first
guess and is only half right.** The real mechanism is `node.autoconnect`, not `PW_ID_ANY`; see the
correction after the quote. Kept rather than deleted because the wrong guess is the one a reader
arrives with.

The project's auto-memory (`~/.claude/projects/…/memory/MEMORY.md` — **not a file in this repo**,
despite earlier revisions of this dossier citing it as one) records the design decision this
behaviour was first attributed to:

> **Use PW_ID_ANY (0xffffffff)** as targetId in `pw_stream_connect` — let PipeWire resolve via
> `target.object` property

With `PW_ID_ANY`, PipeWire is free to resolve the stream to **whatever the default source is** when
the intended target is absent. On this box the default input is
`alsa_input.pci-0000_00_1f.3.analog-stereo`, which is exactly where it landed.

### ⚠ The correction — `PW_ID_ANY` is not the mechanism, and there is no trade-off

Established by [the plan](../../design/plans/AUD-11-the-capture-that-recorded-the-wrong-jack.md) by
reading the code rather than the memory note:

**The fallback decision is not in this repo at all.** `PipeWireNativeStream.cs:204` sets
`node.autoconnect = true` alongside `target.object`. Per `pipewire-props(7)`, `node.autoconnect`
*"instructs the session manager to automatically connect this node to some other node"* — an
instruction with **no failure mode** — while `target.object` is only a **preference**. An
unresolvable preference plus an outstanding instruction yields the default source.

**`PW_ID_ANY` bought no binding guarantee.** It removed a *competing* numeric target that was being
fed an `object.serial` into a parameter meaning node **id**. That is the whole purchase. So it is
not a constraint on the fix: **`PW_ID_ANY` stays untouched and the fix is additive** —
`node.dont-reconnect = true`, documented as *"also inhibits that the node is moved to another
sink/source."* The one move genuinely unavailable is passing `_targetNodeId` as `targetId` again.

**This repo already knew, and the defence was lost in a migration.** `LinuxBluetoothService.cs:1985-1987`
documents the same behaviour for the old `pw-record` path, which guarded it **twice**
(`-P node.autoconnect=false` plus an explicit re-link). The native path that replaced it in #262
inherited neither. This is a regression, not a novel defect.

**A second route the symptom description misses:** `LinuxBluetoothService.cs:1814` converts "could
not read `object.serial`" into the literal target `0` — reachable on a cold connect with nothing
having disappeared.

## Why this outranks its own severity

This is the third instance this month of the failure mode `CLAUDE.md` § *Pre-Merge Review* exists
for — **a component reporting success while doing nothing**, alongside `AUD-2`'s silent gain/ducking
miss and `SoundFlowMasterMixer` logging a detach it never performed. Here the false signal is not a
log line but the graph itself, which is worse: it defeats the exact diagnostic a person would reach
for.

A fix that only re-targets, without also making a wrong binding *observable*, leaves the next
occurrence just as invisible. **Whatever the fix, the stream must be able to say it is attached to
the wrong thing.**

## Scope questions the plan must answer

1. Can the stream bind to the BT node specifically and **refuse** anything else, without
   reintroducing the problem `PW_ID_ANY` solved?
2. What should happen when the target is gone — stop, retry, or park in an explicit
   `WaitingForDevice` state? The current answer is "silently record something else", which is the
   only clearly wrong option.
3. Is the same pattern used by any other capture source? USB/Vinyl/GenericUSB all capture through
   related paths and may share it. Enumerate rather than assume.

## Verification

Reproduce by removing the target: play over BT, pause on the handset (which destroys the node per
`AUD-10`), then inspect `pw-link -l`. Today the stream re-appears on `alsa_input…capture_FL`; after
the fix it must not, and the state must be visible somewhere a person can read.

Manual recovery, for reference — this is what was used on the night:

```bash
pw-link -d alsa_input.pci-0000_00_1f.3.analog-stereo:capture_FL radio-bt-stream:input_FL
pw-link -d alsa_input.pci-0000_00_1f.3.analog-stereo:capture_FR radio-bt-stream:input_FR
```

---

## 🔬 LOG EVIDENCE GATHERED 2026-09-10 — the owner asked for logs rather than a cabinet run

Item #4 of the phone sitting was *"check the logs for this."* Captured live on `f4d71b28`, Pixel 10
Pro XL over A2DP.

### ⛔ CORRECTION FIRST — an instrument error made while gathering this, recorded so nobody repeats it

`pactl list short sources | grep -i bluez` returned **nothing**, and this coordinator concluded from
that there was **no audio path**. ⛔ **That conclusion was FALSE and audio was flowing the entire
time.**

**This box captures BT through native PipeWire P/Invoke (`PipeWireNativeStream`), not `pw-record`**,
so the capture **never appears as a PulseAudio source at all**. The node existed and the log names it:
`bluez_input.B0_D5_FB_D2_0D_68.2 (id=64, serial=27884)`.

⭐ **The correct instrument is the callback counter, which shows the stream live and healthy:**

```
15:15:09 🔬 PipeWire OnProcess: count=28136, interval min=7.02ms max=14.49ms, bursts=5, execution max=2.74ms
```

~938 callbacks per 10 s, climbing. ⚠ **`pactl` returning no bluez source is NOT evidence of a missing
capture on this box.** Use `OnProcess` counts, or the node id/serial from the service's own log.

### What the logs show

- **The node IS destroyed and re-established**, and the service detects it:
  `BT device connected but capture stream missing (attempt 1/3) — attempting recovery` →
  `BT pipeline recovery successful — capture stream re-established`, 0.5 s apart.
- ⚠ **The recovery is raised from a DEVICE-CONNECTED event**, which a pause/resume does not raise —
  see [`AUD-10`](AUD-10.md), where this is the likely explanation for *"only a full
  disconnect/reconnect restores it."*
- **One buffer underrun** in the window: `⚠️ Buffer underrun (Single): 1 underruns, 1920 zero samples`.
- **`Bluetooth device removed from BlueZ … — evicted from cache`** appears mid-session, followed by a
  reconnect. ⚠ **Not yet correlated with the owner's pause presses** — the timeline was reconstructed
  after the fact and nobody recorded when each button was pushed.

### ⚠ What was NOT established

⛔ **The causal link between "pause on the handset" and "node destroyed" is still INFERRED, not
measured.** No timestamped record of the owner's button presses exists for this session. **A
purpose-built run — note the wall-clock time of each pause, then read the log against it — would
settle it, and that is the honest next step rather than more log archaeology.**

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
