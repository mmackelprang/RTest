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
