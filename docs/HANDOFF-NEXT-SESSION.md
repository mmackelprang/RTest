# HANDOFF — Start here

**Status:** `[CURRENT — 2026-09-03]` · Rewritten against the tree at `5e571b88`. **The previous
revision was ~30 merges stale in its first three sections** — it named `#511` as the latest merge,
the box at `739a859`, a P0 count of "21 listed, 18 effective", and pointed "Start here" at `ENC-5` /
`ENC-7`, both of which had shipped. Its later sections were correct and are kept.

---

## The situation, in one paragraph

Grandpa Anderson's console radio: a .NET 10 audio command center (Blazor Server UI, SoundFlow engine,
BT/Cast/SDR/vinyl/phone) on an Intel N100 Ubuntu box in kiosk Chrome at 1920x720. **The cabinet is
nearly built and this is going into it** — recoverable only by SSH once the back is closed. The full
prioritised punch list is [`docs/HANDOFF-GA-PUNCH-LIST.md`](HANDOFF-GA-PUNCH-LIST.md); the encoder
design is [`HANDOFF-rotary-encoder-mapping.md`](design-handoffs/HANDOFF-rotary-encoder-mapping.md)
(**Rev 7** as of 2026-09-03 — ⚠ that document's own `Status:` line still says `REV 5`; the revision list at
its `:12` is the accurate one). **Read the punch list section 2 ordering constraints before claiming anything.**

**All owner decisions are closed.** D23, D24, D9, D25 and D27 are answered.

---

## State right now

| | |
|---|---|
| **Latest merge** | **`TTS-9` as [#548](https://github.com/mmackelprang/RTest/pull/548)** (`5e571b88`) — eSpeak removed entirely. |
| **Working tree** | `main` at `5e571b88` |
| **Box** | deployed and **SHA-verified at `5e571b88` on both services** (`/api/health/version` on `:5000` and `:5002`, checked 2026-09-03), encoder connected |
| **P0 count** | **21 listed, 2 open** — `PHN-1` and `PHN-2`, both in the phone arc |

### The headline: the encoder arc is complete

⭐ **All 12 encoder P0s have shipped** — `ENC-0` `ENC-1` `ENC-2` `ENC-3` `ENC-4` `ENC-5` `ENC-6`
`ENC-7` `ENC-8` `ENC-11` `ENC-12` `ENC-15`. The knobs are live, the router matches the escutcheon
(`0 = VOLUME · 1 = SOURCE · 2 = PRESETS · 3 = TUNING`), the HUD renders on the panel's real axis, and
`ENC-11`'s tiered fault model finally has a voice through `ENC-12`. This was priced at 3–4 working
weeks.

⚠ **Two things temper that, and both are recorded rather than glossed.**

1. **`ENC-15`'s gate FAILED, so panel blanking is withdrawn permanently — do not reinstate it.** Touch
   cannot wake a blanked panel *by construction*: the touchscreen is powered by the panel and leaves
   the USB bus when it blanks. The encoders are not compositor input devices either (`cafe:4005` has
   zero evdev nodes), so a knob wake only works if `radio-api` reads hidraw and itself calls the D-Bus
   unblank. `ENC-6` therefore shipped three sleep states, not five.
2. **Roughly half of every encoder row's UAT could not be run.** There is no software path to inject
   encoder input, so the behaviour a guest actually touches is the part least verified. Every row
   stated that as an uncovered gap rather than a pass. **`ENC-17` is filed to close it** via
   `/dev/uhid`, which is present on the appliance.

### Shipped since the previous revision of this file

`ENC-4`/`ENC-4a`/`ENC-4c` · `ENC-5` · `ENC-6` · `ENC-7` · `ENC-8`/`ENC-8a`/`ENC-8b` · `ENC-9` ·
`ENC-11`/`ENC-11a` · `ENC-12` · `ENC-15` (gate failed) · `ENC-16` · `OPS-5` · `PHN-1a` · `PHN-1b` ·
`SEC-1` · `SEC-2` · `TEST-4` · `TTS-3` · `TTS-9` · `XR-1a`, plus the 2026-09-01/02 batch
(`TEST-1` `TEST-3` `OPS-1` `LOG-1` `LOG-11` `AUD-6` `AUD-7` `ENC-0`/`ENC-0a` `ENC-1` `ENC-2` `ENC-3`
`UI-1` `UI-5` `TTS-1(ii)`, all 11 quick wins) and a firmware fix in the separate **RotaryUsb** repo
(its PR #11).

**Three P0s closed without writing code** — `AUD-8` and `AUD-9` had already shipped on 2026-05-22 and
the punch list never recorded it; `SEC-1` closed by verification.

**`TTS-9` closed three rows by deletion rather than repair.** Removing eSpeak entirely closed `SEC-4`
(unauthenticated argument injection into `espeak-ng`), `TTS-7` (whose *completion* was what made
`SEC-4` reachable) and `TTS-3`. The owner chose removal over sanitising, because it was comparable
effort and closed three rows instead of guarding one.

---

## Start here: `PHN-1c`, the third of the seven-PR phone arc

**The claimable row is `PHN-1c`** — `EventPlaybackService` and the `/api/audio/events` route family.
Both its dependencies are merged (`PHN-1a` [#528](https://github.com/mmackelprang/RTest/pull/528),
`PHN-1b` [#534](https://github.com/mmackelprang/RTest/pull/534)), it has a full plan at
[`design/plans/PHN-1c-event-playback-service-and-route.md`](../design/plans/PHN-1c-event-playback-service-and-route.md),
and it is **the first PR of the arc a user can reach**.

⚠ **The whole arc is `O6`-ordered and `PHN-2` sits behind it.** `PHN-2` is what the cabinet does
*wrong* today: `VoicemailPlayer.razor` is an HTML5 `<audio>` element pointed straight at the bridge,
so a voicemail bypasses mute, master volume, balance, ducking and Cast routing — press play while the
radio is on and two sounds run in the room at full level each. That is live behaviour, not a latent
risk (D17).

⚠ **Read the plan's §0.4 C-21…C-33 first.** It is authority over both ADR-029 and the `PHN-1a`/`PHN-1b`
plans wherever they disagree, and it disagrees in three places.

⚠ **`PHN-2` also carries the arc's entire verification debt** — four device-only checks deferred to
"PR 6", which has no plan file of its own. They are pinned to the `PHN-2` punch-list row so a
re-sequencing cannot lose them.

### If you would rather take something small and self-contained

`ENC-17` (input injection via `/dev/uhid`, 1 d) is the highest-leverage non-phone row on the board —
it converts *"a human must turn each knob"* into *"a test can"*, which is the substrate the rest of
the encoder arc's unrun UAT rests on. `TEST-7` (a `TimeProvider` seam for `NowPlayingPanel`'s two
hardcoded debounce timers) is queued and claimable, as is **`PHN-5`** — ⚠ **not shipped, despite what an
earlier draft of this file said**: `PhoneContactLookupService.cs:62` still logs a raw phone number and the
contact's full name on the same line, and the row is still `📋` in the queue.

---

## Two documentation defects closed on 2026-09-03, worth knowing about

1. **14 deferred items existed only in a commit message, a PR body or a plan, with no row anywhere.**
   They are now filed — see the punch list's `ENC-18`, `ENC-19`, `TTS-10`, `TEST-5`, `TEST-6`,
   `TEST-7`, `XR-6`, `OPS-6`, and `design/FUTURE-WORK.md` § *TTS seam*.
2. **"0 warnings expected" has never been true, and the number depends on the command you run.**
   `dotnet build RadioConsole.sln -c Release --no-incremental` produces **53** `IDE0011` warnings across
   **15** files, which **confirms** the figure in `WORK-LOG.md:51` and the two 2026-05-22 plans.
   ⚠ **Without `--no-incremental` the same commit reports 30 across 13** — MSBuild skips up-to-date
   projects and does not re-emit their analyzer warnings. Quote the number with the command, or you will
   "correct" the record wrongly, which is what happened while `TEST-6` was being filed. ⚠ **`CLAUDE.md`'s
   "warnings as errors in Release builds" is NOT contradicted**: `Directory.Build.props:6-7` exempts
   `IDE0011` and `IDE0161` explicitly and deliberately, and every other class still fails the build.
   **The honest local test gate is "adds no new failures", not "zero failures"** — see `TEST-5` for the
   four `SrcVariableResamplerTests` that fail on every Windows dev machine because the native library is
   `libsamplerate.so.0`.

---

## ⭐ You can now drive the encoders from a script (`ENC-17`)

**`tools/encoder-harness/virtual_encoder.py`.** Six encoder rows shipped on 2026-09-02/03 and not one
could fully verify the behaviour a guest actually touches, because there was no way to synthesise a
turn or a press. There is now.

```bash
scp tools/encoder-harness/virtual_encoder.py mmack@radio:/tmp/
ssh mmack@radio "sudo python3 /tmp/virtual_encoder.py -c 'turn 0 3' -c 'hold 0 900'"
```

It creates a **real USB HID device** with the RotaryUsb identity and descriptor, so the shipped
`HidRotaryEncoderService` reads it exactly as it reads the physical knobs. Commands: `turn`,
`offline-turn`, `press`, `release`, `tap`, `hold`, `idle`, `detach`, `attach`. Encoders are
`0 = VOLUME, 1 = SOURCE, 2 = PRESETS, 3 = TUNING`. **Read
[`tools/encoder-harness/README.md`](../tools/encoder-harness/README.md) before using it** — it
carries the recovery procedure and the two design decisions.

Three things to know before you reach for it:

- **It unbinds the real encoder** for the duration and rebinds it on exit. A `SIGKILL` is the one
  case that leaks — verified: the gadget and the vhci attachment survive it, leaving a virtual
  `cafe:4005` enumerated (but **inert**, since it only ever sends what you type) and the physical
  knobs dead. `sudo python3 /tmp/virtual_encoder.py --cleanup` puts everything back; so does simply
  starting the harness again, or a reboot.
- **`/dev/uhid` does not work for this**, even though it is present on the box and the `ENC-17` row
  named it as the route. HidSharp does not enumerate a device with no USB parent — measured, with the
  uhid device present and readable, `GetHidDevices(0xCAFE, 0x4005)` returned 0. The harness uses a
  usbip loopback USB gadget instead. Do not spend the afternoon re-testing uhid.
- **It does not replace the owner's hand on the panel.** Feel and acceleration are still his, and it
  does not exercise the firmware at all.

**What it has already verified on the appliance** (2026-09-03, against `5e571b8`): configuration
verifies `Configured` on attempt 1; a turn moves volume at 2% per unit; the `ENC-3` per-event clamp
holds at ±6 units against single events of 20 and 50 detents (⚠ **both figures are pre-`ENC-20` and
are dated history, not current** — the live values are **1% per unit** and **±4 units = ±4 points**;
`ENC-20` re-ran this measurement and recorded 1 point per detent at base speed); the `ENC-4` HUD renders left-anchored
at its band; a 900 ms hold on encoder 0 synthesises a long press with the progress ring and a 200 ms
hold does not; and **`ENC-1`'s re-baseline rule holds across a real USB disconnect — 50 detents
accrued while unplugged produced a 0-point jump on replug.** That last one was Designer's
highest-weighted encoder test and had never been run against a real disconnect.

**`ENC-17` delivered the instrument only.** Re-verifying the rest of the encoder arc with it —
`ENC-5`'s states A–E and the wrap animation, `ENC-6`'s scenarios B/C/D/E/I, `ENC-7`'s C4 recall from
Bluetooth — is the next row's work and is now cheap.

---

## Gotchas that will otherwise cost an hour each

1. **journalctl only carries WARNING and above now.** LOG-11 level-restricted the API console sink,
   and under systemd the console *is* the journal. Information lines live in the file sink, so a
   startup sequence you expect in `journalctl -u radio-api` will look like it never happened. Read
   the newest file under `/opt/radio-console/logs/radio-*.txt` instead.

2. **The deploy needs no flags any more.** OPS-1 fixed the defaults — `Deploy-ToLinux.ps1` with no
   arguments targets `radio` / `linux-x64` correctly, and `Deploy-ToPi.ps1` names `piradio`
   explicitly. Any doc telling you to pass `-Runtime linux-x64` is stale.

3. **Both services are SHA-verified on deploy.** `radio-web` gained `/api/health/version` and the
   deploy exits non-zero on either mismatch. If it says verified, it is.

4. **The encoder hidraw node moves.** It was hidraw4, is hidraw3 after a re-flash. Never hard-code
   it — match on VID/PID `cafe:4005`, which is what the service does.

5. **The encoder needs a udev rule to be openable at all.**
   `deploy/common/99-rotaryusb-encoder.rules`. Without it every open fails with
   `DeviceUnauthorizedAccessException` and the knobs silently never work.

6. **The firmware fix must survive any re-flash.** RotaryUsb PR #11 normalises report IDs arriving on
   the interrupt OUT endpoint. Flashing an older build silently reinstates the defect: every
   host-to-device write is accepted and ignored, so config pushes do nothing and nothing complains.
   Verify by sending command `0x04` — a working device answers with a 107-byte report `0x02`.

7. **Shell heredocs with complex content keep failing in this environment.** Writing C# or Markdown
   through a nested Python string inside a bash heredoc broke repeatedly, including one case that
   wrote a literal backspace byte into a regex and another that silently no-opped a doc edit.
   Embedded shell one-liners containing quotes break it too. Use a quoted heredoc as the only command
   in the call, keep quoted shell snippets out of the content, and assert on every string replacement
   rather than trusting it.

8. **`VolumeChanged` is the wrong channel for anything that must be on screen fast.** Its one call
   site is a 500 ms change-detecting poller, so it is 2 Hz, and it carries the volume rather than
   which knob moved. ENC-4's plan adds a separate push channel for HUD updates. The "do not add a
   second throttle" finding below is about `VolumeChanged` and still stands.

9. **`/sleep` reached by idle and `/sleep` reached by the Sleep pill are different states.**
   `idle-dimmer.js`'s **`navigateToSleep`** navigates by `window.location.href` without calling
   `SetSleepAsync(true)`, so `SleepService.IsSleeping` is **false** on the idle path and knobs act
   normally there; the pill sets it **true** and any encoder input is consumed by the wake. Testing
   sleep-screen encoder behaviour via the pill will look like a failure when it is the documented
   pre-ENC-6 behaviour.
   *(⚠ Cited by function name since 2026-09-04. This used to say `idle-dimmer.js:73-81`; the function
   has moved and the line range no longer contains it. The substance was right — and it turned out to
   be load-bearing.)*

   ⭐ **This gotcha was already true, already written down, and a stop condition was still built on the
   opposite assumption.** ADR-029 §7.5 hung *"entering `/sleep` stops attended playback"* on
   `SleepService.EnterSleepAsync`, which the idle path never reaches — so the rule never fired for the
   30-minute idle timer, which is the case §7.5's own motivating sentence names. Fixed in `PHN-1e` by
   adding a second edge on `SetSleepScreenVisibleAsync(true)`; ADR-029 §16.4/§16.5 is the record.
   **If you are about to reason about what happens when the console goes to `/sleep`, this entry is
   the one to read first, and `IsSleeping` is the wrong predicate.**

---

## Live findings worth not re-deriving

- **The encoders factory config was measured, not inferred.** Before ENC-11 all four sat on `step=1`
  with tiers `(150ms x5), (80ms x15), (40ms x50)`. At the host 2% per unit that is 50 x 2% = 100
  volume points in one detent — the "one detent from silence to full" the handoff has warned about
  since Rev 2, read off the device. **Since ENC-20 (2026-09-03) it is `step=1` with
  `[150ms x2, 80ms x4, disabled]`, `VolumeStepPercent = 1` and a host clamp of `VolumeClamp = 4`,
  worst case 4 points per detent** and 1 point per detent at an ordinary slow turn.
  ⚠ **Two things in the sentence this replaced were wrong, and both are worth carrying forward.**
  ENC-11 shipped `step=2`, which ENC-20 has since reverted to `1`. And *"worst case 6 points"* was
  units read as points: at `step=2` with the ×3 tier the device emitted 6 **units**, which the host
  turned into **12 points** at 2% per unit. Device units and volume points are different quantities —
  `step_size × multiplier` gives units, `× VolumeStepPercent` gives points — and conflating them is
  what put a safety floor of 1.33 s in the handoff that the shipped code never met (the real figure
  was 0.67 s at 80 ms/detent). ENC-20 sets both `step_size` and `VolumeStepPercent` to `1` so that on
  VOLUME one unit *is* one point and the two cannot drift apart again. Full account: handoff Rev 8
  and §5.4.
  ⚠ Older verification notes in this file and in `design/INTEGRATIONS.md` that quote **±6 units** are
  dated measurements against the pre-ENC-20 build and are correct as history; the live clamp is ±4.

- **max_value is marked *inert* in the handoff and the device validates it anyway.** A 0 on TUNING
  made the firmware reject the **entire** config, because `validate_config` is all-or-nothing — so
  the volume tiers were discarded too. Fixed in ENC-11a, with tests that encode the *device* validation
  rules rather than the host expectations.

- **ENC-3 broadcast throttle is already satisfied and its justification is wrong.** The row claims a
  fast spin drives ~100 SignalR fan-outs per second. The VolumeChanged send has exactly one call
  site, inside a 500 ms change-detecting poller: 2 Hz, trailing-edge, final-value. Do not add a
  second throttle.

- **Encoder 1 odd raw-pin reading was not a fault.** It read 5 where idle is 7; after the knobs were
  turned the 5 moved to encoder 3. It is whichever knob is parked between detents. invalid_count is
  1-2 against 270-459 edges — ordinary contact bounce.

- **Project memory `project_autoswitch_bt_bug.md` asserts a defect that does not exist** — an
  unbounded retry loop. Both loops are bounded (12x10 s outer, 20x1 s inner, ~140 s worst case) and
  were before the AUD-9 gate landed. Retire it rather than cite it.

- **ENC-3 volume ramp was deliberately deferred.** It changes gain application in the audio callback
  path — where the long-running capture bug and the distortion reports live — and its acceptance
  criterion is whether it *sounds* right on a fast spin. It wants someone in the room.

---

## Needs the owner

1. ~~**SEC-2** (P2) — the Azure TTS key slot appears to hold a **Google** key.~~ ✅ **CLOSED by
   PR #523, and the diagnosis was wrong.** The two slots returned byte-identical masks because the
   Secrets form was **saving the mask over the real secret** — a P0 in the write path, not a
   mis-paste, and not an owner-side ten-minute fix. It destroyed the live Google TTS key on
   2026-09-02. Guarded server-side now; the UI no longer puts a mask in an editable field.

2. **ENC-15** (P0) — the touch-wake gate needs a human touching a blanked panel. It is a hard
   predecessor of the ENC-6 blanking half, and the punch list is blunt that it "will look skippable
   right up until it isn't": if touch cannot wake a blanked screen and the encoder USB drops, the
   panel is unwakeable inside sealed furniture.

3. **O9 is the one irreversible step** — the knob order VOLUME / SOURCE / PRESETS / TUNING before the
   escutcheon is drilled.

---

## The remap is complete — three things it left behind

`ENC-7` closed the last index. `RotaryEncoderActionRouter` dispatches
**`0 = Volume · 1 = SOURCE · 2 = PRESETS · 3 = Tuning`**, matching the cabinet's
**VOLUME / SOURCE / PRESETS / TUNING** on every knob and matching what `ENC-11` pushes to the device.

1. **Tuning acceleration went live for the first time in `ENC-5`.** `RotaryEncoderConfigDefaults`
   always pushed the tuning tiers `(150 ×2 / 80 ×4 / 40 ×8)` to encoder 3; before the remap they
   landed on the wrong handler. `TuningClamp = 8` stopped being theoretical, and a hard spin can now
   issue up to 8 sequential tuner calls per detent.
2. **Handlers no longer hard-code their HUD index.** `ENC-4` wrote `PublishHud(1, "TUNING", …)` and
   friends as literals matching the *old* table; after a remap those put each card beside the wrong
   knob. `_turnHandlers` / `_pressHandlers` are now `Action<int,int>` / `Action<int>` and the router
   passes the index the event arrived on. **A future remap must not reintroduce a literal.**
3. **The Settings page's "does not match the cabinet" warning is computed per knob, by keyword.**
   `SystemConfigPage.DescribesItsCabinetRole` matches each row's *turn description* against its
   engraved name — there is no handler identity on the wire. It names nothing today. ⚠ **Rewording
   `_mapping` so an entry loses its engraving's keyword relights the banner on a knob that is
   correct**, which is why `RotaryEncoderRouterMappingTests` pins the index-2 wording.

---

## The PHN arc (D25 = full ADR-029 arc, no stopgap)

Sequenced in [`design/plans/PHN-arc-pr-breakdown.md`](../design/plans/PHN-arc-pr-breakdown.md) —
seven PRs. PR 1 (core contracts) is nearly mechanical; **PR 4 is the sharp one** and should be
reviewed hardest, because it is the first load-bearing use of a subsystem the ADR proves is currently
decorative: `GetActiveEventsByPriority` and `StopAllDuckingAsync` have **zero non-test callers**, and
the INTEGRATIONS.md claim that higher-priority announcements interrupt lower ones is false today.

Fully independent of the encoder arc — different files, no shared ordering constraint.

---

## Verify claude-mem reconnected

This session was restarted because claude-mem recorded **zero user_prompts rows** for it. Every
observation landed with a null prompt_number, so 347 stored observations had no turn to attach to.
The session-init raced the worker boot at start and never recovered (upstream #2794 / #2795). Capture
itself worked — roughly a third of observations were additionally lost to parser rejections.

Worth confirming early in the new session: query the `user_prompts` table in
`C:/Users/mark/.claude-mem/claude-mem.db` for rows created today matching the new session id. If it
stays at zero while other sessions record prompts, the race happened again.
