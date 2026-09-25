# `AUD-10` — pausing on the phone destroys the A2DP transport, and resume never restores it

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** **Observed live on `radio` 2026-09-06**, reproduced twice in one session.

## Symptom

Bluetooth playback is effectively **single-use**. Pause on the phone and audio never comes back —
pressing play again does nothing. Only a full disconnect/reconnect *from the handset* restores it.

The phone continues to report itself as connected throughout, and AVRCP metadata (song title) keeps
updating on the console, which makes it read as a console fault rather than a transport one.

## Mechanism

On pause, the A2DP transport drops and the `bluez_input` node disappears from PipeWire entirely.
It does not return on resume.

```
# During the failure — device still connected at the BlueZ level:
$ bluetoothctl … devices Connected
Device B0:D5:FB:D2:0D:68 Pixel 10 Pro XL

# …but the card has no source:
$ pactl list cards | sed -n '/bluez_card.B0_D5_FB_D2_0D_68/,/Ports:/p'
audio-gateway: Audio Gateway (A2DP Source & HSP/HFP AG) (sinks: 0, sources: 0, available: yes)
Active Profile: audio-gateway

# …and no BlueZ port exists at all:
$ pw-link -o | grep -i bluez
(nothing)
```

⚠ **`audio-gateway` is the correct profile — do not chase it.** Its full name is *"Audio Gateway
(A2DP Source & HSP/HFP AG)"*; it is not the HFP-only voice profile it resembles. **The number that
matters is `sources:`.** `sources: 0` means no transport; `sources: 1` means the node exists. An
investigation on 2026-09-06 lost time treating the profile name as the fault.

⚠ **Cycling the profile does not help.** `pactl set-card-profile … off` then back to
`audio-gateway` was tried and changed nothing — it is not a transport-level action.

## What is not known

Whether this is ours at all. Candidates, in the order worth testing:

1. **PipeWire 1.0.7 / `bluez.lua`.** The patch at line ~384 was verified present during the
   incident, so the known quirk it works around was *not* the cause — but the same area is the
   first place to look.
2. **The handset.** A Pixel 10 Pro XL, newer than anything in the existing BT notes. Reproducing
   with a second phone would separate "our box" from "this phone" in one test and should be the
   first thing the plan asks for.
3. **Something holding the transport open/closed across the pause**, e.g. an interaction with
   radio-api's capture stream keeping the node in a state BlueZ will not re-establish.

**This row may legitimately close as "upstream / not ours"** — but not before the two-phone test,
because the workaround cost is high and lands on the owner every time.

## Related, filed the same day

- **`AUD-11`** — what the capture stream does *when* this node vanishes. Different fix, and it is
  the reason the failure is silent rather than loud. Neither row blocks the other.
- **`AUD-12`** — the source state stall. Independent, but it also surfaced across a pause/resume
  cycle, so expect to hit all three while testing any one of them.

## Verification

Box-only; nothing here is unit-testable. Play over BT, pause on the handset, resume, and confirm
audio returns without a reconnect. Watch `pactl list cards … sources:` across the cycle — it should
never reach `0` while the device is connected.

---

## ⛔ RE-CONFIRMED 2026-09-10 at the cabinet — **"still happens"**, on `f4d71b28`

Owner ran the phone sitting on a **Pixel 10 Pro XL**. Item #3: *"still happens."*

⭐ **AND ITEM #1 IS PROBABLY THIS ROW, NOT `AUD-12`.** The owner reported *"playing over BT works, but
**won't resume after pause** on either the radio console or the phone."* That is this row's symptom
(*"pressing play again does nothing; only a full disconnect/reconnect from the handset restores it"*),
not `AUD-12`'s `Ready` stall.

⛔ **`AUD-12`'s mechanism was captured FIRING CORRECTLY in the same session**, which is what separates
them:

```
BluetoothAudioSource: phone reports Playing but the source is Ready
  — promoting to Playing (via ApplyDeferredCaptureState)
```

**So the `Ready` stall is being caught and promoted. The resume failure is a different defect.**

### ⭐ A recovery path EXISTS and works — but nothing triggers it on resume

Captured live:

```
15:10:09 [WRN] BT device connected but capture stream missing (attempt 1/3) — attempting recovery
15:10:09 [INF] Found PipeWire BT node: bluez_input.B0_D5_FB_D2_0D_68.2 (id=64, serial=27884, attempt 1)
15:10:10 [INF] BT pipeline recovery successful — capture stream re-established
```

⚠ **That recovery is raised from a DEVICE-CONNECTED event.** A pause/resume does not raise one — which
is exactly why *"only a full disconnect/reconnect restores it"*: the reconnect is what fires the
recovery, not the play button. ⭐ **The repair mechanism this row needs may already exist and simply be
wired to the wrong trigger.** **Check that before designing a new one.**

⚠ **Do not read the `attempt 1/3` cap as the cause without measuring it** — it succeeded on attempt 1
here, so the cap was never approached in this sample.

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

---

## ✅ ROOT CAUSE MEASURED 2026-09-25 11:29–11:30 EDT — the node IS destroyed on pause, and our stream targets a serial that never comes back

⛔ **This supersedes "pause does not destroy the node" in both sections above.** That claim was read off
**our** log — no teardown of **our** stream — and was never a measurement of **PipeWire's** node. A 1 Hz
recorder on the box (node serial, `radio-bt-stream`'s link, BlueZ `MediaTransport1.State`) settles it.
Owner at the cabinet, Pixel 10 Pro XL, box on `9ca4259` (code-identical to `main`).

| time | recorder (box) | app log |
|---|---|---|
| 11:29:26 | node `bluez_input.B0_D5_FB_D2_0D_68.2` **serial 58968**, stream linked to it, transport `active` | — |
| 11:29:31.9 | **PAUSE** — transport `active → idle`, **node 58968 GONE** | — |
| 11:29:32–33 | — | `1 underruns` then `50 underruns, 96000 zero samples` |
| 11:29:34.1 | stream **re-linked to `alsa_input.pci-0000_00_1f.3.analog-stereo`** (the line-in — `AUD-11`) | `OnProcess … max=2045.44ms` |
| 11:30:03.7 | — | **RESUME** — AVRCP `Playing` |
| 11:30:04.6 | transport `pending`, **NEW node, serial 59112**, same name | stream still on the line-in, 18–24 ms callbacks |
| 11:30:08.9 | transport back to `idle`, node 59112 **gone again** | — |

**Owner: "no audio started back up."**

### The mechanism, end to end

1. **PipeWire destroys the A2DP node on pause, by design.** It is a *dynamic* node: emitted when the
   transport rises to `PENDING`, removed when it falls to `IDLE` (`spa/plugins/bluez5/bluez5-device.c:769-780`
   @ 1.0.7). It is recreated on resume under the **same name with a new `object.serial`**.
2. **Our stream binds by serial** — `target.object = {_targetNodeId}` with the serial
   (`PipeWireNativeStream.cs:204`), and WirePlumber 0.4.17 matches a numeric `target.object` **only**
   against `object.serial` (`policy-node.lua:298-300`). The recreated node can never match.
3. **With the target gone, `node.autoconnect = true` re-links us to the default source** — the line-in.
   That is `AUD-11`, and it is the same event, not a neighbouring one.
4. **Nobody links or starts the recreated node, so nobody `Acquire`s the transport.** Re-acquisition only
   happens from the node's start (`media-source.c:689-709`). Unacquired, the transport falls back to
   `idle` ~4 s after resume and the phone gives up. **That is why resume never recovers.**

### The ~2036 ms constant, explained (by timing — strongly supported, not proven from source)

Node gone at 31.9 → re-linked to the line-in at 34.1 ≈ **2.1 s**. During that window our stream has **no
driver**, so no callbacks arrive; the `max=` figure *is* that gap. The later **~21 ms interval is the
line-in's ALSA device driving the graph at the default 1024-frame quantum** (1024/48000 = 21.3 ms), in
place of the BT node's 512/48000 request (`media-source.c:861-868`). No ~2 s timer exists in the bluez5
plugin (checked @ 1.0.7); the delay is WirePlumber's re-link, not a PipeWire timeout.

### The same signature appears WITHOUT a pause

The owner's first connection at 11:27:16 came up with the phone paused. Our stream started against serial
58921 while the transport was not delivering, and within 4 s showed the identical burst → `max=2046.18ms` →
~21 ms callbacks. Pressing play at 11:27:28 never recovered it. **The trigger is "our stream bound while the
phone is not streaming", of which pause is the common case.**

### Why nothing notices — four health signals, all blind to it

- `OnProcess` stamps `_lastOnProcessTimestamp` **before** the empty-buffer checks
  (`PipeWireNativeStream.cs:377` vs `:380-410`) — and in this failure the buffers are not even empty,
  they are line-in audio.
- `BluetoothCaptureWatchdog` reads `MillisecondsSinceLastOnProcess()` (`LinuxBluetoothService.cs:223`) —
  callbacks keep coming.
- `MonitorBtPipelineAsync` recovers only when `_nativeStream == null` (`:269`) — it is not null.
- `PipelineStatus` reports `Healthy` whenever `_nativeStream != null` (`:184`).

⭐ **A full rebuild path exists and nothing can reach it** — `OnGeneratorStalled` → `StopCoreAsync` +
`PlayCoreAsync` (`BluetoothAudioSource.cs:460-493`); a rebuild re-searches by device address
(`GetAudioCaptureDeviceAsync` → `SearchForCaptureDeviceAsync` → `FindPipeWireBluetoothNodeAsync`,
`LinuxBluetoothService.cs:292`, `:1305`). ⚠ **But do not assume it is the fix.** The recreated node lived
**~4 s** (11:30:04.6 → 11:30:08.9) before the unacquired transport fell back to `idle`. A rebuild helps
only if it lands inside that window, so a *timed* watchdog on "no BT data for N s" would usually find no
node to bind to. **The re-bind must be driven by the node's appearance, not by a timeout.**

### What a fix must do (for the plan — not decided here)

- **Follow the node across recreation**: bind by `node.name` (stable across pause/resume; WirePlumber
  matches non-numeric targets by name, `policy-node.lua:337-345`), **or** watch the registry and re-target
  on `global` for the same name. ⚠ **Reconcile with `AUD-11`'s plan**, which adds `node.dont-reconnect`:
  whatever stops the fallback to the line-in must not also stop the re-link to the recreated node.
- **Make the binding observable** — a liveness signal that means "attached to *the BT node*", not "a
  callback arrived". `AUD-11` requires this too.
- ⛔ **Merge `AUD-10` and `AUD-11` into one plan.** They are one event observed from two sides.

### Two further defects seen in the same window — NOT this row, recorded so they are not lost

- **Spurious disconnect.** 11:29:14.381 and 11:27:50.492: `Bluetooth device disconnected … reason="Unknown"
  (user-initiated: false)` — while the recorder showed the device still connected, the transport `active`
  and node 58968 unchanged. Our service tore down a healthy stream on it (the 11:29:15–26 unlink), and the
  reconnect loop then aborted on `Device already connected` in the same millisecond. Same family as
  `AUD-25` / `AUD-21`.
- **Spurious eviction.** 11:29:43.883 and 11:28:20.879: `Bluetooth device removed from BlueZ … evicted from
  cache`, while `bluetoothctl info` reported `Paired: yes / Bonded: yes / Connected: yes`. Candidate: an
  `InterfacesRemoved` for a child object (transport or player) read as the device's — see `AUD-14`.
