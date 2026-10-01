# `AUD-77` — BT album art is lost when the recognition sample straddles a track change

[← Builder Queue index](../BUILDER_QUEUE.md)

✅ **CLOSED BY OWNER VALIDATION 2026-10-02, NO CODE.** Owner: *"#3 - I validated this yesterday - it passes."* Nothing was built for this row; the investigation below is the whole of the work. ⚠ **INFERENCE, not established:** the likely reason it passes now is `AUD-14` ([#726](https://github.com/mmackelprang/RTest/pull/726), squash `b64c8cd`, merged and deployed 2026-09-30). It fixed a stale AVRCP watcher that survived a player re-attach, which can leave the BT source stuck in `Ready` with no `Playing` edge — the mechanism this row's investigation found (the source sat in `Ready` for the whole of "APT."). Supporting, not proving: the 2026-09-29 observation ran on `24b6ce7`, an `AUD-15` branch build that does not have `b64c8cd` in its history. No phone session, `dbus-monitor` capture or Debug-level log read was done to confirm it, so which of the dossier's causes (a)/(b)/(c) it was is still unknown. The owner script below was not run and is kept for the record; it is the place to start if the symptom returns.

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
2. **The one recognition that did happen came from a capture that began before the 15:58:09.669 track start.** Most likely it began in the brief `Playing` window, 15:57:59.5–15:58:04.7. The exception is if the SongRec lookup and art caching took longer than ~5 s, in which case it began on the previous source. `captureStartTime` is taken before `CaptureAsync` (`BackgroundIdentificationService.cs:310`), and the capture reads forward whatever the state does. Either way `AUD-33`/`AUD-34` drop it. Calling that drop "right" is a ruling, not a measurement: the dropped result named the track that was actually playing. The drop line itself is `BluetoothAudioSource` at Information, held at Warning by `LOG-2`, so it is not in the file. That link is still inferred, but no other outcome fits: an accepted result would have set the art, and none was set.
3. **The source left `Ready` only at the next track change**, in the same millisecond as the new track's metadata. That is consistent with the phone re-sending its AVRCP status alongside a track change, and with no AVRCP `Playing` edge reaching the source after the 15:58:10 routing.

**So `AUD-77` is a sibling of `AUD-12`** ("the Bluetooth source stalled at `Ready` while the audio kept playing, so fingerprinting was gated off", #623): the same symptom by a different path. `AUD-12` added `TryPromoteToPlayingFromLastAvrcpStatus`. On the Linux path it runs whenever the source lands in `Ready`: through `ApplyDeferredCaptureState` when a capture is acquired after the connect, and through `InitializeAsync`'s catch-up, which `AUD-12` extracted into the helper. The Windows platform-managed return at `BluetoothAudioSource.cs:313` does not call it. Every path that could have written `Ready` at 15:58:10.219 calls it. It promotes only if `_avrcpReportsPlaying` is true. So at 15:58:10 the source's last AVRCP report was **not** `Playing`. Either:
- (a) the phone's `playing` never reached `OnPlaybackStatusChanged` after the 15:58:04 `Stopped`; or
- (b) it arrived as an unrecognised status string, which `LinuxBluetoothService.UpdatePlaybackStatus` maps to `Stopped` through its `_ =>` arm. That clears the bit. `BluetoothAudioSource.cs` already warns about this in the comment at the top of `OnPlaybackStatusChanged` (`:1555-1563`); or
- (c) it arrived, and a later `Paused`/`Stopped`/`Error` edge (`:1564-1567`) or a disconnect (`:1010`) cleared the bit before 15:58:10.

The Debug line `Bluetooth playback status: {Status}` (`BluetoothAudioSource.cs:1571`, in a namespace held at Warning) separates (a), where no `Playing` line appears after the connect, from (b)/(c), where a `Stopped`/`Paused` line follows a `Playing`. It logs the **mapped** enum, though, so it cannot tell an unrecognised status string from a genuine `stopped`. `UpdatePlaybackStatus` (`LinuxBluetoothService.cs:3819-3838`) logs nothing. The script therefore also captures the raw D-Bus value.

**Not built.** Each of (a), (b) and (c) needs a different fix, in `LinuxBluetoothService` or in the promotion rule, and a fix to the wrong one is a live-audio-path change that cannot be checked without the phone. **Held for a phone session, script below.** The straddle drop is correct and needs no change.

### Owner script — about 10 minutes, phone needed

Run from any shell that reaches the box. Record the times.

1. **Raise two namespaces to Debug.** This is runtime only, and a restart or the reset in step 7 undoes it:
   ```bash
   for n in Radio.Infrastructure.Audio Radio.Infrastructure.Platform.Bluetooth; do
     curl -s -X PUT http://radio:5000/api/system/logging/levels/$n \
       -H 'Content-Type: application/json' -d '{"level":"Debug"}'; echo; done
   ```
2. **Start the raw AVRCP status capture.** Leave it running for 3 minutes in its own terminal. `sudo` is needed; as `mmack` the monitor is refused and falls back to eavesdropping, which misses signals:
   ```bash
   ssh mmack@radio "sudo -n timeout 180 dbus-monitor --system \"type='signal',interface='org.freedesktop.DBus.Properties',member='PropertiesChanged',arg0='org.bluez.MediaPlayer1'\"" > aud77-dbus.txt
   ```
3. **Reproduce the order from 2026-09-29.**
   1. Phone Bluetooth **off**.
   2. At the console, pick the **Bluetooth** source.
   3. Turn phone Bluetooth **on**, let it connect, then press **play** on the phone.
4. **Within ~5 s of the music starting:**
   ```bash
   curl -s http://radio:5000/api/audio | grep -o '"state":"[^"]*"' | head -1
   ```
   `Playing` means this run did not stall. Try once more, skipping a track on the phone right after connecting. `Ready` means it reproduced; keep going.
5. **Let the song play for about 45 s.** The pass criterion from the Verification section above is album art within about 2 recognition cycles.
6. **Save the evidence before anything else:**
   ```bash
   ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -nE "Bluetooth playback status|promoting to Playing|Already attached to media player|Source state changed: Bluetooth|Dropped fingerprint result|SongRec recognized|Identified track" $F | tail -80' > aud77-evidence.txt
   ```
7. **Reset the levels:**
   ```bash
   curl -s -X POST http://radio:5000/api/system/logging/levels/reset
   ```

Hand `aud77-evidence.txt` and `aud77-dbus.txt` to the next Builder.
- **If no `Playing` line follows the connect:** the cause is (a). `Already attached to media player` points at the `AUD-14` re-attach dedup.
- **If a `Stopped` follows a `Playing`:** the `"Status"` strings in `aud77-dbus.txt` decide between (b), an unrecognised value, and (c).
