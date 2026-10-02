# `ENC-23` — long-press SLEEP for deep sleep (sleep + panel off); a VOLUME press wakes it

[← Builder Queue index](../BUILDER_QUEUE.md)

🚧 **IN FLIGHT 2026-10-02** — branch `feat/enc-23-24-sleep-gestures`, one PR with `ENC-24`. Not deployed.

🟠 **P1 — owner request 2026-10-02.** Filed and built by the Builder.

> *"Two other UX improvements we should consider - long pressing the 'Sleep' button in the upper right
> hand corner of the console should put the LCD in off/low-power/deep-sleep mode. Clicking the 'Volume'
> knob should wake it back up. Long pressing the volume knob shoudl put the console in "normal" sleep
> mode."*

The first two sentences are this row; the third is [`ENC-24`](ENC-24.md).

## Scope

- **Long-press the topbar SLEEP pill** → the normal sleep (Standby: audio paused and muted, `/sleep` on
  screen) **and** the panel powered off at once, through `ENC-22`'s `PanelPowerService` — not a second
  mechanism. Works with `Sleep:PanelOffAfterMinutes = 0` (the box's setting).
- **A short tap on SLEEP is unchanged.**
- **A VOLUME knob press wakes it** — panel on and out of sleep — and that press is consumed: it does not
  toggle mute.
- `ENC-22`'s safety rules apply unchanged: never dark without a connected, stable encoder.

Details, decisions and evidence are filled in by the PR.
