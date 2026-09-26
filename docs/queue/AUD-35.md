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

---

## 🚧 BUILT 2026-09-26 — branch `fix/aud-35-identification-loop-idle-wait`

**Fix:** `IdentifyCurrentAudioAsync` now returns whether it captured samples. When it did not (no tap,
inactive source, no lookup needed, no samples — or the cycle threw), `ExecuteAsync` waits
`FingerprintingOptions.IdlePollIntervalMs` (new, default **1000**) before the next cycle, on the same
`_delayCts` that `RequestImmediateIdentification` already cancels. A cycle that captured still goes straight
round (its capture is the throttle); SongRec back-off is unchanged.

**Cost:** a source that becomes active or starts needing a lookup *without* calling
`RequestImmediateIdentification` waits up to 1 s longer for its first capture. Track changes on File and BT
call it, so they are unaffected.

**Tests** (`BackgroundIdentificationServiceIdleLoopTests`, real `ExecuteAsync` cycles, synchronised on the
tap being polled):
- Red first against the unfixed loop: **629,167 tap polls in 2 s** (bound: ≤ 20). A bounded wall-clock
  window, safe in this direction — starvation can only lower the count.
- `RequestImmediateIdentification` ends a 10-minute idle wait (so the idle wait cannot delay a track change).

**Box check:** after deploy, with nothing playing (or BT playing between identifications),
`top -H -p $(pgrep -f /opt/radio-console/api/Radio.API)` shows no thread pinned near 100 %, and
`Radio.API`'s CPU time stops growing at ~1 core-second per second.

**Adversarial review (session model): nothing blocking.** Fixed in the PR:
- **MEDIUM** — the spin test only exercised the *inactive source* path; reintroducing the spin on the
  steady-state *no lookup needed* path (or *no samples*) stayed green. The test is now a theory over all
  three no-capture paths. Mutation-checked: returning `true` from either of those two paths turns its case
  red (517,889 and 296,394 polls against the bound of 20).
- **LOW** — comments narrowed: a no-capture cycle *may* have returned without awaiting (the no-samples path
  awaited a full capture); a track change that requests identification is delayed by *at most one idle
  interval* (a request can land outside the wait, or before the file player reaches `Playing`).
- **LOW** — the idle floor is 100 ms, not 1 ms, so a mistaken `0` cannot recreate most of the old cost.

**Accepted / follow-up:**
- `UpdatePhase(Idle)` now runs at 1 Hz while idle, rebuilding the status snapshot; the SignalR broadcast of
  it is already throttled to 3 s. Was worse during the spin; optional to skip an unchanged Idle.
- **→ [`AUD-36`](AUD-36.md):** `IdentificationIntervalSeconds` is read by nothing but the start-up log line,
  yet is offered in the UI as "time between identification attempts".
