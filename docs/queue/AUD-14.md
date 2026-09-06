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
