# `TEST-10` — `NodeArrivesAfterProbe_SwitchesViaEvent` races a wall clock against a wall clock

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-09 when it failed a full-suite run under load during `UX-1`'s merge gate.

## What fails

`tests/Radio.Infrastructure.Tests/Audio/Services/BluetoothAutoSwitchServiceTests.cs`,
`NodeArrivesAfterProbe_SwitchesViaEvent` — **`Expected event subscription active`**.

```csharp
using var svc = CreateService(bt, audioMock, probeMs: 200, maxWaitMs: 5000);
bt.SimulateDeviceConnected("AA:BB:CC:DD:EE:FF");
await Task.Delay(500);                                    // the TEST's clock
Assert.False(counters.SwitchedToBluetooth);
Assert.True(bt.CaptureNodeAvailableSubscriberCount >= 1,  // PRODUCTION's clock
            "Expected event subscription active");
```

## ⭐ This is the exact shape `CLAUDE.md` § *Test Timing* already documents

The test sleeps 500 ms and **assumes the production probe loop (`probeMs: 200`) has since expired and
subscribed**. ⛔ **There is no rendezvous between those two clocks.** Under CPU load one probe interval
stretches past the whole window, the subscription is not yet active, and the assertion fails.

⚠ **It fails in the DANGEROUS direction.** `CLAUDE.md` notes a timing dependency is only hazardous
when starvation can flip the assertion to *fail* — a bounded negative check that starvation merely
weakens is safe. **This one flips to fail**, so it is the hazardous kind.

⭐ **`BluetoothCaptureWatchdogTests` is the worked precedent** (`TEST-4`): it set a value, slept 60 ms,
and assumed a 20 ms poll loop had observed it — **13/200 under CPU saturation**, and once red on
`main` at `2a81f56`. The **test-only** fix parked the watchdog on entry to every poll until the test
granted it, so polls were **counted rather than timed**: 200/200 at the same load. **Read that file's
class-level `<remarks>` before touching this one.**

## Why it is P2 and not P1

- It **passed 3/3 in isolation** and on **three prior full-suite runs** of the same branch.
- The branch it failed on contains **zero `Radio.Infrastructure` files** — nine files of docs, CSS,
  `.gitattributes` and one `Radio.Web.Tests` regex. **It cannot have been caused by that change.**
- ⚠ **But it is NOT in the documented known-failing set**, so it must not decay into background noise
  the way a known flake does. That is the whole reason this row exists.

## The fix

⛔ **Do NOT raise the delay.** `CLAUDE.md`: *"Raising a timeout or adding a sleep converts a flaky test
into a SLOW flaky test; the failure rate drops but never reaches zero."*

**Synchronize on the observation, not on elapsed time.** Either park the service the way `TEST-4`
parked the watchdog, or inject a `TimeProvider` (house idiom — `TimeProvider.System` by default,
`FakeTimeProvider` in the test; see `EncoderHudService`). ⚠ **Advancing a fake clock is only half the
job when the callback is `async`** — a completion rendezvous is still needed before asserting.

⚠ **Check the sibling tests in the same file for the same shape** — the test above it does
`await Task.Delay(150)` against the same probe loop. Fixing one and leaving three is how this returns.

## Verification

⭐ **Assert the fix by REPEAT COUNT UNDER LOAD, not by one green run.** `TEST-4`'s standard was
**200 iterations under CPU saturation**, before and after. A single pass proves nothing here — the
test passes most of the time already, which is precisely the problem.

---

## 🚧 BUILT 2026-09-26 — branch `fix/test-10-autoswitch-probe-rendezvous`

**Test-only fix, as the row said, across the whole file (not just the reported test).** Every
`Task.Delay`-then-assert in `BluetoothAutoSwitchServiceTests` is gone:
- The fake now signals when the service **subscribes** to `CaptureNodeAvailable` and when it removes its
  last handler — which the service does in a `finally`, *after* its switch/abandon decision — and the
  audio-manager mock signals the **switch**. `NodeReadyInsideProbeWindow`, `NodeArrivesAfterProbe` and
  `NodeNeverArrives` await those, with a 30 s safety net that is never the thing being timed.
- The three skip tests and `Dispose_UnsubscribesFromEvent` need **no wait at all**: on every skip path the
  `async void` handler returns before its first `await`, so its decision is complete when
  `SimulateDeviceConnected` returns.
- The class's run time went from ~2.3 s of fixed sleeps plus its timers to ~0.8 s measured (the remaining
  floor is the production probe / max-wait timers the tests configure).

**Meaningfulness, by mutation** (run 2026-09-26, each caught by exactly its target): the service never
switching when the node arrives → `NodeArrivesAfterProbe` fails (at its 30 s safety net); `Dispose` not
unsubscribing → `Dispose_UnsubscribesFromEvent` fails at once.

**Adversarial review (session model): nothing blocking.** Its two comment findings are fixed (the skip tests'
reliance on `IsCaptureNodeAvailable = true`; the safety net must stay shorter than the 60 s max wait).

⚠ **The row's "repeat count under load, before and after" standard was NOT met, and here is exactly why.**
- *Before*, under CPU saturation (64 spinning threads, 200 iterations of the whole class, 9 min):
  **0/200 failures for every test** — CPU pressure alone does not reproduce it.
- A second baseline under **thread-pool starvation** (200 blocked pool threads — the more likely pressure
  in a parallel full-suite run) was **killed by the host for low system memory** before reporting, and was
  not re-run.
- So there is no reproduction of the original failure on record. The fix's claim rests on construction —
  no assertion depends on a sleep any more — plus the mutations above, not on a measured before/after.
