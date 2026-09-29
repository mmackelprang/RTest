# `AUD-77` — BT album art is lost when the recognition sample straddles a track change

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-29 from an owner question at the cabinet: *"bluetooth is playing, but the album art isn't recognized - is this an unknown song?"*

## What was measured (box, 2026-09-29, build `24b6ce7`)

- The phone sent full AVRCP metadata: `APT.` / `ROSÉ, Bruno Mars` / `rosie`. AVRCP never supplies usable art on this box (`AUD-17`), so on Bluetooth art comes only from SongRec.
- `15:58:09.669` `Created BT play history entry … 'APT.'` (the AVRCP track change).
- `15:58:19.461` `SongRec recognized: 'APT.' by 'ROSÉ & Bruno Mars'`; `Identified track … coverArt: /api/albumart/d2378ea88fd89cdd.jpg`. That file serves `200 image/jpeg`, 39,115 bytes.
- `/api/audio/nowplaying` while "APT." played: `albumArtUrl: /images/default-album-art.png`. Play history for the entry: no cover art. **No `Enriched existing play history entry` line for it** (there is one for the next track).
- No further SongRec activity until the next track: `16:00:51.905` "Shape of You" started, `16:01:07` recognised and enriched — its art reached now-playing and history about 16 s in. **So the art path works; this track fell through it.**

## The likely mechanism — inferred, not proven

The capture is 15 s, so the sample that recognised "APT." began ~`15:58:04`, before AVRCP announced the track at `15:58:09`. `BluetoothAudioSource.OnTrackIdentified` drops a result sampled before the current track started (`AUD-33`) and calls `ForgetRecentIdentification` so the song can be identified again — `NeedsFingerprintingLookup` stays set (`UseShazamForAllSources` is true on the box). Yet no re-identification happened in the 2½ minutes that followed. Why is the open question: a skipped cycle, a no-match logged at Debug, or state in `BackgroundIdentificationService` (e.g. its song-change tracking) that treats the same song as nothing new.

⚠ The deciding lines are Information in `Radio.Infrastructure.Audio`, held at Warning by `LOG-2`. **First task: raise that namespace (runtime, `LOG-5`) across one track change and read the drop and the next cycles.**

## Verification

A track whose first sample straddles the AVRCP change still gets its art within ~2 cycles (≤ 45 s), in both now-playing and play history.
