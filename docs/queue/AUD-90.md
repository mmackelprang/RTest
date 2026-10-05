# `AUD-90` — after a band sweep, `/api/radio/state` reports `gain: 28` with `autoGain: true`

[← Builder Queue index](../BUILDER_QUEUE.md)

🟢 **P4.** Filed 2026-09-30 by the `AUD-76` PR 2 Builder, from the PR 1 Builder's box UAT (deployed
`6c33ec3`). **Observed once, not measured or explained.**

## What was seen

Before a live band sweep (`POST /api/radio/bandmap/scan` while the SDR radio played 92.3),
`GET /api/radio/state` reported `autoGain: true`, `gain: 0`. After the sweep it reported
`autoGain: true`, `gain: 28`.

The live sweep path (`RadioReceiver.SweepChannels`, [`AUD-76`](../../archive/queue/AUD-76.md) PR 1) sets a fixed **28 dB
manual** gain for the measurement and restores the gain *mode* in its `finally`. So 28 is the sweep's
own value. What the state is reporting now is the open question.

**Reproduced 2026-09-30 on `9ee77af` (the `AUD-76` PR 2 UAT).** After a requested live scan,
`/api/radio/state` read `autoGain: true, gain: 28`. The radio panel's AGC readout showed **28.0 dB** next to
AUTO, so the value is user-visible. Switching source and back reset it to `gain: 0`.

## Belief, unverified

The likely cause is cosmetic. `rtlsdr_get_tuner_gain` (or the receiver's cached gain field) still
returns the last **manual** value after the tuner goes back to AGC, and the `gain` field echoes it
while AGC is in fact in control.

The alternative is not cosmetic: the restore put the *flag* back but left the tuner at a fixed 28 dB.
In that case reception after any live sweep would run at manual gain until the next tune or gain change.

## To settle it

1. On the box, with the radio playing and `autoGain: true`, read `/api/radio/state`, request a scan,
   and read it again after the sweep returns.
2. Distinguish the two explanations: compare `rssiDbu` / `signalStrength` and the clip indicator on a
   strong and a weak station before and after. Or read the tuner's gain mode directly; `rtl_test` cannot
   run while the API holds the dongle, so this needs a temporary debug log at the restore point.
3. If it is cosmetic, report `gain` as null (or the AGC value if one can be read) while `autoGain` is
   true. If it is real, restore the tuner's AGC in `SweepChannels`' `finally` explicitly, and add a
   test through the fake device that the gain mode is AGC after a sweep.
