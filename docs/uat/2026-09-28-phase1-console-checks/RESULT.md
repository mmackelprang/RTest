# Phase 1 console checks — results, 2026-09-28

Run by the owner at the cabinet against [`CHECKLIST.md`](CHECKLIST.md). Box verified at `4adfbe9`
before the sitting, then `a4d52f3` after the mid-sitting RDS head fix (#684). Station for 1.1:
Rock 92, 92.3 FM, PI `0x70DB` → call sign WKRR.

| # | Check | Result |
|---|---|---|
| 1.1 | RDS scroll: no jump on RT update or PS flip | ⛔ → ✅ **FAIL, fixed in the sitting, then PASS.** First pass: *"once the entire string has rolled by, the beginning of the next cycle will have the first few characters replaced a couple of times before settling down"* — *"'Rock 92' is overwritten by 'The Cars'"*. Cause: Rock 92 rolls its **PS** through artist/title every ~3 s (`Rock 92` → `Cars` → `Good` → `Times` → `Roll`, every logged page clean), and the head leads each cycle. #684 binds the head to the PI call sign. After deploy: *"The scrolling is now fixed."* ⭐ The decoder fixes (`AUD-69`/`AUD-70`) are confirmed: no hybrid page in the log window. |
| 1.1a | Speed alternates on RT updates (`AUD-64`) | ✅ **Not observed** — *"I don't see a speed change with the scrolling."* `AUD-64` stays filed (the mechanism is in the code) but has no visible symptom on this station; P2, not a GA item. |
| 1.1b | D-B: rolling PS vs call sign | ✅ **Decided by the observation** — call sign, shipped as #684. Owner note: *"The station name always scrolls with the text when visible — it's not separate from it."* That is the single-row design (`RdsCard_RendersAsSingleRow_PSAndRtShareOneLine`); whether the name should instead be pinned and only the RT scroll is an **open product question**, not a defect. |
| 1.2 | BT: Pause on the console | ⛔ **FAIL — `AUD-40` CONFIRMED.** *"Pausing from the phone works, pausing from the console doesn't pause the music in BT mode."* Exactly the row's prediction: the console path sets `State = Paused` and never pauses the mixer component; the phone path is different (`OnPlaybackStatusChanged`). |
| 1.2+ | BT volume, observed alongside | ⚠ **Two findings, recorded against `AUD-47`.** (a) *"Changing volume on my phone changes audible volume on the console, but doesn't change the slider"* — consistent with `AUD-47`: `OnTransportPropertiesChanged` raises `VolumeChanged` and **nothing subscribes**. (b) *"Changing the slider by hand on the console changes volume on the soundbar, but doesn't affect the phone"* — the slider is the console's master (local output), which is behaving as built; whether it should also drive the phone via AVRCP absolute volume is a **product question**. |
| 1.2+ | BT disconnect | ✅ *"Disconnecting BT from the phone drops the album art and artist info immediately."* Expected: the source is gone. Recorded so nobody files it. |
| 1.1c | Owner follow-ups on the ticker | ✅ **Both shipped in the sitting as [#687](https://github.com/mmackelprang/RTest/pull/687):** *"pin the station name so it's always visible"* (name is now its own element beside the ticker) and *"a significant space between the end of one scroll and the beginning of the next ... always data scrolling rather than a blank time"* (seamless loop; the only gap is the separator). Verified in headless Chromium against the real engine + stylesheet: 257 samples over 6 wraps, **text covers 100 % of the strip at every sample**, each wrap is exactly one loop period. ⚠ Not yet watched on the panel (source was the file player); the harness ticker came out ~174 px wide with the pinned name, label and PTY sharing the 420 px card — **look at whether it feels cramped**. |
| 1.3 | Stop Scan → disconnect/reconnect phone | ⛔ **FAIL — but not `AUD-41`.** *"Album art and artist shows, but music isn't heard or seen in visualization."* Measured on the box: the first-connect PBAP sync finished, then `PbapSyncService` called `ConnectAsync` on the **still-connected** phone; BlueZ renegotiated its profiles, the `bluez_input` node vanished 250 ms later and the hci0 card came back in `audio-gateway` (the call profile) instead of A2DP. Metadata travels over AVRCP, so art and title still showed. Fixed as [#686](https://github.com/mmackelprang/RTest/pull/686): reconnect only when the phone actually dropped. ⚠ **Re-check pending.** A guest's phone hits this on its FIRST connection; to reproduce on the owner's phone it must be forgotten and re-paired. `AUD-41` (Stop Scan disposes the watcher) was not exercised by this failure and stays open. *(The console was also muted at the time — deliberately, by the owner after verifying sound; not a factor.)* |
| 1.4 | Cast volume after restart (`AUD-38`) | ⏸ **DEFERRED by the owner** — *"we need to defer the casting fixes for now. I'll UAT casting and these failure modes later."* |
| 1.5 | Chromecast dropped mid-stream (`AUD-37`) | ⏸ **DEFERRED by the owner** — same ruling. The Cast arc (2c in the close-out plan) moves after the other build phases. |
| 1.6 | Voicemail over the radio + tap (`PHN-2` U5) | ✅ **PASS for playback, ducking, mute and volume** — *"looks like it works."* ⚠ **The tap-to-seek half could not be run:** *"the progress bar on voicemails either doesn't allow dragging or is hard to grab."* Both are true of the code: `VoicemailPlayer.razor`'s `.vm-scrubber` handles `@onclick` only — no pointer-move, so a drag does nothing — and the target is the thin dock progress bar. Filed as **`UI-16`**. `PHN-2` U5 therefore stays open until `UI-16` gives it a grabbable control. |
| 1.7 | Notification priority (`TTS-4`) | ✅ **PASS** — `TTS-4` struck on the punch list. |
| 1.8 | `AUD-32` / `AUD-33` / `AUD-35` | ✅ **PASS** — tags read correctly, no stale identification on skip, idle console quiet. All three can be archived. |
| 1.9 | Knob feel | ✅ **PASS.** |

## Shipped during the sitting

| PR | What |
|---|---|
| [#684](https://github.com/mmackelprang/RTest/pull/684) | RDS head shows the PI call sign, not the rolling PS (D-B) |
| [#685](https://github.com/mmackelprang/RTest/pull/685) | `AUD-40`: console Pause/Play in BT mode ask the phone over AVRCP — ⚠ re-check 1.2 pending |
| [#686](https://github.com/mmackelprang/RTest/pull/686) | The post-PBAP-sync reconnect no longer drops a live A2DP link (1.3) |
| [#687](https://github.com/mmackelprang/RTest/pull/687) | Station name pinned; RadioText loops seamlessly |

## Owner rulings recorded

- Console volume slider **not** driving the phone's volume is **acceptable — do not change it.**
- Casting UAT and the Cast rows (`AUD-37`, `AUD-38`, `AUD-54`, `AUD-5`) are **deferred** until the owner schedules them.

## Re-checks — ✅ all accepted by the owner, 2026-09-28

*"These are all accepted."* — 1.2 (#685), 1.3 (#686) and the ticker at panel width (#687).

### (as listed before the re-check)

1. **1.2 again** — console Pause/Play on a BT source now pauses/resumes the phone (#685).
2. **1.3 again** — reconnect the phone; ideally forget and re-pair once to exercise the first-connect sync path (#686).
3. **The ticker at panel width** — pinned name + seamless loop (#687), on an RDS station.
