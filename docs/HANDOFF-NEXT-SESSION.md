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
| **Open PR** | **#511 `ENC-3`** — host clamps. Gates were green at hand-off; merge it first. |
| **Working tree** | clean, branch `feat/enc-3-host-clamps` |
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

## Start here: ENC-4

**Merge #511 first**, then take ENC-4 — the EncoderHud, every knob visible within 100 ms on every
route.

It is the right next row for a reason beyond its own value: **ENC-11 currently has no way to tell the
owner anything.** Its tiered fault model (Configured / Transient / Degraded / Hard fault) works and is
verified on hardware, but a Degraded or Hard-fault outcome is visible only in the log and the API.
ENC-4 hosts the badge; ENC-12 is the notification. Until then the safety response is real — the host
volume clamp tightens — but silent.

ENC-4a (the persistent MUTED chip in the topbar) already shipped and is the pattern to follow.

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

1. **SEC-2** (P2) — the Azure TTS key slot appears to hold a **Google** key. The secrets endpoint
   returns GoogleAPIKey and AzureAPIKey as byte-identical masked values, both beginning `AIza`, which
   is Google prefix. Latent while `tts:defaultEngine` is Google; it will surface as an auth failure
   that reads like a broken integration. Ten minutes of re-entry, or clear the slot.

2. **ENC-15** (P0) — the touch-wake gate needs a human touching a blanked panel. It is a hard
   predecessor of the ENC-6 blanking half, and the punch list is blunt that it "will look skippable
   right up until it isn't": if touch cannot wake a blanked screen and the encoder USB drops, the
   panel is unwakeable inside sealed furniture.

3. **O9 is the one irreversible step** — the knob order VOLUME / SOURCE / PRESETS / TUNING before the
   escutcheon is drilled.

---

## Known mismatch, deliberate

ENC-11 pushes device config in the handoff physical order **VOLUME / SOURCE / PRESETS / TUNING**,
while `RotaryEncoderActionRouter` still maps the old **Volume / Tuning / Source / Visualizer**.

Index 0 is VOLUME in both, so **the dangerous knob is correct today**. Indices 1-3 will feel wrong
until the router is remapped, which belongs with ENC-5 (SOURCE overlay) and ENC-7 (PRESETS) — those
rows introduce the behaviour the remap points at. Remapping earlier would leave encoder 2 driving a
PRESETS handler that does not exist yet.

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
