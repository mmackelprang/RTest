# `ENC-23` — long-press SLEEP for deep sleep (sleep + panel off); a VOLUME press wakes it

[← Builder Queue index](../BUILDER_QUEUE.md)

🚧 **BUILT 2026-10-02, HELD for owner panel UAT with the knob** — branch `feat/enc-23-24-sleep-gestures`,
[#772](https://github.com/mmackelprang/RTest/pull/772), one PR with [`ENC-24`](ENC-24.md). **Not deployed, not merged.**

🟠 **P1 — owner request 2026-10-02.** Filed and built by the Builder.

> *"Two other UX improvements we should consider - long pressing the 'Sleep' button in the upper right
> hand corner of the console should put the LCD in off/low-power/deep-sleep mode. Clicking the 'Volume'
> knob should wake it back up. Long pressing the volume knob shoudl put the console in "normal" sleep
> mode."*

The first two sentences are this row; the third is [`ENC-24`](ENC-24.md).

## What changed

- **Hold the topbar SLEEP pill for 600 ms** (`SleepPill.razor`, new) → `POST /api/system/sleep`
  `{ sleep: true, panelOff: true }` → `SleepService.EnterSleepAsync` (the ordinary Standby: audio paused and
  muted, `/sleep` on screen), **then** `PanelPowerService.PowerOffNow`: the panel is powered off at once
  through `ENC-22`'s own pump and Mutter `PowerSaveMode` — no second mechanism. It works with
  `Sleep:PanelOffAfterMinutes = 0`, which is what `radio` runs today (start-up log line, 2026-10-02 09:40:
  *"power-off after sleep is off (Sleep:PanelOffAfterMinutes = 0)"* — although
  [`RETURN-CHECKLIST.md`](../uat/RETURN-CHECKLIST.md) §4.3 records it set to 10 on 2026-09-29, it is not
  in effect now).
- **A short tap on SLEEP is unchanged** (and so is keyboard activation).
- **Wake: a VOLUME press** on the deep-dark panel lights it **and** leaves sleep in the same press
  (`TryClaimWake` + `WakeAsync("encoder-deep-sleep")`; audio and the pre-sleep mute state come back
  through the ordinary wake). The press is **consumed**: it never reaches the gesture, so its release is
  dropped as an orphan and **mute is not toggled**, and holding it past 600 ms does not re-enter Standby.
- **Every other input** on the deep-dark panel (a turn, or another knob's press) only lights it, onto the
  Standby sleep screen — `ENC-22`'s light rule, unchanged; a press from there wakes as usual. The ENC-22
  **timer**-darkened panel keeps exactly its old behaviour (a VOLUME press only lights it).
- **Safety rule 1 applies unchanged, refused rather than deferred:** no encoder, or one connected for less
  than `EncoderStableSeconds`, and the panel stays on (sleep is still entered). The API answers
  `panelOff` + `panelOffResult` (`PoweredOff` / `AlreadyOff` / `RefusedEncoderNotConnected` /
  `RefusedEncoderNotStable` / `Unavailable`); the Web does not show a refusal on screen.
- **`IPanelPowerService.OnEncoderInput`** now returns `PanelInputOutcome` (`Pass`, `LitPanel`,
  `LitPanelFromDeepSleep`, `ConsumedInGrace`) instead of `bool`. A private `_deepSleep` mark is cleared by the
  single `RequestOnLocked` helper that every panel-on path goes through, so it cannot survive into a later
  timer-dark cycle.

## Decisions (Builder's, for the owner to overrule)

| Question | Chosen | Why |
|---|---|---|
| What a VOLUME press wakes to | **The full UI** (out of sleep) | Owner: *"Clicking the 'Volume' knob should wake it back up."* Lighting only onto the sleep screen would need a second press for the same intent. |
| Other knobs on a deep-dark panel | Light only (Standby screen) | Keeps `ENC-22`'s light rule; never reduces the wake paths. |
| Threshold | **600 ms** (`EncoderInteractionTimings.LongPressThresholdMs`) | The one long-press value the app uses everywhere — band pill, preset card (`UI-20`), knobs. |
| When it fires | **On lift** | Same as the band pill / preset card; and the panel does not go dark under a finger still on the glass. |
| Feedback | Accent fill behind the label from **300 → 600 ms** (40 % mix, solid bottom edge), then the label steps to **SCREEN OFF** | Mirrors the encoder HUD ring's schedule; the label swap is the "let go now" cue, because a hold commits only on lift. The two touch holds before this (band pill, preset card) have no feedback at all — whether this becomes the shared affordance is a Designer call (polish L6, not done here). |
| Configurability | **Fixed** | The `ENC-8` mapping surface is a read-only view of the router's table; no press/hold action is reassignable anywhere. |

Gates, review findings and mutation checks are recorded in the PR description.

## Known limitations (deferred)

- **A wake that produces no sleep-screen edge leaves the panel dark** (review L1). The panel is lit on wake
  by `/sleep` closing; if the kiosk never reached `/sleep` (its hub link down while a LAN client held the
  pill), a REST wake has no such edge. A knob still lights it.
- **The hold is measured on the `Radio.Web` server** between two Blazor events (review L5), as the band pill
  is; a ≥ 600 ms stall between them would turn a tap into deep sleep. Benign and recoverable.
- **The fill's contrast at 40 % has not been measured on the panel.**

## Owner checks (panel, with the knob)

1. Hold SLEEP until it reads **SCREEN OFF**, lift → the panel goes dark (audio paused).
2. Press VOLUME once → the panel comes on (≈ 2 s firmware splash, accepted in `ENC-22`) and the console is
   **awake on the normal UI**; mute is as it was before sleep (not toggled).
3. Tap SLEEP briefly → the ordinary sleep screen, panel stays on (unchanged).
4. Optional: hold SLEEP, then turn a knob instead of pressing → panel lights onto the sleep screen; press
   VOLUME → awake.
