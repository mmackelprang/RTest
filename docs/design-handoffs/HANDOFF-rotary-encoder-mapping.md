# HANDOFF — Rotary encoder control mapping (four knobs on the cabinet face)

**Surface:** Physical — 4× rotary encoder + integrated shaft button (`github.com/mmackelprang/RotaryUsb`, Pi Pico, USB HID) mounted permanently in the restored console cabinet — **plus** the on-screen feedback those knobs drive across Home, the always-present topbar, and the sleep screen — **plus** the presence, configuration and blanking behavior the app owns on their behalf (§7, §8).
**Status:** **`[DESIGNER-PHASE DRAFT — REV 3 — FOR OWNER REVIEW]`**. §13 is down to four open questions. **The escutcheon is final (D2) — §9 is now a build document, not a proposal.**
**Status vs. existing handoffs:**
- **Extends** `design_handoff_radio_console` and `design_handoff_radio_controller` into a surface neither covers: there is no existing handoff for physical input. Every visual element is composed from tokens and components those handoffs already established; **no new design tokens are introduced** (§6.9).
- **Deviates (small, deliberate) from `HANDOFF-saved-station-display.md`** — that handoff's bank is titled `MEMORY · n saved`. The cabinet is engraved **PRESETS** (D10), so the on-screen bank is retitled to match. Rationale in §4.4; this is a one-word change and nothing else in that handoff moves.
- **Follows** `HANDOFF-kiosk-desktop-launcher.md` §5.1 — *silent when nothing needed fixing* — as the governing rule for the startup handshake (§7.4).
- **Follows** `HANDOFF-bell-failure-surfacing.md` §3.7 for cross-route surfacing of a persistent hardware fault (§7.6).
- **Extends** `HANDOFF-sleep-weather-visual-redesign.md` with a sleep-screen readout in that handoff's own dim-amber, single-emissive-color palette (§8.6). Its "one emissive color" rule is honored, not broken.
**Author:** Designer
**Date:** 2026-08-19 (Rev 1) · revised 2026-08-19 (Rev 2, Rev 3)
**Consumers:** Owner (§13) → Architect (§12.1) → Planner

---

## 0. Revision history

### Rev 3 — owner decisions D1–D21

**Confirmed as specced. Settled; do not re-litigate.**

| # | Decision | Effect |
|---|---|---|
| **D1** | **The knobs ship LIVE at install. The whole arc is P0.** | No inert-knob fallback path anywhere. `RotaryEncoderOptions.Enabled` flips to default `true` and becomes an escape hatch, not an opt-in gate (§7.3). |
| **D2** | **Layout approved. Escutcheon final:** `VOLUME · SOURCE · PRESETS · TUNING`, 90/70/90 mm pitches, ring groove on PRESETS. | §9 is now a build document. Its rationale is retained as the record of *why*, not as an argument still being made. |
| **D3** | **Owner guarantees encoder index order == physical left-to-right.** | The HUD spatial mapping (§6.2) is safe. Recorded in §5.0 as an **owner-owned constraint**, not an assumption I defend — with the one maintenance note it implies. |
| **D7** | **Fold the radio bands into the source list.** The source knob changes *bands*, not just source type. | Approved scope increase, now specced properly in §4.4 — including what commit does for a band vs. a source, and how it stays in sync with the on-screen band pills. |

**Decisions that changed the spec.**

| # | Decision | What moved |
|---|---|---|
| **D10** | Engraving is **`PRESETS`**, not `MEMORY` | Renamed throughout — knob, overlay, empty state, HUD copy, screen-reader text. **And the on-screen bank is renamed with it** (§4.4). A panel that says PRESETS over a screen that says MEMORY is the same mismatch class I flagged in the settings table; fixing one and not the other would have been worse than either. |
| **D4** | **Reset-position exists (`0x03`); set-position does not** | Accumulator semantics is now **forced by the protocol, not merely preferred** (§5.1). Full command table added (§7.1). And it surfaced a hazard I had missed: **the host must re-baseline on every reconnect**, or the first post-reconnect sample delivers every detent turned while the app was down — as one delta (§5.1, §7.5). |
| **D5** | **Detents-per-revolution is handled in firmware**; the host sees monotonic counts governed by the pushed config | **The 4× uncertainty — which I called the riskiest assumption in the document — is resolved.** §5.2's warning is withdrawn and the residual risk downgraded to a cosmetic one (§5.2). The calibration flow survives on different merits and is re-argued (§7.9). |
| **D8** | **Re-enable hardware blanking. Any button press or knob movement is a wake signal.** | **This collided with Rev 2's "volume acts in place" rule.** Resolved with one visible criterion — **the panel's own light** — in a rewritten three-state model (§8). One place where I have narrowed the owner's instruction is called out explicitly in §8.4 and put back to them as §13 Q1. The blanking risk I raised in Rev 2 is now a **hard precondition** (§8.5). |
| **D21** | One-time flash approved, **and the owner wants configuration + Save in the app** | **This partially reverses Rev 2 §7.6 ("delete the editors"), and the owner's question exposed a genuine weakness in my own design.** The "safe baseline" is **dropped** — it was solving a problem the host clamp already solves, and it would have made Save write something other than what the screen shows. §7.7 and §7.8 are rewritten. |
| **NEW** | **Auto-detect presence and degrade gracefully** | New §7.3, folded into the existing tiered fault model as a fourth tier rather than a second model. Includes the answer to "does the touchscreen change?" — **no, with exactly one behavioral exception.** |

**Where an owner decision overrode me, and what I would want them to know** — the honest list is §13.5.

### Rev 2 — summary

Tone withdrawn (no DSP, none planned); the free knob went to the preset bank; the button model was named (**Action** — a press never changes what turning does); the startup config push became a runtime requirement. The `sbyte` overflow argument was **retracted** — it described this repo's parser, not the device, which reports int32 positions and accumulators. The case against the factory acceleration tiers never needed it: at ×50 with `step_size 2`, **one detent takes volume from silence to full** (§5.4).

---

## 1. Problem + intent

The console has four knobs and a 1920×720 touchscreen tucked into a piece of living-room furniture. The touchscreen is the *leaning-in* surface. The knobs are the *standing-at-arm's-length* surface, and they are the only part of this machine a guest will treat as self-evident. A guest does not tap a cabinet. They grab a knob and turn it, and they expect the thing that happens to be the thing the knob is labeled.

The constraints, as they now stand:

- **Volume and Tuning each own one knob.** Not negotiable.
- **No new audio DSP.** Tone is off the table.
- **The knobs ship live at install (D1).** They are not an accessory bolted on later, so nothing in this spec may assume a period where the console works without them — nor may anything *break* when they are missing (§7.3).

The current code gives one knob to source selection and one to visualization mode. **Visualization does not survive this pass.** It is a set-once preference a person changes maybe a dozen times in the life of the machine, and it is the only one of the four current mappings that can be *inert* — it does nothing when the visualizer is hidden or the console is asleep. §4 replaces it; §11 keeps the capability.

The deeper problem is not the mapping. It is that **two of the four knobs currently produce no visible evidence that anything happened**, and one of them changes the machine's entire behavior from an invisible internal counter. A knob that acts silently is worse than a knob that does nothing, because the user's response to silence is *to turn it further*.

---

## 2. Current-state audit

### 2.1 What the knobs do today

Authority is `src/Radio.Infrastructure/Platform/Input/RotaryEncoderActionRouter.cs`. The docs disagree with it in two places; the code wins.

| # | Turn | Press | Visible on screen? |
|---|---|---|---|
| 0 | Volume ± `VolumeStepPercent`, clamped 0–1 (`:128-136`) | Mute toggle (`:138-143`) | **Partially.** `VolumeChanged` reaches the browser via `AudioStateUpdateService.cs:463-473` → `AudioStateStore`, and moves the `NowPlayingPanel` slider + `NN%` readout. That panel is the **left 520 px rail of Home only** — on `/queue`, `/metrics`, `/phone`, `/devices` there is nothing at all, because `NowPlayingDock` carries no volume control. Mute shows only as an icon glyph swap. |
| 1 | Radio frequency step × `\|delta\|` (`:147-178`), radio sources only | Start/stop scan, direction Up (`:180-196`) | **Only while `RadioControlPanel` is the visible center panel.** Otherwise silent. Inert on every non-radio source. |
| 2 | Advances a **private** `_currentSourceIndex` over `[Radio, FilePlayer, Bluetooth, Vinyl, GenericUSB]`; `LogDebug` and nothing else (`:200-207`) | Commits — actually switches the source (`:209-227`) | **No. Nothing whatsoever.** The user turns, the machine changes state, the screen does not move, and the next press changes what the console is doing. |
| 3 | `VisualizationModeService.CycleMode(delta)` (`:231-234`) | `ToggleEnabled()` (`:236-239`) | **No.** The broadcast exists (`AudioStateUpdateService.cs:969-978`), but **no Razor component subscribes to it** — `VisualizerPanel.razor` holds its own local `_currentMode`. |

Plus: **any turn or press while `ISleepService.IsSleeping` is consumed as a wake** (`:60-64`, `:90-96`) and performs no action.

### 2.2 Five defects found while speccing — Planner should fold these in

1. **`TuningStepKHz` is dead config.** `RotaryEncoderOptions.cs:31` defines it, `appsettings.json:254` sets it, `INTEGRATIONS.md` documents it, and `SystemConfigPage.razor:1535` puts an editable numeric field in front of the owner — and **nothing reads it.** The router calls `radio.StepFrequencyUpAsync()` and lets the radio service own the step. Delete it (§7.8).
2. **The Encoder Mapping table in the UI is stale.** `SystemConfigPage.razor:1492-1495` claims encoder 1 press = "Seek Next Station" and encoder 2 press = "Play/Pause". The code does scan-toggle and source-commit. It is hand-typed HTML, so it will drift again. Serve it from the router's own mapping (§12.2).
3. **`VisualizerPanel` never subscribes to `VisualizationModeChanged`.** Any out-of-band mode change leaves the on-screen picker showing the wrong segment.
4. **No throttling anywhere in the input path.** `HidRotaryEncoderService` polls at `PollIntervalMs = 10` and raises one event per report. No debounce, no rate limit — a fast spin can drive state changes at up to 100 Hz. On an Intel N100 where **audio distortion already correlates with incidental CPU load**, an unthrottled 100 Hz SignalR fan-out to a Blazor Server circuit is a plausible new distortion trigger, and a miserable one to diagnose: it would only reproduce while someone was touching the radio. §6.8 makes coalescing a requirement.
5. **The shipped parser cannot represent the device's protocol.** `HidRotaryEncoderService.ParseReport` (`:174-203`) reads an 8-byte report with one **signed byte** delta per encoder. The device reports **int32 positions and int32 accumulators**, and accepts the configuration, command and diagnostics reports in §7.1 that the current service has no concept of. A transport mismatch, not a tuning problem — **everything in §7 depends on closing it first** (§12.2).

### 2.3 There are two software sleep states, and now a third physical one

| | **Ambient** (was "Screen Sleep") | **Standby** |
|---|---|---|
| How you get there | 30 min idle → `idle-dimmer.js:78` does `window.location.href = '/sleep'` | Topbar **Sleep** pill (`MainLayout.razor:1060-1067`) or an API call → `SystemApi.SetSleepAsync(true)` |
| Calls `SleepService`? | **No** — the comment at `idle-dimmer.js:71-74` is explicit that idle navigation must not | Yes → `EnterSleepAsync()` |
| `ISleepService.IsSleeping` | **`false`** | `true` |
| Audio | **Still playing.** Untouched. | **Paused and muted** (`SleepService.cs:60-80`) |
| What the knobs do today | `TryWakeFromSleep` returns false → **every knob acts normally, invisibly, on a screen showing a clock** | First input is swallowed as a wake |

So the current behavior is not "any input wakes." It is *"any input wakes, except in the state you actually reach overnight, where every knob acts silently instead."* The volume-at-2am scenario is not hypothetical — **it is what the machine does today**, with zero feedback.

**D8 adds a third state: the panel physically dark.** `SleepService.cs:84-87` disables hardware DPMS blanking today with the comment *"Will be re-enabled when rotary encoders provide a hardware wake source."* That precondition is this handoff, and the owner has approved re-enabling it. §8 is the resulting three-state model, and §8.5 carries the risk that comes with it.

---

## 3. The five principles this mapping is derived from

1. **No knob may be inert in any reachable state.** A dead knob reads as a broken machine, and the user's first repair attempt is to turn it harder. This eliminates Visualization, eliminates Browse (§4.3), and forces the tuning knob to be context-aware (§4.4).
2. **Every knob has a visible surface, on every route, within 100 ms of the first *acted-upon* input.** No exceptions, including the sleep screen — and including inputs that are consumed rather than applied (§8.3).
3. **Turning acts; selecting commits.** A knob that changes *level* is instant and self-correcting because you hear it. A knob that changes *what the machine is doing* previews first and requires a deliberate press. A wrong source switch on this box is genuinely expensive (§10.3).
4. **Nothing a knob does may startle a room, and no press may destroy anything.** No volume slam, no abrupt source cut, no wrap from silence to full, no deletion, no power-off.
5. **The physical order is permanent; the assignment is not.** This principle earned its keep twice — Rev 2 withdrew Tone and Rev 3 renamed a knob, neither of which touched a hole. **The escutcheon is now final (D2); the mapping remains software.**

---

## 4. Recommended mapping

### 4.1 The button model — the Action model

> ### **A press performs one named, immediate action. No knob's button ever changes what turning that knob does — on any knob, in any state, ever.**

**Why the mode/shift model is wrong on this hardware, in furniture.**

1. **You cannot see the mode.** The device has no LEDs and no display. The only feedback surface is a screen that may be asleep, showing a clock, **physically dark (D8)**, or out of the line of sight of the person with a hand on the knob. **A mode you cannot see is a mode you cannot trust** — and the way a person resolves "which mode is this in?" is *to turn it and find out*. On a volume knob, finding out costs the room.
2. **Modes are sticky, and this machine has many users across time.** A knob left in its secondary mode at 11 p.m. behaves wrongly at 8 a.m. for someone who did not set it and does not know it exists.
3. **A guest has no model for it.** Every physical convention a guest brings says *press = do*.
4. **Modes multiply the mis-grab problem.** Under the Action model a mis-grab is "wrong knob" — one error, immediately legible. Under a mode model it becomes "wrong knob, in an unknown state."
5. **Vintage precedent, which is this project's whole premise.** Console knobs never had modes, and that is a substantial part of why a 1948 radio is still operable by anyone who walks up to it.

**Why the hybrid is worse than either pure model.** A hybrid buys capability and pays in *predictability*, which is the only currency a knob has. If three knobs are press-to-act and one is press-to-shift, the user must remember which — and the only way to check is to press it and observe. **A consistent rule that is slightly less capable beats four individually-clever behaviors.**

**Context is not mode — and this distinction is load-bearing.** The TUNING button does seek on radio and play/pause on everything else (§4.4). That is *context*, selected by something the user already knows and can see, not a hidden state a button toggles. The test: **can the user tell what the control will do without touching it?** For context, yes. For a mode, no. Every context switch in this spec is keyed to the active source and to nothing else.

### 4.2 Summary table — the deliverable

Physical order is left → right as the owner faces the cabinet, and **matches encoder index 0..3 by owner guarantee (D3)**.

| Pos | Enc | Engraved | **Turn** (never changes) | **Press** (< 600 ms, on release) | **Long-press** (≥ 600 ms) | Kind |
|---|---|---|---|---|---|---|
| 1 (far left) | 0 | **VOLUME** | Master volume | **Mute / unmute** | **Standby** — and wake from Standby | Continuous |
| 2 | 1 | **SOURCE** | Move the highlight in the source list (**preview only — changes nothing**) | **Select** — commit the highlight | — | Selector |
| 3 | 2 | **PRESETS** | Move the highlight in the preset list (**preview only — changes nothing**) | **Recall** — commit the highlight, switching source and band if needed | **Save** what is playing to the next free slot | Selector |
| 4 (far right) | 3 | **TUNING** | Radio: step frequency · Everything else: previous / next track | Radio: **Seek** up (press again = stop) · Everything else: **Play / Pause** | — | Continuous |

### 4.3 Why PRESETS takes the free knob — and the three candidates I rejected

The slot had to be filled by something that ships with **no new audio DSP**, is **reached for often**, is **meaningful on every source**, and **will not strand a guest** — the same gate that eliminated Visualization.

**The recommendation: PRESETS — the saved-station bank, on a knob.** Turn moves a highlight through the saved list; press goes there, switching source and band if it has to; hold saves what is playing. On a real radio, the preset buttons are the second-most-used control on the panel after volume — *"put on my station"* is the single most common thing anyone does to a radio, and right now on this console it costs a source switch plus twenty-five detents of tuning.

Against the gate:

- **No new DSP, and no new data model in v1.** It drives the preset bank that already exists (`HANDOFF-saved-station-display.md`, `PresetCard.razor`, save/rename/delete all shipped).
- **Meaningful on every source, because the list is not scoped to the active source.** You are on Bluetooth, you turn PRESETS, you see your stations, you press one, and the console switches to FM and tunes it. **It is never dead** — and it is the only candidate that is *more* useful the further you are from what you want to hear.
- **It will not strand a guest.** It is the opposite: turn it, press it, something good plays. If a guest touches exactly one knob after volume, this is the one that should reward them.
- **The empty state teaches its own use** (§6.6).

**Rejected — Browse.** Turn a highlight through the presets on radio, or the queue otherwise. **Dead or near-dead on half the sources**: Bluetooth is a phone-driven stream with no queue the console owns, and Phono and USB are line inputs with no list at all — precisely the failure that demoted Visualization. And browsing is a look-at-the-screen activity, which is the wrong ergonomics for a knob.

**Rejected — Balance.** `BalanceModifier` already exists, so it needs nothing new, and it is meaningful on every source. But it is a **set-once installation control** — two speakers in one cabinet in one room. It fails *reached for often*, and its failure mode is silent: knocked off center, the console is quietly lopsided with no reason a user would ever look for.

**Rejected — Output / Cast destination.** Genuinely useful in a multi-room house, but **a mis-grab sends the audio to another room** — the most startling failure this hardware can produce, with no model for undoing it.

*(Also considered and set aside as too rare to earn a permanent hole: sleep timer, display brightness, per-source gain trim.)*

### 4.4 Knob-by-knob specification

#### Knob 1 — VOLUME (encoder 0) · continuous

- **Turn.** Master volume, `±(step × multiplier)` percentage points, clamped 0–100. Continuous, instant, no confirmation.
- **Turning while muted unmutes.** The most important small rule in this document. Today, turning the volume knob while muted changes a value nobody can hear; the user sees the number move, hears nothing, and concludes the radio is broken. Every car radio built in the last thirty years unmutes on a volume turn. The first detent clears mute and applies the delta in the same frame.
- **Press — Mute / unmute.** Fires on *release*, before the long-press threshold. Mute is a state, not an event, so its indicator is persistent (§6.7).
- **Long-press, 600 ms — Standby.** Pauses and mutes audio and shows the standby screen. While in Standby, a long-press wakes. This restores the vintage console's power switch to the volume knob, and answers *"how do I turn this off"* — otherwise unanswerable from the panel.
  - At 300 ms the volume HUD begins drawing a progress ring, completing at 600 ms; the action fires **at the threshold, while still held**, and the release does nothing. Let go before 600 ms and the ring collapses and **mute fires instead**.
  - **Host-side synthesis.** The protocol reports raw press/release only.
  - **It must never map to system shutdown** (§10.6).

#### Knob 2 — SOURCE (encoder 1) · selector

Preview an index, commit on press — the mechanism the code already has is **correct** and only ever lacked a screen. Two reasons it is right, both specific to this machine:

1. **Live-commit-per-detent would be actively harmful.** Spinning through the list would tear down and stand up an audio source at every detent. This repo has two open bugs in exactly that area — the long-running capture device lifecycle bug and the `autoSwitchOnConnect` Bluetooth bug — and a switch to Bluetooth can take seconds or fail outright.
2. Principle 3: selecting commits.

- **Turn.** Opens the source overlay if closed, and moves the highlight one entry per detent. **Nothing switches.** Acceleration disabled (§5.3) — one detent is always exactly one entry.
- **The list is a band selector (D7 — approved).** The tuner's bands are folded in as first-class entries, the way the original cabinet's selector read `BROADCAST / SHORTWAVE / PHONO`. The user does not think "Radio source, then FM band." They think *"put on FM."*

  | # | Entry | Commit does |
  |---|---|---|
  | 1 | **FM** | Radio band |
  | 2 | **AM** | Radio band |
  | 3 | *(SW / WB — see composition rule below)* | Radio band |
  | 4 | **BLUETOOTH** | `AudioSourceType.Bluetooth` |
  | 5 | **PHONO** | `AudioSourceType.Vinyl` |
  | 6 | **USB** | `AudioSourceType.GenericUSB` |
  | 7 | **FILES** | `AudioSourceType.FilePlayer` |

  Specifics D7 makes necessary:
  - **Committing a band while the radio source is already active is a band change, not a source switch** — `SetBandAsync(band)`, no engine teardown, no spinner, no 150 ms source fade. It should feel instant, because it is. Restore the **last-tuned frequency for that band**, falling back to the band's default when there isn't one.
  - **Committing a band while some other source is active does both** — activate the radio source, then set the band, then restore that band's last frequency. This is a real source switch: 150 ms fade, State D spinner, State E on failure (§6.6).
  - **The "current" marker tracks the active *band*, not just "Radio."** On AM, row 2 is marked current — not row 1. Getting this wrong makes the knob feel like it lost its place.
  - **List composition is fixed per tuner, resolved once at startup**, from the bands the active tuner reports. A tuner that never reports SW does not render a permanently dead SW row; a tuner that does gets it at position 3, always. Composition does not change during a session, so positions are stable in the only sense that matters. **Within a composition, positions never move** — no recency ordering, no hiding an unavailable entry.
  - **Unavailable entries render dimmed with a reason**, reusing `SourceBubble`'s existing `" · offline"` idiom (`SourceBubble.razor:13-39`). With no tuner hardware at all, FM/AM dim to `no tuner detected`. A deliberate divergence from the topbar strip, which *is* a live list — **declared here so Polisher does not flag it as drift.**
  - **The knob and the on-screen band pills are the same state.** `RadioControlPanel`'s band pills and this overlay both read and write the active band; neither may hold its own copy. TUNING's band-edge wrap (§4.4, TUNING) uses the **active band's** edges.
  - The list wraps, with the highlight animating bottom→top over 200 ms so it reads as a wrap rather than a teleport.
- **Press — Select.** One rule: *press commits the highlight.* With the overlay closed the highlight is what is already playing, so a press commits the status quo — which changes nothing and opens the overlay showing you where you are. The "open" behavior falls out of the rule rather than being a second meaning for the button.
- **Pressing a dimmed entry is never a silent no-op** — it flashes that row amber with its reason for 1.5 s and leaves the overlay open (§6.6 State C).
- **No long-press.**

#### Knob 3 — PRESETS (encoder 2) · selector

**Renamed from MEMORY in Rev 3 (D10).** The function is unchanged; the owner chose the engraving, and the word now has to be the same in three places or it is worse than either choice alone:

1. the escutcheon — **PRESETS**;
2. this overlay — **PRESETS**;
3. **the existing on-screen bank in `RadioControlPanel`, today titled `MEMORY · n saved` — retitled to `PRESETS · n saved`.**

Item 3 is a deliberate one-word deviation from `HANDOFF-saved-station-display.md`. I am taking it because a panel engraved PRESETS sitting under a screen that says MEMORY is exactly the mismatch class this spec already objects to in the settings table (§2.2 defect 2) — and a physical engraving cannot be edited later. Everything else in that handoff (field hierarchy, slot numbering, long-press-to-save, kebab menu) is untouched.

Same interaction grammar as SOURCE, deliberately. Two adjacent selector knobs that behave identically is a feature: learn one, you have learned both.

- **Turn.** Opens the preset overlay if closed, moves the highlight one entry per detent. **Nothing plays.** Acceleration disabled (§5.3).
- **The list, v1.** The existing saved-station bank in slot order, with its name-primary / frequency-secondary field hierarchy carried over intact. Fixed slot positions — slot 3 is always slot 3.
- **Press — Recall.** Commit the highlight: **switch source and band if needed**, tune, and play. Recall is not scoped to the active source — that is what keeps the knob alive from Bluetooth or Phono. With the overlay closed, the highlight is the currently-playing entry (or nothing), so a press just shows the list.
- **Long-press, 600 ms — Save.** Write what is playing to the **next free slot**. Same 300→600 ms ring, same threshold as everywhere else in this project.
  - **Never overwrites.** If every slot is full, the HUD says `PRESETS FULL — replace a slot on screen` for 2 s and writes nothing. Replacement stays on the touchscreen behind the existing kebab, where it has a confirmation and an undo.
  - **v1 saves radio stations only.** On a non-radio source the hold reports `Only radio stations can be saved` for 1.5 s. A clearly-messaged v1 boundary, not a silent failure, and the one gesture in this spec that is context-limited. Cross-source presets are the natural v2 and need a data model that does not exist (§12.1).
- **Empty state is instructional**, not empty: `NO STATIONS SAVED` / `hold this knob to save what's playing`.

#### Knob 4 — TUNING (encoder 3) · continuous

Internally the **Dial**: *move through the content of whatever is playing.* One concept, two expressions, never inert.

- **Turn, radio sources.** Step the frequency one tuner step per detent × the acceleration multiplier. **One detent = one step of whatever the on-screen `STEP` control currently reads** (`RadioControlPanel.razor` → `CycleStepSizeAsync`) — do not hard-code 200 kHz, and do not resurrect the dead `TuningStepKHz` for this.
- **Turn, everything else.** Previous / next track. **The host collapses any delta to ±1 and debounces at 300 ms**, so a hard spin advances one track rather than eight. The device's acceleration tiers are configured for a frequency dial and are meaningless against a track list; the host ignores the magnitude in this mode rather than reconfiguring the device on every source switch.
- **Band edges: wrap, but with a sticky stop.** With an endless encoder there is no physical end, so a hard stop at the top of the band reads as a broken knob — and every car radio wraps. But at ×8 a fast spin would fly through the boundary and dump the user somewhere random. So: **the first detent that reaches a band edge stops there.** Crossing requires a fresh detent after a ≥300 ms gap. Deliberate wrap yes; accidental wrap no. Host-side synthesis. The frequency well flashes its border amber for 200 ms at the edge.
- **Press, radio — Seek.** Seek up (`StartScanAsync(Up)`); pressing again while scanning stops it.
- **Press, everything else — Play / Pause.** The transport control the panel currently lacks entirely, on the button a user is already holding while skipping tracks.
- **No long-press.** Save-to-preset lives on PRESETS, where recall and store belong together.

### 4.5 Press behavior at a glance, including what a mis-grab costs

| Knob | Press does | Destructive? | Startling? | How it is undone |
|---|---|---|---|---|
| VOLUME | Mute / unmute | No | **Yes — the room goes quiet** | Press again. Persistent `MUTED` chip (§6.7) says why. |
| VOLUME (hold) | Standby | No | **Yes — quiet and dark** | Hold again. Requires a deliberate 600 ms with a visible ring from 300 ms. |
| SOURCE | Select the highlight | No | No — with the overlay closed it commits what is already playing, i.e. **nothing happens** | Turn back and press. |
| PRESETS | Recall the highlight | No | No — same closed-overlay no-op | Turn back and press, or recall the previous entry. |
| PRESETS (hold) | Save to next free slot | **No — never overwrites** | No | Delete on the touchscreen (which has its own undo toast). |
| TUNING | Seek (radio) / Play-Pause (other) | No | Mildly — a seek moves the station | Press again to stop the seek; PRESETS recalls the station back in one press. |

**No press in this spec deletes anything, powers anything off, or sends audio out of the room.** The two startling presses are on the same knob, both audible-and-obvious the instant they happen, and both reversed by repeating the identical gesture. The two selector presses are *free* on a mis-grab because a closed overlay commits the status quo — the design's answer to putting two similar knobs side by side.

---

## 5. Per-encoder device configuration

### 5.0 One constraint the owner owns

**Encoder index order equals physical left-to-right order (D3).** This is a wiring guarantee from the owner, not an inference from the protocol, and everything in §5.2 and §6.2 rests on it — in particular the HUD's spatial mapping, which is only learnable if it is *always* true.

One maintenance note follows, and it is the sort of thing that gets rediscovered expensively: **if the Pico is ever replaced or the harness re-terminated, this guarantee must be re-established, not assumed.** The `Calibrate a knob` flow (§7.9) is the fastest way to confirm it — turn the leftmost knob, see which row moves.

### 5.1 Semantics: accumulator-driven, and now forced

**D4 settles this.** The command set (§7.1) offers `0x03 reset positions to min` and **no set-position command at all**. So position semantics would leave the device holding an absolute value the host can never correct — and the host *must* correct it, because volume is written from the touchscreen slider, from AVRCP on a paired phone, from the REST API, from Cast, and from the sleep enter/exit path (`SleepService.cs:78-80` mutes and later restores). Under position semantics every one of those writes desynchronizes the knob permanently, and the only available remedy is to slam the position to *minimum*.

Rev 2 recommended accumulator on design grounds. **Rev 3 records that the protocol forces it**, which is a stronger and simpler position to hold.

Two consequences worth stating plainly:

- **`min_value`, `max_value` and `wrap` are inert** under accumulator semantics. They are still pushed (§5.2) — a field being unused today is not a reason to leave a device the app is responsible for in an unknown state.
- **The host must re-baseline on every connect and reconnect.** This is the hazard D4 surfaced that Rev 2 missed. The accumulator is free-running and keeps counting whether or not anything is listening. If the app restarts, or the USB is knocked loose and re-seated (§7.3), and the host resumes by diffing the new sample against its last remembered value, then **every detent turned during the outage arrives as one delta** — potentially a violent one on the volume knob. The rule:

  > **On every connect, the first sample from each encoder is a baseline, not an input. It is recorded and discarded. No delta is ever computed across a disconnect.**

  `0x03 reset positions to min` is issued as part of the startup handshake (§7.5) as belt-and-braces — cheap, since positions are unused, and it means a knob that has drifted for any reason starts from a known state. It is the **only** host-side position control that exists, so it is also the documented recovery for a knob whose reported values look wrong.

### 5.2 The literal configuration table

**D5 resolves the assumption that Rev 2 called the riskiest in this document.** The firmware handles detent decoding and delivers **monotonic counts governed by the configuration the host pushes** — not raw quadrature edges. One detent is one count of `step_size`. There is no 4× divisor question, and Rev 2's warning that *"every figure here could be off by 4×"* is **withdrawn**.

What remains is smaller and cosmetic: **detents per revolution is a mechanical property of the encoder body**, not of the protocol. Every "revolutions to cross the band" figure below assumes a conventional 20-detent knob. If the hardware turns out to be 24- or 30-detent, **the counts stay exactly right and only the revolution figures move** — the feel gets slightly slower or faster, and nothing needs re-deriving. It is a one-turn measurement once the knobs are in hand (§7.9), not a risk to the design.

| | **Enc 0 — VOLUME** | **Enc 1 — SOURCE** | **Enc 2 — PRESETS** | **Enc 3 — TUNING** |
|---|---|---|---|---|
| **Semantics** | accumulator | accumulator | accumulator | accumulator |
| `min_value` | `0` *(inert)* | `0` *(inert)* | `0` *(inert)* | `0` *(inert)* |
| `max_value` | `100` *(inert)* | `6` *(inert)* | `6` *(inert)* | `0` *(inert)* |
| `step_size` | `2` | `1` | `1` | `1` |
| `wrap` | **`false`** | `false` *(host wraps the list)* | `false` *(host wraps the list)* | `false` *(host wraps the band — §4.4)* |
| `reverse` | `false` | `false` | `false` | `false` |
| **T1** (threshold ms, ×) | `150, 2` | **`0, 0` — disabled** | **`0, 0` — disabled** | `150, 2` |
| **T2** | `80, 3` | **`0, 0` — disabled** | **`0, 0` — disabled** | `80, 4` |
| **T3** | **`0, 0` — disabled** | **`0, 0` — disabled** | **`0, 0` — disabled** | `40, 8` |
| **Host per-event clamp** | **`±6`** | **`±1`** | **`±1`** | `±8` radio / **`±1`** track |
| One count means | 1 percentage point | 1 list entry | 1 list entry | 1 tuner `STEP` |

`reverse` is `false` on all four, meaning **clockwise increases** — louder, down the list, down the list, up in frequency. If a knob is wired backwards, this flag is the fix, and it is the one field a human should ever edit (§7.8). `INTEGRATIONS.md`'s troubleshooting section currently tells the reader to swap the A/B pins on the Pico or negate the delta in firmware; with a `reverse` flag in the protocol that advice is wrong and should be replaced (§12.2).

**The host-clamp row is not defensive boilerplate.** There is a real window on every boot and after every reconnect during which the device runs whatever is in its flash (§7.5), and the clamp is what makes that window safe. It is also — see §7.7 — the reason flash can honestly hold the real operating configuration instead of a deliberately duller one.

### 5.3 Why acceleration is disabled entirely on both selector knobs

A seven-entry list with a ×5 multiplier means one quick flick moves the highlight five entries and lands somewhere the user did not aim. Acceleration exists to make long traversals bearable; there is no long traversal in a seven-item list. **One detent, one entry, always** — on SOURCE and PRESETS alike. It is also what keeps the two adjacent selector knobs interchangeable in the hand (§9.2).

### 5.4 Volume — the numbers, and why there is no third tier

Full scale is 0 → 100. At a conventional 20-detent revolution:

| Tier | Interval per detent | Mult | Points per detent | Detents 0 → 100 | Revolutions |
|---|---|---|---|---|---|
| base | > 150 ms | ×1 | 2 | 50 | ≈ 2.5 |
| T1 | 80–150 ms | ×2 | 4 | 25 | ≈ 1.25 |
| T2 | 40–80 ms | ×3 | 6 | 17 | ≈ 0.85 |
| T3 | — | *disabled* | — | — | — |

Maximum slew is **6 points per 80 ms = 75 points/second** — silence to full takes at least **1.33 seconds** of sustained, deliberate spinning. Fast enough to kill the volume when the phone rings; slow enough that no single gesture produces a blast.

**Compare the factory defaults on this knob.** With `step_size = 2` and T3 at ×50, **one detent moves volume by 100 points** — a single click from silence to full, in a living room, from a knob a guest may be touching for the first time. This is the whole case against the factory tiers, and it is why §7.5 treats "the device is running factory defaults right now" as a live safety state rather than a startup detail.

**Volume must not wrap.** `wrap = false` is the single most safety-critical value in the table — one detent past zero would be full scale, at 2 a.m., pointed at a sofa. This is why §7.6 promotes a `wrap` mismatch to a hard fault while a mismatched acceleration tier is only amber.

### 5.5 Tuning — the numbers against the actual band

US FM is 88.1–107.9 MHz on 0.2 MHz centers: 100 channels, **99 steps end to end**. (The panel header reads `FM · 87.5–108.0 MHz`, the tuner's hardware range; the channel grid is what a listener traverses.)

**FM, `STEP` = 200 kHz — the normal case**

| Tier | Interval per detent | Mult | MHz per detent | Detents 88.1 → 107.9 | Revolutions (20-detent knob) |
|---|---|---|---|---|---|
| base | > 150 ms | ×1 | 0.2 | 99 | ≈ 5.0 |
| T1 | 80–150 ms | ×2 | 0.4 | 50 | ≈ 2.5 |
| T2 | 40–80 ms | ×4 | 0.8 | 25 | ≈ 1.25 |
| T3 | < 40 ms | ×8 | 1.6 | 13 | ≈ 0.6 |

**FM, `STEP` = 100 kHz** — 205 steps over 87.5–108.0: 205 / 103 / 52 / **26** detents.
**AM, 10 kHz, 530–1700 kHz** — 117 steps: 117 / 59 / 30 / **15** detents.

The feel this is designed for is deliberately vintage: **a slow, careful turn moves one channel at a time; a firm two-and-a-half-turn sweep crosses the band; a hard flick crosses it in about two-thirds of a revolution.** Old dial-cord tuners took three or four turns end to end.

**Against the factory defaults:** ×5 gives 1.0 MHz per detent (the whole band in one revolution); ×15 gives 3.0 MHz; **×50 gives 10.0 MHz per detent — two detents cross the entire FM band.** That is not a tuning dial, it is a random station generator.

### 5.6 Retracted: the `sbyte` overflow argument

Rev 1 argued here that a ×50 tier would overflow a signed-byte report field and reverse the knob's direction. **Withdrawn** — it described this repo's parser, not the device, which reports int32 positions and accumulators. §5.4 is the load-bearing argument and never depended on it.

What survives is larger and sits at §2.2 defect 5: the shipped `HidRotaryEncoderService` speaks an 8-byte sbyte-delta report and has no concept of the device's configuration, command or diagnostics reports. **Everything in §7 requires closing that gap first** (§12.2).

**How this configuration reaches the device, what happens when the device is not there, and what the owner sees in each case, is §7.**

---

## 6. On-screen feedback — the `EncoderHud`

### 6.1 What exists today, and what this proposes to build

There is **no HUD, toast convention, or transient-overlay component in this project.** The inventory turned up exactly three reusable pieces, and the proposal is built entirely from them:

1. **`.snackbar-enter` / `.snackbar-exit`** (`design-system.css:1218-1219`) and `@keyframes snackbarSlideIn` / `snackbarSlideOut` (`:1030`, `:1035`) — 200 ms, `--anim-ease-emphasized`. **These exist and no Razor component consumes them.** Use them; do not write new keyframes.
2. **The `GainPopoverService` pattern** (`src/Radio.Web/Services/GainPopoverService.cs`, backdrop in `MainLayout.razor:236-245`) — the established way to host an overlay *above* the `.page-transition` stacking context.
3. **`SourceBubble.razor` + `SourceTypeHelper.cs`** for the source overlay rows, and **`PresetCard.razor`'s field hierarchy** for the preset overlay rows — so neither overlay can drift from the surfaces they mirror.

**Not Radzen `NotificationService`** for the HUD. It is the right channel for "a thing happened, here is a sentence about it" — which is why §7.3 and §7.6 use it — and the wrong channel for a value that updates at 20 Hz while a hand is moving.

**One component, two hosts.** `EncoderHud.razor` renders in `MainLayout` for every normal route, and again inside `Sleep.razor` with `Variant="Sleep"`. Two hosts are unavoidable: the sleep screen is a separate route on `EmptyLayout`, so `MainLayout` is not in that tree. `z-index: 10000` in the MainLayout host, matching the gain-popover tier.

### 6.2 Geometry — the readout follows the knob

**Transient readouts appear above the knob that produced them. Selection overlays center.**

The four knobs sit in a row across the cabinet face (§9), so the HUD divides the 1920 px width into quarters — centers at **240 / 720 / 1200 / 1680 px** — and renders the active knob's card in its own quarter, bottom-anchored at `bottom: 24px`. Turn the second knob from the left, and something lights up above the second knob from the left. Nobody has to be told this; they see it once and they know which knob is which forever. **D3's index-order guarantee is what makes this dependable.**

```
 1920 × 720 kiosk viewport
┌───────────────────────────────────────────────────────────────────────────────┐
│  topbar (120px: 64 primary + 56 source bubbles)            [MUTED]  ← §6.7    │
├───────────────────────────────────────────────────────────────────────────────┤
│                          │                       │                            │
│   NowPlayingPanel        │  RadioControlPanel    │     VisualizerPanel        │
│   (520px)                │  / QueueHistoryPanel  │     (710px)                │
│                          │  (flex ≈688px)        │                            │
│    ┌─────────┐    ┌─────────────────────┐        │    ┌─────────┐             │
│    │ VOLUME  │    │  SOURCE / PRESETS   │        │    │ TUNING  │  ← HUD      │
│    │   62    │    │  overlays center    │        │    │ 98.5MHz │    bottom:24│
│    └─────────┘    └─────────────────────┘        │    └─────────┘             │
└───────────────────────────────────────────────────────────────────────────────┘
     ▲ 240px          ▲ 720px         ▲ 1200px        ▲ 1680px
     │                │               │               │
    ╭─╮              ╭─╮             ╭─╮             ╭─╮
    │◉│ VOLUME       │◉│ SOURCE      │◉│ PRESETS     │◉│ TUNING   ← cabinet face
    ╰─╯              ╰─╯             ╰─╯             ╰─╯
```

Card chrome, shared: 360 px wide, `--surface-overlay` at ~92% opacity with `backdrop-filter: blur(12px)`, `1px solid var(--surface-separator)`, 10 px radius, `padding: var(--sp-4)`. (This project has no `--radius-*` or `--shadow-*` tokens — radii are per-component literals — so a literal `10px` matching `.nav-pill` is the consistent choice.)

Label row, shared: `--font-mono`, 11 px, uppercase, `letter-spacing: 0.20em`, `--text-low` — the same treatment as `.sleep-screen-hint` and `.rcp-presets-hint`.

**Color rule: the HUD borrows the color of the surface it mirrors.** Frequency is amber because the frequency well is amber. Source rows carry their own source accent because the topbar bubbles do. Preset rows follow `PresetCard`. Volume has no existing colored surface, so its numerals are `--text-high` and only the fill bar is cyan.

### 6.3 Volume

```
┌──────────────────────────────────────┐
│ VOLUME                               │   label: mono 11px, --text-low
│                                      │
│   62        ████████████░░░░░░░░     │   numerals: --font-led (Orbitron)
│             ▲                        │             64px/700, --text-high,
└──────────────────────────────────────┘             tabular-nums
              fill --accent-primary, track --surface-separator, 6px tall
```

Muted variant: numerals drop to `--text-low`, the fill renders as an unfilled `--signal-red` outline, and a `MUTED` chip in `--signal-red` sits right of the label.

Long-press variant: from 300 ms a `--accent-primary` ring draws around the numerals, completing at 600 ms, label switching to `HOLD FOR STANDBY`. Releasing early collapses the ring and fires mute.

### 6.4 Tuning

**Radio** — suppressed when `RadioControlPanel` is the visible center panel, because the frequency well already shows this live at 43 px; two identical readouts 400 px apart is noise. Everywhere else:

```
┌──────────────────────────────────────────┐
│ TUNING                    FM · STEP 200k │
│                                          │
│        98.5 MHz                          │   reuse .display-frequency verbatim:
│        ▂▄▆█▆▄▂  ▏SCANNING UP             │   --font-display 43px/600,
└──────────────────────────────────────────┘   --signal-amber, letter-spacing 3px
```

**Track mode** — always shown.

```
┌──────────────────────────────────────┐
│ TRACK                        4 / 17  │
│  Boulevard of Broken Dreams          │   title: --text-high, 20px, ellipsis
│  Green Day · American Idiot          │   artist: --text-medium, 14px
└──────────────────────────────────────┘
```

### 6.5 Timings

| Event | Value | Why |
|---|---|---|
| Appear | 200 ms, `.snackbar-enter` | Existing unused primitive |
| Hold after last detent | **1500 ms** | Long enough to read a two-digit number after the hand stops; short enough not to camp on the visualizer |
| Dismiss | 200 ms, `.snackbar-exit` | Same primitive |
| Re-arm | Any new detent resets the 1500 ms timer without re-animating | Continuous turning shows one stable card |
| Long-press ring | Starts 300 ms, completes 600 ms | The first 300 ms is indistinguishable from a click |
| Selector overlay idle dismiss | **4000 ms**, nothing committed | §6.6 |
| Mute chip | Persistent while muted | State, not event |
| `prefers-reduced-motion` | Enter/exit instant; bars still move; ring becomes a filling bar | Matches `RdsScrollMarquee` and `.sleep-screen-drift` |

### 6.6 The two selector overlays — SOURCE and PRESETS

**One pattern, two lists.** The knobs are adjacent and behave identically, so their overlays are siblings — same geometry, same commit rule, same dismissal, same footer grammar.

Centered in the content area, ~440 px wide, `--surface-overlay` + `blur(12px)`, `1px solid var(--surface-separator)`, 12 px radius. Not a modal — no backdrop dimming, no focus trap, no Escape requirement. A heads-up list the machine forgets about on its own.

```
SOURCE — State A: open, previewing (nothing has changed yet)

        ┌────────────────────────────────────────┐
        │ SOURCE                                 │
        ├────────────────────────────────────────┤
        │ ▌ ((•)) FM              98.5 MHz    ◀  │  ← CURRENT tracks the active
        │   ((•)) AM               1010 kHz      │     BAND, not just "Radio"
        │ ▌  ⌁    BLUETOOTH    Mark's iPhone     │  ← HIGHLIGHT: 2px left bar in
        │    ◉    PHONO                          │     that source's accent +
        │    ⌸    USB                            │     --surface-hover background
        │    ♪    FILES                          │
        ├────────────────────────────────────────┤
        │ PRESS THE KNOB TO SWITCH               │  ← mono 11px, --text-low
        └────────────────────────────────────────┘

PRESETS — State A: open, previewing

        ┌────────────────────────────────────────┐
        │ PRESETS                       4 saved  │
        ├────────────────────────────────────────┤
        │   01  KEXP Seattle              90.3   │  ← PresetCard field hierarchy:
        │ ▌ 02  Classic Vinyl Rock       105.1   │     name primary --text-high,
        │   03  KQED Public Radio         88.5   │     freq secondary mono dim
        │   04  The Bridge                90.9   │
        ├────────────────────────────────────────┤
        │ PRESS TO PLAY · HOLD TO SAVE           │
        └────────────────────────────────────────┘

PRESETS — State B: empty (a fresh install teaches itself)

        ┌────────────────────────────────────────┐
        │ PRESETS                       0 saved  │
        │        NO STATIONS SAVED               │  --text-low
        │   hold this knob to save what's        │
        │   playing                              │
        └────────────────────────────────────────┘

State C — an entry is unavailable

        │    ⌁    BLUETOOTH · no device paired   │  ← whole row --text-low, reusing
        │  ((•))  FM · no tuner detected         │     SourceBubble's " · offline" idiom
        ...pressing it flashes the row border --signal-amber for 1500 ms and leaves
        the overlay open. Never a silent no-op.

State D — committed, switch in flight (a real source change only — a band change
          within the active radio source is instant and skips this state)

        │    ⌁    Switching to Bluetooth…    ◐   │  ← spinner; card stays up until
                                                      success or failure

State E — the switch failed

        │  ⚠ Bluetooth unavailable               │  --signal-amber
        │    Staying on FM 98.5                  │  --text-medium, 4000 ms

PRESETS — State F: save, on a long-press

        │    Saved to 05                         │  --text-high
        │    KQED Public Radio · 88.5 FM         │  --text-medium, 2000 ms

        …bank full:      PRESETS FULL / Replace a slot on screen   (amber)
        …non-radio (v1): Only radio stations can be saved          (1500 ms)
```

Source rows are `SourceBubble` instances at 48 px; preset rows follow `PresetCard`'s grid at 44 px. Seven rows plus chrome fits comfortably inside the 600 px content area.

**States D and E are not optional polish.** A Bluetooth switch on this box can take seconds or fail — the `autoSwitchOnConnect` bug's home territory. An overlay that dismisses on press and leaves the user in silence with the old source still playing is how a person concludes the knob is broken and starts pressing it repeatedly, which is precisely the input pattern that provokes the capture-lifecycle bug.

**Auto-commit on dwell was considered and rejected** for both overlays. It would help a guest who turns and walks away — but it converts every accidental brush into a real source change 1.2 seconds later, from across the room, with nobody touching anything. **Explicit press only**, with the footer line carrying the discoverability dwell would have bought.

### 6.7 The persistent mute indicator

Mute currently shows as a single icon glyph inside `NowPlayingPanel`, which exists only in Home's left rail. On `/queue`, `/metrics`, `/devices`, `/history`, `/phone` there is **no mute indication at all**. A muted console with no visible reason is indistinguishable from a broken one.

Add a `.topbar-mute-chip` to the primary topbar row, right of the OUT cluster: `volume_off` glyph + `MUTED` in `--font-mono` 11 px uppercase, `letter-spacing: 0.20em`, `--signal-red`, 1 px `--signal-red` border at 30% mix. Visible on every route the topbar is on. Tapping it unmutes. On the sleep screen the equivalent is a dim `MUTED` line above `.sleep-screen-hint` (§8.6).

### 6.8 Throttling is a requirement, not an optimization

`PollIntervalMs = 10` with no rate limiting means a fast spin can drive up to 100 state changes per second, each fanning out over SignalR to a Blazor Server circuit that re-renders a component tree. **This box is an Intel N100 on WiFi where audio distortion already correlates with incidental CPU load** (§2.2 defect 4).

- Coalesce encoder-driven state broadcasts to **≥ 50 ms (20 Hz)**, trailing-edge, always emitting the final value so the resting state is never stale.
- The **audio action itself is not throttled** — volume applies per event at full rate. Only the *broadcast and render* are coalesced. The ear leads; the screen catches up.
- Apply volume changes with a **60–80 ms** linear ramp per applied step to avoid zipper noise.
- The diagnostics poll (§7.9) is bound by the same rule: 2 Hz, and only while its card is open.

### 6.9 Tokens

**No new design tokens.** Everything resolves to: `--surface-overlay`, `--surface-separator`, `--surface-hover`, `--text-high`, `--text-medium`, `--text-low`, `--accent-primary`, `--signal-amber`, `--signal-red`, the five `--source-*` accents, `--font-led`, `--font-display`, `--font-mono`, `--sp-1`…`--sp-4`, `--touch-min`, and the `--anim-duration-*` / `--anim-ease-*` set. Builder must not add `--hud-*` anything.

---

## 7. Presence, configuration, and what the owner sees

### 7.1 The protocol surface

Recorded here because Rev 2 referred to reports loosely and D4 supplied the full set. Planner should treat the RotaryUsb integration doc as authority and this table as the design's dependency list.

| Report | Direction | Purpose | Used by this design for |
|---|---|---|---|
| Input (state) | device → host | int32 position + int32 accumulator per encoder, button states | Every turn and press. **Accumulator is what the host reads** (§5.1). |
| **`0x02`** config | host → device | Per-encoder `min` / `max` / `step_size` / `wrap` / `reverse` + three acceleration tiers | The §5.2 push, on every detect and reconnect |
| **`0x03`** commands (2 bytes) | host → device | `0x01` save config to flash · `0x02` factory reset · `0x03` reset positions to min · `0x04` read config back · `0x05` zero diagnostics counters | `0x04` verification (§7.6) · `0x03` handshake reset (§5.1) · `0x01` owner-initiated Save (§7.7) · `0x05` the Diagnostics reset button (§7.9) |
| Diagnostics | device → host | Edge counts, invalid-transition counts, detent counts | §7.9 |

**Command `0x02` (factory reset) is never sent automatically, ever.** It would wipe the flashed configuration and leave the device on defaults where one volume detent spans the full range (§5.4). If it is exposed at all it belongs behind an Advanced disclosure and a typed confirmation, and §7.8 does not include it.

### 7.2 The governing rule

> **The app owns the configuration. The device holds a copy of it, and a copy is never trusted.**
>
> **No write is considered applied until read-back matches it field for field.** The device silently rejects values it does not like; a write without a verified read-back is not a write, it is a hope.

### 7.3 Presence — auto-detect and graceful degradation (NEW)

The owner's requirement: *"auto-detect whether the encoders are available and have the system respond appropriately."* This replaces the old opt-in posture, in which `RotaryEncoderOptions.Enabled` defaults `false` and the integration is off until someone edits a config file — which is incompatible with D1's "the knobs ship live."

**`Enabled` flips to default `true` and changes meaning**: it is no longer a gate that must be opened, it is an escape hatch for disabling a misbehaving encoder without crawling behind the furniture. When it is `false`, presence detection is silent about everything — the owner turned the knobs off on purpose and must not be nagged.

**Does the touchscreen UI change when knobs are present versus absent?**

> **No — with exactly one behavioral exception.**

Every function the knobs perform has a touch equivalent by construction: the volume slider, the source strip, the preset bank, the tuner panel. **Absent knobs cost convenience, not capability**, so there is no degraded mode to design and no "the knobs are missing" chrome to add. Adding hints like *"or turn the SOURCE knob"* would clutter the UI when knobs are present and lie when they are absent.

The one exception is not chrome, it is safety: **panel blanking requires present knobs** (§8.5).

Four events, and what each does:

| Event | What happens | What the owner sees |
|---|---|---|
| **Boot, device present** | Handshake: `0x03/0x03` reset positions → `0x02` config push → `0x03/0x04` read-back verify → baseline the first sample (§5.1) | **Nothing.** Status card reads `Configured`. |
| **Boot, device absent** | Reconnect loop runs at `ReconnectDelayMs`. Touch UI fully functional. **Blanking disabled.** | **A persistent nav-pill badge and a status-card row — but no toast.** The owner is most likely standing at the cabinet having just installed or unplugged something; a toast on boot violates §7.4's silence rule for a state they may already know about. The badge persists until resolved, so it cannot be missed. |
| **Appears mid-session** | Full handshake, silently. Badge clears. Blanking becomes available again. | **A toast — but only if we had reported it absent.** *"Knobs connected."* Announce a recovery only for a fault you announced; otherwise stay silent. |
| **Disappears mid-session** — a USB lead knocked loose inside furniture is a real event | Tear down in-flight knob state: **dismiss any open SOURCE/PRESETS overlay without committing** (an overlay you can no longer navigate is a trap), cancel any long-press ring, dismiss the HUD. **If the panel is blanked, unblank immediately and stop blanking** (§8.5). Reconnect loop starts. | **A toast**, because this is genuinely surprising and may land mid-interaction: *"Knobs disconnected. Touch controls still work."* Plus the persistent badge. |

**On every reconnect, the first sample is a baseline and is discarded** (§5.1). This is the rule that prevents the worst version of the loose-lead scenario: someone bumps the cabinet, the lead re-seats, and forty detents of accumulated movement arrive as one delta on the volume knob.

### 7.4 What the owner sees on a normal boot: **nothing**

On a healthy boot the entire handshake is **silent**. No toast, no splash, no "Configuring encoders…" banner.

1. **This is furniture.** It should be on and working. A cabinet that narrates its own initialization is a computer wearing a walnut case.
2. **A status message for a thing that always succeeds trains people to ignore status messages** — and then the one time it matters, it scrolls past unread.
3. **The project already decided this.** `HANDOFF-kiosk-desktop-launcher.md` §5.1 establishes that the repair path is **silent when nothing needed fixing**, and speaks only when something did. Same shape of decision, same answer.

Where it *is* visible, for someone who goes looking: **System Config → Integrations → Rotary Encoders** (`SystemConfigPage.razor:1440-1478`):

```
  Connection:    [ Connected ]
  Configuration: [ Configured ]   verified 2026-08-19 07:14:02
  Saved to device: 2026-08-19 07:20  · matches current design ✓
  VID/PID:       0xCAFE:0x4005
```

### 7.5 The configuration window — and why the host clamps exist

Between USB detect and a verified config, the device runs **whatever is in its flash**, which on a fresh, replaced or factory-reset Pico means defaults — including **volume acceleration at ×50, where one detent is silence to full** (§5.4). Not theoretical: it happens on every boot and every reconnect.

- **The knobs stay live throughout.** A knob that ignores you for two seconds is a broken knob, and this window can coincide exactly with someone walking up to a just-powered console.
- **The host clamps (§5.2) are in force from the very first report**, independent of what the device believes. That is what makes the window safe.
- **The first sample per encoder is a baseline, not an input** (§5.1).
- **Target: verified within 2 s of detect.** If it has not verified by then the status card switches to `Configuring…` — and still nothing appears on Home.

### 7.6 When something disagrees — one tiered model

**Not all mismatches are equal**, and treating them the same is the mistake to avoid. A wrong acceleration tier is a knob that feels off. A wrong `wrap` on volume is a knob that can blast the room. Presence joins the same table as a fourth tier rather than becoming a second model.

| Tier | Trigger | Response | Owner-visible? |
|---|---|---|---|
| **Configured** | Present, read-back matches | — | Status card only |
| **Transient** | Mismatch or no response, attempts 1–3 | Silent retry, backoff **250 ms / 1 s / 3 s** | **No.** A USB peripheral missing a report on the first try is ordinary. |
| **Degraded** | Still mismatched after 3 attempts on a *feel* field (any acceleration tier, `step_size`) | Knobs stay live on host clamps. Acceleration **treated as absent** rather than assumed present. | **Yes** — amber badge, one toast |
| **Hard fault** | Mismatch on a *safety* field — `wrap` on VOLUME, or `reverse` on any knob | Volume host clamp drops to **±2 per event** until a verified push succeeds | **Yes** — red badge, one toast |
| **Absent** (§7.3) | Device not detected | Touch UI unchanged and complete. **Blanking disabled.** | **Yes** — amber badge; toast only on mid-session loss |

Three surfaces, in the order the owner encounters them:

1. **A cross-route fault badge on the topbar nav pill** — the owner will not be sitting in System Config when this happens. Reuse the pattern `HANDOFF-bell-failure-surfacing.md` §3.7 established for exactly this problem. Amber for degraded/absent, `--signal-red` for hard fault.
2. **One notification, once per session, per transition.** Radzen `NotificationService`, the same channel as the preset-delete undo (`RadioControlPanel.razor:1366-1373`). Plain register:
   - Degraded: **"Knob settings couldn't be applied. The knobs still work, but they may feel wrong."** → *Open encoder settings*
   - Hard fault: **"Knob safety settings couldn't be applied. Volume is limited until this is fixed."** → *Open encoder settings*
   - Absent, mid-session: **"Knobs disconnected. Touch controls still work."**
   - **A fault that flaps must not become a notification storm.**
3. **The status card, with the field-level detail** — the only place sent-vs-read-back data belongs (§7.8).

**Never silently retry forever, and never silently accept.** A knob configured with stale bounds that reports itself fine is the worst outcome available here — the same failure class as `TuningStepKHz` (§2.2 defect 1): a thing that claims to be set and is not.

### 7.7 Flash — what Save writes, and why the "safe baseline" is gone

**Rev 2 specified a deliberately duller "safe baseline" in flash than the operating configuration. Rev 3 drops it.** The owner's Save request is what exposed the flaw, and the flaw is real:

> **A Save button that writes something other than what the screen shows is exactly the class of lie this project keeps shipping.**

But honesty was not the only problem — the baseline was also **solving a problem that was already solved**. Its purpose was to make the boot window and any app-down period safe. The boot window is already made safe by the host clamp (§7.5), which lives in the app and bounds volume regardless of what the device believes. And the *operating* configuration is itself the safe one — T3 disabled on volume, ceiling ×3 (§5.4). The dangerous configuration was never the operating config; it was only ever the **factory** default. Flashing the real config is therefore already the large safety win, and dulling it further bought a rounding error at the cost of a dishonest button.

**The policy:**

- **Push fresh on every boot** (§7.5). The app remains the source of truth.
- **Flash holds exactly the operating configuration** — the same values §5.2 specifies and the same values the Settings screen displays.
- **The app never writes flash automatically.** Not on boot, not on config change, not on reconnect. Flash has finite write cycles and an app that writes it on startup will destroy it inside a boot loop. **Owner-initiated only.**
- **`reverse` must agree between flash and the operating config** — a knob that turns backwards during the boot window is worse than one that accelerates slowly.

The residual risk the baseline was guarding against — **a stale flash config outliving the fix that replaced it** — is handled by making staleness *visible* instead of preventing it by dulling. That is strictly better, because it is both honest and diagnosable:

```
  Saved to device: 2026-06-01 · differs from current design ⚠   [ Save to device ]
```

### 7.8 The Settings surface — "configuration and Save" (D21)

The owner asked for the device's configuration and an explicit Save in the app. **Rev 2 said "delete the editors." That was too blunt, and this section is the reconciliation.** The owner is asking for a *capability*, not necessarily 24 numeric inputs — and the distinction between *seeing* the configuration and *hand-editing* it is where the answer lives.

**System Config → Integrations → Rotary Encoders**, four cards:

1. **Status** — connection, configuration tier (§7.6), last verified, last saved-to-device with the staleness comparison from §7.7.

2. **Configuration — read-only, complete, with the comparison always on.** All fields, all four encoders, labeled by **cabinet name** (`VOLUME · SOURCE · PRESETS · TUNING`), never by index. This *is* "configuration in the app": the owner can see exactly what the knobs are running and whether the device agrees.

```
  ┌──────────┬──────┬───────┬──────┬────────┬──────────┬──────────┐
  │ Encoder  │ step │ wrap  │ rev  │   T1   │    T2    │    T3    │
  ├──────────┼──────┼───────┼──────┼────────┼──────────┼──────────┤
  │ VOLUME   │  2   │ false │ off  │ 150×2  │  80×3    │   off    │  ✓ device agrees
  │ SOURCE   │  1   │ false │ off  │  off   │   off    │   off    │  ✓
  │ PRESETS  │  1   │ false │ off  │  off   │   off    │   off    │  ✓
  │ TUNING   │  1   │ false │ off  │ 150×2  │  80×4    │  40×8    │  ⚠ T3 reads 0
  └──────────┴──────┴───────┴──────┴────────┴──────────┴──────────┘
```

   **Why read-only rather than editable.** These numbers are a designed feel, derived against the actual FM channel grid (§5.5) and a volume-safety budget (§5.4) — not preferences. Twenty-four numeric inputs invites setting volume T3 to ×50 and then filing a bug about a volume slam. The owner gets full visibility, which is what "have the configuration in the app" actually needs to mean; what they do not get is a loaded footgun with no labels on it. **If the owner does want direct editing, §13 Q3 puts it back to them** — my recommendation is that they do not, and that anything genuinely needing adjustment should come back to this document instead so the reasoning moves with the number.

3. **The one editable thing — `Reverse direction`, four toggles.** It depends on how the cabinet was wired, not on taste, and a backwards knob is intolerable and un-diagnosable by a normal person. Toggling one **pushes immediately (`0x02` + verify) and marks the flashed copy stale**, so the Save button lights up.

4. **Actions.**

| Button | Sends | Confirm? | Copy |
|---|---|---|---|
| **Save to device** | `0x02` config, verify, then `0x03/0x01` flash | No — it writes exactly what is displayed | *"Saves the settings above to the knobs so they work the same way even if the app is restarting."* |
| **Re-apply settings** | `0x02` + verify only, no flash | No | Recovery from a Degraded tier without touching flash |
| **Reset counters** | `0x03/0x05` | No | §7.9 |

  **The Save copy is the load-bearing part.** It says what it writes, and because §7.7 dropped the baseline, what it writes is what is on screen. If a future revision reintroduces any divergence, this copy has to change with it, and the reviewer should treat a mismatch here as a defect (see this project's pre-merge rule on comments and messages that overclaim).

  **Factory reset (`0x03/0x02`) is not on this page.** It would put the device on defaults where one volume detent spans the full range.

**Should `VolumeStepPercent` and `TuningStepKHz` still be deleted?**

- **`TuningStepKHz` — yes, delete outright.** Nothing reads it and nothing should; the tuner owns its own step (§2.2 defect 1). D21 does not change this — it is not device configuration, it is a field that never did anything.
- **`VolumeStepPercent` — not deleted, *relocated*.** It is a genuine device field (VOLUME `step_size`), so it now appears in card 2, read-only, in one place. What goes away is the **duplicate editable numeric on the Integrations tab**, which was a second source of truth for a value the device also holds. One value, one place, visible.

### 7.9 Diagnostics — re-argued after D5

**D5 removed this card's original headline job.** Rev 2 justified it primarily as the tool that resolves *"does one detent report one count or four?"* — and the firmware now answers that. So the honest question is whether it still earns a card. **It does, on three remaining merits**, and it is smaller than Rev 2's version:

1. **Wiring sanity, which nothing else can see.** The invalid-transition rate is the only signal that distinguishes "this encoder is noisy" from "this encoder is fine" before it becomes an intermittent fault someone chases for a week.
2. **Confirming D3's index-order guarantee** after any hardware work — turn the leftmost knob, see which row moves (§5.0).
3. **Measuring detents per revolution**, the one open mechanical unknown left in §5.2 — a single full turn answers it and pins the feel figures.

**Where:** a Diagnostics card in the same Settings tab. Not on Home, not in the topbar. **Polling: 2 Hz, only while the card is open** (§6.8) — a background diagnostic poll is exactly the incidental load that correlates with audio distortion on this box.

```
  DIAGNOSTICS                                        [ Reset counters ]

  ┌──────────┬─────────┬────────┬────────────┬──────────────────────────┐
  │ Encoder  │ Detents │ Edges  │ Edges per  │ Wiring                   │
  │          │         │        │ detent     │ (invalid per 1k edges)   │
  ├──────────┼─────────┼────────┼────────────┼──────────────────────────┤
  │ VOLUME   │   1,204 │  4,816 │ 4.00 ✓     │ 0.2  OK                  │
  │ SOURCE   │     318 │  1,272 │ 4.00 ✓     │ 0.0  OK                  │
  │ PRESETS  │     297 │  1,188 │ 4.00 ✓     │ 0.0  OK                  │
  │ TUNING   │   2,881 │ 11,530 │ 3.86 ⚠     │ 6.4  Marginal — check    │
  │          │         │        │            │      this encoder's      │
  │          │         │        │            │      wiring / shielding  │
  └──────────┴─────────┴────────┴────────────┴──────────────────────────┘

  [ Calibrate a knob ▸ ]
```

- **Edges-per-detent changes job, not value.** It was a calibration input; it is now a **wiring health signal**. It should read exactly `4.00`; anything else means edges are being dropped or invented, which is a hardware problem — not a reason to re-derive §5.
- **Invalid transitions are shown as a *rate*, never a total.** A raw total is meaningless without uptime — 400 is fine after a year and alarming after a minute. Thresholds: **< 1 = OK**, **1–10 = Marginal**, **> 10 = Faulty**.
- **The card says what the pattern means**, because a diagnostic nobody can interpret is decoration: *one* encoder high → that encoder's wiring or shielding; *all four* high → a shared ground or supply problem, look at the Pico's power and the cable run, not the knobs.
- **`Calibrate a knob`** — zero the counters (`0x03/0x05`), prompt **"Turn the VOLUME knob exactly one full revolution, slowly"**, then report **detents per revolution**, edges-per-detent, and which encoder index moved. Fifteen seconds, and it settles §13 Q2, confirms D3 after any hardware work, and works the same way on a replacement Pico years from now.
- **One glanceable summary on the status card** so the detail stays optional: `Encoders: 4 connected · wiring OK` or `· TUNING wiring marginal`.

---

## 8. Sleep, wake, and the dark panel

### 8.1 The collision D8 creates, stated plainly

Rev 2's rule was **"volume acts in place; everything else wakes."** D8 says **"any button press or knob movement is a wake signal"** and re-enables hardware blanking. Those two sentences disagree about what a volume turn does, and papering over it would leave Planner to guess.

They disagree because they were written about **different states**. Rev 2's rule was written for the two *software* sleep states, where the panel is lit and showing a clock. D8's instruction is about the *physically dark* panel — a state that did not exist when Rev 2 was written. Once that is seen, the two collapse into one model with a criterion the user can always perceive.

### 8.2 The model — three states, one visible criterion

> ### **Rule 1 — the light rule. If the panel is dark, the first input does exactly one thing: light it. It is consumed. It changes no audio, no station, and never resumes from Standby.**
>
> ### **Rule 2 — the lit rule. Once lit: VOLUME acts in place and does not change screens. Everything else wakes to the full UI and is consumed.**

**The criterion is the panel's own light**, and that is the whole reason this resolves cleanly. The coordinator asked how the user knows which state they are in when the panel is dark. **They always know, because the distinguishing feature is literally whether they can see anything.** A dark panel is visibly dark; a lit clock is visibly lit. There is no hidden state, nothing to learn, and it matches the behavior of every phone in the house: dark screen, first press wakes; lit screen, press acts.

| State | Panel | Audio | Entered by |
|---|---|---|---|
| **Awake** | lit, full UI | playing | any wake |
| **Ambient** | lit, dim clock + weather (`/sleep`) | **playing** | 30 min idle (`idle-dimmer.js`) |
| **Ambient-Dark** | **blanked** | **playing** | +10 min further idle |
| **Standby** | lit, dim clock, "off" hint | **paused + muted** | VOLUME long-press · topbar Sleep pill · API |
| **Standby-Dark** | **blanked** | paused + muted | 60 s after entering Standby |

### 8.3 What each input does, in each state

| Input | Awake | **Ambient** (lit) | **Ambient-Dark** | **Standby** (lit) | **Standby-Dark** |
|---|---|---|---|---|---|
| **VOLUME turn** | acts | **acts in place**, dim readout, stays on the clock | **lights → Ambient. Consumed.** Shows current volume. | consumed (nothing to adjust) | **lights → Standby. Consumed.** |
| **VOLUME press** | mute | **acts in place** | lights, consumed | **resumes → Awake** | lights, consumed |
| **VOLUME hold 600 ms** | → Standby | → Standby | lights, consumed (does **not** enter Standby) | **wakes → Awake** | lights, consumed |
| **SOURCE / PRESETS / TUNING turn** | acts | **wakes → Awake.** Consumed. | **lights → Ambient. Consumed.** Shows that knob's current value. | consumed | **lights → Standby. Consumed.** |
| **SOURCE / PRESETS / TUNING press** | acts | **wakes → Awake.** Consumed. | lights → Ambient, consumed | **resumes → Awake** | lights, consumed |
| **Screen touch** | acts | wakes → Awake | lights → Ambient | resumes → Awake | lights → Standby |

**A consumed input still answers a question.** When a wake input is consumed, the HUD renders **that knob's current value without changing it** — turn VOLUME in the dark and the panel lights showing `VOLUME 62`; turn TUNING and it shows the current frequency; turn SOURCE or PRESETS and it shows what is currently selected (`SOURCE · FM`), not the full overlay. **The first detent tells you where you are; the second one moves it.** This converts the consumed input from a loss into an answer, which is most of what somebody wants at 2 a.m. anyway.

**Waking from dark lands on Ambient, never on the full bright UI.** This is the refinement that makes "any input wakes" survivable at night: a 2 a.m. volume nudge produces a dim clock and a volume readout for a few seconds, not a 1920×720 wall of Home. **Re-blank after 60 s of no input from Ambient, 30 s from Standby.**

### 8.4 The 2 a.m. case, honestly — and where I narrowed the owner's instruction

**The case I raised in Rev 1 and must now answer against D8:** the panel is dark, music is playing, someone nudges volume expecting volume.

Under this model they get: the panel lights dim, showing `VOLUME 62`, and **the volume does not change on that first detent.** The second detent changes it. The cost is one detent — two percentage points — and the compensation is that they can now see what they are doing before doing it. I think that is the right trade in the dark, and it is the owner's stated instruction, so it stands.

**One place where I have narrowed D8, deliberately, and want the owner to override me if they disagree** — §13 Q1:

> D8 says any press or movement is a wake signal. Taken literally at the audio layer, that means **a knob brushed at 3 a.m. resumes a console the owner deliberately put into Standby**, and the radio starts playing in a dark house. So in this model, a **turn** from Standby lights the panel only; **resuming audio requires a press** (any button) or a screen tap.

Rule 1 already honors D8 for the panel — *every* input wakes the display. The narrowing is only about restarting audio, and only from Standby, and only for turns. A turn is what a passing sleeve does; a press is what a person does. If the owner would rather have the literal version, it is one line in the router.

### 8.5 Blanking — the precondition and the brick risk

Rev 2 flagged this and D8 approved blanking anyway, so the risk now needs a mitigation rather than a warning.

`SleepService.cs:84-87` disabled DPMS because **touch-to-wake does not work when the compositor blanks input.** If that is still true, then after blanking there is exactly one wake path — the encoders — and losing the USB while dark leaves a panel that cannot be woken without a keyboard, inside a piece of furniture.

**Three mitigations, in order of importance:**

1. **A hard precondition: verify touch-wake on the box before blanking ships.** If touch still cannot wake a blanked panel, blanking has a single point of failure and should not be enabled until it has two. This is a gate, not a caveat.
2. **Blanking requires present, verified knobs (§7.3).** The app must not blank when the encoder device is absent, and if the device **disappears while the panel is blanked, unblank immediately and stop blanking** until it returns. Failing toward light is the correct direction: a screen left on is a nuisance, a screen that cannot be turned on is a service call.
3. **Document the recovery where someone will find it at 2 a.m.** — `SleepService` already shells the exact call needed, so put it in `INTEGRATIONS.md` alongside the encoder section:
   `ssh mmack@radio` → `gdbus call --session --dest org.gnome.ScreenSaver --object-path /org/gnome/ScreenSaver --method org.gnome.ScreenSaver.SetActive false`

**Two implementation notes for Planner, carried from Rev 2:**
- **The wake must consume exactly one event, not a window.** `TryWakeFromSleep` currently calls `WakeAsync` fire-and-forget (`:121`) and returns true; with a 10 ms poll several more events arrive before `IsSleeping` flips, and each is silently discarded. Latch it synchronously so precisely one input is spent waking. **Rule 1 makes this more important, not less** — under blanking, a fast spin in the dark must lose one detent, not twelve.
- No knob input should restart the idle countdown while on `/sleep` — `idle-dimmer.js:46-50` already declines to run timers on that route, and that is correct.

### 8.6 The Ambient readout

Rendered inside `Sleep.razor`, within the anti-burn-in drift wrapper, replacing the alternating clock/weather composition for its lifetime and restoring it afterward.

```
                    ┌───────────────────────┐
                    │  VOLUME               │  mono 11px, uppercase,
                    │                       │  letter-spacing 0.20em,
                    │       62              │  --text-low
                    │                       │
                    │  ████████████░░░░░    │  fill: dim amber
                    └───────────────────────┘

                       tap anywhere to wake      ← .sleep-screen-hint, unchanged
```

- Numerals: `--font-led` (Orbitron) 700, **96 px**, `color-mix(in srgb, var(--signal-amber) 35%, #050507)` with the 12 px amber `text-shadow` — **byte-identical to `.sleep-screen-clock`** (`design-system.css:2898-2919`), exactly as `HANDOFF-sleep-weather-visual-redesign.md` §4 requires of the temperature glyphs. The volume number *is* the clock's typography, wearing a different label.
- **The sleep screen keeps its one emissive color.** No cyan, no red, not even for mute — muted is `MUTED` in `--text-low` above the hint, and the standby hold ring is dim amber. This is the load-bearing aesthetic rule of that handoff and I am not breaking it for an indicator.
- **In Standby the hint changes** to `hold VOLUME or press any knob to turn on`, because in that state a tap does something different from a turn (§8.3) and the screen is the only place that can say so.

---

## 9. Physical layout — approved, final (D2)

**This section is now a build document.** The rationale is retained as the record of why, not as an argument still being made.

### 9.1 The order

> ### **VOLUME · SOURCE · PRESETS · TUNING**

*(Rev 1 proposed `VOLUME · TONE · SOURCE · TUNING`. When Tone was withdrawn, its "left pair = how it sounds, right pair = what you hear" rationale went with it, and the layout was re-derived below rather than having the replacement jammed into slot 2. Nothing had been drilled.)*

### 9.2 Why this order

> **The outer two knobs *act*. The inner two knobs *choose*.**

1. **The two knobs that take effect the instant you turn them are at opposite ends.** VOLUME and TUNING are the most-used, the most consequential, and the only two whose turn changes something immediately. Three knob-widths of cabinet between them makes the one genuinely costly mis-grab — blasting the room versus losing the station — nearly impossible.
2. **The two knobs in the middle are both preview-only**, so every mis-grab in the middle of the panel is *free*. Grab SOURCE when you meant PRESETS and an overlay opens showing where you are; nothing changes; it dismisses itself in four seconds. This is the interaction design paying for the layout, and it is why two similar-feeling knobs can safely sit adjacent.
3. **The size difference encodes behavior, not just identity.** Big knobs act. Small knobs choose. The panel tells you which knobs are consequential by feel.
4. **Left-to-right reads coarse → fine on the content side.** SOURCE picks the band or input, PRESETS picks a saved point, TUNING moves continuously from there.
5. **It remains period-plausible.** Volume far left and tuning far right is the dominant American console arrangement; one middle knob is still a selector, and the other is the preset bank — which is what the tone slot became when radios got memories.

| Meant → grabbed | What happens | Cost |
|---|---|---|
| Volume → Source | An overlay opens; **nothing changes** | **Free** |
| Source → Volume | Level changes; instantly audible and self-correcting | Trivial |
| Source ↔ Presets | The other overlay opens; **nothing changes** | **Free** |
| Presets → Tuning | Station drifts | Recoverable — visible, and turning back the same number of detents lands on the same channel exactly |
| Tuning → Presets | An overlay opens; **nothing changes** | **Free** |
| Volume ↔ Tuning | *Requires reaching across the whole panel* | Effectively prevented by geometry |

### 9.3 The face

```
        ╔═══════════════════════════════════════════════════════════════════╗
        ║          1920 × 720 display  (behind glass / in the dial bezel)   ║
        ╚═══════════════════════════════════════════════════════════════════╝

              ╭───╮          ╭──╮      ╭──╮           ╭───╮
             ╱ ▓▓▓ ╲        │▒▒│      │▒▒│           ╱ ▓▓▓ ╲
            │  ▓▓▓  │       │▒▒│      │▒▒│          │  ▓▓▓  │
             ╲ ▓▓▓ ╱        │▒▒│      │▒▒│           ╲ ▓▓▓ ╱
              ╰───╯          ╰──╯      ╰──╯           ╰───╯
             VOLUME         SOURCE   PRESETS          TUNING
              42mm           28mm      28mm            42mm
            knurled         smooth   smooth +        deep-fluted
                                     ring groove
             ── acts ──    ── choose ──── choose ──   ── acts ──
                │             │          │              │
                └──── 90mm ───┴── 70mm ──┴──── 90mm ────┘
```

### 9.4 Dimensions

| Spec | Value | Why |
|---|---|---|
| Pitch, outer → inner | **90 mm** | Physically separates the knobs that take effect on turn from the two that only preview. Grouping by spacing is the cheapest legibility win on a panel with no labels visible in the dark. |
| Pitch, inner pair | **70 mm** | Tight enough to read as a pair; still clears an adult hand around a 28 mm knob. **70 mm is the floor anywhere on the panel.** |
| Volume / Tuning diameter | **40–45 mm** | Most-used, most precise, and the two that act |
| Source / Presets diameter | **25–30 mm** | The size difference *is* the "this one only chooses" cue, in the dark |
| Knurling | Volume lightly knurled · Tuning **deeply fluted** · Source and Presets smooth | Four knobs identifiable by fingertip with the lights off — the most important accessibility feature on this device |
| Distinguishing the two smooth knobs | **PRESETS carries a ring groove** | Same size, same finish; one tactile difference keeps them apart without breaking the pair reading |
| Horizontal placement | Centered on the display's four quarters (240 / 720 / 1200 / 1680 px equivalents) | Makes §6.2's HUD mapping literal rather than learned |
| Height | Same row, comfortable standing reach | The knobs are the standing surface; the touchscreen is the leaning-in surface |

**Do not use pointer or "chicken-head" knobs.** A pointer promises an absolute position and end stops. These are endless encoders with neither — a pointer at 3 o'clock while the volume is at 20% lies about the machine's state every time you look at it. Radially symmetric knobs, knurl and groove patterns, no indexing mark.

### 9.5 Engraving

**VOLUME · SOURCE · PRESETS · TUNING**, in the cabinet's own period typography (D10). `TUNING` not `DIAL` — the word on the original and the word a guest reads. `PRESETS` per the owner's choice, **and the on-screen bank is retitled to match** (§4.4) — a panel engraved PRESETS above a screen that says MEMORY is a mismatch nobody can edit away later.

---

## 10. Accessibility and safety

### 10.1 Mis-grab

Covered structurally in §9.2 — the two acting knobs at opposite ends behind 90 mm gaps, the two previewing knobs paired in the middle where mis-grabs cost nothing, tactile differentiation by size, knurl and ring groove, 70 mm floor. **Under this layout there is no mis-grab that changes an audible state without also putting something on screen that says so.**

### 10.2 Volume slam

The most plausible way this machine could hurt someone. Six independent guards, because device configuration can be lost, stale, or unverified:

1. `wrap = false` — a wrapping volume knob puts full scale one detent past silence.
2. **T3 disabled** on volume — the fastest tier is ×3, not ×50.
3. **Host clamp `|delta| ≤ 6`** in the router, applied regardless of what the device sends — what makes an unconfigured or factory-reset Pico *sluggish* rather than *dangerous*, including during the boot window (§7.5).
4. **A `wrap` read-back mismatch is a hard fault** that drops the clamp to ±2 and says so on the topbar (§7.6).
5. **The first sample after any connect is a baseline, not an input** (§5.1) — so a lead knocked loose and re-seated cannot deliver forty detents of accumulated turning as one delta.
6. 60–80 ms ramp per applied step (§6.8) — no clicks, no discontinuities.

Minimum 1.33 s of deliberate spinning from silence to full, and less than that only ever in the safe direction.

### 10.3 Startling changes

- **Source:** preview-then-commit, no auto-commit on dwell, explicit in-flight and failure states, and a **150 ms fade-out / fade-in around the actual switch** so the transition is a transition and not a pop. A band change within an active radio source skips the fade because it is not a source switch.
- **Preset recall** uses the same fade when it has to change source.
- **Mute and Standby** are the two startling presses, both on VOLUME, both instantly legible, both reversed by repeating the identical gesture (§4.5).
- **Waking from a dark panel** never jumps to the full bright UI (§8.3).

### 10.4 "Is it broken?"

The five silences this design removes:
- Turning volume while muted → **unmutes** instead of silently moving a number.
- Muted on a non-Home route → **persistent topbar chip** (§6.7) instead of nothing.
- Turning the source knob → **an overlay** (§6.6) instead of an invisible counter.
- Knobs configured wrong → **a visible tiered fault** (§7.6) instead of a knob that just feels off for months.
- Knobs unplugged → **a badge and, mid-session, a toast** (§7.3) instead of four dead controls and no explanation.

### 10.5 Low vision, dark rooms, and screen readers

- The knobs are themselves the accessible path: they work from across the room, in the dark, without reading a 1920×720 panel. Tactile differentiation (§9.4) is what makes that true for four knobs instead of one.
- HUD numerals are large — volume at 64 px on the main UI, 96 px on the sleep screen — because the reading distance is *the whole room*.
- `role="status"` `aria-live="polite"` with a text mirror (`"Volume 62 percent"`, `"Preset 2, Classic Vinyl Rock, 105.1"`, `"Bluetooth, no device paired"`), matching `RdsScrollMarquee`'s existing screen-reader mirror. **AT-SPI works on this box**, so this is testable rather than aspirational.
- Never color alone: mute is a glyph plus the word; overlay highlights are a left bar plus a background shift; unavailable entries carry a reason string; config states carry a word (`Configured`, `Degraded`, `Absent`) and not just a badge color.
- **A blanked panel is itself an accessibility consideration** — Rule 1 (§8.2) means nobody has to guess whether an input registered, because the panel lighting *is* the acknowledgement.

### 10.6 Things no knob may do

- **No knob may power off or reboot the box.** The volume long-press is *Standby* — a display and audio state a second long-press reverses.
- **No knob may delete or overwrite anything.** PRESETS' long-press saves to a free slot and refuses when the bank is full; replacement and deletion stay on the touchscreen behind the existing kebab and its undo toast.
- **No knob may send audio out of the room** (why Output/Cast was rejected — §4.3).
- **No knob may trigger a factory reset of the encoder device** (§7.1).
- **No destructive action is reachable by turning alone.** Every state change requires a press or a deliberate 600 ms hold.

---

## 11. Capability preservation — where visualization goes

1. **The six-segment picker stays** — `VisualizerPanel.razor:34-71` (`VU · Wave · Spectrum · Fall · Ring · Phase`).
2. **Tap the visualizer canvas** → advance to the next mode. A 710 px-wide target, discoverable by the universal instinct to poke at the moving thing.
3. **Long-press the canvas (600 ms)** → the mode list, so a person can jump rather than cycle.
4. **System Config keeps its dropdown**, unchanged.
5. **Fold in the §2.2 defect 3 fix**: `VisualizerPanel` must subscribe to `VisualizationModeChanged`, or items 2–4 can still disagree with server state.

`VisualizationModeService` and its API surface are untouched — this is purely a change in which input drives it.

---

## 12. Dependencies and escalations

### 12.1 For Architect

1. **New SignalR events.** Volume and mute already reach the browser (`AudioStateUpdateService.cs:463-473`). **Source preview**, **preset preview** (neither is a commit) and **encoder presence + config state** need new events, following the `VisualizationModeChanged` precedent (`:969-978`). Whether *ephemeral preview state* belongs on the audio hub at all is an architecture call — it is UI-only state on a hub that otherwise carries audio truth. The presence/config event is different in kind: durable health, closer to the phone system-status pattern.
2. **Band-as-source-entry (D7).** The source overlay now commits either an `AudioSourceType` or a `RadioBand`, and the "current" marker must resolve across both. Whether that is one heterogeneous list model or two lists rendered as one is a modeling question. **It also needs per-band last-tuned-frequency recall** — if that state does not exist, it is a small addition to `RadioPreferences`.
3. **Cross-source presets (v2).** §4.4 ships v1 against the existing radio preset bank. Letting a slot point at a playlist, a Bluetooth device, or "the record player" needs a favorites model that does not exist. v1 does not block on it.
4. **Encoder presence and configuration ownership.** §7 makes the app responsible for a peripheral's runtime state, with presence detection, a verify-and-fault loop, and a flash policy. Where that lives and how its health reaches the API are architecture questions. **The UX contract is fixed by §7** — silent when healthy, tiered when not, never trusted without read-back, and never blanking the panel without a live wake source.

### 12.2 For Planner — host-side work this spec creates

| Item | Detail |
|---|---|
| **Protocol reconciliation — do this first** | `HidRotaryEncoderService.ParseReport` (`:174-203`) reads an 8-byte sbyte-delta report; the device speaks int32 positions/accumulators plus the §7.1 config, command and diagnostics reports. **Nothing in §7 can be built until this lands.** |
| Presence detection | Boot-present / boot-absent / appears / disappears (§7.3), folded into the §7.6 tier table. `Enabled` defaults `true` and becomes an escape hatch. |
| **Reconnect baselining** | **First sample per encoder after any connect is a baseline and is discarded.** Never diff across a disconnect (§5.1). This is a correctness *and* safety requirement. |
| Handshake | `0x03/0x03` reset positions → `0x02` push → `0x03/0x04` verify field-for-field → retry 250 ms / 1 s / 3 s → tier the fault by field (§7.6). |
| Flash | Owner-initiated only, writes **exactly the operating config**, never automatic (§7.7). Staleness surfaced in the UI. |
| Diagnostics | 2 Hz, only while the card is open. Compute edges-per-detent and invalid-per-1k-edges; never surface raw totals. `Calibrate` measures detents per revolution (§7.9). |
| **Blanking** | Re-enable DPMS **gated on** verified touch-wake and on present knobs; unblank and stop blanking if the device disappears while dark (§8.5). |
| Three-state wake model | Rule 1 / Rule 2 (§8.2), the per-state table (§8.3), consumed-input-still-renders-a-value, wake-from-dark lands on Ambient, re-blank 60 s / 30 s. |
| Wake latch | Consume exactly one event, synchronously (§8.5). |
| Long-press synthesis | 600 ms (reuse `RadioControlPanel.LongPressThresholdMs`). Short branch on release; long branch **at** the threshold while held. Two consumers: volume→standby, presets→save. |
| Progress-ring feedback | 300 ms → 600 ms. *(Optional fold-in: the existing band-pill and preset-row long-presses have no hold feedback either — one component fixes all three.)* |
| Band-edge sticky stop | Stop at the active band's edge; fresh detent after ≥300 ms to wrap. |
| Track-mode delta collapse | `\|delta\| → 1`, 300 ms debounce. |
| Per-event host clamps | §5.2, from the first report, never trusting device flash. |
| Broadcast coalescing | ≥50 ms trailing-edge (§6.8). |
| Volume ramp / source fade | 60–80 ms per step; 150 ms fade around a real source switch (not a band change). |
| Unmute-on-turn | §4.4. |
| **Rename** | `MEMORY` → `PRESETS` everywhere, **including `RadioControlPanel`'s existing bank header** (§4.4). |
| Settings surgery | **Delete** `TuningStepKHz`; **relocate** `VolumeStepPercent` into the read-only config table; **add** four `Reverse` toggles labeled by cabinet name; **add** Save / Re-apply / Reset-counters actions (§7.8). |
| §2.2 defects | Dead `TuningStepKHz`; stale UI mapping table (serve from the router); missing `VisualizerPanel` subscription; `INTEGRATIONS.md`'s now-wrong "swap the A/B pins" advice, plus the §8.5 recovery command. |
| ROADMAP | Still **no roadmap row for physical input**. This is now at least three PRs: protocol + presence/config, mapping + HUD, blanking + wake model. |

---

## 13. Open questions for the owner

Closed by D1–D21: the Tone fork, encoder index order, set-position, detents-per-count, band folding, blanking approval, engraving, flash policy, and the Settings surface. Four remain.

1. **Do you want the literal version of D8, or my narrowed one?** (§8.4) I have made a **turn** from Standby light the panel but *not* resume audio; resuming needs a **press** or a screen tap. The literal reading of *"any knob movement is a wake signal"* would have a sleeve brushing a knob at 3 a.m. start the radio in a dark house. Rule 1 honors D8 fully for the *panel*; this narrowing is only about restarting *audio*, only from Standby, only for turns. One line in the router either way.
2. **Detents per revolution** — the last mechanical unknown (§5.2). It does not affect any count, only the "≈2.5 revolutions to cross the FM band" feel figures. One full turn on the `Calibrate` flow answers it once the knobs are in hand.
3. **Do you actually want to hand-edit the 24 config fields?** (§7.8) I have given you full read-only visibility plus the four `Reverse` toggles plus Save, which I believe is what "configuration and save" needs to mean. If you want direct numeric editing as well, say so — my recommendation is that a number needing adjustment should come back to this document so the reasoning moves with it, rather than drifting silently on a settings page.
4. **A configurable maximum volume ceiling?** A guest/child guard. Trivial to add, easy to forget about and then be confused by. I still lean no.

### 13.5 Where a decision overrode me — and what I'd want you to know

**D8 (blanking + any input wakes).** I recommended against blanking in Rev 2 on the grounds that losing the encoder USB while dark could brick the display. That risk did not disappear when blanking was approved — it moved from a warning to §8.5's three mitigations, and **the first one is a gate I would not skip: verify that touch can wake a blanked panel before this ships.** If touch cannot, the console has exactly one wake path, inside furniture, and the recovery is an SSH session. The two coupling rules (no blanking without present knobs; unblank immediately if they vanish) are what make it safe enough to ship, and they are not optional trim.

The cost you are accepting knowingly is one detent: at 2 a.m., the first volume nudge lights the panel and shows you where you are, and the second one moves it (§8.4).

**D21 (Save in Settings).** Your question improved the design, and I want that on the record rather than buried: my Rev 2 "safe baseline" would have made the Save button write something duller than the screen showed. It was defending the boot window — which the host clamp already defends — at the cost of a button that lies. **It is dropped**, flash now holds exactly what you see, and staleness is surfaced instead of prevented (§7.7).

The one thing I held back on is the numeric editor (§13 Q3). Not because it is hard, but because these numbers are a designed feel with a safety budget behind them, and a field that can be set to ×50 will eventually be set to ×50.

**D10 (PRESETS).** No trade — the word is better on furniture. It did produce one consequence worth confirming: **the on-screen bank is renamed too**, which is a small deviation from an existing handoff (§4.4). Leaving them mismatched would have been worse than either name.

---

## 14. Out of scope

- The RotaryUsb firmware itself. This spec configures the device; it does not change it.
- Any change to `VisualizationModeService`, the visualizer renderers, or the audio pipeline. **No new audio DSP is proposed anywhere.**
- Cross-source presets (v2) — escalated in §12.1, not designed here.
- The touchscreen source strip, `NowPlayingPanel`, `RadioControlPanel`, and the preset bank — reused and (in one header) renamed, not redesigned.
- Cabinet joinery, escutcheon fabrication, and shaft hardware. §9.4 gives the dimensions the UX depends on.
- Multi-turn or absolute-position knobs, motorized knobs, pointer knobs (§9.4).

---

## 15. Acceptance criteria (for Tester)

**Presence and configuration (§7)**
- [ ] Cold boot, device present: **nothing appears on Home, on any route, or as a notification.** Status card reads `Configured` within 2 s of detect.
- [ ] Cold boot, device absent: nav-pill badge and status card show it; **no toast**; every touch control still works; **the panel never blanks**.
- [ ] Plug the device in mid-session: handshake runs silently, badge clears, and a `Knobs connected` toast appears **only because absence had been reported**.
- [ ] Unplug mid-session: toast appears, badge persists, any open overlay **dismisses without committing**, any long-press ring cancels, and a blanked panel unblanks.
- [ ] **Turn a knob ~50 detents while unplugged, then replug: volume does not jump.** The first sample is baselined, not applied. *(This is the single most important safety test in this document.)*
- [ ] Force a read-back mismatch on an acceleration tier: amber `Degraded`, badge, exactly one toast, knobs still work.
- [ ] Force a mismatch on VOLUME `wrap`: red, volume clamp drops to ±2, toast says volume is limited.
- [ ] A flapping fault produces **one** notification per session, not a storm.
- [ ] Across 10 boots, the app issues **zero** flash writes.
- [ ] `Save to device` writes **exactly the configuration shown on screen** — read it back and diff. The status line then reads `matches current design ✓`.
- [ ] Change a `Reverse` toggle: it pushes immediately, and the saved-to-device line goes stale until Save is pressed.
- [ ] With the device unconfigured (factory defaults live), a fast volume spin still cannot exceed the host clamp.

**Sleep, wake, blanking (§8)**
- [ ] **Touch wakes a blanked panel.** *(If this fails, blanking must not ship — §8.5.)*
- [ ] Panel dark + music playing: the first volume detent **lights the panel to Ambient, shows the current volume, and does not change it.** The second detent changes it.
- [ ] Panel dark: waking never lands on the full bright UI — always Ambient.
- [ ] Ambient (lit) + music playing: volume **acts in place** and does not navigate home.
- [ ] Ambient (lit): SOURCE / PRESETS / TUNING wake to the full UI and change nothing.
- [ ] Standby: a **turn** lights the panel and does **not** resume audio; a **press** resumes and restores the pre-sleep mute state.
- [ ] Ambient re-blanks after 60 s of no input; Standby after 30 s.
- [ ] Exactly **one** encoder event is consumed by a wake — a fast spin in the dark loses one detent, not twelve.
- [ ] Unplug the encoders while the panel is blanked: the panel unblanks and stays lit.

**Safety**
- [ ] From volume 0, spin as fast as physically possible: reaching 100 takes **≥ 1.3 s**, and no single detent moves volume more than 6 points.
- [ ] Turning volume down past 0 does not wrap to 100. Ever.
- [ ] No knob or gesture can power off the box or factory-reset the encoder device.
- [ ] PRESETS' long-press never overwrites an occupied slot; with a full bank it writes nothing and says so.

**Buttons — the Action model**
- [ ] No press on any knob changes what turning that knob subsequently does. Verified on all four.
- [ ] Pressing SOURCE or PRESETS with the overlay closed opens it and **changes nothing audible**.
- [ ] TUNING's press does seek on radio and play/pause on Bluetooth, with no user-toggled state in between.
- [ ] Every audible press is reversed by the same knob.

**Source list with bands (D7)**
- [ ] Committing `AM` while already on FM is a **band change** — no spinner, no fade, no source teardown — and restores AM's last-tuned frequency.
- [ ] Committing `FM` from Bluetooth performs a full source switch with the fade and the spinner.
- [ ] On AM, the overlay's current marker is on **row 2**, not row 1.
- [ ] Changing band from the on-screen pills moves the overlay's marker, and vice versa.
- [ ] With no tuner hardware, FM/AM render dimmed with `no tuner detected` and pressing one flashes amber rather than doing nothing.
- [ ] TUNING's band-edge stop uses the **active** band's edges.

**Naming (D10)**
- [ ] The engraving, the overlay title, the HUD copy, the empty state, the screen-reader text, **and `RadioControlPanel`'s bank header** all say `PRESETS`. No surface says `MEMORY`.

**Feedback**
- [ ] Every knob produces a visible change **within 100 ms** of its first acted-upon input, on Home, on `/queue`, and on `/sleep`.
- [ ] Turning SOURCE or PRESETS changes nothing until pressed; waiting 4 s dismisses with no change.
- [ ] Committing an unavailable source shows State E, not silence.
- [ ] Preset recall from Bluetooth switches source and band, tunes, and plays.
- [ ] An empty preset bank shows the instructional empty state.
- [ ] Muting on `/metrics` shows the topbar `MUTED` chip.
- [ ] Turning volume while muted **unmutes and applies the delta**.

**Feel**
- [ ] A slow FM turn moves exactly one channel per detent.
- [ ] A firm sweep crosses 88.1 → 107.9 in **≈2.5 revolutions** on a 20-detent knob (±20%); a hard flick in **≈0.6**. *(If the knobs are not 20-detent, record the measured value from §7.9 and re-check the ratio, not the absolute.)*
- [ ] The band edge stops the first time it is reached and wraps only on a fresh detent.
- [ ] SOURCE and PRESETS each move exactly one entry per detent at every spin speed.
- [ ] In track mode, a hard dial spin advances **one** track, not eight.

**Load**
- [ ] Spin volume continuously for 30 s while audio plays: no distortion, SignalR volume traffic at or under 20 Hz.
- [ ] Leaving the Diagnostics card open for 10 minutes produces no audible distortion.

**Accessibility**
- [ ] AT-SPI reports the HUD's live-region text for each knob.
- [ ] Every state — including the five config tiers — is distinguishable without color.
- [ ] With the lights off, a first-time user identifies all four knobs by touch alone, and can tell SOURCE from PRESETS by the ring groove.

---

## Hand-off summary

**For the owner:** the escutcheon is settled — **VOLUME · SOURCE · PRESETS · TUNING** — and Rev 3 folds in every decision. The knob is renamed to PRESETS everywhere, **including the on-screen bank**, so the panel and the screen say the same word. Bands are now first-class entries in the SOURCE list, so the knob really is a band selector. **D5 resolved the riskiest assumption in the document** — the firmware handles detents, so the 4× uncertainty is gone and only a cosmetic detents-per-revolution figure remains. **D4 surfaced a hazard I had missed**: without re-baselining on reconnect, a lead knocked loose and re-seated delivers every accumulated detent as one delta — §5.1 fixes it and §15 tests it. **D8's blanking is resolved by one visible criterion** — if the panel is dark the first input lights it and is consumed; if it is lit, volume acts in place. **D21 killed my "safe baseline"**, correctly: Save now writes exactly what the screen shows. Four questions remain in §13, and §13.5 is the honest account of the two places a decision overrode me and what that costs.

**For Architect:** §12.1 — hub events for preview/presence/config, the band-as-source-entry model and per-band frequency recall, cross-source presets (v2), and where responsibility for a peripheral's runtime state lives.

**For Planner:** protocol reconciliation (§12.2) gates all of §7 and must land first. §5.2 is the literal config; §7.1 is the command set. Three tests carry disproportionate weight: **replug-after-turning** (§15), **touch-wakes-a-blanked-panel** (§15, a ship gate), and **Save writes what is displayed** (§15).

**Not approved. Designer-phase draft, Rev 3, pending owner review.**
