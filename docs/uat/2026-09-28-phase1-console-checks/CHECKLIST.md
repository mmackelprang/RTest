# Phase 1 — console checks at the cabinet

**Source:** [`HANDOFF-GA-CLOSEOUT.md`](../../HANDOFF-GA-CLOSEOUT.md) §2. One sitting, no code.
Fill in the **Result** column (✅ / ⛔ / ⚠ + a few words) and the time. Anything you can't
decide, write what you saw — that is still a result.

## Before you start (30 seconds each — skipping these has cost a whole sitting before)

- [ ] **Box matches `main`.** From the dev box: `pwsh deploy/Deploy-ToLinux.ps1 -VerifyOnly` → `Box matches HEAD`.
- [ ] **Console is NOT muted** and master volume is audible. (The 2026-09-09 sitting started muted and every sound check would have failed for the wrong reason.)
- [ ] **Note the wall-clock start time.** Phone checks fall in and out of the ~20-minute GV auth blackout (`XR-3`); times make the results readable.

Start time: ______

## The checks, in order

| # | Do this | Pass looks like | Row | Result | Time |
|---|---|---|---|---|---|
| **1.1** | **RDS.** Tune an FM station with RDS — ideally one whose station name *rolls* (changes every few seconds). Watch the ticker for 3–5 minutes. | The scrolling text **never jumps back to the start** when the song title updates or the station name flips. The station name changes **in place** without the scrolling text lurching sideways. No mangled names (e.g. half of one page, half of the next). | `AUD-63` `AUD-69` `AUD-70` | | |
| 1.1a | While watching: does the scroll **speed** visibly alternate fast/slow between updates? | Expected yes — that is `AUD-64`, not fixed yet. Note how noticeable it is. | `AUD-64` | | |
| 1.1b | **Decision D-B:** should the left-hand station name keep **rolling** (live PS, like a car radio) or stay **fixed** on the call sign? Does a name ever lag or skip a page? | Your call; either is a one-line change. | D-B | | |
| **1.2** | Play from your phone over **Bluetooth**. Press **Pause on the console panel**. | Audio stops. | `AUD-40` | | |
| **1.3** | Settings → Bluetooth: start a scan, then **Stop Scan**. Disconnect the phone, reconnect it. Check the console still shows track info / follows phone volume. Optional: pair a second phone. | Reconnect and track info work as before. | `AUD-41` | | |
| **1.4** | Set the **Cast speaker to 20 %** from the speaker/Google Home. On the console select Cast output. Then from the dev box: `ssh mmack@radio 'sudo systemctl restart radio-api'`. After it's back, note the speaker's level. Then **turn the volume knob**. | Speaker stays near 20 % (not forced to 70 %), and the knob changes the Cast speaker's volume. | `AUD-38` | | |
| **1.5** | While casting music, **unplug the Chromecast** for ~10 s, plug back in, wait 60 s. | Audio comes back somewhere (Cast or local). Fail = cabinet silent, local speakers still muted. | `AUD-37` | | |
| **1.6** | **Voicemail + tap (PHN-2 U5).** With the radio playing, open Phone → a voicemail → play it, then **tap the progress bar** partway along. Also try mute and the volume knob during it. | Voicemail ducks the radio, the tap jumps position, mute/volume apply to it. | `PHN-2` U5 | | |
| **1.7** | **Notification priority.** Settings → Notifications: trigger a low-priority (e.g. 2) and, while it speaks, a high-priority (e.g. 9) announcement. | The high-priority one takes over (or is clearly prioritised); the room never has two voices at once. | `TTS-4` | | |
| **1.8** | **Recent fixes (queue rows merged, never UAT'd):** play `Meditating Beat.mp3` and `Hear What They Say.mp3` from Files; skip tracks quickly a few times; then stop everything and leave it idle 2 minutes. | Tagged artist/title shown correctly (not a fingerprint guess); a skipped-from track's ID never lands on the next one; idle console isn't busy. | `AUD-32` `AUD-33` `AUD-35` | | |
| **1.9** | **Knob feel** (the encoder harness covers the rest remotely): a slow and a fast spin on each knob; SOURCE and PRESETS overlays; a long-press on VOLUME. | Feels right; nothing jumps. Note anything odd. | ENC arc | | |

## What I run remotely during or after (you don't need to do these)

- RDS: CDP poll of the ticker's `_debugState` offset and a scan of `RDS: Station name` log lines over the same window as 1.1 (with `RTLSDRCore` raised to Debug for the window — since `LOG-12` only the first name per tune is Information).
- `AUD-47` volume policy (`wpctl`) and `AUD-39` AVRCP-vs-underrun correlation during 1.2/1.3's BT session.
- Encoder re-verification with `tools/encoder-harness/virtual_encoder.py` (ENC-5/6/7 scenarios).

## Notes

_Anything else you noticed:_
