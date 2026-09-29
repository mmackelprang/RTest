# Checklist for when you return

**Session paused 2026-09-28 ~17:00 EDT for your testing and review.** Box and `main` are both on
`30e69df` (both services SHA-verified, kiosk live). Panel is **on**; `ENC-22` is **off**; one real
encoder connected; firmware check passed.

Work top to bottom — it is ordered so the cheapest checks come first and each item needs only what the
ones above it established. Detail and the "what I already verified" record for every item is in
[`OWNER-REVIEW.md`](OWNER-REVIEW.md); this file is the walk-through.


## ⭐ Evening batch, 2026-09-29 — test these first

**What is on the box:** `7dd34b5` = `main` (`dffb55f`) **plus the unmerged `AUD-14` fix** (PR #726, held for your phone test). So `-VerifyOnly` against `main` will report a mismatch until #726 merges — that is expected. The audio output is on the **built-in speakers** (switched from the console at 16:57), not Cast.

### A. At the panel, no phone needed (10 min)
- [ ] **Diagnostics (`UI-2`, #728):** no Metrics pill in the top nav; Settings has **Diagnostics** as its last tab; tiles grouped by category, header "Updates every 15 s while open"; tap a tile → its chart, tap again → closed; 5m/1h/24h changes the tiles and survives a reload; DevTray → Fingerprint events lands on Diagnostics.
- [ ] **File player seek (`AUD-28`, #722):** drag the seek bar slowly, then fast, across a playing track — no stutter while dragging, one jump on release; the time follows your finger; a tap still seeks; the transport bar layout looks right.
- [ ] **Voicemail scrubber (`UI-16`, #722 — `PHN-2` U5):** play a voicemail; drag and tap both move the audio; the bar is easy to grab.
- [ ] **USB inputs (`AUD-13`, #719):** Vinyl still plays the turntable; **USB Audio shows Error** (unconfigured, as you ruled).
- [ ] **Centre panel (`UI-17`, #715/#716 — still outstanding from this afternoon):** each source's default view; the Stats chip (off by default, survives reload); Queue pill on Radio shows the queue with no ⋮ menu and the radio keeps playing; Bluetooth with nothing connected opens on Connect.

### B. With your phone on Bluetooth (15 min)
- [ ] **Position bar (`AUD-29`, #724):** play a track — the console's bar moves with the phone; pause on the phone and it stops; seek on the phone and it jumps to match.
- [ ] **Re-attach (`AUD-14`, #726 — then tell me to merge it):** pause on the phone for 30 s+ and resume; then disconnect and reconnect the phone while playing. Pass = the console follows play/pause/track after each.
- [ ] **Announcements (`AUD-74`, #723):** two test announcements close together — the music must not swell back to full volume between them.

### C. Cast volume (`AUD-80` + `AUD-5`, #725) — 5 min
- [ ] Switch output to the Office speaker. Set the **speaker's** volume to about 25 % **on the speaker or Google Home** (the console cannot change it — see `AUD-81`). Disconnect Cast, reconnect: it comes back at 25 %, not loud. Repeat once after a `radio-api` restart. (The very first reconnect after the deploy keeps whatever the speaker is at, because nothing is remembered yet.)

### D. Decisions only you can make
- [ ] **`AUD-17` — Bluetooth album art from the phone:** measured, your phone offers no cover art to BlueZ. **A (recommended):** close the dead code path; art keeps coming from song recognition. **B:** experiment with BlueZ's experimental cover art (starts with the boundary doc).
- [ ] **`AUD-81` — should the console's volume drive a connected Cast speaker?** Today only the speaker/Google Home can change it.
- [ ] **Optional box cleanup (`AUD-80`):** the stale case-duplicated keys `audiopreferences:masterVolume/currentOutput/currentSource` are never read; delete them after a DB backup if you like (SQL in PR #725).

### Already verified on the box, nothing to do
`AUD-78` (history finalised at shutdown — clean on three deploys), `AUD-79` (Cast underwater — you confirmed), `AUD-18` watchdog (#727; trips after 5 min of genuinely empty captures). `AUD-27`, `AUD-25` did not reproduce; `AUD-73`'s by-ear check was clean.

---

## 0. Before anything (30 s) — passed 2026-09-29

- [x] **Box matches `main`:** `pwsh deploy/Deploy-ToLinux.ps1 -VerifyOnly` → `=== Box matches HEAD ===`.
- [x] **Console not muted**, volume audible. (One sitting has already been wasted on this.)

## 1. Glances at the panel — no sound needed (5 min) — passed 2026-09-29

> DevTray passed, but opening it needed a mouse: a finger cannot reach the top-right corner tap zone. Moved to the slot left of **Home** on branch `fix/devtray-gesture-left-of-home` (not yet merged/deployed).

- [x] **DevTray log card:** triple-tap the blank space just left of **Home** in the top nav → the *Verbose logs* card reads "config · tap for Debug"; tap → "runtime · tap to reset"; tap → back. *(LOG-5)*
- [x] **Firmware line:** System Config → Integrations → Rotary Encoders → *Firmware:* reads "processes settings ✓" in green. *(ENC-19)*
- [x] **No flicker on restart:** watch the panel while running `ssh mmack@radio 'sudo systemctl restart radio-api'`. Pass = no blink when the app sends "panel on" to a panel already on. *(ENC-22 side-effect)*
- [x] **RDS ticker** on an FM station (e.g. Rock 92): call sign pinned on the left, song text loops with no blank gap. Already accepted — only re-look if anything seems off at the new log levels.

## 2. By ear (25–30 min, mostly just listening) — passed 2026-09-29 (audio and notifications good; `AUD-73` stays where it is)

- [x] **Distortion A/B (the point of Phase 2d).** 20+ min of FM on this build. Pass = no new artefacts, ideally fewer. Then flip *Verbose logs* on (DevTray) for 10 min and back off. If Verbose makes it worse, log volume is still an audio problem — note the times. *(LOG-2/5/6/7/8/12)*
- [x] **Optional — two announcements at once:** send two long test notifications a second apart. Two voices together → tell me and `AUD-73` goes to P1.

## 3. With your phone on Bluetooth (5 min) — passed 2026-09-29 (a stats line every ~11 s)

- [x] **BT capture statistics still work** (the four commands are in `OWNER-REVIEW.md` → Phase 2d → "LOG-6 on a real BT session"). Pass = a line every ~10–12 s while playing, none while paused. *(LOG-6)*

## 4. Panel power-off — `ENC-22`, in this exact order (15 min + overnight) — passed 2026-09-29; `ENC-22` archived

- [x] **4.1 Physical check BEFORE enabling:** dark the panel by hand, turn VOLUME one detent, confirm the volume changed. Exact commands in `OWNER-REVIEW.md` → Phase 2g → step 1. This is the one thing no script could prove.
- [x] **4.2 Confirm the stop-time backstop:** `ssh mmack@radio 'systemctl show radio-api -p ExecStopPost'` prints the `gdbus … PowerSaveMode <0>` line.
- [x] **4.3 Enable:** add `"Sleep": { "PanelOffAfterMinutes": 10 }` to `/opt/radio-console/api/appsettings.Production.json` (no restart needed; deploys never overwrite it).
- [x] **4.4 Real wake:** reach the sleep screen, wait 10 min for dark, turn a knob → lit within ~2 s (panel splash expected), same sleep screen, the turn itself changes nothing. Repeat with a press.
- [x] **4.5 Overnight:** leave it; dark in the morning; one knob wakes it.

## 5. Next day (2 min) — passed 2026-09-29; `UI-10` archived

> Run over ssh at 11:00. The literal `--since '-24h'` count is **116**, but every one of those lines is from 11:00–16:21 on 2026-09-28, before `radio-web` restarted onto the current build at 16:57:59. Since that restart (~18 h): **0**. The log line is still live — `HubReconnectLogging.cs` renders `"{Hub} hub reconnecting"` as `Visualization hub reconnecting` at Warning — so zero is a measurement, not a renamed string. Re-running the literal command after 16:58 today will read 0.

- [x] **UI-10 over 24 h:** `ssh mmack@radio "journalctl -u radio-web --since '-24h' --no-pager | grep -c 'Visualization hub reconnecting'"`. Pass ≤ 5 (was 535/day). Then UI-10 archives.

## 6. Decisions only you can make

- [x] **`AUD-75` — file player media root.** *Answered 2026-09-29:* `//nas.local/multimedia` at `/mnt/nas_media`; music root `/mnt/nas_media/Music`. The mount is fixed on the box (the old `//nas/...` fstab line never resolved). Pointing the app at it is the remaining `AUD-75` work. *(`queue/AUD-75.md`)*
- [ ] **Queue the UI-10 follow-ups?** A `Levels` subscription leak in `GainControlPopover`, and the API's "skip FFT when nobody's watching" gate being effectively always on. CPU saving, not correctness.
- [ ] **Judgement calls to overrule or accept** (each listed in `OWNER-REVIEW.md`): LOG-2 carve-outs (Audio.Services / Audio.SoundFlow kept at Information; Cast at Warning); ENC-22's wake-lights-but-stays-on-sleep-screen, 2 s grace window, knob-restarts-countdown; ENC-19's separate pre-push check.
- [ ] **Still yours, still deferred:** casting UAT + `AUD-37`/`AUD-38`/`AUD-54`/`AUD-5`; `OPS-3` (`BindsTo=`), which you review personally.

## 7. What resumes after this

Order from `HANDOFF-GA-CLOSEOUT.md`: **2f** queue rows with visible symptoms (`AUD-15` BT buffer runs empty, `AUD-13` USB port fallback, `AUD-27` art vanishes on pause, `AUD-28` seek-on-release, `AUD-29` BT position bar, `AUD-17`, `AUD-18`, `AUD-14`, `AUD-25`, `AUD-34`, `UI-15`, `AUD-74`) and `UI-16` (voicemail scrubber drag) → **2h** Metrics under Settings (`UI-2`/`UI-4`) → **2i** confirm-or-close + **`AUD-16` RaddyRF320BT removal** → **2j** hygiene → **2k** cross-repo filings → **2c** casting, when you schedule it. Tell me "resume" and I'll dispatch 2f the same way as today.

## Today, for the record

27 PRs (#677–#709): deploy from Linux + deploy safety (`OPS-12`/`OPS-13`), CI green again (`TEST-11`), Phase 0 record fixes, Phase 1 cabinet sitting (RDS head/pin/loop, `AUD-40`, the PBAP reconnect bug), 2a (`AUD-41`, `AUD-39`), 2b (`AUD-26`, `TTS-5`, `TTS-2`, `TEST-8`; `TTS-6` stale), 2d (`LOG-5/2/6/7/8/12`, `UI-10`), 2g (`ENC-22` off-by-default, `ENC-19`). Filed: `UI-16`, `ENC-22`, `AUD-73`, `AUD-74`, `AUD-75`, `OPS-13`, `TEST-11`.
