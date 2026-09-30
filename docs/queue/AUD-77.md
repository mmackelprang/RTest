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

---

## 2026-09-30 — the missing re-identification is explained, and it is not in the fingerprint pipeline

The Builder read the **file sink** for 2026-09-29 on the box (`/opt/radio-console/logs/radio-20260929.txt`, lines 35716–35768). `AudioManager`'s state lines are at Information in the `...Audio.Services` carve-out, so they are there even though `LOG-2` holds the rest of `Radio.Infrastructure.Audio` at Warning. The timeline, in EDT:

| Time | Line |
|---|---|
| 15:57:59.508 | Source switched to Bluetooth **before the phone connected** (`No connected Bluetooth device — cannot create audio capture`); `Stopped -> Ready -> Playing` |
| 15:58:03.622 | Phone `B0:D5:FB:D2:0D:68` connected |
| 15:58:04.743 / .802 | `Playing -> Paused -> Stopped` |
| 15:58:09.669 | `Created BT play history entry … 'APT.'` (the AVRCP track change) |
| 15:58:10.219 | `Stopped -> Ready`, and the capture is routed into the mixer (`AUDIO ROUTING COMPLETE … GeneratorId=5`) |
| 15:58:19.461 | `SongRec recognized: 'APT.'` (`Identified track … coverArt: /api/albumart/d2378ea88fd89cdd.jpg`) |
| *(2 min 41 s of no identification activity)* | |
| 16:00:51.905 / .906 | `Created BT play history entry … 'Shape of You'`; **`Ready -> Playing`** |
| 16:01:07.432 | `SongRec recognized: 'Shape of You'`; `Song change detected`; `Enriched existing play history entry` |

**What this shows (measured):**

1. **The source sat in `Ready` for the whole of "APT.", from 15:58:10 to 16:00:51, while the phone was audibly playing.** The identification loop gates on it. `SoundFlowAudioTap.IsActive` requires `ActiveSource.State == Playing`, and `BackgroundIdentificationService.IdentifyCurrentAudioAsync` returns early with `Audio source not active, skipping identification` (Debug) every idle poll. That is why nothing re-sampled the song. It is not duplicate suppression, not a SongRec no-match, and not the drop.
2. **The one recognition that did happen came from a capture that began while the source was briefly `Playing`, between 15:57:59.5 and 15:58:04.7.** That is before the 15:58:09.669 track start, so `AUD-33`'s rule dropping pre-track results was right to drop it. The drop line itself is `BluetoothAudioSource` at Information, held at Warning by `LOG-2`, so it is not in the file. That link is still inferred, but no other outcome fits: an accepted result would have set the art, and none was set.
3. **The source left `Ready` only at the next track change**, in the same millisecond as the new track's metadata. That is consistent with the phone re-sending its AVRCP status alongside a track change, and with no AVRCP `Playing` edge reaching the source after the 15:58:10 routing.

**So `AUD-77` is a sibling of `AUD-12`** ("the Bluetooth source stalled at `Ready` while the audio kept playing, so fingerprinting was gated off", #623): the same symptom by a different path. `AUD-12` added `TryPromoteToPlayingFromLastAvrcpStatus`. It runs whenever the source lands in `Ready`: through `ApplyDeferredCaptureState` when a capture is acquired after the connect, and through `InitializeAsync`'s identical catch-up. Every path that wrote `Ready` at 15:58:10.219 calls it. It promotes only if `_avrcpReportsPlaying` is true. So at 15:58:10 the source's last AVRCP report was **not** `Playing`. Either:
- (a) the phone's `playing` never reached `OnPlaybackStatusChanged` after the 15:58:04 `Stopped`; or
- (b) it arrived as an unrecognised status string, which `LinuxBluetoothService.UpdatePlaybackStatus` maps to `Stopped` through its `_ =>` arm. That clears the bit. `BluetoothAudioSource.cs` already warns about this in `OnPlaybackStatusChanged`'s header comment; or
- (c) it arrived and something cleared the bit before 15:58:10.

The Debug line `Bluetooth playback status: {Status}` separates the three. It is in a namespace held at Warning.

**Not built.** Each of (a), (b) and (c) needs a different fix, in `LinuxBluetoothService` or in the promotion rule, and a fix to the wrong one is a live-audio-path change that cannot be checked without the phone. **Held for a phone session, script below.** The straddle drop is correct and needs no change.

### Owner script — about 10 minutes, phone needed

Run from any shell that reaches the box. Record the times.

1. **Raise the two namespaces to Debug.** This is runtime only, and a restart or the reset in step 6 undoes it:
   ```bash
   for n in Radio.Infrastructure.Audio Radio.Infrastructure.Platform.Bluetooth; do
     curl -s -X PUT http://radio:5000/api/system/logging/levels/$n \
       -H 'Content-Type: application/json' -d '{"level":"Debug"}'; echo; done
   ```
2. **Reproduce the order from 2026-09-29.**
   1. Phone Bluetooth **off**.
   2. At the console, pick the **Bluetooth** source.
   3. Turn phone Bluetooth **on**, let it connect, then press **play** on the phone.
3. **Within ~5 s of the music starting:**
   ```bash
   curl -s http://radio:5000/api/audio | grep -o '"state":"[^"]*"' | head -1
   ```
   `Playing` means this run did not stall. Try once more, skipping a track on the phone right after connecting. `Ready` means it reproduced; keep going.
4. **Let the song play for about 45 s.** The pass criterion from the Verification section above is album art within about 2 recognition cycles.
5. **Save the evidence before anything else:**
   ```bash
   ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -nE "Bluetooth playback status|promoting to Playing|Source state changed: Bluetooth|Dropped fingerprint result|Audio source not active|SongRec recognized|Identified track" $F | tail -60' > aud77-evidence.txt
   ```
6. **Reset the levels:**
   ```bash
   curl -s -X POST http://radio:5000/api/system/logging/levels/reset
   ```

Hand `aud77-evidence.txt` to the next Builder. The `Bluetooth playback status:` lines between the connect and the stall decide between (a), (b) and (c).
