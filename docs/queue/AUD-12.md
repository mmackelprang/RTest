# `AUD-12` — the BT source stalls at `Ready` while audio is playing

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** **Observed live on `radio` 2026-09-06**, not inferred. Rank this **first** of the three
BT rows filed that day: it is the one with a user-visible consequence, and it has prior art to
check against.

## Symptom

Album art never appears for Bluetooth playback. It sits on `/images/default-album-art.png`
indefinitely, while the title and artist are correct.

`/api/audio/nowplaying` simultaneously reports `"isPlaying": false` **while audio is audibly
playing** through the speakers, with every PipeWire node `[active]`.

## Mechanism

`BluetoothAudioSource`'s state machine terminates at `Ready` and never returns to `Playing`.
Measured sequence, `/opt/radio-console/logs/radio-*.txt`:

```
10:16:53  Source state changed: Bluetooth Audio  "Playing" -> "Paused"
10:17:54  Source state changed: Bluetooth Audio  "Paused"  -> "Stopped"
10:18:19  Source state changed: Bluetooth Audio  "Stopped" -> "Ready"     ← terminal
```

It stayed `Ready` indefinitely, while at the same moment the graph was fully connected and audible:

```
radio-bt-stream:input_FL  <- bluez_input.B0_D5_FB_D2_0D_68.2:output_FL   [active]
Radio.API:output_FL       -> SN6140 Analog:playback_FL                   [active]
```

**Background fingerprinting is gated on `Playing`.** Its last activity was `10:08:23`; nothing in
the following ten-plus minutes, against a 15 s interval. No identification means no cover-art
lookup, so the art can never resolve. The album art is a *symptom*; the state stall is the defect.

An earlier transition in the same session reached `Playing` briefly (`10:08:15 Paused -> Playing`)
and fingerprinting ran during exactly that window — which is what produced the one identification
of the session. That is the strongest evidence the gate is the cause: recognition tracks the state,
not the audio.

## ⚠ Check the prior art before writing a plan

`CLAUDE.md` § *Pre-Merge Review* documents this exact class in this exact file, as its own example #2:

> `BluetoothAudioSource` carried *"If source is already Playing … route to mixer now"* two lines
> below an assignment of `State = Ready`, making the `Playing` branch statically unreachable — **BT
> song recognition was silently disabled** (fixed in #469).

Same file, same gate, same silent consequence. **Establish whether this is a recurrence of #469 or
a sibling path before planning a fix** — read #469's diff first. Do not assume either answer: five
rows in the 2026-09-05 planning pass turned out to rest on a premise that did not survive reading
the code.

## Scope questions the plan must answer

1. Why does `Stopped -> Ready` happen at all while audio is flowing, and what is supposed to drive
   `Ready -> Playing` afterwards?
2. Is `isPlaying:false` the same defect surfacing through the API, or a second mapping bug? Say
   which; do not assume.
3. Does anything else gate on `Playing` and fail silently the same way? Fingerprinting is the one
   we caught because it has a visible output. Ducking, play-history and the UI's transport state
   are all candidates.

## Verification

Cannot be closed by a green suite. It needs the box: play over BT, confirm `Playing` in
`/api/audio/nowplaying`, confirm a `SongRec recognized` line appears within ~15 s, and confirm
`albumArtUrl` leaves the placeholder. Pause and resume, then confirm all three still hold —
the stall appeared *after* a pause/resume cycle, so a test that never pauses will pass on a broken
build.

⚠ Do not use `AUD-10`'s reconnect dance as part of the test without noting it: a full reconnect is
currently required to restore audio at all, which can mask this row's transition.
