# `AUD-33` — a fingerprint result that arrives after a track change is applied to the new track

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-26 from `AUD-1`'s box UAT. Pre-dates AUD-1 — the old overwrite applied it too.

## What was observed

```
08:37:55  Starting playback - "Heres To The Night.mp3"
08:38:01  Jumping to full playlist index 1: Meditating Beat.mp3
08:38:10  Fingerprint result for file 'Here's to the Night' by 'Eve 6' (confidence: 80 %); filled from it: title, artist, album, cover art
```

The sample was captured from *Here's To The Night*; the answer arrived 9 s after the skip and was merged
into *Meditating Beat*'s metadata. (All four fields were "missing" only because of `AUD-32` — with tags
loaded, the damage would be limited to whatever that track lacked, typically cover art: **wrong art on
the next track**.)

## Mechanism

`FilePlayerAudioSource.OnTrackIdentified` checks only `IsActiveSource` and `Playing/Paused`. Nothing ties
an identification to the track that was playing when its sample was captured. The same shape applies to
Bluetooth on a track change.

## First task

Stamp the capture with the current track identity (file path / AVRCP title+artist) and drop results whose
stamp no longer matches, logging the drop.

## Verification

Skip tracks within the first ~15 s repeatedly: no `Fingerprint result for file` line names a song other
than the one playing, and no art from the previous track lands on the next.
