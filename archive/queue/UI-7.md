# `UI-7` — the multicast-await shape survives in ~17 more places, and `AudioStateHubService` is the one that matters

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2.** Filed 2026-09-07 by the `UI-6` Builder, which fixed three instances of this shape and
**deliberately left the rest alone** rather than let a 0.5 d row grow into a sweep. The count and the
sites are its measurement, not an estimate.

## The shape

`UI-6` ([#596](https://github.com/mmackelprang/RTest/pull/596)) fixed `AudioStateStore`: a multicast
`Func<Task>` invoked with `await handler.Invoke()`, where **`Delegate.Invoke` runs every handler but
returns only the last one's `Task`**. Every earlier subscriber runs to its first `await` and its
continuation is never observed, so a `try`/`catch` around the invoke protects exactly one of N. And a
subscriber that throws **synchronously**, before its first `await`, propagates straight out of
`Invoke` — so every handler registered after it never runs. That is starvation, not a lost log line.

The fix is to iterate `GetInvocationList()`, await each, and catch per subscriber.

## Where it still lives

| Site | Count |
|---|---|
| **`AudioStateHubService`** | **14** |
| `RadioPanelToggleService.cs:64` | 1 |
| `DeviceDisplayStateService.cs:22` | 1 |
| `PhoneUnreadState.cs:23` | 1 |

## Why `AudioStateHubService` is the one worth a row

It carries 14 of the ~17, and it is **the service `AudioStateStore` subscribes to** — so it sits one
layer upstream of the defect `UI-6` just fixed.

⚠ **Its saving grace today is an invariant nothing enforces.** `AudioStateStore` is its only
subscriber, so "N subscribers, one awaited" is currently "1 subscriber, one awaited" and the bug is
dormant. **Nothing in the code prevents a second subscriber being added**, and the moment one is, the
same silent-exception and starvation behaviour appears — in the service that feeds every piece of
audio state to the UI.

This is the failure mode `CLAUDE.md` § *Pre-Merge Review* keeps naming: not a defect you can observe
today, but a claim ("this is safe") whose reason ("there is only one subscriber") is true by accident
and unguarded.

## Scope questions for the plan

1. **Fix `AudioStateHubService`'s 14 sites**, or make a single subscriber structurally enforced?
   The second is cheaper and might be the honest answer — but it must then *say* it is a constraint,
   not leave the reason implicit as it is now.
2. **The three singletons** (`RadioPanelToggleService`, `DeviceDisplayStateService`,
   `PhoneUnreadState`) are one line each. Worth folding in, or a separate cleanup? Decide
   deliberately; `UI-6` grew from one site to three plus a hub sweep once the shape was visible.
3. **Is a lint the right closer?** `UI-6` fixed three sites and this row lists ~17 more; a fourth
   discovery would suggest the class needs a detector rather than another row. `LogSafetyLintTests`
   is the in-repo precedent for a source-text rule of this kind.
4. ⚠ **Enumerate subscribers by every mechanism, not by grep.** `UI-6` found six: `+=`/`-=` across
   `.cs` *and* `.razor`; **DI activation** (`Program.cs:461` container-activates
   `ConsolePlaybackState`, whose *constructor* subscribes at `:50`, with no `+=` anywhere near the
   registration); rendered components creating handlers implicitly; **target-typed `new(...)`**,
   invisible to a `new AudioStateStore(` grep; raise paths with no nearby subscription; and name
   collisions, since `AudioStateHubService` has an identically-named event set and `Sleep.razor`
   injects it as a variable literally called `AudioState`. A constructor-shaped grep is structurally
   blind to most of that — `GV-6` compiled clean at 47/0 on exactly that mistake and then failed 15
   tests at runtime.

## Verification

Unit-testable without hardware, and `UI-6` set the bar: **mutation-check the headline test.** Revert
the fix and confirm the new test fails, and that it is the one that fails. `UI-6`'s reverted loop
failed 7 of 11 and nothing else; a narrower mutation restoring one site's original shape failed
exactly 3.

⚠ `CLAUDE.md` § *Test Timing*: multicast-ordering tests are an easy place to race a wall clock
accidentally. Synchronize on the observation.

## Correction inherited from `UI-6`

`UI-6`'s dossier cites `DuckingService.cs:481-483` as the precedent for this shape being a known,
accepted limitation. **That anchor is stale — the real precedent is `:550-552`.** Use the corrected
one; the argument it carries is still sound.

---

## ⭐ OWNER DECISION, 2026-09-08 — one mandatory fan-out seam, plus the lint. §1.1 is DISCHARGED.

**The row is unblocked and buildable.** The originally-approved approach — *"enforce the single
subscriber"* — **cannot be built**, because the census (confirmed three times independently) found
**ten production subscriber types**, with **eight rendered components subscribing per circuit**. There
is no single subscriber to enforce, and both mechanisms proposed for enforcing one fail:

- **throw at registration** throws inside `MainLayout.OnInitializedAsync` on the very first circuit,
  ⚠ **with every deploy gate still green** — including the kiosk connection check, because a circuit
  that connects and *then* faults has still established a connection;
- **a plain single delegate** is worse: it silently *replaces* the first subscriber, so `MainLayout`
  would evict `AudioStateStore` from its own events with no error anywhere.

### What ships instead

Route **all 15 raise sites** through **two private `NotifyAsync` helpers** that iterate
`GetInvocationList()`, `await` each handler, and `catch` per subscriber — the same shape `UI-6`
already shipped and proved at `AudioStateStore.cs:444-494`. **A lint makes the seam mandatory**, so
raise site 16 cannot reintroduce the shape. That is what turned this from a fixed bug into a closed
class.

### Carried into the build, from the plan

- **15 sites, not 14.** `SourceChanged` is raised twice, and `:432` sits ~140 lines from the others.
- **`PhoneUnreadState.cs:23` is NOT this bug** — it is `event Action<int>`, synchronous, and must not
  be swept in.
- **Two of the "three singletons" are `AddScoped`.** Check the registration, not the row.
- ⛔ **Do not fix `SystemConfigPage.razor`'s handler leak here** — that is `UI-9`, deliberately kept
  separate so it is not held up by this row's decision.
- ⚠ **`C-213` — the test harness contains the defect under test.** `SleepTests.cs:503-511` and five
  siblings fire hub events with `await del.Invoke(dto)` on a reflected backing field, which is correct
  at one subscriber and awaits only the last at two. **A naive test would therefore pass against an
  unfixed implementation.** Fix the harness before writing the assertion, and prove the test is RED
  against `main` first.

---

## Merged — 2026-09-08, [#620](https://github.com/mmackelprang/RTest/pull/620)

⚠ **Merged, not shipped.** The appliance was deployed to `e740b94` earlier the same day; this row is
**not** in that build. The plan's §4.5 live check (panel paints; a source switch updates both
`NowPlayingPanel` and the topbar, which are two different `SourceChanged` subscribers) is **deferred
and was not performed**.

**What shipped:** all **15** raise sites through two private `NotifyAsync` helpers that walk
`GetInvocationList()`, await each subscriber and catch per subscriber; the same loop inline in
`RadioPanelToggleService` and `DeviceDisplayStateService`; and `AsyncEventFanOutLintTests` making the
seam mandatory. The lint reported **17 violations before, 0 after**.

**The `C-213` harness fix came first, and it is what makes the green run mean anything.** Twelve sites
across five test files — not the six this dossier named, nor the eight the plan's anchor list implied;
nine on `AudioStateHubService` (two share one field lookup) plus three on `AudioVisualizationHubService`,
which no row mentioned — fired hub events with `await del.Invoke(dto)` on a reflected backing field.
Repaired into `HubEventFire` before the assertion depending on them was written. The headline tests
additionally drive the real production method (`NotifySourceChangedAsync`), so they do not rest on the
repair. **Shown RED against the unfixed implementation first: 4 failed / 3 passed, the four being
exactly the discriminating ones. After the fix: 7/7.**

### Three corrections to the plan, all measured rather than argued

1. ⭐ **The plan's §4.1 headline test design would not have discriminated.** It specified three
   subscribers each completing after `await Task.Yield()`. `UI-6`'s `AudioStateStoreNotifyTests`
   already records why that fails — on unfixed code the discarded continuations usually complete
   anyway, so it *"would have passed against the bug most of the time"*. The gate /
   `TaskCompletionSource` pattern was used instead. **The sixth instrument-of-this-family failure in
   three days, and the first where the vacuous instrument was specified by the plan being executed.**
2. **The predicted 17 violations first measured 18.** The extra was `ConsolePlaybackState.cs:39` — a
   `///` line correctly *describing* another class's defect, in a file declaring its own event named
   `Changed`. Fixed by blanking comments and string literals (offsets preserved) rather than by
   exempting the file: a lint whose first casualty is the documentation of the defect it forbids is
   worse than no lint.
3. **The direct-call arm predicted to false-positive produced zero hits** against the unfixed tree.
   Kept rather than deleted; the measurement replaces the prediction.

### One process failure, recorded because it cost real time

Another session checked out its own branch **in the shared working tree between two of this cycle's
commits**. A deliberately-RED lint commit landed on `docs/owner-input-ducking-tts` and reached `main`
through [#617](https://github.com/mmackelprang/RTest/pull/617)'s squash, leaving `main`'s suite red
until this merge. The cycle was finished in an isolated worktree outside the repository. **Two agents
sharing one checkout is not safe** — and note the worktree must live *outside* the repo, since
`RepositoryRoot.Find()` prefers the outermost non-worktree checkout and a nested one would make both
lints scan the wrong tree.
