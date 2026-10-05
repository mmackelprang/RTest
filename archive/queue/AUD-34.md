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

## Shipped

- **Activation stamp.** `PrimaryAudioSourceBase.MarkActivated(utcNow)` records when the audio manager
  made the source active; `AudioManager.SwitchSourceAsync` calls it *before* publishing the new
  `_activeSource`, and only when the active source actually changes (re-selecting the active source
  is not an activation).
- **One comparison.** `LatestTrackBoundaryUtc(trackStartedTicks)` returns the later of the source's
  own AUD-33 track-start stamp and its last activation, or `null` if neither was ever set. Both
  `BluetoothAudioSource.OnTrackIdentified` and `FilePlayerAudioSource.OnTrackIdentified` now drop a
  result sampled before that instant (same drop path, same `ForgetRecentIdentification`). "Never
  stamped" on BT (before the first AVRCP event) therefore means "started at activation".
- **Deliberately not changed:** the AVRCP key and file-path stamps are not reset on deactivation — the
  activation stamp covers the switch-away-and-back cases without adding a later restamp on the
  first AVRCP event after return. A BT *reconnect* while BT stays the active source is not an
  activation and is not stamped; a reconnect onto a different song still restamps via the AVRCP key.
  Radio/SDR/vinyl/USB keep no track boundary (unchanged from AUD-33).
- Drop log lines keep their AUD-33 prefix (`… sampled before the current BT track started` / `… file
  started`) and now add `or … became the active source`.
- Tests: `AudioManagerActivationStampTests` (switch stamps; away-and-back restamps; re-select does
  not); BT `Aud34_*` (same song after switch-back dropped; before first AVRCP, activation is the
  boundary; post-activation sample applies); FilePlayer `Aud34_*` (same file after reactivation
  dropped; post-activation sample applies).

## ✅ Owner UAT — passed 2026-10-04

Reported by the coordinator on 2026-10-04: `AUD-34` (a stale identification across a source switch) passes owner UAT. 

**Archived 2026-10-04** in [`BUILDER_QUEUE_ARCHIVE.md`](../BUILDER_QUEUE_ARCHIVE.md).
