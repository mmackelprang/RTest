# `ENC-24` — long-press the VOLUME knob for normal sleep; a short press keeps its action

[← Builder Queue index](../BUILDER_QUEUE.md)

🚧 **IN FLIGHT 2026-10-02** — branch `feat/enc-23-24-sleep-gestures`, one PR with `ENC-23`. Not deployed.

🟡 **P2 — owner request 2026-10-02.** Filed and built by the Builder.

> *"... Long pressing the volume knob shoudl put the console in "normal" sleep mode."*

(The full request is quoted in [`ENC-23`](ENC-23.md).)

## Scope

- **Long-press VOLUME** → normal sleep, the same state the SLEEP pill's tap enters.
- **A short press keeps its current action** (mute on / off), and fires on release.
- **Turning the knob while it is held must not read as a long-press** (or as a short press).

Details, decisions and evidence are filled in by the PR.
