# `AUD-34` — a stale identification that crosses a source switch or BT reconnect is still applied

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-26 from `AUD-33`'s adversarial review (finding M2). Pre-dates AUD-33.

## Mechanism

`AUD-33` drops a fingerprint result sampled before the current track started, but the "track started"
stamp only moves when the **track** changes — the AVRCP `title|artist` key (`BluetoothAudioSource`) or the
file path (`FilePlayerAudioSource`). Neither `_currentTrackKey` nor `_trackStartedFile` is reset when the
source is deactivated, and `AudioManager` (`:289`) resets song-change state on a switch without cancelling
a capture already in flight.

## Failure scenarios (from review; not yet observed)

- A radio capture starts, the listener switches to BT, and the phone resumes the **same** song as its last
  session: no key change, no restamp — the radio result is applied and its art cached for that song. (The
  2026-09-06 "Spirit In The Sky" misidentification in `AUD-1` had this shape.)
- BT before its first AVRCP event: the stamp is 0 ("unknown"), so nothing is dropped — exactly the window
  right after a connect when residual audio from the previous source is most likely.
- File → BT → File on the same file: no restamp on return.

## First task

Stamp the track start on **source activation** as well (both sources), and treat "never stamped" as
"started at activation". Test: capture begins, source switched away and back (same key / same file),
result dropped.
