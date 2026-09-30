# HANDOFF — GA close-out: the sequenced list to a cabinet-ready radio

**Written 2026-09-28** on the Linux dev box (`appserver`), against `main` at `dda36f27` and the
appliance at `f409bb9` (both services, SHA-verified by IP). Supersedes the "Start here" section of
[`HANDOFF-NEXT-SESSION.md`](HANDOFF-NEXT-SESSION.md) for sequencing; the punch list
([`HANDOFF-GA-PUNCH-LIST.md`](HANDOFF-GA-PUNCH-LIST.md)) remains the authority on *what each row
is*, and [`BUILDER_QUEUE.md`](BUILDER_QUEUE.md) on *what is claimable*. This file is the order.

## 0. Where we actually are

| | |
|---|---|
| **P0 — blocks installation** | **Effectively closed.** Every `PHN-1` PR (`1a`–`1f`, [#528](https://github.com/mmackelprang/RTest/pull/528) … [#566](https://github.com/mmackelprang/RTest/pull/566)) and `PHN-2` ([#566](https://github.com/mmackelprang/RTest/pull/566)) are archived as shipped. ⚠ The punch list's banner and §9 still say *"2 open, PRs 5, 5b, 6 and 7 remain"* — that is the record lagging, and [PR #665](https://github.com/mmackelprang/RTest/pull/665) (open, conflicted) is the fix. |
| **P1 — blocks calling it finished** | **~30 open** once stale rows are struck (see §1). Of the 8 filed 2026-09-27, 3 shipped in [#674](https://github.com/mmackelprang/RTest/pull/674) and are now on the box. |
| **Queue-only rows not on the punch list** | 22 open in `BUILDER_QUEUE.md`, several 🟠 and one 🔴 (`OPS-12`). They are cabinet-relevant and §3 folds them in. |
| **Verification debt** | Four things are *merged and deployed but never watched*: the RDS fixes ([`HANDOFF-RDS-DEV-BOX.md`](HANDOFF-RDS-DEV-BOX.md) §3), the seven BT/Cast/SDR box checks ([`HANDOFF-BT-CAST-SDR-REVIEW.md`](HANDOFF-BT-CAST-SDR-REVIEW.md) *Confirm on the box*), `PHN-2` §3 U5 (voicemail + TAP, scheduled as U2, no result recorded — per #665), and roughly half of every encoder row's UAT (`ENC-17`'s harness exists to close it). |
| **CI** | Red on every `main` run since 2026-09-26 (`TEST-11`). The merge gate's second opinion is currently silent. |
| **Deploys** | Now run from this Linux box ([#677](https://github.com/mmackelprang/RTest/pull/677)); the first one exposed `OPS-13`. `OPS-12`'s ordering defect is still live. |

**Definition of done for this document:** every P1 row on the punch list is ✅ or has an owner ruling
moving it to P2; every 🟠-or-worse queue row is ✅ or ruled; the four verification debts have a
recorded result; and the §5 install checklist is green on the box.

## 1. Phase 0 — make the record true (½ day, docs + one test fix)

Nothing below can be sequenced honestly against a punch list that says P0 is open and a queue
that shows shipped rows as 🚧. Do this first, in one PR.

1. **Land [#665](https://github.com/mmackelprang/RTest/pull/665).** It archives `AUD-24`, `AUD-30`,
   `AUD-31`, records the `AUD-28` seek-on-release ruling, refreshes the P0 banner, corrects `XR-5`/`XR-6`.
   It conflicts with `main` in `BUILDER_QUEUE.md` only (its row removals vs. the `OPS-13`/`TEST-11`
   rows and the `OPS-12` annotation added 2026-09-28); the resolution is mechanical — keep both.
2. **Strike the rows that shipped without being struck.** Verified against the tree, not the docs:
   - `LOG-11` (CLAUDE.md documents it live since 2026-09-02);
   - `UI-3` — no `/logs` route in `MainLayout.razor`; the tab is gone;
   - `TTS-10` — `EventPlaybackService.cs:736` reads `GenerationTimeoutSeconds`;
   - `TTS-11` — archived in `BUILDER_QUEUE_ARCHIVE.md`;
   - `UX-1` — archived; two UAT sittings on file;
   - `TTS-4` — `EventPlaybackService.cs:899` now calls `GetActiveEventsByPriority`, so "zero
     production callers" is false since `PHN-1d`. ⚠ Whether the *Notifications page* priority is
     honoured end to end is the residual; confirm at the cabinet in Phase 1 before striking.
3. **Queue statuses.** `AUD-32`, `AUD-33`, `AUD-35` are 🚧 with their fixes merged
   ([#669](https://github.com/mmackelprang/RTest/pull/669), [#670](https://github.com/mmackelprang/RTest/pull/670),
   [#671](https://github.com/mmackelprang/RTest/pull/671)) and deployed; record the UAT result or mark
   them "merged, UAT pending" explicitly. `AUD-30`/`AUD-31` are archived by #665.
4. **Re-tally §9** from the struck rows; the P1 cell has been wrong since 2026-09-27's +8.
5. **`TEST-11` part 1** (test-only guard fix). It is the one code change in this phase because it
   turns CI green again, and every later phase leans on CI as the second opinion.
6. **CLAUDE.md**: record the Linux Release baseline (**33 warnings / 0 errors**, all `IDE0011`, measured
   twice on 2026-09-28) beside the Windows 47, and re-point *"Deploy to Pi (from Windows)"* wording —
   done in #677 for the command block, not yet for the gate paragraph.
7. **Point `HANDOFF-NEXT-SESSION.md` here.** Its "Start here" is `PHN-1c`, which shipped three weeks ago.

## 2. Phase 1 — watch what is already on the box (one afternoon at the cabinet, no code)

The appliance runs `f409bb9`, which contains everything merged. Five of the review rows say *"code
review, not measured"* in their own text, and their tier depends on the answer. Do these before
building anything, and record results in `docs/uat/2026-09-xx-…/REPORT.md` as the previous sittings did.

| # | Check | Source | Decides |
|---|---|---|---|
| 1.1 | **RDS acceptance**: `_debugState` poll over CDP `:9223`, offset never drops to 0 on RT update or PS flip; no fragment `RDS: Station name` lines in the file sink (⚠ raise `RTLSDRCore` to Debug first — since `LOG-12` later pages are Debug; see `HANDOFF-RDS-DEV-BOX.md` §3 step 4) | `HANDOFF-RDS-DEV-BOX.md` §3 | closes `AUD-63`/`69`/`70` at the panel; and the **two product questions** in its §4 (head = rolling PS vs PI call sign; `PsConfirmThreshold` 2 vs 1) |
| 1.2 | BT playing → press Pause on the panel → does audio stop? | `AUD-40` | P1 confirmed → one-line fix in 2a; or close |
| 1.3 | Stop Scan → disconnect/reconnect phone → does AVRCP re-attach? pair a new phone | `AUD-41` | same |
| 1.4 | Cast at 20 %, restart `radio-api` with Cast persisted, read speaker + master; turn the knob | `AUD-38` | whether the 70 % push and the knob dead-end are real |
| 1.5 | While casting, power-cycle the Chromecast, wait 60 s: cabinet silent with local muted? journal shows the DirectChannel warning every ~5 s? | `AUD-37` | P1 if silent |
| 1.6 | `wpctl get-volume <bluez_input>` after phone-side resume, then after a phone volume press | `AUD-47` (P2) | which volume policy the box has |
| 1.7 | Correlate AVRCP `Track` events with `Buffer underrun` lines on a BT session | `AUD-39` | whether the per-event flush is visible |
| 1.8 | **`PHN-2` §3 U5**: play a voicemail over the radio with TAP; mute, master, balance, ducking all apply | #665 body; `PHN-2` dossier | the one phone-arc check never run |
| 1.9 | **Encoder re-verification with the harness**: `ENC-5` states A–E + wrap, `ENC-6` B/C/D/E/I, `ENC-7` C4 recall from Bluetooth | `HANDOFF-NEXT-SESSION.md` § ENC-17 | converts "UAT could not be run" into results; owner's hand still needed for feel |
| 1.10 | Notifications page priority 1 vs 10 with two overlapping announcements | `TTS-4` residual | strike or keep `TTS-4` |

⚠ Phone-surface checks must record wall-clock time against the 20-minute `XR-3` blackout cycle
(punch list §10), or results look random.

## 3. Phase 2 — the build order

Governed by punch list §2 (`O4`, `O6`, `O7` still bind) and the review handoffs' own orders. Within
that: **deploy safety first (2e — the owner may install mid-arc and keep deploying), then
audible/visible-at-the-cabinet, diagnostics second, hygiene last.** Effort figures are the rows' own.
The subsection letters are labels, not the order; the order is: **2e → 2a → 2b → 2d → 2g (`ENC-22` first) → 2f → 2h → 2i → 2j → 2k → 2c.** *(Updated 2026-09-28: casting (2c) deferred to last by the owner; `ENC-22` added and pulled forward as GA scope.)*

### 2a. Sub-hour BT fixes, one PR each (≈ ½ day total) — after 1.2/1.3/1.7

`AUD-40` (pause that does not pause) · `AUD-41` (Stop Scan kills the watcher) · `AUD-39` (flush only
on a real track change; do it *before or with* `AUD-15` so its underrun counts stay attributable).
Ride-alongs from P2 when the file is open: `AUD-50`, `AUD-51`, `AUD-53`, `AUD-46` (plain delete).

### 2b. Ducking and announcement correctness (≈ 2 days)

These are the "control that lies" rows and they are all on the path a doorbell or voicemail takes:

1. `AUD-26` 🟠 — source switch during a duck leaves the new source at full volume (queue).
2. `TTS-6` — ducking cleared only for the current source; source A stays attenuated until restart.
3. `TTS-5` — `State = Playing` overwrites a terminal state (the #469 defect shape, again).
4. `TTS-2` — stop returning 200 for a swallowed announcement failure.
5. `TEST-8` — pin `Priority ?? 8` (ride with whichever of the above touches `NotificationsController`).

### 2c. Cast as one arc (≈ 2 days) — after 1.4/1.5

`AUD-37` (dropped Cast never noticed) + `AUD-54` (same client lifetime) + `AUD-5` (superseded
connection persists master volume) share `GoogleCastOutput`'s lock discipline; plan the three
together, then `AUD-38` (70 % push, knob dead-end) after, because reconnect decides where the volume
push lives. `AUD-58`'s transport half belongs here too.

⛔ **Updated 2026-09-30 after the owner's casting baseline (MEASURED, box on `b64c8cd`):** **`AUD-84`
(🔴 P0, GA-blocking) leads this arc** — a Cast speaker dropping mid-stream crashes `radio-api` through
an unhandled exception in SharpCaster's heartbeat timer, restarting the whole console. `AUD-37`'s
"notice and fall back" follows it. `AUD-5` has shipped (#725), and `AUD-38` is superseded by
`AUD-80` (#725) + `AUD-81` (console volume drives the speaker, owner-ruled 2026-09-30). Record:
[`uat/RETURN-CHECKLIST.md`](uat/RETURN-CHECKLIST.md) § Casting baseline.

### 2d. Logging & distortion, in `O4`/`O7` order (≈ 2–3 days)

`LOG-5` (runtime level switch — *highest diagnostic value per hour in the whole document*) →
`LOG-2` (gated behind it) · `LOG-6` (logging off `OnProcess`; **hard prerequisite of P2 `LOG-10`,
never the other way round**) · `LOG-7` · `LOG-8` (rate-limit backstop for the resampler warning) ·
`LOG-4` (journald bounds inside a sealed cabinet).

### 2e. Deploy and service safety (≈ 1–2 days, owner-reviewed)

- `OPS-13` + `OPS-12` **together**: verify from the box over ssh; relaunch the kiosk on every path
  that stopped it; prove the transport before stopping anything; make the rsync/scp choice
  explicit per host. Both are sequencing outages, and both are now on the path every deploy from
  this box takes. ⛔ Validate with `-NoRestart` and a dry run, never by a failing full deploy.
- `OPS-3` (`BindsTo=` — **re-scope required**, the rehearsal disproved the row as specified;
  owner reviews personally) then `OPS-10` (readiness; the analysis exists in `OPS-3`'s plan).

### 2f. Queue rows with a cabinet-visible symptom (≈ 3–4 days)

In severity order as the queue marks them: `AUD-15` (BT ring buffer runs empty, 55 underruns —
after `AUD-39`) · `AUD-13` (`USBPort: ""` matches every device, so Radio/Vinyl/USB bind to whatever
enumerates first — matters the day a cable is reseated inside the cabinet) · `AUD-27` (album art
vanishes on pause) · `AUD-28` (seek-on-release; ruling recorded, plan needed) · `AUD-29` (BT position
bar never moves) · `AUD-17` (AVRCP album art has never worked) · `AUD-18` (fingerprint tap returned
zero bytes for 11½ h and nothing noticed) · `AUD-14` · `AUD-25` · `AUD-34` · `UI-10` (Blazor circuit
times out every ~30 s — check `ServerTimeout ≥ 2 × KeepAlive` first) · `UI-15`.

### 2g. Encoders P1 (≈ 1–2 days)

**`ENC-22` first — owner request 2026-09-28, GA:** power the panel off after a period in sleep, any knob wakes it; feasibility measured on the box (see the row), safety rules included. Then `ENC-19`, cheap: a firmware-version read at startup so a re-flash to an older build (which
silently reinstates the dropped-report defect) is *announced*. Then `ENC-18` (presence state
machine seam + tests). `ENC-14` (diagnostics card) last — survives smaller since D5; candidate for a
P2 ruling, see §4.

### 2h. UI surface

`UI-2`/`UI-4`: `/metrics` is still top-level in `MainLayout.razor:171`; fold a trimmed diagnostics
surface under Settings and kill the 40-query fan-out (D11). `UX-2` (skeleton shimmer purpose) is a
decision, not a build.

### 2i. Confirm-or-close (each ≤ ½ day, may end in no code)

`AUD-21` (BT disconnect reason — does BlueZ even supply one?) · `GV-10` (falsified as ours; owner
closes) · `TEST-2` (P2) · `AUD-36` (dead `IdentificationIntervalSeconds`) · **`AUD-16` as a removal
PR per D-D**: delete the RaddyRF320BT USB radio source, its protocol/config/docs surface and the
`external/RaddyRF320BT` submodule; the RTL-SDR path is the only tuner afterwards. Check first that
nothing in `Radio.Tools.AudioUAT` or the `SourceSelector` list still enumerates it. · **`AUD-17` as a removal PR** (owner ruling 2026-09-30, option A:
*"AUD-17 recommendation is fine."*): delete the never-firing AVRCP cover-art read and
`CacheAvrcpArtAsync`'s unreachable branch, fix the "MPRIS" comment and log strings; BT art keeps
coming from song recognition. Scope in [`queue/AUD-17.md`](queue/AUD-17.md).

### 2j. Test & ops hygiene

`TEST-10` (wall-clock race, `TEST-4` shape) · `TEST-5` (document the Windows-only resampler
failures — or make them skip) · `TEST-6` (baseline + command; now two baselines, 47 Windows / 33
Linux) · `HW-2` (verify the RotaryPhone hub contract against the running service).

### 2k. Cross-repo — file, do not build here

`XR-2` (thread ids with `/`), `XR-3` (PSIDTS blackout, status lies during it), `XR-4` (CDP cookie
log spam on a box where journald churn correlates with distortion) go to RotaryPhone via the
boundary-doc protocol. `XR-5`'s remaining half is **ours**: `BellHealth.Failed` has no producer —
that is queue row `PHN-7`. `XR-6` is delivered on their side (#665).

### Deliberately last or post-GA unless Phase 1 promotes them

`AUD-22` (codec observability), `AUD-23` (PipeWire event subscription, 2–3 d), `TTS-8` (box-only
voice default; fresh-install reproducibility), `PHN-8`, `PHN-9`, `OPS-10`, `AUD-16` (retire USB
radio path — a removal decision), `AUD-19`, `AUD-20` (SDR deadline misses, no cause established —
`AUD-42`'s GC measurement is the cheap next step), and every P2 row in punch list §5.

## 4. Decisions — answered by the owner 2026-09-28

| # | Question | Ruling |
|---|---|---|
| D-A | **Does "finished" require the diagnostics tier?** `AUD-22`, `AUD-23`, `ENC-14`, `ENC-18`, `LOG-4`, `HW-2`, `TEST-5`, `TEST-6` are P1 by the original criteria but none changes what a guest hears or sees. | ✅ **P2 for the cabinet install.** They stay P1 in the punch list with this ruling dated beside them; ~8 rows and ~6 days off the critical path. |
| D-B | RDS head: live rolling PS or PI call sign? `PsConfirmThreshold` 2 or 1? | ✅ **Decide after watching one rolling-PS station in 1.1**; both are one-line changes. The owner flagged RDS as one of the jankiest UX items left, which is why 1.1 is first. |
| D-C | `OPS-12`: delete the rsync branch, or keep it as the Linux transport with an explicit per-host choice? | ✅ **Keep, explicit.** It is the only transport that has run from this box. |
| D-D | `AUD-16`: is the deprecated USB radio path (and the `RaddyRF320BT` submodule) in or out? | ✅ **REMOVE ALL SUPPORT for the RaddyRF320BT device — the RTL-SDR supersedes it.** `AUD-16` becomes a removal row: the USB radio source, its protocol code, config surface, docs, and the `external/RaddyRF320BT` submodule (`.gitmodules`) all go. Sequenced in 2i as a self-contained PR; it is deletion, not GA-blocking, but the owner wants it done rather than carried. |

**Also ruled:** the cabinet install is **not the end of the arc**. Deploying to the installed radio
is quick, so the owner may install before this list is done and keep developing. Two consequences,
both applied above: **2e (deploy safety) runs right after Phase 1**, ahead of the long build phases,
because every post-install deploy goes through the script that darkened the panel on 2026-09-28; and
§5 is an *"any time after 2e"* checklist rather than a finish line.

## 5. Install checklist (any time after 2e — the install does not end the arc)

- [ ] `main` and both `/api/health/version` endpoints agree, read by IP *and* by name from the box.
- [ ] `OPS-13`/`OPS-12` merged, so a deploy can no longer leave the panel dark on a failed check.
- [ ] `ENC-19`'s firmware check reports the RotaryUsb #11 build (`0x04` → 107-byte report `0x02`).
- [ ] `deploy/provision/provision.sh` idempotent re-run is clean (WirePlumber rules now match the repo
      byte-for-byte after 2026-09-28's deploy; expect "unchanged").
- [ ] Recovery lines are in `design/INTEGRATIONS.md` and on a card in the cabinet: kiosk relaunch
      (`/usr/local/bin/radio-kiosk-launch`), the 2 a.m. unblank (`gdbus … ScreenSaver.SetActive false`),
      `radio-kiosk-exit` never widened to `pkill -f chrome`.
- [ ] `/opt/radio-console/data` backed up (config DB, DataProtection keys, fingerprints) before the
      back goes on.
- [ ] Phase 1 report on file with wall-clock times.

## 6. Rough critical path

| Phase | Effort | Gate |
|---|---|---|
| 0 record + `TEST-11` | ½ d | CI green on `main` |
| 1 box afternoon | ½ d + owner | report on file; tiers of `AUD-37`–`41` settled |
| 2e deploy/service safety | 1–2 d | one supervised deploy from this box, panel live at the end — **this is the install gate** |
| 5 checklist | ½ d | back on the cabinet, whenever the owner chooses |
| 2a–2c audible fixes | ~4–5 d | owner UAT per PR, at the cabinet |
| 2d logging | 2–3 d | `LOG-5` toggles a level without a restart |
| 2f queue symptoms | 3–4 d | per-row UAT |
| 2g–2k remainder incl. `AUD-16` removal | 2–4 d, with D-A applied | — |

**About two working weeks with D-A applied, at the pace of the last three.** The number is a shape,
not a promise: `AUD-15` and `AUD-20` are the two rows where the cause is not yet established, and
either can absorb a week.
