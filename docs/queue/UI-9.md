# `UI-9` — every visit to the config page leaks three event handlers, permanently

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-08 by the `UI-7` Planner, which found it while enumerating subscribers to
`AudioStateHubService` and correctly declined to fold an unrelated defect into that row.

## The defect

`src/Radio.Web/Components/Pages/SystemConfigPage.razor:2219`, `:2229` and `:2239` subscribe to hub
events with **anonymous lambdas**:

```razor
HubService.SomethingChanged += async () => { ... };
```

An anonymous lambda has no stable reference, so **there is no `-=` that can ever remove it**, and
the page has no matching unsubscribe. `AudioStateHubService` is registered `AddSingleton`
(`Program.cs:411`), so it lives for the process.

**Therefore every navigation to `/system` permanently adds three handlers to a
process-lifetime object.** They are never collected: the singleton holds the delegate, the delegate
holds the component, and the component holds its render tree. Ten visits is thirty handlers and ten
retained component graphs; the appliance runs for weeks between restarts.

## Why this may outrank `UI-7`

`UI-7` is about *how* a multicast event awaits its subscribers — a correctness bug in fan-out whose
effect is a lost exception or a starved continuation. **This is an unbounded resource leak on a
long-running appliance**, and it compounds with `UI-7`: each leaked handler is another subscriber
that `UI-7`'s single-await defect then fails to observe. The two are in the same file family and
the same event set, and a Builder in there for one should at least know about the other.

⚠ **They are still separate rows.** `UI-7` is blocked on an owner decision at its §1.1 and this is
not — the fix here is unambiguous. Do not let this wait on that.

## The fix, and the thing that makes it non-trivial

Store each handler in a field and unsubscribe in `IDisposable.Dispose()` / `DisposeAsync`, the
pattern the other components already use — find and match one rather than inventing a shape.

⚠ **Corrected 2026-09-08 — this row originally warned that the page might not implement disposal at
all, and that adding the interface to a "~2,000-line page" would be the hard part. Both were wrong.**
The page **already** declares `@implements IDisposable` at `:15` and has a live `Dispose()` at
`:4192-4197` disposing three timers. There is no interface to add and no lifecycle to negotiate — the
fix is three named methods and three `-=` in the existing `Dispose()`. (The file is **4,214** lines,
not ~2,000.) **The row is smaller than filed: ~1.5–2 h.**

**Enumerate every `+=` in the file before fixing three of them** — `UI-7`'s census found that a grep
shaped around one receiver name misses subscriptions by construction. ✅ **Done 2026-09-08: the true
count is exactly 3**, confirmed by searching the operator rather than a receiver name and then
sweeping the mechanisms that census was blind to (`.On<`, `LocationChanged`, `.Subscribe(`,
`PropertyChanged`, `CancellationToken.Register`, `EventCallback`) — all zero.

⭐ **And this row closes the class.** A repo-wide sweep found `SystemConfigPage` is the **only**
component in `Radio.Web` that subscribes to a singleton service event without a matching `-=`.

⚠ **Corrected 2026-09-08 in pre-merge review — "the other 22 all unsubscribe correctly" does NOT
reproduce, and the number is dropped rather than restated.** An independent sweep counted **16**
other subscribing components, plus 3 subscribing *services* (`AudioStateStore`,
`EncoderHudService`, `ConsolePlaybackState`) which are not components at all. **The substantive
claim survived falsification** — every other subscription site has a matching `-=` in the same
file; the only two without one are `MainLayout._timer.Elapsed` and `PhonePage._pollTimer.Elapsed`,
both component-owned timers stopped and disposed, so the delegate dies with the component.

## Verification

Unit-testable without hardware and **must fail first**.

⛔ **The verification this row originally specified does not discriminate, and would have been RED
against correct code.** It said: *"render the page twice against a shared service, count the
invocation list, assert it does not grow."* **That fails after the fix too** — two simultaneously
live components legitimately hold two sets of handlers. That is correct multicast behaviour, not a
leak.

**The leak is that disposal does not shrink the list.** Assert **render → dispose returns the
invocation list to its baseline**, and confirm it is RED against `main` before trusting it.

✅ **Done 2026-09-08, and the numbers are the record.** Against the unfixed page: baseline **0**,
and after render *and* dispose **1 per event** — 3 handlers surviving teardown. Five render/dispose
cycles left **5 per event**, 15 in total. Linear, unbounded, and **exactly 3 per visit**, which
settles the plan's open §5.1 question: no child component subscribes to these events as well, so
the delta is the page's own three and nothing more. After the fix both counts return to baseline.
⭐ **The instrument check passed before either assertion ran**, so the page demonstrably reaches
`InitializeSignalRAsync` under the hermetic rig and the tests could see the unfixed state.

⚠ **A test cannot read these invocation lists directly.** They are field-like events, so
`hub.SourceChanged.GetInvocationList()` is CS0070 from outside the declaring type, and
`InternalsVisibleTo` does not help because the backing field is compiler-generated private.
Reflection is the only route.

⛔ **Corrected 2026-09-08 during the build — the seam citation was already stale when it was
written.** This row said *"eight existing test files already use that seam (`SleepTests.cs:501-523`
is the idiom)"*. **Both halves are wrong now**, because `UI-7` merged hours before this row was
built and its `C-213` repaired twelve hand-rolled reflection sites:

- `SleepTests.cs` is **511 lines**, so `:523` is past EOF. Its raw `GetField` is **gone**, replaced
  by two one-line wrappers over the shared helper.
- **Exactly one** raw `GetField` on a hub event backing field remains in the whole repo, and it is
  inside `tests/Radio.Web.Tests/TestHelpers/HubEventFire.cs:91-92`. "Eight test files" is now one.

**The shipped test therefore counts through `HubEventFire.InvocationListOf<TDelegate>`** — public,
shared, and hardened — rather than hand-rolling a thirteenth copy. That is not merely tidier: the
helper **throws** on a missing backing field instead of counting zero (so a renamed or
accessor-converted event fails loudly rather than making every assertion vacuously true), and
**throws** on a wrong payload type instead of silently widening to `Delegate`. `nameof` moves an
event rename one step earlier still, from a runtime throw to a compile error.

⭐ **Note what happened here**: a row about a resource leak specified an instrument that could not
tell the fixed state from the broken one. That is the same failure recorded three other times this
session — see the banner in [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md). **Prove the instrument can
see the unfixed state before trusting it on the fixed one.**

`CLAUDE.md` § *Test Timing* applies if the handlers are `async`: synchronize on the observation,
never on elapsed time.

## Provenance

Found 2026-09-08 during `UI-7` planning; independently reproduced by a second census pass, which
saw the same three sites as `+= async () =>` / `+= async (_) =>` — delegates with no name to
unsubscribe by.
