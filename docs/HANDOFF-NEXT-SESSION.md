# HANDOFF — Start here

**Status:** `[CURRENT — 2026-09-02]` · Supersedes the 2026-08-19 planning handoff, whose "start with
`TEST-1`" instruction is done.

---

## The situation, in one paragraph

Grandpa Anderson's console radio: a .NET 10 audio command center (Blazor Server UI, SoundFlow engine,
BT/Cast/SDR/vinyl/phone) on an Intel N100 Ubuntu box in kiosk Chrome at 1920x720. **The cabinet is
nearly built and this is going into it** — recoverable only by SSH once the back is closed. The full
prioritised punch list is [`docs/HANDOFF-GA-PUNCH-LIST.md`](HANDOFF-GA-PUNCH-LIST.md); the encoder
design is [`HANDOFF-rotary-encoder-mapping.md`](design-handoffs/HANDOFF-rotary-encoder-mapping.md)
(Rev 3). **Read the punch list section 2 ordering constraints before claiming anything.**

**All owner decisions are closed.** D23, D24, D9 and D25 were answered on 2026-09-01/02.

---

## State right now

| | |
|---|---|
| **Open PR** | none. **`ENC-3` merged as [#511](https://github.com/mmackelprang/RTest/pull/511)** (`d358d5f`). |
| **Working tree** | clean, on `main` |
| **Box** | deployed and healthy at `739a859`, encoder connected |
| **P0 count** | **21 listed, 18 effective** |

### Shipped 2026-09-01/02

`TEST-1` · `TEST-3` · `OPS-1` · `LOG-1` · `LOG-11` · `SEC-1` · `AUD-6` · `AUD-7` ·
`ENC-0` · `ENC-0a` · `ENC-1` · `ENC-2` · `ENC-9a` · `ENC-4a` · `ENC-11` · `ENC-11a` ·
`UI-1` · `UI-5` · `TTS-1(ii)` · `ENC-8a` · `ENC-8b` · `XR-1a`, all 11 quick wins, and a firmware fix
in the separate **RotaryUsb** repo (its PR #11).

**Three P0s closed without writing code** — AUD-8 and AUD-9 had already shipped on 2026-05-22 and the
punch list never recorded it; SEC-1 closed by verification.

---

> **Added by the `ENC-8` cycle (2026-09-02, [#527](https://github.com/mmackelprang/RTest/pull/527)).** `ENC-8` has shipped, so
> **`ENC-12` is now unblocked** and is the other claimable row — both its dependencies (`ENC-8`, `ENC-4`) are done. It was
> sequenced deliberately after `ENC-8`: its toast copy ends *"Open encoder settings"*, and that page now exists. Take
> `ENC-5`/`ENC-7` or `ENC-12`; the queue banner has the detail. **This start-here pointer was left as the previous session
> wrote it rather than repointed**, because two Builders were running concurrently when `ENC-8` merged.
>
> ⚠ **One thing from the `ENC-8` cycle that will cost you directly:** `ENC-5`/`ENC-7` own the router remap, and the router
> no longer has a `switch` to edit. `RotaryEncoderActionRouter` now dispatches through `_mapping` / `_turnHandlers` /
> `_pressHandlers`, and the Settings page renders whatever that array says over an API projection. **The remap is an edit to
> those three arrays and nothing else — do not reintroduce a `switch` beside them**, and do not hand-edit the page's mapping
> table, which no longer exists as HTML. `RotaryEncoderMappingTableTests` pins the order so the change has to be deliberate,
> and the page's "these do not match the cabinet labels yet" note is computed from the two orders, so it disappears by
> itself the day you land the remap.

## Start here: ENC-5 / ENC-7 (and read the ENC-4 note first)

**ENC-4 is shipped** — the EncoderHud renders in the quarter above the knob that moved, on every route. The next
rows are **`ENC-5` (SOURCE overlay) and `ENC-7` (PRESETS)**, which the design handoff says to build together or back
to back: one component, two lists. **They also own the router's index→handler remap**, which ENC-4 deliberately left
alone and pinned with a test.

⚠ **Two things from the ENC-4 cycle that will cost you if you skip them.**

1. **ENC-4's implementation reached `main` without a PR and was never reviewed pre-merge.** The cause was a subagent
   switching the shared working tree off the feature branch mid-cycle; it happened **twice** in one cycle. If you
   dispatch subagents that touch the tree, re-check `git branch --show-current` immediately before every commit and
   every push. The review, run late, found three HEAD-level defects that were already live in the cabinet.
2. **The short button press now fires on RELEASE, and a >600 ms hold on knobs 2–4 does nothing at all.** That is the
   correct pre-ENC-5 behaviour, not a fault — only encoder 0 has a long action wired.

🔵 **One ENC-4 question is still open for the owner:** under `prefers-reduced-motion` the progress ring keeps
sweeping instead of becoming handoff §6.5's "filling bar". See `design/FUTURE-WORK.md`.

It was the right next row for a reason beyond its own value, and that reason is now closed: **ENC-11's
tiered fault model used to have no way to tell the owner anything.** It works and is verified on
hardware, but a Degraded or Hard-fault outcome reached only the log and the API — so the safety
response was real (the host volume clamp tightens from 6 units per event to 2) and completely silent,
experienced as a volume knob that had quietly gone sluggish inside sealed furniture. **ENC-4 landed the
topbar host, ENC-8 [#527](https://github.com/mmackelprang/RTest/pull/527) built the page that explains it, and ENC-12 [#535](https://github.com/mmackelprang/RTest/pull/535) made it
*noticed*:** an amber/red fault badge on the Settings nav pill that is correct on every route, plus one
notification per browser session on escalation only. A healthy boot is still completely silent — the
repair path speaks only when something needed repairing. The record is kept here rather than deleted
because the gap is worth remembering: a correct safety response nobody can perceive is indistinguishable
from a broken knob.

ENC-4a (the persistent MUTED chip in the topbar) already shipped and is the pattern to follow.

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
holds at ±6 units against single events of 20 and 50 detents; the `ENC-4` HUD renders left-anchored
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
   `idle-dimmer.js:73-81` navigates without calling `SetSleepAsync(true)`, so
   `SleepService.IsSleeping` is **false** on the idle path and knobs act normally there; the pill
   sets it **true** and any encoder input is consumed by the wake. Testing sleep-screen encoder
   behaviour via the pill will look like a failure when it is the documented pre-ENC-6 behaviour.

---

## Live findings worth not re-deriving

- **The encoders factory config was measured, not inferred.** Before ENC-11 all four sat on `step=1`
  with tiers `(150ms x5), (80ms x15), (40ms x50)`. At the host 2% per unit that is 50 x 2% = 100
  volume points in one detent — the "one detent from silence to full" the handoff has warned about
  since Rev 2, read off the device. It is now `step=2` with `[150ms x2, 80ms x3, disabled]`, worst
  case 6 points.

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
