# HANDOFF — Start here

**Status:** **`[REVIEW DRAFT]`** · **Date:** 2026-08-19 · **Author:** Planner

---

## The situation, in one paragraph

This is Grandpa Anderson's console radio: a .NET 10 audio command center (Blazor Server UI, SoundFlow engine,
BT/Cast/SDR/vinyl/phone) running on an Intel N100 Ubuntu box in kiosk Chrome at 1920×720. **The cabinet is
nearly built and this is going into it** — a real piece of furniture in a family home, used by the owner and
by guests who walk up expecting a radio, recoverable only by SSH once the back is closed. **The knobs ship
live**: four rotary encoders get drilled into the escutcheon as `VOLUME · SOURCE · PRESETS · TUNING`, and the
encoder arc is now the largest single body of work in the project. The full prioritized punch list —
**23 P0 items, 36 P1, 15 P2** — is at [`docs/HANDOFF-GA-PUNCH-LIST.md`](HANDOFF-GA-PUNCH-LIST.md); the
encoder design is at
[`docs/design-handoffs/HANDOFF-rotary-encoder-mapping.md`](design-handoffs/HANDOFF-rotary-encoder-mapping.md)
(**Rev 3**, 1,126 lines — every encoder decision now closed). Read the punch list's §2 (ordering constraints) before claiming anything; it is the part that
bites.

---

## Start here: `TEST-1`

**The single first task, and it is not close.** Fix the test suite before shipping anything else.

**Why it goes first.** Four tests fail under timing/load pressure and pass on retry. Underneath them sits the
real defect: **unit tests can reach an ambient `localhost:5000`.** A unit test whose result depends on
whether `radio-api` happens to be running on the self-hosted runner is not a test — and every other item in
the punch list is verified by this suite. This has already produced **one wrong diagnosis** in a previous
session, and the most recent Builder cycle (`KIOSK-2`, PR #480) shipped with 5 failures that reproduce
identically on `main`, **on a branch that changes no C# at all.** Everything you do after this is claimed on
the strength of a green run, so make the green mean something first.

**Where to start:**

- `tests/Radio.Web.Tests/.../VisualizerPanelTests.cs:184-193` — the bUnit one. It races a real `await` and
  wants `WaitForAssertion`. **Five files in that project already use the idiom**; copy one.
- `tests/Radio.API.Tests` — three timeouts, and they are a **different** failure mode: 30–46 s under full
  suite load, 22/22 in ~5 s in isolation. Do not "fix" these by raising timeouts.
- The root cause is shared: find what lets a unit test open a socket to `localhost:5000` and cut it.

**Acceptance condition:** the full suite passes with `radio-api` **running** on the machine, and passes again
with it **stopped**, and the two runs agree. Anything less and you have not fixed it.

---

## Then, in order

1. **`OPS-1` — build stamp on `radio-web` + real deploy verification.** *Before the first post-install
   deploy.* Right now a stale `radio-web` binary passes verification silently (only `systemctl is-active` is
   checked). Once the cabinet is closed every fix arrives by deploy — the first one that does not take effect
   will get debugged as a code bug. ~80% already built; the gap is a version endpoint on `Radio.Web`.
2. **`ENC-1` — rewrite the HID decoder.** *Gates the entire encoder arc (12 P0 items).* The shipped parser
   reads an **8-byte** report with `sbyte` deltas; the device sends **37 bytes** with `int32` positions and
   accumulators, and the service has no concept of the config or diagnostics reports at all. Nothing else in
   the arc can be built on top of it. `src/Radio.Infrastructure/Platform/Input/HidRotaryEncoderService.cs:9-13,
   :154, :170, :184-215`. ⚠ **Read the re-baseline rule in "what NOT to do" before you write the delta
   logic** — it is a decoder requirement and the obvious implementation is wrong.
3. **`ENC-15` — the touch-wake gate. 1–2 hours, and it can be done any time.** Verify on the box that touch
   can independently wake a **blanked** panel. `SleepService.cs:84-87` disabled DPMS in the first place
   because touch-to-wake does not work when the compositor blanks input. If that is still true, blanking
   leaves **exactly one wake path — the encoders** — inside sealed furniture. It is P0 because it is a hard
   predecessor of `ENC-6`'s blanking half and it will look skippable right up until it isn't.
4. **`AUD-6` then `AUD-7` — output device identity, in that order.** *Never the other way round.* `AUD-6` is
   a persisted-format defect (`Id = $"playback-{i}"` is an enumeration ordinal); `AUD-7` makes startup
   *act* on that preference. Shipping `AUD-7` first converts a mis-report into a **mis-route**.
5. **`LOG-1` — 30 minutes, and the largest log-volume reduction in the project.** `src/Radio.Web/Program.cs:14`
   hardcodes `.MinimumLevel.Debug()` and never calls `ReadFrom.Configuration`, so the appsettings logging
   block is read by nothing: 65 MB/day measured, no retention cap, on a box where journald churn correlates
   with audio distortion. With the in-app log viewer being removed (D12), retention caps are now the *only*
   thing bounding that file.
6. **`PHN-1` + `PHN-2` — voicemail through the audio engine.** *Now P0.* The owner confirmed GV read works,
   which means this is live today: voicemail plays through a browser `<audio>` element, **bypassing mute,
   master volume, balance, ducking and Cast routing**. With Cast active it still comes out of the local
   speakers, and the radio does not duck under it — two sounds at full level in the room. ADR-029
   (`design/decisions/2026-08-03-gv-audio-through-engine.md`) already specs the fix. ⚠ **Do not start this
   without checking D25** — the owner has not ruled on whether to take a half-day JS-interop stopgap first.

---

## What NOT to do

Each of these has already cost this project time, or is one step from doing so.

- **Do NOT set `UseShazamForAllSources = false`.** It looks like the fix for SongRec overwriting AVRCP track
  titles. It **kills BT album art entirely** — BlueZ 5.72 ships no BIP/cover-art implementation, and 7 days
  of data show **0 AVRCP-sourced art vs 2,560 SongRec-sourced**. The wanted behaviour already exists at
  `BluetoothAudioSource.cs:893-905`, on the branch the flag never takes.
- **Do NOT ship `AUD-7` before `AUD-6`.** See above. Mis-report → mis-route.
- **NEVER promote the capture thread to realtime (`UseRealtimeCaptureThread`, `LOG-10`) before `LOG-6`.**
  That thread currently **logs and takes a managed lock** in its hot path. Bumping it to `SCHED_FIFO`-50
  converts a latency problem into a **potential priority-inversion hang**, on a box you can only reach by
  SSH. This is the one ordering constraint that can brick the appliance.
- **Do NOT use `POST /api/sources/events/{tts,file}` as a template for anything.** `SourcesController.cs:601`
  injects `IDuckingService` and never uses it (those events don't duck), `:651` adds a mixer source that is
  **never removed or disposed** (leaks per play), `:636` hardcodes ESpeak, and `PlayFileEvent` double-plays.
  ADR-029 says this explicitly.
- **Do NOT drill the escutcheon against any pre-Rev-2 layout.** The order is
  **`VOLUME · SOURCE · PRESETS · TUNING`** — ≈90 mm outer→inner, ≈70 mm inner pair. An earlier revision said
  `VOLUME · TONE · SOURCE · TUNING`; **Tone is out in full** (no new audio DSP for GA) and that order is
  dead. This is the only irreversible step in the project.
- ⭐ **Do NOT diff the encoder accumulator against your last remembered value across a disconnect.** This
  is the obvious way to write the decoder and it is **wrong**. The accumulator is **free-running — it keeps
  counting whether or not anything is listening**. If the app restarts, or a USB lead is knocked loose and
  re-seats, a naive diff delivers **every detent turned during the outage as one delta, on the volume
  knob.** The rule: **on every connect, the first sample from each encoder is a baseline, not an input —
  recorded and discarded. No delta is ever computed across a disconnect.** Designer's test, and it is the
  single most important safety test in the encoder spec: *"turn a knob ~50 detents while unplugged, then
  replug: volume does not jump."*
- **Do NOT ship panel blanking before `ENC-15` passes.** If touch cannot wake a blanked panel, the encoders
  are the only wake path, and losing the USB while dark leaves a screen that cannot be turned on from the
  room it lives in. Two coupling rules go with it: **never blank when the knobs are absent**, and **if they
  vanish while blanked, unblank immediately and stop blanking.** Fail toward light.
- **Do NOT delete `VolumeStepPercent`.** An earlier revision said to delete both encoder numeric fields.
  `TuningStepKHz` is dead and goes; **`VolumeStepPercent` is a genuine device field and is RELOCATED**, read-only,
  into the encoder configuration card. Deleting it loses a real value.
- **Do NOT "fix" the on-screen preset bank back to `MEMORY`.** It becomes `PRESETS · n saved` to match the
  engraved knob — a **deliberate, declared** one-word deviation from `HANDOFF-saved-station-display.md`.
- **Do NOT trust a green test run until `TEST-1` lands.** Including your own.
- **Do NOT treat encoder idle silence as a disconnect.** Reports are change-only. The reconnect loop must key
  on enumeration, not on quiet.
- **Do NOT re-litigate the parked decisions** in punch list §6 — twenty of them, each with the reason. In
  particular: the `sbyte` overflow argument is formally retracted by both documents, and the metrics-page
  "maintenance cost" premise is not supported by git history.

---

## State of the box

- **TTS was fixed live on 2026-08-19.** `tts:defaultVoice` is now **`en-US-News-K`** in the config store
  (it had been `"en"` — an eSpeak voice ID being sent to Google, producing a 400 that was swallowed into a
  `200 OK` and a "Success" toast). **Verified working, and ducking confirmed engaging** — which also closed
  the owner's separate "ducking is broken" complaint as a duplicate: ducking was never broken, it was never
  reached.
- ⚠ **`espeak-ng` is not installed on the box at all.** `/api/sources/events/tts/engines` reports ESpeak
  `isAvailable: false`. **Every path routed to ESpeak produces nothing** — including the Event Sources → TTS
  preview button, which hardcodes it. There is no eSpeak fallback anywhere, so the failure mode is silence,
  not degraded audio. Tracked as `TTS-7`; the decision (install it as an offline fallback vs remove it from
  the engine list) is still open.
- ⚠ **The TTS fix is box-only.** `appsettings.json:178` still ships `en-US-Standard-A`; the store value
  overrides it. Durable on `radio` across restarts and deploys, but **a fresh install elsewhere gets the
  robotic Standard voice.** Tracked as `TTS-8`.
- **Config-store writes go through `POST /api/configuration/{section}`, which UPSERTS rather than replaces.**
  Sending a partial section merges it; it does not clear the keys you left out.
- **Reaching the box:** `ssh mmack@radio` from WSL — **not the bare IP**, which fails on key selection.
  Deploy with `./deploy/Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64` — **the documented default
  is `linux-arm64`, which ships ARM binaries to this x64 host.**
- **Keep `journalctl` queries bounded** (`--since '-30min'`, never tail). Heavy log reads correlate with
  audio distortion on this hardware.

---

## Decisions already made — do not reopen

| ID | Answer |
|---|---|
| **D1** | **Knobs ship LIVE at install.** Encoder arc is unconditionally P0, ≈3–4 working weeks. |
| **D2** | Layout **final**: `VOLUME · SOURCE · PRESETS · TUNING`, ≈90 mm outer / ≈70 mm inner. |
| **D3** | Encoder index order **== physical left-to-right**. Owner-guaranteed constraint. |
| **D4** | Reset-position exists; **set-position does not**. Accumulator semantics forced. |
| **D5** | Detents-per-revolution **handled by firmware**; host sees monotonic counts. *(This retired the 4× acceleration-figure risk.)* |
| **D6** | **Tone is out** — no new audio DSP for GA. Browse withdrawn too; PRESETS took the knob. |
| **D7** | **Yes** — fold radio bands into the SOURCE list. `ENC-5` re-estimated to 4–5 days. |
| **D8** | **Yes** — re-enable screen blanking; **any press or knob movement wakes**. Resolved on one criterion — **the panel's own light**: dark → first input lights it and is consumed; lit → volume acts in place, everything else wakes. Waking from dark lands on **dim Ambient, never the full bright UI**. |
| **D9** | Maximum-volume ceiling: leaning **no**, not revisited. |
| **D10** | Engraving is **`SOURCE`** and **`PRESETS`**. |
| **D11** | **Remove Metrics from top-level nav**; fold trimmed diagnostics under Settings; kill the 40-query fan-out. |
| **D12** | **Remove the Settings → Logs tab.** *(Overrides the punch list's own recommendation — owner's call, reason recorded.)* |
| **D13** | **Delete `/diagnostic`.** Frees the route name for the consolidated diagnostics surface. |
| **D14** | **Wire** the ducking priority model. Do not remove the control. |
| **D15** | DataProtection keys **must be retained** → `SEC-1` is P0; branch `fix/dataprotection-keyring-persist-path` awaits review. |
| **D16** | Ring WAV **deprioritized** — the owner has a physical rotary phone with a working ringer. |
| **D17** | **GV read works today.** Voicemail and texts are visible and playable in the UI. |
| **D19** | **Canned text responses are wanted**, explicitly **without an on-screen keyboard** (ADR-029 Feature C). |
| **D21** | **One-time flash approved**, plus device config and an explicit **Save** action in the Settings UI. Rev 3 resolved it: **read-only visibility of all 24 fields**, four `Reverse` toggles, Save / Re-apply / Reset-counters. **Flash holds exactly the operating config** — Designer withdrew its own "safe baseline" because a Save that writes something duller than the screen shows is a button that lies. |
| **D22** | **Designer's D8 narrowing is APPROVED** — *"that D8 narrowing is fine, keep it."* A **turn** from Standby lights the panel only; **resuming audio requires a press or a screen tap.** The display honours D8 fully; the narrowing applies only to restarting *audio*, only from *Standby*, only for *turns*. |
| **new** | **Auto-detect the encoders** and degrade gracefully when absent. |

---

## Still open

Short, and honestly so. Designer Rev 3 closed the two that were blocking it, and the owner has since
answered the largest of the three it opened (**D22 — the D8 narrowing: approved**). What is left is **three
of Designer's four §13 questions plus one unruled fork.** None of it blocks starting work.

1. **D25 — the `PHN-2` stopgap.** A half-day JS-interop patch would route the voicemail `<audio>` element's
   volume and mute through master volume. It **does not** fix ducking or Cast routing. The full ADR-029 arc
   is 1.5–2 weeks. **Not ruled on — do not let it default silently into the full arc.**
2. **D24 — hand-editing the 24 encoder config fields?** Designer says no and this document agrees: *"a field
   that can be set to ×50 will eventually be set to ×50."* Full read-only visibility plus four `Reverse`
   toggles plus Save is what "configuration in the app" needs to mean.
3. **D23 — detents per revolution.** The last mechanical unknown, and a small one: it affects **no count**,
   only the "≈2.5 revolutions to cross the FM band" feel figures. One full turn on `ENC-14`'s Calibrate
   flow answers it once the knobs are in hand.
4. **`TTS-7`** — install `espeak-ng` as an offline fallback, or remove ESpeak from the engine list. Either
   is defensible; an engine listed as available that produces silence is not.
5. **`UX-1`** — skeleton shimmer amplitude needs a Designer answer, and may close as no-change.
6. **`D18` / `D20`** — file the bell-failure contract request to RotaryPhone, and write
   `docs/BUILDER_PROMPT.md`. Both are *actions*, not rulings; nobody is blocked.

Five confirm-or-close items remain in the punch list (`AUD-2`, `AUD-10`, `GV-10`, `TEST-2`, `UX-1`) — each
may legitimately close having produced nothing but an answer. Do not budget them as shipped work.

---

**Review draft. No queue rows have been added; `docs/BUILDER_QUEUE.md` is unchanged.**
