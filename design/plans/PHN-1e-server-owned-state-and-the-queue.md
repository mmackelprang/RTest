# PLAN — `PHN-1e` · ADR-029 PR 5: server-owned state, the stop conditions, and where the queue goes

**Status:** `[PLAN — 2026-09-04]` · **Row:** `PHN-1e` · **Arc:** ADR-029, PR 5 of seven
**Written against:** `main` at **`a8656c71`** (`PHN-1d`, #558), clean.
**Source of truth:** [`design/decisions/2026-08-03-gv-audio-through-engine.md`](../decisions/2026-08-03-gv-audio-through-engine.md)
(ADR-029) **D6**, **D7**, §6.2, §8.1, §8.2, §14 Q8 · owner decision **`D28`** (punch list §7).
**Predecessors:** [`PHN-1c`](PHN-1c-event-playback-service-and-route.md) §5 · [`PHN-1d`](PHN-1d-ducking-priority-load-bearing.md) §0.4 **C-46**, §5.
**Contradiction numbering continues from `PHN-1d`'s C-46: this plan opens at C-47.**

> ⚠ **The filename says "and the queue" and this plan RECOMMENDS THAT THE QUEUE DOES NOT SHIP HERE.**
> The file was named before the row was sized. §0.2 is the argument, §5 is the queue's complete
> design so that nothing is lost by moving it, and §6 proposes **two** rows rather than one. If the
> owner overrules the split, §5 is written to be lifted into this plan's task list unchanged — it is
> a design, not a deferral note.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

PR 3 built a seam that owns one attended playback. PR 4 let something else stop it. **PR 5 is the PR
where the rest of the house finds out.** `EventPlaybackService.PlaybackChanged` has been raised on
every transition since PR 3 and subscribed by nothing; this row connects it to `/hubs/audio`, caches
it in `Radio.Web`'s `AudioStateStore`, and seeds that cache from `GET /api/audio/events/current` so a
browser that arrives mid-playback is not told the room is silent. Alongside it land the two stop
conditions that do not depend on a client — **the hard max-duration cap** (D7 §7.1, the one that
actually works) and **the last-circuit-closed backstop** (D7 §7.3) — plus the one surface where the
console can put itself somewhere with no stop control, **`/sleep`** (D7 §7.5, ADR §14 **Q8**).

**Nothing a user can see changes.** `GvMedia:Enabled` ships `false` and is not flipped until PR 6;
the topbar chip that renders any of this is PR 6's. What this row buys is that PR 6 has something
true to render, and that audio cannot outlive every client that could stop it.

### 0.2 ⚠ THE SIZING QUESTION, ANSWERED FIRST: this should be two rows, and here is the argument

The brief asked for a re-derived estimate and invited a split. **Re-derived: 5-6 days as one row,
against a prior estimate of 4-5 — and the recommendation is to split it into `PHN-1e` (this plan,
~3 d) and `PHN-1f` (the queue, ~2.5-3 d, fully designed in §5).** The split does not save time. It
buys something better.

**The arc's own founding argument applies to itself.** The PR breakdown opens by explaining why
`PHN-1` is not one PR:

> *"the pieces carry sharply different risk … Landing those together would bury the change most
> likely to make the room sound wrong inside a diff of interface plumbing."*

That is exactly the shape of the combined row:

| | This plan (`PHN-1e`) | The queue (`PHN-1f`, §5) |
|---|---|---|
| **Shape** | Wide and shallow | Narrow and deep |
| **Assemblies** | `Radio.Infrastructure`, `Radio.API`, `Radio.Web` (+3 test projects) | `Radio.Core`, `Radio.Infrastructure` (+1 test project) |
| **Novelty** | Every piece has an in-tree exemplar to copy | A `Radio.Core` contract change to a **shared audio service** with a live subscriber |
| **Audio risk if wrong** | A stale chip, a missed backstop | **The radio jumps to full volume mid-announcement**, or a *stop* is read as a preemption |
| **Review posture** | Ordinary | `PHN-1d`'s — two adversarial reviewers |

Landing them together buries the second column in the first. `PHN-1d` needed **two** adversarial
reviewers to catch a real defect and five wrong *reasons*, in a diff confined to one assembly. The
combined row would be four assemblies wide with the same class of hazard inside it, and the automated
GitHub reviewer is out of quota (queue banner, 2026-09-04) — so the compensating posture is the only
posture available.

**And the split ordering is not neutral — it is better than the combined row for `D28`'s own reason.**
`D28` says *"the queue goes where its visibility goes."* Under the split, **the broadcast lands first
and the queue lands into a system that already broadcasts**, so `Waiting` is on the wire the moment it
exists and its delivery is exercised on a channel that has already run in production. In the combined
row the queue's wire shape would be designed alongside a broadcast that has never carried a message.

**What the split must not do, and does not:** it must not put the queue *after* PR 6. `PHN-1f` is the
row immediately after this one and before PR 6; the arc becomes eight PRs, not seven, and §6 proposes
**both** rows at once so the sequencing is visible rather than promised.

**Why the prior 4-5 d estimate was low**, so the new number is auditable rather than asserted: it
priced the queue as "a wait and a wake". §5 question 4's honest answer requires a change to
`DuckingStateChangedEventArgs` — a `Radio.Core` contract change, plus a behaviour change in
`DuckingService`, plus an update to `AudioManager`'s handler, plus a **third** rewrite of the same two
ducking tripwire test files in three consecutive PRs. That is most of a day on its own, and §5 Q4
argues it is not optional.

**If the owner rejects the split**, the merge is mechanical: §5's five answers are written as task
specifications and append to §1 as Tasks 13-20. The estimate is then 5-6 d and the review posture is
`PHN-1d`'s, doubled.

### 0.3 ⚠ Re-check list — what was verified against `a8656c71`, and where a Builder must re-grep

Everything below was read out of merged `main`, not out of plan text. **Re-grep rather than
re-derive**; if any row is false, stop and re-plan rather than improvising.

| # | Assertion | Where | Status |
|---|---|---|---|
| S1 | `EventPlaybackService` is `sealed`, singleton via `AddEventPlayback`, aliased so `EventPlaybackService` and `IEventPlaybackService` resolve to ONE instance | `EventPlaybackServiceExtensions.cs` | ✅ |
| S2 | `PlaybackChanged` is raised by `Raise(...)`, which is try/caught, and has **zero** subscribers in `src/` | `EventPlaybackService.cs:1144`, `:1118` | ✅ |
| S3 | Every **terminal** publish happens **while holding `_gate`** — `StartAsync`'s replacement arm, `StopAsync`, `OnSourceCompleted`'s dispatched task, `FailAsync` | `:202`, `:259`, `:735`, `:943` | ✅ — and the shipped remark at `:1122` warns PR 5 about it by name |
| S4 | `Playback` owns a `CancellationTokenSource` that **nothing observes once acquisition returns** | `:1173`, `:1190` | ✅ — see **C-49** |
| S5 | `AudioStateUpdateService` is a `BackgroundService` with a 500 ms poll loop **and** an event-subscription half wired in its constructor and unwired in `Dispose` | `:38`, `:149-153`, `:961-964` | ✅ — the subscription half is the pattern to copy; the poll is not |
| S6 | `AddControllers().AddJsonOptions(… JsonStringEnumConverter …)` — **MVC only** | `Radio.API/Program.cs:58-62` | ✅ — see **C-47** |
| S7 | `Radio.Web` has **no** `CircuitHandler` anywhere | `grep -rn CircuitHandler src/` → 0 matches | ✅ |
| S8 | `AudioStateStore` **is** constructed: `MainLayout.razor:20` injects it | `MainLayout.razor:20` | ✅ — see **C-48**, which corrects `PHN-1c` §5 |
| S9 | A Web singleton **cannot** inject a typed `HttpClient`; the house fix is `IServiceScopeFactory` + a scope per use | `BellHealthService.cs:33` and its class remark (ADR-022 §6.2) | ✅ |
| S10 | `SleepService.EnterSleepAsync` pauses the primary source, saves and sets `IsMuted`, broadcasts `SleepStateChanged`; `WakeAsync` **restores the previous mute state** | `SleepService.cs:132`, `:164-165`, `:219` | ✅ — see **C-51** |
| S11 | `SleepService`'s constructor already ends in an optional `IAudioManager? audioManager = null`, and `SleepServiceTests` constructs it directly | `SleepService.cs:118-127` | ✅ — a trailing optional parameter keeps those tests compiling |
| S12 | `TimeProvider? timeProvider = null` → `?? TimeProvider.System` is the house idiom, and `Microsoft.Extensions.TimeProvider.Testing` is referenced by all three relevant test projects | `EncoderHudService.cs:46-49`; three `.csproj` | ✅ |
| S13 | `GvMedia:MaxPlaybackSeconds` = 300 already ships in `GvMediaOptions.cs` and `src/Radio.API/appsettings.json` | `GvMediaOptions.cs:67`, `appsettings.json:274` | ✅ — **this row adds no config key** |
| S14 | `EventPlaybackServiceTests` has `CreateService(… ducking: …)` by name, `NextSnapshotWith`, `WaitUntilAsync`, `FakeEventSource`, `FakeTtsFactory`, and a `FakeDuckingService` modelling priority deletion plus four explicit raise shapes | `EventPlaybackServiceTests.cs:92`, `:150`, `:173`, `:1994` | ✅ — Task 8 extends it in place; never write a second fake |
| S15 | `EventPlaybackController.Transport` already answers **409** for a playback that exists but cannot do the thing, and **404** only for an id `Current` has never described | `EventPlaybackController.cs:230-246` | ✅ — no controller change is needed for a source-less playback |

**Not verified. Check these before writing the code.**

| # | Assertion | Why it is open | If it is false |
|---|---|---|---|
| U1 | System.Text.Json round-trips `TimeSpan` / `TimeSpan?` on **both** the MVC path and the SignalR path in .NET 10 | `Radio.Web/Models/ApiModels.cs:40-41` already declares `TimeSpan?` on a Web DTO, which is strong evidence but not proof that *this* payload survives both hops | Fall back to `double` seconds on the DTO **and** change `GET /current`'s shape to match. The two must not diverge — that is the whole of C-47 |
| U2 | A `CircuitHandler` registered **singleton** receives `OnCircuitOpenedAsync` / `OnCircuitClosedAsync` for every circuit | Documented ASP.NET Core behaviour — handlers are resolved from the circuit scope, so a singleton registration yields the same instance to every circuit — but this repo has never had one | Register scoped and move the count into a separate singleton tracker. Task 7 gives that shape explicitly |
| U3 | `OnCircuitClosedAsync` fires only after Blazor's disconnect-retention window, so a browser **refresh** goes 1 → 2 → 1 and never touches zero | This ordering is what makes ADR §7.3 safe, and §7.3 was written from it rather than from a measurement | The backstop stops audio on a refresh — the exact failure §7.3 exists to avoid. **Stop and re-plan.** Do not paper over it with a delay |

### 0.4 ⚠ Nine contradictions found while planning, and how each resolves

**C-47, C-49 and C-51 change what this row builds. C-48 and C-50 correct claims two predecessor plans
handed forward. C-53 corrects a comment `PHN-1d` shipped.**

---

**C-47 — ⚠ CHANGES THE WORK. SignalR does not use MVC's JSON options, so the broadcast and the REST
body would disagree about how an enum is spelled — and both feed the same client field.**

`Radio.API/Program.cs:58-62` registers `JsonStringEnumConverter` on **`AddControllers().AddJsonOptions`**.
That configures MVC's output formatter and nothing else. SignalR serialises through
`JsonHubProtocol.PayloadSerializerOptions`, which is a **separate** options object this project never
touches.

The consequence, if `EventPlaybackSnapshot` were handed to both paths unchanged:

| Path | `State` on the wire |
|---|---|
| `GET /api/audio/events/current` (MVC, shipped in PR 3) | `"state": "Playing"` — a string |
| `"EventPlaybackChanged"` (SignalR, new here) | `"state": 1` — a number |

ADR §8.1 requires the store to be fed by **both** — seeded from the REST path, kept current by the
broadcast — into the same field. One of the two would fail to deserialise, and which one depends on
options the Web client does not set today.

**Resolution: the broadcast sends an explicitly shaped payload whose enums are `ToString()`, and the
Web DTO models them as `string`.** This is not a workaround, it is this repo's stated convention, and
`AudioStateUpdateService` already writes the reason down at `:1000-1002` for `EncoderConfigStatusDto`:

> *"Sent as strings for the same reason `EncoderHudDto.Phase` is: an unknown tier from a newer API
> build must degrade to 'show nothing special' on a kiosk nobody is watching, not throw during
> deserialization."*

Three things follow, and the third is why this matters beyond this row:

1. The two paths now agree byte-for-byte on `state` and `kind`.
2. Every other field is copied verbatim from the record, so both paths hand the same CLR types to the
   same serialiser and cannot diverge on `TimeSpan` or `DateTimeOffset` however it renders them (U1).
3. ⭐ **It makes `PHN-1f`'s `EventPlaybackState.Waiting` safe by construction.** A Web client built
   before that member exists receives `"Waiting"`, fails to match any known value, and renders
   nothing — rather than deserialising an integer into an enum that does not have that member. The
   `MediaIdHasIllegalCharacter` precedent is about not *renumbering*; this is the other half of the
   same problem, and it is closed here rather than in the row that adds the member.

⚠ **Do NOT "fix" this by adding `JsonStringEnumConverter` to the hub protocol's options.** That would
change the serialisation of every payload on `/hubs/audio` — twelve existing registrations in
`AudioStateHubService.cs:114-236` — to fix one that has not shipped yet.

---

**C-48 — `AudioStateStore` "had never been constructed in its life" is FALSE, and has been since
`ENC-12`. `PHN-1c` §5 carried it forward from a stale queue note.**

`PHN-1c` §5 hands this row: *"note what the queue recorded when `ENC-12` shipped: **`AudioStateStore`
had never been constructed in its life** — zero consumers in `src/Radio.Web` — so its hub cache has
never once run. Anything that plans to 'read the cached state' needs a consumer first."*

Measured at `a8656c71`: `MainLayout.razor:20` reads `@inject AudioStateStore AudioState`, and
`git log -S'@inject AudioStateStore'` attributes that line to **`8df35ddc` — `ENC-12` itself** (#535).
So the sentence was already false when it was written; `ENC-12` is the commit that made it false, not
the commit that observed it.

**Resolution — this makes the row EASIER, and the correction is worth making because the false version
would have caused real work.** A Builder acting on `PHN-1c` §5 would have gone looking for a consumer
to build before the store could be relied on. There is one: the store is a live singleton whose hub
cache runs on every broadcast today, and `MainLayout` already reads two cached fields from it
(`EncoderConnection`, `EncoderConfigStatus`) with class remarks explaining why the lifetime is right.
Task 6 adds a third field in exactly that shape. **Nothing needs to be brought to life.**

⚠ The half of `PHN-1c` §5 that is still true and still load-bearing: **the cache is a fallback, not
the primary.** `MainLayout.razor:388-397` states the rule for `ENC-12` — an authoritative pull, with
the cache used only when the API cannot be reached — because a deploy restarts both services together
and the API's boot broadcast can fire while `AudioStateHubService.StartAsync` is still retrying. Task 6
follows that rule, including its ordering guard.

---

**C-49 — ⚠ CHANGES THE WORK. `CancelAfter` on `Playback`'s existing CTS is NOT the max-duration cap.
Two predecessor handoffs say it is, and cancelling that token stops no audio at all.**

`PHN-1c` §5: *"`EventPlaybackService.Playback` already owns a `CancellationTokenSource`.
`CancelAfter(GvMedia:MaxPlaybackSeconds)` on it, at the point the source starts playing, **is the whole
feature**."* `PHN-1d` §5 repeats it and adds the (correct) refinement that the arming point is now
inside `_gate`.

Traced against the shipped code, the token has exactly one observer and it is finished by then:

- `AcquireAndPlayAsync(playback, request, playback.Token)` passes it to the fetch or the synthesis and
  to `source.PlayAsync(token)`. `PlayAsync` **starts** playback and returns; it does not await
  completion (`EventPlaybackService.cs:531`, and `PHN-1c` §0.6 is explicit that not awaiting is the
  one thing not to copy from `AnnouncementService`).
- After that line nothing in this file reads the token again. `AudioFileEventSource` drives its own
  completion from a wall-clock timer over `DurationSeconds`; `TTSEventSource` likewise.

So `playback.Cancel()` **has never been what stops audio.** What stops audio is `TearDownAsync` →
`ReleaseSourceAsync` → `StopDuckingAsync` / `StopAsync` / `DisposeAsync`. `Cancel()` sits at the top of
`TearDownAsync` to abort an acquisition that has not returned; on a *playing* source it is a no-op.

A cap built as `CancelAfter` would therefore expire silently at 300 s and change nothing — the worst
possible shape for a guarantee, because the guarantee would appear to exist. **This is the one item in
this plan a reviewer should check first**, because two merged documents assert the opposite.

**Resolution (Task 1): the cap is a one-shot timer whose callback DISPATCHES `StopAsync(playback.Id)`.**
It is created through an injected `TimeProvider` (`CLAUDE.md` § Test Timing's named idiom, S12),
armed inside `_gate` immediately after `PlayAsync` returns, and disarmed in `ReleaseSourceAsync` so a
ten-second voicemail does not leave a timer alive for five minutes. Idempotence is free:
`StopAsync` resolves by id and `ClaimTerminal` admits one terminal transition, so a callback racing a
natural end is a no-op.

⚠ `CancelAfter` is **not** wrong for `PHN-1f`'s staleness bound, and the difference is the point: there
the token genuinely is being awaited, by the wait itself. Same API, opposite verdict, because the
question is always "who is observing this token".

---

**C-50 — `IEventPlaybackService.Current`'s doc does say "in-flight", and its remark ALREADY corrects
that once. The correction the brief asks for belongs to the row that adds `Waiting`, not to this one.**

`PHN-1d` §5 question 2 flags: *"`Current`'s doc says 'The one **in-flight** attended playback', and a
waiting playback is not in flight. Correct it in the same PR."* Correct as far as it goes — and the
shipped file at `:93-101` already carries a remark opening *"⚠ 'In flight' is not the whole of it"*,
which explains that the last snapshot is **retained** after a playback ends. So the doc is not naive;
it is a summary line with one documented exception.

**Resolution: this row adds no new state, so it makes no new exception, so it does not touch that
doc.** Correcting it here would be correcting it for a case that does not exist yet, which is the same
error as adding `EventPlaybackState.Waiting` "ready for PR 5" — the thing `D28` explicitly forbade PR 4
from doing. §5 carries the exact replacement text to `PHN-1f`, where the sentence actually becomes
false.

⚠ **If the split is rejected and the queue lands here, this becomes a task**, and the correction is a
*second* exception on the existing remark rather than a rewrite of it: the summary becomes *"the one
attended playback this seam is tracking, or null"*, and the remark gains *"a playback can also be
**waiting** — accepted, audio acquired, and deliberately not sounding because something more important
is."*

---

**C-51 — ⚠ CHANGES THE WORK. ADR §7.5's `/sleep` rule has THREE client-side entry points and ONE
server-side one, and mute is not a substitute for a stop.**

§7.5 decides that entering `/sleep` stops attended playback, because `/sleep` runs under `EmptyLayout`
and PR 6's chip lives in `MainLayout`'s `.topbar-primary`. Read as a `Radio.Web` change it is three
hooks: `HandleSleepButtonAsync` (`MainLayout.razor:1178`), `OnJsSleepRequested` from `idle-dimmer.js`
(`:540`), and `OnSleepStateChanged` from a server push (`:1163`).

All three funnel through **one** server-side method. The button and the JS callback both call
`SystemApi.SetSleepAsync(true)` → `POST /api/system/sleep` → `SleepService.EnterSleepAsync`
(`SystemController.cs:521`), and the server push *originates* there
(`SleepService.cs:175`). So the rule belongs in `SleepService.EnterSleepAsync` — one place, covering
every route, every client and every future caller, instead of three Web hooks that a fourth entry
point would silently bypass.

⚠ **And it must be a stop, not a reliance on the mute `EnterSleepAsync` already applies.** Sleep sets
`_wasMutedBeforeSleep = _audioManager.IsMuted; _audioManager.IsMuted = true` (`:164-165`) and
`WakeAsync` puts it back (`:219`). So under a mute-only reading, a voicemail keeps playing silently
through the sleep and then becomes **audible again, mid-word**, at the moment someone touches the
panel in a dark room. That is worse than the problem §7.5 was written about.

**Resolution (Task 2): `SleepService` takes an optional `IEventPlaybackService?` and stops any
non-terminal attended playback on the way into sleep**, before it mutes — matching the established
order in that method, where quieting comes before the broadcast. Optional and trailing, so
`SleepServiceTests` keeps compiling (S11). This also closes ADR §14 **Q8** in the direction §7.5 called
the safe one; if the sleep arc later gives that surface its own stop control, the rule can be revisited
with a Designer, and Task 11 records that.

---

**C-52 — the last-circuit backstop must fire on the TRANSITION to zero, never on an observed zero, and
the two-browser case the brief names is the easy half.**

ADR §7.3 is careful about *which* circuit leaving matters, and silent about a state this row can
actually reach: `Radio.Web` restarts (a deploy) while `Radio.API` keeps playing. The count is then zero
at rest with audio in the room, and a backstop written as *"if count == 0, stop"* evaluated anywhere
other than a close would stop it — plausibly correct, actually wrong, because no client ever left. The
opposite reading is also wrong: a handler that only ever decrements can be driven negative by a close
without a matching open (a circuit that opened before this handler was registered — impossible today,
possible after any DI reshuffle).

**Resolution (Task 7): `Interlocked.Decrement` and act only when the returned value is exactly 0**, so
the stop is a property of the *edge*. Clamp at zero on the way down and log a warning if it would go
negative, rather than silently normalising — a negative count means the handler missed an open, and
that is worth knowing.

The brief's two-browser case falls out for free and is worth stating so it is tested rather than
assumed: kiosk + laptop is 2, the laptop closing yields 1, nothing stops. A refresh is 1 → 2 → 1 (U3).
A tab closing for good is 1 → 0 after the retention window, and stops.

---

**C-53 — ⚠ CORRECTS A COMMENT `PHN-1d` SHIPPED. Subscribing `AudioStateUpdateService` to
`PlaybackChanged` makes `EventPlaybackService` eager, and its constructor comment says it is lazy.**

`EventPlaybackService.cs:141-144` explains that the service *"is constructed lazily — on the first
injection into `EventPlaybackController` — so before anything has ever posted to `/api/audio/events`
there is no subscription at all. That is correct rather than a gap."*

True at `a8656c71`, and **false the moment Task 3 lands**: `AudioStateUpdateService` resolves its
collaborators in its constructor (`:85-92`), it is registered `AddHostedService` (`Program.cs:134`),
so resolving `IEventPlaybackService` there constructs the singleton at host start.

The behaviour change is benign in both directions — with no attended playback there is nothing to
preempt, so an earlier `DuckingStateChanged` subscription costs nothing, and an earlier construction is
what a hosted service resolving a singleton normally does. **The comment is what becomes wrong**, and
`CLAUDE.md` § Pre-Merge Review is explicit that a comment surviving the code it described is worse than
no comment.

**Resolution: Task 3 rewrites that comment in the same diff that falsifies it.** Two side effects a
reviewer should see stated rather than discover: `GvMediaClient`, `AudioFileEventSourceFactory`,
`ITTSFactory` and `IDuckingService` are all now constructed at host start rather than on first POST,
and a resolution failure in any of them becomes a **startup** failure rather than a 500 on the first
voicemail. That is the better direction — a service that will not start in a cabinet is visible; a
service that fails on first use is not — and Task 9's DI probe is what makes it a test rather than a
hope.

---

**C-54 — every terminal `PlaybackChanged` raise happens while `_gate` is held, and the shipped code
says so to this row by name. The broadcast is safe; the constraint is real.**

`EventPlaybackService.cs:1122-1129`:

> *"⚠ FOR PR 5, BEFORE YOU SUBSCRIBE. Every terminal call site of this method … invokes it WHILE
> HOLDING `_gate` … A hub broadcast that only serialises and sends is fine. A subscriber that
> re-enters this seam — `StopAsync`, `StartAsync`, or anything that awaits something which does —
> DEADLOCKS."*

**Resolution: nothing to build, and one rule to hold.** Task 3's handler builds a payload and calls
`_hubContext.Clients.All.SendAsync`, exactly as its five siblings do, and touches
`IEventPlaybackService` for nothing at all — not even to re-read `Current`, which the snapshot argument
already carries. Task 12's scope gate greps for it.

⚠ The near-miss worth naming: the obvious "improvement" of having the handler read
`_playback.Current` to enrich the payload takes `_stateLock`, not `_gate`, so it would not deadlock —
it would **occasionally disagree with the snapshot it was given**, because `Current` is not retained
for a replaced playback (`:1113-1116`). Use the argument.

---

**C-55 — the raise now arrives on arbitrary thread-pool threads, and one existing sibling handler shows
the shape that survives it.**

`PHN-1d` §5 hands this row: *"`PlaybackChanged` now fires for preemptions too, and they arrive on a
`Task.Run` thread rather than on the request thread."* Confirmed — `OnDuckingStateChanged` dispatches
through `Task.Run` (`:897`), and `OnSourceCompleted` has always done the same (`:720`).

`IHubContext<T>` is documented as safe from any thread and the five existing `async void` handlers in
`AudioStateUpdateService` already rely on it. **Resolution: nothing to build.** Recorded so a reviewer
checking `PHN-1d`'s handoff finds the answer rather than an open item. The one real requirement is the
`async void` + `try`/`catch` shape those siblings use (`:969`, `:992`, `:1024`): an exception escaping
an `async void` handler is a process-level hazard, and this one is invoked from inside a `try` in
`Raise` that would log it as *"a PlaybackChanged subscriber threw"* — accurate, but it would be logged
against the seam rather than against the broadcaster.

### 0.5 What this row is NOT

1. ⛔ **No topbar chip, no `VoicemailPlayer` change, no `.topbar-primary` markup.** PR 6. This row
   makes the chip *possible* by putting the state where a chip can read it.
2. ⛔ **No queue, no `EventPlaybackState.Waiting`, no wait, no wake, no `MaxQueuedWaitSeconds`.** §5
   and `PHN-1f`. Adding the enum member "ready" is the thing `D28` forbade PR 4 from doing, and the
   reason does not change with the PR number.
3. ⛔ **No `DuckingService` change and no `DuckingStateChangedEventArgs` change.** Both tripwire test
   files (`DuckingServiceTests`, `DuckingServiceCharacterizationTests`) must be untouched by this
   row's diff — that is a scope-gate grep in Task 12.
4. ⛔ **No config key.** `GvMedia:MaxPlaybackSeconds` already ships (S13).
5. ⛔ **No new route and no DTO change on an existing route.** `GET /api/audio/events/current` and
   `DELETE /api/audio/events/{id}` are what this row consumes; it changes neither.
6. ⛔ **No `TTSEventSource.Position` fix.** `PHN-1c` C-27 hands PR 5 a three-line override so a speech
   scrubber stops reading zero, and `ASpeechSnapshotReportsPositionZeroForItsWholeLife` is the test to
   update. **It belongs to PR 6**, which is the first row with a scrubber to be wrong: fixing it here
   would change a pinned behaviour for no observer, and PR 6's UAT is where "the bar moves" is actually
   checked. Task 11 records the handoff.
7. ⛔ **No seek/pause/resume behaviour change.** S15: the controller already answers 409 for a playback
   that exists and cannot comply.
8. ⛔ **No `docs/BUILDER_QUEUE.md`, `docs/HANDOFF-GA-PUNCH-LIST.md` or `docs/HANDOFF-NEXT-SESSION.md`
   edit.** Those three files are owned by another pass while this plan is written; §6 holds the
   proposed rows instead of filing them.

### 0.6 What a viewer actually sees — the wire shape, named now

The brief asks for this even though PR 6 builds the chip, and `D28`'s whole argument is that an
invisible state is not a state. Here is what leaves the API, in both directions, after this row.

**The broadcast** — `"EventPlaybackChanged"`, `Clients.All`, on every transition and only on
transitions. No tick, no position stream (ADR §8.2).

```jsonc
{
  "id": "evp-4f2c…",              // the seam's own id space; the handle for DELETE
  "kind": "RemoteMedia",          // string — "Speech" | "RemoteMedia"   (C-47)
  "label": "Voicemail from Jane", // presentation only, capped at 128 chars
  "state": "Playing",             // string — Preparing|Playing|Paused|Completed|Stopped|Failed (C-47)
  "duration": "00:00:29.9000000", // null while Preparing, and null when the provider said 0
  "positionAtBroadcast": "00:00:00",
  "broadcastAtUtc": "2026-09-04T18:22:41.117Z",
  "failureReason": null           // set only when state is Failed
}
```

**The seed** — `GET /api/audio/events/current`, the same shape, 200 or 204. This is ADR §8.1's
re-attach path and after C-47 the two are byte-identical in structure.

**What a client must do with it, stated as requirements PR 6 inherits:**

1. **Interpolate, never poll.** `positionAtBroadcast` + `broadcastAtUtc` + `state` is an anchor. While
   `state == "Playing"` the progress bar advances from the anchor on the client's own clock and
   re-anchors on every transition (ADR §8.2). Drift over a 60-second voicemail is clock skew between
   two processes on the same box.
2. **Render `duration: null` as indeterminate, not as zero.** The provider reporting 0 means
   *unknown*, and the snapshot says so honestly rather than showing a confident wrong bar.
3. **Treat an unrecognised `state` as "something is happening, offer Stop".** That is what makes
   `PHN-1f`'s `Waiting` land without a Web deploy in lockstep, and it is why `state` is a string.
4. **A terminal state is retained, not cleared.** `Completed` / `Stopped` / `Failed` stay in `Current`
   and in the cache until a new playback replaces them — so "nothing is playing" is a *state*, never
   the absence of a snapshot. A chip that hides on any snapshot at all would never show a failure.

**What is deliberately NOT on the wire, with the reason:** an `EndReason` distinguishing a user stop
from a preemption from the duration cap. All three publish `Stopped`. `PHN-1d` §5 offered to add the
field here and made the right condition — *"if the chip needs to distinguish"* — and PR 6's chip, per
ADR §12 item 4, returns the UI to an idle, replayable state in all three cases. Adding a field no
renderer branches on is a wire commitment bought with nothing. It is one nullable string away if PR 6
asks; §5 records the request path. An operator can already tell the three apart from the log — the
preemption warning names the preempting source, and Task 1's cap logs its own line.

---

## 1. Tasks

Twelve tasks. Tasks 1-7 are the change; 8-10 are the tests; 11 is the documentation this row
falsifies; 12 is the build and the scope gate. **Run Task 12's gate before opening the PR, not after.**

---

### Task 1 — the max-duration cap, built as a timer rather than as a cancellation

**File:** `src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs`
**Also:** `src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs` (one doc), `src/Radio.Core/Configuration/GvMediaOptions.cs` (one doc)
**ADR:** D7 §7.1 · **See C-49 first** — two merged plans prescribe a shape that stops no audio.

**1a. Inject a `TimeProvider`.** Field and constructor, following `EncoderHudService.cs:46-49`:

```csharp
  private readonly GvMediaClient _gvMediaClient;

  /// <summary>
  /// Clock for the max-duration cap. Injectable so a test can advance it rather than wait on it —
  /// CLAUDE.md § Test Timing's named idiom, and the reason FakeTimeProvider is already referenced by
  /// Radio.Infrastructure.Tests.
  /// </summary>
  /// <remarks>
  /// ⚠ PHN-1d deliberately did NOT take this dependency, and its C-44 says why: that PR added no
  /// timer, so the thing a test had to synchronise on was a dispatch (PreemptionTail), not a clock.
  /// This PR adds a real timer, so the idiom now applies. Both are true; neither supersedes the other.
  /// </remarks>
  private readonly TimeProvider _timeProvider;
```

```csharp
  public EventPlaybackService(
    ILogger<EventPlaybackService> logger,
    IOptionsMonitor<GvMediaOptions> gvMediaOptions,
    IOptionsMonitor<TTSOptions> ttsOptions,
    ITTSFactory ttsFactory,
    AudioFileEventSourceFactory fileFactory,
    IDuckingService duckingService,
    GvMediaClient gvMediaClient,
    TimeProvider? timeProvider = null)
  {
    // … existing assignments unchanged …
    _timeProvider = timeProvider ?? TimeProvider.System;
```

⚠ **Trailing and optional**, so `EventPlaybackServiceTests.CreateService` and the container both keep
working with no registration. `TimeProvider` is not registered in `Radio.API`'s DI today and this row
does not register it — the default is the production value.

**1b. `Playback` learns to hold one timer.** Add beside `_source` / `_released` / `_terminal`:

```csharp
    private ITimer? _capTimer;
```

and, after `ClaimSourceForRelease`:

```csharp
    /// <summary>
    /// Arms the hard max-duration cap on this playback (ADR-029 D7 §7.1).
    /// </summary>
    /// <remarks>
    /// Idempotent: a second arm disposes the first timer, so a re-arm can never leave two running.
    /// Guarded by _sourceLock rather than by a lock of its own — the callback takes no lock at all,
    /// so reusing it introduces no ordering this class does not already have.
    /// </remarks>
    public void ArmDurationCap(TimeProvider timeProvider, TimeSpan after, Action onExpired)
    {
      lock (_sourceLock)
      {
        _capTimer?.Dispose();
        _capTimer = timeProvider.CreateTimer(_ => onExpired(), null, after, Timeout.InfiniteTimeSpan);
      }
    }

    /// <summary>Disarms the cap. Safe when it was never armed, and safe to call twice.</summary>
    /// <remarks>
    /// ⚠ ITimer.Dispose does NOT wait for a callback already running, and it deliberately is not
    /// made to: the callback only dispatches StopAsync(Id), which is idempotent through
    /// ClaimTerminal, so a cap firing at the same instant as a natural end is a no-op rather than a
    /// double stop. Waiting here would mean blocking a teardown on a timer thread.
    /// </remarks>
    public void DisarmDurationCap()
    {
      lock (_sourceLock)
      {
        _capTimer?.Dispose();
        _capTimer = null;
      }
    }
```

**1c. Arm it inside `_gate`, immediately after `PlayAsync` returns.** In `AcquireAndPlayAsync`:

```csharp
        await source.PlayAsync(token);

        // ADR-029 D7 §7.1 — THE guarantee, and the only stop condition that needs no client at all.
        // ⚠ Armed HERE, inside _gate and after PlayAsync returned, for two reasons. The gate is what
        // makes it impossible to arm a cap on a playback a preemption has already torn down (PHN-1d
        // §5 flags exactly this). And "at most one timer exists" then follows from D5 rule 1 — one
        // attended playback at a time — rather than from bookkeeping this class would have to keep.
        ArmDurationCap(playback);

        PublishNonTerminal(playback, EventPlaybackState.Playing);
```

and the helper, placed with the other private members:

```csharp
  /// <summary>
  /// Arms the hard max-duration cap on a playback that has just started producing audio.
  /// </summary>
  /// <remarks>
  /// ⚠ This is NOT CancelAfter on playback.Token, which is what PHN-1c §5 and PHN-1d §5 both
  /// prescribe — and the difference is the whole feature rather than a detail. Nothing observes that
  /// token once acquisition has returned: AcquireAndPlayAsync's last read of it is
  /// source.PlayAsync(token), which STARTS playback and returns rather than awaiting completion, and
  /// both event sources drive their own completion from a duration they were given. So cancelling it
  /// stops no audio. A cap built that way would expire silently at 300 s and change nothing, which is
  /// the worst available shape for something the ADR calls "the guarantee".
  ///
  /// What actually stops audio is TearDownAsync -> ReleaseSourceAsync, and StopAsync is the public
  /// door to it. Hence a timer whose callback dispatches a stop.
  ///
  /// ⚠ DISPATCHED, never awaited, for OnSourceCompleted's reason: StopAsync takes _gate, and the
  /// callback arrives on a timer thread that must not be parked for the length of a teardown
  /// (ducking release fade included). Idempotence is free — StopAsync resolves by id and
  /// ClaimTerminal admits exactly one terminal transition — so a cap racing a natural end is a
  /// no-op.
  ///
  /// ⚠ Math.Max(1, …): there is NO off switch, and that is deliberate. ADR-029 §7.1 calls this the
  /// guarantee that everything else is a latency improvement on, and GvMediaOptions.PreemptAtPriority
  /// is this arc's worked example (plan PHN-1d C-43) of a knob that silently disables a feature while
  /// leaving it looking intact. A 0 here clamps to one second rather than meaning "never".
  /// </remarks>
  private void ArmDurationCap(Playback playback)
  {
    var seconds = Math.Max(1, _gvMediaOptions.CurrentValue.MaxPlaybackSeconds);
    var playbackId = playback.Id;

    playback.ArmDurationCap(_timeProvider, TimeSpan.FromSeconds(seconds), () =>
    {
      // Warning, not Information: since LOG-11 the journal carries Warning and above, and "the
      // voicemail stopped by itself after five minutes" is exactly what an operator diagnoses from
      // the box. Ids only — never a media id and never request text (PHN-1b §0.3 ④).
      _logger.LogWarning(
        "Attended playback {Id} reached GvMedia:MaxPlaybackSeconds ({Seconds}s); stopping it",
        playbackId, seconds);

      _ = Task.Run(
        async () =>
        {
          try
          {
            await StopAsync(playbackId);
          }
          catch (ObjectDisposedException)
          {
            // The container went away underneath the timer. Nothing left to stop.
          }
          catch (Exception ex)
          {
            // An unobserved faulted task is a process-level hazard on this box.
            _logger.LogWarning(ex, "Error stopping capped attended playback {Id}", playbackId);
          }
        },
        CancellationToken.None);
    });
  }
```

**1d. Disarm it in the one place that stops a source.** First statement of `ReleaseSourceAsync`:

```csharp
  private async Task ReleaseSourceAsync(Playback playback, IEventAudioSource source)
  {
    // The single funnel for stopping and disposing a source — six callers reach it through
    // TearDownAsync or Dispose — so the single place the cap is disarmed. A ten-second voicemail must
    // not leave a five-minute timer alive behind it.
    playback.DisarmDurationCap();

    try { await _duckingService.StopDuckingAsync(source); }
    // … the three existing guarded steps, unchanged …
```

⚠ **Residual, stated rather than hidden:** `Dispose` claims the source and runs `ReleaseSourceAsync`
through a bounded `Wait`. If that times out, the disarm happens on the abandoned task rather than
before shutdown returns. The cost is one orphaned timer during process teardown, whose callback finds
a disposed service and is swallowed by the `ObjectDisposedException` arm above. Not worth a second
disarm site.

**1e. Two documentation lines this task makes false.**

- `IEventPlaybackService.cs:147-152`, `EventPlaybackState.Stopped`: *"⚠ Preemption (PR 4) and the
  duration cap (PR 5) will land here too, and **NEITHER EXISTS YET**."* Preemption shipped in
  `PHN-1d` and the cap ships here. It is technically scoped (*"as of PHN-1c"*) and would still read as
  a live claim to the next person. Replace with:

```csharp
  /// <summary>
  /// Ended before the end of the content. Four things produce it: a user stop, a new playback taking
  /// the single slot, a source starting at or above GvMedia:PreemptAtPriority (PHN-1d), and the
  /// GvMedia:MaxPlaybackSeconds cap (PHN-1e). ⚠ The snapshot does not say WHICH — all four are a
  /// stop, and no renderer branches on the difference today. The log line does say which.
  /// </summary>
  Stopped = 4,
```

- `GvMediaOptions.cs:63-67`, `MaxPlaybackSeconds`: *"Consumed by PR 5 … In this PR it is used only to
  bound the download size and the no-cache sweep window."* Replace the first sentence with a statement
  of what now reads it and what the value guarantees:

```csharp
  /// <summary>
  /// Hard cap on one attended playback (ADR-029 D7 §7.1). Read by
  /// <c>EventPlaybackService.ArmDurationCap</c>, which arms a one-shot timer when the source starts
  /// producing audio and stops the playback when it fires — with no client cooperation, no heartbeat
  /// and no poll. D5 rule 1 bounds the count of armed timers at one. Also bounds the download size
  /// and the no-cache sweep window in GvMediaCache.
  ///
  /// <para>
  /// ⚠ There is no "off". A value below 1 clamps to 1 rather than disabling the cap: this is the one
  /// stop condition that survives every client going away, and the arc already has a worked example
  /// of a knob that disables a feature while leaving it looking intact (see PreemptAtPriority).
  /// </para>
  /// </summary>
  public int MaxPlaybackSeconds { get; set; } = 300;
```

---

### Task 2 — entering sleep stops attended playback (ADR §7.5, closing §14 Q8)

**File:** `src/Radio.API/Services/SleepService.cs` · **See C-51.**

`using Radio.Core.Interfaces.Audio;` is already present (`:5`); no new using is needed.

**2a. One optional, trailing constructor parameter** — trailing so `SleepServiceTests`, which
constructs this type directly, keeps compiling (S11):

```csharp
  private readonly IAudioManager? _audioManager;

  /// <summary>
  /// The attended-playback seam, or null when event playback is not registered. Used for one thing
  /// only: ADR-029 §7.5's rule that entering /sleep stops attended playback.
  /// </summary>
  private readonly IEventPlaybackService? _eventPlayback;

  public SleepService(
    ILogger<SleepService> logger,
    IHubContext<AudioStateHub> hubContext,
    IAudioManager? audioManager = null,
    IEventPlaybackService? eventPlayback = null)
  {
    _logger = logger;
    _hubContext = hubContext;
    _audioManager = audioManager;
    _eventPlayback = eventPlayback;
  }
```

**2b. Stop on the way in**, inside `EnterSleepAsync`, **before** the existing pause/mute block so the
room quiets in one direction:

```csharp
      // ADR-029 D7 §7.5, closing that ADR's open question Q8 in the direction it called safe.
      // /sleep declares @layout EmptyLayout, so PR 6's stop chip — which lives in MainLayout's
      // .topbar-primary — does not render there, and MainLayout navigates the console to /sleep
      // ITSELF on a server-pushed sleep and on the idle timer. Attended playback may not exist on a
      // surface that offers no way to stop it.
      //
      // ⚠ A STOP, not a reliance on the mute two blocks below. WakeAsync restores
      // _wasMutedBeforeSleep, so a muted-but-still-playing voicemail would become audible again
      // MID-WORD the instant somebody touches the panel in a dark room — worse than the problem the
      // rule was written about.
      //
      // ⚠ Here rather than in Radio.Web. Three client paths reach sleep — the Sleep pill, the
      // idle-dimmer JS callback, and a server-pushed SleepStateChanged — and all three arrive at this
      // method. One place covers every route, every client, and the entry point nobody has written
      // yet.
      await StopAttendedPlaybackAsync();
```

**2c. The helper**, placed beside the other private members of `SleepService`:

```csharp
  /// <summary>
  /// Stops attended event playback that could still be producing sound. Never throws: sleep has to
  /// happen whether or not a voicemail could be stopped.
  /// </summary>
  /// <remarks>
  /// ⚠ A non-null Current is NOT the same as audio in the room. IEventPlaybackService.Current
  /// RETAINS the last snapshot after a playback ends, because StartAsync answers before any audio
  /// exists and that surface is the only place an acquisition failure can be read from. So the state
  /// is what decides, not the null check.
  /// </remarks>
  private async Task StopAttendedPlaybackAsync()
  {
    if (_eventPlayback?.Current is not { } snapshot)
    {
      return;
    }

    // Preparing is included deliberately: a fetch or a synthesis still in flight would otherwise
    // start audio moments after the panel went dark.
    if (snapshot.State is not (EventPlaybackState.Preparing
        or EventPlaybackState.Playing
        or EventPlaybackState.Paused))
    {
      return;
    }

    try
    {
      if (await _eventPlayback.StopAsync(snapshot.Id))
      {
        _logger.LogInformation(
          "Sleep stopped attended playback {Id}: /sleep offers no transport (ADR-029 §7.5)",
          snapshot.Id);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error stopping attended playback on the way into sleep");
    }
  }
```

⚠ **`WakeAsync` gains nothing.** There is no resume: ADR §6.2 rule 2's reasoning applies here too —
the recording is replayable at zero cost, and resuming a voicemail mid-word after a wake is worse than
restarting it. Do not add a symmetric restore.

---

### Task 3 — the broadcast, and the comment it falsifies

**Files:** `src/Radio.API/Services/AudioStateUpdateService.cs`,
`src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs` (one comment)
**ADR:** D6 §8.1 delta item 1 · **See C-47, C-53, C-54, C-55.**

**3a. Resolve and subscribe**, in the constructor, after the `_encoderFeedback` block:

```csharp
    // ADR-029 D6 §8.1. GetService rather than GetRequiredService, matching every sibling above: this
    // service has to start even when parts of the audio stack are not registered at all.
    _eventPlayback = serviceProvider.GetService<IEventPlaybackService>();

    if (_eventPlayback != null)
    {
      // ⚠ Change-driven. There is deliberately NO position tick and this must never move into
      // CheckAndBroadcastUpdatesAsync's 500 ms loop — ADR-029 §8.2 refuses one outright, because a
      // tick puts a timer on the server and a message on the wire per client for the whole duration,
      // on a box where CPU churn is audible.
      _eventPlayback.PlaybackChanged += OnEventPlaybackChanged;
      _logger.LogInformation("Subscribed to attended event playback transitions");
    }
    else
    {
      _logger.LogWarning(
        "IEventPlaybackService not available - event playback SignalR updates disabled");
    }
```

with the field beside its siblings:

```csharp
  private readonly IEventPlaybackService? _eventPlayback;
```

**3b. Unsubscribe in `Dispose`**, beside the five existing unsubscriptions:

```csharp
    if (_eventPlayback != null)
    {
      _eventPlayback.PlaybackChanged -= OnEventPlaybackChanged;
    }
```

**3c. The handler.** Place it beside `OnEncoderHudChanged`, whose shape it copies:

```csharp
  /// <summary>
  /// Broadcasts one attended-playback transition (ADR-029 D6 §8.1).
  /// </summary>
  /// <remarks>
  /// ⚠ THE ENUMS ARE SENT AS STRINGS, and it is not cosmetic. Radio.API registers
  /// JsonStringEnumConverter on AddControllers().AddJsonOptions ONLY (Program.cs:58-62); SignalR
  /// serialises through JsonHubProtocol.PayloadSerializerOptions, which this project never
  /// configures. Handing the record straight to SendAsync would put "state": 1 on the hub and
  /// "state": "Playing" on GET /api/audio/events/current — and ADR-029 §8.1 feeds BOTH into the same
  /// client field, the REST call as the seed and this as the update. ToString() makes them identical.
  /// It also means a Radio.Web build that predates a new state member receives an unrecognised STRING
  /// and can ignore it, rather than deserialising a number into an enum that has no such value — the
  /// same reason EncoderConfigStatusChanged below sends its tier as a string.
  ///
  /// ⚠ Every other field is copied verbatim, so both paths hand the same CLR types to the same
  /// serialiser and cannot diverge on however TimeSpan and DateTimeOffset happen to render.
  ///
  /// ⚠ The snapshot ARGUMENT is the payload. Do NOT enrich it from _eventPlayback.Current: this
  /// handler is invoked from inside EventPlaybackService.Raise, and Current is deliberately not
  /// retained for a playback that has been replaced — so a re-read would sometimes describe a
  /// different playback than the transition being broadcast. (It would not deadlock; Current takes
  /// that service's _stateLock, not its _gate. It would just occasionally lie.)
  ///
  /// ⚠ And do not call back into the seam at all. Every TERMINAL publish reaches Raise while
  /// EventPlaybackService holds its non-reentrant _gate, and that file's own remark says so to this
  /// PR by name: a subscriber that re-enters StopAsync or StartAsync deadlocks.
  ///
  /// async void with a catch-all, matching the five sibling handlers here. This is also raised from
  /// arbitrary thread-pool threads since PHN-1d — a preemption arrives on a Task.Run — which
  /// IHubContext is safe for.
  /// </remarks>
  private async void OnEventPlaybackChanged(object? sender, EventPlaybackSnapshot snapshot)
  {
    try
    {
      await _hubContext.Clients.All.SendAsync("EventPlaybackChanged", new
      {
        snapshot.Id,
        Kind = snapshot.Kind.ToString(),
        snapshot.Label,
        State = snapshot.State.ToString(),
        snapshot.Duration,
        snapshot.PositionAtBroadcast,
        snapshot.BroadcastAtUtc,
        snapshot.FailureReason,
      });

      // Debug, matching PlaybackStateChanged. Label is user-supplied content and the id is a live
      // handle; neither belongs in a production line by default, and the state alone is what a
      // "why did the voicemail stop" question needs from this side.
      _logger.LogDebug("Broadcast EventPlaybackChanged: {State}", snapshot.State);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error broadcasting attended event playback state");
    }
  }
```

**3d. Correct the comment this makes false** — `EventPlaybackService.cs:141-144`, C-53:

```csharp
    // ⚠ This singleton is built at HOST START, not lazily, and the comment that used to sit here said
    // the opposite. It was true until PHN-1e: AudioStateUpdateService now resolves
    // IEventPlaybackService in its own constructor and is registered AddHostedService, so this
    // subscription is live from boot rather than from the first POST to /api/audio/events.
    //
    // That is the better direction, and the consequence is worth seeing rather than discovering:
    // GvMediaClient, AudioFileEventSourceFactory, ITTSFactory and IDuckingService are all now
    // constructed at startup, so a resolution failure in any of them is a service that will not start
    // — visible — rather than a 500 on the first voicemail. EventPlaybackRegistrationTests is what
    // keeps that a test rather than a hope.
```

---

### Task 4 — the Web-side DTO and the two-method API client

**Files:** `src/Radio.Web/Models/ApiModels.cs`, new
`src/Radio.Web/Services/ApiClients/EventPlaybackApiService.cs`, `src/Radio.Web/Program.cs`

**4a. The DTO**, appended to `ApiModels.cs`:

```csharp
/// <summary>
/// One attended event playback: the payload of "EventPlaybackChanged" and the body of
/// GET /api/audio/events/current (ADR-029 D6 §8.1, §8.2).
/// </summary>
/// <remarks>
/// ⚠ Kind and State are STRINGS rather than the Radio.Core enums, deliberately and twice over.
/// (1) The two paths that fill this record serialise through different System.Text.Json options —
/// MVC's carry JsonStringEnumConverter, SignalR's do not — so the API spells them explicitly; see
/// AudioStateUpdateService.OnEventPlaybackChanged. (2) A value this build has never heard of must
/// degrade to "show nothing special" rather than throw during deserialization on a kiosk nobody is
/// watching, which is the rule EncoderConfigStatusDto and EncoderHudDto.Phase already follow.
///
/// ⚠ This is an ANCHOR, not a tick. PositionAtBroadcast + BroadcastAtUtc + State is everything a
/// client needs to interpolate a progress bar on its own clock; there is no periodic position
/// message and there must not be one (ADR-029 §8.2).
/// </remarks>
public record EventPlaybackSnapshotDto(
  string Id,
  string? Kind,
  string? Label,
  string? State,
  TimeSpan? Duration,
  TimeSpan PositionAtBroadcast,
  DateTimeOffset BroadcastAtUtc,
  string? FailureReason)
{
  /// <summary>
  /// True while this playback could still be producing sound — the only states worth offering a Stop
  /// for, and the only ones the last-circuit backstop acts on.
  /// </summary>
  /// <remarks>
  /// ⚠ Written as "not one of the terminal three" rather than "one of the live three", so a state
  /// this build does not recognise counts as LIVE. That is the safe direction: an unknown state that
  /// is in fact playing must keep its stop control. PHN-1f's Waiting is exactly such a value, and
  /// this is what lets it arrive without a lockstep Radio.Web deploy. A null State is the one thing
  /// that is not live — it means the payload did not carry one at all.
  /// </remarks>
  public bool IsLive =>
    State is not null && State is not ("Completed" or "Stopped" or "Failed");
}
```

**4b. The client**, `src/Radio.Web/Services/ApiClients/EventPlaybackApiService.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// Attended event playback — the READ and STOP halves of /api/audio/events (ADR-029 D1, D6, D7).
/// </summary>
/// <remarks>
/// ⚠ Two methods, deliberately. PHN-1e needs the re-attach read (ADR §8.1) and the stop the
/// last-circuit backstop dispatches (§7.3). Start, seek, pause and resume belong to PR 6, which is
/// the first row with a transport to drive them from — a client method with no caller is a claim
/// that a surface exists.
/// </remarks>
public class EventPlaybackApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<EventPlaybackApiService> _logger;

  public EventPlaybackApiService(HttpClient httpClient, ILogger<EventPlaybackApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  /// <summary>The one attended playback, or null when there is none to report.</summary>
  /// <remarks>
  /// ⚠ 204 is a real answer, not a failure: it means nothing has EVER been started since the API
  /// booted, and it is distinct from a 200 carrying a Completed snapshot, which means something ran
  /// and finished. Both reach a caller as "nothing live"; only a caller that wants to render a
  /// FINISHED playback needs the difference, and that caller (PR 6's chip) reads the snapshot rather
  /// than this method's null.
  /// </remarks>
  public async Task<EventPlaybackSnapshotDto?> GetCurrentAsync(
    CancellationToken cancellationToken = default)
  {
    try
    {
      using var response =
        await _httpClient.GetAsync("/api/audio/events/current", cancellationToken);

      if (response.StatusCode == HttpStatusCode.NoContent)
      {
        return null;
      }

      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<EventPlaybackSnapshotDto>(
        cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to read the current attended playback");
      return null;
    }
  }

  /// <summary>Stops the playback with this id. False when nothing was stopped, for any reason.</summary>
  /// <remarks>
  /// ⚠ A 404 or a 409 is NOT an error and is not logged as one. Both are ordinary answers to "stop
  /// this": the playback ended between the caller reading the id and this call landing, which on the
  /// last-circuit path — where the id can be minutes old — is the common case rather than the
  /// exceptional one. Only a transport failure is worth a line.
  /// </remarks>
  public async Task<bool> StopAsync(string playbackId, CancellationToken cancellationToken = default)
  {
    try
    {
      using var response = await _httpClient.DeleteAsync(
        $"/api/audio/events/{Uri.EscapeDataString(playbackId)}", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to stop attended playback");
      return false;
    }
  }
}
```

**4c. Registration**, in `src/Radio.Web/Program.cs`, copying the `AudioApiService` block verbatim
(`:95-107`) with the type swapped:

```csharp
builder.Services.AddHttpClient<EventPlaybackApiService>(client =>
{
  client.BaseAddress = new Uri(apiBaseUrl);
  client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<ApiConnectionLoggingHandler>()
.ConfigurePrimaryHttpMessageHandler(() =>
{
  var handler = new HttpClientHandler();
  ConfigureHttpClientHandler(handler);
  return handler;
});
```

---

### Task 5 — the hub client registration

**File:** `src/Radio.Web/Services/Hub/AudioStateHubService.cs`

**5a. The event**, beside `SleepStateChanged`:

```csharp
  /// <summary>
  /// Raised when the one attended event playback changes state (ADR-029 D6 §8.1). Typed, like
  /// NowPlayingChanged and unlike PlaybackStateChanged: the payload IS the state, so a subscriber
  /// that re-fetched it over REST would be adding a round trip to a push that already carries
  /// everything. Fires on transitions only — there is no position tick (§8.2).
  /// </summary>
  public event Func<EventPlaybackSnapshotDto?, Task>? EventPlaybackChanged;
```

**5b. The registration**, beside the twelve existing `_hubConnection.On<…>` calls:

```csharp
      _hubConnection.On<EventPlaybackSnapshotDto?>("EventPlaybackChanged", async (dto) =>
      {
        _logger.LogDebug("Received EventPlaybackChanged event");
        if (EventPlaybackChanged != null)
        {
          await EventPlaybackChanged.Invoke(dto);
        }
      });
```

**5c.** Add `EventPlaybackChanged` to the class summary's `Handles:` list, which enumerates them.

---

### Task 6 — the store caches it, and seeds itself once from the re-attach path

**File:** `src/Radio.Web/Services/AudioStateStore.cs` (add `using Radio.Web.Services.ApiClients;`)
**ADR:** §8.1 ⟨A1·4⟩ · **See C-48** — the store is live, and this adds a third cached field to it.

**6a. The cached field and its change event:**

```csharp
  /// <summary>Raised when the one attended event playback changes state (ADR-029 D6).</summary>
  public event Func<Task>? EventPlaybackChanged;
```

```csharp
  /// <summary>
  /// Latest attended-playback snapshot, or null if none has been observed since this process
  /// started.
  /// </summary>
  /// <remarks>
  /// Process-wide, not per circuit, for the reason EncoderConfigStatus is: this store is registered
  /// AddSingleton, and there is one audio engine and one set of speakers, so the state it caches is
  /// global by nature (ADR-029 D6 §8.1). A terminal snapshot is RETAINED here exactly as it is on the
  /// server, so "nothing is playing" is a state rather than the absence of one — a chip that hid on a
  /// null would never show a failure.
  /// </remarks>
  public EventPlaybackSnapshotDto? EventPlayback { get; private set; }

  // 0 or 1. Set the first time a broadcast lands, read by the seed so a response already in flight
  // cannot overwrite something newer. The ENC-12 rule (MainLayout.razor:388-397).
  private int _eventPlaybackBroadcastSeen;

  // 0 or 1, claimed with Interlocked so two circuits opening at once seed exactly once.
  private int _eventPlaybackSeedStarted;
```

**6b. Subscribe and unsubscribe**, beside the ten existing pairs:

```csharp
    _hubService.EventPlaybackChanged += OnHubEventPlaybackChanged;
```
```csharp
    _hubService.EventPlaybackChanged -= OnHubEventPlaybackChanged;
```

**6c. The handler:**

```csharp
  private async Task OnHubEventPlaybackChanged(EventPlaybackSnapshotDto? dto)
  {
    EventPlayback = dto;
    Volatile.Write(ref _eventPlaybackBroadcastSeen, 1);
    await NotifyAsync(EventPlaybackChanged);
  }
```

**6d. The one-shot seed:**

```csharp
  /// <summary>
  /// Seeds <see cref="EventPlayback"/> from GET /api/audio/events/current. Runs at most once per
  /// process; every later call returns immediately.
  /// </summary>
  /// <remarks>
  /// ADR-029 §8.1 ⟨A1·4⟩ makes this a requirement rather than a nicety: broadcasts fire on
  /// TRANSITIONS, so a client connecting between two of them would render "nothing is playing" while
  /// the room is talking. A fresh circuit arriving mid-playback is now routine — the user navigated
  /// away and back, the kiosk refreshed, a second browser opened.
  ///
  /// ⚠ A one-shot PULL, not a poll. Trap 5 of the arc breakdown disqualifies a poll outright.
  ///
  /// ⚠ The API client is a PARAMETER rather than a constructor dependency, and that is not style. A
  /// Web singleton cannot inject a typed HttpClient (ADR-022 §6.2, and BellHealthService's class
  /// remark says so in as many words) — holding one for the process lifetime pins a handler that is
  /// meant to rotate. The caller resolves it in a scope and hands it in, so this store stays free of
  /// HTTP.
  ///
  /// ⚠ Ordering, and it is ENC-12's rule rather than a new one: a broadcast that lands while the
  /// pull is in flight describes a LATER moment than the response now in hand, so it wins and the
  /// response is discarded. Seeding from the cache alone was wrong on exactly the boot the seed
  /// exists for — a deploy restarts both services together, so the API can broadcast while
  /// AudioStateHubService.StartAsync is still in its retry loop, and that broadcast reaches nobody.
  ///
  /// ⚠ Never throws. Its callers are a CircuitHandler and, from PR 6, a layout; neither is worth a
  /// blank screen.
  ///
  /// ⚠ KNOWN LIMITATION, shared with every other cached broadcast in this store and deliberately not
  /// fixed here: a hub connection that drops and reconnects can miss transitions, and nothing
  /// re-seeds. What bounds the damage is that the next transition corrects it, and that
  /// GvMedia:MaxPlaybackSeconds bounds how long a missed one can matter. Re-seeding on Reconnected is
  /// a fast-follow.
  /// </remarks>
  public async Task EnsureEventPlaybackSeededAsync(
    EventPlaybackApiService api, CancellationToken cancellationToken = default)
  {
    if (Interlocked.Exchange(ref _eventPlaybackSeedStarted, 1) != 0)
    {
      return;
    }

    try
    {
      var snapshot = await api.GetCurrentAsync(cancellationToken);

      if (Volatile.Read(ref _eventPlaybackBroadcastSeen) != 0)
      {
        return;
      }

      if (snapshot is not null)
      {
        EventPlayback = snapshot;
        await NotifyAsync(EventPlaybackChanged);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error seeding attended playback state");
    }
  }
```

---

### Task 7 — the last-circuit-closed backstop

**Files:** new `src/Radio.Web/Services/AttendedPlaybackCircuitHandler.cs`, `src/Radio.Web/Program.cs`
**ADR:** D7 §7.3, §7.4 · **See C-52, U2, U3.**

```csharp
using Microsoft.AspNetCore.Components.Server.Circuits;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Services;

/// <summary>
/// ADR-029 D7 §7.3 — the last-circuit-closed backstop for attended playback, and the first
/// CircuitHandler this application has ever had.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ It fires on "no circuits remain", NOT on "the circuit that started it left". The original design
/// matched a departing circuit against an OwnerToken; ADR-029 ⟨A1·4⟩ deleted both, because a kiosk
/// refresh drops one circuit and opens another, so an owner-matched handler would stop audio the user
/// is actively watching about three minutes later for no visible reason. There is one audio engine
/// and one set of speakers, so there is no owner (§7.4).
/// </para>
/// <para>
/// ⚠ SINGLETON, deliberately. CircuitHandler instances are resolved from each circuit's scope, so a
/// singleton registration hands the SAME instance to every circuit and the count is process-wide. A
/// scoped registration would give every circuit its own counter, each reaching zero when that circuit
/// closes — which is precisely the owner-circuit rule this class exists not to be. If that resolution
/// behaviour ever changes, move the counter into a separate singleton and keep this scoped; the count
/// is the thing that must be shared, not the handler.
/// </para>
/// <para>
/// ⚠ This is the WEAKEST of the three defences and must not be trusted as the guarantee. Blazor
/// Server closes a circuit not at tab close but after the disconnect retention window (~3 minutes by
/// default), so this is a minutes-latency mechanism and for a short voicemail the recording has
/// simply finished first. EventPlaybackService's max-duration cap is the guarantee (§7.1); this is a
/// latency improvement on it.
/// </para>
/// <para>
/// A Web singleton cannot inject a typed HttpClient, so the API client is resolved through a scope
/// per use — the shape BellHealthService and GvBridgeStatusService already use (ADR-022 §6.2).
/// </para>
/// </remarks>
public sealed class AttendedPlaybackCircuitHandler : CircuitHandler
{
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly AudioStateStore _store;
  private readonly ILogger<AttendedPlaybackCircuitHandler> _logger;

  private int _openCircuits;

  public AttendedPlaybackCircuitHandler(
    IServiceScopeFactory scopeFactory,
    AudioStateStore store,
    ILogger<AttendedPlaybackCircuitHandler> logger)
  {
    _scopeFactory = scopeFactory;
    _store = store;
    _logger = logger;
  }

  /// <summary>Live circuits. Exposed for tests and for diagnostics, never for a policy decision.</summary>
  internal int OpenCircuits => Volatile.Read(ref _openCircuits);

  /// <inheritdoc />
  public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
  {
    var count = Interlocked.Increment(ref _openCircuits);
    _logger.LogDebug("Circuit opened; {Count} live", count);

    // A circuit opening IS ADR-029 §8.1's re-attach moment: a client has arrived and may be arriving
    // mid-playback. The seed is one-shot per process and never throws, so this is fire-and-forget by
    // design rather than by omission — awaiting it would hold the circuit's start behind an HTTP call
    // to a service that may still be booting, on a deploy that restarts both together.
    _ = SeedAsync();
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public override async Task OnCircuitClosedAsync(
    Circuit circuit, CancellationToken cancellationToken)
  {
    var remaining = Interlocked.Decrement(ref _openCircuits);

    if (remaining < 0)
    {
      // A close with no matching open. Not reachable today — this handler is registered before the
      // app serves a request — but a count left negative would make the "== 0" test below unreachable
      // for the life of the process: a backstop that has silently stopped backstopping. Reset loudly.
      Interlocked.Exchange(ref _openCircuits, 0);
      _logger.LogWarning("Circuit closed with no matching open; live-circuit count reset to zero");
      return;
    }

    if (remaining != 0)
    {
      _logger.LogDebug("Circuit closed; {Count} still live", remaining);
      return;
    }

    // ⚠ The TRANSITION to zero, never an observed zero. Radio.Web restarting while Radio.API keeps
    // playing leaves the count at zero at rest, and nothing about that is a client walking away.
    await StopAttendedPlaybackAsync();
  }

  private async Task SeedAsync()
  {
    try
    {
      using var scope = _scopeFactory.CreateScope();
      var api = scope.ServiceProvider.GetRequiredService<EventPlaybackApiService>();
      await _store.EnsureEventPlaybackSeededAsync(api);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error seeding attended playback state on circuit open");
    }
  }

  private async Task StopAttendedPlaybackAsync()
  {
    // Read from the store rather than re-reading GET /current. The store is fed by the same broadcast
    // a fresh read would be racing, and a stale read costs nothing here: a stop against an id that
    // has already ended answers 404 or 409, and EventPlaybackApiService.StopAsync reports both as a
    // plain false without logging an error.
    if (_store.EventPlayback is not { IsLive: true } snapshot)
    {
      _logger.LogDebug("Last circuit closed; no attended playback to stop");
      return;
    }

    _logger.LogInformation(
      "Last circuit closed with attended playback {Id} still live; stopping it (ADR-029 §7.3)",
      snapshot.Id);

    try
    {
      using var scope = _scopeFactory.CreateScope();
      var api = scope.ServiceProvider.GetRequiredService<EventPlaybackApiService>();
      await api.StopAsync(snapshot.Id);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error stopping attended playback after the last circuit closed");
    }
  }
}
```

**Registration**, in `src/Radio.Web/Program.cs` beside the other singletons (add
`using Microsoft.AspNetCore.Components.Server.Circuits;`), following the concrete-then-alias pattern
`AddEventPlayback` and `GvBridgeStatusService` already use so both resolve to one instance:

```csharp
// ADR-029 D7 §7.3 — the last-circuit-closed backstop for attended playback. Registered concretely
// and then aliased so the CircuitHandler every circuit resolves and the singleton holding the count
// are the SAME object; two would be two counters, each reaching zero on its own circuit's close.
builder.Services.AddSingleton<AttendedPlaybackCircuitHandler>();
builder.Services.AddSingleton<CircuitHandler>(sp =>
  sp.GetRequiredService<AttendedPlaybackCircuitHandler>());
```

---

### Task 8 — the cap's tests, driven by a fake clock

**File:** `tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs` — **extend in
place** (S14). Never write a second fixture.

**8a. Thread the clock through the fixture.** One parameter and one argument:

```csharp
  private EventPlaybackService CreateService(
    ITTSFactory? ttsFactory = null,
    GvMediaOptions? gvMedia = null,
    TTSOptions? tts = null,
    HttpMessageHandler? httpHandler = null,
    FakeDuckingService? ducking = null,
    CapturingLoggerProvider? logs = null,
    TimeProvider? timeProvider = null)
  {
    // … unchanged …
    return new EventPlaybackService(
      logs?.CreateLogger<EventPlaybackService>() ?? NullLogger<EventPlaybackService>.Instance,
      gvMonitor,
      new StaticOptionsMonitor<TTSOptions>(tts ?? DeployedTtsOptions()),
      ttsFactory ?? new FakeTtsFactory(),
      fileFactory,
      ducking ?? _ducking,
      client,
      timeProvider);
  }
```

**8b. The five tests**, in a new `// ── the max-duration cap (ADR-029 D7 §7.1) ──` section.

```csharp
  [Fact]
  public async Task TheDurationCapStopsAPlaybackThatOutlivesMaxPlaybackSeconds()
  {
    // ADR-029 D7 §7.1 — "This is THE guarantee. No client cooperation, no heartbeat, no timer loop,
    // no polling." Everything else in D7 is a latency improvement on this line.
    //
    // ⚠ Driven by FakeTimeProvider, never by Task.Delay. CLAUDE.md § Test Timing forbids racing a
    // wall clock against a wall clock, and TEST-4 is the row about the last time this repo did.
    // Advance() fires every DUE timer synchronously before it returns, and the rendezvous below is on
    // the Stopped SNAPSHOT rather than on elapsed time — so both halves are deterministic.
    var time = new FakeTimeProvider();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(
      ttsFactory: tts,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = 30
      },
      timeProvider: time);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    var accepted = await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    // Subscribed BEFORE the advance, so the transition cannot be missed.
    var stopped = NextSnapshotWith(service, EventPlaybackState.Stopped);
    time.Advance(TimeSpan.FromSeconds(30));

    var final = await stopped.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(accepted.Id, final.Id);
    Assert.Equal(1, source.StopCalls);
    Assert.Equal(EventPlaybackState.Stopped, service.Current!.State);
  }

  [Fact]
  public async Task TheDurationCapDoesNotFireBeforeItsTime()
  {
    // ⚠ A NEGATIVE assertion that is DETERMINISTIC rather than merely patient, and this test says so
    // about itself because CLAUDE.md § Test Timing asks a test to. FakeTimeProvider.Advance runs
    // every due timer synchronously before returning, so when it returns with none due there is
    // nothing in flight for the assertions to lose a race to. This is NOT "no event arrived within
    // 200 ms", and it is the reason the cap uses TimeProvider rather than a raw Timer.
    var time = new FakeTimeProvider();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(
      ttsFactory: tts,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = 30
      },
      timeProvider: time);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    time.Advance(TimeSpan.FromSeconds(29));

    Assert.Equal(EventPlaybackState.Playing, service.Current!.State);
    Assert.Equal(0, source.StopCalls);
  }

  [Fact]
  public async Task ANaturalEndDisarmsTheDurationCap()
  {
    // The disarm lives in ReleaseSourceAsync, the one funnel that stops and disposes a source. If it
    // were missing, a ten-second voicemail would leave a five-minute timer alive — and on this box a
    // timer per playback that never fires is the shape trap 5 of the arc breakdown exists to refuse.
    // Observable consequence, and the reason this assertion is on StopCalls rather than on a state:
    // the playback is already Completed, so a late cap would change no snapshot; it would only touch
    // the source a second time.
    var time = new FakeTimeProvider();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(
      ttsFactory: tts,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = 30
      },
      timeProvider: time);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    var completed = NextSnapshotWith(service, EventPlaybackState.Completed);
    source.RaiseCompleted(PlaybackCompletionReason.EndOfContent);
    await completed.WaitAsync(TimeSpan.FromSeconds(5));

    var stopsAtEnd = source.StopCalls;
    time.Advance(TimeSpan.FromMinutes(10));

    Assert.Equal(stopsAtEnd, source.StopCalls);
    Assert.Equal(EventPlaybackState.Completed, service.Current!.State);
  }

  [Fact]
  public async Task AReplacedPlaybackDoesNotTakeItsReplacementDownWhenItsCapExpires()
  {
    // Two independent reasons this holds, and the test exists because only one of them is obvious.
    // (1) The replaced playback's teardown runs ReleaseSourceAsync, which disarms its cap.
    // (2) Even if it did not, the callback addresses StopAsync BY THE OLD ID, which no longer matches
    //     _current, so it is a no-op.
    // Belt and braces — but a refactor that made the cap address "whatever is current" would turn a
    // stale timer into a stop of an unrelated playback, and this is the test that catches it.
    var time = new FakeTimeProvider();
    var first = new FakeEventSource();
    var second = new FakeEventSource();
    var queue = new Queue<IEventAudioSource>([first, second]);
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult(queue.Dequeue()) };
    using var service = CreateService(
      ttsFactory: tts,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = 30
      },
      timeProvider: time);

    var firstPlaying = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await firstPlaying.WaitAsync(TimeSpan.FromSeconds(5));

    // Ten seconds into the first playback, a second one replaces it — so the first's cap would fire
    // twenty seconds from now, while the second's has thirty seconds to run from here.
    time.Advance(TimeSpan.FromSeconds(10));
    var replaced = NextSnapshotWith(service, EventPlaybackState.Stopped);
    var two = await service.StartAsync(SpeechRequest());
    await replaced.WaitAsync(TimeSpan.FromSeconds(5));
    await WaitUntilAsync(() => second.PlayCalls == 1, TimeSpan.FromSeconds(5));

    time.Advance(TimeSpan.FromSeconds(21));

    Assert.Equal(two.Id, service.Current!.Id);
    Assert.Equal(EventPlaybackState.Playing, service.Current!.State);
    Assert.Equal(0, second.StopCalls);
  }

  [Fact]
  public async Task AZeroMaxPlaybackSecondsClampsToOneSecondRatherThanMeaningNoCap()
  {
    // ⚠ There is deliberately no off switch (ADR-029 §7.1 calls this THE guarantee), and this pins
    // which direction a nonsense value resolves in. The alternative reading — 0 means "never cap" —
    // is the PreemptAtPriority trap in another key: a number that silently deletes a safety property
    // while leaving it looking configured (plan PHN-1d C-43).
    var time = new FakeTimeProvider();
    var source = new FakeEventSource();
    var tts = new FakeTtsFactory { OnCreate = (_, _, _) => Task.FromResult<IEventAudioSource>(source) };
    using var service = CreateService(
      ttsFactory: tts,
      gvMedia: new GvMediaOptions
      {
        Enabled = true, CacheDirectory = _cacheDir, MaxPlaybackSeconds = 0
      },
      timeProvider: time);

    var playing = NextSnapshotWith(service, EventPlaybackState.Playing);
    await service.StartAsync(SpeechRequest());
    await playing.WaitAsync(TimeSpan.FromSeconds(5));

    var stopped = NextSnapshotWith(service, EventPlaybackState.Stopped);
    time.Advance(TimeSpan.FromSeconds(1));

    await stopped.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(1, source.StopCalls);
  }
```

⚠ `FakeTtsFactory.OnCreate` returning from a `Queue` is the shape
`ASecondStartReplacesTheFirst_AndTheFirstIsTornDown` already uses; re-read it before writing the
replacement test rather than inventing a second way to hand out two sources.

---

### Task 9 — the API side: the broadcast's shape, and the resolution that now happens at boot

**Files:** `tests/Radio.API.Tests/Services/AudioStateUpdateServiceTests.cs` (extend),
`tests/Radio.API.Tests/Services/SleepServiceTests.cs` (extend)

**9a. The C-47 pin — the single most valuable test in this row.**

```csharp
  [Fact]
  public async Task EventPlaybackChanged_PutsStateAndKindOnTheWireAsStrings()
  {
    // ⚠ THE C-47 PIN. Radio.API registers JsonStringEnumConverter on
    // AddControllers().AddJsonOptions ONLY; SignalR serialises through
    // JsonHubProtocol.PayloadSerializerOptions, which this project never configures. Handing the
    // snapshot record straight to SendAsync would put "state": 1 on the hub while
    // GET /api/audio/events/current says "state": "Playing" — and ADR-029 §8.1 feeds BOTH into the
    // same client field, the REST call as the seed and this as the update.
    //
    // ⚠ Asserted by SERIALISING the captured payload and reading the JSON, not by reflecting over the
    // anonymous type. Both would catch today's defect; only the JSON states the property that
    // actually matters, which is what a client parses.
    var fake = new FakeEventPlaybackService();
    object? captured = null;

    var service = CreateServiceWith(fake, onSend: (method, args) =>
    {
      if (method == "EventPlaybackChanged")
      {
        captured = args[0];
      }
    });

    fake.Raise(new EventPlaybackSnapshot(
      "evp-abc", EventPlaybackKind.RemoteMedia, "Voicemail from Jane",
      EventPlaybackState.Playing, TimeSpan.FromSeconds(29), TimeSpan.Zero,
      DateTimeOffset.UtcNow, null));

    await WaitUntilAsync(() => captured is not null, TimeSpan.FromSeconds(5));

    var json = JsonSerializer.Serialize(captured, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    using var doc = JsonDocument.Parse(json);

    Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("state").ValueKind);
    Assert.Equal("Playing", doc.RootElement.GetProperty("state").GetString());
    Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("kind").ValueKind);
    Assert.Equal("RemoteMedia", doc.RootElement.GetProperty("kind").GetString());

    service.Dispose();
  }
```

**9b. The same payload deserialises into the Web DTO's shape.** Declare a private record inside the
test file mirroring `EventPlaybackSnapshotDto`'s members and round-trip the captured payload into it,
asserting `Id`, `State`, `Label`, `Duration` and `FailureReason` all survive. This is the closest a
single assembly can get to proving the contract; the Web-side half is Task 10.

⚠ **State plainly what it does not prove:** it does not exercise `JsonHubProtocol`'s own options, and
no unit test in this repo can. **U1 and the wire shape are settled on the box**, and §2.2 carries the
`curl` and the browser check.

**9c. The remaining three, specified rather than written out** — all mechanical against the existing
`CreateService()` fixture plus a `FakeEventPlaybackService` (a `Current` property, a `PlaybackChanged`
event, a `Raise` helper, and `StopAsync` recording the ids it was given):

| Test | Asserts |
|---|---|
| `AMissingEventPlaybackServiceDisablesTheBroadcastRatherThanFailingToStart` | Constructing over an empty `ServiceProvider` neither throws nor subscribes — the `GetService` null path, which is how every sibling here behaves |
| `DisposeUnsubscribesFromPlaybackChanged` | After `Dispose`, a raise sends nothing. A missed unsubscribe on a **singleton** event source keeps a dead hosted service alive for the process |
| `ASubscriberExceptionDoesNotEscapeTheHandler` | A throwing `IHubContext` is logged, not propagated — the `async void` hazard, and the reason the catch-all is there |

**9d. `SleepServiceTests` gains three** (Task 2):

| Test | Asserts |
|---|---|
| `EnteringSleepStopsAPlayingAttendedPlayback` | `StopAsync` called with the snapshot's id |
| `EnteringSleepDoesNotStopAPlaybackThatHasAlreadyEnded` | A retained `Completed` snapshot is left alone — `Current` being non-null is not the same as audio in the room |
| `EnteringSleepStillSleepsWhenTheStopThrows` | The mute, the pause and the `SleepStateChanged` broadcast all still happen. **Sleep is not allowed to fail because a voicemail would not stop** |

---

### Task 10 — the Web side: the client, the store's seed ordering, and the circuit count

**Files:** new `tests/Radio.Web.Tests/Services/ApiClients/EventPlaybackApiServiceTests.cs`, new
`tests/Radio.Web.Tests/Services/AudioStateStoreEventPlaybackTests.cs`, new
`tests/Radio.Web.Tests/Services/AttendedPlaybackCircuitHandlerTests.cs`, and one addition to the
existing `AudioStateHubServiceTests.cs`

**10a. `EventPlaybackApiServiceTests`** — over a stub `HttpMessageHandler`, following
`AudioApiServiceTests`:

| Test | Asserts |
|---|---|
| `GetCurrent_ReturnsNull_OnNoContent` | 204 → `null`, and **no exception**: 204 is an answer, not a failure |
| `GetCurrent_DeserialisesAStringState` | A body with `"state": "Playing"` yields `State == "Playing"` — the client half of C-47 |
| `GetCurrent_ReturnsNull_WhenTheApiIsUnreachable` | A throwing handler is caught, matching every sibling client |
| `Stop_ReturnsTrue_OnNoContent` and `Stop_ReturnsFalse_OnNotFoundAndOnConflict` | Both refusals are ordinary answers |
| `Stop_EscapesThePlaybackIdIntoThePath` | The id reaches the URL escaped. Ids are server-minted `evp-<guid>` today, so this is defence in depth on the same posture the seam takes toward `MediaId` |

**10b. `AudioStateStoreEventPlaybackTests`** — the ordering guard is the point:

| Test | Asserts |
|---|---|
| `ABroadcastCachesTheSnapshotAndRaisesTheChangeEvent` | `EventPlayback` set, `EventPlaybackChanged` raised |
| `TheSeedAppliesWhenNoBroadcastHasArrived` | Pull result cached |
| `ABroadcastThatLandsWhileTheSeedIsInFlightWINS` | ⭐ Given a `GetCurrentAsync` parked on a gate, a broadcast delivered before it is released must survive. This is the ENC-12 boot case — a deploy restarts both services, so the API can broadcast while the hub client is still retrying — and it is the assertion the whole ordering guard exists for |
| `TheSeedRunsAtMostOnce` | Two concurrent callers produce exactly one `GetCurrentAsync` |
| `TheSeedNeverThrows` | A throwing client is logged and swallowed. Its callers are a circuit handler and a layout |

**10c. `AttendedPlaybackCircuitHandlerTests`** — the brief's two-browser case, made a test:

```csharp
  // ⚠ Circuit has no public constructor, so every call below passes null!. That is not a compromise:
  // the handler must NEVER read its circuit argument. ADR-029 §7.4 deleted the ownership model
  // outright — "there is one audio engine and one set of speakers, so there is one playback and no
  // owner" — so a handler that looked at WHICH circuit closed would be reimplementing the rule §7.3
  // was rewritten to remove. The untestability of Circuit is a useful fence around that.
```

| Test | Asserts |
|---|---|
| `TwoCircuitsOpenAndOneClosing_StopsNothing` | ⭐ The kiosk and a laptop. The named case in the brief, and the one an owner-circuit implementation gets wrong |
| `TheLastCircuitClosing_StopsALivePlayback` | `StopAsync` called once, with the cached id |
| `TheLastCircuitClosing_StopsNothingWhenTheSnapshotIsTerminal` | A retained `Completed` is not live (`IsLive`) |
| `TheLastCircuitClosing_StopsAnUnrecognisedStateAnyway` | ⭐ A snapshot whose `State` is `"Waiting"` — a value this build has never heard of — **is** treated as live. This is the assertion that makes `PHN-1f` deployable without a lockstep `Radio.Web` build, and it must be written now, while the value really is unknown |
| `AClose_WithoutAnOpen_ResetsTheCountAndWarns` | The count does not go negative (C-52) |
| `ACircuitOpening_SeedsTheStoreOnce` | Two opens produce one `GetCurrentAsync` |
| `ASeedFailureOnOpenDoesNotFaultTheCircuit` | A throwing scope is swallowed |

**10d. `AudioStateHubServiceTests`** gains one registration test in the existing file's shape,
asserting that a delivered `"EventPlaybackChanged"` reaches the typed event with its payload intact.

---

### Task 11 — the documentation this row falsifies, and the two things it hands forward

**Files:** `design/INTEGRATIONS.md`, `design/FUTURE-WORK.md`

**11a. `design/INTEGRATIONS.md` § *Server-side GV media fetch and cache (`GvMedia`)*, the
configuration table.** The `MaxPlaybackSeconds` row currently ends *"Becomes the attended-playback cap
in a later PR."* ⚠ **Find it by content, not by line number** — plan `PHN-1d` C-34 records that four
documents carried a line number for a claim in this file and three were wrong. `grep -n
'Becomes the attended-playback cap'`. Replace the sentence with:

> Also the hard cap on one attended playback (ADR-029 §7.1): `EventPlaybackService` arms a one-shot
> timer when audio starts and stops the playback when it fires, with no client cooperation and no
> poll. **There is no "off"** — a value below 1 clamps to 1.

**11b. Same file, § *The attended-playback route family*.** Add a short subsection after the 404/409
paragraph:

> **State reaches the UI by push, and by one pull.** Every transition is broadcast on `/hubs/audio` as
> **`EventPlaybackChanged`**, carrying the same snapshot `GET /api/audio/events/current` returns —
> with `state` and `kind` as **strings** on both, so the same client field can be filled from either.
> There is deliberately **no position tick**: the snapshot is an anchor
> (`positionAtBroadcast` + `broadcastAtUtc` + `state`) and clients interpolate locally (ADR-029 §8.2).
> `Radio.Web` seeds its cache from the REST call **once per process**, because broadcasts fire on
> transitions and a client connecting between two of them would otherwise render silence over a
> talking room.
>
> **Three things stop an attended playback without anyone pressing Stop**, in descending order of
> trustworthiness: the `GvMedia:MaxPlaybackSeconds` cap (the guarantee — no client involved);
> **entering `/sleep`**, because that route runs under `EmptyLayout` and offers no transport (ADR-029
> §7.5); and **the last Blazor circuit closing**, which is a *minutes*-latency backstop because Blazor
> tears a circuit down after its disconnect-retention window rather than at tab close. Navigating
> between routes does **not** stop playback, and closing one of two open browsers does not either.

**11c. `design/FUTURE-WORK.md`** — two entries, per the project rule that nothing unimplemented is
left undocumented:

1. **Re-seed `AudioStateStore` after a hub reconnect.** What exists: a one-shot seed on the first
   circuit open. What is missing: `HubConnection.Reconnected` does not re-run it, so transitions
   missed during a drop leave the cache stale until the next one. What bounds it: the next transition
   corrects it, and `MaxPlaybackSeconds` bounds how long a missed one can matter. Priority: low.
2. **An `EndReason` on `EventPlaybackSnapshot`.** What exists: a user stop, a preemption and the
   duration cap all publish `Stopped`, distinguishable only in the log. What is needed: one nullable
   string, set at the three call sites. When: only if PR 6's chip branches on it. Why not now: a wire
   field no renderer reads is a commitment bought with nothing (plan §0.6).

⚠ **Do NOT touch § *How Audio Ducking Works*.** `PHN-1d` rewrote that correction and this row changes
no ducking behaviour; editing it would be re-litigating a merged decision.

---

### Task 12 — build, test, and the scope gate

```bash
dotnet build --configuration Release            # 0 warnings expected
dotnet test  --configuration Release --verbosity normal
```

Then the gate. **Every command below must print what the comment says, and a Builder runs them before
opening the PR rather than after.**

```bash
# No ducking change of any kind. Both tripwire files must be untouched (§0.5 item 3).
git diff --name-only main...HEAD | grep -E 'DuckingService|DuckingServiceTests|DuckingServiceCharacterizationTests'
# → nothing

# No queue. None of these may appear in any CODE file (§0.5 item 2).
# ⚠ Scoped to src/ and tests/ deliberately: this plan's own §5 DESIGNS the queue and uses all three
# words, so an unscoped grep would fire on the very document that forbids the code. Every grep below
# that reads diff CONTENT rather than filenames is scoped for the same reason.
git diff main...HEAD -- src tests | grep -nE 'Waiting|MaxQueuedWaitSeconds|WaitExpired'
# → nothing

# No new config key (§0.5 item 4).
git diff main...HEAD -- src/Radio.API/appsettings.json src/Radio.Core/Configuration/GvMediaOptions.cs \
  | grep -E '^\+' | grep -vE '^\+\+\+' | grep -E 'public (int|bool|string)'
# → nothing

# The broadcast never calls back into the seam (C-54). The handler's body must contain no
# _eventPlayback member access at all.
sed -n '/OnEventPlaybackChanged/,/^  }/p' src/Radio.API/Services/AudioStateUpdateService.cs | grep '_eventPlayback'
# → nothing

# The cap is NOT CancelAfter (C-49).
grep -n 'CancelAfter' src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs
# → nothing

# The three ownership words ADR §7.4 deleted must not come back.
git diff main...HEAD -- src tests | grep -nE 'OwnerToken|ownerCircuit|owningCircuit'
# → nothing

# No mixer.AddSource anywhere in this diff — the most copy-able mistake in the arc (PHN-1c §0.6).
git diff main...HEAD -- src tests | grep -n 'AddSource'
# → nothing

# The three files another cycle owns (§0.5 item 8).
git diff --name-only main...HEAD | grep -E 'BUILDER_QUEUE|HANDOFF-GA-PUNCH-LIST|HANDOFF-NEXT-SESSION'
# → nothing

# The full expected file list. Anything else is scope creep.
git diff --name-only main...HEAD
```

Expected files, and nothing else:

```
design/FUTURE-WORK.md
design/INTEGRATIONS.md
design/plans/PHN-1e-server-owned-state-and-the-queue.md
src/Radio.API/Program.cs                                        (registration only if needed)
src/Radio.API/Services/AudioStateUpdateService.cs
src/Radio.API/Services/SleepService.cs
src/Radio.Core/Configuration/GvMediaOptions.cs                  (one doc)
src/Radio.Core/Interfaces/Audio/IEventPlaybackService.cs        (one doc)
src/Radio.Infrastructure/Audio/Services/EventPlaybackService.cs
src/Radio.Web/Models/ApiModels.cs
src/Radio.Web/Program.cs
src/Radio.Web/Services/ApiClients/EventPlaybackApiService.cs
src/Radio.Web/Services/AttendedPlaybackCircuitHandler.cs
src/Radio.Web/Services/AudioStateStore.cs
src/Radio.Web/Services/Hub/AudioStateHubService.cs
tests/Radio.API.Tests/Services/AudioStateUpdateServiceTests.cs
tests/Radio.API.Tests/Services/SleepServiceTests.cs
tests/Radio.Infrastructure.Tests/Audio/Events/EventPlaybackServiceTests.cs
tests/Radio.Web.Tests/Services/ApiClients/EventPlaybackApiServiceTests.cs
tests/Radio.Web.Tests/Services/AttendedPlaybackCircuitHandlerTests.cs
tests/Radio.Web.Tests/Services/AudioStateHubServiceTests.cs
tests/Radio.Web.Tests/Services/AudioStateStoreEventPlaybackTests.cs
```

---

## 2. Test Plan

### 2.1 What the automated tests actually prove

- The cap **stops audio**, fires at the configured second and not before, is disarmed by a natural
  end, and cannot reach across a replacement (Task 8). Deterministically, on a fake clock.
- The broadcast puts **strings** on the wire for both enums, survives a missing service, unsubscribes
  on dispose and cannot take the host down (Task 9).
- Entering sleep **stops** a live playback, leaves a finished one alone, and **still sleeps** when the
  stop fails (Task 9d).
- The store seeds **once**, and a broadcast racing the seed **wins** (Task 10b).
- The backstop fires on the **transition** to zero and not on one browser of two closing, and treats
  a state it has never heard of as live (Task 10c).

### 2.2 What the tests cannot prove — carried to PR 6's UAT on the box

1. ⭐ **That the broadcast deserialises through the real `JsonHubProtocol`** (U1). No unit test here
   uses SignalR's own serialiser. The check, with `GvMedia:Enabled` temporarily true:
   ```bash
   curl -s -X POST http://radio:5000/api/audio/events \
     -H 'Content-Type: application/json' \
     -d '{"kind":"Speech","text":"Testing the broadcast","label":"Test"}' | head -c 400
   curl -s http://radio:5000/api/audio/events/current | head -c 400
   ```
   Then confirm from the kiosk's CDP console that the Web process logged
   `Received EventPlaybackChanged event` — the file sink, not journald, since `LOG-11`:
   ```bash
   ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -c EventPlaybackChanged $F'
   ```
   ⚠ **A `state` that arrives as a number rather than `"Playing"` is C-47 having been got wrong**, and
   it will look like "the chip does not update" rather than like a serialisation fault.
2. **That the max-duration cap fires on real hardware.** Set `GvMedia:MaxPlaybackSeconds` to 20 in
   `/opt/radio-console/api/appsettings.Production.json`, restart `radio-api`, play a longer
   voicemail, and confirm the audio stops at 20 s and the journal carries the Warning (it is above the
   `LOG-11` threshold, so `journalctl -u radio-api --since '-10min'` is the right query). **Put the
   value back.**
3. **That the last-circuit backstop fires, and how long it takes** (U3). Play a voicemail, close every
   browser on the LAN including the kiosk, and wait out the disconnect-retention window. ⚠ **Record
   the measured latency**, because ADR §7.3 asserts "~3 minutes" from the framework default and nobody
   has watched it here.
4. ⭐ **That a browser REFRESH does not stop playback** (U3, and the failure §7.3 was rewritten to
   avoid). Play a voicemail, reload the kiosk, and confirm the audio continues — then keep watching
   for four minutes, because the old circuit's close lands *after* the retention window and a wrong
   implementation stops the audio then, not immediately.
5. **That two browsers behave** — kiosk plus a laptop, close the laptop, audio continues.
6. **That entering `/sleep` stops it** — start a voicemail, press the Sleep pill, confirm silence, and
   then **wake the panel and confirm it does not resume** (C-51's whole point).
7. Everything `PHN-1a`/`PHN-1b`/`PHN-1c`/`PHN-1d` already carried to PR 6 is unchanged: seek
   repositions; `Time` advances; pausing a TTS source does not report completion; `./data/gvmedia` is
   writable under the service account; the preemption listening test; and the row's own settling check
   (duck, mute, volume, Cast).

⚠ **A `MediaNotFound` during any of this is as likely to be the GV auth blackout as a bad id**
(`PHN-1c` C-22). Record the wall-clock time and retry after five minutes before concluding anything.

### 2.3 Commands

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal \
  --filter "FullyQualifiedName~EventPlaybackServiceTests"
dotnet test --configuration Release --verbosity normal \
  --filter "FullyQualifiedName~AttendedPlaybackCircuitHandlerTests|FullyQualifiedName~AudioStateStoreEventPlayback"
dotnet test --configuration Release --verbosity normal
```

---

## 3. Self-review

**Placeholder scan.** No `TBD`, no "similar to Task N", no "implement later". Every task carries the
literal code or, where it is a test matrix, the literal name and the literal assertion. The three
places that say *specified rather than written out* (Tasks 9c, 10a, 10b, 10c) are test tables with an
exact name and an exact assertion per row, over a fixture the plan defines — and they say so rather
than implying full code exists.

**Internal consistency.** The cap is armed in exactly one place (Task 1c) and disarmed in exactly one
(Task 1d), and Task 8's third and fourth tests are the two ways that pairing can be wrong. The enum
string decision is made once (C-47) and consumed by Tasks 3, 4, 9 and 10 without restatement. The
"transition to zero" rule is stated once (C-52) and tested once (Task 10c).

**Scope check.** Eight ⛔ items in §0.5, each with a grep in Task 12. The riskiest scope creep this
plan could invite is the `DuckingStateChangedEventArgs` change — it is genuinely tempting here,
because it closes two of `PHN-1d`'s residuals — and §5 Q4 argues at length that it belongs with the
queue rather than here, on the principle that a Core contract change must arrive with the feature that
motivates it. Task 12 greps for it.

**Ambiguity check.** Three things a Builder could reasonably get wrong, all called out at the point of
decision: the cap is a **timer, not a cancellation** (C-49, and two merged plans say otherwise); the
`CircuitHandler` is **singleton, not scoped** (Task 7's remark, with U2's fallback); and the seed's
ordering is **broadcast-wins**, not last-write-wins (Task 6d).

**One thing this plan states it cannot verify.** U1, U2 and U3 are open by construction — the first
needs the real hub protocol, the second and third need a real Blazor host. §2.2 items 1, 3 and 4 are
where they get settled, and no test in §1 claims otherwise.

---

## 4. What this plan deliberately does not do, with the reason

1. **Does not add `EventPlaybackState.Waiting`.** §5 / `PHN-1f`. `D28` forbade exactly this for PR 4
   and the reason does not change with the PR number: an enum member on the wire that no code can
   produce is a lie the size of a state.
2. **Does not touch `DuckingService` or `DuckingStateChangedEventArgs`.** §5 Q4 — it is the queue's,
   and it must arrive with the feature that justifies it.
3. **Does not correct `IEventPlaybackService.Current`'s "in-flight" summary.** C-50: this row adds no
   state that makes it false, and the existing remark already documents the one exception that does.
4. **Does not add an `EndReason` to the snapshot.** §0.6 — no renderer branches on it yet, and PR 6 is
   the row that can say whether one will.
5. **Does not fix `TTSEventSource.Position`.** §0.5 item 6 — PR 6 owns it, because PR 6 is the first
   row with a scrubber that is visibly wrong.
6. **Does not register `TimeProvider` in DI.** The optional constructor parameter defaults to
   `TimeProvider.System`; registering it would change the resolution of every future consumer for one
   timer's benefit.
7. **Does not add a route.** ADR §7.3's backstop is Web-side because circuits are Web-side, and it
   reaches the API through the `DELETE` that already ships.
8. **Does not re-seed the store on hub reconnect.** A real gap, filed in `FUTURE-WORK.md` (Task 11c)
   rather than fixed, because it is shared with every other cached broadcast in that store and fixing
   it for one field would be the inconsistency it looks like a fix for.

---

## 5. Handoff — and the queue, designed rather than deferred

### 5.1 ⭐ THE MIRROR-CASE QUEUE (`D28`) — the five questions, answered

`PHN-1d` §5 recorded five open questions with a lean on each and said *"none of these is settled, and
PR 5's planner owns the decision."* They are settled below. **Three leans are confirmed, one is
overturned, and one dissolves** — the fifth question turns out to be an artefact of an assumption
about *where* the wait happens, and choosing differently makes it not arise.

Everything here is a task specification. If the owner rejects §0.2's split, it appends to §1 unchanged.

---

#### The shape the five answers fall out of, stated first

**A waiting playback IS the current playback, in a new state. There is no pending slot.**

`PHN-1d` §5 framed the queue as "a pending slot that `StopAsync` must also resolve and `Current` must
also report". Read against the shipped seam, that framing costs a great deal and buys nothing: there is
**one** attended playback by construction (D6 §8.1), and a playback that is waiting is simply one that
has not started producing audio yet — exactly like `Preparing`, which the seam already models.

So the change is one state and one `await`, placed inside the acquisition path that already exists:

```
StartAsync            → mints the playback, installs it as _current, publishes Preparing   (unchanged)
AcquireAndPlayAsync   → fetches or synthesises                                             (unchanged)
                      → ⭐ NEW: if the air is not clear, publish Waiting and await it
                      → _gate.WaitAsync … TryAdopt … StartDuckingAsync … PlayAsync … Playing (unchanged)
```

Four things come free rather than being built, and they are four of the five questions:

| Falls out of the shape | Why |
|---|---|
| Replace semantics | `StartAsync`'s existing replacement arm tears down whatever is in the slot, waiting or playing |
| `StopAsync` resolves it | It resolves `_current` by id; the waiting playback *is* `_current` |
| `Current` reports it | Same reason. `GET /api/audio/events/current` — ADR §8.1's re-attach path — carries it with no controller change |
| Pause / seek / resume refuse it correctly | They resolve `playback.Source`, which is null until adoption, so `EventPlaybackController.Transport` answers **409** and not 404 (S15) |

⚠ **Acquire FIRST, then wait** — not the other way round, and the reason is a UX one rather than a
mechanical one. Acquiring during the wait means the audio is ready the instant the room goes quiet, and
it means an acquisition **failure** surfaces immediately rather than after twenty seconds of Waiting —
*"wait, then fail"* being a strictly worse version of the shape `D28` rejected. The cost is one open
`FileStream` over a cached recording held for the length of the wait, which is bounded by Q3 and which
`TearDownAsync` disposes on any cancel.

---

#### Q1 — Depth. **Lean CONFIRMED (one deep, replace semantics). Its mechanism is overturned.**

One deep. A second tap while one is waiting **replaces** the waiting one; it never builds a list.
`PHN-1d` §5's two reasons both stand — `StartAsync` already does exactly this for in-flight playbacks
(ADR §6.2 rule 1), so the console gains no second mental model; and a queue deeper than one is a list
the user can neither see nor reorder on a wall panel.

**What is overturned is the pending slot.** There is nothing to add: replacement is
`StartAsync`'s existing arm acting on `_current`, and it already publishes `Stopped` for the playback
it displaces. A separate slot would be a second place a playback can live, and every one of Q2's
requirements would then have to be written twice.

---

#### Q2 — The cancel path. **Lean CONFIRMED, and it costs no code.**

`StopAsync(id)` takes `_gate`, matches `_current.Id`, claims the terminal flag and tears down. For a
waiting playback that teardown is nearly empty and **exactly right**: `ClaimSourceForRelease` returns
null because nothing was adopted, and — this is the load-bearing part, already built in PR 3 — it
**permanently closes adoption**, so when the wait unblocks, `TryAdopt` refuses and the acquisition path
disposes the source it is holding through `DisposeOrphanAsync`. The `Stopped` snapshot is published by
`StopAsync` as usual.

**The doc correction `PHN-1d` §5 asked for lands here, and it is narrower than it looked** (C-50).
`Current`'s summary says *"The one **in-flight** attended playback"* and its remark already opens *"⚠
'In flight' is not the whole of it"* to document the retained-terminal-snapshot case. This adds a
**second** exception rather than replacing the first:

```csharp
  /// <summary>
  /// The one attended playback this seam is tracking, or null. There is one audio engine and one set
  /// of speakers, so this state is global rather than per-caller (ADR-029 D6).
  /// </summary>
  /// <remarks>
  /// ⚠ "In flight" is not the whole of it, in TWO directions, and both are load-bearing for the 202
  /// shape.
  ///
  /// The LAST snapshot is RETAINED after a playback ends — Completed, Stopped or Failed — until a new
  /// playback replaces it. It has to be: StartAsync answers before any audio exists, so an
  /// acquisition failure has no response left to carry it, and this is the surface a caller re-reads
  /// to find out what happened (ADR-029 §8.1's re-attach path).
  ///
  /// And a playback can be WAITING: accepted, its audio already acquired, and deliberately not
  /// sounding because a source at or above GvMedia:PreemptAtPriority is (ADR-029 D5 §6.2 rule 2 read
  /// symmetrically; owner decision D28). It is not in flight and it is not finished.
  ///
  /// So null means "nothing has been started yet", never "nothing is playing" —
  /// EventPlaybackState on the snapshot is what says whether audio is being produced.
  /// </remarks>
```

⚠ **This is the fifth correction of this class in this arc.** `CLAUDE.md` § Pre-Merge Review exists
for it; making it in the same PR that falsifies the sentence is the whole discipline.

---

#### Q3 — Staleness. **Lean CONFIRMED: a hard bound, `Failed` + `"WaitExpired"`, 30 seconds.**

Thirty seconds, because the thing being waited on is a notification measured in seconds; a wait longer
than its blocker means the blocker was not what we thought. `Failed` is the honest state — it never
produced sound — and failing is acceptable *here*, and only here, precisely because by then the user
has watched a visible Waiting state, which is what made a bare refusal embarrassing.

**Mechanism, and it satisfies trap 5 without argument:**

```csharp
    await waiter.Task.WaitAsync(
      TimeSpan.FromSeconds(Math.Max(1, gv.MaxQueuedWaitSeconds)), _timeProvider, playback.Token);
```

One `Task.WaitAsync` overload does all three jobs — the wake, the bound and the cancel. It is a
**one-shot** timer, not a poll or a tick, and it is the same shape ADR §7.1 already blesses for the
duration cap. ⭐ **And it takes the `TimeProvider` this row already injects** (Task 1a), so the wait is
as testable as the cap: `FakeTimeProvider.Advance(30s)` produces `WaitExpired` deterministically, with
no `Task.Delay` anywhere near an assertion.

A `TimeoutException` from that call is mapped by a `catch` to `FailAsync(playback, "WaitExpired", ex)`
— reusing the existing terminal path rather than inventing one. `OperationCanceledException` already
falls through to the existing arm.

**Config: one new key, `GvMedia:MaxQueuedWaitSeconds`, default 30.** ⚠ Per `PHN-1b` **C-14** it goes in
`GvMediaOptions.cs` and `src/Radio.API/appsettings.json` — the file the deploy overwrites — and
**not** in `deploy/*/appsettings.Production.json`, which the deploy seeds only when absent and which
`radio` already has. **No off switch**, clamped at 1: a `0` meaning "never wait" would resolve to
mixing, which is the option `D28` rejected, and `PreemptAtPriority` is this arc's worked example of a
knob that deletes a behaviour while looking configured (`PHN-1d` C-43).

---

#### Q4 — The wake trigger. **⚠ LEAN OVERTURNED. Carry the priority and a start/stop discriminator on `DuckingStateChangedEventArgs`.**

`PHN-1d` §5 leaned to *"accept the starvation case in v1 and say so"*, and named the alternative it was
avoiding. `PHN-1d`'s own Builder independently proposed the alternative. **The Builder is right, and
the reason the lean does not survive is `D28` itself.**

**The starvation case, restated exactly.** `StopDuckingAsync` raises only when the ducking set
*empties* (`DuckingService.cs:217`, `needsRestore = _isDucking && remainingEvents == 0`). So a
priority-8 blocker ending while a priority-5 announcement continues produces **no raise at all**, the
waiting playback is never woken, and it expires at Q3's bound as `Failed` / `"WaitExpired"`.

**That outcome is `D28`'s rejected option, delivered late.** The owner rejected refusing because
*"press play, get an error, nothing happens"* is the punch list's tier (b) shape. A queue whose reachable
failure mode is *press play, watch a spinner for thirty seconds, get an error* is that same sentence
with a worse timeline. Shipping the queue with a live path to it would be filing the complaint on
purpose — which is the exact form of the argument the owner used to move this work into PR 5 in the
first place.

**How reachable is it?** Concurrent ducking sources are ordinary — `AnnouncementService` is the only
other caller and Home Assistant can post two events at once — but this case additionally needs the
*continuing* source to be **sub-8**, and `NotificationsController` clamps `request.Priority ?? 8`, so
that takes a caller naming a low priority explicitly. Rare today. **Not rare once
`PhoneIntegration:Enabled` is on**, where a ring at 9 and a caller-ID announcement at 8 overlap by
design.

**Why there is no cheaper fix.** Three were considered and none works: re-evaluating on `IsDucking:
false` alone is the edge that does not fire; `DuckingLevelChanged` fires per fade step and not on a
non-final stop either; and polling `GetActiveEventsByPriority` is trap 5. **The raise genuinely does
not exist**, so it has to be made to.

**The change, and it is small.** In `Radio.Core`:

```csharp
/// <summary>What happened to the ducking set, as distinct from what the aggregate state now is.</summary>
/// <remarks>
/// ⚠ This exists because IsDucking answers a DIFFERENT question and overloading it is what makes the
/// obvious implementations wrong. IsDucking is the AGGREGATE — "is anything ducking" — and
/// AudioManager keys ClearDuckingMultiplier off its false edge. A source leaving while others remain
/// is an Ended transition with IsDucking still TRUE, and the two facts must be separately expressible
/// or one of them has to lie.
/// </remarks>
public enum DuckingSourceTransition
{
  /// <summary>A source joined the ducking set.</summary>
  Started = 0,

  /// <summary>A source left the ducking set. Others may remain — read IsDucking for that.</summary>
  Ended = 1,

  /// <summary>Every source was cleared at once (StopAllDuckingAsync). TriggeringSource is null.</summary>
  AllCleared = 2
}
```

and on `DuckingStateChangedEventArgs`:

```csharp
  /// <summary>What happened to the set. See <see cref="DuckingSourceTransition"/>.</summary>
  public DuckingSourceTransition Transition { get; init; }

  /// <summary>
  /// The triggering source's priority, CAPTURED AT RAISE TIME, or 0 when there is no triggering
  /// source.
  /// </summary>
  /// <remarks>
  /// ⚠ Captured rather than looked up, and that is the entire point of the field. A subscriber that
  /// calls GetPriority for itself races DuckingService.StopDuckingAsync, which DELETES the override
  /// before it raises — so the answer for a source that has just left is the category default 8 for
  /// an announcement whose caller explicitly claimed 3. PHN-1d had to guard that with an
  /// ActiveEventCount check and could only narrow it, not close it. This closes it.
  /// </remarks>
  public int TriggeringSourcePriority { get; init; }
```

**`DuckingService` then raises on every removal, with the true aggregate:**

```csharp
    // Raised for EVERY source that LEAVES, not only when the set empties — the mirror of what
    // PHN-1d did for StartDuckingAsync, and for the same reason: a subscriber cannot act on a source
    // ending if it is never told one did. EventPlaybackService's queue wakes on this.
    //
    // ⚠ IsDucking carries the TRUE aggregate, so it stays false only when the set is actually empty.
    // That is what keeps AudioManager.ClearDuckingMultiplier firing exactly when it does today —
    // raising IsDucking:false while other sources remain would restore the radio to full volume
    // MID-ANNOUNCEMENT, which is the hazard that made this look unsafe until the transition field
    // separated the two questions.
    RaiseDuckingStateChanged(
      isDucking: !needsRestore, eventSource, DuckingSourceTransition.Ended, priorityBeforeRemoval);
```

⚠ **`priorityBeforeRemoval` must be read inside the same `lock (_lock)` that removes the entry**, and
before the removal. That is the whole of the capture.

**`AudioManager.OnDuckingStateChanged` branches on `Transition`, not on `IsDucking`.** Required, not
optional: with `IsDucking` true on an `Ended` raise, today's code would log *"Ducking started"* for a
source that stopped — a new instance of the exact class `CLAUDE.md` § Pre-Merge Review names.
`ClearDuckingMultiplier` stays on the `!IsDucking` edge and therefore fires exactly when it fires
today.

**`EventPlaybackService.OnDuckingStateChanged` keys on `Transition == Started` and reads
`e.TriggeringSourcePriority`.** ⭐ **That closes both of the residuals PR 4 handed forward, as a
by-product rather than as tasks:**

1. **The fade window.** `DuckingService` raises its transition event *after* awaiting the attack fade,
   so a stop landing inside that ~100 ms deletes the override first and a synchronous `GetPriority`
   still answers 8. With the priority captured at raise time inside the lock, there is nothing left to
   race. The `ActiveEventCount == 0` guard `PHN-1d` added becomes redundant and should be **deleted
   with its comment**, not left as a fossil.
2. **`ActiveEventCount`'s residual** — the same guard's acknowledged hole, *"if some OTHER source is
   still ducking, the count is non-zero and this guard does not fire"*. Same fix, same line.

**⭐ THE BRIEF ASKS WHICH PR CLOSES THE FADE-WINDOW RESIDUAL. The answer is neither PR 5 nor PR 6 — it
is the row that carries this args change, which is the queue row.** And the deadline is not PR 6:
the residual is unreachable while `PhoneIntegration:Enabled` is `false`, and **no row in this
seven-PR arc turns that flag on** — PR 6 flips `GvMedia:Enabled`, which is a different key. So the real
constraint is *"before anything ever enables phone integration"*, and putting it in `PHN-1f` meets that
with room to spare. ⛔ **Do not ask PR 6 to close it.** PR 6 is a `Radio.Web` row and has no natural
place for a `Radio.Core` contract change; bolting a third guard onto the handler there would add a
defence instead of removing the need for one.

**And it belongs with the queue rather than here, on a principle rather than a convenience:** without
the queue there is nothing to wake, so in `PHN-1e` this would be a `Radio.Core` contract change
motivated by a feature that does not exist — the same mistake as adding `EventPlaybackState.Waiting`
"ready for PR 5", wearing different clothes.

**The wake itself is a STATE re-evaluation, not an edge**, and that is deliberate:

```csharp
  private void OnDuckingStateChanged(object? sender, DuckingStateChangedEventArgs e)
  {
    // … PR 4's preemption decision, unchanged except for reading e.TriggeringSourcePriority …

    // Every raise, both directions, re-evaluates whether the air is clear. An EDGE would have to be
    // right about which transitions can unblock a wait; a state re-evaluation is idempotent, cannot
    // be desynchronised by a missed raise, and — the part that matters — uses the SAME predicate that
    // decided to wait in the first place, so "blocked" has exactly one definition in this file.
    TryWakeWaitingPlayback();
  }
```

over a predicate that is the file's single definition of "blocked":

```csharp
  /// <summary>
  /// True while some event source at or above GvMedia:PreemptAtPriority is in the ducking set.
  /// </summary>
  /// <remarks>
  /// ⚠ This gives GetActiveEventsByPriority its FIRST non-test caller since it was written — which
  /// PHN-1d C-42 predicted would be the queue, and it was.
  ///
  /// ⚠ No exclusion for our own source is needed, and one is deliberately NOT written: the predicate
  /// is only ever evaluated for a playback that has not yet reached StartDuckingAsync, so the
  /// attended source is not in the set when it is asked. A guard for a state that cannot occur reads
  /// as evidence that it can. APlaybackAtPriorityEightDoesNotBlockItself pins it.
  /// </remarks>
  private bool IsBlockedByAHigherPrioritySource(int threshold) =>
    _duckingService.GetActiveEventsByPriority()
      .Any(s => _duckingService.GetPriority(s) >= threshold);
```

---

#### Q5 — The orphaned-source window. **DISSOLVED, not answered — and that is why the shape is what it is.**

`PHN-1d` §5 warns that a deferred start is *"a second entry point into the acquisition tail, reached
from an event handler rather than from `StartAsync`"*, and that a wake starting audio outside `_gate`
would reopen the window Task 5 closed — audio with no `playbackId`, which nothing can stop.

**Under the shape above there is no second entry point.** The wake does exactly one thing:

```csharp
    Volatile.Read(ref _waiter)?.TrySetResult();
```

It never touches a source, never takes `_gate`, never starts audio. The acquisition task that was
already running resumes, takes `_gate`, and runs **PR 3's tail unchanged** — `TryAdopt`, `SetPriority`,
`StartDuckingAsync`, the terminal re-check, `PlayAsync`, `PublishNonTerminal(Playing)`. Every property
`PHN-1d` Task 5 established is inherited rather than re-established, and the diff over that tail is
zero lines.

⚠ **One detail is load-bearing and would be easy to drop:**

```csharp
    var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
```

Without `RunContinuationsAsynchronously`, `TrySetResult` runs the continuation **inline on the raising
thread** — and that continuation's next act is `_gate.WaitAsync`. Stated exactly rather than
alarmingly, because the overclaim is the trap here: **it is not a deadlock today.** The live wake comes
from `AnnouncementService`'s teardown, which does not hold this service's `_gate`. What the flag buys
is that it stays not-a-deadlock: the acquisition tail *does* hold `_gate` across `StartDuckingAsync`,
so the raising thread holding the gate is one refactor away, and this is the same reasoning
`OnSourceCompleted` and the preemption dispatch are both written from.

---

#### The two things `PHN-1f` must not get wrong

1. ⛔ **`EventPlaybackState.Waiting` goes at the END of the enum** (value 6), never in the middle. The
   shipped comment on `EventPlaybackRejection.MediaIdHasIllegalCharacter` records why: these names
   reach log lines and the wire, and inserting into the middle is how one quietly stops meaning what it
   used to. ⭐ **`PHN-1e` makes this safer than it was** — after C-47 the wire carries the *name*, not
   the number, and `EventPlaybackSnapshotDto.IsLive` treats an unrecognised state as live, so a
   `Radio.Web` build predating this member renders it as "something is happening, offer Stop" rather
   than failing to parse. Task 10c pins that in advance.
2. ⛔ **`APlaybackStartedUnderAHigherPrioritySourceStillMixes_TODAY` is UPDATED, never deleted.** It is
   the one test in that file written to be changed, and it says so. It becomes
   `APlaybackStartedUnderAHigherPrioritySourceWaitsAndThenPlays`: `Waiting` first, then `Playing` after
   the blocker's `Ended` raise, with `ducking.ActiveEventCount == 1` at the moment audio starts — which
   is the assertion that actually shows the two voices no longer overlap.

#### The test matrix `PHN-1f` owes

| Test | Asserts |
|---|---|
| `APlaybackStartedUnderAHigherPrioritySourceWaitsAndThenPlays` | The rewritten characterization test above |
| `AWaitingPlaybackIsReportedByCurrent` | `Current.State == Waiting`, so `GET /current` re-attaches to it |
| `StopAsyncResolvesAWaitingPlayback_AndDisposesWhatItAcquired` | Q2: `Stopped` published, source disposed exactly once, `PlayCalls == 0` |
| `ASecondStartReplacesAWaitingPlayback` | Q1, through `StartAsync`'s existing arm |
| `AWaitingPlaybackExpiresAsFailedWaitExpired` | Q3, on `FakeTimeProvider.Advance`, never on a delay |
| ⭐ `AHigherPrioritySourceEndingWhileALowerOneContinuesStillWakesTheQueue` | **Q4 — the starvation case, which is the whole reason for the args change.** It must fail if the `Ended` raise is reverted to fire only when the set empties |
| `AWaitingPlaybackIsNotWokenByASubThresholdSourceEnding` | The predicate is a state check, not "any raise wakes" |
| `APlaybackAtPriorityEightDoesNotBlockItself` | Q4's no-exclusion-needed claim |
| `TheWakeDoesNotStartAudioOnTheRaisingThread` | Q5: `FakeDuckingService`'s raising-thread instrument (already built, `:2007-2011`) shows `PlayAsync` did not happen inline |
| `AnEndedRaiseCarriesThePriorityTheSourceHadBeforeItWasRemoved` | In `DuckingServiceTests`, against the real service. The capture, directly |
| `AnEndedRaiseWithOtherSourcesStillActiveReportsIsDuckingTrue` | The hazard that made this look unsafe — `AudioManager` must not restore the radio mid-announcement |

### 5.2 To PR 6 (`PHN-2` — retire the `<audio>` element)

- **The chip reads `AudioStateStore.EventPlayback` and subscribes to `EventPlaybackChanged`.** Both
  exist after this row; the store is seeded on the first circuit open, so the chip is correct on first
  paint (ADR §8.1 ⟨A1·4⟩ item 2) without a fetch of its own.
- **Render an unrecognised `state` as "something is happening, offer Stop"**, not as nothing. That is
  what lets `PHN-1f`'s `Waiting` arrive without a lockstep deploy, and §0.6 item 3 is the requirement.
- **Interpolate the progress bar locally from the anchor** (§0.6 item 1). There is no position tick and
  PR 6 must not add one.
- **`TTSEventSource.Position` is PR 6's** (`PHN-1c` C-27, §0.5 item 6): a three-line override mirroring
  `AudioFileEventSource`'s, and `ASpeechSnapshotReportsPositionZeroForItsWholeLife` is the test to
  update rather than delete. It is PR 6's because PR 6 is the first row with a scrubber that is
  visibly wrong.
- **If the chip needs to distinguish a user stop from a preemption from the cap, ask for it** — §0.6
  records why the field was not added speculatively, and `FUTURE-WORK.md` carries the request.
- **Carry the preemption listening test** (`PHN-1d` §2.2 item 1) and **all of §2.2 above**, especially
  items 3 and 4: the backstop's real latency, and that a refresh does not stop playback.
- ⚠ **A `MediaNotFound` during UAT is as likely to be the GV blackout as a bad id** (`PHN-1c` C-22).

### 5.3 To the owner — one decision and one thing to know

1. **The split (§0.2).** `PHN-1f` is a new row and the arc becomes eight PRs. Nothing slips past PR 6:
   `PHN-1f` sits between this row and PR 6, and §6 proposes both rows together so the sequence is
   visible rather than promised.
2. **Attended playback now stops when the console goes to sleep**, including the idle timer. That is
   ADR §7.5 and it closes ADR §14 **Q8** in the direction it called safe. If the sleep surface later
   grows its own stop control, the rule can be revisited — that is the sleep arc's call, jointly with
   the Designer, and `FUTURE-WORK.md` does **not** carry it because §7.5 already decided it.

---

## 6. Proposed `BUILDER_QUEUE` rows

✅ **FILED 2026-09-04 — and the copies that were here are deleted, with this pointer left in their
place.** Both rows are now in [`docs/BUILDER_QUEUE.md`](../../docs/BUILDER_QUEUE.md) § *Queue*, and both
ordering notes are in that file's § *Dependency / ordering notes*. **`PHN-1e` is 📋 claimable; `PHN-1f`
is 🔒 and has no plan** — this document's §5 is its design input, not its plan.

The reason this section is a pointer rather than a copy is the rule the `PHN-1d` filing pass wrote into the
queue's own banner: **two copies of a queue row is how a queue row drifts.** The row that ships is the one
in the queue; if the two ever disagree, the queue wins and this section is stale by construction.

⚠ **The header note, §0.2, §0.5 and §5.3 each say** *"§6 proposes two rows"* — read that now as *"§6 names two rows, both
filed"*. The count and the sequencing those passages describe are unchanged; only where the rows live has.

*What stood here, for the record:* the ⛔ **Not filed** note explaining that `docs/BUILDER_QUEUE.md` was
owned by another pass while this plan was written — *"a concurrent edit to it has already cost this project
once today"* — followed by the two ready-to-paste rows and the two ordering notes. The filing pass changed
three things and nothing else: `PHN-1e`'s plan link took the house's full-path form, its *Depends on* cell
names the merge (#558, `a8656c71`) and says **claimable now** in those words, and `PHN-1f`'s item text leads
with the ⛔ **no plan yet** gate instead of ending with it — because a claimer reads the front of a cell.
