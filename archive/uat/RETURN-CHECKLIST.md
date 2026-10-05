# Checklist for when you return

**Session paused 2026-09-28 ~17:00 EDT for your testing and review.** Box and `main` are both on
`30e69df` (both services SHA-verified, kiosk live). Panel is **on**; `ENC-22` is **off**; one real
encoder connected; firmware check passed.

Work top to bottom — it is ordered so the cheapest checks come first and each item needs only what the
ones above it established. Detail and the "what I already verified" record for every item is in
[`OWNER-REVIEW.md`](OWNER-REVIEW.md); this file is the walk-through.


## ⭐ Evening batch, 2026-09-29 — test these first

**What is on the box (updated 2026-09-30, evening):** `646be99` = `main` — #736 (`AUD-84`) merged after your Cast-unplug UAT and deployed; both services SHA-verified, kiosk live. *(Was `b64c8cd` — #726 (`AUD-14`) merged after your phone UAT.)* *(Was `7dd34b5`, `main` plus the unmerged fix, for the evening tests.)*

### ✅ Results, 2026-09-30 (owner, late afternoon)

Owner, verbatim: *"Re-attach worked fine. Cast volume works now. BT position bar updates as expected. Vinyl sounds fine - even through casting. Bluetooth album art passes."*

- **Merged and deployed:** #726 (`AUD-14`) → squash `b64c8cd`.
- **Rulings:** `AUD-81` — *"I'd like the console volume to be able to change the cast volume."* `AUD-80` — remove the stale keys (**done**, below). §6 — queue the `UI-10` follow-ups (**filed**).
- **BT is quieter than Radio/Vinyl — measured 2026-09-30 with the Pixel streaming at ~75 % slider:** `bluez_input` node volume **1.00** (`wpctl get-volume`, channel and soft volumes 1.0) and the BlueZ `MediaTransport1.Volume` **127 (max)**. So the phone is **not** using absolute volume: its slider scales the PCM on the phone, invisible to the box — ⚠ **this contradicts the cubic-node-volume mechanism assumed in `AUD-47` and in `NormalizeBtNodeVolume`'s doc comment (`LinuxBluetoothService.cs:3336-3341`) for this phone.** ✅ **Closed by ear the same day — owner: *"radio volume and bt volume (when the phone volume is set to 100%) are very close."*** So the gap was the phone's own slider, not a systemic defect on the box. Match heard **with the existing ×2 BT source gain**, which therefore stays; it covers the FM-vs-streaming mastering difference. Remaining, optional: Pixel Developer options → "Disable Bluetooth absolute volume" off would turn the phone slider into a remote for the console volume instead of a PCM scaler (`AUD-47`'s "drive master from AVRCP" half); and the ×2 gain can hit the tanh limiter (`LimiterModifier.cs`) on full-scale masters — listen for harshness on loud tracks. `SourceRms` / `SourceGainMode` rows in the config store are dead data (nothing in `src/` reads them).
- **Still open from this batch:** ~~by-ear stutter on a slow file-player drag (`AUD-28`); whether the voicemail bar is easy to grab (`UI-16`); by ear, that two announcements do not swell and are both heard in full (`AUD-74`); the `AUD-13` no-feedback finding (accept, or surface the refusal); the `AUD-17` A/B decision (art is arriving via song recognition either way).~~ ✅ **All closed 2026-09-30, later — see below. Nothing from this batch is open.**

### ✅ Results, 2026-09-30 (owner, later)

Owner, verbatim: *"AUD-28 Passes."* *"UI-16 passes."* *"AUD-74 passes."* *"AUD-13 is ok as is."* *"AUD-17 recommendation is fine."* *"The current judgement calls for #6 are fine."*

- **Archived:** `AUD-28` + `UI-16` (#722, squash `60e68bc`), `AUD-74` (#723, squash `866e333`), `AUD-13` (#719, squash `2006974` — the silent refusal of the unconfigured "USB Audio", no Error and no toast, accepted as is).
- **`AUD-17`:** option A chosen — close the dead AVRCP cover-art path; art keeps coming from song recognition. Now a small removal row, sequenced with Phase 2i.
- **§6 judgement calls:** accepted (below).
- **Owner question: *"I don't see a slider for BT - should there be one?"*** No — by design. BlueZ's `org.bluez.MediaPlayer1` exposes `Position` read-only and offers only Play / Pause / Stop / Next / Previous / FastForward / Rewind (the last two press-and-hold); AVRCP has no absolute seek, and the BT source reports `SupportsSeek: false` (`BluetoothAudioSource.cs:231`). A non-draggable position bar is correct. Optional future idea, not filed: hold-to-fast-forward / hold-to-rewind buttons. (Recorded in the `AUD-29` archive entry.)

### 🔬 Casting baseline, 2026-09-30 (owner at the console, box on `b64c8cd`) — MEASURED

Owner, verbatim: *"The console did **not** notice when the speaker disconnected, but it did auto-reconnect and the previous volume. The console UI did appear to crash here, though- was there a depoyment? After restarting the UI, changing the volume on the console didn't affect the office speaker. Switching between speakers seems to work as expected."*

**There was no deployment.** Evidence from `journalctl -u radio-api` and `systemctl show` on `radio`:

1. **Unplugging the Office speaker crashed `radio-api` → filed `AUD-84` (🔴 P0, GA-blocking; [`queue/AUD-84.md`](../queue/AUD-84.md)).** 16:36:36 EDT `Unhandled exception. System.IO.IOException: Unable to write data to the transport connection: Broken pipe.` from `Sharpcaster.Channels.HeartbeatChannel.TimerElapsed` (an exception escaping an `async void` timer handler inside SharpCaster 3.0.0, the latest on NuGet) → 16:36:41 `code=dumped, status=6/ABRT` → 16:36:51 systemd restart; `radio-web` restarted in the same second, so the kiosk's circuit dropped (the "UI crash"); you relaunched the kiosk at 16:38:33. The phone had to reconnect. **The "auto-reconnect at the previous volume" was the restarted process restoring the saved Cast output with `AUD-80`'s remembered volume — not a reconnect.** `AUD-37` (punch list) predicted "never noticed"; the process dies instead. Its "notice and fall back" half is still open.
2. **Console volume does not reach the Office speaker** — exactly `AUD-81`, which you ruled buildable today. `AUD-38` is now marked superseded by `AUD-80` + `AUD-81` in the punch list.
3. **Switching Office → Kids room → Office (16:39) worked for you, but one request failed underneath:** at 16:39:12 your connect for "Office speaker" returned **500** (`Cannot connect in state Connecting`) because it raced the controller's own default-device auto-connect, which then failed at 16:39:13 (`Cannot start output in state Streaming`). Final state correct. Also: every teardown logs a Warning `Cast media stop failed during teardown` (`MediaSessionID is not available`) — expected in DirectChannel mode, so it is Warning-level noise. Recorded on `AUD-54`; its handler-leak half could not be judged because Cast logging is held at Warning (`LOG-2`).

#### ✅ `AUD-84` fixed and owner-verified, 2026-09-30 (evening) — and `AUD-85` found on the re-pick

- **`AUD-84` passed** on the branch build `079d46c`, unplugging the Office speaker (Google Home Mini) while casting. Owner, verbatim: *"powered up speaker again - audio started playing on console after disconnection."* then *"Re-picked Cast, it's playing on the office speaker again"*. Box: `17:31:42 WRN DirectCast: send of chunk seq 232 stalled for 5s — reporting the connection lost` → `Cast connection to "Office speaker" was lost (...) — switching to local output "Soundbar"`, ≈ 5 s after the unplug; `NRestarts` stayed 0; zero `Unhandled exception` lines. Merged as [#736](https://github.com/mmackelprang/RTest/pull/736) (squash `646be99`), deployed, both services verified on `646be99`, kiosk live. **Archived.** `AUD-37`'s "notice and fall back" core is done; auto-reconnect when the speaker returns is still open (you re-picked by hand).
- **`AUD-85` filed (🟠) from that re-pick:** Cast played, but the pick **erased the saved default Cast speaker**. The UI's connect raced the API's own auto-connect, got a 500 (`Cannot connect in state Connecting`, 17:32:55), and the UI cleared the default as if the speaker were unreachable (`AudioPreferences:DefaultCastDeviceId` is now empty). Same race as item 3 above. Until it is fixed, the next Cast pick opens the device dropdown instead of connecting straight away; choosing "Office speaker" there saves it as the default again. [`queue/AUD-85.md`](../queue/AUD-85.md).

> **Agent pre-pass, 2026-09-29 ~22:47–22:57 EDT (Windows dev machine).** Everything below that a
> machine can check was driven against the box (`7dd34b5`, both services SHA-verified): a separate
> 1920×720 browser on `radio:5002`, the API, and one bounded journal read. The master was **muted the
> whole time** (it was already muted at 0.12), so nothing was judged by ear. State was restored
> afterwards: Radio 97.7 playing, 0.12 muted, output Soundbar, Diagnostics range 5m, Stats chip off.
> **Side effects you may notice:** play history gained "Vinyl 22:50" and "Sgt. Pepper's 22:52"; the
> file player's queue position moved. Evidence screenshots are in the gitignored `.playwright-mcp/`
> on that machine.

### Morning order as written 2026-09-29 (items 1–3 passed 2026-09-30; item 4's Vinyl passed)

1. **Re-attach, `AUD-14` (section B) — then tell me to merge #726.** It is the only open PR, and `main` cannot be deployed until it merges.
2. **Cast volume (section C)** — not run by the agent: it needs the speaker's own volume set by hand, and Cast audio bypasses the console mute, so it would have played aloud in the office at night.
3. **BT position bar, `AUD-29` (section B)** — needs the phone; do it in the same sitting as item 1.
4. **By-ear only:** Vinyl with a record on (USB inputs), a slow file-player drag for stutter (file player seek), two announcements for the swell *and* for whether both are heard in full (announcements).
5. **Decide on the `AUD-13` finding below** (USB Audio gives no feedback) and glance at the minor findings.

### A. At the panel, no phone needed (10 min)
- [x] **Diagnostics (`UI-2`, #728)** — ✅ **agent-verified.** No Metrics pill in the top nav; Diagnostics is the last Settings tab; tiles grouped by category; header "Updates every 15 s while open"; tap a tile → chart with count/avg/min/max, tap again → closed; switching to 1h changed the tiles and **1h survived a reload** (read from `aria-pressed`), restored to 5m; `/diagnostics` (the DevTray card's target, per `DevTray.razor:355`) opens the panel. *Not done: the physical DevTray triple-tap.* **Minor findings:** the chart's y-axis runs negative (`-41.8 ms` on Request Duration); the **"Requests Api" tile always reads 0** because `api.requests.api` counts only bare `/api` — the real counts live in per-route keys (`api.requests.api.Audio.nowplaying`, …), so the tile is a mislabelled, near-useless key rather than a counting bug.
- [x] ✅ **Owner 2026-09-30: *"AUD-28 Passes."* → archived.** **File player seek (`AUD-28`, #722)** — ✅ **agent-verified by position, not by ear.** Slow 12-step drag while playing: the API position kept advancing normally (0:03 → 0:07) the whole time, then **one jump on release** to 1:20.6 (= 65 %, where the finger stopped). Fast drag: no movement while held, release → 0:37.8 (30 %). Tap at 10 % → 0:13. The on-screen time followed the finger (1:19 mid-drag). **Yours:** listen for stutter during a slow drag, and whether the transport layout looks right.
- [x] ✅ **Owner 2026-09-30: *"UI-16 passes."* → archived.** **Voicemail scrubber (`UI-16`, #722 — `PHN-2` U5)** — ✅ **agent-verified.** On an already-read voicemail: the server's playback anchor (`broadcastAtUtc`) **did not change once during a six-step drag** and changed exactly once on release, to 16.19 s (= 77 %, where the finger stopped); the label tracked the finger (0:04 → 0:14); a tap at 30 % → 6.3 s on the server. **Yours:** is the bar easy to grab with a finger.
- [x] ✅ **Owner 2026-09-30: *"AUD-13 is ok as is."* — the silent refusal is accepted; no follow-up row → archived.** **USB inputs (`AUD-13`, #719)** — ⚠ **half passes.** Vinyl: ✅ **owner 2026-09-30: *"Vinyl sounds fine - even through casting."*** (Agent: switched, `Playing`, bound to its own port, `USB Microphone`.) USB Audio: the backend refuses correctly (`no capture device matches USBPort "AB13X" — not binding to any input`, then `Failed to create source: GenericUSB`), so it **no longer silently plays the turntable** — the actual bug is fixed. **But the panel shows nothing:** no Error, no toast; the tap looks like a no-op and the previous source keeps playing (sampled every 0.5 s for 4 s). #719 says the source "goes to Error", but the exception fires during *creation*, so no source object ever exists to be in Error. Also: the configured port is `AB13X`, not empty — refused as *no match*, not as *unconfigured*. **Decide:** accept as is, or queue a small row to surface the refusal (a toast, or an error state on the bubble).
- [x] **Centre panel (`UI-17`, #715/#716)** — ✅ **agent-verified.** Radio: RADIO / HISTORY (+ QUEUE when opened). Vinyl: history with the Stats chip. File Player: QUEUE · 31 / HISTORY with the ⋮. Bluetooth with nothing connected: **opens on CONNECT** (paired phone listed, Scan button). Queue pill on Radio: QUEUE · 31 tab, **no ⋮**, radio stayed `Playing`. Stats chip: **off by default, on survived a reload**, turned back off; stats render. **Minor findings:** with no phone connected the Bluetooth source reports `Playing` and its position counter runs (0:04) — possibly `AUD-29`'s extrapolation ticking with no device, worth a look during B1; History stats' **top track is "KFM/EBK (feat. Ca…"**, which looks like a fingerprinting misidentification that has accumulated plays.

### B. With your phone on Bluetooth (15 min)
- [x] ✅ **Owner 2026-09-30: *"BT position bar updates as expected."*** **Position bar (`AUD-29`, #724):** play a track — the console's bar moves with the phone; pause on the phone and it stops; seek on the phone and it jumps to match. *(Also check: does the bar still tick with the phone **dis**connected? See the centre-panel finding in section A.)*
- [x] ✅ **Owner 2026-09-30: *"Re-attach worked fine."* → #726 merged (`b64c8cd`) and deployed.** **Re-attach (`AUD-14`, #726):** pause on the phone for 30 s+ and resume; then disconnect and reconnect the phone while playing. Pass = the console follows play/pause/track after each.
- [x] ✅ **Owner 2026-09-30: *"AUD-74 passes."* → archived.** **Announcements (`AUD-74`, #723)** — ✅ **ducking agent-verified; by-ear still yours.** Two announcements 1.5 s apart, `duckingState` polled every ~150 ms: ducked to 20 % at 2.1 s and **held continuously** through both (active events 1 → 2 → 1), then ramped 28 → 54 → 79 → 100 % only after the last ended. Both returned `completed`. **Yours, by ear:** no swell, and whether *both* were heard in full — the duck began releasing ~0.45 s before announcement one's request returned, which is relevant to `AUD-73`.

### C. Cast volume (`AUD-80` + `AUD-5`, #725) — 5 min — not run by the agent (see the morning order)
- [x] ✅ **Owner 2026-09-30: *"Cast volume works now."*** Switch output to the Office speaker. Set the **speaker's** volume to about 25 % **on the speaker or Google Home** (the console cannot change it — see `AUD-81`). Disconnect Cast, reconnect: it comes back at 25 %, not loud. Repeat once after a `radio-api` restart. (The very first reconnect after the deploy keeps whatever the speaker is at, because nothing is remembered yet.)

### D. Decisions only you can make
- [x] ✅ **Ruled 2026-09-30: *"AUD-17 recommendation is fine."* — A chosen; now a small removal row, sequenced with Phase 2i.** **`AUD-17` — Bluetooth album art from the phone:** measured, your phone offers no cover art to BlueZ. **A (recommended):** close the dead code path; art keeps coming from song recognition. **B:** experiment with BlueZ's experimental cover art (starts with the boundary doc). *(2026-09-30: owner reports "Bluetooth album art passes" — via song recognition; the A/B choice is still open.)*
- [x] **`AUD-81` — should the console's volume drive a connected Cast speaker?** ✅ **Ruled 2026-09-30: *"I'd like the console volume to be able to change the cast volume."*** Row is now buildable; it belongs to the Cast arc (2c).
- [x] **Optional box cleanup (`AUD-80`):** ✅ **Done 2026-09-30.** Backed up first (`.backup` to `/opt/radio-console/data/config/configuration.db.bak-20260930-aud80`, `integrity_check` ok, 134 rows). `Key` is `TEXT PRIMARY KEY` with the default case-sensitive collation — checked before deleting, so the live `AudioPreferences:*` keys could not match. Deleted exactly three rows, all dated 2026-03-10: `masterVolume` (75), `currentOutput` (empty), `currentSource` (Radio); 131 rows remain. `audiopreferences:hiddenSources` kept, as #725 advised.

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
- [x] ✅ **Ruled 2026-09-30: queue them — filed as queue rows.** **Queue the UI-10 follow-ups?** A `Levels` subscription leak in `GainControlPopover`, and the API's "skip FFT when nobody's watching" gate being effectively always on. CPU saving, not correctness.
- [x] ✅ **Accepted 2026-09-30 — owner: *"The current judgement calls for #6 are fine."*** **Judgement calls to overrule or accept** (each listed in `OWNER-REVIEW.md`): LOG-2 carve-outs (Audio.Services / Audio.SoundFlow kept at Information; Cast at Warning); ENC-22's wake-lights-but-stays-on-sleep-screen, 2 s grace window, knob-restarts-countdown; ENC-19's separate pre-push check.
- [ ] **Still yours, still deferred:** ~~casting UAT + `AUD-37`/`AUD-38`/`AUD-54`/`AUD-5`~~; `OPS-3` (`BindsTo=`), which you review personally.
  - ✅ **Casting baselined 2026-09-30** (see § Casting baseline above): `AUD-84` filed (a Cast drop crashes `radio-api`, P0) — ✅ **fixed and owner-verified the same evening (#736, `646be99`), archived; `AUD-85` found on the re-pick**; `AUD-37` measured (crash, not silence); `AUD-38` superseded by `AUD-80` + `AUD-81`; `AUD-54` given measured evidence. ⚠ **`AUD-5` in this line is stale** — it passed with `AUD-80` (*"Cast volume works now."*) and was archived 2026-09-30.
  - ⏳ **`OPS-3` still deferred** — you will review it at a desk.

## 7. What resumes after this

Order from `HANDOFF-GA-CLOSEOUT.md`: **2f** queue rows with visible symptoms (`AUD-15` BT buffer runs empty, `AUD-13` USB port fallback, `AUD-27` art vanishes on pause, `AUD-28` seek-on-release, `AUD-29` BT position bar, `AUD-17`, `AUD-18`, `AUD-14`, `AUD-25`, `AUD-34`, `UI-15`, `AUD-74`) and `UI-16` (voicemail scrubber drag) → **2h** Metrics under Settings (`UI-2`/`UI-4`) → **2i** confirm-or-close + **`AUD-16` RaddyRF320BT removal** → **2j** hygiene → **2k** cross-repo filings → **2c** casting, when you schedule it. Tell me "resume" and I'll dispatch 2f the same way as today.

## Today, for the record

27 PRs (#677–#709): deploy from Linux + deploy safety (`OPS-12`/`OPS-13`), CI green again (`TEST-11`), Phase 0 record fixes, Phase 1 cabinet sitting (RDS head/pin/loop, `AUD-40`, the PBAP reconnect bug), 2a (`AUD-41`, `AUD-39`), 2b (`AUD-26`, `TTS-5`, `TTS-2`, `TEST-8`; `TTS-6` stale), 2d (`LOG-5/2/6/7/8/12`, `UI-10`), 2g (`ENC-22` off-by-default, `ENC-19`). Filed: `UI-16`, `ENC-22`, `AUD-73`, `AUD-74`, `AUD-75`, `OPS-13`, `TEST-11`.
