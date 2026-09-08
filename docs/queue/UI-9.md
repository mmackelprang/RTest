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

**Therefore every navigation to `/system-config` permanently adds three handlers to a
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

⚠ **Check whether the page implements disposal at all.** A Blazor component only gets `Dispose` if
it declares `IDisposable`/`IAsyncDisposable`; adding the interface to a 2,000-line page is a bigger
change than adding three fields, and the page may have other subscriptions with the same problem.
**Enumerate every `+=` in the file before fixing three of them** — `UI-7`'s census found that a
grep shaped around one receiver name misses subscriptions by construction.

## Verification

Unit-testable without hardware and **must fail first**: render the page twice against a shared
service, count the invocation list, assert it does not grow. ⚠ Against today's code that count
grows by three per render, so the test discriminates — confirm it actually does by running it
before the fix, not only after.

`CLAUDE.md` § *Test Timing* applies if the handlers are `async`: synchronize on the observation,
never on elapsed time.

## Provenance

Found 2026-09-08 during `UI-7` planning; independently reproduced by a second census pass, which
saw the same three sites as `+= async () =>` / `+= async (_) =>` — delegates with no name to
unsubscribe by.
