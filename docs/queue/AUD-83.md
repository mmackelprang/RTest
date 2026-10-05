# `AUD-83` — with no phone connected, the Bluetooth source reports `Playing` and its position runs

[← Builder Queue index](../BUILDER_QUEUE.md)

🟢 **P3.** Filed 2026-09-30 from the agent pre-pass of 2026-09-29.

## Provenance

Found during the agent pre-pass of 2026-09-29 ~22:47–22:57 EDT while verifying `UI-17`'s centre panel against the box (`7dd34b5`): [`RETURN-CHECKLIST.md`](../../archive/uat/RETURN-CHECKLIST.md) evening batch §A, "Centre panel (`UI-17`)", minor findings. No `HANDOFF-GA-PUNCH-LIST.md` counterpart.

## What was observed

At ~22:52 EDT, with no phone connected, the Bluetooth source was selected: the panel's position counter ran (observed at 0:04), and the API reported `activeSource.state = Playing` with `bluetoothDevices: []`. The panel correctly opened on CONNECT.

## Hypothesis (not established)

`AUD-29` ([#724](https://github.com/mmackelprang/RTest/pull/724)) made `BluetoothAudioSource.Position` extrapolate from the last AVRCP anchor while the source is `Playing`. If the source can be `Playing` with no device, that extrapolation ticks with nothing behind it. Two separate questions follow, and the answer may be either or both:

1. Why is the source `Playing` with no device? A state that says `Playing` over no audio is the defect class `AUD-12` and `AUD-25` belong to.
2. Should the extrapolation require a connected device, whatever the state says?

## Scope questions for the plan

- Reproduce first: select Bluetooth with no phone connected and read `/api/audio/nowplaying` and the source state twice, a few seconds apart.
- Establish which layer reports `Playing` (the source's own state, or a projection of it) before choosing a fix.
- `AUD-29`'s owner UAT passed on 2026-09-30 with a phone connected; a fix here must not change that path.

## Verification

With no phone connected and Bluetooth selected, the source is not `Playing` (or at least the position does not advance). With a phone connected and playing, the position still advances as `AUD-29` requires.
