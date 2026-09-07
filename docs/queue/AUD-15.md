# `AUD-15` — the BT audio buffer runs empty, 55 underruns and climbing

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Found 2026-09-07 while investigating a Bluetooth *disconnect* (which turned out to be a
RotaryPhone HFP boundary violation — see below). **This is a different defect and must not be
conflated with that one.**

## The evidence

From the `radio-api` file sink, four samples inside ~32 seconds of ordinary BT playback:

```
11:22:24 [WRN] Buffer underrun (Single): 1 underruns,  574 zero samples in last 13.5s (buffer: 0/384000, total underruns: 47)
11:22:36 [WRN] Buffer underrun (Single): 2 underruns,  488 zero samples in last 11.7s (buffer: 0/384000, total underruns: 49)
11:22:38 [WRN] Buffer underrun (Single): 2 underruns, 1274 zero samples in last  2.3s (buffer: 0/384000, total underruns: 51)
11:22:56 [WRN] Buffer underrun (Single): 4 underruns,  736 zero samples in last 17.4s (buffer: 0/384000, total underruns: 55)
```

**`buffer: 0/384000` on every sample** — the ring buffer is not merely low, it is *empty* at the
moment of measurement. `zero samples` is silence being substituted for audio that did not arrive,
so this is audible: brief dropouts during Bluetooth playback.

The counter climbed **47 → 55 in about 32 seconds**, i.e. this is continuous, not a one-off.

## The likely mechanism — inferred, not proven

Alongside the underruns, the PipeWire capture callback is reporting unhealthy timing:

```
11:22:22 PipeWire OnProcess: count= 9349, interval min= 7.17ms max=14.17ms, bursts=2, execution max= 1.01ms
11:22:32 PipeWire OnProcess: count=10285, interval min= 1.11ms max=21.94ms, bursts=2, execution max= 9.44ms
11:22:42 PipeWire OnProcess: count=11222, interval min= 3.04ms max=19.79ms, bursts=2, execution max=14.20ms
11:22:52 PipeWire OnProcess: count=12160, interval min= 8.23ms max=13.14ms, bursts=2, execution max= 1.97ms
```

**Callback execution reaching 14.2 ms, and inter-callback intervals to 21.9 ms**, against a quantum
that `CLAUDE.md` § *PipeWire Quantum Tuning* documents as 512 frames / 10.67 ms. A callback that
takes longer than the quantum cannot keep the buffer fed.

⚠ **The jump from 1.01 ms to 14.20 ms execution across consecutive samples is the interesting
number**, and it is not explained. `CLAUDE.md` records that this box is resource-constrained and
that load correlates with audible distortion, so CPU contention is the obvious suspect — but
**"obvious suspect" is not a diagnosis, and this row must not start from the assumption.** The
first task is to find out what the callback is doing during a 14 ms execution.

## Deliberately not assumed

- **Not the same as `AUD-10`.** That row is the A2DP *transport* dying on pause. Here the transport
  is healthy and the audio simply arrives too late.
- **Not the RotaryPhone HFP issue.** That was the *disconnect* the owner reported on the same day
  (boundary doc entry 2026-09-07). Same logs, different defect.
- **Not necessarily new.** `total underruns: 55` is cumulative since process start; nothing here
  establishes when it began or whether it correlates with a deploy. Establishing that is cheap and
  worth doing first.

## Scope questions for the plan

1. **What runs inside `OnProcess`?** `MEMORY.md` records a standing constraint: *"Do NOT use
   `PwStreamFlags.RtProcess` — OnProcess callback calls `AddSamples` which takes a lock; blocking
   the RT thread stalls PipeWire."* A lock on the callback path is exactly the shape that produces a
   14 ms execution under contention. Verify what `AddSamples` contends with.
2. **Is the underrun counter's own arithmetic right?** The samples report "1 underruns … total 47"
   then "2 underruns … total 49" — the deltas are consistent here, but a counter that is read in a
   log message is a claim worth checking against the code before it is used as evidence.
3. **What is the actual quantum in effect**, and does it match the 512 / 10.67 ms the docs claim?
   ⚠ `MEMORY.md`: **do not** use `pw-metadata clock.force-quantum` to change it — it disrupts the
   graph and forces BT reconnection.
4. **Does this affect other capture sources** or only Bluetooth? The underrun label says
   `(Single)`, which suggests a specific generator; confirm what that scopes to.

## Verification

Partly measurable off-box: whatever runs in the callback can be profiled or reasoned about in the
repo. But the underruns themselves are a live-box phenomenon and the fix must be confirmed there —
the counter should stop climbing during sustained BT playback.

⚠ Touches the live audio path. **Not auto-mergeable.**
