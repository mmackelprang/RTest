# `ENC-24` — long-press the VOLUME knob for normal sleep; a short press keeps its action

[← Builder Queue index](../BUILDER_QUEUE.md)

🚧 **BUILT 2026-10-02, HELD for owner panel UAT with the knob** — branch `feat/enc-23-24-sleep-gestures`,
one PR with [`ENC-23`](ENC-23.md). **Not deployed, not merged.**

🟡 **P2 — owner request 2026-10-02.** Filed and built by the Builder.

> *"... Long pressing the volume knob shoudl put the console in "normal" sleep mode."*

(The full request is quoted in [`ENC-23`](ENC-23.md).)

## Finding: the gesture already existed

**VOLUME long-press → Standby has been in the router since `ENC-4`/`ENC-6`** (`RotaryEncoderActionRouter.OnLongPress`
case 0 → `EnterSleepAsync`, the same Standby the SLEEP pill's tap enters), and the **short press (mute on /
off) already fires on release**, not on press-down (`EncoderLongPressGesture`). So **the press feel does not
change** in this row. The owner may simply not have found it: the Standby screen's hint
(`hold VOLUME or press any knob to turn on`) is the only place the panel mentions it.

## What changed

- **A turn while the knob is held no longer reads as a hold.** Before, holding VOLUME while adjusting it
  dropped the console into Standby at 600 ms, and a press-and-turn released early toggled mute.
  `EncoderLongPressGesture.OnTurn(index, delta)` now cancels the hold — neither the long nor the short action
  fires, and the HUD ring collapses — **once the turns since press-down net to 2 or more detents**
  (`TurnCancelDetents`). One stray detent does not cancel, and a ±1 wobble nets to zero: the HID parser raises
  the button edge before the turn from the same report, so a push that clicks the knob one detent off-axis
  would otherwise cancel every hold (review M2). The turns still act (volume moves).
- **A late threshold callback from an earlier press can no longer fire into a newer one** (review L6): each
  press-down bumps a generation that the timer callback captures.
- Applies to every knob with a hold (VOLUME and PRESETS); SOURCE and TUNING have none.

## Decisions

| Question | Chosen |
|---|---|
| Long-press when already in Standby | **Wakes** — at press-down, through the existing Standby gate (any press resumes); it does not re-enter Standby. Matches the encoder handoff §4: *"While in Standby, a long-press wakes."* |
| Long-press on the lit Ambient clock (idle path) | Enters Standby (VOLUME acts in place on Ambient). |
| Long-press on a dark panel | Lights it only (timer-dark) or lights and wakes (`ENC-23` deep sleep). |
| Configurability | **Fixed** — the `ENC-8` surface is read-only. |

## Owner checks (panel, with the knob)

1. Hold VOLUME ~1 s → the ring fills, the console goes to the sleep (Standby) screen; mute not toggled.
2. Short press VOLUME → mute toggles on release, as before.
3. Hold VOLUME and turn it two or more detents while holding → the volume changes, no sleep, no mute toggle.
4. Ten deliberate short presses and five holds on VOLUME → count any that did nothing (a miss would point at
   the 2-detent tolerance).
