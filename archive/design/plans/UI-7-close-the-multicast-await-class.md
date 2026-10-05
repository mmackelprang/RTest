# PLAN — `UI-7` · Close the multicast-await class, and stop calling a live defect dormant

> **Row:** `UI-7`, [`docs/queue/UI-7.md`](../../docs/queue/UI-7.md). 🟡 **P2.** Filed 2026-09-07 by the
> `UI-6` Builder.
> **Branch:** `fix/ui-7-close-the-multicast-await-class`
> **Estimate:** **1.5 d**, and **an owner decision before Task 1**. §0.7 derives both.
> **Auto-mergeable on green gates — but ONLY for §1.1's mechanism.** Implementing the mechanism the
> row's scope question names would brick the console panel on first render. §0.8.
> **Planned against** `main` at **`084a6bbd`**. Every line number below was read out of the tree at
> that commit.
> **Nothing on the box was touched.** No SSH, no `curl`, no deploy. Every claim is from source or
> from `git`; §7.2 says which are neither.

---

## 0. Read this before Task 1

### 0.1 ⚠⚠ Read §0.2 before anything else

The owner's decision of 2026-09-07 was **"enforce the single subscriber AND add the lint."** The lint
half is right, is unchanged by anything below, and this plan builds it.

**The enforcement half rests on a premise the source refutes.** `AudioStateHubService` does not have
one subscriber. It has ten production subscriber types, on twelve of its fourteen events, and the
defect the row calls *dormant* is **live on the appliance today**. A single-registration guard would
throw during the first circuit's render.

This plan does not re-open a settled decision for preference. It reports that the fact the decision
was made on is untrue, gives the nearest enforcement that is actually available, and hands the
substitution back to the owner as a decision — §1.1 and §0.4.

### 0.2 ⚠⚠ `C-203` — THE ROW'S CENTRAL PREMISE IS FALSE

`docs/queue/UI-7.md:34-38` says:

> ⚠ **Its saving grace today is an invariant nothing enforces.** `AudioStateStore` is its only
> subscriber, so "N subscribers, one awaited" is currently "1 subscriber, one awaited" and the bug is
> dormant.

**It is not its only subscriber, and the bug is not dormant.** Counted at `084a6bbd`, by the six
mechanisms §0.3 lists:

| Event | Distinct production subscribers | Store is the only one? |
|---|---|---|
| `NowPlayingChanged` | **5** | no |
| `SourceChanged` | **5** | no |
| `PlaybackStateChanged` | **4** | no |
| `RadioStateChanged` | **4** | no |
| `QueueChanged` | **3** | no |
| `VolumeChanged` | **3** | no |
| `EncoderConnectionChanged` | **3** | no |
| `SleepStateChanged` | **3** | no |
| `FingerprintStatusChanged` | **2** | no |
| `PhoneCallStateChanged` | 1 | **no — the store does not subscribe at all** |
| `EncoderHudChanged` | 1 | **no — the store does not subscribe at all** |
| `EncoderConfigStatusChanged` | 1 | yes |
| `EventPlaybackChanged` | 1 | yes |
| `ConfigChanged` | **0** | neither — dead event (`C-208`) |

**Ten production types subscribe:** `AudioStateStore` (`AudioStateStore.cs:90-100`),
`EncoderHudService` (`:54-55`), and eight rendered components — `MainLayout.razor:381-386`,
`NowPlayingPanel.razor:554-559`, `NowPlayingDock.razor:117-118`, `QueueHistoryPanel.razor:410-411`,
`RadioControlPanel.razor:981`, `RadioPage.razor:252`, `Sleep.razor:221-222`,
`SystemConfigPage.razor:2219/2229/2239`.

⭐ **And the component subscribers are per circuit, on a process-lifetime singleton.**
`Program.cs:411` is `AddSingleton<AudioStateHubService>()`. `MainLayout` renders once per circuit and
subscribes six events each time. So the invocation list is not `1`; it is
`(store or service) + (components × open circuits)`. Two browsers on the kiosk LAN is already
**nine** handlers on `NowPlayingChanged`.

**Every consequence the row describes is therefore happening now, not waiting to happen.** Fourteen of
the fifteen raise sites carry no `try`/`catch` at all, so a subscriber that throws synchronously
starves every handler after it in the list, in the service that feeds all audio state to the panel.

📌 **This is not a criticism of the `UI-6` Builder's measurement.** Its *count* of raise sites was
very nearly right (`C-204`). What did not survive is the *inference* — that a class whose fan-out is
consumed by `AudioStateStore` is consumed **only** by it. `AudioStateStore` re-exposes 11 of the 14
events (`AudioStateStore.cs:34-80`), which is what makes that inference so easy to draw and so wrong:
the three it does not re-expose are exactly why `EncoderHudService` and `SystemConfigPage` reach past
it to the hub (`C-208`, §0.5).

### 0.3 How the subscribers were enumerated — six mechanisms, not a grep

The row's own scope question 4 demanded this, and it was right to. Two independent enumerations were
run and cross-checked, and two of their hits were verified by hand against the file
(`MainLayout.razor:378-392`, `NowPlayingPanel.razor:550-562`).

| # | Mechanism | What it found here |
|---|---|---|
| 1 | `+=` / `-=` in `.cs` | `AudioStateStore.cs:90-100`, `EncoderHudService.cs:54-55`, and the test fixtures |
| 2 | `+=` / `-=` in `.razor` | ⭐ **the majority of them** — 8 components, invisible to a `--type cs` filter |
| 3 | **DI activation** | `EncoderHudService` — `Program.cs:425` registers it, `:45` takes the hub, `:54-55` subscribes in the **constructor**, with no `+=` near the registration. `AudioStateStore` arrives the same way via `Program.cs:456` |
| 4 | Rendered components | the `.razor` subscriptions above run in `OnInitializedAsync`, so they exist only for a rendered circuit |
| 5 | Target-typed `new(...)` | **zero occurrences.** Every construction is an explicit `new AudioStateHubService(` |
| 6 | **Name collisions** | ⚠ **the trap fires backwards from the row's warning** — see below |

⚠ **`Sleep.razor:13` is `@inject AudioStateHubService AudioState`, and `Sleep.razor` injects no
`AudioStateStore` at all.** So `Sleep.razor:221-222` are genuine **hub** subscriptions. Meanwhile
`MainLayout.razor:20` is `@inject AudioStateStore AudioState`, so `MainLayout.razor:428-429` are
**store** subscriptions and must be excluded. The row warned that `AudioState` might be the store;
in the one file it named, `AudioState` is the hub. **A grep either way round gives a wrong answer** —
every hit below was resolved against its own `@inject` or field declaration.

Two more receivers, resolved the same way and both excluded: `ConsolePlaybackState.cs:50/:56`
(`_store` is `AudioStateStore`) and `DevTray.razor:2` (injects the hub but reads only
`ConnectionState` at `:155` — an injection, not a subscription).

### 0.4 What the owner chose, and which half of it survives

| The decision, in two halves | Status after §0.2 |
|---|---|
| **Add the lint** | ✅ **Unaffected and built here.** §1.3, Task 3. The reasoning holds exactly as given: `UI-6` fixed 3, `UI-7` found ~17, a third discovery would make the detector unarguable. |
| **Enforce the single subscriber** | ⛔ **Not available.** There is no single subscriber to enforce. §1.1 gives what replaces it. |

⚠ **The reasoning behind the enforcement half is still right, and this plan keeps it.** The owner's
argument was: *fixing N call sites treats the symptom and leaves the shape available to the next
author; make it structural and the sites stop mattering.* That argument does not depend on the
subscriber count. What it needs is a structure that makes **the shape** unreachable — which §1.1
provides — rather than one that makes **a second subscriber** unreachable, which the code cannot
tolerate.

### 0.5 The three "singletons" — two are not singletons, and one is not this bug

The row's table lists three one-line sites. All three line numbers are correct. Almost nothing else
about the grouping is.

| Site | Row says | Actually | This bug? |
|---|---|---|---|
| `RadioPanelToggleService.cs:64` | singleton | **`AddScoped`** (`Program.cs:476`) | ✅ yes — `event Func<Task>? RadioPanelToggled` (`:21`) |
| `DeviceDisplayStateService.cs:22` | singleton | **`AddScoped`** (`Program.cs:475`) | ✅ yes — `event Func<Task>? DisplaySettingsChanged` (`:13`) |
| `PhoneUnreadState.cs:23` | singleton | `AddSingleton` (`Program.cs:431`) ✔ | ⛔ **NO — `event Action<int>?` (`:14`)** |

⭐ **`C-205` — `PhoneUnreadState` is a category error, and the repository already says so.**
`ConsolePlaybackState.cs:44-46`, in the tree today:

```
/// ⚠ PhoneUnreadState.Set still carries the plain invoke (PhoneUnreadState.cs:23) and is NOT in
/// UI-6's scope. Only the STARVATION half can bite there: its Changed is an Action<int>, so
/// Invoke really does run every handler and there is no discarded Task to lose.
```

`Action<int>` is void-returning. There is no `Task` to drop and no unobserved continuation, so the
half of the defect this row is named for cannot occur there. **It is excluded from the fix and from
the lint's rule** (which keys on `event Func<…Task>`), and §6.1 files the starvation half separately.

⭐ **And the severities are inverted.** The two sites that **are** this bug are `Scoped` with exactly
one subscriber each — `Home.razor:36` and `MainLayout.razor:387` respectively — so their invocation
list is length 1 and the defect is genuinely latent. The one site that is **genuinely multicast**
(`PhoneUnreadState`, singleton, one `MainLayout` handler per circuit, 15 publishers in
`PhonePage.razor`) is not this bug. **The row pairs the wrong severity with each of the three**, and
the live severity is entirely in `AudioStateHubService`, which the row called dormant.

### 0.6 The lint's three traps — two inherited, one new

`LogSafetyLintTests` is the right idiom to follow and the wrong file to copy from wholesale.

**Trap 1 — `C-152`, filename-scoped rules that disable themselves on rename.** `LogSafetyLintTests`'s
`Rule.OnlyInFile` (`:135`) is compared with `name.Equals(rule.OnlyInFile, StringComparison.Ordinal)`
at `:215`. Five rules use it — `AnnouncementService.cs`, `TTSFactory.cs`, `SoundFlowMasterMixer.cs`,
`GvTrunkApiService.cs`, `ContactResolutionService.cs`. Rename any of those and its rule goes green
forever with nothing to say so.
⛔ **The `UI-7` rule takes no `OnlyInFile` and needs none.** It is keyed on what the file *declares*,
not on what the file is *called* — §1.3.

**Trap 2 — `C-100`, the worktree root. ⚠ It is already fixed; the risk now is re-copying the broken
version.** `C-211`. `FindRepositoryRoot` at `LogSafetyLintTests.cs:290-324` was repaired in `PHN-5`:
`:313` is now

```csharp
    var root = candidates.LastOrDefault(c => !IsInsideAWorktree(c)) ?? candidates.LastOrDefault();
```

— the `?? candidates.LastOrDefault()` fallback is what stops a checkout under a `worktrees` segment
filtering out its own only candidate. **So the trap is not "reintroduce the bug"; it is "write a
second copy of this method".** It is `private static` in a class, so a new lint file in the same
assembly cannot call it, and copying it is the obvious move. Two copies is two chances to diverge and
one of them will be the pre-`PHN-5` shape. **Task 3a extracts it once, into `RepositoryRoot`, and
points `LogSafetyLintTests` at it.**

**Trap 3 — new, and the one the owner named. A "must be zero" lint cannot prove itself alive with a
floor on its own findings.** `LogSafetyLintTests`'s floors (`files.Count > 200` at `:233`,
`callsScanned > 800` at `:236`) prove **the scanner ran**. They do not prove **the rule can match** —
a regex that matches nothing satisfies `violations.Count == 0` exactly as well as a correct one, and
`GV-6`'s Builder shipped a lint whose success was unfalsifiable for precisely this reason. So the
floor has to sit on something that must be **non-zero**. §1.3 specifies three, headed by a positive
control that drives the rule against the pre-fix text and requires it to **fire**.

⭐ **The in-repo precedent for that is not `OPS-2`; it is better and it is 30 lines away.**
`VisualizerPanelTests.cs:233-236` pins a `NotContain` assertion and guards it first:

```csharp
    // ⚠ Prove the instrument before trusting its silence. A NotContain against an empty list passes
    // for the wrong reason, so pin a surviving event first: if this reflection call ever stops seeing
    // the hub's events, THIS line fails rather than the one below quietly passing forever.
    hubEvents.Should().Contain("EncoderConfigStatusChanged");
    hubEvents.Should().NotContain("VisualizationModeChanged");
```

That is exactly the shape §1.3 asks for, already written in this repository, about this same class.
Cite it in the new file.

### 0.7 The estimate — **1.5 d**, plus a decision that is not Builder's to make

| Work | Cost |
|---|---|
| Task 1 — two `NotifyAsync` helpers + 15 raise sites in `AudioStateHubService` | 0.25 d |
| Task 2 — the two real `Func<Task>` sites (`RadioPanelToggleService`, `DeviceDisplayStateService`) | 0.1 d |
| Task 3 — `RepositoryRoot` extraction + the lint | 0.5 d |
| Task 4 — the comment corrections (`C-205`, `C-209`, `C-212`) | 0.15 d |
| Task 5 — tests and every mutation in §4 | 0.5 d |

**What holds it to 1.5 d:** the fix is not designed here, it is *copied*. `AudioStateStore.cs:444-494`
is the shipped implementation of both helpers, written by `UI-6` four days ago, with its rationale in
`<remarks>`. Task 1 is a transcription plus 15 call-site edits.

⚠ **What is not in the number: the owner's decision on §1.1.** Builder must not start Task 1 until the
substitution in §1.1 is confirmed, because the row as written asks for something that does not
compile into a working panel (§0.8). That is a message and a reply, not a work item — but it is a
blocking one.

⚠ **What would push it to 2.5 d:** if the owner rejects §1.1 and wants genuine structural enforcement,
that is a different and much larger row — `C-207` means the `add`/`remove` accessor route breaks eight
test files, and the fan-out would first have to be routed through `AudioStateStore` for three events it
does not currently carry (`C-208`). §6.2 sketches it; it is not this row.

### 0.8 Auto-merge — **yes for §1.1's mechanism, and emphatically no for the row's**

The repository's policy allows a merge on green gates when the suite plus a user-flow check stand in
for UAT. Both branches of that need saying, because they differ.

**⛔ If Builder implements the row literally — a guard that throws on a second subscriber — the gates
go green and the appliance goes dark.** Trace it: `AudioStateStore` is DI-activated and subscribes
eleven events in its constructor; `MainLayout.OnInitializedAsync` then subscribes six of the same
events at `:381-386`. **The second one throws.** `MainLayout` is the root layout, so the exception
surfaces as a Blazor circuit failure on the *first page load* — the console UI in the cabinet shows
an error boundary and nothing else.

⚠ **And every deploy gate this repository has would still be green.** `systemctl is-active` is green;
`/api/health/version` on 5002 returns the new `gitSha`, because the binary genuinely *is* the new one;
the deploy's kiosk check counts **established connections** to `:5002`, and a circuit that connects
and then faults still established a connection. Unit tests pass because ~18 of the 20 test fixtures
construct the hub with one subscriber. This is the "deployed and displayed are different things" shape
`OPS-5` was filed for, arriving through a different door.

**✅ For §1.1's mechanism, auto-merge on green gates is appropriate**, with one addition:

1. It is confined to `src/Radio.Web` and `tests/`. No audio path, no hardware, no config, no migration.
2. Everything it changes is unit-testable without the box, and §4 mutation-checks every pin.
3. **One live check is still wanted and it costs the owner nothing** — load the kiosk and confirm the
   panel paints and now-playing still updates. Not because the change is risky in the `AUD-11` sense,
   but because this service is the event bus every UI surface reads, and "the page still renders" is
   not something the unit suite can assert. It is a page load, not an interruption to audio.

⚠ **The one behavioural change to name in the PR body:** subscribers now run **sequentially** and
their exceptions are now **caught and logged** rather than propagating into the SignalR dispatch. Both
are the intended semantics and both are what `AudioStateStore` already does — but on the hub the
invocation list is longer (up to 5 subscribers per event per circuit), so serialization is more
visible here than it was there. `AudioStateStore.cs:438-442` already argues why it is affordable: the
handlers are `InvokeAsync(StateHasChanged)` dispatches that queue onto their own circuit's renderer
and return, so serializing costs a dispatch each rather than a render each. That argument transfers,
and Task 1 says so at the call site.

### 0.9 ⚠ Eleven corrections found while planning — numbering continues from `C-202`

**`C-203` falsifies the row's premise and is why §1.1 exists.** **`C-205`, `C-206`, `C-207` and
`C-208` change the work.** **`C-204` is a count.** **`C-209`, `C-210` and `C-211` change how the lint
and its citations are written.** **`C-213` changes how the TESTS must be written.** **`C-212` is a
defect in a neighbouring file, filed and not fixed.**

---

**`C-203` — ⚠⚠ FALSIFIES THE ROW'S PREMISE. `AudioStateStore` is not `AudioStateHubService`'s only
subscriber. Ten production types subscribe, twelve of the fourteen events have a subscriber that is
not the store, and the store does not subscribe to two of them at all. The defect is live in
production today, not dormant.**

Full census and derivation in §0.2, enumeration method in §0.3. **Consequence: single-subscriber
enforcement is not implementable** — §0.8 traces what it does at runtime — and the row's framing of
`AudioStateHubService` as "the safe one" inverts the truth. It carries the **worst** exposure in the
tree: singleton lifetime, fourteen events, fifteen unguarded raise sites, up to five subscribers per
event per circuit, and no `try`/`catch` on fourteen of the fifteen.

---

**`C-204` — the site count is fifteen, not fourteen.**

`SourceChanged` is raised **twice**: at `AudioStateHubService.cs:196` inside the SignalR `On`
handler, and again at `:432` in `NotifySourceChangedAsync`, the local-trigger path documented at
`:423-427`. The row's table says 14, which is the number of `On`-handler raises and also, by
coincidence, the number of events. Task 1 must edit **fifteen** sites; a Builder working from the
row's count would leave `:432` behind — the one site not adjacent to the others and therefore the one
a sweep misses.

---

**`C-205` — ⚠ CHANGES THE WORK. `PhoneUnreadState.cs:23` is not an instance of this defect, and the
repository already documents the exclusion.**

`PhoneUnreadState.cs:14` declares `public event Action<int>? Changed;` — void-returning, so
`Delegate.Invoke` runs every handler and there is no discarded `Task`. `ConsolePlaybackState.cs:44-46`
says exactly this, in as many words, and predates this row. §0.5. **Excluded from Task 2 and outside
the lint's rule by construction.** Its starvation exposure is real and is filed at §6.1 — and it is
the only one of the three that is genuinely multicast, which is worth a row of its own rather than a
rider here.

---

**`C-206` — ⚠ CHANGES THE WORK. Two of the row's "three singletons" are `AddScoped`.**

`Program.cs:475` — `AddScoped<DeviceDisplayStateService>()`; `Program.cs:476` —
`AddScoped<RadioPanelToggleService>()`. Only `PhoneUnreadState` (`:431`) is a singleton, and it is the
one that is not this bug (`C-205`). **Scoped means one instance per circuit**, so their invocation
lists cannot accumulate across circuits the way the hub's does, and each has exactly one subscriber
(`Home.razor:36`; `MainLayout.razor:387`). Both are therefore **latent, cheap, and worth folding in**
— §1.2 — but for consistency, not urgency, and the plan must not describe them as live.

---

**`C-207` — ⚠ CHANGES THE WORK. The obvious way to enforce single registration — explicit `add`/`remove`
accessors — breaks eight test files, and the breakage is in reflection rather than in the compiler.**

A field-like `event` gets a compiler-generated private backing field; an event with explicit
accessors does not. Eight test files reach that field by name:
`SleepTests.cs:503,515`, `NowPlayingDockTests.cs:222`,
`NowPlayingPanelTests.cs:455,844,973,1042`, `RadioControlPanelBandSyncTests.cs:171`. The shape, from
`SleepTests.cs:503-506`:

```csharp
    var field = typeof(AudioStateHubService).GetField("NowPlayingChanged",
      BindingFlags.NonPublic | BindingFlags.Instance);
    field.Should().NotBeNull("NowPlayingChanged backing field must exist");
```

📌 **A correction to how this was first reported to me:** these fail **loudly**, not silently — the
`.Should().NotBeNull(...)` is exactly the guard that makes a null field an assertion failure rather
than a `NullReferenceException` three lines later. That is to the tests' credit and it does not change
the conclusion: the accessor route costs eight test files, and it buys an enforcement that `C-203`
says cannot be turned on anyway. Separately, `VisualizerPanelTests.cs:228-237` pins the public event
set via `GetEvents()` — that call **survives** explicit accessors, so it is not a blocker, only
another thing the route would have had to be checked against.

---

**`C-208` — ⚠ CHANGES THE WORK. `ConfigChanged` has zero subscribers anywhere in the solution, and
`AudioStateStore` does not re-expose it or two others.**

`AudioStateHubService.cs:74` declares it; `:284-288` raises it behind a null guard; **nothing
subscribes**, in `src/` or `tests/`. Its only live effect is `_configStoreNotifier?.NotifyReload()` at
`:283`, which is not the event. The comment at `:71-73` — *"Optional for subscribers that want an
immediate re-render"* — describes a subscriber that has never existed. ⛔ **Do not delete it in this
row** (§6.1); do fix the comment (Task 4), and note that it is still one of the fifteen sites Task 1
converts, because a dead raise site is exactly where the next author copies the shape from.

⭐ **The related structural fact matters more.** The store re-exposes 11 of the hub's 14 events
(`AudioStateStore.cs:34-80`); the three missing are `PhoneCallStateChanged`, `EncoderHudChanged` and
`ConfigChanged`. **That is *why* `EncoderHudService` and `SystemConfigPage` subscribe to the hub
directly** — routing them through the store is not currently possible. Any future attempt to make the
store the hub's sole subscriber (§6.2) must add those events to the store first.

---

**`C-209` — the `DuckingService` anchor correction is CONFIRMED, and it needs a further narrowing the
row does not make.**

The row's inherited correction is right: `DuckingService.cs:481-483` is fade-parameter arithmetic
(`CalculateFadeParameters`, declared `:475`) and has nothing to do with events. The real anchor is
`:550-552`, inside the `<remarks>` block spanning `:537-556`:

```
/// This catches; it does not resume the invocation list. A handler that throws still prevents the
/// handlers registered after it from running. That is accepted for two subscribers; anything more
/// would want a GetInvocationList loop and a reason.
```

⚠ **The narrowing:** `DuckingStateChanged` is declared `public event EventHandler<DuckingStateChangedEventArgs>?`
(`DuckingService.cs:90`; interface `IDuckingService.cs:75`) — **synchronous and void-returning**. So it
precedents the **starvation** half of the shape only, never the dropped-`Task` half. Citing it as "the
precedent for the multicast-**await** shape" overstates it. `AudioStateStore.cs:435-436` already cites
it correctly (*"documents the same shape as a known, accepted limitation for two subscribers"*);
**copy that wording, not the row's.**

---

**`C-210` — the aliveness guard cannot be a floor on the rule's own findings, and the in-repo
precedent for the right shape is `VisualizerPanelTests`, not `OPS-2`.**

§0.6 trap 3 and §1.3. A lint whose green state is `violations.Count == 0` is satisfied identically by
a correct rule and by a rule that matches nothing. `LogSafetyLintTests`'s floors prove the *scanner*
ran, which is a different property. `VisualizerPanelTests.cs:233-236` already does the right thing —
*"Prove the instrument before trusting its silence"* — for an assertion about this very class.

---

**`C-211` — `LogSafetyLintTests`'s worktree bug is already REPAIRED. The live risk is a second copy of
`FindRepositoryRoot`, not a regression of it.**

`:313` carries `PHN-5`'s `?? candidates.LastOrDefault()` fallback, and `:279-288` documents why. The
method is `private static`, so a new lint class in the same assembly cannot reuse it and the path of
least resistance is copy-paste. **Task 3a extracts it to an internal `RepositoryRoot` helper used by
both**, which is the only version of "do not reintroduce `C-100`" that survives a second lint being
added later.

---

**`C-212` — filed, NOT fixed. `SystemConfigPage.razor` leaks three handlers onto a process-lifetime
singleton on every navigation.**

`:2219`, `:2229`, `:2239` subscribe `SourceChanged`, `EncoderConnectionChanged` and
`PhoneCallStateChanged` with **anonymous lambdas**, and there is no `-=` anywhere in the file. An
anonymous lambda cannot be unsubscribed even in principle without keeping the delegate. So every visit
to `/system` permanently adds three handlers to a singleton, each closing over a disposed
component's `InvokeAsync`/`StateHasChanged`.

⚠ **It interacts with this row and is still not part of it.** Unbounded list growth makes the
sequential fan-out Task 1 introduces slower over process lifetime, and it is the mechanism by which
`PhoneCallStateChanged` — a one-subscriber event by the census — becomes an N-subscriber event after N
navigations. But it is a **lifecycle** defect, not a fan-out defect; fixing it means giving three
lambdas names and adding an `IDisposable` to a 2000-line page, which is a diff whose review has
nothing to do with this one. §6.1 files it at P2.

---

**`C-213` — ⚠⚠ CHANGES HOW THE TESTS MUST BE WRITTEN. The test harness fires hub events using the very
shape this row exists to delete, so a naive multi-subscriber test would measure the bug rather than
the fix — and pass against a completely unfixed implementation.**

`SleepTests.cs:503-511`, and the same helper shape at `SleepTests.cs:515`,
`NowPlayingDockTests.cs:222`, `NowPlayingPanelTests.cs:455,844,973,1042` and
`RadioControlPanelBandSyncTests.cs:171`:

```csharp
    var del = (Func<NowPlayingDto?, Task>?)field!.GetValue(hub);
    if (del != null)
    {
      await del.Invoke(dto);
    }
```

That is a multicast await, in the test harness. With one subscriber — which is what every one of those
fixtures has — it is correct and nobody would notice. **Register two subscribers and it awaits only the
second**, so a `UI-7` test written through that helper would report "all subscribers awaited" whether
or not Task 1 landed.

⛔ **The lint cannot catch this**: it scans `src/`, and this is `tests/`. §4's third warning states the
rule for new tests (drive the production path, or walk the invocation list in the helper) and §6.1
files the sweep. **This is the single most likely way for this row to ship green and broken.**

### 0.10 Things Builder must NOT do

- ⛔ **Do not implement a single-registration guard, in any form, without the owner reversing §1.1.**
  `C-203`, and §0.8 traces what it does on the box.
- ⛔ **Do not convert the fourteen events to explicit `add`/`remove` accessors.** `C-207`. Eight test
  files reflect the compiler-generated backing field.
- ⛔ **Do not touch `PhoneUnreadState.cs`.** `C-205`. It is not this defect and the repo already says so.
- ⛔ **Do not delete `ConfigChanged`** despite `C-208`, and do not delete the dead `@inject` at
  `Home.razor:4`. Both are filed in §6.1. A dead-code sweep inside a defect-class PR is how the diff
  stops being reviewable.
- ⛔ **Do not fix `SystemConfigPage.razor`'s handler leak.** `C-212`, §6.1.
- ⛔ **Do not give the new lint rule an `OnlyInFile` scope.** `C-152` / §0.6 trap 1.
- ⛔ **Do not copy `FindRepositoryRoot` into the new file.** `C-211`. Extract it (Task 3a).
- ⛔ **Do not edit `docs/BUILDER_QUEUE.md` or `docs/queue/UI-7.md` from this plan.** §8 carries the
  wording for whoever does.
- ⛔ **Do not edit `CLAUDE.md`.** §6.3.
- ⛔ **Do not touch the box.** Nothing in this row needs it before §4.5.

---

## 1. Decision

### 1.1 The enforcement mechanism — **make the SHAPE unreachable, since the SUBSCRIBER COUNT cannot be**

⚠ **This is a substitution for the mechanism the owner named, forced by `C-203`. It is offered as a
decision for the owner, not taken unilaterally — see the end of this section.**

**Route every one of the fifteen raise sites through two private `NotifyAsync` helpers that walk
`GetInvocationList()` and catch per subscriber, and let the lint (§1.3) enforce that no raise site can
ever bypass them again.**

The helpers are not designed here. They are `AudioStateStore.cs:444-463` and `:476-494`, shipped by
`UI-6` on 2026-09-04, transcribed into `AudioStateHubService` with the logger name changed.

Four options were considered against `C-203`.

| Option | Verdict |
|---|---|
| **Single-registration guard that throws on the second `+=`** | ⛔ **Rejected — not implementable.** Twelve of fourteen events have 2-5 production subscribers. It throws in `MainLayout.OnInitializedAsync` on the first circuit and the panel never paints (§0.8). This is the row's own scope-question 1 and it dies on contact with the census. |
| **Replace `event Func<T,Task>` with a plain single delegate the type system cannot multicast** | ⛔ **Rejected, same reason, worse failure mode.** A single delegate field does not *throw* on a second assignment — it **silently replaces** the first. `MainLayout` subscribing after `AudioStateStore` would evict the store from its own eleven events, and every component reading cached state would go quiet with no error anywhere. A silent wrong answer is strictly worse than a loud one. |
| **`GetInvocationList()` fan-out through two shared helpers, lint-enforced** ✅ | **Taken.** Makes the *shape* unreachable, which is what the owner's argument actually asked for. Zero behavioural constraint on subscribers. Copies an implementation that shipped four days ago with its rationale attached. |
| **A generic `AsyncEvent<T>` wrapper type replacing the `event` keyword outright** | ⛔ **Rejected as out of scope, and it is the honest strongest option.** A type exposing only `Subscribe`/`RaiseAsync` makes the defect unrepresentable rather than merely detectable — no lint needed. It also changes the backing field type (breaking `C-207`'s eight test files), changes the public API of a class whose event set is pinned by `VisualizerPanelTests.cs:228-237`, and touches all eight subscribing components. That is a refactor with its own design conversation. §6.2 files it. |

⭐ **Why the lint is load-bearing here in a way it would not be under the rejected options.** The
chosen option leaves the fourteen `event` declarations exactly as they are, so nothing in the type
system stops author fifteen from writing `await SomeEvent.Invoke()` next to the helpers. **The lint is
what converts a convention into a constraint** — it is not a belt-and-braces addition to the
enforcement, it *is* the enforcement. That is why the owner's instinct to build it now rather than
file `UI-8` is right, and it is more right under this option than under the one they were choosing
between.

⚠ **What this option does NOT buy, stated so nobody reads more into a green suite.** It makes every
subscriber's exception observed and stops one throwing subscriber starving the rest. It does **not**
reduce the subscriber count, does not fix `C-212`'s unbounded growth, and does not make the fan-out
cheaper — §0.8 names the sequential-dispatch trade explicitly.

> 📌 **Owner decision required before Task 1.** The row's scope question 1 offered "fix 14 sites" vs
> "make a single subscriber structurally enforced", and the second was chosen. The census says the
> second does not exist. **This plan proposes the third thing neither option named: fix all fifteen
> sites *through a single seam*, and make the seam mandatory with the lint.** It is closer to the
> chosen option than to the rejected one — the sites stop mattering because the shape is gone, which
> is the owner's own stated reasoning — but it is not what was approved, and Builder should have a
> yes before starting.

### 1.2 The three one-line sites — **fold the two real ones in; exclude the third**

**Decided, not left open.**

**Fold in `RadioPanelToggleService.cs:64` and `DeviceDisplayStateService.cs:22`** (Task 2). Three
reasons, in order of weight:

1. **The lint would fail on them anyway.** The rule in §1.3 is global and spelling-independent; both
   files declare `event Func<Task>` and raise it with `await X.Invoke()`. Landing Task 3 without Task 2
   means landing a red lint, so they are not optional — they are a dependency.
2. They are one line each, in files of 67 and 25 lines, with one subscriber each and no ambiguity.
3. `UI-6` grew from one site to three once the shape was visible; leaving two known sites behind in a
   row whose entire purpose is closing the class would be filing `UI-8` on purpose.

⛔ **Exclude `PhoneUnreadState.cs:23`.** `C-205` — it is `event Action<int>`, not this defect, the
repository already documents the exclusion at `ConsolePlaybackState.cs:44-46`, and the lint's rule
does not match it. Its genuine starvation exposure is filed at §6.1.

⚠ **Both folded-in sites get the loop inline, not a shared base class.** They are unrelated scoped
services in different files with one event each; extracting a common helper would create a coupling
between a device-display service and a radio-panel toggle that exists only because they share a bug.
Four lines each, with a comment pointing at `AudioStateStore.NotifyAsync` as the canonical version.

### 1.3 The lint's shape — global, declaration-keyed, and alive by positive control

**File:** `tests/Radio.Core.Tests/AsyncEventFanOutLintTests.cs` (new). Same project and same idiom as
`LogSafetyLintTests`, which scans `src/` and therefore needs no project reference to `Radio.Web`.

**The rule, in one sentence:** *in any file that declares an `event Func<…>`, the declared event's name
— or a local assigned from it — may never be the receiver of `.Invoke(` or be invoked directly.*

Three properties make it rename-proof and spelling-independent, which is what `C-152` costs to get
wrong:

- **Keyed on the declaration, not on a filename.** No `OnlyInFile`. The rule reads each file's own
  `event Func<` declarations and enforces against *those names*, so it applies automatically to a file
  nobody has written yet and survives every rename.
- **Two passes, because one is provably insufficient.** Pass 1 collects declared event names. Pass 2
  forbids `Name.Invoke(` / `Name?.Invoke(` / `Name!.Invoke(` and bare `Name(`. **Pass 2b handles the
  alias**: `var handler = ConfigChanged;` at `AudioStateHubService.cs:284` followed by
  `await handler.Invoke();` at `:287` is a real site in the tree today and matches no direct rule.
  ⚠ **A rule that skipped 2b would cover fourteen of fifteen sites while claiming fifteen** — the exact
  failure `LogSafetyLintTests`'s own remarks record for `P7` (*"this lint covered TEN of the row's
  eleven sites while its own summary claimed eleven"*). Do not repeat it.
- **No allowlist is needed, and that is a property of the fix rather than luck.** After Task 1 the
  event names appear only as *arguments* (`await NotifyAsync(PlaybackStateChanged)`), never as
  receivers. The three correct fan-outs in the tree invoke a cast local — `subscriber` at
  `AudioStateStore.cs:456`, `handler` at `ConsolePlaybackState.cs:102`, `func` at
  `AudioVisualizationHubService.cs:508-509` — and none of those is a declared event name. ⚠ **If this
  rule ever needs an exemption, the exemption is the bug** — `LogSafetyLintTests.cs:72-73` makes the
  same argument about `phoneNumber` and it applies verbatim here.

**Scope:** `src/**/*.cs` **and** `src/**/*.razor`. `event Func<` occurs in exactly six files today, all
`.cs` — but a `.razor` `@code` block can declare one, and including the extension costs a glob.

**The aliveness guard — three layers, and the first is the one that matters.** `C-210`.

1. ⭐ **A positive control, in the test file, independent of the tree.** Drive the rule against a
   verbatim copy of the pre-fix text and require it to **fire**. Two fixtures, because there are two
   shapes: the direct raise (`AudioStateHubService.cs:139-141`) and the aliased one (`:284-287`). Plus
   a negative control: the corrected shape must **not** fire. **This is what a "must be zero" lint
   cannot get from a floor on its own findings**, and it is the layer `GV-6` was missing.
2. **A tree floor on the corrected shape, which must be non-zero.** At least 5 files under `src/`
   declare `event Func<` (there are exactly 6 — `AudioStateHubService`, `AudioStateStore`,
   `ConsolePlaybackState`, `AudioVisualizationHubService`, `RadioPanelToggleService`,
   `DeviceDisplayStateService`), and at least 5 `GetInvocationList()` fan-out sites exist (post-fix
   there are 6). Floors, not exact counts — the `LogSafetyLintTests.cs:231-236` convention.
3. **Scan floors**, as `LogSafetyLintTests` has: the `src` directory exists, and the file count is
   above a floor. These catch "scanned the wrong tree", not "wrote a dead regex".

---

## 2. Tasks

### Task 1 — `AudioStateHubService`'s fifteen raise sites go through one seam

**File:** `src/Radio.Web/Services/Hub/AudioStateHubService.cs`

**1a.** Add the two helpers at the end of the class, before `IsConnectionRefused` (`:484`). These are
transcribed from `AudioStateStore.cs:444-494`; the `<remarks>` are rewritten for this class because the
subscriber facts are different and `C-203` is the reason this row exists.

```csharp
  /// <summary>
  /// Awaits every subscriber of a parameterless hub event in registration order, catching and
  /// logging each one's exception separately.
  /// </summary>
  /// <remarks>
  /// ⚠ The <see cref="Delegate.GetInvocationList"/> loop is the whole point of this method, and the
  /// one-liner it replaces was wrong in two independent ways (queue row `UI-7`, and `UI-6` before it
  /// for the same shape in <see cref="Radio.Web.Services.AudioStateStore"/>).
  ///
  /// <c>await SomeEvent.Invoke()</c> on a multicast <c>Func&lt;Task&gt;</c> RUNS every subscriber but
  /// returns only the LAST one's <see cref="Task"/>. Every earlier subscriber ran to its first
  /// <c>await</c> and its continuation was never observed, so the caller's <c>await</c> completed
  /// while N−1 handlers were still in flight and their exceptions faulted tasks nobody held.
  ///
  /// ⚠ The sharper half: a subscriber that throws SYNCHRONOUSLY — before its first <c>await</c> —
  /// threw out of <c>Invoke</c> itself, so every handler registered AFTER it never ran at all. That
  /// is starvation, not a lost log line, and catching INSIDE the loop is what resumes the list.
  /// <see cref="Radio.Infrastructure.Audio.Services.DuckingService"/> documents the same shape as a
  /// known, accepted limitation for two subscribers and says a third would want exactly this loop —
  /// note that its event is a synchronous EventHandler&lt;T&gt;, so it precedents the starvation half
  /// only (plan UI-7 C-209).
  ///
  /// ⚠⚠ THIS CLASS IS NOT THE DORMANT CASE, AND THE ROW THAT FILED IT SAID IT WAS. `UI-7` C-203:
  /// AudioStateStore is NOT the only subscriber. Ten production types subscribe — the store,
  /// EncoderHudService, and eight rendered components — and this service is registered AddSingleton
  /// (Program.cs:411) while the components subscribe PER CIRCUIT. Two open browsers already puts nine
  /// handlers on NowPlayingChanged. Every consequence above was happening on the appliance.
  /// ⛔ Do NOT "simplify" this back to a null check and an Invoke.
  ///
  /// ⚠ Subscribers now run SEQUENTIALLY rather than being started back-to-back, and the invocation
  /// list here is longer than the store's. The handlers are Blazor InvokeAsync(StateHasChanged)
  /// dispatches, which queue onto their own circuit's renderer and return, so serializing them costs
  /// a dispatch each rather than a render each — AudioStateStore.cs:438-442 makes the same argument.
  /// </remarks>
  private async Task NotifyAsync(Func<Task>? handler)
  {
    if (handler == null)
    {
      return;
    }

    foreach (var subscriber in handler.GetInvocationList())
    {
      try
      {
        // Inside the try, so a synchronous throw is caught and the NEXT subscriber still runs.
        await ((Func<Task>)subscriber).Invoke();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error notifying AudioStateHubService subscriber");
      }
    }
  }

  /// <summary>
  /// Awaits every subscriber of a hub event that carries a payload, in registration order, catching
  /// and logging each one's exception separately.
  /// </summary>
  /// <remarks>
  /// The generic twin of the parameterless overload above — see its remarks for why the loop exists.
  /// Generic rather than duplicated per event because there are THIRTEEN events between the two
  /// overloads here; thirteen hand-rolled loops is thirteen chances to drift, which is precisely what
  /// UI-6 found when AudioStateStore's two hand-rolled sites had already diverged (one carried a
  /// try/catch and the other carried none).
  /// </remarks>
  private async Task NotifyAsync<T>(Func<T, Task>? handler, T arg)
  {
    if (handler == null)
    {
      return;
    }

    foreach (var subscriber in handler.GetInvocationList())
    {
      try
      {
        await ((Func<T, Task>)subscriber).Invoke(arg);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error notifying AudioStateHubService subscriber");
      }
    }
  }
```

**1b.** Rewrite the fifteen raise sites. Each collapses a four-line `if (X != null) { await X.Invoke(…); }`
to a single call — the null check now lives in the helper. **The mapping is exhaustive; work down it.**

| # | Line | Before | After |
|---|---|---|---|
| 1 | 138-141 | `if (PlaybackStateChanged != null) { await PlaybackStateChanged.Invoke(); }` | `await NotifyAsync(PlaybackStateChanged);` |
| 2 | 149-152 | `… NowPlayingChanged.Invoke(dto);` | `await NotifyAsync(NowPlayingChanged, dto);` |
| 3 | 160-163 | `… QueueChanged.Invoke();` | `await NotifyAsync(QueueChanged);` |
| 4 | 174-177 | `… RadioStateChanged.Invoke(dto);` | `await NotifyAsync(RadioStateChanged, dto);` |
| 5 | 185-188 | `… VolumeChanged.Invoke(dto);` | `await NotifyAsync(VolumeChanged, dto);` |
| 6 | 194-197 | `… SourceChanged.Invoke();` | `await NotifyAsync(SourceChanged);` |
| 7 | 205-208 | `… FingerprintStatusChanged.Invoke();` | `await NotifyAsync(FingerprintStatusChanged);` |
| 8 | 215-218 | `… PhoneCallStateChanged.Invoke();` | `await NotifyAsync(PhoneCallStateChanged);` |
| 9 | 227-230 | `if (EncoderConnectionChanged != null && dto != null) { … }` | ⚠ see note | 
| 10 | 237-240 | `if (EncoderConfigStatusChanged != null && dto != null) { … }` | ⚠ see note |
| 11 | 247-250 | `if (EncoderHudChanged != null && dto != null) { … }` | ⚠ see note |
| 12 | 257-260 | `… SleepStateChanged.Invoke(isSleeping);` | `await NotifyAsync(SleepStateChanged, isSleeping);` |
| 13 | 268-271 | `… EventPlaybackChanged.Invoke(dto);` | `await NotifyAsync(EventPlaybackChanged, dto);` |
| 14 | 284-288 | `var handler = ConfigChanged; if (handler != null) { await handler.Invoke(); }` | `await NotifyAsync(ConfigChanged);` |
| 15 | 430-433 | `… SourceChanged.Invoke();` (in `NotifySourceChangedAsync`) | `await NotifyAsync(SourceChanged);` |

⚠ **Sites 9, 10 and 11 carry a SECOND condition and it must survive.** They read
`if (SomeEvent != null && dto != null)` — the `dto != null` half is a payload guard, not a subscriber
guard, and `NotifyAsync` does not replace it. Write:

```csharp
        if (dto != null)
        {
          await NotifyAsync(EncoderConnectionChanged, dto);
        }
```

⛔ **Do not collapse these to `await NotifyAsync(EncoderConnectionChanged, dto);` unguarded.** The
payload is non-nullable in the delegate signature (`Func<EncoderConnectionDto, Task>`), so dropping the
check hands `null` to subscribers typed to receive a value. Sites 2, 5 and 13 are different — their
delegates are `Func<…Dto?, Task>` and a null payload is meaningful there.

⚠ **Site 14 keeps `_configStoreNotifier?.NotifyReload();` at `:283` exactly where it is**, before the
notify. It is the cross-process config reload and it is not part of the event fan-out. And site 14 is
the one the alias rule (§1.3 pass 2b) exists for — after this edit it no longer needs a local at all.

⚠ **Site 15 is the one a sweep misses** (`C-204`). It is 140 lines below the others, in
`NotifySourceChangedAsync`. The row's count of 14 does not include it.

**1c.** Correct the class summary at `:9-16`, which lists the fourteen events and says nothing about
how they are raised:

```csharp
/// <summary>
/// SignalR hub service for real-time audio state updates
/// Handles: PlaybackStateChanged, NowPlayingChanged, QueueChanged,
/// RadioStateChanged, VolumeChanged, SourceChanged, FingerprintStatusChanged,
/// PhoneCallStateChanged, EncoderConnectionChanged,
/// EncoderConfigStatusChanged, EncoderHudChanged, SleepStateChanged, EventPlaybackChanged,
/// ConfigChanged
/// </summary>
/// <remarks>
/// ⚠ EVERY event on this class is raised through <see cref="NotifyAsync(Func{Task})"/> or its
/// generic twin, and a new one must be too — see those methods' remarks for what the direct
/// <c>await SomeEvent.Invoke()</c> form does wrong (queue row `UI-7`). This is enforced, not
/// requested: <c>AsyncEventFanOutLintTests</c> in <c>Radio.Core.Tests</c> fails the build if any
/// event declared in this file is invoked directly.
///
/// ⚠ This is a SINGLETON (Program.cs:411) and its component subscribers are PER CIRCUIT, so the
/// invocation list grows with open browsers. Ten production types subscribe; `UI-7` §0.2 has the
/// census. Anything reasoning about "the subscriber" of this class is reasoning about a class that
/// does not exist.
/// </remarks>
```

---

### Task 2 — the two scoped services that are actually this bug

**⛔ Not `PhoneUnreadState`.** `C-205`.

**File:** `src/Radio.Web/Services/RadioPanelToggleService.cs`

Replace `:62-65`:

```csharp
      IsRadioPanelVisible = value;
      if (RadioPanelToggled != null)
      {
        await RadioPanelToggled.Invoke();
      }
```

with:

```csharp
      IsRadioPanelVisible = value;

      // UI-7. `await RadioPanelToggled.Invoke()` on a multicast Func<Task> runs every subscriber but
      // returns only the LAST one's Task, and a subscriber throwing synchronously starves every
      // handler after it. This service is AddScoped (Program.cs:476) with one subscriber today
      // (Home.razor:36), so the list is length 1 and the defect was latent here — but "one
      // subscriber" is a fact about today, not a constraint, which is exactly the reasoning UI-7
      // C-203 found to be false about AudioStateHubService. The canonical version of this loop, with
      // the full argument, is AudioStateStore.NotifyAsync.
      if (RadioPanelToggled == null)
      {
        return;
      }

      foreach (var subscriber in RadioPanelToggled.GetInvocationList())
      {
        try
        {
          await ((Func<Task>)subscriber).Invoke();
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Error notifying RadioPanelToggled subscriber");
        }
      }
```

⚠ **This class has no logger today.** Builder adds `ILogger<RadioPanelToggleService>` as a constructor
dependency; it is `AddScoped` so DI supplies it, and any test constructing it directly needs
`NullLogger<RadioPanelToggleService>.Instance`. **Check for such tests before editing** — if none
exists, say so in the PR body rather than implying the seam was verified.

**File:** `src/Radio.Web/Services/DeviceDisplayStateService.cs`

Same transformation on `:18-24`. Same comment, with `Program.cs:475`, `AddScoped`, and the one
subscriber at `MainLayout.razor:387`.

⚠ **And correct the doc comment at `:16`**, which currently reads *"Notifies all subscribers that
display settings have changed."* That is the `CLAUDE.md` § *Pre-Merge Review* class exactly: all
subscribers **ran**, but only the last was **awaited**, so the comment asserted a property the code did
not have. After the change it becomes true — say so:

```csharp
  /// <summary>
  /// Notifies every subscriber that display settings have changed, awaiting each one and isolating
  /// its exceptions. UI-7 — before that row this said "all subscribers" while awaiting only the last.
  /// </summary>
```

---

### Task 3 — the detector

#### Task 3a — extract `FindRepositoryRoot` before writing a second copy of it

**`C-211`.** **New file:** `tests/Radio.Core.Tests/RepositoryRoot.cs`

Move `FindRepositoryRoot` and `IsInsideAWorktree` **verbatim** out of `LogSafetyLintTests.cs:290-342`,
including their `<remarks>` — that documentation is the record of `C-100` and what `PHN-5` did to it,
and it must not be lost in the move. Make the type `internal static class RepositoryRoot` with a
`public static string Find()`, and add one paragraph:

```csharp
  /// ⚠ EXTRACTED IN UI-7, and the extraction is the point rather than tidiness. This method was
  /// private to LogSafetyLintTests, so the second source-scanning lint in this assembly
  /// (AsyncEventFanOutLintTests) could not call it and the path of least resistance was a copy. Two
  /// copies is two chances to diverge, and the pre-PHN-5 version of this method — LastOrDefault with
  /// no fallback — FAILED in any checkout under a directory named "worktrees", which is the
  /// convention this repository uses. A copy made from memory would very likely be that version.
  /// One implementation, used by every lint.
```

Then replace the two call sites in `LogSafetyLintTests` (`:187` and the method body) with
`RepositoryRoot.Find()` and delete the originals.

⛔ **No behaviour change.** `LogSafetyLintTests` must pass identically before and after 3a; that is
mutation `M6` in §4.

#### Task 3b — the lint

**New file:** `tests/Radio.Core.Tests/AsyncEventFanOutLintTests.cs`

```csharp
using System.Text.RegularExpressions;

namespace Radio.Core.Tests;

/// <summary>
/// A lint over <c>src/**/*.cs</c> and <c>src/**/*.razor</c> that fails if an <c>event Func&lt;…&gt;</c>
/// is raised by invoking it directly instead of walking <see cref="Delegate.GetInvocationList"/>.
/// </summary>
/// <remarks>
/// ⚠ WHAT THIS FORBIDS AND WHY. `await SomeEvent.Invoke()` on a multicast Func&lt;Task&gt; RUNS every
/// subscriber but returns only the LAST one's Task, so every earlier subscriber's continuation is
/// unobserved and a try/catch around the invoke protects exactly one of N. Worse, a subscriber that
/// throws SYNCHRONOUSLY throws out of Invoke itself and every handler registered after it never runs.
/// Queue rows `UI-6` (3 sites) and `UI-7` (17). The canonical correct form, with the full argument, is
/// <c>AudioStateStore.NotifyAsync</c>.
///
/// ⚠ THIS RULE IS DELIBERATELY NOT SCOPED TO A FILENAME, and that is a lesson from the file beside it.
/// <c>LogSafetyLintTests</c> has five rules narrowed by a literal filename compared with
/// StringComparison.Ordinal; every one of them silently disables itself on a rename and reports green
/// forever (plan UI-7 §0.6, `C-152`). This rule reads each file's OWN event declarations and enforces
/// against those names, so it applies to files nobody has written yet and survives every rename.
///
/// ⚠ IT IS A REGRESSION LINT OVER A SHAPE, NOT A PROOF OF THE PROPERTY. It parses C# with regexes.
/// It knows the two forms that occurred — a direct raise and a raise through a local aliased from the
/// event — and it does not model partial classes, so an event declared in one file and raised in
/// another part of the same type would sail past. Nothing in the tree does that today.
///
/// ⚠ IF THIS RULE EVER NEEDS AN EXEMPTION, THE EXEMPTION IS THE BUG. There is no allowlist and there
/// must not be one; <c>LogSafetyLintTests</c> makes the same argument about `phoneNumber` at its own
/// :64-73 and it applies verbatim. A new file matching this rule is a new instance of the defect.
///
/// ⛔ event Action&lt;T&gt; and EventHandler&lt;T&gt; are OUT OF SCOPE and the rule does not match them.
/// They are void-returning, so Invoke really does run every handler and there is no discarded Task.
/// Only the starvation half applies to them, and that is a different (open) question — see
/// PhoneUnreadState.cs:23 and plan UI-7 §6.1.
/// </remarks>
public class AsyncEventFanOutLintTests
{
  /// <summary>Matches an async event declaration and captures its name.</summary>
  /// <remarks>
  /// Covers <c>event Func&lt;Task&gt;? X;</c>, <c>event Func&lt;T, Task&gt;? X;</c> and the
  /// non-nullable spellings. The type argument list is matched loosely — anything up to the closing
  /// angle bracket — because what makes this the defect is the delegate being awaited, not which
  /// payload it carries.
  /// </remarks>
  private static readonly Regex AsyncEventDeclaration = new(
    @"\bevent\s+Func\s*<[^>]*>\s*\??\s*(?<name>\w+)\s*[;=]", RegexOptions.Compiled);

  [Fact]
  public void NoAsyncEventInTheSolutionIsRaisedByDirectInvoke()
  {
    var src = Path.Combine(RepositoryRoot.Find(), "src");

    // The scan must be provably alive before its silence means anything.
    Assert.True(Directory.Exists(src), $"Expected a source tree at '{src}'.");

    var files = Directory
      .EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
      .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
      .Where(f => !IsGenerated(f))
      .ToList();

    var violations = new List<string>();
    var declaringFiles = 0;
    var fanOutSites = 0;

    foreach (var file in files)
    {
      var text = File.ReadAllText(file);
      fanOutSites += Regex.Matches(text, @"\bGetInvocationList\s*\(\s*\)").Count;

      var found = Violations(text).ToList();
      if (AsyncEventDeclaration.IsMatch(text))
      {
        declaringFiles++;
      }

      foreach (var (offset, shape) in found)
      {
        var line = text.Take(offset).Count(c => c == '\n') + 1;
        violations.Add($"{Path.GetRelativePath(src, file)}:{line} raises [{shape}]");
      }
    }

    // ⚠ Floors on things that must be NON-zero. A "violations == 0" assertion is satisfied just as
    // well by a regex that matches nothing, so these are what stop this test passing forever after
    // someone breaks the extractor. See also TheRuleFiresOnTheShapesItExistsToForbid below, which is
    // the layer that does not depend on the tree at all.
    Assert.True(files.Count > 200, $"Only {files.Count} source files found under '{src}'.");
    Assert.True(
      declaringFiles >= 5,
      $"Only {declaringFiles} files declare an `event Func<...>` under '{src}' — there were 6 when "
      + "this was written, so the declaration regex is probably broken.");
    Assert.True(
      fanOutSites >= 5,
      $"Only {fanOutSites} GetInvocationList() fan-out sites found under '{src}' — there were 6 "
      + "after UI-7, so either a fix was reverted or the scan is looking at the wrong tree.");

    Assert.True(
      violations.Count == 0,
      "UI-6 / UI-7: an `event Func<...>` must be raised by walking GetInvocationList() and awaiting "
      + "each subscriber inside its own try/catch — never by `await TheEvent.Invoke(...)`, which "
      + "returns only the LAST subscriber's Task and lets a synchronous throw starve the rest. "
      + $"Scanned '{src}'. Copy AudioStateStore.NotifyAsync.\n  "
      + string.Join("\n  ", violations));
  }

  /// <summary>
  /// ⭐ The positive control. Without it this class is unfalsifiable: the tree is expected to be
  /// clean, so the scan above can only ever assert zero, and a regex matching nothing asserts zero
  /// just as well. These fixtures are the VERBATIM pre-UI-7 text of two real sites.
  /// </summary>
  /// <remarks>
  /// The idiom is <c>VisualizerPanelTests.cs:233-236</c>'s — "prove the instrument before trusting
  /// its silence" — applied to a source scan instead of a reflection call.
  /// </remarks>
  [Fact]
  public void TheRuleFiresOnTheShapesItExistsToForbid()
  {
    // AudioStateHubService.cs:39 + :138-141 as they stood at 084a6bbd — the direct raise.
    const string DirectRaise = """
      public event Func<Task>? PlaybackStateChanged;
            if (PlaybackStateChanged != null)
            {
              await PlaybackStateChanged.Invoke();
            }
      """;

    // AudioStateHubService.cs:74 + :284-288 as they stood at 084a6bbd — the ALIASED raise, which no
    // direct rule reaches. A version of this lint without the alias pass would cover fourteen of the
    // row's fifteen sites while claiming fifteen; LogSafetyLintTests records exactly that mistake
    // about its own P7 site.
    const string AliasedRaise = """
      public event Func<Task>? ConfigChanged;
            var handler = ConfigChanged;
            if (handler != null)
            {
              await handler.Invoke();
            }
      """;

    // The corrected shape, which must NOT fire — the event name appears only as an ARGUMENT.
    const string Corrected = """
      public event Func<Task>? PlaybackStateChanged;
            await NotifyAsync(PlaybackStateChanged);
            foreach (var subscriber in handler.GetInvocationList())
            {
              await ((Func<Task>)subscriber).Invoke();
            }
      """;

    Assert.Single(Violations(DirectRaise));
    Assert.Single(Violations(AliasedRaise));
    Assert.Empty(Violations(Corrected));
  }
```

**The `Violations` extractor.** Two passes over one file's text; the second is why site 14 is covered.

```csharp
  /// <summary>
  /// Yields (offset, shape) for every direct raise of an async event declared in this same text.
  /// </summary>
  /// <remarks>
  /// Pass 1 collects the declared event names. Pass 2 forbids each name as the receiver of
  /// <c>.Invoke(</c> or as a direct call. Pass 2b resolves ONE level of aliasing —
  /// <c>var handler = SomeEvent;</c> — because that is a real shape in the tree
  /// (AudioStateHubService.cs:284-287) and it matches no direct rule. One level is enough for
  /// everything that exists; a chain of two would not be caught, and that is stated rather than
  /// implied.
  /// </remarks>
  private static IEnumerable<(int Offset, string Shape)> Violations(string text)
  {
    var names = AsyncEventDeclaration.Matches(text)
      .Select(m => m.Groups["name"].Value)
      .Distinct()
      .ToList();

    if (names.Count == 0)
    {
      yield break;
    }

    // Pass 2b — locals aliased from a declared event, one level.
    var aliases = new List<string>();
    foreach (var name in names)
    {
      foreach (Match m in Regex.Matches(text, @"\bvar\s+(?<alias>\w+)\s*=\s*" + Regex.Escape(name) + @"\s*;"))
      {
        aliases.Add(m.Groups["alias"].Value);
      }
    }

    foreach (var receiver in names.Concat(aliases).Distinct())
    {
      // `X.Invoke(`, `X?.Invoke(`, `X!.Invoke(` — the receiver form, which is all fifteen UI-7 sites.
      foreach (Match m in Regex.Matches(
        text, @"\b" + Regex.Escape(receiver) + @"\s*[!?]?\s*\.\s*Invoke\s*\("))
      {
        yield return (m.Index, receiver + ".Invoke(...)");
      }

      // `X(` — direct delegate invocation. Not present in the tree today; it is the same defect
      // spelled differently and costs one alternation to cover.
      // ⚠ The lookbehind excludes a declaration and a method named the same thing.
      foreach (Match m in Regex.Matches(
        text, @"(?<![\w.])" + Regex.Escape(receiver) + @"\s*\((?!\s*\))"))
      {
        yield return (m.Index, receiver + "(...)");
      }
    }
  }

  private static bool IsGenerated(string path) =>
    path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
    path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
```

⚠ **The direct-call arm (`X(`) is the one likeliest to false-positive**, because an event and a method
can share a name and because `NotifyAsync(PlaybackStateChanged)` puts the name in front of a `)` not a
`(`. **Builder runs the lint against the tree BEFORE Task 1 and records the count** — it should report
exactly the fifteen sites of `C-204` plus the two of Task 2, i.e. **seventeen**, and nothing else. Any
extra hit is a false positive to be understood, not suppressed. ⛔ **If the direct-call arm produces
false positives that cannot be fixed cleanly, delete that arm and say so in the file** — it covers a
shape that does not occur, and a rule with a suppression list is worth less than a narrower rule
without one.

---

### Task 4 — the comments that assert more than the code does

Three, all `CLAUDE.md` § *Pre-Merge Review* shape.

1. **`AudioStateHubService.cs:24-25`** — *"the ~9 test fixtures that new this service up directly"*.
   The real count is **20 test files** (18 direct `new`, 2 DI-activated). Replace `~9` with `~20`, or
   better, drop the number: it was wrong within days of being written and the sentence works without
   it — *"so the test fixtures that new this service up directly keep compiling"*.
2. **`AudioStateHubService.cs:71-73`** — *"Optional for subscribers that want an immediate re-render"*
   describes a subscriber that has never existed (`C-208`). Correct it to say the event has **zero**
   subscribers today, that the live effect is `_configStoreNotifier?.NotifyReload()` at `:283`, and
   that it is retained deliberately (§6.1) rather than overlooked.
3. **`DeviceDisplayStateService.cs:16`** — *"Notifies all subscribers"*. Handled in Task 2.

📌 **Also correct one plan-adjacent citation while in the file:** `MainLayout.razor:1374` cites
`ConsolePlaybackState.cs:88-100` for the isolating loop; the method is at `:90-111` with the
`try`/`catch` at `:100-109`. One-character-class change, and it is the same drift-of-anchors problem
`C-209` is about.

---

### Task 5 — tests

Detailed in §4. Listed as a task so it is not treated as optional.

---

## 3. Ordering

**Task 1 → 2 → 3a → 3b → 4 → 5.**

- **1 and 2 before 3b.** The lint fails on all seventeen sites until they are fixed. Landing 3b first
  means landing a red suite.
- **3a before 3b.** 3b calls `RepositoryRoot.Find()`.
- **4 any time.**

**One PR.** The deliverable is a property — *no async event in this solution is raised by direct
invoke* — and it is not true until all seventeen sites and the detector are in. Splitting the lint out
would produce a PR that can only be merged red, and splitting the sites out would produce a PR whose
value ("the shape is now unreachable") is not yet true. `AUD-11` §3 and `PHN-5` §3 made the same
argument.

⚠ **Task 3a touches `LogSafetyLintTests`, a file unrelated to this row's subject.** Call it out in the
PR body as a mechanical extraction with `M6` as its evidence, so a reviewer does not have to work out
why a phone-number lint is in a multicast-await diff.

---

## 4. Test plan

> ⚠ **This repository has repeatedly found tests that passed against a deliberately broken
> implementation.** Every pin below names the mutation that must make it fail. **Builder runs each
> mutation and records the result in the PR body. A mutation that does not make its test fail is a
> finding, not a formality.**

> ⚠ **`CLAUDE.md` § *Test Timing* applies, and there is a specific trap here.** The natural way to test
> "all N subscribers ran" is to have handler 1 `await Task.Delay(50)` and then assert. That races two
> clocks with no rendezvous — `TEST-4`'s exact shape. **Synchronize on the observation:** each fake
> subscriber appends its own id to a `ConcurrentQueue` and completes a `TaskCompletionSource`; the
> assertion is on the queue's contents after the notify call returns, never on elapsed time. ⛔ **No
> `await Task.Delay(...)` followed by an assertion.**

> ⚠⚠ **`C-213` — THE TEST HARNESS ITSELF CONTAINS THE DEFECT, and a naive multi-subscriber test would
> measure the bug instead of the fix.** `SleepTests.cs:503-511` fires hub events by reflecting the
> private backing field and then calling `await del.Invoke(dto)` — a multicast await, in the test. With
> one subscriber that is fine and it is why nobody noticed. **A `UI-7` test that registers two
> subscribers and fires through that helper would await only the second one**, and could report a pass
> for a completely unfixed implementation. **Every new test in §4.1 must call the production path
> (`_service.NotifySourceChangedAsync()`) or fire via a helper that itself walks the invocation list.**
> The lint does not catch this, because it scans `src/` and this is in `tests/`. §6.1 files a sweep.

### 4.1 `T1` — the hub fan-out

**File:** `tests/Radio.Web.Tests/Services/AudioStateHubServiceNotifyTests.cs` (new)

The reachable production seam without a live SignalR connection is `NotifySourceChangedAsync()`
(`AudioStateHubService.cs:428-434`, site 15), which raises `SourceChanged` with no hub involvement.
`Radio.Web.csproj` already declares `InternalsVisibleTo("Radio.Web.Tests")`, so if a second seam is
wanted, making one raise-helper `internal` is available — **prefer the public path**; `PHN-2`'s review
found a test priming through a method whose only caller was that test.

| Test | Pins | Falsifying mutation |
|---|---|---|
| `NotifySourceChanged_AwaitsEverySubscriber` | 3 subscribers, each completing after an `await Task.Yield()`; all 3 recorded by the time the call returns | **`M1`** — replace `NotifyAsync` with `await SourceChanged.Invoke()` → only the last is observed, fails |
| `NotifySourceChanged_RunsSubscribersInRegistrationOrder` | recorded ids are `[1,2,3]` | reverse `GetInvocationList()` → fails |
| ⭐ `NotifySourceChanged_SynchronousThrow_DoesNotStarveLaterSubscribers` | subscriber 2 throws **before any await**; 1 and 3 both recorded | **`M2`** — move the `try` outside the `foreach` → 3 never runs, fails |
| `NotifySourceChanged_AsyncThrow_IsObservedAndLogged` | subscriber 2 throws **after** an `await`; a `LogWarning` is recorded | **`M3`** — delete the `catch` → the exception escapes, fails |
| `NotifySourceChanged_NoSubscribers_DoesNotThrow` | the `handler == null` early return | remove the null guard → `NullReferenceException`, fails |
| `NotifySourceChanged_OneSubscriber_StillCompletes` | that the loop is not an N>1 special case | — |

⚠ **`M2` is the headline mutation** and it is the one `UI-6` proved matters: it distinguishes "wrapped
the whole thing in a try/catch" — which looks like a fix, passes a casual review, and leaves starvation
completely intact — from the loop that actually resumes the list.

**Generic overload.** The same six against `Func<T, Task>`. `NotifySourceChangedAsync` cannot reach it,
so drive it through `AudioStateStore`'s existing seams — `OnHubVolumeChanged` / `OnHubRadioStateChanged`
are already `internal` for exactly this purpose (`AudioStateStore.cs:188-204`, `:230-239`) — **or** state
plainly in the file that the generic overload is pinned only by the store's existing
`AudioStateStoreNotifyTests`. ⛔ **Do not add an `internal` seam to `AudioStateHubService` whose only
caller is a test** without saying so in the file.

### 4.2 `T2` — the two scoped services

**Files:** `tests/Radio.Web.Tests/Services/RadioPanelToggleServiceTests.cs`,
`DeviceDisplayStateServiceTests.cs` (new or extended)

Both have a public raise path (`SetIsVisibleAsync` via `ShowRadioPanelAsync`;
`NotifyDisplaySettingsChangedAsync`), so no seam is needed. Three tests each: awaits every subscriber;
a synchronous throw does not starve the rest; no subscribers does not throw.

**Mutation `M4`:** revert either file's loop to `await X.Invoke()` → the "awaits every subscriber" test
fails **in that file only**. ⚠ **That the blast radius is one file is itself the assertion** — if
reverting `RadioPanelToggleService` also fails a `DeviceDisplayStateService` test, the two share state
they should not.

### 4.3 `T3` — the lint

**File:** `tests/Radio.Core.Tests/AsyncEventFanOutLintTests.cs`

`TheRuleFiresOnTheShapesItExistsToForbid` (Task 3b) is itself the aliveness test and runs on every
build. Its mutations:

| Mutation | Expected |
|---|---|
| **`M5a`** — delete the alias pass (2b) | `TheRuleFiresOnTheShapesItExistsToForbid` fails on `AliasedRaise` **and nothing else** |
| **`M5b`** — break `AsyncEventDeclaration` (e.g. require `Func<Task>` exactly) | both positive fixtures fail, **and** `declaringFiles >= 5` fails — two independent layers catch it |
| **`M5c`** — make `Violations` always return empty | both positive fixtures fail. ⭐ **The scan test still PASSES.** That is the whole point of the positive control, and Builder should record this result explicitly: it is the proof that the floors alone were not enough |
| **`M5d`** — point `RepositoryRoot.Find()` at a directory with no `src` | the `Directory.Exists` assertion fails with the resolved path in the message |
| **`M6`** — Task 3a's extraction | `LogSafetyLintTests` passes identically before and after. ⚠ **Run it from a path containing a `worktrees` segment as well**, which is where `C-100` lived |

⭐ **The decisive one is `M5c`.** A lint that cannot fail is the failure mode `GV-6` shipped, and
`M5c`'s asymmetry — scan green, control red — is the evidence that this one can.

⚠ **And run the lint against the tree BEFORE Task 1** (Task 3b's note). Expected: exactly **17**
violations — 15 in `AudioStateHubService.cs` and 1 each in the two scoped services. **A count that is
not 17 is a finding**: fewer means the rule is missing a shape, more means a false positive or a site
this plan did not enumerate. Record the pre-fix count and the post-fix count (0) in the PR body.

### 4.4 What these tests cannot falsify

State this in the test files rather than letting a green run imply more than it earns.

1. **They do not prove the panel works.** Every test here drives one service with fake subscribers.
   That the eight real components still receive their events on a real circuit is `§4.5`'s job.
2. **The lint proves a shape is absent, not that fan-out is correct.** A raise routed through a helper
   that itself does the wrong thing satisfies the lint completely. What pins the helper is `T1`.
3. **Nothing here covers `tests/`.** The lint scans `src/` only, so `C-213`'s reflection helpers keep
   the defect in the test harness. §6.1.
4. **Nothing here covers `event Action<T>` / `EventHandler<T>` starvation** — `PhoneUnreadState` and
   the `Radio.Infrastructure` synchronous events. Deliberate (`C-205`), and filed.

### 4.5 The live check — a page load, not an interruption

⛔ **Not run at plan time. The owner was asleep and nothing on the box was touched.**

After deploy, and it needs no audio interruption:

```bash
# 1. Both services carry this build.
curl -s http://radio:5000/api/health/version
curl -s http://radio:5002/api/health/version

# 2. The panel paints at all. This is the check that catches the §0.8 failure mode — a circuit that
#    connects and then faults still counts as an established connection, so the deploy's own kiosk
#    check cannot distinguish it.
ssh mmack@radio "systemctl --user status radio-kiosk"

# 3. Events still reach subscribers: change the source from the panel and confirm now-playing and the
#    topbar both update. Two surfaces, because they are two different subscribers to SourceChanged
#    (NowPlayingPanel.razor:556 and MainLayout.razor:384) and the defect being fixed is precisely
#    "only one of them was awaited".

# 4. Nothing new is warning. ⚠ Radio.Web's console sink has NO level restriction (CLAUDE.md), so its
#    Information lines DO reach the journal — a per-broadcast log line here would be a real cost.
ssh mmack@radio "journalctl -u radio-web --since '-10min' --no-pager | grep -i 'notifying.*subscriber'"
#    Expect: nothing. The new LogWarning fires only when a subscriber throws.
```

⚠ **Step 4 is not a formality.** The helpers log at `Warning`, which reaches journald, and this service
broadcasts at up to 20 Hz while an encoder knob is moving (`AudioStateHubService.cs:59-62`). A bug that
made the catch fire routinely would put a journald line on every broadcast, on a box where log volume
correlates with audible audio distortion.

### 4.6 Gates

- `dotnet build --configuration Release` — 0 warnings (warnings are errors in Release).
- `dotnet test --configuration Release` — full suite green.
  ⛔ **Never pipe it to `tail`** (`CLAUDE.md`): redirect, echo `$?`, then grep the file. Read the
  **per-project** summary lines.
  Known-failing on Windows and not regressions: four `SrcVariableResamplerTests` (`libsamplerate.so.0`,
  `TEST-5`) and `NwsObservationIntegrationTests.RealNwsCall_*` (live network, `Category=Integration`,
  CI-excluded).
- ⚠ **`Radio.Web.Tests` is the project most likely to move.** Task 1 changes behaviour that ~20 test
  files construct this service for (`C-207`'s list, plus the fixtures at §0.3). Expect churn there;
  **a test that now fails because exceptions are caught rather than propagating is the change landing**,
  and each such case must be understood and noted, not blanket-updated.
- Every mutation `M1`–`M6` run, with its result in the PR body — **including `M5c`'s asymmetry**.

---

## 5. Docs and queue

| # | Task |
|---|---|
| 1 | `design/FUTURE-WORK.md` — add §6.1's five filed items. |
| 2 | `design/INTEGRATIONS.md` — **no change.** This row touches no integration service. Stated so its absence is not read as an oversight. |
| 3 | `docs/BUILDER_QUEUE.md` — Builder marks `UI-7` ✅ at merge and adds a cycle banner entry. §8.1 has the row replacement. |
| 4 | `docs/queue/UI-7.md` — append §8.2. ⛔ **Change nothing above it**, including the premise `C-203` refutes: the row's measurement is its value and the correction belongs beside it, not over it. |
| 5 | ⛔ **`CLAUDE.md` — nothing.** §6.3. |

---

## 6. Deliberately not done

### 6.1 Filed, not fixed — five

1. **`SystemConfigPage.razor:2219/2229/2239` leak three handlers per navigation** onto a
   process-lifetime singleton, with no `-=` and anonymous lambdas that could not be unsubscribed
   anyway (`C-212`). ⭐ **P2, and arguably higher than this row** — it is unbounded growth on the same
   object, and it is the mechanism by which a one-subscriber event becomes an N-subscriber one. Not
   fixed here because it is a **lifecycle** defect, not a fan-out one: the fix is three named handlers
   and an `IDisposable` on a 2000-line page, and that review has nothing to do with this one.
2. **`PhoneUnreadState.cs:23`'s starvation exposure** (`C-205`). `event Action<int>`, singleton, one
   `MainLayout` handler per circuit, 15 publishers in `PhonePage.razor` — so it is the one site of the
   three that is **genuinely multicast**. The dropped-`Task` half cannot apply; the starvation half can.
   It wants a row about synchronous multicast events, which would also cover `Radio.Infrastructure`'s
   `EventHandler<T>` events and `DuckingService`'s own documented limitation at `:550-552`.
3. **The test harness reproduces the defect** (`C-213`). `SleepTests.cs:503,515`,
   `NowPlayingDockTests.cs:222`, `NowPlayingPanelTests.cs:455,844,973,1042`,
   `RadioControlPanelBandSyncTests.cs:171` fire events via `await del.Invoke(dto)` on a reflected
   backing field. Harmless at one subscriber; a trap for anyone writing a multi-subscriber test (§4's
   third warning). A `tests/`-scoped sweep, or extending the lint's scope, is a follow-up.
4. **`ConfigChanged` is dead** (`C-208`) — zero subscribers, `src/` and `tests/`. **Not deleted here.**
   It is public API, `VisualizerPanelTests.cs:228-237` pins the event set by name, and a dead-code
   deletion inside a defect-class PR muddies a diff whose value is that it is mechanical. Task 4 makes
   the comment true; deleting it is a separate decision.
5. **`Home.razor:4` injects `AudioStateHubService HubService` and never uses it**, and
   `HomePageTests.cs:61` constructs a hub solely to satisfy it. One line each. Same argument as 4.

### 6.2 Genuine structural enforcement — rejected for this row, sketched for the next

If the owner wants the shape to be **unrepresentable** rather than **detectable**, the route is a
generic `AsyncEvent<T>` exposing only `Subscribe` / `Unsubscribe` / `RaiseAsync`, replacing the
`event` keyword on all fourteen. It needs no lint, because there is no `Invoke` to reach.

⛔ **Not taken here**, for four costs and one prerequisite:

- Eight test files reflect the compiler-generated backing field (`C-207`); an `AsyncEvent<T>` field
  changes its type and they all move.
- `VisualizerPanelTests.cs:228-237` pins the public event set via `GetEvents()`, which returns nothing
  for a property.
- All eight subscribing components change from `+=` to `Subscribe(...)`.
- It is a public-API change to `Radio.Web`'s central event bus, reviewed as a refactor rather than a fix.
- ⚠ **And the prerequisite the row never states:** making `AudioStateStore` the hub's sole subscriber
  first requires the store to re-expose `PhoneCallStateChanged`, `EncoderHudChanged` and `ConfigChanged`,
  which it does not (`C-208`). Until then, `EncoderHudService` and `SystemConfigPage` have nowhere else
  to subscribe.

**That is a design conversation and a 2.5 d row, not a rider on this one.**

### 6.3 The `CLAUDE.md` note

`CLAUDE.md` § *Pre-Merge Review* is where this repository's recurring comment-vs-code failures are
recorded, and it already lists three. **`DeviceDisplayStateService.cs:16`'s *"Notifies all
subscribers"* is a fourth of exactly that kind**, and `C-203` — a queue row asserting a
single-subscriber invariant about a class with ten subscribers — is arguably a fifth, in a document
rather than a comment.

⛔ **Not added here.** `CLAUDE.md` is shared context and editing it inside a feature PR is how two
sessions end up disagreeing about it. The facts are written where the code is instead (Task 1c, Task 2,
Task 4) and **recommended to the owner for `CLAUDE.md` separately.**

### 6.4 `UI-6`

Untouched. It shipped ([#596](https://github.com/mmackelprang/RTest/pull/596)) and its three sites are
correct; this row copies its implementation rather than revisiting it. The one thing `UI-7` changes
about `UI-6` is a comment `UI-6` left behind: `AudioStateStore.cs:66-78` and `Program.cs:458-463` both
retire the correctness half of `PHN-2` §0.6's argument, correctly, and neither is affected here.

---

## 7. Self-review

### 7.1 What was verified first-hand at `084a6bbd`

- `AudioStateHubService.cs` **in full** — the 14 event declarations (`:39-74`), all **15**
  `await …Invoke(` sites (grepped and counted, `C-204`), the `:284` alias, the payload guards at
  `:227/:237/:247`, and the ctor comment's `~9` claim at `:24-25`.
- `AudioStateStore.cs` **in full** — the 11 ctor subscriptions (`:90-100`), the 11 re-exposed events
  (`:34-80`) and therefore the **three that are missing**, and both `NotifyAsync` overloads
  (`:444-494`) with their `<remarks>`, which are what Task 1 transcribes.
- `LogSafetyLintTests.cs` **in full** — the `OnlyInFile` mechanism (`:135`, `:215`) and its five users;
  `FindRepositoryRoot` (`:290-324`) including the `PHN-5` fallback at `:313`; the scan floors
  (`:233-236`).
- `Program.cs:405-500` — every registration cited: hub `:411` singleton, `EncoderHudService` `:425`
  singleton **with the comment at `:416-417` stating it subscribes in its constructor**,
  `PhoneUnreadState` `:431` singleton, `AudioStateStore` `:456` singleton, `DeviceDisplayStateService`
  `:475` **scoped**, `RadioPanelToggleService` `:476` **scoped**.
- `MainLayout.razor:378-392` and `NowPlayingPanel.razor:550-562` — **read by hand**, not via an agent,
  because `C-203` overturns the row and a second-hand enumeration is not enough to do that on.
- `SystemConfigPage.razor:2215-2245` — the three anonymous-lambda subscriptions (`C-212`).
- `SleepTests.cs:500-520` — the reflection fire-helper and its own `await del.Invoke(dto)` (`C-213`).
- `VisualizerPanelTests.cs:225-240` — the `GetEvents()` pin and its "prove the instrument" guard.
- `DuckingService.cs:475-490` and `:545-560` — both ranges read; `C-209` confirmed and narrowed.
- `RadioPanelToggleService.cs`, `DeviceDisplayStateService.cs`, `PhoneUnreadState.cs` — all three in
  full; all three declared types checked (`C-205`).
- `ConsolePlaybackState.cs:33-46`, `:90-111` and `AudioVisualizationHubService.cs:503-516` — the two
  other correct fan-outs, which is how §1.3 establishes that no allowlist is needed.
- **Two independent enumerations** of the subscriber census, cross-checked against each other, with two
  hits verified by hand.

### 7.2 What could not be verified, and what it costs

1. **No box was touched.** Nothing was built, run, or deployed. **Every code block here is unexecuted**,
   including the lint's regexes — which is why Task 3b makes Builder run the lint against the tree
   *before* Task 1 and confirm the count is exactly 17. **That step is the plan's own falsification and
   it must not be skipped.**
2. **The lint's direct-call arm (`X(`) is the least tested idea in this plan.** It covers a shape that
   does not occur in the tree, so a false positive is more likely than a true one. Task 3b authorises
   deleting it — say so if it goes.
3. **Whether `RadioPanelToggleService` has an existing direct-construction test** that Task 2's new
   logger parameter would break. Task 2 says to check; the plan did not.
4. **The per-circuit subscriber counts are static, not measured.** "Two browsers means nine handlers on
   `NowPlayingChanged`" is arithmetic over the `+=` sites, not an observation. The *direction* is not in
   doubt — the sites exist and the service is a singleton — but nobody has counted the invocation list
   at runtime.
5. **Whether any of the ~20 test fixtures depends on an exception propagating** out of a raise. Task 1
   changes that from propagate to catch-and-log. §4.6 expects churn in `Radio.Web.Tests` and requires
   each case to be understood; the plan could not enumerate which.
6. **`C-212`'s leak is read from source, not observed.** That every `/system` visit adds three
   permanent handlers follows from three `+=` with no `-=` on a singleton; no growing invocation list
   was measured.

### 7.3 What would falsify this plan's central decision

§1.1 rests entirely on `C-203` — that `AudioStateHubService` has many subscribers and cannot be
constrained to one.

**What would overturn it:** evidence that the eight component subscriptions are not what they appear —
that `MainLayout.razor:381` resolves something other than the hub, or that those components never
render in production. **Both were checked.** `MainLayout.razor:8` is `@inject AudioStateHubService
HubService`; `MainLayout` is the root layout of every page. ⭐ **And the strongest single piece of
evidence is not mine:** `Program.cs:416-417`, written by a previous author about a *different* class,
states that `EncoderHudService` "takes AudioStateHubService in its constructor and subscribes to
EncoderHudChanged there". That is a second production subscriber, documented in the DI file, months
before the row claimed there was one.

⚠ **If `C-203` somehow falls, this plan does not survive it — §1.1's whole justification goes with it**,
and the row's original enforcement option comes back onto the table. That is why §7.1 lists the two
hand-verified files: I did not want a claim this load-bearing to rest on a subagent's grep.

**What is unaffected either way:** the lint (Task 3), the two scoped-service fixes (Task 2), and every
comment correction (Task 4). Those are worth shipping under any resolution of §1.1, which is the same
shape `AUD-11` §7.3 describes — *the half that makes the defect visible does not depend on which half
stops it.*

---

## 8. Queue row wording

⛔ **This plan does not edit `docs/BUILDER_QUEUE.md` or `docs/queue/UI-7.md`.** Other agents are editing
queue files concurrently. The wording below is for whoever does.

### 8.1 Replacement for the `UI-7` line in `docs/BUILDER_QUEUE.md` § Queue

Same column shape as the rows around it; only the **Plan** and **Notes** cells change.

```
| UI-7 | ⚠ **PREMISE CORRECTED 2026-09-08 — the row calls this defect dormant and it is LIVE.** `AudioStateHubService` has TEN production subscribers, not one; 12 of its 14 events have a subscriber that is not `AudioStateStore`, and it is a singleton whose component subscribers register PER CIRCUIT. — [detail](queue/UI-7.md) | 📋 | [`UI-7-close-the-multicast-await-class.md`](../design/plans/UI-7-close-the-multicast-await-class.md) · **1.5 d** · ✅ auto-mergeable on green gates **for the plan's mechanism only** · ⛔ **the row's own scope-question-1 mechanism (a single-subscriber guard) would throw in `MainLayout.OnInitializedAsync` and leave the kiosk on an error boundary with every deploy gate green** — plan §0.8. 📌 **Owner decision needed before Task 1**: §1.1 substitutes "one mandatory fan-out seam + the lint" for "enforce the single subscriber", which does not exist to be enforced. | _`C-203`–`C-212`. Site count is **15**, not 14 (`SourceChanged` is raised twice — `:196` and `:432`). `PhoneUnreadState.cs:23` is `event Action<int>` and is **NOT this bug** — excluded, and `ConsolePlaybackState.cs:44-46` already said so. `DeviceDisplayStateService` and `RadioPanelToggleService` are `AddScoped`, not singletons._ | — _(no row dependency. Copies `UI-6`'s shipped implementation. ⚠ Touches `LogSafetyLintTests` for a mechanical extraction — plan Task 3a, `C-211`.)_ | `fix/ui-7-close-the-multicast-await-class` |
```

### 8.2 Addition to `docs/queue/UI-7.md`

Append; **change nothing above it.** The row's measurement is its value, and the correction belongs
beside it rather than over it.

```markdown
## Planned — 2026-09-08

**Plan:** [`design/plans/UI-7-close-the-multicast-await-class.md`](../../design/plans/UI-7-close-the-multicast-await-class.md).
**1.5 d.** ✅ Auto-mergeable on green gates — for the plan's mechanism, not this row's. Plan §0.8.

⚠⚠ **The premise of the section "Why `AudioStateHubService` is the one worth a row" does not survive
reading the source, and the correction inverts it.** `C-203`: `AudioStateStore` is **not** its only
subscriber. **Ten** production types subscribe — the store, `EncoderHudService`
(`Program.cs:416-417` says so in the DI file), and eight rendered components at
`MainLayout.razor:381-386`, `NowPlayingPanel.razor:554-559`, `NowPlayingDock.razor:117-118`,
`QueueHistoryPanel.razor:410-411`, `RadioControlPanel.razor:981`, `RadioPage.razor:252`,
`Sleep.razor:221-222` and `SystemConfigPage.razor:2219/2229/2239`. Twelve of the fourteen events have
a subscriber that is not the store; the store does not subscribe to two of them at all.

**So the bug is not dormant — it is live on the appliance, and this class carries the worst exposure
in the tree**: singleton lifetime (`Program.cs:411`), per-circuit component subscribers, and no
`try`/`catch` on fourteen of the fifteen raise sites. The row's framing of this class as "the safe
one, saved by an unenforced invariant" is exactly backwards.

The four scope questions are answered:

1. **Fix the sites, or enforce a single subscriber?** ⛔ **The second is not implementable.** A guard
   that throws on the second `+=` throws in `MainLayout.OnInitializedAsync` on the first circuit, and
   the panel never paints — while `systemctl is-active`, both `/api/health/version` endpoints and the
   deploy's kiosk connection count all stay green (plan §0.8). A plain single delegate is worse: it
   *silently replaces* the first subscriber instead of throwing. **The plan substitutes a third
   option neither the row nor the decision named: route all fifteen sites through one mandatory
   `GetInvocationList()` seam and make the seam mandatory with the lint.** That keeps the owner's own
   reasoning — the shape becomes unreachable, so the sites stop mattering — without depending on a
   subscriber count that does not exist. Plan §1.1. **Owner confirmation wanted before Task 1.**
2. **The three singletons — fold in or separate?** **Fold in two; exclude the third**, and two of the
   three are not singletons. `RadioPanelToggleService.cs:64` and `DeviceDisplayStateService.cs:22` are
   `AddScoped` (`Program.cs:476`, `:475`) with one subscriber each — genuinely latent, and folded in
   because **the lint fails on them otherwise**, which makes them a dependency rather than a choice.
   ⛔ `PhoneUnreadState.cs:23` is **excluded**: it is `event Action<int>?`, void-returning, so there is
   no discarded `Task` — and `ConsolePlaybackState.cs:44-46` already documents that exclusion. `C-205`.
   ⭐ **The severities are inverted from the row's table:** the two that are this bug are latent; the
   one that is genuinely multicast is not this bug.
3. **Is a lint the right closer?** **Yes, and under the plan's mechanism it is not a backstop — it IS
   the enforcement**, because the fourteen `event` declarations stay as they are and nothing else
   stops the next author bypassing the seam. Shape: global, **keyed on each file's own `event Func<>`
   declarations rather than on a filename** (⚠ `LogSafetyLintTests`'s five `OnlyInFile` rules silently
   disable themselves on rename — `C-152`), with a two-level pass that also catches the aliased raise
   at `AudioStateHubService.cs:284-287` (a rule without it would cover 14 of 15 sites while claiming
   15 — the same mistake that file records about its own `P7`). ⭐ **Aliveness: a positive control
   that drives the rule against the verbatim pre-fix text and requires it to FIRE**, because a
   "violations == 0" assertion is satisfied identically by a regex that matches nothing. Floors on the
   scan are not enough and `GV-6` is why. Plan §1.3.
4. **Enumerate by every mechanism.** Done — six, cross-checked by two independent passes with two hits
   verified by hand. ⚠ **The name-collision trap fires the OPPOSITE way from this row's warning:**
   `Sleep.razor:13` injects the **hub** as `AudioState` (and injects no store at all), while
   `MainLayout.razor:20` injects the **store** under that name. A grep either way round is wrong. Plan
   §0.3.

⚠ **`C-213` — the test harness fires hub events with `await del.Invoke(dto)` on a reflected backing
field** (`SleepTests.cs:503-511` and five siblings). Correct at one subscriber; **at two it awaits only
the second**, so a `UI-7` test written through that helper would pass against an unfixed
implementation. The lint cannot catch it — it scans `src/`, and this is `tests/`. Plan §4.

**Corrections `C-203`–`C-213`**, of which four change the work: `C-205` (`PhoneUnreadState` excluded),
`C-206` (two of three are `AddScoped`), `C-207` (explicit `add`/`remove` accessors break eight test
files that reflect the backing field), `C-208` (`ConfigChanged` has zero subscribers, and the store
re-exposes only 11 of 14 events — which is *why* two consumers reach past it).

📌 **The inherited `DuckingService` correction is confirmed and needs one further narrowing.**
`:481-483` is fade-parameter arithmetic; `:550-552` is the real anchor. But `DuckingStateChanged` is
`EventHandler<T>` — synchronous and void-returning — so it precedents the **starvation** half only,
never the dropped-`Task` half. `AudioStateStore.cs:435-436` already cites it correctly; copy that
wording. `C-209`.

⚠ **Filed, not fixed — and one of them may outrank this row.** `SystemConfigPage.razor:2219/2229/2239`
subscribe with **anonymous lambdas and no `-=`**, so every navigation to `/system` permanently
adds three handlers to a process-lifetime singleton. Unbounded growth on the same object this row is
about, and the mechanism by which a one-subscriber event becomes an N-subscriber one. `C-212`, plan §6.1.
```
