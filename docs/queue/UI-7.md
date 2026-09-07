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
