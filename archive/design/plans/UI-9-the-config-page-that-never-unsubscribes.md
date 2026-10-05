# `UI-9` — the config page that never unsubscribes

**Row:** [`docs/queue/UI-9.md`](../../docs/queue/UI-9.md) · **Index:** [`docs/BUILDER_QUEUE.md`](../../docs/BUILDER_QUEUE.md)
**Planned:** 2026-09-08 · **Estimate:** S (one PR, ~1.5–2 h including the fail-first run)
**Files touched:** 2 (`SystemConfigPage.razor`, `SystemConfigPageTests.cs`)

---

## §0 — Findings that change the row

Everything in this section was measured against the working tree, not inherited. Four of the row's
statements are wrong; none of them changes the fix, and two of them make it **smaller** than filed.

### 0.1 ⚠ The route is `/system`, not `/system-config`

`SystemConfigPage.razor:1` is `@page "/system"`. That is the file's only `@page`. The row says
`/system-config` five times, and `UI-7`'s plan inherits the same wrong string at its `:412` and
`:1536`. **The prior session that recorded `/system` was right.** No redirect or second route exists,
so `/system-config` is not an alias — it is simply not a route in this application.

Nothing in the fix depends on the route. It matters only because every prose description of this bug
— row, `UI-7` §6.1, and the eventual PR body — names a URL the appliance does not serve, and the next
person to go looking for the page will not find it.

### 0.2 ⚠ The page ALREADY implements `IDisposable`, and already has a `Dispose()` body

The row's central worry — *"adding the interface to a 2,000-line page is a bigger change than adding
three fields"* — does not apply. `SystemConfigPage.razor:15` already declares `@implements IDisposable`,
and `:4192-4197` is a live `Dispose()` that Blazor already calls:

```csharp
4192:  public void Dispose()
4193:  {
4194:    _statsTimer?.Dispose();
4195:    _sourceGainDebounceTimer?.Dispose();
4196:    StopEncoderPolling();
4197:  }
```

So there is no interface to add, no lifecycle to introduce, and no interaction with existing disposal
to negotiate. The change is three named methods, three `+=` right-hand sides, and three `-=` lines
inserted into a method that already runs. **This is the smallest possible shape of this fix**, and the
row over-estimated it.

`IAsyncDisposable` is **not** wanted here. Unsubscribing is synchronous; adding a second disposal
interface to get it would be strictly more code for no behaviour. `MainLayout.razor` implements both
and its `DisposeAsync` (`:1603`) just delegates to `Dispose()` (`:1620`) — an argument against
copying that shape, not for it.

### 0.3 The true `+=` count in the file is **exactly 3**, and they are the row's three

The row asks for a census before fixing three sites, citing `UI-7`'s lesson that a grep shaped around
one receiver name is structurally blind. Done, and deliberately shaped around the *operator* rather
than any receiver name:

| Search | Hits in `SystemConfigPage.razor` |
|---|---|
| `\+=` (all forms, incl. arithmetic and string concat) | **3** — `:2219`, `:2229`, `:2239` |
| `.On<` / `.On(` (SignalR handler registration) | 0 |
| `LocationChanged` (NavigationManager) | 0 |
| `OnChange` | 1 — `:1366`, an `<InputFile OnChange=...>` **Blazor parameter**, not a subscription |
| `.Subscribe(` (Rx) | 0 |
| `PropertyChanged` | 0 |
| `CancellationToken.Register` | 0 |
| `EventCallback` | 0 |

The three are the row's three, and there is no fourth leak hiding behind a different mechanism in this
file. The row's count is correct — but it was correct by luck rather than by census, and it is now
correct by census.

**Beyond this file:** a full sweep of `src/Radio.Web/` found that **`SystemConfigPage` is the only
component in the application that subscribes to a singleton service event without a matching `-=`.**
Twenty-two other components subscribe to service events; every one of them declares a disposal
interface and unsubscribes symmetrically. That makes this row a genuine one-off outlier, not the first
instance of a pattern — and it means fixing it closes the class in `Radio.Web`, which is worth saying
in the PR body.

The one near-miss, explicitly **out of scope**: `PhonePage.razor:263` subscribes
`_pollTimer.Elapsed += async (_, _) => await PollStatusAsync();` with no `-=`. It is not a leak — the
timer is a component-owned field, stopped and disposed at `:1055-1056`, so the delegate dies with the
component. It is a style asymmetry against `PhoneDiagnosticsPanel.razor:162/:194`, which does the
named-method version of the identical pattern. Not this row's business; noted so the next census does
not re-flag it as a finding.

### 0.4 ⚠ The verification as the row describes it does NOT discriminate

The row says: *"render the page twice against a shared service, count the invocation list, assert it
does not grow."* **That assertion fails after the fix too, and it should.** Two simultaneously-live
components legitimately hold two sets of handlers — that is correct multicast behaviour, not a leak.
A test asserting "no growth across two live renders" would be red on correct code.

The leak is not that subscribing grows the list. It is that **disposal does not shrink it.** The
discriminating assertion is therefore:

> render → dispose → the count returns to its pre-render baseline

and, for the compounding claim the row actually cares about:

> five render/dispose cycles leave the singleton in the same state as one

Both are specified with literal code in §2, Task 2. This correction is the single most important thing
in this plan: implemented as written, the row's own test would have been worthless in the direction
that matters.

### 0.5 The file is 4,214 lines, not "~2,000" / "~2,300"

Cosmetic, but it changes where a Builder expects to land. `Dispose()` is at `:4192`, roughly 1,950
lines below the subscriptions at `:2219`. The two edit sites are far apart in the same file; nothing
sits between them that either edit disturbs.

### 0.6 All three events are `Func<...,Task>` — no `async void` anywhere

Confirmed at `AudioStateHubService.cs`:

| Event | Declaration | Line |
|---|---|---|
| `SourceChanged` | `public event Func<Task>? SourceChanged;` | `:49` |
| `PhoneCallStateChanged` | `public event Func<Task>? PhoneCallStateChanged;` | `:51` |
| `EncoderConnectionChanged` | `public event Func<EncoderConnectionDto, Task>? EncoderConnectionChanged;` | `:55` |

So `+= async () => {...}` is an `async Task` lambda, **not** `async void`. The handlers are awaitable
and the raise sites can observe them. This matters twice: it means the conversion to named methods is
type-preserving with no `async void` trap to avoid, and it means `CLAUDE.md` § *Test Timing* applies
in its ordinary form rather than its nastiest one (§3.2).

`Program.cs:411` — `builder.Services.AddSingleton<AudioStateHubService>();` — **confirmed exactly as
the row states.** Process lifetime, single instance.

---

## §1 — The fix, and the pattern it copies

### 1.1 Pattern source: `NowPlayingPanel.razor`

**Copied from `src/Radio.Web/Components/Shared/NowPlayingPanel.razor`.** It is the closest shape in the
repo to what `SystemConfigPage` is doing: a component subscribing to **several `AudioStateHubService`
events**, from inside a `try` in its async initialization path, with **named instance methods**, and a
disposal method that unsubscribes all of them in the same order.

Subscribe (`:554-559`):

```csharp
      HubService.PlaybackStateChanged += OnPlaybackStateChanged;
      HubService.NowPlayingChanged += OnNowPlayingChanged;
      HubService.SourceChanged += OnSourceChanged;
      HubService.VolumeChanged += OnVolumeChangedEvent;
      HubService.FingerprintStatusChanged += OnFingerprintStatusChanged;
      HubService.RadioStateChanged += OnRadioStateChanged;
```

Unsubscribe (`:1161-1166`), same order, first statements in `DisposeAsync`:

```csharp
    HubService.PlaybackStateChanged -= OnPlaybackStateChanged;
    HubService.NowPlayingChanged -= OnNowPlayingChanged;
    HubService.SourceChanged -= OnSourceChanged;
    HubService.VolumeChanged -= OnVolumeChangedEvent;
    HubService.FingerprintStatusChanged -= OnFingerprintStatusChanged;
    HubService.RadioStateChanged -= OnRadioStateChanged;
```

**Why this one and not the others.** Two runners-up were considered and rejected as templates:

- `AudioStateStore.cs:90-100` / `:496-512` does the same thing across 11 events and is the most
  complete example in the repo — but it is a **service**, not a component, so it models
  constructor-subscribe / `DisposeAsync`-unsubscribe rather than the component lifecycle. Cite it as
  corroboration, not as the template.
- `MainLayout.razor:381-471` / `:1570-1601` is the most thorough (15 subscriptions, 8 services) and is
  the only site that documents the singleton-pinning hazard in comments (`:1583-1594`) — worth reading
  once for that rationale — but it implements both disposal interfaces, which §0.2 rules out here.

**On the `IDisposable`-vs-`IAsyncDisposable` mismatch:** `NowPlayingPanel` uses `IAsyncDisposable` and
this page uses `IDisposable`. That difference is immaterial to the pattern — the same
named-method-plus-symmetric-`-=` discipline is used under plain `IDisposable` by
`DevTray.razor:118`/`:279`, `EncoderHud.razor:145`/`:151`, `Home.razor:36`/`:46`,
`MessageBubble.razor:225`/`:229` and `VoicemailPlayer.razor:150`/`:165`. **Keep `IDisposable`.**

### 1.2 Why named methods actually work here

`hub.SourceChanged -= OnHubSourceChangedAsync` constructs a *new* delegate instance from the method
group, which looks like it should fail to remove anything. It does not: delegate equality compares
**target object + method**, not instance identity, so `Delegate.Remove` matches and removes the
registered handler. This is the mechanism the six components above already rely on in production, so
it is proven in this codebase, not merely correct in principle.

The corollary is the actual invariant, and it is what the comment in Task 1 must state: *every
subscription in `InitializeSignalRAsync` has a matching unsubscription in `Dispose`*. The naming is a
means to that; the symmetry is the property.

---

## §2 — Tasks

Sequence is fixed: **Task 2 (the test) must be written and run RED before Task 1 (the fix).**

### Task 1 — Convert the three lambdas to named methods and unsubscribe in `Dispose`

**File:** `src/Radio.Web/Components/Pages/SystemConfigPage.razor`

**Edit 1 of 3** — replace the subscription block. Current `:2214-2259`, with the block to replace at
`:2218-2246`:

```csharp
  private async Task InitializeSignalRAsync()
  {
    try
    {
      // ⚠ UI-9: named methods, never lambdas. An anonymous delegate has no stable reference, so no
      // `-=` can remove it — and HubService is AddSingleton (Program.cs:411), so a handler that
      // cannot be removed outlives every circuit that added it: the singleton holds the delegate,
      // the delegate holds this component, and the component holds its render tree. The invariant
      // is the symmetry, not the naming: every `+=` here has a matching `-=` in Dispose().
      // Shape copied from NowPlayingPanel.razor:554-559 / :1161-1166.
      HubService.SourceChanged += OnHubSourceChangedAsync;
      HubService.EncoderConnectionChanged += OnHubEncoderConnectionChangedAsync;
      HubService.PhoneCallStateChanged += OnHubPhoneCallStateChangedAsync;

      // Start the hub connection if not already connected
      if (!HubService.IsConnected)
      {
        await HubService.StartAsync();
      }
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "Failed to initialize SignalR for event source updates, falling back to manual refresh");
      // Silently fail - fallback to manual refresh
    }
  }

  /// <summary>Reloads the event-source list when the hub reports a source change.</summary>
  private async Task OnHubSourceChangedAsync()
  {
    await InvokeAsync(async () =>
    {
      await LoadEventSourcesAsync();
      StateHasChanged();
    });
  }

  /// <summary>Refreshes the encoder status card when the encoder connects or drops.</summary>
  /// <remarks>The payload is unused — this handler re-pulls the full status over REST rather than
  /// trusting the transition DTO, which is the behaviour the lambda it replaced had.</remarks>
  private async Task OnHubEncoderConnectionChangedAsync(EncoderConnectionDto _)
  {
    await InvokeAsync(async () =>
    {
      _encoderStatus = await IntegrationsApi.GetEncoderStatusAsync();
      StateHasChanged();
    });
  }

  /// <summary>Refreshes the phone status card when a call changes state.</summary>
  private async Task OnHubPhoneCallStateChangedAsync()
  {
    await InvokeAsync(async () =>
    {
      _phoneStatus = await IntegrationsApi.GetPhoneStatusAsync();
      StateHasChanged();
    });
  }
```

The three method bodies are the three lambda bodies **unchanged, verbatim**, including the outer
`await InvokeAsync(async () => ...)` wrapping. Keeping the `async`/`await` form rather than collapsing
to an expression-bodied `=> InvokeAsync(...)` is deliberate: it makes the transformation obviously
behaviour-preserving at review time, which is worth more than one saved line.

`EncoderConnectionDto` needs no new `@using` — it lives in `Radio.Web.Models`
(`src/Radio.Web/Models/ApiModels.cs:1299`), already imported at `SystemConfigPage.razor:3`.

**Edit 2 of 3** — the three `-=` lines in the existing `Dispose()` at `:4192-4197`:

```csharp
  public void Dispose()
  {
    // ⚠ UI-9: symmetric with InitializeSignalRAsync. HubService is a process-lifetime singleton
    // (Program.cs:411); without these three lines every visit to /system leaves three handlers
    // behind, each retaining this component and its whole render tree, on an appliance that runs
    // for weeks between restarts.
    HubService.SourceChanged -= OnHubSourceChangedAsync;
    HubService.EncoderConnectionChanged -= OnHubEncoderConnectionChangedAsync;
    HubService.PhoneCallStateChanged -= OnHubPhoneCallStateChangedAsync;

    _statsTimer?.Dispose();
    _sourceGainDebounceTimer?.Dispose();
    StopEncoderPolling();
  }
```

**Edit 3 of 3** — none. There is no third edit; this is listed so the Builder does not go looking for
one. No `@implements` change, no new field, no constructor.

**The partial-initialization case needs no guard.** `InitializeSignalRAsync` is called at `:2201`,
after `LoadIntegrationDataAsync()` at `:2198`. If `OnInitializedAsync` throws before reaching `:2201`,
Blazor still disposes the component, so `Dispose` runs three `-=` for handlers that were never added.
`Delegate.Remove` against a delegate that is not in the list — including against a `null` event — is a
defined no-op, not an exception. No null check, no `_subscribed` flag. (`GainControlPopover.razor:178`
does carry such a flag, for a different reason: its subscription is genuinely conditional.)

### Task 2 — The failing test

**File:** `tests/Radio.Web.Tests/Components/Pages/SystemConfigPageTests.cs` — **append to the existing
fixture; do not create a new file.** That fixture is already exactly what this test needs and it
already works: `Services.AddHermeticTestRig()` (`:27`) makes rendering network-free, and — the load-
bearing part — **`Services.AddSingleton<AudioStateHubService>()` at `:54`** means every
`RenderComponent<SystemConfigPage>()` in a given test instance resolves *the same hub*. That is the
"shared service instance" the row asks for, already present. Duplicating this 40-line fixture into a
new file would be two copies free to drift.

Add to the existing usings:

```csharp
using System.Reflection;
using FluentAssertions;
```

`FluentAssertions` is already used elsewhere in this test project (e.g. `SleepTests.cs:505`); this file
currently uses raw `Assert`. The new tests use FluentAssertions because the `because` strings carry the
reasoning, and on a leak test the failure message is the deliverable.

Append at the end of the class:

```csharp
  // ========== UI-9: handler-leak guards ==========

  /// <summary>
  /// Reads the number of handlers currently registered on one of the hub's field-like events.
  /// </summary>
  /// <remarks>
  /// ⚠ Reflection is not a shortcut here, it is the only route. A field-like <c>event</c> can only be
  /// used with <c>+=</c> / <c>-=</c> from outside its declaring type — <c>hub.SourceChanged
  /// .GetInvocationList()</c> is CS0070 and will not compile, and <c>InternalsVisibleTo</c> does not
  /// change that because the backing field is compiler-generated and private. The seam is the same one
  /// SleepTests.cs:501-523 and NowPlayingDockTests already use against this exact type; cast to
  /// <see cref="Delegate"/> so one helper serves all three events regardless of their payload types.
  /// </remarks>
  private static int HubHandlerCount(AudioStateHubService hub, string eventName)
  {
    var field = typeof(AudioStateHubService).GetField(eventName,
      BindingFlags.NonPublic | BindingFlags.Instance);
    field.Should().NotBeNull($"{eventName} backing field must exist");
    var del = (Delegate?)field!.GetValue(hub);
    return del?.GetInvocationList().Length ?? 0;
  }

  /// <summary>The three events SystemConfigPage subscribes to, counted together.</summary>
  private static Dictionary<string, int> HubHandlerCounts(AudioStateHubService hub) => new()
  {
    ["SourceChanged"] = HubHandlerCount(hub, "SourceChanged"),
    ["EncoderConnectionChanged"] = HubHandlerCount(hub, "EncoderConnectionChanged"),
    ["PhoneCallStateChanged"] = HubHandlerCount(hub, "PhoneCallStateChanged"),
  };

  /// <summary>
  /// Disposing the page must remove every hub handler it added.
  /// </summary>
  /// <remarks>
  /// ⚠ The assertion is that disposal RETURNS THE COUNT TO BASELINE — not that rendering twice fails
  /// to grow the list. Two simultaneously-live components legitimately hold two sets of handlers;
  /// that is correct multicast behaviour and it is true after the fix as well as before it. The leak
  /// is that teardown does not shrink the list, so teardown is what this measures.
  /// </remarks>
  [Fact]
  public void SystemConfigPage_Dispose_RemovesEveryHubSubscriptionItAdded()
  {
    var hub = Services.GetRequiredService<AudioStateHubService>();
    var baseline = HubHandlerCounts(hub);

    var cut = RenderComponent<SystemConfigPage>();

    // ⚠ Instrument check, and it is not optional. If the page never reached InitializeSignalRAsync
    // — an earlier await in OnInitializedAsync threw, the reflection seam broke, an event was
    // renamed — then the counts never move, and every assertion below would pass against a page
    // that subscribes nothing. A leak test that cannot see the leak is worse than no test.
    // This also supplies the rendezvous: it waits for the SUBSCRIPTION TO BE OBSERVED rather than
    // for a duration, per CLAUDE.md § Test Timing.
    cut.WaitForAssertion(() =>
      HubHandlerCounts(hub).Should().NotBeEquivalentTo(baseline,
        "rendering the page must attach hub handlers; if this never becomes true the test is blind"));

    DisposeComponents();

    HubHandlerCounts(hub).Should().BeEquivalentTo(baseline,
      "every handler InitializeSignalRAsync adds must be removed in Dispose — AudioStateHubService is "
      + "AddSingleton (Program.cs:411), so whatever is left here outlives the circuit for the life of "
      + "the process");
  }

  /// <summary>
  /// Repeated visits to /system must not accumulate handlers on the process-lifetime singleton.
  /// </summary>
  [Fact]
  public void SystemConfigPage_RepeatedVisits_DoNotAccumulateHubHandlers()
  {
    var hub = Services.GetRequiredService<AudioStateHubService>();

    RenderComponent<SystemConfigPage>();
    DisposeComponents();
    var afterOneVisit = HubHandlerCounts(hub);

    for (var i = 0; i < 4; i++)
    {
      RenderComponent<SystemConfigPage>();
      DisposeComponents();
    }

    HubHandlerCounts(hub).Should().BeEquivalentTo(afterOneVisit,
      "five visits to /system must leave the singleton in the same state as one; the appliance runs "
      + "for weeks between restarts, so per-visit growth is unbounded in practice");
  }
```

`DisposeComponents()` is bUnit's teardown call and it invokes `IDisposable.Dispose()` on every rendered
component. It is already the repo's idiom for exactly this kind of teardown assertion —
`RdsScrollMarqueeTests.cs:259-268`, `Marquee_ComponentDispose_DisposesEngineInstance`, described in its
own `because` string as a leak guard.

### Task 3 — Docs

- `design/DECISION-LOG.md` — one entry: the class is closed in `Radio.Web` (§0.3), the route is
  `/system` (§0.1), and the row's proposed assertion was replaced and why (§0.4).
- ⚠ **Do not touch `design/TESTING.md` or `DECISION-LOG.md` while the `TEST-2` Builder holds them.**
  Both are in that Builder's declared file set. Either sequence this row's doc commit after `TEST-2`
  merges, or put the note in the PR body and file the doc edit as a follow-up. **Sequencing, not
  merging** — do not co-edit.

---

## §3 — Verification protocol

### 3.1 Fail-first is a gate, not a formality

The row says *"confirm it actually does [discriminate] by running it before the fix"*. Concretely,
Builder must do this and paste the output into the PR:

1. Apply **Task 2 only**. Do not touch the `.razor` file.
2. Run:
   ```bash
   dotnet test tests/Radio.Web.Tests/Radio.Web.Tests.csproj -c Release \
     --filter "FullyQualifiedName~SystemConfigPageTests" > /tmp/ui9-before.log 2>&1; echo "exit=$?"
   grep -E "Passed!|Failed!|error" /tmp/ui9-before.log
   ```
   ⚠ Note the redirect. `CLAUDE.md` is explicit: **never pipe `dotnet test` into `tail`** — the shell
   reports `tail`'s exit code and a failing run reads as a pass.
3. **Required observation:** both new tests **FAIL**, and the failure message shows the counts differing
   by the number of subscriptions actually attached. **Record that number.** It should be 1 per event
   (3 total across the three events), but it is measured, not assumed — if a child component of the page
   also subscribes to one of these events, the delta will be larger, and that is a finding worth
   reporting rather than a test to adjust.
4. **If the guard `WaitForAssertion` is what fails** — i.e. the counts never move off baseline — then
   the page is not reaching `InitializeSignalRAsync` under the hermetic rig, and **the entire test is
   invalid**. Do not proceed to Task 1. Diagnose why (most likely: an `await` in `OnInitializedAsync`
   before `:2201` throwing unhandled) and report back. This is the specific failure that would produce
   a test that passes on both sides of the fix.
5. Apply Task 1. Re-run. Both tests pass, and the pre-existing 20-odd `SystemConfigPageTests` stay green.

### 3.2 The timing rule, and how this test satisfies it

`CLAUDE.md` § *Test Timing* forbids racing a wall clock against a wall clock. This test contains **no
`Task.Delay` and no sleep**. The one place a rendezvous is genuinely needed is between
`RenderComponent` returning and `OnInitializedAsync`'s async continuation reaching `:2201` — bUnit's
`RenderComponent` returns once the initial render completes, which is not necessarily after the whole
async lifecycle has run.

`WaitForAssertion` is the correct instrument for that: it re-evaluates **on render events** until the
condition holds, so it synchronizes on the observation (*the handlers are attached*) rather than on
elapsed time. Its timeout is a backstop against hanging, not the mechanism.

⚠ **Builder should record which way it actually resolved.** The hermetic rig fails requests by throwing
inside `SendAsync` (`HermeticTestRig.cs:73-82`), which may complete every await synchronously and make
`OnInitializedAsync` run to completion before `RenderComponent` returns. If so, `WaitForAssertion`
succeeds on its first evaluation and the test is fully deterministic with no polling at all — which is
the better outcome and worth stating in the PR rather than leaving as an unknown. If it needs more than
one evaluation, say so; that is a real (if bounded) timing dependency and it should be visible.

### 3.3 No UAT required

Backend-invisible correctness on a component already covered by 24 render tests. Per the auto-merge
policy, the unit suite stands in for UAT here. A leak of this kind is not observable in a browser
session short enough to run by hand — that is precisely why it survived to be filed.

### 3.4 Gates

- `dotnet build -c Release` — **baseline is 47 warnings, 0 errors.** The gate is *equality with the
  baseline*. Adding three methods and three statements should move it by zero; if the count changes,
  build `main` too before assuming this row caused it.
- Full suite green, read from the per-project summary lines, with the known-failing set from `CLAUDE.md`
  (four `SrcVariableResamplerTests`, the `Category=Integration` NWS and Cover Art tests) excluded.

---

## §4 — Relationship to `UI-7`

**They do not collide, and this row does not wait.**

`UI-7`'s plan is at `design/plans/UI-7-close-the-multicast-await-class.md`. It already knows about this
defect and deliberately declines it: it files it as `C-212` at `:406-412`, and at `:459` states
**"⛔ Do not fix `SystemConfigPage.razor`'s handler leak."** So `UI-7` will not touch the file this row
edits, and the non-collision is `UI-7`'s own explicit decision rather than an inference of mine.

**Line-anchor overlap:** none in source. `UI-7` converts raise sites in `AudioStateHubService.cs` and
the fan-out helpers in `AudioStateStore.cs`; `UI-9` edits `SystemConfigPage.razor` and
`SystemConfigPageTests.cs`. Disjoint sets. `UI-7` cites `SystemConfigPage.razor:2215-2245` (`:1388`) and
`:2219/2229/2239` (`:64`, `:1304`, `:1476`, `:1535`) as **read-only reference anchors** in its prose —
so landing `UI-9` first will make those five citations in `UI-7`'s plan stale. That is a documentation
consequence, not a merge conflict, and it is one-directional: `UI-9` first costs `UI-7` five stale
anchors; `UI-7` first costs `UI-9` nothing.

**One real coupling, worth stating.** This row's test reads the events' private backing fields by
reflection. `UI-7`'s `C-207` (`:320-341`) examined converting these events to explicit `add`/`remove`
accessors — which would delete those backing fields and break **eight existing test files** plus, now,
this one. `UI-7` **rejected that route** in favour of a `GetInvocationList()` fan-out (`:489`), so the
backing fields survive and this test is safe. But if that decision is ever revisited, this test joins the
list of casualties. Adding it takes the count from eight files to nine; it does not create the coupling.

**Background worth reading, not acting on:** `UI-7`'s census (ten production subscriber types, eight
components subscribing per circuit) and its `C-208` finding that the store re-exposes 11 of the hub's 14
events — the three it omits are `PhoneCallStateChanged`, `EncoderHudChanged` and `ConfigChanged`, which
is **why** `SystemConfigPage` reaches past `AudioStateStore` to the hub directly (`:355-359`). That
explains the design and confirms there is no "just route it through the store" alternative available in
this row. Do not attempt one.

---

## §5 — Risks, and what I could not verify

| # | Item | Assessment |
|---|---|---|
| 1 | **The exact handler delta under test is measured, not asserted in advance.** §3.1 step 3 requires Builder to record it. If a child component also subscribes to these events the delta exceeds 3. | Low — the test compares against a measured baseline rather than a hard-coded 3, so it is correct either way. Flagged because the *number in the PR body* should be the observed one. |
| 2 | **Whether `OnInitializedAsync` completes synchronously under the hermetic rig.** Not verified — it needs a run. §3.2 requires Builder to report which way it went. | Low. `WaitForAssertion` is correct under both outcomes; only the determinism claim in the PR body depends on it. |
| 3 | **`WaitForAssertion` has a timeout.** It is a backstop, but it is still a clock. | Accepted, and it is the safe direction: starvation makes the *guard* fail, which aborts the test loudly rather than weakening the real assertion. This is exactly the distinction `CLAUDE.md` § Test Timing draws between a dangerous timing dependency and a tolerable one. |
| 4 | **Prerendering could double the subscription per visit in production.** `@rendermode InteractiveServer` (`:2`) with prerendering runs `OnInitializedAsync` twice per navigation. I did **not** verify whether prerendering is enabled for this route. | Does not change the fix — `Dispose` removes what was added either way. It would mean the production leak rate is six handlers per visit rather than three. Worth one line of investigation in the PR, not a blocker. |
| 5 | `design/TESTING.md` and `DECISION-LOG.md` are held by the `TEST-2` Builder. | Handled by sequencing — §2 Task 3. |

**Auto-mergeable: yes.** Two files, no source outside one page component, no schema/auth/config/migration
surface, no hardware dependency, unit-test-verified with a mandated fail-first run. It meets all four
auto-merge conditions **provided §3.1 step 3 produced a genuine RED**. If the tests pass before Task 1 is
applied, that is a red gate — stop and report, do not merge.

---

## §6 — Explicitly out of scope

- `PhonePage.razor:263`'s timer-lambda asymmetry (§0.3). Not a leak.
- Anything in `UI-7`'s territory: raise-site fan-out, the `GetInvocationList()` helpers, the lint.
- Adding `PhoneCallStateChanged` / `EncoderHudChanged` / `ConfigChanged` to `AudioStateStore` so the
  store can be the hub's sole subscriber — that is `UI-7` §6.2's idea and needs its own row.
- Fixing `/system-config` → `/system` in `UI-7`'s plan text. Report it; let that row's owner edit it.
