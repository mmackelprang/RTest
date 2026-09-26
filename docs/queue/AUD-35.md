# `AUD-35` — the identification loop busy-spins a CPU core whenever there is nothing to identify

[← Builder Queue index](../BUILDER_QUEUE.md)

🔴 **P1.** Filed 2026-09-26. Suspected by `AUD-33`'s adversarial reviewer from the code; **measured on the
box the same hour.**

## What was observed

`top` on `radio`, 75 min after a deploy, BT source selected, **nothing playing**:

```
PID     %CPU  TIME+     COMMAND
721837  100.0 72:23.58  Radio.API          (72 CPU-minutes in 75 wall-minutes)
724435   99.9 25:45.03  .NET TP Worker     (top -H: one thread-pool thread)
```

On a 4-core N100 where CPU and I/O contention already correlate with audible distortion.

## Mechanism

`BackgroundIdentificationService.ExecuteAsync` (`:204-209`) skips the idle wait whenever
`_consecutiveSongRecFailures == 0` ("the capture duration already throttles"). But
`IdentifyCurrentAudioAsync` returns **before** capturing when the tap is inactive or the source does not
need a lookup (`:254-264`) — synchronously, with no `await` reached. The loop then `continue`s straight
back: a synchronous tight loop (scope creation, dedup cleanup, `StatusChanged`) that never yields.

`NeedsFingerprintingLookup` is false most of the time on the appliance — BT clears it after every
identification until the next AVRCP change — so this is the steady state, not an edge case.

## Fix direction

When a cycle does no capture, wait an idle interval before the next one, on the same `_delayCts` that
`RequestImmediateIdentification` already cancels — so a track change still starts an identification
immediately.
