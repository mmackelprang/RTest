# Owner review list

**The one place to look when you are back.** Maintained during autonomous work from 2026-09-28. Each
entry is something that could not be validated without you — a physical action, your ears or eyes, or
a decision — with what to do and what a pass looks like. Newest phase at the bottom. Tick the box or
write a note beside it; the next session reads this file first.

Box state at the time of writing is in the last line of each section (`-VerifyOnly` output).

## Already passed today (for the record)

- ✅ Phase 1 console checks — [`2026-09-28-phase1-console-checks/RESULT.md`](2026-09-28-phase1-console-checks/RESULT.md).
- ✅ Phase 2b by-ear checks — AUD-26 (switch during a duck), TTS-6 (switch back), AUD-26 same-source Next, TTS-2/TEST-8 notification. Owner: *"These pass."*

## Standing items (carried from earlier today)

- [ ] **AUD-73 (optional, 1 min):** send two long test notifications a second apart. Two voices at once = promote AUD-73 to P1.
- [ ] **ENC-22 physical check:** the one thing the feasibility test could not prove — that a real knob turn is delivered while the panel is dark. **Built; the check and the enable step are in Phase 2g below.**
- [ ] **OPS-3** (`BindsTo=` for radio-web) — owner reviews personally; not attempted autonomously.
- [ ] **Casting** — deferred by you; `AUD-37`, `AUD-38`, `AUD-54`, `AUD-5` untouched.


## Phase 2d — logging

Shipped and deployed: `LOG-5` [#699](https://github.com/mmackelprang/RTest/pull/699) · `LOG-2` [#700](https://github.com/mmackelprang/RTest/pull/700) · `LOG-6` [#701](https://github.com/mmackelprang/RTest/pull/701) · `LOG-7` [#702](https://github.com/mmackelprang/RTest/pull/702) · `LOG-8` [#703](https://github.com/mmackelprang/RTest/pull/703) · `LOG-12` [#704](https://github.com/mmackelprang/RTest/pull/704) (new row, your "remove low-value log items" request) · `UI-10` [#705](https://github.com/mmackelprang/RTest/pull/705) (the visualization-hub reconnect loop). `LOG-4` was skipped per D-A.

**What I verified on the box myself** (2026-09-28, build `b8162e8`):
- **`LOG-5`, runtime log levels:**
  - `RTLSDRCore` to Debug took the file sink from 0 to 242 Debug lines in 60 s. After the reset it went back to 0, with no restart.
  - The change and the reset each wrote one Warning to the journal.
  - `PUT … {"level":"Fatal"}` is refused (400).
  - All ten switches are listed at their configured levels. Nothing on the box (Production overlay, SQLite store) overrides them.
- **`LOG-2`:** `Radio.Infrastructure.Audio` and `...Platform.Bluetooth` are configured at Warning. Raising Audio to Debug brought the SDR generator's lines back, and a reset stopped them.
- **`LOG-7`:** those stats lines now arrive at exactly 10.000 s intervals, which is timer cadence rather than callback timing. The missed-deadline Warning is unchanged.
- **`LOG-8`:** during that Debug window the RDS decoder's search/confirm lines ran at ~285/min each. They were cut to 120/min, and each got one counted `LOG-8: rate limit suppressed 165 events …` Warning. The backstop worked on a real flood.
- **Volume, 30 min before (14:30–15:00) vs 30 min after (15:04–15:34) the deploy:**

  | | before | after |
  |---|---|---|
  | radio-api file sink | 476 lines | **69** (−85 %) |
  | radio-web journal | 99 lines | **0** |
  | `Visualization hub reconnecting` | 33 | **0** |
  | radio-api journal | 8 | 11 (2 are my `LOG-5` flip and reset of the Audio namespace; the RTLSDRCore ones were before 15:04. 2 are the `LOG-8` reports, and 7 are missed-deadline Warnings) |

**What needs you:**

- [ ] **Distortion, by ear (the reason this phase exists).** Listen to FM (and BT if you can) for 20+ minutes on this build. **Pass:** no new artefacts, and ideally fewer than before. **A/B in 2 seconds, no restart** (this is what `LOG-5` is for): open the DevTray (triple-tap top-right), tap **Verbose logs** ("config · tap for Debug" changes to "runtime · tap to reset"), listen for 10 minutes, then triple-tap to reopen the tray (it auto-locks after 30 s) and tap again to reset. Don't leave it on: that would contaminate the comparison. If audio is worse with Verbose on, log volume is still an audio problem. Note the time of any artefact and I can correlate it.
- [ ] **DevTray card works on the panel.** Triple-tap and check the card reads "config · tap for Debug". Tap it and it reads "runtime · tap to reset"; tap again and it goes back. **Pass:** exactly that. It is unit-tested but not driven on the kiosk.
- [ ] **`LOG-6` on a real BT session** (needs your phone; BT was idle today).
  1. `curl -X PUT http://radio:5000/api/system/logging/levels/Radio.Infrastructure.Platform.Bluetooth -H 'Content-Type: application/json' -d '{"level":"Information"}'`
  2. Play from the phone for 2 min, then: `ssh mmack@radio 'grep -h "PipeWire OnProcess" /opt/radio-console/logs/radio-$(date +%Y%m%d)*.txt | tail'`
  3. Pause the phone for 1 min and check the lines stop.
  4. `curl -X POST http://radio:5000/api/system/logging/levels/reset`

  **Pass:** a line every ~10–12 s while playing, and none while paused. That silence is what `bt_stall_detect.py` depends on.
- [ ] **UI-10 over a full day.** 30 minutes at 0 is strong but short, and CLAUDE.md warns against claiming a fix on a quiet hour. Tomorrow: `ssh mmack@radio "journalctl -u radio-web --since '-24h' --no-pager | grep -c 'Visualization hub reconnecting'"`. **Pass:** ≤ 5 in 24 h. The baselines were 535 on 2026-09-27, 56 in the hour before the fix, and 33 in the half-hour before it. Then UI-10 can move from ✅🔬 to the archive.
- [ ] **Judgement calls I made; overrule if you disagree:**
  - `LOG-2` carve-outs: `Audio.Services` (source switching, announcements) and `Audio.SoundFlow` (device selection) stay at Information. Cast (`Audio.Outputs`) lines are now at Warning, so raise that namespace when casting work (2c) starts.
  - `LOG-2` lives in the shipped `appsettings.json`, not the Production overlay, because the overlay is seed-only.
  - `LOG-12`: the SDR drift line is a 5-minute tally, and a SongRec repeat is Information again after 10 minutes.
- [ ] **Filed, not fixed: `AUD-75`, the file player's media root is a dev checkout path.** The box's SQLite config store holds `fileplayer:rootDirectory = /home/mmack/RTest/src/Radio.API/media/audio` (since 2026-02-12). It overrides `/mnt/nas/music` and rejects real files in `/opt/radio-console/media/audio` as "path traversal". Your call on the root and on bookmark semantics; see [`queue/AUD-75.md`](../queue/AUD-75.md). `/mnt/nas/music` listed nothing on 2026-09-28, so check the NAS mount first.
- [ ] **Filed, not fixed, found while fixing UI-10:**
  - `GainControlPopover` subscribes the visualization hub to `Levels` and never unsubscribes;
  - radio-web's never-stopped connection keeps `ConnectedClients ≥ 1`, so the API's "skip FFT when nobody is watching" gate is effectively always off.

  Both are in [`queue/UI-10.md`](../queue/UI-10.md). Queue them if you want the CPU back.
- [ ] **`LOG-10` (SCHED_FIFO) is still blocked. O4 is not discharged by `LOG-6`.** The capture thread still takes `AddSamples`' lock, calls `Marshal.PtrToStructure` (allocating), and its loop's `OnStateChanged` logs. The render path still calls `IMetricsCollector.Increment` (allocates and locks). This is recorded in the punch-list O4 cell.
- [ ] **An observation for `AUD-20`, not caused by this phase.** The GC-correlated missed-deadline lines show roughly 0.25 full (Gen2) collections per second, both before and after this deploy (e.g. 13:40 `Gen2 +57` over 4 min; 15:20 `Gen2 +77` over 5 min). The Gen1-to-Gen2 ratio changed (now equal), but the rate did not.

`-VerifyOnly` (2026-09-28 15:02 EDT): `API (:5000): running b8162e8 - matches` · `Web (:5002): running b8162e8 - matches` · `Kiosk: 2 established connections to :5002` · **`=== Box matches HEAD ===`**


## Phase 2g — panel power-off and firmware check

Shipped: ✅🔬 `ENC-22` [#707](https://github.com/mmackelprang/RTest/pull/707) — the panel powers off after a period on the sleep screen, and any knob wakes it. **It shipped DISABLED** because the check below needs your hand. `ENC-18` and `ENC-14` were skipped per D-A.

**What I verified on the box myself** (build `47b55ae`, timeout temporarily 1 min, virtual encoder harness; full table in [`queue/ENC-22.md`](../queue/ENC-22.md)):
- (a) Sleep screen by the idle route: power-off after exactly 1 min, `dpms=Off`, and the encoder stays connected.
- (b) `turn` while dark lights the panel. The turn is consumed; volume and mute are unchanged.
- (c) `tap` / `press` while dark light the panel. The VOLUME press did not toggle mute.
- (d) Unplugging the encoder while dark lights the panel within about a second. With the encoder unplugged, the timer declines to power off.
- (e) `systemctl restart radio-api` while dark lights the panel. SIGKILL while dark also lights it within 0.5 s, from `ExecStopPost=` alone.
- (f) A deploy while dark ends with the panel on and the kiosk live.
- Then set back to disabled and confirmed: the panel stays on for 73 s on the sleep screen.

**What needs you — ENC-22, in this order:**

- [ ] **1. The physical check, BEFORE enabling (2 min).** This is the one thing no script can do: a real knob, turned by a hand, delivered while the panel is dark. Temporarily dark the panel by hand, then turn a knob:
  ```bash
  ssh mmack@radio
  export DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus
  gdbus call --session --dest org.gnome.Mutter.DisplayConfig --object-path /org/gnome/Mutter/DisplayConfig \
    --method org.freedesktop.DBus.Properties.Set org.gnome.Mutter.DisplayConfig PowerSaveMode "<3>"
  ```
  The panel goes dark. Turn **VOLUME** one detent.

  ⚠ With the feature disabled the app does not light a panel that *you* darkened: it only tracks darks it caused. So in this step you are checking that the knob event **arrives**, not that the panel wakes:
  ```bash
  ssh mmack@radio 'journalctl -u radio-api --since "-2min" --no-pager | tail -3; F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -h "encoder\|Encoder" $F | tail -5'
  ```
  Also check `curl -s http://radio:5000/api/audio/volume` moved by 1 point.

  **Pass:** the volume changed while dark. Then light the panel again with the same command using `"<0>"` (or `sudo systemctl restart radio-api`).
- [ ] **2. Enable it.** Add this to `/opt/radio-console/api/appsettings.Production.json`. A deploy never overwrites that file, and no restart is needed:
  ```json
  "Sleep": { "PanelOffAfterMinutes": 10 }
  ```
  First confirm the stop-time backstop is installed. It came from provisioning, not a deploy, and it is on `radio` since today:
  `ssh mmack@radio 'systemctl show radio-api -p ExecStopPost'` must print the `gdbus … PowerSaveMode <0>` argv.
- [ ] **3. Real wake, by hand.** Let the console reach the sleep screen, wait 10 min for the panel to go dark, then turn a knob. **Pass:** the panel lights within ~2 s. The panel's own splash for ~2 s is expected. You land on the same sleep screen, and that first turn changes nothing. Repeat with a press.
- [ ] **4. One overnight cycle.** Leave it on the sleep screen overnight. **Pass:** dark in the morning, and one knob wakes it.
- [ ] **Judgement calls I made; overrule if you disagree:**
  - **Wake lights the sleep screen; it does not leave it.** This is the dossier's default. A second input then acts as it does on the lit sleep screen.
  - **Grace window:** for 2 s after a knob wake, all knob input is consumed. The panel is showing its splash, so a fast VOLUME spin that woke it must not change the volume unseen. Setting: `Sleep:WakeGraceMilliseconds`.
  - **Knob activity on the lit sleep screen restarts the countdown.** Touch cannot, because touch never reaches the API.
  - **An unconfirmed power-off is treated as dark.** gdbus reports failure on a reply timeout, so the panel is sent "on" at once. From the pre-merge review.
  - **Default suggestion 10 min,** from the dossier.
- [ ] **Known limitation (recorded, not fixed):** if both "sleep screen closed" reports are lost (a WiFi blip during a hard navigation), the server keeps believing the sleep screen is up. The panel can then go dark N minutes into normal touch use. It is still knob-wakeable. Details are in `design/INTEGRATIONS.md` §1.
- [ ] **Not verified: `PowerSaveMode 0` sent to a panel that is already on.** It happens at every `radio-api` start and stop. I saw no kiosk disconnect and no `dpms` change, but I did not watch the panel for a flicker. Glance at it during the next restart.
- **Build baseline moved to 32 warnings (Linux) from 33.** The warning that went is the `IDE0011` in the deleted `SleepService.SetDisplayPowerAsync`. I measured `main` and the branch side by side.

