# Checklist for when you return

**Session paused 2026-09-28 ~17:00 EDT for your testing and review.** Box and `main` are both on
`30e69df` (both services SHA-verified, kiosk live). Panel is **on**; `ENC-22` is **off**; one real
encoder connected; firmware check passed.

Work top to bottom — it is ordered so the cheapest checks come first and each item needs only what the
ones above it established. Detail and the "what I already verified" record for every item is in
[`OWNER-REVIEW.md`](OWNER-REVIEW.md); this file is the walk-through.

## 0. Before anything (30 s)

- [ ] **Box matches `main`:** `pwsh deploy/Deploy-ToLinux.ps1 -VerifyOnly` → `=== Box matches HEAD ===`.
- [ ] **Console not muted**, volume audible. (One sitting has already been wasted on this.)

## 1. Glances at the panel — no sound needed (5 min)

- [ ] **DevTray log card:** triple-tap top-right → the *Verbose logs* card reads "config · tap for Debug"; tap → "runtime · tap to reset"; tap → back. *(LOG-5)*
- [ ] **Firmware line:** System Config → Integrations → Rotary Encoders → *Firmware:* reads "processes settings ✓" in green. *(ENC-19)*
- [ ] **No flicker on restart:** watch the panel while running `ssh mmack@radio 'sudo systemctl restart radio-api'`. Pass = no blink when the app sends "panel on" to a panel already on. *(ENC-22 side-effect)*
- [ ] **RDS ticker** on an FM station (e.g. Rock 92): call sign pinned on the left, song text loops with no blank gap. Already accepted — only re-look if anything seems off at the new log levels.

## 2. By ear (25–30 min, mostly just listening)

- [ ] **Distortion A/B (the point of Phase 2d).** 20+ min of FM on this build. Pass = no new artefacts, ideally fewer. Then flip *Verbose logs* on (DevTray) for 10 min and back off. If Verbose makes it worse, log volume is still an audio problem — note the times. *(LOG-2/5/6/7/8/12)*
- [ ] **Optional — two announcements at once:** send two long test notifications a second apart. Two voices together → tell me and `AUD-73` goes to P1.

## 3. With your phone on Bluetooth (5 min)

- [ ] **BT capture statistics still work** (the four commands are in `OWNER-REVIEW.md` → Phase 2d → "LOG-6 on a real BT session"). Pass = a line every ~10–12 s while playing, none while paused. *(LOG-6)*

## 4. Panel power-off — `ENC-22`, in this exact order (15 min + overnight)

- [ ] **4.1 Physical check BEFORE enabling:** dark the panel by hand, turn VOLUME one detent, confirm the volume changed. Exact commands in `OWNER-REVIEW.md` → Phase 2g → step 1. This is the one thing no script could prove.
- [ ] **4.2 Confirm the stop-time backstop:** `ssh mmack@radio 'systemctl show radio-api -p ExecStopPost'` prints the `gdbus … PowerSaveMode <0>` line.
- [ ] **4.3 Enable:** add `"Sleep": { "PanelOffAfterMinutes": 10 }` to `/opt/radio-console/api/appsettings.Production.json` (no restart needed; deploys never overwrite it).
- [ ] **4.4 Real wake:** reach the sleep screen, wait 10 min for dark, turn a knob → lit within ~2 s (panel splash expected), same sleep screen, the turn itself changes nothing. Repeat with a press.
- [ ] **4.5 Overnight:** leave it; dark in the morning; one knob wakes it.

## 5. Next day (2 min)

- [ ] **UI-10 over 24 h:** `ssh mmack@radio "journalctl -u radio-web --since '-24h' --no-pager | grep -c 'Visualization hub reconnecting'"`. Pass ≤ 5 (was 535/day). Then UI-10 archives.

## 6. Decisions only you can make

- [ ] **`AUD-75` — file player media root.** The box's config store pins it to a dev checkout path (`/home/mmack/RTest/src/Radio.API/media/audio`), overriding `/mnt/nas/music`, which listed nothing on 2026-09-28. Where should the root be, and is the NAS mount expected to be up? *(`queue/AUD-75.md`)*
- [ ] **Queue the UI-10 follow-ups?** A `Levels` subscription leak in `GainControlPopover`, and the API's "skip FFT when nobody's watching" gate being effectively always on. CPU saving, not correctness.
- [ ] **Judgement calls to overrule or accept** (each listed in `OWNER-REVIEW.md`): LOG-2 carve-outs (Audio.Services / Audio.SoundFlow kept at Information; Cast at Warning); ENC-22's wake-lights-but-stays-on-sleep-screen, 2 s grace window, knob-restarts-countdown; ENC-19's separate pre-push check.
- [ ] **Still yours, still deferred:** casting UAT + `AUD-37`/`AUD-38`/`AUD-54`/`AUD-5`; `OPS-3` (`BindsTo=`), which you review personally.

## 7. What resumes after this

Order from `HANDOFF-GA-CLOSEOUT.md`: **2f** queue rows with visible symptoms (`AUD-15` BT buffer runs empty, `AUD-13` USB port fallback, `AUD-27` art vanishes on pause, `AUD-28` seek-on-release, `AUD-29` BT position bar, `AUD-17`, `AUD-18`, `AUD-14`, `AUD-25`, `AUD-34`, `UI-15`, `AUD-74`) and `UI-16` (voicemail scrubber drag) → **2h** Metrics under Settings (`UI-2`/`UI-4`) → **2i** confirm-or-close + **`AUD-16` RaddyRF320BT removal** → **2j** hygiene → **2k** cross-repo filings → **2c** casting, when you schedule it. Tell me "resume" and I'll dispatch 2f the same way as today.

## Today, for the record

27 PRs (#677–#709): deploy from Linux + deploy safety (`OPS-12`/`OPS-13`), CI green again (`TEST-11`), Phase 0 record fixes, Phase 1 cabinet sitting (RDS head/pin/loop, `AUD-40`, the PBAP reconnect bug), 2a (`AUD-41`, `AUD-39`), 2b (`AUD-26`, `TTS-5`, `TTS-2`, `TEST-8`; `TTS-6` stale), 2d (`LOG-5/2/6/7/8/12`, `UI-10`), 2g (`ENC-22` off-by-default, `ENC-19`). Filed: `UI-16`, `ENC-22`, `AUD-73`, `AUD-74`, `AUD-75`, `OPS-13`, `TEST-11`.
