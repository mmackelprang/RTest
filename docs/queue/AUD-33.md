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

---

## 🚧 BUILT 2026-09-26 — branch `fix/aud-33-drop-stale-identifications`

**Fix, as planned ("stamp the capture"), plus one thing the plan missed:**
- `TrackIdentifiedEventArgs.CaptureStartedAt` — set by `BackgroundIdentificationService` from the capture
  start it already recorded. `WasCapturedBefore(instant)` is the one comparison both sources use; an
  unknown time on either side is never stale.
- **FilePlayer** stamps its track start in `UpdateMetadataFromFile` when the file path changes (re-reading
  the same file — repeat, queue edits, restore — does not restamp). **Bluetooth** stamps it in
  `OnMetadataChanged` when the AVRCP `title|artist` key changes (a same-track AVRCP refresh does not).
  Both drop a result sampled before their stamp and log it at Information (file sink only on the box).
- ⚠ **Missed by the plan: dropping alone could suppress the NEW track for minutes.** The service marks a
  result as recently identified *before* raising the event. A dropped result can legitimately name the
  track now playing — a capture straddling the skip, or (Bluetooth) AVRCP metadata trailing the audio by
  a second or two — and duplicate suppression would then block that track's re-identification for 5–30
  min; on Bluetooth that is its only art source. So a drop also calls the new
  `BackgroundIdentificationService.ForgetRecentIdentification`.
- A grace window was considered and rejected: the observed capture began ~6 s before the skip, so any
  grace wide enough to absorb AVRCP lag safely would not have caught the reported case.

**Not changed (scope):** `PlayHistoryTracker` still consumes a stale result (it re-points History rows —
`AUD-19`'s territory). Radio/SDR/vinyl sources have no track boundary to stamp.

**Tests** (red first: the two drop tests failed against the unfixed sources; the three "still applies"
controls passed): FilePlayer skip / current track / reload-same-file; BT track change / AVRCP refresh /
dropped result forgotten by dedup / applied result stays suppressed; Core `WasCapturedBefore` semantics.
**Mutations**, each caught by exactly its target test: removing the BT dedup forget; restamping FilePlayer
on every metadata read; restamping BT on every AVRCP update.
Known gap: no test runs a real identification cycle, so the service's one-line `captureStartTime`
hand-off is covered by review only.

**Box check (for the owner's UAT):** in the file player, skip tracks within the first ~10 s of each, a few
times. No `Fingerprint result for file` line should name the previous song; `Dropped fingerprint result
… sampled before the current file started` lines may appear, and each dropped-into track still gets
identified on the next cycle.

**Adversarial review (session model): nothing blocking.** Fixed in the PR:
- **M1** — nothing pinned that the service actually sets `CaptureStartedAt`, and a missing time fails
  *open* (never stale). New `BackgroundIdentificationServiceCaptureTimeTests` runs one real cycle (mock tap
  + mocked SongRec) and asserts the raised args carry a capture time **and** that the result is already
  suppression-marked when raised (the ordering `ForgetRecentIdentification` depends on). Mutation-checked:
  removing `captureStartTime` from the raise site turns it red.
- **M4** — the kind-B seam's justification ("no harness runs a cycle") was false; reworded.
- **L3** — BT now stamps the track start *before* writing the new title (File already did).
- **L4** — two comments narrowed (the "still identified" claim; the sub-second fuzziness of the boundary).

**Filed or scoped, not fixed here:**
- **M2 → [`AUD-34`](AUD-34.md):** a stale result that crosses a *source switch* or a BT reconnect on the same
  song is not dropped — the stamp only moves on a track-key / file-path change.
- **M3 → `AUD-19`:** `PlayHistoryTracker` and the service's song-change bookkeeping still consume stale
  results, unchanged. **L1:** because a dropped result is now forgotten by dedup, a second raise of the same
  identity can reach History and label an older unidentified row — conditional, and History's to fix.
- **L2 (accepted):** a correct result can be dropped (straddling capture; BT audio ahead of AVRCP). Cost is
  one extra ~15 s cycle; recovery is guaranteed because the drop returns before the lookup flag is cleared
  and dedup is forgotten. A phone that changes its AVRCP key more often than every ~15 s would keep
  dropping — not observed.
- **Found in passing → [`AUD-35`](AUD-35.md):** the identification loop busy-spins a core whenever there is
  nothing to identify. Measured on the box at 99.9 % of one thread.
