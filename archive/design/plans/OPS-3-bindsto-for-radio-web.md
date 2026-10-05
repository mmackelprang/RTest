# PLAN — `OPS-3` · `BindsTo=` **+ `Upholds=`** for `radio-web` — two lines of unit file, and the rollout that earns it

> **Row:** `OPS-3`, [`docs/queue/OPS-3.md`](../../docs/queue/OPS-3.md). Filed 2026-08-10 out of PR
> [#467](https://github.com/mmackelprang/RTest/pull/467)'s own pre-merge review.
> **Branch:** `fix/systemd-bindsto-radio-web`
> **Estimate:** **0.75 d** of build — of which **~2 h is a systemd rehearsal that is not optional** — **plus
> a ~30 min supervised session at the appliance**. §0.8 derives both.
> **Planned against** `main` at **`278beefa`**; **re-scoped against `084a6bbd`**, at which every line number
> below was re-read and none had moved (`git log 278beefa..084a6bbd -- deploy/` is empty).
> **Nothing on the box was touched while planning this.** No SSH, no `systemctl`, no deploy. Every claim
> below comes from the repository, from `git`, from systemd's own manual pages quoted verbatim, or from the
> 2026-09-08 container rehearsal. §9.2 lists what is still none of those.

---

> ## ⭐ AMENDED 2026-09-08 — the plan's open question was measured, and the answer disproved the row
>
> §0.4.4 named one unmeasured behaviour as merge-gating. It was rehearsed on 2026-09-08 (full measurements:
> [`docs/queue/OPS-3.md`](../../docs/queue/OPS-3.md) § *Rehearsal result*) and **both halves of the concern
> were confirmed**: `BindsTo=` propagation fires during `radio-api`'s ordinary 10 s `RestartSec` back-off,
> and the console never returns. **`BindsTo=` alone must not ship** — it makes the appliance worse in its
> most common failure mode.
>
> **The row is re-scoped, not cancelled.** The measured working shape is `BindsTo=radio-api.service` on
> `radio-web` **plus `Upholds=radio-web.service` on `radio-api`**, which gives joint failure *and* the return
> direction `BindsTo=` structurally lacks.
>
> **What that changes here:** two unit files instead of one (§1.1, §2), four heredocs instead of two
> (`C-198` widened), §0.4.4 becomes a recorded result rather than a question, and **three new open questions
> that `Upholds=` creates and the rehearsal did not cover** — §0.4.5, §0.5.1 and `C-199`'s version gap. The
> `systemd.unit(5)` derivation in §0.2, `C-191`, `C-192`, `C-195`, `C-197`, the no-wedge derivation in §0.5
> and §6's three-tier rollback are unchanged and still load-bearing.

---

> ## ⭐⭐ AMENDED AGAIN — 2026-09-08, second amendment: the owner decided on readiness, and one premise underneath that decision is false
>
> **Re-derived against `main` at `ec4f1490`** (was `084a6bbd`; **7 commits / 6 PRs** have landed since). ⭐ **`git log 084a6bbd..ec4f1490 -- deploy/` is EMPTY** — nothing under `deploy/` moved, and all twelve `Deploy-ToLinux.ps1` anchors, both unit files' anchors, all four `setup.sh` heredoc anchors, `DEPLOYMENT.md:30`/`:731-732`, `ORDERING-NOTES.md:28` and `HANDOFF-GA-PUNCH-LIST.md:1110` were **re-read on the working tree and every one still resolves**. §9.4 lists them.
>
> **The owner decision** (`docs/queue/OPS-3.md` § *OWNER DECISION, 2026-09-08*): **keep `BindsTo=` on web and `Upholds=` on api, AND give `radio-api` real readiness.** `C-200`'s blocking status is discharged; the row is buildable. This amendment adds the readiness work — **§0.7b** (analysis), **§1.4** (the mechanism decision), **Task 7** (the diff), **§4.11** (`S12`–`S15`), and `C-203`–`C-207`.
>
> ### The mechanism: **`ExecStartPost=`, not `Type=notify`** — and the reason is not a preference
>
> ⛔ **`Type=notify` does not deliver the stated goal, and this is measurable from the source rather than arguable.** .NET's `UseSystemd()` sends `READY=1` on **`ApplicationStarted`**, which `IHost.StartAsync` raises only after **every** `IHostedService.StartAsync` has returned. `AudioEngineInitializationService` (`src/Radio.API/Services/AudioEngineInitializationService.cs:19`) is a **raw `IHostedService`, not a `BackgroundService`**, so its `StartAsync` (`:107-187`) blocks the host on audio-engine init (`:127`, `:130`), **two** device enumerations (`:142`, `:143`), source activation (`:171`), Bluetooth pre-warm (`:176`) and BlueZ adapter bring-up (`:180`→`:458`) — with no timeout on any of them, and registered at `Program.cs:128`, near-last. **So `Type=notify` would report readiness strictly LATER than "the hub answers", by an unbounded margin gated on hardware**, against an **absent `TimeoutStartSec`** (default **90 s**) and `StartLimitBurst=5`. A slow BT adapter on a cold boot would not delay the console — it would **latch `radio-api` in `failed`**, which is the exact wedge this row is trying to stop creating.
>
> ⚠ **And it destroys `C-191`** — the property that merging and deploying are both inert on the box, which is the only reason merging this row on green gates is defensible. `Type=notify` splits the change across two rollout mechanisms with different timings, and creates a new half-installed state (`Type=notify` in the unit, a binary that never notifies) that hangs 90 s and then crash-loops. ⚠ That trap is **already latent in the tree** at `deploy/debian-x64/setup.sh:172` and `deploy/raspberry-pi/setup.sh:281`. §1.4 states what the rejected option gives up.
>
> ### ⚠⚠ `C-202`'s stated harm is WRONG, and every document in this chain repeats it
>
> This plan (§0.7), `docs/queue/OPS-3.md`, `docs/BUILDER_QUEUE.md:36`, the owner decision and the brief that commissioned this amendment all say `Upholds=` starting `radio-web` early would *"reintroduce the pre-Fix-A dead-visualization-hub bug"*, leaving the hub **"dead for the lifetime of the radio-web process"**. **It would not.** "Fix A" was **two** fixes in one commit (`c1fba27a`, #386): the deploy poll *and* a client-side background retry loop, and the second one is still on `main` and still works. §0.7b.1 has the citations. **`C-202` is a few seconds of missing visualization that self-heals — not a permanent regression.** It is re-graded, not deleted.
>
> ⭐ **The readiness work survives that correction** — §0.7b.2 gives four reasons that do not depend on the false premise, including one nothing in this chain credits it with: **at boot there is no deploy poll at all.** But the owner decided while believing the harm was permanent, and it is not. §1.4 says what that does and does not change.
>
> ### Everything else this amendment touches
>
> §0.4.6's residual-cost table (the dark window grows — §0.7b.4), §0.8 (**0.75 d → 1.25 d**, box session 30 → 45 min), §0.9, §2/§3 (a seventh task), §4.10/§4.11, §5.2/§5.3, §6.2/§6.3 (a **third** directive to install and to roll back), §8.5 (its third option already shipped), §9.2, §9.4 and §10.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`deploy/common/radio-web.service:4` says `Requires=radio-api.service`. It reads like *"the web UI cannot
outlive the API"*, and it is not that. `Requires=` propagates only a **deliberate** stop or restart of
`radio-api`; a `radio-api` that dies on its own — the exact thing it does after PR #467 gave it a working
start limiter — is not propagated. So the terminal failure of the audio service leaves the Blazor UI up,
answering on `:5002`, painted on the kiosk, with every backend call behind it dead.

The fix looked like one word. ⚠ **It is not — and finding that out is what the 2026-09-08 rehearsal
bought.** `BindsTo=` alone couples the two units in the failing direction only, and it fires on *every*
crash rather than only terminal ones, so a transient API blip that heals itself in 10 s today would leave
the console permanently dark. The fix is **two** directives in **two** unit files: `BindsTo=` on
`radio-web` for the failing direction, `Upholds=` on `radio-api` for the returning one.
**The whole of the rest of this plan is about the fact that those two words change how two production
services fail together on an appliance whose only management link is WiFi, and that no test this
repository can run will observe it.**

### 0.2 ⭐ What `BindsTo=` actually changes — from the documentation, not from the name

`systemd.unit(5)`, verbatim.

> **`Requires=`** — "If this unit gets activated, the units listed will be activated as well. If one of the
> other units fails to activate, and an ordering dependency `After=` on the failing unit is set, this unit
> will not be started. Besides, with or without specifying `After=`, this unit will be stopped (or
> restarted) if one of the other units is explicitly stopped (or restarted)."

> **`BindsTo=`** — "Configures requirement dependencies, very similar in style to `Requires=`. However,
> this dependency type is stronger: in addition to the effects of `Requires=`, which already stops (or
> restarts) the configuring unit when a listed unit is explicitly stopped (or restarted), it also does so
> when a listed unit stops unexpectedly (which includes when it fails). When used in conjunction with
> `After=` on the same unit the behaviour of `BindsTo=` is even stronger. In this case, the unit bound to
> strictly has to be in active state for this unit to also be in active state."

Three consequences, and each of them is load-bearing:

1. ⭐ **`BindsTo=` is a strict superset of `Requires=`.** The documentation says so in its own words —
   *"in addition to the effects of `Requires=`"*. **Nothing `Requires=` does today is given up.** The
   entire delta is the clause *"it also does so when a listed unit stops unexpectedly (which includes when
   it fails)"*. That is a much narrower change than "swap the dependency type" sounds like, and it is worth
   knowing before reading §0.4's list of what it costs.

2. ⭐ **`After=radio-api.service` is already present** — `radio-web.service:3`, unchanged by this row and
   deliberately left alone per the row's own instruction. So the **"even stronger"** clause applies from
   the moment this lands: *"the unit bound to strictly has to be in active state for this unit to also be
   in active state."* The coupling becomes a continuously-enforced invariant, not an edge-triggered one.
   ⚠ **This is the clause that makes §0.4.4 the plan's central open question**, because "active state" has
   a precise meaning that a service in its `Restart=` back-off window may or may not satisfy.

3. ⚠ **`BindsTo=` propagates a stop. It does not propagate a start, and it has no return direction.**
   Neither man page states what happens when the bound-to unit comes back, and the omission is not an
   oversight — there is no behaviour to document. **Measured 2026-09-08 and confirmed** (§0.4.1). This is
   the source of the largest new failure mode, and it is why the row now also carries `Upholds=`.

### 0.2b ⭐ `Upholds=` — the return direction, and why it has to be a *second* directive

`systemd.unit(5)`, verbatim:

> **`Upholds=`** — "Configures dependencies similar to `Wants=`, but as long as this unit is up, all units
> listed in `Upholds=` are started whenever found to be inactive or failed, and no job is queued for them.
> While a `Wants=` dependency on another unit has a one-time effect when this units started, a `Upholds=`
> dependency on it has a continuous effect, constantly restarting the unit if necessary."

Four consequences:

1. ⭐ **It is the return direction, and nothing else in systemd is.** *"as long as this unit is up … started
   whenever found to be inactive"* is precisely the clause `BindsTo=` lacks. `radio-api` active ⇒ systemd
   keeps putting `radio-web` back.

2. ⚠ **It lives on the OTHER unit.** `BindsTo=` goes in `radio-web.service`; `Upholds=` goes in
   `radio-api.service`. The coupling now spans two files, and §6's rollback has to move them **together** —
   a half-rollback is its own failure mode (`C-201`).

3. ✅ **It creates no ordering, so it cannot make a cycle.** `Upholds=` is *"similar to `Wants=`"*, and
   `Wants=` implies no ordering. `radio-web` keeps `After=radio-api.service`; `radio-api` gains no `After=`
   at all. There is no cycle for systemd to refuse or break.

4. ⚠⚠ **"Constantly restarting the unit if necessary" has no exception carved out for a deliberate stop**,
   and that is the source of two new open questions — §0.4.5 (can `radio-web` still be stopped on its own?)
   and §0.5.1 (do those starts consume `radio-web`'s start-limit budget?). **Neither was measured on
   2026-09-08.** Both are merge gates now.

### 0.3 The failure it fixes, concretely

Today, on `radio`, when `radio-api` fails five times inside 300 s:

| | State |
|---|---|
| `radio-api` | `failed` — start limit hit. `Restart=always` **will not** clear it; `radio-api.service:22-27` says so in its own words. |
| `radio-web` | **`active`.** Untouched. `Requires=` saw no explicit stop, so nothing propagated. |
| `:5002` | Serves HTTP 200. The Blazor circuit connects. Pages render. |
| The kiosk | Shows the console. Chrome holds established connections to `:5002`. |
| The deploy's own kiosk check | Would report **`Kiosk is live`** (`Deploy-ToLinux.ps1:584-585` counts established sockets to `:5002`). |
| Everything the UI does | Fails, one call at a time, against `ApiBaseUrl=http://localhost:5000`. |

⭐ **The defect is not that the console is broken; it is that nothing on the appliance says so.** This is
the same class `CLAUDE.md` § *Pre-Merge Review* exists for, one layer above a comment: a running service is
asserting an availability it does not have. `BindsTo=` makes the assertion stop.

The tradeoff the owner has now accepted, stated as the row states it: **a dark console is a worse-looking
failure than a live UI with a dead backend, and a more honest one.**

### 0.4 ⚠ The failure modes it CREATES — this is the section the approval was given on

⭐ **Read §0.4.1 and §0.4.4 as a pair: they were the plan's two predictions, both are now measured, and one
of them is why the row changed shape.** §0.4.5 is new and is the cost `Upholds=` brings with it.

#### 0.4.1 ⚠⚠ Under `BindsTo=` alone the console goes dark and **stays** dark — ✅ MEASURED, and it is why `Upholds=` is now in scope

`BindsTo=` propagation enqueues an explicit **stop job** against `radio-web`. Two documented facts meet
here and the meeting is the whole problem:

- `systemd.service(5)`, on `Restart=`: *"the service will not be restarted if the exit code or signal is
  specified in `RestartPreventExitStatus=` … or the service is stopped with **systemctl stop** or an
  equivalent operation."* A propagated stop job **is** an equivalent operation. So `radio-web`'s
  `Restart=always` does not fight the propagation, and does not fire afterwards.
- `BindsTo=` has no recovery direction (§0.2.3). When `radio-api` returns, **nothing starts `radio-web`.**
  The systemd directive that would do that is `Upholds=` (v249+). ⭐ **Since 2026-09-08 this row DOES add
  it** — that is the whole re-scope (§1.1). The paragraph above describes `BindsTo=` in isolation, which is
  no longer what ships; it is kept because the isolated behaviour is what the two unit-file comments have to
  explain.

So the resting state after the failure is: `radio-api` `failed`, `radio-web` `inactive`, **and it stays
that way until a person issues a start.**

✅ **Both clauses measured 2026-09-08, and the resting state is worse than "needs a human" — it is
`inactive / dead / success`.** A *clean stop*, which is exactly why `Restart=` does not act on it, and
`NRestarts=0` confirms it never tried. The prediction was right in every particular.

⛔ **That is why `BindsTo=` alone does not ship.** Paired with §0.4.4's measurement — propagation fires
during the ordinary back-off, not only on a terminal failure — the two together mean a *transient* API
crash, the case `Restart=always` exists to absorb invisibly, would darken the console permanently. The row
as originally scoped made the appliance worse in its most common failure mode.

⭐ **`Upholds=radio-web.service` on `radio-api` erases this subsection**, measured in the same run: web
returns at t=16 when the API recovers, `NRestarts=0`, `active running`. The trade the owner accepted — a
lying UI exchanged for a dark screen needing a human — turns out to be available on better terms than it
was offered: the screen goes dark *with* the API and comes back *with* it. The residual cost is not "needs
a human" but "is dark for the length of the back-off", which §0.4.6 prices.

#### 0.4.2 ⚠ The row's own recovery command — wrong under `BindsTo=` alone, **softened but not discharged** by the re-scope

`docs/queue/OPS-3.md:32` records, for the runbook:

```bash
sudo systemctl reset-failed radio-api.service && sudo systemctl start radio-api.service
```

Under `BindsTo=` **alone**, that command brings the API back and leaves the console dark, for the reason in
§0.4.1 — starting `radio-api` propagates nothing to `radio-web`. An operator would run the documented
recovery, watch `radio-api` go green, look up at a black screen, and conclude the recovery failed. Recorded
as `C-192`.

⭐ **The re-scope softens this, and it is worth being precise about how much.** Under `BindsTo=` +
`Upholds=`, `systemctl start radio-api` makes `radio-api` active, and an active `radio-api` upholds
`radio-web` — so **the row's originally-recorded command is no longer wrong.** `C-192` is not discharged,
though, for two reasons that both survive the re-scope:

1. **A latched `radio-web` still needs `reset-failed`.** `Upholds=` starts a unit *"found to be inactive or
   failed"* — but a unit whose own start limiter has tripped refuses to start at all, and `Upholds=` has no
   power over that. The `C-193` path (five explicit restarts inside 300 s) reaches exactly that state.
2. **The command names only one unit for `reset-failed`.** The whole reason to name both is so an operator
   at a dark panel does not have to work out which one latched.

The form Task 4 puts in the runbook, unchanged by the re-scope:

```bash
sudo systemctl reset-failed radio-api radio-web && sudo systemctl start radio-web
```

`start radio-web` alone is sufficient and is still the better instruction, even though `start radio-api`
now also works: it is a **single job with an ordering dependency** (`After=`) that systemd resolves
deterministically, where `start radio-api` relies on `Upholds=`'s continuous-effect machinery to notice and
act. Both end in the same place; one of them is observable in a single `systemctl` exit code.
`reset-failed` covers both units because it is a harmless no-op on a unit that is not failed.

⚠ **The new runbook line the re-scope makes necessary: tell the operator to WAIT.** Under `Upholds=` a dark
console is no longer prima-facie a fault — during `radio-api`'s back-off the console is *expected* to be
dark and to return by itself within ~15 s (measured: web back at t=16). An operator who types the recovery
command at t=5 is intervening in a recovery that was already happening. Task 4 adds *"wait ~30 s before
acting; if it is still dark, the limiter has latched and the command below is needed."*

#### 0.4.3 What it does **not** create — stated so it is not over-claimed

- **A deliberate `systemctl stop radio-api` taking `radio-web` with it is not new.** `Requires=` already
  does that (§0.2's quote: *"explicitly stopped (or restarted)"*). `deploy/DEPLOYMENT.md:731-732` already
  documents it, and it is already true. **Not a delta.**
- **`systemctl restart radio-api` restarting `radio-web` is not new either**, for the same clause. This
  matters because `deploy/provision/systemd/radio-api-restart.service:6` (`systemctl restart
  radio-api.service`, daily) exists in-tree — but `deploy/provision/README.md:102-108` records it as
  **captured, not installed**, and disabled on the live box. Checked; **not a delta** even if it were
  enabled.
- **A restart loop between the two units is not reachable.** `BindsTo=` propagation moves in one direction
  only (`radio-web` depends on `radio-api`, never the reverse), and it propagates a stop, which suppresses
  `Restart=`. There is no cycle to oscillate.
  ⚠ **Re-checked against the re-scope, because `Upholds=` does put a directive on the `radio-api` side.**
  Still no loop: `Upholds=` adds **no ordering** (§0.2b.3) and acts only downward, api → web. A `radio-web`
  that crash-loops on its own does not disturb `radio-api` — nothing propagates upward in either directive.
  ⚠ **What is new is contention, not a cycle:** `radio-web`'s own `Restart=always` and `radio-api`'s
  `Upholds=` are two independent mechanisms trying to start the same unit, and both consume its start-limit
  budget. That is §0.5.1's question, not a loop.

#### 0.4.4 ✅ ANSWERED 2026-09-08 — propagation fires during the back-off, and that is the bad answer

`radio-api` has `Restart=always` / `RestartSec=10`. An ordinary crash is therefore not terminal: the
process dies, systemd waits 10 s, and starts it again. The question this plan could not answer from
documentation was:

> **During that 10-second back-off, does `radio-api` count as "not in active state" for the purposes of
> `BindsTo=` + `After=`, and does `radio-web` therefore get stopped?**

**It does.** Rehearsed in a privileged systemd container (systemd 249, PID 1 = `systemd`) with stand-in
units carrying the production timings, failure induced by `systemctl kill --signal=SIGKILL`:

```
Requires= (control)   t=0 web=active   t=1 web=active     t=15 web=active
BindsTo=              t=0 web=active   t=1 web=inactive   t=15 web=inactive
```

That is the left-hand column of the table this section used to pose as a coin-flip — the branch that was
supposed to stop the row:

| Propagation fires on **every** auto-restart cycle ← **MEASURED** | It fires only on a **terminal** stop (`failed` / limiter trip) |
|---|---|
| A single transient API crash — the kind that self-heals in 10 s today, unnoticed — **permanently darkens the console** until a human intervenes. | The change would do exactly what the row wants and nothing more. |
| ⛔ The appliance gets materially worse. **`BindsTo=` alone must not ship.** | Not what happens. |

⚠ **Note what was wrong here, because it generalises past this row.** This plan speculated that systemd
holds a `Restart=`-ing service in the `activating (auto-restart)` sub-state rather than letting it reach
`inactive`, and that this *might* spare the bound unit. It does hold that sub-state — and it made no
difference; `BindsTo=` acted anyway. **Reasoning from sub-state names produced a confident wrong answer,
exactly as this section warned it might.** `1b42ece9` exists because someone reasoned from `Requires=`'s
name; the rehearsal exists because the same reflex nearly shipped the inverse mistake.

⭐ **The row's goal is still reachable — `Upholds=` supplies what `BindsTo=` structurally lacks:**

```
BindsTo= on radio-web  +  Upholds=radio-web.service on radio-api
t=0 web=active   t=2 web=inactive   t=16 web=active    ← both directions
web: NRestarts=0, active running
```

⚠ **Read the t=2 → t=16 gap carefully, because it is evidence about something the rehearsal never directly
tested.** `Upholds=` is documented as starting the upheld unit *"whenever found to be inactive"* — so why
was `radio-web` still inactive at t=9? Because `radio-web` `BindsTo=radio-api`, and while `radio-api` is
down that start cannot succeed. `Upholds=` was retrying and being refused. **The only thing holding it back
was `BindsTo=`'s unsatisfiability** — so when `radio-api` *is* active, nothing gates it at all. §0.4.5 and
§0.5.1 are the two consequences, and both are open.

#### 0.4.5 ⚠⚠ NEW AND UNMEASURED — under `Upholds=`, can `radio-web` still be stopped on its own?

`Upholds=` has *"a continuous effect, constantly restarting the unit if necessary"* while the upholding
unit is up, and the man page carves out no exception for an operator's deliberate stop. So:

> **With `radio-api` active, does `sudo systemctl stop radio-web` stick — or does `Upholds=` put it
> straight back?**

If it does not stick, three things here are affected, and the first is not cosmetic:

1. ⚠⚠ **`Deploy-ToLinux.ps1:170` stops `radio-web` first, while `radio-api` is still active.** That is
   exactly the shape in question, and a resurrected `radio-web` would then be running over the deploy's
   `rsync --delete` of `web/`. §0.7 re-derives the stop path; Task 5 acts on it.
2. **There would be no way to stop the web UI alone** — for debugging, for a partial restart, for anything
   — short of stopping `radio-api` too, or masking the unit. A real ergonomic cost, and one that belongs in
   the unit file rather than being rediscovered at a terminal.
3. `C-193`'s hand-testing trap gains a second face: a Builder who stops `radio-web`, sees it return, and
   files a bug.

⛔ **Not answered by the 2026-09-08 rehearsal, which only ever killed `radio-api`.** `S8` measures it and
§4.8 makes it merge-blocking. ⚠ **Do not settle it by reading systemd's source or reasoning from the
directive's name** — that is the error §0.4.4 just caught in this very plan.

#### 0.4.6 The residual cost after the re-scope, priced honestly

`Upholds=` does not make the change free. What survives it:

| Window | Console | Recovers by itself? |
|---|---|---|
| `radio-api`'s 10 s back-off after a transient crash | **dark** (today: up, and lying) | ✅ yes, ~15 s |
| Across up to 5 retries before the limiter trips | **dark**, up to ~50 s | ✅ yes, if any retry succeeds |
| `radio-api` `failed`, limiter latched | **dark** | ⛔ no — needs §0.4.2's command, or the touch icon with Task 6 |

⭐ **The middle row is the honest headline: a crash that is invisible today becomes a console that blinks
out for 10–50 s and comes back.** That is a real regression in perceived reliability, bought in exchange for
the appliance no longer asserting an availability it does not have. It is a *smaller* cost than the one the
owner accepted on 2026-09-07 — which was "dark until a human intervenes, every time" — but it is not zero,
and the PR body must say so in these words rather than presenting `Upholds=` as having made the row free.

⚠ **SUPERSEDED IN PART by the second amendment: readiness makes this table's numbers LONGER.** The console
now waits for a backend that works instead of returning to one that does not, so the first row becomes
~16–18 s typical and the second up to ~150 s worst case. **§0.7b.4 carries the replacement table**, and
`S14` plus §5.3 step B measure the real figures. ⛔ **Do not quote the 10–50 s from this table in the PR
body or in `DEPLOYMENT.md`** — it describes a shape that is not what ships.

### 0.5 ⚠ The `StartLimit*` interaction — can the pair wedge? **No, and here is the derivation**

PR #467 put `StartLimitIntervalSec=300` / `StartLimitBurst=5` in `[Unit]` on **both** units
(`radio-api.service:28-29`, `radio-web.service:23-24`). The row asks whether a binding pair plus start
limits can reach a state neither recovers from. Worked through:

1. **Propagation cannot consume `radio-web`'s start-limit budget.** A start limit counts *starts*
   (`systemd.unit(5)`: *"Units which are started more than burst times within an interval time span are not
   permitted to start any more"*). `BindsTo=` propagation issues a **stop**. A stop is not a start, and per
   §0.4.1 it also suppresses the `Restart=` that would otherwise produce one. **`radio-web`'s counter does
   not move.** This holds under *both* branches of §0.4.4 — even if propagation fires on every one of
   `radio-api`'s auto-restart cycles, each occurrence is another stop, and stops are free.

2. **So the terminal state is asymmetric, and only one half is latched.** `radio-api`: `failed`, start
   limit hit, refuses to start until cleared. `radio-web`: `inactive`, **not failed, not rate-limited,
   startable immediately**.

3. **Clearing it needs `reset-failed` on `radio-api` only** — `systemd.unit(5)`: *"**systemctl
   reset-failed** will cause the restart rate counter for a service to be flushed, which is useful if the
   administrator wants to manually start a unit and the start limit interferes with that."* §0.4.2's
   command names both units for operator-ergonomics reasons, not because `radio-web` needs it.

**Answer to the row's question: no. This change introduces no state that `reset-failed` on both units
followed by `systemctl start radio-web` does not clear**, and `S4` (§4.5) tests exactly that rather than
trusting this derivation. ⚠ **That answer covers `BindsTo=` only — see §0.5.1 for the half `Upholds=`
reopens, which `S9` measures.**

⚠ **One adjacent case that is NOT a wedge but will be mistaken for one.** Five `systemctl restart
radio-api` inside 300 s propagates five *restarts* to `radio-web` (§0.4.3) and trips **`radio-web`'s own**
limiter, after which `start radio-web` is refused with *"start request repeated too quickly"* until
`reset-failed radio-web`. **That is true today under `Requires=` and is not caused by this change** — but a
Builder hand-testing the change is very likely to restart things five times in a row, hit it, and file it
as a `BindsTo=` regression. Recorded as `C-193`. §4.6 is the control that distinguishes them.

#### 0.5.1 ⚠⚠ NEW — `Upholds=` reopens the wedge question **from the other side**, and the margin is zero

⭐ **The derivation above is unchanged and still correct — for `BindsTo=`.** Its load-bearing step is
*"`BindsTo=` propagation issues a **stop**. A stop is not a start … `radio-web`'s counter does not move."*
That survives the re-scope untouched.

⚠ **But `Upholds=` issues starts, and starts are exactly what a start limiter counts.** The premise that
made §0.5 safe does not extend to the new directive, and the arithmetic is uncomfortably tight:

| | |
|---|---|
| `radio-api` may cycle | **5** times in 300 s before its own limiter latches (`StartLimitBurst=5`) |
| Each `radio-api` recovery upholds `radio-web` back to active | **1 start** of `radio-web` per cycle |
| `radio-web`'s own budget | **5** starts per 300 s — *the same size* |

So a flapping `radio-api` drives `radio-web`'s start count **1:1 with its own failure count**, against an
identically-sized budget. `systemd.unit(5)` trips on *"more than burst times"*, so a fifth start is the last
one that fits — **the margin is exactly zero.** Any additional start inside the same 300 s window (the
deploy's own `start radio-web`, one manual restart, a `radio-console-open` tap) pushes `radio-web` over its
limiter and into `failed`.

**Why that matters more than it first looks:** a `radio-web` in `failed` cannot be rescued by `Upholds=` —
a rate-limited unit refuses to start, and `Upholds=` has no power over the limiter. The console would then
stay dark *even after `radio-api` recovered*, which is precisely the failure the re-scope was adopted to
eliminate, reached by a different route.

⛔ **Unmeasured.** The 2026-09-08 rehearsal ran a single crash/recover cycle (`NRestarts=0`), which cannot
see this. `S9` drives five and reads both limiters. ⚠ **Do not read the recorded `NRestarts=0` as evidence
of no-wedge under the new shape** — it is evidence about one cycle, and the question is about five.

**If `S9` shows `radio-web` latching:** the fix is not to delete `Upholds=` but to widen `radio-web`'s
`StartLimitIntervalSec` / `StartLimitBurst` so its budget exceeds `radio-api`'s failure count — a change to
values PR #467 chose deliberately, which makes it the owner's call and not a Builder's. Take the result to
him rather than adjusting the numbers.

### 0.6 ⚠⚠ The change does not reach the box on a deploy. Merging it is inert.

**`Deploy-ToLinux.ps1` never installs unit files.** Verified by reading the whole script: it publishes and
rsyncs `api/` and `web/`, seeds `appsettings.Production.json` (`:314-353`), syncs five WirePlumber Lua
rules (`:373-425`), and stops/starts the two units — and the only `.service` strings anywhere in the file
are the *kiosk* transient unit at `:579-591`. Nothing copies `deploy/common/*.service` to
`/etc/systemd/system/`.

The main units are installed in exactly one place per target:

| Installer | What it does | When it runs |
|---|---|---|
| `deploy/debian-x64/setup.sh:200-203` | `cp deploy/common/radio-web.service /etc/systemd/system/` | one-time provisioning, by hand |
| `deploy/raspberry-pi/setup.sh:309-312` | the same, for the Pi | one-time provisioning, by hand |
| `deploy/provision/provision.sh:168-178` | installs **drop-ins only**, and only when a deployed unit predates a fold (`deploy/provision/README.md:92-100`) | provisioning |

⭐ **This is the single most reassuring fact in the row and the single most important rollout fact.**
Merging this PR changes a file in the repository and changes nothing on `radio`. The box keeps
`Requires=` until a human deliberately installs the new unit and runs `daemon-reload`. **The merge and the
behaviour change are separate events, separated by an act of intent** — which is precisely why merging on
green gates is safe here even though green gates cannot see the behaviour. Recorded as `C-191`, and §5 is
built on it.

⚠ **The corollary is the trap.** A Builder who merges, deploys, sees the console behaving normally, and
writes *"verified on the box"* has verified nothing — the box is still running the old unit. §0.11 forbids
that claim.

⚠ **The re-scope adds one thing to this section: there are now TWO units to install, and installing one
without the other is a defined bad state.** `radio-web.service` carries `BindsTo=`; `radio-api.service`
carries `Upholds=`. Install only the web unit and you have shipped the shape §0.4.4 measured and rejected —
a console that goes dark on a transient crash and never returns. Install only the api unit and you have
shipped §0.4.5's ergonomic cost with none of the benefit. **§5.2 installs both in one step and verifies
both before walking away**, and `C-201` records the half-installed state as a hazard in its own right.

### 0.7 What this does to a deploy, and to `-NoRestart`

The row asks for this specifically. Both paths were read.

**Stop, `Deploy-ToLinux.ps1:170`:**

```
sudo systemctl stop radio-web 2>/dev/null; sudo systemctl stop radio-api 2>/dev/null; …
```

⭐ **Under `BindsTo=` alone this was already safe, by accident.** `radio-web` is stopped first, so by the
time `radio-api` stops there is nothing left to propagate to. Nothing in that line says the order matters,
and the two `stop`s are separated by `;` precisely so either can fail harmlessly.

⚠⚠ **The re-scope inverts that conclusion, and this is the single most consequential thing `Upholds=`
touches in this repository.** `stop radio-web` runs **while `radio-api` is still active** — which is exactly
the state §0.4.5 asks about. If `Upholds=` reverses a deliberate stop, then:

```
sudo systemctl stop radio-web     # radio-api still active → Upholds= may start it straight back
sudo systemctl stop radio-api     # BindsTo= then stops it again
                                  # ... and in between, rsync --delete runs over web/
```

The end state is still both-stopped, so a deploy would likely *appear* to work. **The damage would be in
the window**: a resurrected `radio-web` holding open file handles under `rsync --delete`, or serving the
kiosk from a half-replaced directory. That is a race, which means it fails intermittently and blames
something else.

⛔ **Do not guess which way this goes.** `S8` measures it. Task 5 is written with both branches and a
decision rule:

| `S8` result | Task 5 |
|---|---|
| The stop **sticks** (`Upholds=` does not reverse an explicit stop) | Keep `:170` exactly as-is; add the comment explaining that the order is now load-bearing. |
| The stop is **reversed** | Change `:170` to a single `sudo systemctl stop radio-api radio-web`. One job, and systemd stops them in reverse `After=` order within the transaction, so `radio-web` still goes first — but there is no window in which `radio-api` is up while `radio-web` is down. ⚠ **This directly contradicts §0.11's standing prohibition on collapsing those two calls**, which was written for the `BindsTo=`-only world; the prohibition is amended there. |

**Either way there is no change to the deploy's *outcome*** — only to whether it has a race in the middle.

**Start, `Deploy-ToLinux.ps1:452:**

```
sudo systemctl daemon-reload && sudo systemctl start radio-api && for i in $(seq 1 20); do curl -sf … && break || sleep 0.5; done && sudo systemctl start radio-web
```

API first, health-poll, then web — already the only order `BindsTo=` permits, and `&&`-chained, so a
`radio-api` that fails to start short-circuits before `start radio-web` is reached. **No functional change
from `BindsTo=`.**
⚠ **One narrow new delta:** if `radio-api` starts and then dies *inside* the ~10 s poll window, today
`start radio-web` still succeeds and the deploy comes up serving a dead backend; after this change
`start radio-web` fails. Both paths end at the `is-active` check (`:455-458`) and `exit 1` (`:605`), so the
deploy's outcome is the same — it just reports the real reason sooner. Recorded as `C-194`.

⚠⚠ **`Upholds=` defeats that health-poll, and the poll is there for a measured reason.** The comment
immediately above the line (`:445-451`) says why it exists, verbatim:

> "systemctl returns when the process is launched (not when its listener is bound) so the previous
> `api && web` parallel-launch always lost the race on slow boxes: radio-web's SignalR client tried to
> negotiate before radio-api had opened port 5000, the initial StartAsync threw, and the visualization hub
> stayed dead for the lifetime of the radio-web process (pre-Fix A behavior)."

⭐ **`Upholds=` fires on `radio-api` becoming *active* — which is the launch moment the comment says is too
early, not the listener-bound moment the poll waits for.** So on a box with the new units,
`sudo systemctl start radio-api` at `:452` can bring `radio-web` up via `Upholds=` **before** the
`curl … /hubs/visualization/negotiate` loop has succeeded, reintroducing precisely the race "Fix A"
eliminated. The trailing `&& sudo systemctl start radio-web` would then be a no-op on an already-running
unit, and the poll would be decorative.

⚠⚠ **RE-GRADED 2026-09-08 (second amendment). The paragraph above is right about the mechanism and WRONG
about the harm, and the error propagated into four other documents.** The claim *"every deploy would
reintroduce the pre-Fix-A dead-visualization-hub bug"* rests on the comment's own words — *"the visualization
hub stayed dead for the lifetime of the radio-web process"* — being **still true of the code**. It is not.
**§0.7b.1 has the citations**: `c1fba27a` (#386) shipped the deploy poll **and** a client-side background
retry loop in the same commit, and the loop is still on `main`. `C-202`'s real cost is **seconds of missing
visualization that self-heals**, not a dead hub. The trailing clause of the comment quoted above —
*"regardless of hub-service code resilience"* — is the tell, and this plan read past it twice.

⭐ **The readiness fix is still the right change**, for four reasons that do not depend on the false premise
(§0.7b.2). But it is a *correctness and boot-ordering* fix, not the rescue of a blocking regression, and the
PR body must describe it that way. **`S10` is superseded by `S12`** (§4.11): its job changes from *sizing a
regression* to *proving the fix works*, and its expected result inverts.

⚠ **`daemon-reload` is on the *start* path, not the stop path.** That is worth knowing for §5: a deploy run
after the new unit is installed picks it up at `:452`, mid-deploy, at a moment when `radio-api` is stopped
and `radio-web` is stopped. Nothing propagates from a reload against two inactive units. Safe.

**`-NoRestart` (`:145`, `:443`, `:607-611`):** skips the stop, skips the start, and therefore skips
`daemon-reload` entirely. Binaries are replaced under two running services — already true today, unchanged
by this row. Its closing hint, `Start manually: sudo systemctl start radio-api radio-web` (`:611`), stays
correct: naming both is redundant under `BindsTo=` but not wrong. **No change needed, and none should be
made** — §8.4.

### 0.7b ⭐ NEW 2026-09-08 (second amendment) — readiness: what it fixes, what it does not, and what it costs

#### 0.7b.1 ⚠⚠ First, the correction: "Fix A" was TWO fixes, and the second one is still working

`Deploy-ToLinux.ps1:445-451`'s comment is the sole evidence behind `C-202`, behind the owner decision's
framing, and behind this plan's §0.7. Its load-bearing sentence is that a failed initial negotiate left the
visualization hub *"dead for the lifetime of the radio-web process (pre-Fix A behavior)"*.

**"Fix A" is `c1fba27a` — PR #386, *"fix(web/deploy): Radzen dark theme, viz hub startup race,
sleep-clock anti-burn-in"* — and it changed SIX files, not one.** Alongside the 20-line change to
`Deploy-ToLinux.ps1` it rewrote both hub clients:

| Evidence | Where |
|---|---|
| *"Background retry loop activated when the initial StartAsync fails (radio-api not yet listening at deploy time, network blip, etc.). The loop polls until the hub is reachable, then replays any subscriptions the UI recorded while we were offline."* | `src/Radio.Web/Services/Hub/AudioVisualizationHubService.cs:20-23` |
| The catch that arms it: *"Initial connect failed — typically because radio-api hasn't bound port 5000 yet during a fresh deploy … kick off a background retry loop and leave the connection object intact"* → `StartRetryLoop(hubUrl)` | `AudioVisualizationHubService.cs:187-193` |
| The loop itself, back-off `{2, 5, 10, 30}` s, replaying subscriptions on success | `AudioVisualizationHubService.cs:208-218` |
| The same treatment on the **audio-state** hub — *"Without this guard a failed initial StartAsync would leave `_hubConnection` non-null and every subsequent call would skip silently"* | `AudioStateHubService.cs:31-34`, `:104-110`, `:355-356`, `:390` |
| `git log -S 'StartRetryLoop'` on the visualization service returns **exactly one commit**: `c1fba27a` | — |

⭐ **So the comment describes behaviour its own commit fixed.** Its final clause even says so — *"eliminates
the race regardless of hub-service code resilience"* — which only makes sense written by someone who had
just added that resilience. This plan quoted that clause twice (§0.7, §8.5) and read past it both times.

**What `Upholds=` starting `radio-web` early actually costs today:** one `LogWarning` per hub, and no
visualization or live audio-state until the retry loop connects — typically **2 s**, worst case **30 s** on
the back-off ladder, self-healing without intervention. ⚠ **Not nothing** — the console is visibly inert
while it lasts — **but not a dead hub, and not permanent.**

⚠ **Two further corrections to the record while in this neighbourhood**, both from reading `:452` rather
than its comment:

1. **The deploy's poll is advisory, not enforcing.** `for i in $(seq 1 20); do curl … && break || sleep 0.5;
   done` **exits 0 whether or not the endpoint ever answered**, and the `&&` chain then starts `radio-web`
   regardless. So "keep the poll — belt and braces" (the owner decision's words, and this plan's) is keeping
   a **soft wait**, not a gate. That is still worth keeping, and it is not the safety net the phrase implies.
2. **`Deploy-ToLinux.ps1:459-460` already concedes the whole premise in one line:** *"Poll because the
   service takes a few seconds to bind its HTTP listener after start."* The repository has known since
   `OPS-1` that `is-active` precedes listener-bound; nothing had made the unit say so.

#### 0.7b.2 ⭐ Why readiness is still worth doing — four reasons that survive §0.7b.1

1. ⭐ **At boot there is no poll at all, and nothing in this chain has said so.** `radio-web` is
   `After=radio-api.service` and `WantedBy=multi-user.target`; on a cold boot systemd starts web as soon as
   api's start job completes, which for `Type=simple` is at exec. `Deploy-ToLinux.ps1` is not running. **The
   ordering the poll exists to enforce has never been enforced outside a deploy** — which makes readiness a
   fix for a path that is exercised on every power cycle, not just a defence of the deploy path.
   ⚠ Stated as *structurally present and unmeasured*: nobody has observed the boot race losing. It is the
   same shape the comment describes, on a path with no mitigation but the client retry loop.
2. **`Upholds=` turns a rare race into a routine one.** Today `radio-web` restarts on deploy and boot only.
   After this row it restarts on **every transient `radio-api` crash**, and each restart re-runs the initial
   connect. The retry loop absorbs it, but the console is inert for 2–30 s *on top of* the dark window each
   time. Truthful readiness removes the race rather than absorbing it.
3. **It makes `active` true for every consumer, not just this one.** `After=`, `Upholds=`, any future unit,
   and the deploy's own `is-active` check at `:455-458` all currently read a value that means "exec'd".
4. **It is the option the owner chose**, and §1.4's mechanism keeps it inside the unit file, so it costs
   nothing that `C-191` was buying.

#### 0.7b.3 ⭐ What "ready" means here — the narrowest definition that fixes the problem

> **Ready ≡ `POST /hubs/visualization/negotiate?negotiateVersion=1` returns 2xx.**
> That is Kestrel bound **and** the hub mapped **and** the endpoint answering — and it is *exactly* the
> condition `Deploy-ToLinux.ps1:452` already polls for in production.

⛔ **Ready does NOT mean "the audio engine is up".** Resist widening it, and here is the measurement that
says why. `Program.cs:182-183` map the hubs before `app.Run()` at `:218`, and Kestrel
(`GenericWebHostService`, implicit at `Program.cs:15`) is the **first** hosted service — so the hub answers
early. `AudioEngineInitializationService.StartAsync` (`:107-187`) then blocks the *host* on hardware for an
unbounded time. **A readiness definition that waited for the audio engine would delay `active` behind a BT
adapter**, which is how `Type=notify` fails (§1.4) — and `radio-web` does not need the audio engine to
serve its UI, only the HTTP/SignalR surface.

**Narrower is safer here, and it is also the only definition with production evidence behind it.**

#### 0.7b.4 ⚠ What readiness COSTS — the dark window grows, and the ceiling is bounded by the limiter

**`ExecStartPost=` runs on every start, including every `Restart=always` auto-restart.** So `radio-api`
holds `activating` for the readiness wait before going `active`, and `Upholds=` cannot return the console
until then. §0.4.6's table gains time:

| Window | Before this amendment | With readiness |
|---|---|---|
| One transient crash | dark ~15 s | dark ~**16–18 s** typical (`RestartSec=10` + a 1–3 s bind), up to **~30 s** worst case |
| Up to 5 retries | dark up to ~50 s | dark up to **~150 s** worst case |

⭐ **The typical number barely moves** — `AudioVisualizationHubService.cs:216` records the observed bind as
*"the typical case is a 1–3 second startup race"*, and the deploy's 10 s poll has been sufficient in
production since #386. The worst case is what grows, and it grows **because the console now waits for a
backend that works instead of returning to one that does not.** That is the intended trade; say it in the
PR body in those words.

⚠⚠ **The poll ceiling is bounded ABOVE by the start limiter, not by patience — this is the constraint a
Builder will otherwise miss.** A failure that occurs *after* `ExecStartPost` has run costs
`RestartSec + t_ready` per cycle, and the limiter only latches if 5 attempts land inside
`StartLimitIntervalSec=300`. Attempts fall at `0, T, 2T, 3T, 4T` where `T = 10 + t_ready`, so latching needs
`4T ≤ 300`, i.e. **`t_ready ≤ 65 s`**. Past that, `radio-api` **never comes to rest** — it crash-loops
forever and the console flaps dark/light indefinitely, which is strictly worse than the wedge #467's limiter
exists to produce.

**Chosen ceiling: 20 s** (40 × 0.5 s). Derived, not invented — it is **2× the deploy's own 10 s poll**
(`seq 1 20` × `sleep 0.5` at `:452`), which is the only figure in the tree with production standing. It sits
**3× under** the 65 s limiter bound and **4.5× under** the 90 s `DefaultTimeoutStartSec` that applies
because `TimeoutStartSec=` is absent from every unit in this repository. `S15` tests the limiter still
latches with it in place.

#### 0.7b.5 ⛔ What CANNOT be verified without the appliance — stated here, not buried

⭐ **Read this before writing any task that assumes otherwise.** The brief for this amendment asked for it
explicitly, and the honest answer is that **one number this design depends on does not exist in the
repository.**

| # | Unverifiable from the repo | Consequence of being wrong | Cheapest way to settle it |
|---|---|---|---|
| 1 | ⚠⚠ **How long `radio-api` actually takes from exec to `negotiate` answering, on this box, on a cold boot.** The 20 s ceiling is sized from the deploy's 10 s poll, and **nobody has recorded whether that poll has ever reached its 20th iteration.** | Too small: the poll gives up, `active` is asserted early, and readiness silently does nothing on exactly the slow boot it was written for. Too large: §0.7b.4's limiter bound. | ⭐ **The §5.3 box session measures it** — one number, no risk, and it is now a required step rather than an optional one. Until then the ceiling is a *defensible default*, not a measured one. |
| 2 | Whether `curl` is present on the appliance. Both `setup.sh` install it (`debian-x64:45`, `raspberry-pi:45`) and every deploy already runs it at `:452` — but **`deploy/provision/packages.sh` does NOT**, though its header comment at `:7` claims curl is covered. | `ExecStartPost` fails to exec → but it is written to exit 0, so the unit still starts. Degrades to today's behaviour, loudly. | `command -v curl` in the §5.2 install, one line, no risk. `C-206`. |
| 3 | Whether `ExecStartPost=` actually holds `ActiveState=activating` until it completes. **The entire option rests on this**, and it is the class of claim §0.4.4 caught this plan getting wrong by reasoning from names. | ⛔ **The option collapses** and `Type=notify` becomes forced despite §1.4. | ⛔ **`S12a` measures it FIRST, in the container, before anything else in §4.11.** Not derived here on purpose. |
| 4 | Whether the appliance's real startup ever exceeds the ceiling. | Readiness gives up and the boot race (§0.7b.2.1) stays unfixed — no worse than today, but the row would not have delivered. | Same measurement as #1. |

⛔ **No task in this plan may be written as though #1 has been answered.** The value ships as 20 s, the box
session records the truth, and a follow-up adjusts it if the truth disagrees. **Nothing here is a reason to
touch the box before §5.**

### 0.8 The estimate — **1.25 d**, re-priced twice on 2026-09-08, and a supervised session that is not part of it

⚠⚠ **RE-PRICED AGAIN (second amendment): 0.75 d → 1.25 d, box session 30 → 45 min.** The readiness work adds
a third directive, a seventh task, four rehearsal scenarios, and — the largest single item — a **rebuilt
fixture**, because the existing stand-in `ops3-api` binds nothing and therefore cannot express readiness at
all (§4.11).

⚠ *The first amendment's 0.5 d → 0.75 d still stands and is folded in below.* It added a second unit file,
doubled the heredoc count, and folded in the previously-optional touch-icon task.

| Work | Cost | Δ (2nd amendment) |
|---|---|---|
| Task 1 — `radio-web.service`: `BindsTo=` + comment rewrite | 0.75 h | — |
| Task 2 — `radio-api.service`: `Upholds=` + comment | 0.75 h | — |
| **Task 7 — `radio-api.service`: `ExecStartPost=` + `TimeoutStartSec=` + comment** | **0.5 h** | ⭐ **new** |
| Task 3 — **four** heredocs, **plus the two `Type=notify` corrections** | 0.5 h | +0.25 h |
| Task 4 — `deploy/DEPLOYMENT.md`: wait-before-acting, two-file rollback, **the readiness section and the "`start radio-api` now blocks" note** | 1.0 h | +0.25 h |
| Task 5 — `Deploy-ToLinux.ps1`, comment **or** stop-order change per `S8`, **plus the two comments this change makes stale (`:445-451`, `:459-460`)** | 0.5 h | +0.25 h |
| Task 6 — `radio-console-open` (`reset-failed`) | 0.5 h | — |
| §4 the systemd rehearsal — **rebuild the fixture for readiness**, re-run `S1`/`S6`/`S7`/`S8`/`S9`/`S11` as regression, run the new `S12`–`S15` | **3.5 h** | **+1.5 h** |
| PR, review, merge — three directives, a re-scope **and a corrected premise** to explain | 1.0 h | +0.25 h |
| | **9 h ≈ 1.25 d** | **+3 h** |

**Where the extra 1.5 h of rehearsal goes.** ⭐ **`S12a` is the expensive one and it is not optional** — the
whole `ExecStartPost=` option rests on `ExecStartPost` holding `ActiveState=activating` until it completes,
and §0.4.4 is this plan's own standing evidence that such a question must not be settled by reasoning from a
directive's name. The rest is fixture work: `ops3-api` gains a late-published readiness marker and the unit
gains a real `ExecStartPost=`, which means `S1`/`S6`/`S7` are re-run against a **changed** fixture rather
than a preserved one — so their re-runs stop being nearly free.

⚠ **What is not in the number, and must not be folded into it: the box session, now ~45 min.** It installs
two units carrying three directives, verifies five loaded properties, confirms the `Upholds=` return
direction on the appliance's own systemd version (`C-199`), **and now carries a measurement step that is the
only available source of a number this design depends on** (§0.7b.5 #1 — the real exec→ready duration). It
needs the owner physically present and costs a brief interruption to whatever is playing. It is his to
schedule.

📌 **`docs/HANDOFF-GA-PUNCH-LIST.md:1110` prices this row at 2–3 h.** That was right for a one-word diff and
did not price the rehearsal, which did not exist when it was written — and the rehearsal is the only reason
this row is not currently shipping a regression. The two are not in conflict; this plan says what the other
4 h buys.

### 0.9 ✅ Merge posture — approved, and what the approval does and does not cover

The row, `docs/queue/ORDERING-NOTES.md:28`, `docs/ROADMAP.md:144` and
`docs/HANDOFF-GA-PUNCH-LIST.md:1110` all carry a standing *"exempt from auto-merge-on-green"* /
*"must NOT auto-merge"* marker. **The owner reviewed and approved the merge on 2026-09-07.** §10 carries the
wording to update all four.

#### ⚠⚠ Does that approval survive the re-scope? Read this before merging.

**The approval was given for a change whose behaviour has since been disproved.** On 2026-09-07 the owner
accepted: *a dark console that needs a human, in exchange for the appliance not lying about its
availability* — on the plan's belief that propagation was terminal-only. §0.4.4 measured that belief false.
The change he approved does not exist; what exists is either worse (plain `BindsTo=`: dark on every
transient blip, forever) or different (`BindsTo=` + `Upholds=`: dark for 10–50 s, then back).

**The position this plan takes, and the Builder must state it in the PR body rather than assume it:**

- ✅ **The approval transfers to the re-scoped shape**, because the re-scope is *strictly better on the
  exact axis the owner weighed*. He accepted "needs a human every time"; he gets "self-heals in ~15 s, needs
  a human only when the limiter latches". Nobody is being given less than they agreed to.
- ⛔ **It does not transfer to plain `BindsTo=`**, which is what the row still literally says. Shipping the
  row as written would deliver materially worse behaviour than the thing that was approved. §0.4.4.
- ⚠ **It does not cover the three costs that are new since he looked** — §0.4.5 (`radio-web` may become
  unstoppable on its own), §0.5.1 (the zero-margin limiter interaction), `C-202` (the deploy health-poll
  defeated). **If `S8`, `S9` or `S10` comes back badly, the answer is not to proceed on the 2026-09-07
  approval** — it is a new tradeoff, and it goes back to him.

⭐ **A SECOND owner decision was taken on 2026-09-08** (`docs/queue/OPS-3.md` § *OWNER DECISION*): keep both
directives **and** give `radio-api` real readiness. That covers the readiness work in principle. ⚠⚠ **But it
was taken on a premise this amendment has since falsified** — that a defeated deploy poll leaves the
visualization hub *"dead for the lifetime of the radio-web process"*. §0.7b.1: the client-side retry loop
that shipped in the same commit as the poll makes the real cost **2–30 s of inert console that self-heals**.

**The position this plan takes, and the Builder must state it in the PR body rather than assume it:**

- ✅ **The decision stands.** Readiness is still the right change — §0.7b.2 gives four reasons that do not
  rely on the false premise, and one of them (**the boot path has no poll at all**) is a benefit nobody had
  credited it with. Nothing about the shape changes.
- ⚠ **What changes is the urgency, and therefore how much cost is proportionate.** `C-202` was described to
  the owner as a *blocking regression on the appliance's most frequent operation*. It is not. If he would
  rather ship `BindsTo=` + `Upholds=` now and file readiness as its own row, **that is a defensible reading
  of the corrected facts and this plan should not talk him out of it** — say so on the PR and let him
  choose. ⛔ **A Builder must not make that call.**
- ⛔ **It does not cover `Type=notify`.** The decision names it first of two candidates; §1.4 rejects it on
  evidence the decision did not have (`AudioEngineInitializationService` blocks the host on hardware, so
  `READY=1` would mean something much later and unbounded). **If a Builder is tempted back toward
  `Type=notify`, that is a new tradeoff and it goes to the owner**, because it also forfeits `C-191`.

⚠ **The reason the exemption existed has not gone away, and the approval is not a substitute for it.** It
existed because *no gate this repository can run observes what this change does* — there is no unit test
for systemd propagation, CI has no systemd, and a green suite is exactly as green with `Requires=` as with
`BindsTo=`. What the approval means is that the owner has accepted that risk **on the strength of this
plan** — a plan whose central prediction has now been measured wrong once. So:

**A Builder may:** merge the PR on green gates once §4's rehearsal has run and its
`S1`/`S6`/`S7`/`S8`/`S9`/**`S12`**/**`S13`**/**`S14`**/**`S15`** results are pasted into the PR body,
**and** `S8`, `S9`, `S12a` and `S15` all came back clean.

⛔ **`S12a` is the new hard gate and it precedes every other readiness result.** If `ExecStartPost=` does not
hold `ActiveState=activating` until it completes, the chosen mechanism does not work at all and nothing
downstream of it means anything (§0.7b.5 #3). **Run it first; stop if it fails.**

**A Builder must NOT, unattended, even with the merge approved:**

- ⛔ **Deploy to `radio`.** Not as verification, not as a smoke test, not because the change is small.
- ⛔ **Install the unit on the box**, by `cp`, by re-running `setup.sh`, or by any other route. §5 is a
  procedure for a human with the owner present.
- ⛔ **Run the §5.3 kill-drill**, or any `systemctl kill` / `stop` / `restart` against `radio-api`.
- ⛔ **Run §4's rehearsal on `radio`, or on the self-hosted CI runner** (`[self-hosted, linux, x64,
  appserver]`). It deliberately drives a service into a tripped start limiter; that belongs in a container
  nobody will miss.
- ⛔ **Merge before §4 has run.** The rehearsal is the gate, not a nice-to-have. §0.4.4 is now settled, but
  §0.4.5, §0.5.1 and `C-202` are not, and each has an answer that should stop this row.
- ⛔ **Ship plain `BindsTo=` because the row's title says so.** Measured 2026-09-08: it makes the appliance
  worse in its most common failure mode. The row is re-scoped; the title is a historical artefact.
- ⛔ **Silently adjust `radio-web`'s `StartLimit*` values** if §0.5.1 turns out badly. PR #467 chose them
  deliberately and the owner reviewed them. Take the measurement to him.
- ⛔ **Write "verified on the box" for anything.** Per §0.6 a deploy does not install the units, so a
  post-merge deploy proves the change is *absent*, not present.

⭐ **Five more, all from the readiness work (second amendment):**

- ⛔ **Do not switch `radio-api` to `Type=notify`**, do not add `Microsoft.Extensions.Hosting.Systemd`, and
  do not call `UseSystemd()`. §1.4 rejects it on measured grounds. ⚠ It is the option the owner decision
  names *first*, so this prohibition will look like a deviation — the PR body must explain it rather than
  leave it to be discovered.
- ⛔ **Do not delete or weaken `Deploy-ToLinux.ps1:452`'s poll**, even though readiness makes it redundant.
  The owner decision is explicit, and the reason is attribution: removing it in the same change would make
  any regression impossible to assign. ⚠ **And note it is a soft wait, not a gate** (§0.7b.1) — do not
  "tidy" it into one either; that is a behaviour change wearing a cleanup's clothes.
- ⛔ **Do not size the `ExecStartPost=` ceiling by feel, and do not raise it past 50 s.** §0.7b.4 derives a
  hard bound of ~65 s from `StartLimitIntervalSec` — past it `radio-api` never latches and crash-loops
  forever, which is worse than the wedge #467 exists to produce. 20 s is the shipping value.
- ⛔ **Do not make `ExecStartPost=` fail on timeout.** It must `exit 0` unconditionally (Task 7). A failing
  `ExecStartPost` fails the *start*, which converts "the API is slow" into "the API is failed" and feeds the
  limiter — the exact conversion this row is trying to avoid.
- ⛔ **Do not use `NRestarts` in any readiness measurement.** `Upholds=` issues `start`, not `restart`
  (§4.8's instrument correction), so it reads 0 either way. §4.11 uses `ActiveEnterTimestamp` and journal
  timestamps instead, **and validates that instrument against a known cycle before trusting it** — which is
  precisely the step whose absence made the earlier `NRestarts` derivation vacuous.

### 0.10 ⚠ Seventeen constraints — `C-191` … `C-207`, numbering continues from `C-190` (`AUD-15`)

⚠ **This heading said "Twelve" until the second amendment; the count is now 17.** `C-203`–`C-207` are
defined at the end of §8.2 alongside `C-199`–`C-202`, not here — a layout this plan inherited and did not
reorganise, so **grep for `` `C-2 `` rather than assuming they are all in one place.**

**`C-191` and `C-192` change the work. `C-193` and `C-197` are traps a Builder will otherwise walk into.
`C-195` is a claim in the row's own tracking documents that does not survive reading the code. `C-194`,
`C-196` and `C-198` are findings recorded so they are not rediscovered.**

⭐ **`C-199`–`C-202` are new on 2026-09-08 and all four come from the `Upholds=` re-scope.** `C-200` was one
of the two that could stop the row; `C-199` is closed; `C-201` is the rollback hazard; **`C-202` is
re-graded — its mechanism holds, its severity was overstated in four documents** (§0.7b.1).

⭐ **`C-203`–`C-207` are new on the second amendment and all five come from readiness.** `C-203` is the
partial state that looks fine and is not; `C-204` is why `Type=notify` is rejected although the owner
decision names it first; `C-205` is the ceiling's hard upper bound; `C-206` and `C-207` are recorded so they
are not rediscovered or "fixed" mid-row.

---

**`C-191` — ⚠ CHANGES THE ROLLOUT. `Deploy-ToLinux.ps1` does not install unit files, so merging this PR
does not change the box, and a deploy does not either.**

Derivation and the three real installers in §0.6. The change reaches `radio` only when a human runs
`deploy/debian-x64/setup.sh` or copies the unit deliberately, followed by `daemon-reload`. **Consequences:
(a) merging on green gates is genuinely low-risk, which is what makes the discharged exemption
defensible; (b) §5 must be an explicit written procedure rather than "it goes out with the next deploy";
(c) any "verified on the box" claim made after a deploy is false by construction.**

---

**`C-192` — ⚠ CHANGES THE WORK. The recovery command this row asks to record in the runbook is wrong for
the world this row creates.**

`docs/queue/OPS-3.md:32` gives `reset-failed radio-api && start radio-api`. Under `BindsTo=` that returns
the API and leaves `radio-web` `inactive` (§0.4.1), i.e. it leaves the console dark while looking like it
worked. Task 4 records `reset-failed radio-api radio-web && systemctl start radio-web` instead. ⭐ **The row
asked for a runbook entry and the entry it named is the one that must not be written.**

---

**`C-193` — ⚠ A TRAP FOR WHOEVER TESTS THIS BY HAND. `radio-web` can be start-limited by repeated explicit
restarts of `radio-api`, today, with no `BindsTo=` anywhere.**

`Requires=` propagates explicit restarts (§0.2's quote), `radio-web` carries `StartLimitBurst=5` /
`StartLimitIntervalSec=300` (`radio-web.service:23-24`), and five hand-restarts inside five minutes is a
completely ordinary way to test a service. The symptom — *"`start radio-web` says start request repeated
too quickly"* — looks exactly like a `BindsTo=` wedge and is not one. §4.6's control run is what tells them
apart.

---

**`C-194` — a narrow new deploy delta, benign, recorded so it is not read as a regression.**

If `radio-api` starts and then dies inside `Deploy-ToLinux.ps1:452`'s health-poll window, `start radio-web`
now fails where today it would succeed and serve a dead backend. Both paths reach the same `exit 1` at
`:605`. §0.7.

---

**`C-195` — ⚠ A CLAIM IN THE TRACKING DOCUMENTS THAT DOES NOT SURVIVE THE DOCUMENTATION.
`BindsTo=` does not let anything "recover itself"; it is a stop-propagation directive with no return
direction.**

`docs/HANDOFF-GA-PUNCH-LIST.md:1110` justifies the row as *"Correct coupling is what lets a wedged service
recover itself instead of needing SSH into the cabinet."* **The opposite is the case.** `BindsTo=`
propagates a stop, `Restart=` does not act on a propagated stop, and nothing starts the bound unit when the
dependency returns (§0.4.1) — so this change makes the appliance need a human *more* often, not less. The
row's own framing in `docs/queue/OPS-3.md:30` is the honest one and is the framing to keep: the purchase is
**an honest failure**, not a self-healing one. §10 corrects the punch-list cell.

---

**`C-196` — `deploy/DEPLOYMENT.md`'s two statements about the coupling are technically defensible today and
become unambiguously true after this change, but both name the wrong directive.**

`:30` — *"`radio-web` has `Requires=radio-api.service` — stopping the API automatically stops the Web
UI"* — and `:731-732` — *"stopping `radio-api` automatically stops `radio-web` due to the `Requires=`
dependency"*. Read narrowly (*stopping* = an explicit `systemctl stop`) both are correct under `Requires=`.
⚠ **Note that `1b42ece9` corrected exactly this claim in the unit file and did not touch `DEPLOYMENT.md`,
so the file has carried the un-caveated version for a month.** Task 4 updates both to name `BindsTo=` and
to say what it covers that `Requires=` did not.

---

**`C-197` — ⚠ THE ONE-TOUCH RECOVERY THE APPLIANCE ALREADY HAS COVERS THE FAILURE THIS ROW CREATES, EXCEPT
IN THE CASE THAT CREATES IT.**

`deploy/debian-x64/kiosk/bin/radio-console-open` — the **Radio Console** desktop icon
(`radio-console.desktop`: *"Starts anything that isn't running"*) — probes both services (`:92-93`) and
starts what is down (`:180-181`, `sudo -n systemctl start radio-api` / `radio-web`). **That is precisely
the recovery §0.4.1 makes necessary, and it is already a single touch on the panel with no keyboard.**

⛔ **But it does not run `reset-failed`.** On a `radio-api` that has tripped its start limiter,
`systemctl start` returns non-zero, `repair()`'s `else STATE[AUDIO]=failed` branch (`:236-238`) fires, and
the icon reports a red AUDIO row and gives up. **And a tripped limiter is the only way `radio-web` gets
stopped by this change in the first place.** So the affordance handles every dark console except the one
`OPS-3` produces. Task 6 is a one-line fix; ✅ **the owner approved it on 2026-09-08** (§1.2). ⭐ **The
re-scope sharpens this finding rather than retiring it:** `Upholds=` now clears every *transient* dark
console by itself, so a latched `radio-api` is the only cause left — which is exactly the one this icon
could not fix.

---

**`C-198` — the two `setup.sh` heredoc fallbacks carry `Requires=` too, and they are already badly stale.**

`deploy/debian-x64/setup.sh:205-245` and `deploy/raspberry-pi/setup.sh:314-354` each write a unit inline,
but **only if `deploy/common/radio-web.service` is missing** — which in a git checkout it never is, so
neither has run in a long time. It shows: they still say `User=radio` / `Group=radio` where the canonical
unit says `mmack` (`radio-web.service:29-30`), and they carry none of the CPU-affinity or memory-guard
blocks. Task 3 updates the dependency lines in all four anyway, because leaving stale copies of the old
dependency shape in the tree guarantees the next person greps, finds one, and concludes the change did not
land. **Their
broader staleness is out of scope and filed in §8.2.**

### 0.11 Things Builder must NOT do

- ⛔ **Everything in §0.9's list.** It is the operative one; this list does not repeat it.
- ⛔ **Do not touch `After=radio-api.service`** (`radio-web.service:3`). The row says leave it, and §0.2.2
  says why it matters that it is there — removing it would silently weaken `BindsTo=` out of the
  "even stronger" clause.
- ⭐ **`Upholds=radio-web.service` on `radio-api` is now IN SCOPE and REQUIRED.** ⚠ This inverts what this
  line said before 2026-09-08, when it read *"do not add `Upholds=`"*. The rehearsal is why. **Do not
  "simplify" the PR by dropping it** — a reader who sees `BindsTo=` and assumes `Upholds=` is redundant will
  silently restore the permanently-dark console. That reasoning is written into both unit files by Tasks 1
  and 2 precisely so it is met before the deletion, not after.
- ⛔ **Do not add `PartOf=`, or a `Requires=` on the `radio-api` side.** `Upholds=` is the measured shape;
  nothing else has been rehearsed, and §0.4.4 is the standing evidence that this pair is not safe to reason
  about from directive names.
- ⛔ **Do not "fix" §0.4.1 by removing `radio-web`'s `StartLimit*` block, or by lengthening `RestartSec`.**
  Neither touches the propagation, and #467 paid for those values. ⚠ This holds **even if `S9` shows the
  limiter interaction is tight** (§0.5.1) — that result goes to the owner, it is not a Builder's to adjust.
- ⚠ **Do not collapse `Deploy-ToLinux.ps1:170`'s two `stop` calls into one — *unless `S8` says to*.** This
  prohibition was written for the `BindsTo=`-only world. Under `Upholds=` the collapsed form may be the
  *correct* one (§0.7's decision table). ⛔ Collapsing it without `S8`'s result is still forbidden.
- ⛔ **Do not delete `deploy/provision/systemd/radio-web.service.d/10-dataprotection-home.conf`** or any
  other drop-in while in this file's neighbourhood. They are the documented fallback for a box whose main
  unit predates a fold (`deploy/provision/README.md:92-100`).
- ⛔ **Do not edit `docs/BUILDER_QUEUE.md`, `docs/queue/OPS-3.md`, `docs/queue/ORDERING-NOTES.md`,
  `docs/ROADMAP.md` or `docs/HANDOFF-GA-PUNCH-LIST.md` from this plan.** §10 carries the wording for
  whoever updates them.

---

## 1. Decision

### 1.1 The dependency form — `BindsTo=` on the web unit **and `Upholds=` on the api unit**, `After=` untouched

⚠ **This section was rewritten on 2026-09-08.** The option previously marked "Taken" is the one the
rehearsal disproved; the option previously "held in reserve" is now the plan.

| Option | Verdict |
|---|---|
| **`BindsTo=radio-api.service` on `radio-web` + `Upholds=radio-web.service` on `radio-api`**, `After=` unchanged ✅ | ⭐ **TAKEN — and it is the only shape that has been measured to do what the row asks.** Joint failure in the failing direction, automatic return in the other: `t=2 web=inactive`, `t=16 web=active`, `NRestarts=0`. |
| `BindsTo=` alone, `After=` unchanged | ⛔ **REJECTED 2026-09-08 — was the previous decision.** Measured to fire during the ordinary 10 s back-off and never return, so a transient crash permanently darkens the console. It makes the appliance worse in its most common failure mode and does the opposite of the purpose the row was filed for (`C-195`). |
| Keep `Requires=`, add a health-check watchdog that stops `radio-web` when `/api/health` fails | **Rejected.** Replaces a declarative dependency with a new moving part on a resource-constrained box, and re-implements badly what systemd already does correctly. |
| Do nothing; document the gap harder | **Rejected — that is what #467 already did.** `radio-web.service:12-22` is a good comment and it has not stopped the appliance from serving a UI whose backend is gone. |

The diff is now two lines in two files.

`deploy/common/radio-web.service:4`:

```diff
-Requires=radio-api.service
+BindsTo=radio-api.service
```

`deploy/common/radio-api.service`, a new line after `Wants=` at `:4`:

```diff
 Wants=avahi-daemon.service radio-pipewire-access.service radio-bt-setup.service
+Upholds=radio-web.service
```

⚠ **Why `Upholds=` is not optional, stated here because it is the deletion a future reader will reach
for:** `BindsTo=` has no return direction (§0.2.3, measured). `Upholds=` is the only systemd directive that
supplies one. Remove it and the appliance silently returns to the behaviour §0.4.4 measured and rejected —
a console that goes dark on any transient API blip and stays dark until someone walks over to it. **A
change that looks like a simplification, produces no error, and is only visible the next time the API
hiccups at 11 p.m.** Tasks 1 and 2 put that sentence in both unit files.

✅ **No ordering is added and no cycle is created** — `Upholds=` is *"similar to `Wants=`"*, which implies
no ordering (§0.2b.3). `radio-web` keeps `After=radio-api.service`; `radio-api` gains no `After=`.

### 1.2 The recovery affordance — ✅ **APPROVED BY THE OWNER, folded in as Task 6**

⚠ **This was a decision point until 2026-09-08; the owner took it.** It is no longer optional and no longer
carries a "delete this task if declined" branch.

`C-197` is the finding that matters most for how this lands in practice. Without it, `OPS-3`'s residual
failure mode — a latched `radio-api`, the one case `Upholds=` cannot rescue (§0.4.6's third row) — requires
SSH, on a box whose only link is WiFi, in the one situation where the audio service is already unwell. With
it, the existing **Radio Console** touch icon recovers the pair, unattended, with no keyboard, in one tap.

⭐ **The re-scope sharpens the justification rather than weakening it.** `Upholds=` now handles every
*transient* dark console by itself, so the only case left for this icon is the **latched** one — which is
precisely the case `systemctl start` alone cannot fix, because `start` on a rate-limited unit returns
non-zero and lands in `repair()`'s red-row branch. **The icon's remaining job and the icon's existing gap
are now the same case.**

⚠ **The cost, which must be named in the PR body rather than sold as free:** `reset-failed` is a blunter
instrument than `start`. It clears the limiter that #467 added *specifically* so a crash loop would stop
and be visible. **A one-tap `reset-failed` lets an operator re-arm an endlessly crashing service by tapping
again, indefinitely.** That is a real tension with #467's intent and it is being accepted, not resolved.

**What makes it acceptable:** `radio-console-open` reports each outcome on the glass, and a genuinely
broken service produces a red row every time. The affordance does not *hide* the failure — it permits a
retry that the operator can see failing. ⚠ **It does mean the limiter no longer guarantees a crash loop
comes to rest**; it guarantees only that each attempt is visible. Say that on the PR in those words.

### 1.3 Where the reasoning lives

`radio-web.service:12-22` is currently a well-written comment explaining why the change was *not* made. It
must not survive as-is — a comment saying *"switching `Requires=` to `BindsTo=` is deliberately out of
scope"* sitting above a `BindsTo=` line is exactly the class `CLAUDE.md` § *Pre-Merge Review* exists for.
Task 1 replaces it with a comment that states what the new behaviour is, what it costs, and the recovery
command, so the operator-facing fact lives next to the directive that causes it.

⭐ **The re-scope adds a second, harder requirement: `radio-api.service` must explain a directive whose
purpose is not visible from its own file.** A reader of `radio-api.service` sees `Upholds=radio-web.service`
with no local reason for it — the reason lives in the *other* unit's `BindsTo=`. That is the shape of
comment that gets deleted in a tidy-up. Task 2's comment therefore states the *consequence of removal*
("the console goes dark on any transient API crash and never returns"), not just the intent, because a
consequence is what stops a deletion. ⚠ **Neither comment may claim the pair is fully measured** — §0.4.5,
§0.5.1 and `C-202` are open at the time of writing, and a unit-file comment asserting more certainty than
the plan has is the exact defect `CLAUDE.md` § *Pre-Merge Review* enumerates three shipped examples of.

### 1.4 ⭐ NEW — the readiness mechanism: **`ExecStartPost=`**, and what rejecting `Type=notify` gives up

The owner decision names two candidates and leaves the choice open. **Taken: `ExecStartPost=`.**

| Option | Verdict |
|---|---|
| ⭐ **`ExecStartPost=` polling `POST /hubs/visualization/negotiate?negotiateVersion=1` until it answers, bounded at 20 s, always exiting 0** ✅ | **TAKEN.** It expresses *exactly* the stated readiness condition (§0.7b.3), it is the same probe `Deploy-ToLinux.ps1:452` already runs in production so the two cannot drift, it is **unit-file-only** so `C-191` survives intact, and it has an in-repo precedent — `deploy/provision/systemd/radio-api-restart.service:7`, the repository's only existing `ExecStartPost=`, written as `/usr/bin/bash -c '…'`. ⚠ Conditional on `S12a`. |
| `Type=notify` + `sd_notify` (`Microsoft.Extensions.Hosting.Systemd` / `UseSystemd()`) | ⛔ **REJECTED — and not on taste.** Three independent reasons below. |
| `radio-api-ready.target` | **Rejected.** A target does not *produce* readiness; something still has to decide when to reach it, so this is `ExecStartPost=` with an extra unit. |
| Fix the client instead (make `Radio.Web`'s SignalR resilient) | ⭐ **Already done — see §0.7b.1.** `c1fba27a` shipped it. This is why §8.5's third option is struck rather than deferred. |

#### Why `Type=notify` is rejected

1. ⛔ **It does not signal what we need it to signal, and the gap is unbounded.** .NET sends `READY=1` on
   `ApplicationStarted`, i.e. after **every** `IHostedService.StartAsync` has returned.
   `AudioEngineInitializationService` is a raw `IHostedService`
   (`src/Radio.API/Services/AudioEngineInitializationService.cs:19` — *not* a `BackgroundService`, which is
   the whole difference) and its `StartAsync` awaits, inline and in order: play-history cleanup (`:113`),
   `_audioEngine.InitializeAsync` (`:127`), `_audioEngine.StartAsync` (`:130`), output **and** input device
   enumeration (`:142`, `:143`), startup preferences including a third enumeration (`:162`→`:339`),
   `_audioManager.InitializeAsync` (`:167`), source activation (`:171`), `PreWarmBluetoothAsync` (`:176`)
   and BlueZ adapter bring-up (`:180`→`:458`). **None has a timeout.** Kestrel binds *first* (implicit at
   `Program.cs:15`), so `READY=1` would arrive strictly later than "the hub answers" by however long the
   hardware takes.
2. ⛔ **On this box that is not a delay, it is a wedge.** `TimeoutStartSec=` is **absent from every unit in
   this repository**, so the 90 s default applies. A cold boot where the BT adapter is slow exceeds it,
   systemd kills the start, `Restart=always` retries, and `StartLimitBurst=5` lands `radio-api` in `failed`
   — with `BindsTo=` now taking the console down with it. **The readiness fix would have manufactured the
   exact failure the row exists to make rarer.** Fixing this properly means moving
   `AudioEngineInitializationService` off the start path *and* setting `TimeoutStartSec=`, which is a second
   row's worth of work on the live audio path.
3. ⛔ **It forfeits `C-191`, which is the row's main safety property.** `Type=notify` puts half the change in
   the application binary, which reaches the box via `Deploy-ToLinux.ps1` on a *different schedule* from the
   unit files (which the deploy does not install at all). That creates a new half-installed state —
   `Type=notify` in the unit, a binary that never notifies — which hangs for 90 s and then crash-loops.
   ⚠ **That trap is already latent in the tree**: `deploy/debian-x64/setup.sh:172` and
   `deploy/raspberry-pi/setup.sh:281` already declare `Type=notify` for a binary with no `sd_notify`.
   Task 3 corrects them either way.
   ⚠ Adding the package is also not free: there is **no `Directory.Packages.props`** (pinning is inline in
   `src/Radio.API/Radio.API.csproj:21-36`) and `Directory.Build.props:3` sets `TreatWarningsAsErrors=true`,
   so a package that trips an advisory breaks the Release build rather than warning.

#### ⭐ What the rejected option gives up — stated, because the rejected option is what disappears from a record

- **The systemd-native answer.** `Type=notify` is how this is *supposed* to be done, and a reviewer who knows
  systemd will ask. `ExecStartPost=` is a poll: a second process, on `CPUAffinity=2 3` at `Nice=-5` with the
  rest of the service, running up to 40 `curl` invocations at 0.5 s spacing on a resource-constrained N100.
  Bounded and one-shot per start, but real.
- **A readiness signal that cannot lie.** `sd_notify` is the application asserting its own state.
  `ExecStartPost=` infers it from outside, and because it must `exit 0` on timeout (§0.11), **a `radio-api`
  that never becomes ready is still eventually reported `active`** — truthful in the common case, back to
  today's untruth in the pathological one. `S13` measures that path; the give-up is logged at warning
  (`<4>`, via `SyslogLevelPrefix=true` at `radio-api.service:96`) precisely so it is not silent.
- **One meaning of `active` for all time.** With `ExecStartPost=`, `active` means "the *visualization hub*
  answers" — not "the API is fully up". Anything that later needs a different readiness notion has to add
  its own probe rather than reuse this one.
- **Deleting the deploy poll one day.** Under `Type=notify` the poll would be provably redundant. Under
  `ExecStartPost=` it is redundant in the good case and a genuine second chance in the give-up case, so it
  should stay indefinitely — which the owner decision independently asked for.

⭐ **The deciding asymmetry:** if `ExecStartPost=` is wrong, the console is no worse than today and a warning
says so. If `Type=notify` is wrong, `radio-api` latches `failed` and — post-`BindsTo=` — takes the console
with it. **Pick the option whose failure mode is the one the row is trying to eliminate, not the one it is
trying to create.**

---

## 2. Tasks

⚠ **Re-scoped 2026-09-08.** Six tasks, not five: `Upholds=` adds a second unit file (Task 2), the heredoc
task doubles (Task 3), and what was the optional Task 5 is now the approved Task 6. **Tasks 1 and 2 must
land together or not at all** (`C-201`).

⭐ **Second amendment 2026-09-08: SEVEN tasks.** Task 7 adds the readiness directive to `radio-api.service`,
and Tasks 3, 4 and 5 each widen. **Tasks 1, 2 and 7 must land together or not at all** — `C-201` now spans
three directives across two files, and `C-203` records why `Upholds=` without readiness is the worst of the
partial states.

| | Task | File | Status |
|---|---|---|---|
| 1 | `BindsTo=` + comment | `deploy/common/radio-web.service` | amended |
| 2 | `Upholds=` + comment | `deploy/common/radio-api.service` | **new** (1st amendment) |
| **7** | **`ExecStartPost=` readiness + `TimeoutStartSec=` + comment** | `deploy/common/radio-api.service` | ⭐ **new** (2nd amendment) |
| 3 | four heredocs **+ the two `Type=notify` corrections** | both `setup.sh` | widened again |
| 4 | docs + runbook **+ the readiness section** | `deploy/DEPLOYMENT.md` | widened again |
| 5 | stop-order comment **or** change, **+ two stale comments** | `deploy/Deploy-ToLinux.ps1` | ⚠ conditional on `S8` |
| 6 | `reset-failed` before `start` | `radio-console-open` | ✅ approved, folded in |

⚠ **Task 7 is numbered last and executed second.** It edits the same file as Task 2 and the same `[Service]`
block region; §3 puts it immediately after Task 2 so the api unit is touched once, coherently. The number is
7 only because renumbering the others would break every cross-reference in this plan.

### Task 1 — the web unit, and the comment that must change with it

**File:** `deploy/common/radio-web.service`

Line 4:

```diff
-Requires=radio-api.service
+BindsTo=radio-api.service
```

Replace the comment block at `:12-22` — everything from `# Note what this deliberately does NOT do.` down
to `# production service coupling and is deliberately out of scope for this change.` — with:

```
# === Joint failure (OPS-3, 2026-09-08) — read this before changing the line above ===
# BindsTo=, not Requires=, and the difference is only visible when radio-api dies on
# its OWN. systemd.unit(5): Requires= propagates only an EXPLICIT stop or restart of
# the required unit. BindsTo= "in addition to the effects of Requires= ... also does
# so when a listed unit stops unexpectedly (which includes when it fails)".
#
# Before OPS-3 that left radio-web RUNNING, serving a Blazor UI on :5002 whose API was
# gone: pages rendered, the kiosk held connections, and nothing on the appliance said
# the console was dead. A dark screen is a worse-looking failure and an honest one.
#
# After= on line 3 is load-bearing and must stay. Same man page: with After= present,
# "the unit bound to strictly has to be in active state for this unit to also be in
# active state."
#
# ⚠⚠ THIS LINE IS HALF OF A PAIR. The other half is Upholds=radio-web.service in
# radio-api.service. DO NOT REMOVE IT AS REDUNDANT — it is what brings this unit back.
#
# Measured in a systemd container 2026-09-08 (systemd 249), stand-in units carrying
# these exact timings, failure induced with `systemctl kill --signal=SIGKILL`:
#
#   Requires= (control)   t=0 web=active   t=1 web=active     t=15 web=active
#   BindsTo= alone        t=0 web=active   t=1 web=inactive   t=15 web=inactive
#   BindsTo= + Upholds=   t=0 web=active   t=2 web=inactive   t=16 web=active
#
# Read the middle row: BindsTo= fires during radio-api's ORDINARY 10s RestartSec
# back-off, not only on a terminal failure — so a transient crash that heals itself
# today would darken this console. And it never returns: the resting state is
# `inactive / dead / success`, a CLEAN stop, which is exactly why Restart= does not
# act on it (systemd.service(5): Restart= does not fire for a unit stopped by an
# equivalent-to-`systemctl stop` operation). NRestarts=0.
#
# ⚠ WHAT THIS STILL COSTS, because an operator meets it and not a developer: the
# console goes dark for the length of radio-api's back-off — 10s for one crash, up to
# ~50s across five retries — and comes back on its own. If radio-api exhausts
# StartLimitBurst and latches in `failed`, Upholds= cannot help (a failed unit is not
# "up"), and the console stays dark until:
#
#   sudo systemctl reset-failed radio-api radio-web && sudo systemctl start radio-web
#
# `start radio-web` pulls radio-api in via this dependency; that is the point of
# naming only one unit.
#
# ⛔ Reverting means reverting BOTH units together — this line to Requires= AND the
# Upholds= line in radio-api.service. Half a rollback leaves an appliance where
# `systemctl stop radio-web` is silently undone by systemd. Do NOT instead remove the
# StartLimit* block below or lengthen RestartSec — neither affects propagation, and
# PR #467 paid for those values.
#
# Full derivation, measurements and rollback: design/plans/OPS-3-bindsto-for-radio-web.md
# === end joint failure ===
```

⚠ **The comment states the measured results and nothing beyond them.** At the time Task 1 is written,
§0.4.5 (whether a deliberate stop sticks), §0.5.1 (the limiter interaction) and `C-202` (the deploy
health-poll) are open. **Do not add a line to this comment asserting any of them.** If `S8`/`S9`/`S10`
resolve them before merge, add what was measured, in the same "here is the table, read the middle row"
style — not a claim of general safety.

⚠ **Leave `StartLimitIntervalSec=300` / `StartLimitBurst=5` (`:23-24`) exactly as they are**, and leave the
`# === Restart rate limit ===` block above them intact — this task replaces only the *second* comment
block, the one whose subject is the dependency.

---

### Task 2 — ⭐ NEW: the api unit gains the return direction

**File:** `deploy/common/radio-api.service`

Insert after `Wants=` at `:4`, so the two dependency directives sit together above the
`# === Restart rate limit ===` block:

```diff
 Wants=avahi-daemon.service radio-pipewire-access.service radio-bt-setup.service
+
+# === Console return direction (OPS-3, 2026-09-08) ===
+# This line exists because of a directive in a DIFFERENT file, which is why it looks
+# unmotivated from here. radio-web.service carries BindsTo=radio-api.service, so this
+# unit failing takes the console down with it. BindsTo= has NO return direction — it
+# propagates a stop and nothing starts the bound unit when the dependency recovers.
+# Upholds= is the only systemd directive that supplies one: systemd.unit(5) says that
+# "as long as this unit is up, all units listed in Upholds= are started whenever found
+# to be inactive or failed".
+#
+# ⛔ DO NOT DELETE THIS AS REDUNDANT. Deleting it produces no error, passes every test
+# in this repository, and silently reverts the appliance to the behaviour measured and
+# rejected on 2026-09-08: the console goes dark on ANY transient radio-api crash — the
+# kind Restart=always absorbs invisibly today — and STAYS dark until a human walks over
+# to it. Measured, systemd 249 container, production timings:
+#
+#   BindsTo= alone        t=0 web=active   t=1 web=inactive   t=15 web=inactive
+#   BindsTo= + Upholds=   t=0 web=active   t=2 web=inactive   t=16 web=active
+#
+# Upholds= implies NO ordering (it is "similar to Wants="), so this adds no After=
+# relationship and creates no cycle with radio-web's After=radio-api.service.
+#
+# ⚠ It does NOT rescue a radio-web whose own start limiter has latched, and it does
+# nothing while THIS unit is in `failed` — a failed unit is not "up". That residual
+# case is the one the Radio Console touch icon clears.
+#
+# Full derivation and rollback: design/plans/OPS-3-bindsto-for-radio-web.md
+# === end console return direction ===
+Upholds=radio-web.service
```

⚠ **Placement matters for readability, not for function** — systemd does not care where in `[Unit]` this
sits, but a reader looking for the coupling should find both halves near the top of their respective files.

⛔ **Do not add `After=`, `Wants=` or `Requires=radio-web.service` here.** `Upholds=` alone is the measured
shape. Adding an ordering dependency on `radio-web` would create the cycle §0.2b.3 currently rules out.

---

### Task 3 — the FOUR heredoc fallbacks stop disagreeing with the canonical units

**Files:** `deploy/debian-x64/setup.sh`, `deploy/raspberry-pi/setup.sh`

⚠ **Four edits, not two.** The re-scope puts a directive on `radio-api`, so that unit's heredocs join the
list. Each heredoc runs only when the corresponding `deploy/common/*.service` is absent.

**3a — the `radio-web` heredocs.** `deploy/debian-x64/setup.sh:209` and `deploy/raspberry-pi/setup.sh:318`
both read `Requires=radio-api.service`. In each, replace that line with:

```
BindsTo=radio-api.service
# OPS-3: BindsTo=, not Requires= — a radio-api that dies on its own must take the UI
# with it rather than leaving a console whose backend is gone. Pairs with
# Upholds=radio-web.service in radio-api.service, which brings this unit back; without
# that line the console never returns. Full rationale in common/radio-web.service,
# which is what actually gets installed; this heredoc is the fallback for a checkout
# that is missing it.
```

**3b — the `radio-api` heredocs.** `deploy/debian-x64/setup.sh:165` and
`deploy/raspberry-pi/setup.sh:274` both read `Wants=avahi-daemon.service`. In each, add immediately after:

```
Upholds=radio-web.service
# OPS-3: the return direction for radio-web's BindsTo=radio-api.service. Without this
# the console goes dark on any transient API crash and never comes back. Full
# rationale in common/radio-api.service.
```

**3c — ⭐ NEW (2nd amendment): the `Type=notify` lines must be corrected, and this is the one exception to
"do not modernise".** `deploy/debian-x64/setup.sh:172` and `deploy/raspberry-pi/setup.sh:281` both read
`Type=notify` for a binary that has never called `sd_notify` — confirmed by grep: `UseSystemd`,
`AddSystemd`, `Extensions.Hosting.Systemd`, `sd_notify` and `NOTIFY_SOCKET` return **zero hits across all
C# in the solution**. In each, replace with:

```
Type=simple
# OPS-3: was Type=notify, which was always wrong — Radio.API does not call sd_notify,
# so systemd would hold the start job until TimeoutStartSec (90s default) and then fail
# the unit. Readiness is expressed by ExecStartPost= in common/radio-api.service, which
# is what actually gets installed; this heredoc is the fallback for a checkout missing
# that file.
```

⚠ **Why this line and no other.** §8.2 still holds for `User=radio`, the missing affinity and memory blocks,
and the sandboxing mismatch — those are latent and belong in their own row. **`Type=notify` is different
because this row makes it actively misleading**: a plan whose central question was `Type=notify` versus
`ExecStartPost=` must not leave two files in the tree that appear to have chosen `Type=notify`. Anyone
grepping for `notify` while reviewing this PR finds them, and the wrong conclusion is one grep away.

⚠ **Do not modernise the rest of these heredocs** — they still say `User=radio` and carry none of the
affinity or memory blocks (`C-198`). Fixing that is §8.2, not this row. **The point of touching them at all
is that leaving stale copies of the old dependency shape in the tree guarantees the next person greps, finds
one, and concludes the change did not land.**

⛔ **Do not add `ExecStartPost=` to the heredocs.** They are unreachable fallbacks (`C-198`) that already
disagree with the canonical units on user, sandboxing and resource limits; adding a readiness poll to a unit
that would not otherwise start correctly buys nothing and widens the divergence Task 3 is trying to contain.

---

### Task 4 — the docs say what the coupling is, and the runbook gains the recovery that works

**File:** `deploy/DEPLOYMENT.md`

`:30`:

```diff
-`radio-web` has `Requires=radio-api.service` — stopping the API automatically stops the Web UI.
+`radio-web` has `BindsTo=radio-api.service` and `radio-api` has `Upholds=radio-web.service` (OPS-3) —
+stopping the API stops the Web UI, and so does `radio-api` **failing on its own**. Before OPS-3 only a
+deliberate stop propagated, so a `radio-api` that tripped its restart limiter left a UI running against a
+dead backend. ⚠ **The two directives are a pair and neither works alone**: `BindsTo=` takes the console
+down, `Upholds=` brings it back when the API recovers. ⚠ **A briefly dark console is now expected
+behaviour** during an API restart — see **Recovering from a tripped restart limiter** below.
```

`:731-732`:

```diff
-**Note:** When stopping, stop `radio-web` first (or let systemd handle it — stopping
-`radio-api` automatically stops `radio-web` due to the `Requires=` dependency).
+**Note:** Stopping `radio-api` automatically stops `radio-web` due to the `BindsTo=` dependency, so
+`sudo systemctl stop radio-api` is enough to bring both down. Starting is the other way round:
+`systemctl start radio-web` pulls `radio-api` in, so again one unit name is enough.
+⚠ **`radio-web` cannot be stopped on its own while `radio-api` is running** — `radio-api` has
+`Upholds=radio-web.service`, which starts it again whenever it is found inactive. Stop `radio-api`
+instead, or stop both in one command.
```

⚠ **The `Upholds=` sentence is conditional on `S8`.** It is written above as if `S8` confirms that
`Upholds=` reverses a deliberate stop (§0.4.5), which is the expected answer but is **not measured**. If
`S8` shows an explicit stop *sticks*, delete that sentence and restore the original "stop `radio-web`
first" phrasing. ⛔ **Do not ship this paragraph without `S8`'s result** — it is an operator-facing claim
about behaviour, and shipping the wrong half is the `C-196` failure mode repeating.

Add immediately after the § *Service Management* fenced block (i.e. after `:733`). ⚠ **The outer fence
here is four backticks because the inserted text contains a fenced block of its own — insert the inner
content, not the outer fence:**

````markdown
### A dark console: wait first, then recover

Since OPS-3 the console is **coupled to the API in both directions**, so a dark screen has two very
different causes and only one of them needs you.

**First, wait ~30 seconds.** `radio-api` restarts itself 10 s after a crash (`Restart=always` /
`RestartSec=10`), and `Upholds=radio-web.service` brings the console back with it. A console that goes dark
and returns within ~15 s is the coupling **working**, not failing. Up to five retries can chain, so a
transient fault can leave the screen dark for as much as ~50 s before healing.

**If it is still dark after ~30 s, the restart limiter has latched.** Both units halt after 5 failed starts
in 300 s (`StartLimitIntervalSec` / `StartLimitBurst`, PR #467) — deliberately, so a crash loop is visible
instead of silent. `Restart=always` does not clear that state, and neither does `Upholds=`: a `failed` unit
is not "up", so it upholds nothing.

```bash
sudo systemctl reset-failed radio-api radio-web && sudo systemctl start radio-web
```

`start radio-web` pulls `radio-api` in via `BindsTo=`; naming both in `reset-failed` saves the operator
working out which one latched. `systemctl start radio-api` also works — `Upholds=` brings the console back
— but `start radio-web` is one job with an ordering dependency, so it either works or tells you why.

At the appliance with no SSH: tap **Exit to Desktop**, then **Radio Console** — the launcher probes both
services, clears a latched limiter, and starts what is down. If it still reports a red **AUDIO** row,
`radio-api` is failing for a real reason: check `journalctl -u radio-api -p warning --since '-30min'`.

⚠ **A power cycle clears both limiter counters** and is the floor if nothing else is reachable.
````

⚠ **Two things in this replacement are deliberate and should not be "tidied":** the *wait first*
instruction leads, because under `Upholds=` the most likely reason for a dark console is a recovery already
in progress and the old text would have had the operator interrupt it; and the claim that
`systemctl start radio-api` works is **new since the re-scope** — it was false under `BindsTo=` alone
(`C-192`), which is why the previous version of this runbook entry warned against it.

**4b — ⭐ NEW (2nd amendment): two operator-facing consequences of readiness.**

⚠ **The first one will otherwise be reported as a hang.** Add to the § *Service Management* section, beside
the start commands:

```markdown
⚠ **`sudo systemctl start radio-api` now blocks until the API is actually serving** — normally a second or
two, up to 20 s at worst. That is `ExecStartPost=` (OPS-3) holding the start job open until
`/hubs/visualization/negotiate` answers, so that `active` means "the console has something to talk to"
rather than "the process was launched". **It is not a hang.** The same wait applies to
`systemctl restart radio-api` and to every automatic restart, which is why a dark console now takes a few
seconds longer to come back than it did before OPS-3.

If it ever *does* take the full 20 s, the API did not answer and systemd gave up waiting rather than
failing the unit — deliberately, so a slow API cannot feed the restart limiter. Look for
`readiness poll gave up` in `journalctl -u radio-api -p warning --since '-30min'`.
```

**And amend the wait-first paragraph inserted above:** its "~15 s" becomes **"~15–20 s"**, and the "wait
~30 s" threshold stays. ⛔ **Do not ship a specific number here that `S14` has not produced.** `S14` measures
the real dark window in the container and §5.3 step B measures it on the box; if either lands above ~25 s,
raise the 30 s threshold with it, because an operator told to wait 30 s who is actually facing 40 s will
conclude the recovery failed and interrupt it — which is the precise failure the wait-first instruction was
added to prevent.

⚠ **`DEPLOYMENT.md` must NOT claim that the deploy's poll is now redundant.** It is redundant in the good
case and a genuine second chance in the give-up case (§0.7b.1: it is a *soft* wait that exits 0 regardless,
not a gate). Saying "redundant" invites the next person to delete it.

---

### Task 5 — ⚠ CONDITIONAL ON `S8`: the deploy's stop order

**File:** `deploy/Deploy-ToLinux.ps1`

⛔ **Do not write this task until `S8` has run.** §0.7's decision table selects the branch; §0.4.5 explains
why the answer is not derivable from the man page.

**Branch A — `S8` shows a deliberate `stop radio-web` STICKS.** Comment only, no functional change.
Immediately above `:170`, at the end of the existing comment block:

```powershell
  # OPS-3: the stop order is radio-web THEN radio-api, and it is now load-bearing rather
  # than incidental. radio-web BindsTo=radio-api, so stopping the API first would have
  # systemd stop the web unit underneath this line. That is harmless (a no-op, not an
  # error) but it makes the deploy's own actions invisible in the journal. Do not collapse
  # these into `systemctl stop radio-api radio-web`, and do not reorder them.
```

**Branch B — `S8` shows `Upholds=` REVERSES a deliberate stop.** The current sequence has a race and must
change. Replace the two `stop` calls at `:170` with a single job:

```powershell
  # OPS-3: ONE stop job, both units named, and this must not be split back into two.
  # radio-api has Upholds=radio-web.service, so `stop radio-web` on its own is undone by
  # systemd within moments while radio-api is still up — which is exactly the state the
  # old two-call sequence created, leaving a resurrected radio-web running over the
  # `rsync --delete` of web/ below. Naming both in one job avoids the window entirely:
  # systemd stops them in reverse After= order inside the transaction, so radio-web still
  # goes down first, but radio-api is never up while radio-web is down.
  sudo systemctl stop radio-api radio-web 2>/dev/null;
```

⚠ **Branch B contradicts §0.11's standing prohibition on collapsing those calls**, which was written for
the `BindsTo=`-only world. The prohibition is amended there; taking Branch B is not a violation of it.

⛔ **Neither branch changes the start COMMAND at `:452`.** The poll stays exactly as it is (§0.11).

**5c — ⭐ NEW (2nd amendment): two comments this change makes false, and they must be fixed in this PR.**
Both are the `CLAUDE.md` § *Pre-Merge Review* defect — a comment asserting behaviour the code no longer has
— and this row is personally responsible for one of them and has just discovered the other.

**`:445-451`** — the poll's rationale. Two errors: it describes the hub as staying *"dead for the lifetime
of the radio-web process"*, which its own commit (`c1fba27a`, #386) fixed by adding the client retry loop in
`AudioVisualizationHubService.cs:187-193`; and after Task 7 the ordering it enforces is enforced by systemd
anyway. Replace the body with:

```powershell
  # Start radio-api, then radio-web. Since OPS-3 radio-api carries an ExecStartPost=
  # readiness poll, so `systemctl start radio-api` does not return until the visualization
  # hub answers — the ordering this loop was written to enforce is now enforced by systemd,
  # for boots and crash-recoveries too, not only for deploys.
  #
  # The loop below is KEPT DELIBERATELY. It is belt and braces: it costs one curl on the
  # happy path (the endpoint already answers, so it breaks on the first iteration), and it
  # is a real second chance in the case where ExecStartPost gives up at its 20s ceiling and
  # reports `active` anyway. Removing it in the same change as adding readiness would also
  # make any regression impossible to attribute.
  #
  # ⚠ Note what it is NOT: a gate. The loop exits 0 whether or not the endpoint ever
  # answered, so `start radio-web` runs regardless. It always worked that way.
  #
  # Historical note, corrected 2026-09-08: this comment used to say a failed initial
  # negotiate left the hub "dead for the lifetime of the radio-web process". That was true
  # when written and was fixed in the SAME commit (c1fba27a) by the background retry loop
  # in AudioVisualizationHubService.StartRetryLoop — back-off {2,5,10,30}s, replaying
  # subscriptions on success. AudioStateHubService got the same treatment. A hub that fails
  # its initial connect now recovers in 2-30s on its own.
```

**`:459-460`** — *"Poll because the service takes a few seconds to bind its HTTP listener after start."*
After Task 7 that is no longer why, because `is-active` no longer precedes the listener. Amend it to say the
poll now covers the give-up case and process startup of `Radio.Web` itself, not the API's bind.

⚠ **Both are comment-only edits.** ⛔ Do not let "while I'm here" turn into changing the poll, the retry
counts, or the `is-active` checks at `:455-458`.

---

### Task 6 — ✅ APPROVED: let the one-touch launcher clear a tripped limiter

**The owner approved this on 2026-09-08 (§1.2). It is no longer conditional** — the "delete this task if
declined" branch that was here has been removed.

**File:** `deploy/debian-x64/kiosk/bin/radio-console-open`

Replace `start_audio()` / `start_console()` at `:180-181`:

```bash
# OPS-3: reset-failed BEFORE start, because the state this icon most needs to clear is the
# one `systemctl start` alone cannot. Both units halt after 5 failed starts in 300s (#467)
# and Restart=always does not clear that; `start` on a latched unit returns non-zero, which
# lands in repair()'s `failed` branch and puts a red row on the glass with nothing done.
#
# Since OPS-3 the console is coupled to radio-api in both directions (BindsTo= on web,
# Upholds= on api). Upholds= already handles every TRANSIENT dark console by itself, which
# means the only case left for this icon is the LATCHED one — precisely the case `start`
# alone cannot fix. The icon's remaining job and the icon's existing gap are the same case.
#
# reset-failed on a healthy unit is a no-op, so this costs nothing on the common path.
# ⚠ It does mean an operator can re-arm an endlessly crashing service by tapping again,
# indefinitely. That is a real tension with #467, whose limiter exists so a crash loop comes
# to REST and stays visible; after this change the limiter guarantees only that each attempt
# is visible, not that the loop stops. Accepted deliberately: every attempt reports its own
# outcome on the glass, and the alternative is a dark appliance that needs SSH over WiFi.
start_audio()   { sudo -n systemctl reset-failed radio-api  >/dev/null 2>&1; sudo -n systemctl start radio-api; }
start_console() { sudo -n systemctl reset-failed radio-web  >/dev/null 2>&1; sudo -n systemctl start radio-web; }
```

⚠ **`start_phone()` at `:182` is deliberately unchanged** — `rotary-phone` is RotaryPhone's unit, not this
repository's, and nothing here has established that it carries a start limiter or that clearing it is
wanted. ⛔ Do not widen this to all three.

⚠ **The `>/dev/null 2>&1` on the `reset-failed` matters:** the function's exit status must remain the
`start`'s, because `repair()` branches on it (`:236-238`). A bare `reset-failed` as the last command would
make every start look successful.

---

### Task 7 — ⭐ NEW (2nd amendment): make `radio-api`'s `active` mean "the hub answers"

**File:** `deploy/common/radio-api.service` — the same file as Task 2, executed immediately after it.

⛔ **Do not write this task until `S12a` has run.** If `ExecStartPost=` does not hold
`ActiveState=activating` until it completes, this mechanism does not work and the row goes back to the owner
(§0.7b.5 #3, §1.4).

**7a — the directive.** Insert immediately after `ExecStart=` at `:81`, above `Restart=always` at `:82`:

```diff
 ExecStart=/opt/radio-console/api/Radio.API
+
+# === Readiness (OPS-3, 2026-09-08) — read this before deleting or shortening it ===
+# Type=simple means systemd calls this unit `active` the moment ExecStart is FORKED,
+# not when it is useful. Deploy-ToLinux.ps1:459-460 has conceded that in a comment for
+# a while ("the service takes a few seconds to bind its HTTP listener after start"),
+# and :445-451 records the race it caused. That was harmless while nothing acted on
+# `active` — but radio-api now carries Upholds=radio-web.service (above), and Upholds=
+# fires on `active`. Without the line below, systemd would start the console against an
+# API that has not opened port 5000 yet, on every crash-recovery and every boot.
+#
+# So: hold the start job open until the visualization hub actually answers. After this,
+# `active` means "POST /hubs/visualization/negotiate returns 2xx" — Kestrel bound AND
+# the hub mapped AND replying. It deliberately does NOT mean "the audio engine is up":
+# that would wait on hardware, unbounded, and radio-web needs the HTTP surface, not the
+# audio engine. The probe is character-for-character the one Deploy-ToLinux.ps1:452
+# already runs in production, so the two cannot drift apart.
+#
+# ⛔ IT MUST EXIT 0 WHATEVER HAPPENS, and the trailing `exit 0` is what guarantees it.
+# A failing ExecStartPost fails the START, which would turn "the API is slow" into "the
+# API is failed" and feed StartLimitBurst=5 — manufacturing the wedge this row exists to
+# make rarer. On timeout we log and proceed: no worse than the behaviour before OPS-3.
+#
+# ⚠ 20s (40 x 0.5s) is a CEILING, not a delay — the observed bind is 1-3s, so the usual
+# cost is a second or two. Do not raise it past ~50s: 5 start attempts must fit inside
+# StartLimitIntervalSec=300 for the limiter to latch, and at 4 x (RestartSec + ceiling)
+# a ceiling over ~65s means radio-api NEVER comes to rest and crash-loops forever, which
+# is worse than the wedge. 20s is 2x the deploy poll's own 10s, which is the only figure
+# with production standing behind it.
+#
+# ⚠ The <4> prefix makes the give-up a WARNING, via SyslogLevelPrefix=true below. An
+# unprefixed echo would land at SyslogLevel=debug and be invisible in journalctl.
+ExecStartPost=/usr/bin/bash -c 'timeout 20 bash -c "until curl -sf -o /dev/null -X POST http://localhost:5000/hubs/visualization/negotiate?negotiateVersion=1; do sleep 0.5; done" || echo "<4>OPS-3: radio-api readiness poll gave up after 20s - /hubs/visualization/negotiate did not answer. radio-web may be started by Upholds= against a hub that is not listening yet."; exit 0'
+
+# TimeoutStartSec is absent from every other unit in this repo, so all of them inherit
+# systemd's 90s default. Stated explicitly here only because the ExecStartPost= ceiling
+# above now consumes part of it and the relationship should be visible in the file rather
+# than inherited invisibly. On a stock box this is a no-op — §5.2 records the effective
+# value BEFORE installing, so a box with a lowered DefaultTimeoutStartSec is caught.
+TimeoutStartSec=90
+# === end readiness ===
 Restart=always
```

⚠ **Four details in that one line are load-bearing and must not be "simplified":**

1. **`/usr/bin/bash -c` with an absolute path on the interpreter and bare names inside.** That is the
   established idiom in this tree — `deploy/provision/systemd/radio-api-restart.service:7`, the only other
   `ExecStartPost=` in the repository, is written exactly that way. ⚠ Note `radio-performance.service:7`
   uses `/bin/bash`; both work under usrmerge, and `C-207` records the inconsistency rather than fixing it
   here.
2. **No `$` anywhere.** systemd expands `$VAR` and `${VAR}` in `Exec*` lines before the shell ever sees
   them, so a loop counter would be substituted away to an empty string. `timeout` + `until` avoids shell
   variables entirely; this is why the obvious `for i in $(seq …)` shape is *not* used, even though `:452`
   uses it — `:452` runs through `ssh`, not through systemd.
3. **`timeout … || echo` then `exit 0`**, in that order. `timeout` returns 124 on expiry, which fires the
   `echo`; the unconditional `exit 0` then discards every exit status. Reordering these makes the unit
   failable.
4. **`ExecStartPost=` inherits `User=mmack` (`:35`), `CPUAffinity=2 3` (`:42`) and `Nice=-5` (`:47`)** — it
   runs on the audio cores at elevated priority. Acceptable because it is bounded and one-shot per start;
   named here so nobody discovers it while chasing an audio glitch.

**7b — do NOT touch these while in the file:** `Type=simple` (`:32`) stays; `Restart=always` /
`RestartSec=10` (`:82-83`) stay; the `StartLimit*` block (`:28-29`) stays. ⛔ **And do not add
`WatchdogSec=`** — it is absent from every unit here, it needs `sd_notify` keepalives the application does
not send, and a `WatchdogSec` with no keepalive kills the service on a timer.

**7c — one stale comment to fix while adjacent.** `deploy/common/radio-web.service:105` says the
`ReadWritePaths` idiom *"matches radio-api.service line 81"*. `radio-api.service:81` is `ExecStart=`; the
`ReadWritePaths=` it means is at `:126`. Task 7 inserts directly at `:81`, so leaving it drifts it further.
Correct the number. ⚠ **Nothing else in `radio-web.service` beyond Task 1's block** — this is a one-token
fix to a comment this task is personally responsible for invalidating.

---

## 3. Ordering

**§4's rehearsal runs FIRST, before any file is edited.** That was true before 2026-09-08 and it is the
reason this plan is not currently shipping a regression — `S1` falsified the row's original scope after the
plan was written and before anything was built. Two of its scenarios are still open questions with
row-stopping answers (`S8`, `S9`), and one more (`S10`) can force a follow-up row.

⭐ **Second amendment: `S12a` runs before everything, including the rest of §4.** It decides whether the
chosen readiness mechanism exists at all (§0.7b.5 #3). A failed `S12a` stops the row here.

Then: Task 1 → Task 2 → **Task 7** → Task 3 → Task 4 → Task 5 → Task 6.

⚠ **Tasks 1, 2 and 7 are ONE unit of work, not three adjacent ones.** ⭐ Widened by the second amendment
from two to three. Landing a subset produces a defined bad state:

| Shipped | Result |
|---|---|
| Task 1 only (`BindsTo=`) | The shape §0.4.4 measured and rejected — dark on any transient crash, never returns. |
| Task 2 only (`Upholds=`) | Cost with no benefit; `radio-web` may become unstoppable while api runs (§0.4.5). |
| ⚠⚠ Tasks 1 + 2, **no Task 7** | The console returns on every crash — **against an API that may not be listening yet.** The client retry loop absorbs it (§0.7b.1), so it looks fine and is inert for 2–30 s each time. `C-203`. |
| Task 7 only | Harmless and pointless: `active` becomes truthful, nothing consumes it. |

**Do not split them across commits in a way that leaves an intermediate commit shipping any of those.**

⚠ **Task 5 cannot be written until `S8` has run** — it has two mutually exclusive branches and the
rehearsal picks one. Task 4's `DEPLOYMENT.md` stop-note has the same dependency. ⚠ **Task 7 cannot be
written until `S12a` has run**, for the stronger reason that a failed `S12a` means it cannot be written at
all.

---

## 4. Test plan — and the honest statement that CI cannot provide it

### 4.1 ⛔ What the existing gates prove about this change: nothing

- **No test in this repository reads a `.service` file.** Checked across all ten test projects: the only
  match for `radio-web.service` anywhere under `tests/` is a doc comment in
  `Radio.Web.Tests/Configuration/DataProtectionSetupTests.cs:12`, which mentions `ProtectHome=` in prose
  and asserts nothing about the unit.
- **CI has no systemd to observe.** `.github/workflows/build.yml` builds and runs xUnit. Unit propagation
  is not a thing a .NET test can reach.
- ⭐ **Therefore: a fully green suite is exactly as green with `Requires=` as with `BindsTo=`.** The gates
  prove the repository still builds. They prove nothing whatsoever about this row, and the PR body must say
  so in those words rather than listing a green run as if it were evidence.

**The real gate is §4.3–§4.9: a rehearsal against a real systemd, in a disposable instance.**

✅ **On 2026-09-08 it provided exactly that, and it earned its cost on the first run** — `S1` disproved the
row's central assumption before a line of it was built. The scenarios below are therefore split into two
kinds, and they are not interchangeable:

| | |
|---|---|
| **Recorded** — `S1`, `S6`, `S7` | Already measured. Re-run as regression, to confirm the fixture still behaves and the shape still holds. Their results are known and are quoted in this plan. |
| **Open** — `S8`, `S9`, `S10` | ⚠ **Never measured.** Each has an answer that changes the work, and two of them can stop the row. These are what the 2 h in §0.8 is actually buying now. |

### 4.2 Where the rehearsal runs — ⛔ not the box, not the runner

**Preferred: a privileged systemd container**, which is what the 2026-09-08 run used
(`jrei/systemd-ubuntu:22.04`, systemd 249, PID 1 = `systemd`, `is-system-running` = `running`). Disposable,
reproducible, and destroyed afterwards. **Alternatives:** WSL2 with systemd enabled (`/etc/wsl.conf` →
`[boot]` `systemd=true`), or any disposable Ubuntu VM.

⚠ ~~**`C-199`: run at least `S7` on a systemd 255 base as well.**~~ ✅ **DONE — `C-199` is closed.** The
second rehearsal ran on `jrei/systemd-ubuntu:24.04`, systemd **255.4-1ubuntu8.16**, the appliance's exact
version, and the 249 result transferred unchanged.

⭐ **Second amendment: run the WHOLE of §4 on the 255.4 base now, not just `S7`.** Readiness (§4.11) is
unmeasured on any version, and there is no reason left to prefer the 249 image for anything — matching the
appliance costs nothing and removes a variable from every result at once.

⛔ **Not `radio`** — the rehearsal deliberately drives a unit into a tripped limiter. ⛔ **Not the
self-hosted CI runner** (`[self-hosted, linux, x64, appserver]`) — it is shared, and leaving a latched
failed unit on it would be someone else's confusing morning.

### 4.3 The fixture — two stand-in units that carry the real timings

```bash
sudo tee /usr/local/bin/ops3-api >/dev/null <<'SH'
#!/bin/sh
# Fails on demand: create /run/ops3-crash to make every start exit non-zero.
[ -f /run/ops3-crash ] && exit 1
exec sleep infinity
SH
sudo tee /usr/local/bin/ops3-web >/dev/null <<'SH'
#!/bin/sh
exec sleep infinity
SH
sudo chmod +x /usr/local/bin/ops3-api /usr/local/bin/ops3-web

sudo tee /etc/systemd/system/ops3-api.service >/dev/null <<'EOF'
[Unit]
Description=OPS-3 rehearsal: stand-in for radio-api
Upholds=ops3-web.service
StartLimitIntervalSec=300
StartLimitBurst=5
[Service]
Type=simple
ExecStart=/usr/local/bin/ops3-api
Restart=always
RestartSec=10
EOF

sudo tee /etc/systemd/system/ops3-web.service >/dev/null <<'EOF'
[Unit]
Description=OPS-3 rehearsal: stand-in for radio-web
After=ops3-api.service
BindsTo=ops3-api.service
StartLimitIntervalSec=300
StartLimitBurst=5
[Service]
Type=simple
ExecStart=/usr/local/bin/ops3-web
Restart=always
RestartSec=10
EOF

sudo systemctl daemon-reload && sudo systemctl start ops3-web
systemctl is-active ops3-api ops3-web    # expect: active / active
systemctl show ops3-api -p Upholds       # expect: Upholds=ops3-web.service
systemctl show ops3-web -p UpheldBy -p BindsTo   # expect: UpheldBy=ops3-api.service, BindsTo=ops3-api.service
```

⚠ **`RestartSec=10` and `StartLimitBurst=5` must match production exactly.** §0.4.4 was decided by what
happens *inside* the 10 s back-off, and §0.5.1 turns on the burst value being 5 on both units; shortening
either to make the rehearsal quicker removes the thing being measured.

⚠ **The fixture above carries the FULL re-scoped shape** (`BindsTo=` **and** `Upholds=`). `S1` and `S6`
need it *without* `Upholds=` — comment that line out and `daemon-reload` before running them, then restore
it. The scenarios say which shape they need; running one against the wrong shape produces a plausible
number that means nothing, which is the failure mode §4.6's control exists to catch.

### 4.4 `S1` — ✅ RECORDED 2026-09-08. When does `ops3-web` stop? **During the back-off.**

> **Shape:** `BindsTo=` only — comment out `Upholds=` in the fixture first.
> **Result, measured:** `t=0 web=active` → `t=1 web=inactive` → `t=15 web=inactive` (api recovered, web did
> not). Resting state `inactive / dead / success`, `NRestarts=0`.
> **Verdict:** ⛔ the second row of the table below — **propagation fires on every `Restart=` cycle.**
> `BindsTo=` alone does not ship.
>
> **Re-run it as regression anyway.** It is the fixture's proof of life and it costs a minute. If it now
> reports anything other than the above, stop: either the fixture is wrong or a systemd version difference
> has appeared, and every other result in the run is suspect.

```bash
# In one terminal, watch the transitions with timestamps:
journalctl -f -u ops3-api -u ops3-web -o short-precise

# In another: make every restart fail, then kill it once to start the cascade.
sudo touch /run/ops3-crash
sudo systemctl kill -s KILL ops3-api
```

`ops3-api` will now fail, wait 10 s, fail, … five times, then trip its limiter (~40–50 s total) and land in
`failed`. **The single thing to record is the timestamp at which `ops3-web` is stopped, relative to those
five attempts.**

| Observation | Meaning | Verdict |
|---|---|---|
| `ops3-web` stops **only after** the fifth failure, when `ops3-api` reaches `failed` | Propagation fires on terminal failure only. | Would have made the row good as originally scoped. **Not what happened.** |
| ⭐ `ops3-web` stops **at the first crash**, within seconds, before any retry ← **MEASURED 2026-09-08** | Propagation fires on every `Restart=` cycle. | ⛔ **`BindsTo=` alone must not ship.** One transient API crash permanently darkens the console. `Upholds=` became a prerequisite rather than a follow-up — which is the re-scope this plan now describes. |
| `ops3-web` never stops | The fixture is not reproducing the mechanism. | ⚠ Fixture bug — `BindsTo=` typo, wrong unit, or `daemon-reload` not run. Fix before reading anything else. |

Then record the resting state:

```bash
systemctl is-active ops3-api ops3-web         # expect: failed / inactive
systemctl show ops3-web -p NRestarts -p ActiveState -p SubState
```

### 4.5 `S2`–`S4` — recovery, and the wedge probe

**`S2` — the row's command. ⚠ Its expected result now depends on which shape is loaded — run it under
BOTH, because the difference is the whole point of the re-scope:**

```bash
sudo rm -f /run/ops3-crash
sudo systemctl reset-failed ops3-api && sudo systemctl start ops3-api
systemctl is-active ops3-api ops3-web
```

| Shape | Expected | Meaning |
|---|---|---|
| `BindsTo=` only | `active / inactive` ← console still dark | `C-192`: the row's recorded recovery command is insufficient. |
| `BindsTo=` + `Upholds=` | `active / active` | ⭐ `C-192` is *softened* — the command works again, because an active api upholds web. §0.4.2 explains why the runbook still names both units. |

⚠ **Under the `BindsTo=`-only shape, a result of `active / active` would falsify §0.4.1** and mean
`BindsTo=` has a revival direction this plan says it does not. The 2026-09-08 run measured `inactive`, so
that is not expected — but if it appears now, stop and say so loudly: Tasks 1 and 4's comments would both
need rewriting, and the case for `Upholds=` would collapse.

**`S3` — the command Task 4 puts in the runbook:**

```bash
sudo systemctl stop ops3-web ops3-api
sudo touch /run/ops3-crash && sudo systemctl start ops3-api ; sleep 60 ; sudo rm -f /run/ops3-crash
sudo systemctl reset-failed ops3-api ops3-web && sudo systemctl start ops3-web
systemctl is-active ops3-api ops3-web    # expect: active / active
```

**`S4` — the wedge probe (§0.5's derivation, tested rather than trusted):** after `S1`, and *before* any
`reset-failed`, confirm `ops3-web` is startable on its own terms:

```bash
sudo systemctl reset-failed ops3-api            # clear ONLY the api
sudo systemctl start ops3-web                   # expect: succeeds, both come up
```

If this is refused with *"start request repeated too quickly"*, `ops3-web`'s own limiter was consumed by
propagation and §0.5's conclusion is wrong — which would make the pair genuinely wedgeable and is a
merge-stopper.

### 4.6 `S5`–`S6` — the deploy order, and the control that proves the fixture can see anything

**`S5` — the literal `Deploy-ToLinux.ps1` sequence (§0.7):**

```bash
sudo systemctl stop ops3-web ; sudo systemctl stop ops3-api
sudo systemctl daemon-reload && sudo systemctl start ops3-api && sudo systemctl start ops3-web
systemctl is-active ops3-api ops3-web    # expect: active / active, no errors in the journal
```

**`S6` — ⭐ the control, and it is not optional.** ✅ **RECORDED 2026-09-08:** `t=0/1/15 web=active`
throughout — web never stops. Rewrite `ops3-web.service` with `Requires=` instead of `BindsTo=`,
`daemon-reload`, and re-run `S1`.

```
Expect: ops3-api recovers, and ops3-web stays ACTIVE the whole time.
```

**If `ops3-web` stops under `Requires=` too, the fixture is not measuring what it claims** and every result
above is worthless. This run is what converts "web stopped" from an observation into evidence — it is the
same discipline `docs/BUILDER_QUEUE.md`'s banner records for counters: *verify the instrument against the
number you already believe before trusting it to produce a new one.* ⭐ **It did its job on 2026-09-08:
`S6`'s `active` next to `S1`'s `inactive` at the same timestamps is what makes `S1` a measurement rather
than an anecdote.**

### 4.7 `S7` — ⭐ the re-scoped shape. ✅ RECORDED, and the reason the row survives

> **Shape:** `BindsTo=` **and** `Upholds=` — the fixture as written in §4.3.
> **Result, measured 2026-09-08:** `t=0 web=active` → `t=2 web=inactive` → `t=16 web=active`.
> `web: NRestarts=0, active running`.

```bash
sudo systemctl start ops3-web            # both up
sudo systemctl kill --signal=SIGKILL ops3-api
sleep 2  ; systemctl is-active ops3-api ops3-web   # expect: activating / inactive  ← joint failure
sleep 16 ; systemctl is-active ops3-api ops3-web   # expect: active / active        ← AND it returns
systemctl show ops3-web -p NRestarts -p ActiveState -p SubState
```

⭐ **This is the assertion the whole re-scope rests on: `web` RETURNS.** Both directions in one run —
the console dies with the API and comes back with it.

⚠ **`NRestarts=0` is the detail not to skip.** It says `radio-web` was started by `Upholds=`, not by its own
`Restart=`, which is what keeps §0.5's no-wedge derivation intact for a single cycle. It says nothing about
five cycles — that is `S9`.

⚠ **`C-199`: re-run this scenario on a systemd 255 base** (`ubuntu:24.04`) as well as the recorded 249. The
directive was introduced in 249 and the appliance runs 255; a first-release behaviour is the most likely
kind to have been adjusted since. **A divergence here is a row-stopper**, and finding it in a container
costs minutes where finding it at the appliance costs a supervised session.

### 4.8 ⚠ `S8`–`S10` — the three questions `Upholds=` opened and nothing has answered

⛔ **These are the merge gates now.** `S1`/`S6`/`S7` are re-confirmations; these are the measurements.

**`S8` — ⚠⚠ does a deliberate stop of `ops3-web` stick? (§0.4.5, `C-200`)**

```bash
sudo rm -f /run/ops3-crash
sudo systemctl start ops3-web ; sleep 2
systemctl is-active ops3-api ops3-web    # baseline: active / active

sudo systemctl stop ops3-web
systemctl is-active ops3-web             # IMMEDIATELY after
sleep 5 ; systemctl is-active ops3-web   # and again
sleep 30; systemctl is-active ops3-web   # and again — Upholds= is continuous, not one-shot
```

| Result | Meaning | Consequence |
|---|---|---|
| `inactive` at all three checks | An explicit stop is honoured; `Upholds=` does not reverse it. | ✅ Task 5 **Branch A** (comment only). `Deploy-ToLinux.ps1:170` is safe as written. Delete the `Upholds=` caveat from Task 4's `DEPLOYMENT.md` stop-note. |
| `active` at any check | `Upholds=` reverses a deliberate stop. | ⚠ Task 5 **Branch B** — the deploy's stop path has a race over `rsync --delete` and must become one job. `radio-web` also becomes un-stoppable on its own; Task 4's note stands. **Not a row-stopper, but it changes two files.** |

⚠ **Record *when* it comes back, not just whether.** A resurrection at 200 ms and one at 25 s have very
different implications for the deploy's race window.

**`S9` — ⚠⚠ does a flapping api consume web's start-limit budget? (§0.5.1)**

```bash
# Let ops3-api fail and recover repeatedly, WITHOUT tripping web by hand.
sudo systemctl reset-failed ops3-api ops3-web
for i in 1 2 3 4 5; do
  sudo systemctl kill --signal=SIGKILL ops3-api
  sleep 13          # past RestartSec=10, so api recovers and upholds web each cycle
  systemctl show ops3-web -p NRestarts -p ActiveState -p SubState
done
systemctl is-active ops3-api ops3-web
sudo systemctl start ops3-web    # ← the probe: is web's limiter latched?
```

| Result | Meaning | Consequence |
|---|---|---|
| `start ops3-web` succeeds; web `active` | Web's budget survived five api cycles. | ✅ §0.5's no-wedge conclusion extends to the new shape. Record the observed start count. |
| *"start request repeated too quickly"* | ⛔ `Upholds=` consumed web's budget and the pair CAN wedge. | ⛔ **Stop and take it to the owner.** §0.5.1's arithmetic says the margin is zero, so this is the expected-bad answer. The fix touches `StartLimit*` values PR #467 chose deliberately — **not a Builder's call.** |

**~~`S10`~~ — ⛔ SUPERSEDED by `S12` (§4.11.2). DO NOT RUN IT.**

⭐ **It was written to *size a regression*; `S12` *tests the fix*, on the same fixture question, with the
opposite expected answer.** Running `S10` against a unit that now carries `ExecStartPost=` would report
"`web=active` after `ready=yes`" as a **failure** against the expectation below, when that is precisely the
result the row wants. ⚠ **And `C-202`, which `S10` exists to size, has been re-graded** — its harm was
overstated (§0.7b.1). *Kept below for the record; its expectation paragraph is now wrong on purpose.*

**~~`S10` (original) — does `Upholds=` start web before the api's listener is bound? (`C-202`)~~**

The fixture's `ops3-api` binds nothing, so it cannot answer this directly. Approximate it: make `ops3-api`
slow to become *useful* while becoming *active* immediately, and measure the gap between `ops3-api` going
active and `ops3-web` going active.

```bash
# Give the api a readiness marker it publishes LATE, mimicking a listener that binds
# after the process launches.
sudo tee /usr/local/bin/ops3-api >/dev/null <<'SH'
#!/bin/sh
[ -f /run/ops3-crash ] && exit 1
rm -f /run/ops3-ready ; ( sleep 8 ; touch /run/ops3-ready ) &
exec sleep infinity
SH
sudo systemctl restart ops3-api
# Watch which happens first: ops3-web active, or /run/ops3-ready appearing.
for i in $(seq 1 20); do
  printf '%s web=%s ready=%s\n' "$i" "$(systemctl is-active ops3-web)" "$([ -f /run/ops3-ready ] && echo yes || echo no)"
  sleep 1
done
```

**Expected:** `web=active` well before `ready=yes` — i.e. `Upholds=` fires at launch, not at readiness,
confirming `C-202`. **What to record:** the gap in seconds. That number is what tells the owner whether the
`Deploy-ToLinux.ps1` health-poll has been rendered decorative on this hardware, and it goes in the PR body
next to §8.5's follow-up proposal. ⚠ **This scenario cannot fully model the real API's bind timing** — say
so when reporting it, and do not present the number as a production measurement.

### 4.9 `S11` — the rollback drop-in (§6.3), which uses an idiom this plan did not verify

⚠ **Widened by the re-scope: the drop-in must now clear `Upholds=` on the api side too** (`C-201`).

```bash
sudo mkdir -p /etc/systemd/system/ops3-web.service.d /etc/systemd/system/ops3-api.service.d
printf '[Unit]\nBindsTo=\nRequires=ops3-api.service\n' | sudo tee /etc/systemd/system/ops3-web.service.d/99-ops3-rollback.conf
printf '[Unit]\nUpholds=\n[Service]\nExecStartPost=\n' | sudo tee /etc/systemd/system/ops3-api.service.d/99-ops3-rollback.conf
sudo systemctl daemon-reload
systemctl show ops3-web -p BindsTo -p Requires -p UpheldBy  # expect: BindsTo= empty, Requires=ops3-api.service, UpheldBy= empty
systemctl show ops3-api -p Upholds -p ExecStartPost         # expect: BOTH empty
```

⭐ **Widened again by the second amendment: the api drop-in must clear `ExecStartPost=` too, and that lives
in `[Service]`, not `[Unit]`.** So this scenario now also tests that a **two-section** drop-in resets both —
a materially different claim from resetting one list in one section. ⚠ **Also confirm the reset takes effect
on the next start**, not merely in `show`: `sudo systemctl restart ops3-api` and check it no longer passes
through `start-post`. A `show` that reports empty while the unit still runs the command would make §6.3 and
§6.3b advice that does not work.

Then re-run `S1` and confirm `ops3-web` survives. **If the empty-assignment reset does not clear `BindsTo=`
or `Upholds=`, §6.3's no-backup fallback does not work** and §6 must say so rather than offering it.
⚠ **Check `UpheldBy=` specifically** — it is the automatic reverse dependency, and a rollback that clears
`Upholds=` on the api without clearing `UpheldBy=` on the web unit would leave the coupling half-alive.

**Teardown:**

⚠ **Stop `ops3-api` FIRST or `disable --now ops3-web` may be undone by `Upholds=`** — the teardown is
itself an instance of §0.4.5's question.

```bash
sudo systemctl disable --now ops3-api ops3-web 2>/dev/null
sudo rm -f /etc/systemd/system/ops3-{api,web}.service /usr/local/bin/ops3-{api,web} /run/ops3-crash /run/ops3-ready
sudo rm -rf /etc/systemd/system/ops3-web.service.d /etc/systemd/system/ops3-api.service.d
sudo systemctl daemon-reload && sudo systemctl reset-failed
```

### 4.11 ⭐ NEW (2nd amendment) — `S12`–`S15`, the readiness scenarios

*(Numbered 11 and placed here on purpose: §4.10's gates table stays last, and renumbering would break every
cross-reference in this plan.)*

#### 4.11.0 ⚠ The fixture must CHANGE, and that changes what re-running `S1`/`S6`/`S7` means

§4.3's `ops3-api` binds nothing and publishes nothing, so it cannot express readiness at all. It gains a
**late marker**, and the unit gains a **real `ExecStartPost=` of the same structural shape as Task 7's** —
same wrapper, same `timeout`, same `|| echo`, same trailing `exit 0`, differing only in the probe (a file
instead of a `curl`). ⚠ **Keep that shape identical.** A fixture that tests a *simpler* command validates a
different thing and would miss, for example, a quoting bug in the real line.

```bash
sudo tee /usr/local/bin/ops3-api >/dev/null <<'SH'
#!/bin/sh
# Fails on demand: create /run/ops3-crash to make every start exit non-zero.
[ -f /run/ops3-crash ] && exit 1
# Readiness marker, published LATE — mimics a listener that binds after the process
# launches. /run/ops3-slow suppresses it entirely; that is S13's give-up path.
rm -f /run/ops3-ready
if [ ! -f /run/ops3-slow ]; then ( sleep 8 ; touch /run/ops3-ready ) & fi
exec sleep infinity
SH
sudo chmod +x /usr/local/bin/ops3-api
```

Add to `[Service]` in `/etc/systemd/system/ops3-api.service`, immediately after `ExecStart=`:

```
ExecStartPost=/usr/bin/bash -c 'timeout 20 bash -c "until [ -f /run/ops3-ready ]; do sleep 0.5; done" || echo "<4>ops3: readiness poll gave up after 20s"; exit 0'
TimeoutStartSec=90
```

```bash
sudo systemctl daemon-reload
systemctl show ops3-api -p ExecStartPost -p TimeoutStartUSec   # both must be non-empty
sudo systemd-analyze verify /etc/systemd/system/ops3-api.service /etc/systemd/system/ops3-web.service
```

⚠⚠ **`S1`, `S6` and `S7` must be RE-RUN on this changed fixture, and their recorded timestamps will move.**
The 2026-09-08 numbers (`t=1`, `t=2`, `t=15`, `t=16`) were measured with no `ExecStartPost=`; adding ~8 s of
activation shifts every recovery timestamp later by roughly that much. **What must survive is the ORDER and
the ENDPOINT, not the seconds** — `S6` web never stops, `S1` web stops and stays stopped, `S7` web stops and
returns. ⛔ **Do not report a shifted timestamp as a regression, and do not quote the old numbers as if they
were re-measured.**

#### 4.11.1 ⛔ `S12a` — THE GATE. Does `ExecStartPost=` actually hold `active` open?

⭐ **Run this before anything else in §4, and stop the row if it fails.** §1.4's entire decision rests on it,
and §0.4.4 is this plan's own evidence that a systemd behaviour must not be settled by reasoning from a
directive's name — that error has already cost this row one full re-scope.

```bash
sudo rm -f /run/ops3-crash /run/ops3-slow /run/ops3-ready
sudo systemctl stop ops3-web ops3-api
sudo systemctl start ops3-api &          # background: sample while the start job is open
for i in 1 2 3 4 5 6 7 8 9 10 11 12; do
  printf '%2s %s\n' "$i" "$(systemctl show ops3-api -p ActiveState -p SubState --value | tr '\n' ' ')"
  sleep 1
done
```

| Observation | Meaning | Verdict |
|---|---|---|
| ⭐ `activating start-post` for ~8 samples, then `active running` | `ExecStartPost=` holds the start job open and `active` is deferred until it returns. | ✅ **The mechanism exists. Proceed.** |
| `active running` from the very first sample | `ExecStartPost=` runs but does not gate `active`. | ⛔ **THE OPTION IS DEAD.** Task 7 cannot work. Stop, and take §1.4 back to the owner — `Type=notify` becomes the only candidate and its three costs (§1.4) must be re-priced, including the loss of `C-191`. |
| `activating start` and never `start-post` | The `ExecStartPost=` line was not parsed. | ⚠ Fixture bug — quoting, or `daemon-reload` not run. `systemd-analyze verify` first. |

#### 4.11.2 `S12` — does `ops3-web` now start only AFTER readiness? *(supersedes `S10`)*

⭐ **`S10`'s expected result INVERTS.** `S10` expected `web=active` well *before* `ready=yes`, confirming
`C-202`. `S12` expects the opposite, confirming the fix. **Same fixture question, opposite correct answer.**

```bash
sudo systemctl stop ops3-web ops3-api ; sudo rm -f /run/ops3-ready /run/ops3-slow
sudo systemctl start ops3-api &
for i in $(seq 1 25); do
  printf '%2s api=%-10s sub=%-10s web=%-8s ready=%s\n' "$i" \
    "$(systemctl is-active ops3-api)" "$(systemctl show ops3-api -p SubState --value)" \
    "$(systemctl is-active ops3-web)" "$([ -f /run/ops3-ready ] && echo yes || echo no)"
  sleep 1
done
```

**The assertion is a negative and it is the whole scenario: `web=active` must NEVER appear on a line whose
`ready=no`.** Record the gap in seconds between `ready=yes` and `web=active` — it should be under a second.
⛔ A single `web=active` / `ready=no` line means `Upholds=` is still firing early and Task 7 has not worked;
that is merge-blocking.

#### 4.11.3 `S13` — the give-up path: readiness never arrives

The `exit 0` discipline (§0.11, Task 7a) is what stops a slow API becoming a failed one. Measure it.

```bash
sudo systemctl stop ops3-web ops3-api
sudo touch /run/ops3-slow                     # the marker will never appear
date -u +%T ; sudo systemctl start ops3-api ; echo "exit=$? at $(date -u +%T)"
systemctl show ops3-api -p ActiveState -p SubState -p Result --value
journalctl -u ops3-api --since '-3min' -o short-precise --no-pager | grep -i 'gave up'
sudo rm -f /run/ops3-slow
```

| Expected | Why it matters |
|---|---|
| `start` blocks ~20 s, then **exit 0** | The ceiling is real and bounded. |
| `active` / `running` / **`success`** | ⛔ Any `failed` here means `exit 0` was not honoured, and a slow API would feed `StartLimitBurst=5`. **Merge-stopper.** |
| The `gave up` line appears in the journal **at warning priority** | Without the `<4>` prefix it lands at `SyslogLevel=debug` and the give-up is silent. Confirm it survives `journalctl -p warning`. |
| `ops3-web` comes up anyway, after the ceiling | Degrades to pre-`OPS-3` behaviour rather than to a dark console. As designed. |

#### 4.11.4 `S14` — the crash-recovery dark window, measured rather than estimated

⚠ **Instrument: `ActiveEnterTimestamp`, and validate it before trusting it.** `NRestarts` cannot see any of
this (§4.8) — `Upholds=` issues `start`, not `restart` — and an earlier derivation resting on it was
vacuous. `ActiveEnterTimestamp` moves on *every* entry into active regardless of mechanism, which is exactly
the property `NRestarts` lacks. **Record it before the kill and confirm it has MOVED afterwards; if it has
not, the instrument is not seeing the cycle and every number below is worthless.** That is the same
discipline `S6` supplies for `S1`.

```bash
sudo rm -f /run/ops3-crash /run/ops3-slow
sudo systemctl start ops3-web ; sleep 12
systemctl show ops3-web -p ActiveEnterTimestamp --value      # BASELINE — write it down
sudo systemctl kill --signal=SIGKILL ops3-api
for i in $(seq 1 40); do
  printf '%2s api=%-10s web=%s\n' "$i" "$(systemctl is-active ops3-api)" "$(systemctl is-active ops3-web)"
  sleep 1
done
systemctl show ops3-web -p ActiveEnterTimestamp --value      # must DIFFER from the baseline
```

**Record the dark window** — first sample where `web=inactive` to first sample where `web=active`. Expect
roughly `RestartSec=10` + the ~8 s readiness delay ≈ **18 s**, against ~15 s without readiness. ⭐ **That
number goes into §0.4.6's table, into the PR body and into `DEPLOYMENT.md`'s "wait ~30 s" guidance** — if it
lands above ~25 s the runbook's wait threshold needs raising with it, because an operator told to wait 30 s
who is actually facing 40 s will conclude the recovery failed and intervene in it.

#### 4.11.5 `S15` — ⚠ does the limiter still latch with a longer start? *(§0.7b.4's bound, tested)*

⛔ **This is the scenario that can invalidate the chosen ceiling.** If 5 start attempts no longer fit inside
`StartLimitIntervalSec=300`, `ops3-api` never reaches `failed` and crash-loops indefinitely — the console
flapping dark and light forever, which is **worse** than the wedge #467's limiter exists to produce.

⚠ **Kill only once the unit is `active`.** Killing during activation, or with `/run/ops3-crash` set, makes
`ExecStart` exit before `ExecStartPost` ever runs — that is the *cheap* cycle shape and it is not the one
under test.

```bash
sudo systemctl reset-failed ops3-api ops3-web
sudo rm -f /run/ops3-crash /run/ops3-slow
for i in 1 2 3 4 5 6; do
  timeout 90 bash -c 'until [ "$(systemctl is-active ops3-api)" = active ]; do sleep 0.5; done' \
    || { echo "cycle $i: never reached active — STOP"; break; }
  echo "cycle $i: api active at $(date -u +%T)"
  sudo systemctl kill --signal=SIGKILL ops3-api
  sleep 1
done
systemctl show ops3-api -p ActiveState -p SubState -p Result --value
systemctl is-active ops3-api ops3-web
```

| Result | Meaning | Consequence |
|---|---|---|
| `ops3-api` reaches **`failed`** by cycle 5–6, and cycle 1 → cycle 5 spans **< 300 s** | The limiter still latches with readiness in the loop. | ✅ §0.7b.4's arithmetic holds at a 20 s ceiling. Record the measured span. |
| `ops3-api` **never** reaches `failed` | Each cycle now costs more than 300 s ÷ 4. | ⛔ **Merge-stopper.** Lower the ceiling until the span fits, re-run, and **report both numbers to the owner** — do not silently pick a number. |
| `ops3-web` reaches `failed` first | `Upholds=`'s starts exhausted web's own budget (§0.5.1's zero margin), now with readiness stretching the window. | ⛔ **Stop.** This is `S9`'s bad answer arriving by a second route; the fix touches `StartLimit*` values #467 chose and is **not a Builder's call.** |

⚠ **Run `S9` again after `S15`, not instead of it.** They ask different questions — `S9` is about web's
budget across api recoveries, `S15` is about api's own budget across longer starts — and the readiness
change moves the ground under both.

**Teardown addition:** `sudo rm -f /run/ops3-ready /run/ops3-slow` alongside §4.9's existing removals.

### 4.10 Gates

| Gate | Requirement |
|---|---|
| `dotnet build --configuration Release` | ⚠ **CORRECTED 2nd amendment — this row used to say "0 warnings", which is wrong and would have sent a Builder chasing 47 phantom warnings.** `CLAUDE.md` records the baseline as **47 warnings, 0 errors** (measured on `main` 2026-09-06, all `IDE0011`), and **the gate is equality with that baseline, not an absolute.** ⭐ This row touches no C# at all, so the expected delta is exactly zero either way. (Proves nothing about this row — §4.1.) |
| `dotnet test --configuration Release` | Green, minus the known-failing set `CLAUDE.md` names. ⚠ **Do not pipe it into `tail`** — `CLAUDE.md` records a measured case where that reported `exit=0` with five tests failing. Redirect to a file, then read the per-project summary lines. (Same caveat: proves nothing about this row.) |
| **`S12a`** | ⛔ **THE gate, and it runs first.** `ExecStartPost=` must hold `ActiveState=activating`/`start-post`. A failure kills the chosen mechanism and stops the row (§4.11.1, §1.4). |
| **`S12`** | ⭐ **Merge-blocking.** `ops3-web` must never be `active` while readiness is absent. Supersedes `S10`, whose expected result inverts. |
| **`S13`** | ⭐ **Merge-blocking.** The give-up path must leave the unit `active`/`success`, never `failed`, and must log at warning. |
| **`S15`** | ⭐ **Merge-blocking.** `ops3-api` must still latch `failed` inside 300 s with the readiness ceiling in the loop. A never-latching crash loop is worse than the wedge. |
| `S14` | Run and reported with the measured dark window. ⚠ Not merge-blocking, but the number sets `DEPLOYMENT.md`'s wait threshold and §0.4.6's table. |
| **`S1` / `S6` / `S7` re-run** | ⭐ **On the CHANGED fixture** (§4.11.0). Order and endpoint must hold; timestamps will shift and that is expected, not a regression. |
| **`S7`** | ⭐ **Merge-blocking.** The re-scoped shape returns the console. Result pasted into the PR body verbatim with timestamps. |
| **`S1` + `S6`** | ⭐ **Merge-blocking as a pair.** The control next to the measurement is what makes either one evidence. Both pasted verbatim. |
| **`S8`** | ⭐ **Merge-blocking.** Selects Task 5's branch and Task 4's `DEPLOYMENT.md` wording. Neither can be written without it. |
| **`S9`** | ⭐ **Merge-blocking.** A latched `radio-web` after five api cycles stops the row and goes to the owner (§0.5.1). |
| ~~`S10`~~ | ⭐ **SUPERSEDED by `S12`** (§4.11.2). It was written to *size* `C-202`; `S12` *tests the fix*, and its expected result is the inverse. Do not run both and do not quote `S10`'s framing. |
| ~~`S7` on systemd 255~~ | ✅ **`C-199` CLOSED** by the second rehearsal (`jrei/systemd-ubuntu:24.04`, systemd **255.4-1ubuntu8.16** — the appliance's exact version). ⚠ **But readiness is unmeasured on any version**, so run the **whole of §4.11 on the 255.4 base**, not on 249. §5.3 still confirms on the box. |
| `S2`–`S5`, `S9`, `S11` | Run and reported. A surprise in `S2`, `S4` or `S11` changes the plan text before merge. ⚠ **`S9` must be re-run after `S15`** — readiness moves the ground under it (§4.11.5). |
| Shell syntax (Task 6) | `bash -n deploy/debian-x64/kiosk/bin/radio-console-open` |
| Unit-file syntax | `systemd-analyze verify` on both units, in the rehearsal container. ⭐ **Now load-bearing rather than a formality:** Task 7 adds a long nested-quoting `ExecStartPost=` line, and a quoting error there is **silently ignored or silently mis-split** — the exact failure `S12a`'s third row catches. Also confirm `systemctl show ops3-api -p ExecStartPost` echoes the line back non-empty. |
| Box verification | ⛔ **None, and none is claimed.** §0.6 / §0.9. ⚠ **And one number this design uses has no repository source at all** — §0.7b.5 #1, the real exec→ready duration. It is measured in §5.3 and nowhere earlier. |

---

## 5. Rollout — a written procedure for a human, not a Builder step

⛔ **Nothing in this section is a Builder action.** It runs with the owner present, at a time he chooses,
after the PR has merged.

### 5.1 Why it is a separate event

Per `C-191`, merging changes nothing on `radio` and neither does a deploy. The box keeps `Requires=` until
someone installs the new unit. That separation is the safety property this row leans on — take advantage of
it and do not rush the two together.

### 5.2 Install — two units, and the first commands are the rollback

⚠ **Re-scoped: TWO units, and they go together.** `C-201` — installing one without the other is a defined
bad state, and the worst of them (`Requires=` on web with `Upholds=` still on api) is an appliance where
`systemctl stop radio-web` is silently undone with nothing to explain it.

⭐ **Second amendment: THREE directives now, not two** — `BindsTo=` on web, `Upholds=` **and**
`ExecStartPost=` on api — plus two read-only pre-checks that cost nothing and each close one of §0.7b.5's
unverifiable items.

```bash
# 0. PRE-CHECKS. Read-only, and both settle something the repository could not.
command -v curl || echo "⛔ STOP: curl absent — Task 7's ExecStartPost cannot run (C-206)"
systemctl show radio-api -p TimeoutStartUSec --value
#    Record this. Expect 1min 30s (the systemd default; TimeoutStartSec= is absent from
#    every unit in the repo). Task 7 sets it explicitly to 90s, so a DIFFERENT value here
#    means the box has a non-default DefaultTimeoutStartSec and installing is a behaviour
#    change rather than a no-op. Stop and say so if it differs.

# 1. THE BACKUPS — BOTH of them. Do this first. Section 6.2's rollback depends on both existing.
sudo cp /etc/systemd/system/radio-web.service /etc/systemd/system/radio-web.service.pre-ops3
sudo cp /etc/systemd/system/radio-api.service /etc/systemd/system/radio-api.service.pre-ops3

# 2. Install BOTH new units from a checkout of main, before any daemon-reload.
sudo cp deploy/common/radio-web.service /etc/systemd/system/radio-web.service
sudo cp deploy/common/radio-api.service /etc/systemd/system/radio-api.service

# 3. Apply them together. This does NOT restart anything.
sudo systemctl daemon-reload

# 4. Confirm what systemd actually loaded — not what the files say. FIVE properties now.
systemctl show radio-web -p BindsTo -p Requires -p After -p UpheldBy
#    expect: BindsTo=radio-api.service / Requires= (empty) / After=... radio-api.service ... /
#            UpheldBy=radio-api.service
systemctl show radio-api -p Upholds -p ExecStartPost -p TimeoutStartUSec
#    expect: Upholds=radio-web.service
#            ExecStartPost={ path=/usr/bin/bash ; argv[]=... }  ← must be NON-EMPTY
#            TimeoutStartUSec=1min 30s

# 5. Confirm nothing moved.
systemctl is-active radio-api radio-web    # expect: active / active
```

⚠⚠ **Step 4's `ExecStartPost` check is not decoration.** systemd **silently drops** an `Exec*` line it
cannot parse, and Task 7's line is long, nested-quoted, and the only one of its kind in the repository. **An
empty `ExecStartPost=` here means readiness is absent while everything else looks installed** — which is
`C-203`'s bad state (`Upholds=` firing at exec, exactly as before the fix) with nothing on screen to say so.
This is the only place that failure is visible.

⚠ **Step 3 remains safe against two running units**, and readiness does not change that: `daemon-reload`
re-evaluates dependencies, it does not re-run `ExecStartPost=`. The new directive takes effect on the next
start of `radio-api`, which is §5.3's kill or the next deploy — **not now.**

⭐ **Step 4 checks `UpheldBy=` on the web unit as well as `Upholds=` on the api unit.** `UpheldBy=` is the
automatic reverse dependency systemd derives; seeing it is how you know the two files are actually talking
to each other rather than each carrying a directive that names a unit the other does not confirm. **A typo
in the unit name would show up here and nowhere else** — `Upholds=radio_web.service` parses fine and does
nothing.

⚠ **Step 3 is safe against two running units.** `daemon-reload` re-evaluates dependencies; `radio-api` is
`active` so the `BindsTo=` invariant is already satisfied, and `radio-web` is `active` so `Upholds=` finds
nothing inactive to start. Nothing propagates in either direction.

⚠ **Prefer the targeted `cp` over re-running `deploy/debian-x64/setup.sh`.** The setup script installs
units, enables them, and does a good deal more besides; running the whole thing to change one line is a
much larger blast radius than the row has.

### 5.3 The acceptance test on the box — one controlled kill, owner present

This is the only place the change is observable on the real appliance, and it costs a brief interruption to
whatever is playing.

⭐ **This drill is now the `C-199` version check as well as the acceptance test.** The `Upholds=` behaviour
was measured on **systemd 249** and the appliance runs **255**. This is where that gap closes on the real
hardware — so record the intermediate states, not just the endpoint.

⭐⭐ **Second amendment: this drill now carries a MEASUREMENT, and it is the only source of a number this
design depends on.** §0.7b.5 #1 — how long `radio-api` really takes from exec to the hub answering, on this
hardware. The 20 s ceiling was sized at 2× the deploy's 10 s poll because nothing in the repository records
the real figure. **Do step A even if nothing else in this drill runs.**

⚠ **Two-point sampling is no longer adequate.** `sleep 2` / `sleep 14` worked when recovery was
`RestartSec` alone; readiness adds a variable delay on top, so a point check can land in the gap and report
a false `active / inactive`. Sample every second.

```bash
systemd-analyze --version | head -1     # record it: expect systemd 255 on this box

# ---- STEP A: the measurement. Read-only, no kill, nothing restarts. -------------------
# How long does the hub take to answer from a cold start of the process? Time the probe
# the unit will now run, against the CURRENTLY RUNNING api — this is a lower bound (the
# service is already warm) but it costs nothing and a slow answer here is decisive.
time curl -sf -o /dev/null -X POST \
  http://localhost:5000/hubs/visualization/negotiate?negotiateVersion=1 && echo READY

# ---- STEP B: the acceptance test. ONE kill. -------------------------------------------
systemctl show radio-web -p ActiveEnterTimestamp --value   # BASELINE — write it down
sudo systemctl kill -s KILL radio-api
for i in $(seq 1 40); do
  printf '%2s api=%-10s sub=%-12s web=%s\n' "$i" \
    "$(systemctl is-active radio-api)" \
    "$(systemctl show radio-api -p SubState --value)" \
    "$(systemctl is-active radio-web)"
  sleep 1
done
systemctl show radio-web -p ActiveEnterTimestamp --value   # must DIFFER from the baseline
```

⭐ **What step B is actually measuring, and it is three things at once:**

1. **`BindsTo=` fires** — `web=inactive` within a second or two of the kill.
2. **Readiness is real on the box** — `radio-api` should pass through `sub=start-post` before reaching
   `active`. ⚠ **If `start-post` never appears in 40 samples, `ExecStartPost=` is not running**, and §5.2
   step 4's check was misread. That is `C-203`'s state, live.
3. ⭐ **The real exec→ready duration** — count the samples spent in `start-post`. **That is the number
   §0.7b.5 #1 asks for.** Write it down; it decides whether 20 s was a good ceiling and it belongs in the
   row's record whatever else happens.

⚠⚠ **`NRestarts` is NOT part of this drill any more, and an earlier revision of this section told you to
check it.** `Upholds=` issues `start`, not `restart`, so it reads 0 whether or not `radio-web` was cycled —
§4.8 records that a no-wedge derivation resting on it was **vacuous**. `ActiveEnterTimestamp` moving is the
instrument, and the baseline/after pair is what makes it one rather than a number to nod at.

| Result | Meaning |
|---|---|
| `web=inactive` early, **`sub=start-post` appears**, then `api=active` / `web=active` | ✅ **The expected pass.** All three directives are live on 255. The console blinked out and came back by itself. `C-199` closed on the box; readiness confirmed on the box. **Record the dark window and the `start-post` duration.** |
| `web=inactive` early, then **`api=active` / `web=inactive`** and it stays | ⛔ **`Upholds=` did not fire.** The box now has the shape §0.4.4 rejected. Recover with §6.1, **roll back all three directives** (§6.2), and take it to the owner — the risk `C-199` names. |
| `api=active` / `web=active` throughout, no transition | ⚠ Propagation never fired at all. Either the units did not load (re-check §5.2 step 4) or `daemon-reload` was skipped. **Not a pass** — it means the coupling is absent, i.e. today's behaviour, not the row's. |
| ⭐ **`sub=start-post` NEVER appears** | ⚠⚠ **Readiness is not running**, though `BindsTo=`/`Upholds=` may be fine. `C-203`'s state, live on the box: the console returns against a possibly-unready API. Not an emergency (the client retry loop absorbs it — §0.7b.1) but the row has **not** delivered. Re-check `systemctl show radio-api -p ExecStartPost`; a silently-dropped line is the likely cause. |
| ⭐ **`start-post` lasts longer than ~18 s** | ⚠ The 20 s ceiling is close to being hit on real hardware. §0.7b.5 #1's number is worse than assumed. **Do not adjust anything at the panel** — record it and take it to the owner; the ceiling has a hard upper bound of ~50 s (§0.7b.4) and raising it is a judgement, not a tweak. |
| `ActiveEnterTimestamp` unchanged | ⚠ **The instrument did not see the cycle**, so every timing above is unsupported. Do not report the run as a pass; repeat it. |

⚠ **Expect the audio to stop for the duration** — this kills the audio service. One kill, at a time the
owner chooses.

⛔ **Do not run this five times.** Five kills inside 300 s trips the limiter for real (`C-193`), and the
recovery then costs the owner his music for longer than the test is worth.

⛔ **Do not deliberately trip the limiter on the box to "test the dark console".** `S1` already measured
that in a container. There is nothing left to learn here that is worth a dark appliance.

### 5.4 Afterwards

Leave **both** `/etc/systemd/system/radio-web.service.pre-ops3` and `radio-api.service.pre-ops3` in place
for at least a week. They are a few KB and they are the difference between a one-line rollback and an
editing session at a touch panel. ⚠ **Do not delete one and keep the other** — a rollback that restores
only half is `C-201`'s bad state.

---

## 6. Rollback — at the console, on WiFi, assuming SSH is not available

The scenario the row asks for: the owner is standing at the appliance, the console is dark, and he cannot
get a shell in from elsewhere. Two different things he might want, and they should not be confused.

### 6.1 Recovery — "give me the console back now". No keyboard needed.

1. If the kiosk is showing a connection error, tap **Exit to Desktop** (`radio-exit-browser.desktop` →
   `/usr/local/bin/radio-kiosk-exit`).
2. Tap **Radio Console** (`radio-console.desktop` → `/usr/local/bin/radio-console-open`, *"Starts anything
   that isn't running"*). It probes both services (`:92-93`) and starts what is down (`:180-181`), then
   reopens the kiosk.

**This works whenever `radio-web` is merely stopped and `radio-api` is startable.**

⭐ **Since the re-scope, step 0 is: wait ~30 s.** `Upholds=` returns the console by itself after a transient
API crash (measured: back at t=16), so most dark consoles no longer need any of this. The routes below are
for the case that is *still* dark after the API has had its chance — which means the limiter latched.

⚠ **It does not work if `radio-api`'s limiter is latched** — `C-197`. `systemctl start` on a latched unit
returns non-zero, and the launcher paints a red **AUDIO** row and stops. ⭐ **After the re-scope, a latched
`radio-api` is the *only* remaining cause of a persistently dark console** — `Upholds=` handles every other
one — so without Task 6 the affordance would cover exactly the cases that no longer need it and fail on the
only one that does. **Task 6 is taken (§1.2), so this step handles that case too and no keyboard is needed
at all.**

⚠ **It also depends on passwordless sudo**, which the launcher's own comment (`:176-179`) asserts the box
has and which **no script in this repository installs** — `grep` for `sudoers` / `NOPASSWD` across
`setup.sh`, `setup-kiosk.sh` and `provision.sh` returns nothing. It is box-only configuration. `C-196`'s
neighbour, recorded in §9.2 as unverified. If it is not configured, every start in the launcher fails red
and §6.2 is the only route.

### 6.2 Rollback — "undo OPS-3". Needs a keyboard, one paste, no editor.

⚠⚠ **BOTH unit files must be restored before `daemon-reload`.** `C-201`: restoring only `radio-web` leaves
`Upholds=radio-web.service` on an active `radio-api`, and the box becomes one where `systemctl stop
radio-web` is silently reversed by systemd with nothing on screen to explain it — a state stranger than
either endpoint, and one a future operator has no reason to suspect.

Exit the kiosk (§6.1 step 1), open a terminal from the GNOME desktop, then:

```bash
sudo cp /etc/systemd/system/radio-web.service.pre-ops3 /etc/systemd/system/radio-web.service \
  && sudo cp /etc/systemd/system/radio-api.service.pre-ops3 /etc/systemd/system/radio-api.service \
  && sudo systemctl daemon-reload \
  && systemctl show radio-api -p Upholds -p ExecStartPost \
  && sudo systemctl reset-failed radio-api radio-web \
  && sudo systemctl start radio-web
```

The `systemctl show` in the middle is there to be *read*: it must print an empty `Upholds=` **and an empty
`ExecStartPost=`**. If either names anything, the api unit did not get restored and the rollback is
half-done. ⭐ **Restoring the file covers all three directives at once** — that is the advantage of having
kept readiness inside the unit file (§1.4), and it is what `Type=notify` would have cost: a binary rollback
on a different schedule.

**That is the whole rollback**, and it is one paste precisely because §5.2 step 1 took both backups. The box
is back to pre-`OPS-3` behaviour — `radio-web` will once again survive a dead `radio-api`.

### 6.3 If the backups are missing — drop-ins, no editor, still one paste

⚠ **Two drop-ins now, one per unit**, for the same reason §6.2 restores two files.

```bash
sudo mkdir -p /etc/systemd/system/radio-web.service.d /etc/systemd/system/radio-api.service.d
printf '[Unit]\nBindsTo=\nRequires=radio-api.service\n' \
  | sudo tee /etc/systemd/system/radio-web.service.d/99-ops3-rollback.conf
printf '[Unit]\nUpholds=\n[Service]\nExecStartPost=\n' \
  | sudo tee /etc/systemd/system/radio-api.service.d/99-ops3-rollback.conf
sudo systemctl daemon-reload
systemctl show radio-web -p BindsTo -p Requires -p UpheldBy   # BindsTo= and UpheldBy= must be EMPTY
systemctl show radio-api -p Upholds -p ExecStartPost          # BOTH must be EMPTY
sudo systemctl reset-failed radio-api radio-web && sudo systemctl start radio-web
```

⚠ **Note the api drop-in now spans TWO sections.** `Upholds=` is a `[Unit]` key and `ExecStartPost=` is a
`[Service]` key, so a drop-in carrying only `[Unit]` silently leaves readiness in place — which is the
partial state §6.3b describes, reached by accident instead of on purpose.

#### 6.3b ⭐ NEW — the ONE partial rollback that is allowed: readiness only

⛔ Every other partial state is forbidden (`C-201`, `C-203`, §3's table). **This one is not**, and it exists
because readiness is the newest and least-measured of the three directives.

**When:** `radio-api` is taking a long time to reach `active`, or `sub=start-post` is hanging, and the
coupling itself is behaving. Dropping readiness returns the box to the shape the first amendment measured
and the owner approved — `BindsTo=` + `Upholds=`, console returning against a possibly-unready API — which
the client retry loop absorbs in 2–30 s (§0.7b.1). **Degraded, understood, and not dark.**

```bash
sudo mkdir -p /etc/systemd/system/radio-api.service.d
printf '[Service]\nExecStartPost=\n' \
  | sudo tee /etc/systemd/system/radio-api.service.d/98-ops3-no-readiness.conf
sudo systemctl daemon-reload
systemctl show radio-api -p ExecStartPost -p Upholds   # ExecStartPost EMPTY, Upholds still set
```

⚠ **It takes effect on the next start of `radio-api`, not immediately** — a start job already in `start-post`
is not interrupted by a `daemon-reload`. If one is stuck, `sudo systemctl stop radio-api` first, which also
takes the console down via `BindsTo=`; then start again.

⚠ **Record it and tell the owner.** A `98-` drop-in silently disabling half a shipped row is exactly the
kind of box-only divergence `C-191` warns is invisible from the repository. It is a stopgap, not a decision.

An empty assignment resets a list in systemd, so `BindsTo=` on its own clears the binding, `Upholds=` on
its own clears the return direction, and the `Requires=` line reinstates the old dependency. ⚠ **This plan
did not verify that idiom against a running systemd** — `S11` does, and if `S11` fails this subsection must
be deleted rather than shipped as advice. ⚠ **`UpheldBy=` is the one to watch**: it is derived rather than
declared, and a rollback that clears `Upholds=` but leaves `UpheldBy=` populated has not actually landed.

Undo the drop-ins later with
`sudo rm -rf /etc/systemd/system/radio-{web,api}.service.d/99-ops3-rollback.conf && sudo systemctl daemon-reload`.

### 6.4 ⭐ The floor: no keyboard, no SSH, no terminal — power cycle, and it works

Both units are `WantedBy=multi-user.target` and enabled (`setup.sh:249-250`), and **start-limit counters do
not survive a boot**. So a restart brings up `radio-api`, then `radio-web`, from a clean slate.

Use the **Shut Down** desktop icon (`radio-shutdown.desktop` → `radio-shutdown-confirm`) if the desktop is
reachable, or hold the power button if it is not.

⭐ **This is the reassurance worth stating plainly: `OPS-3` cannot create a state that a reboot does not
clear.** The one exception is a `radio-api` that fails deterministically on every boot — in which case the
console is dark on every boot, the appliance genuinely is broken, and that is the failure `OPS-3` exists to
make visible rather than one it caused.

---

## 7. Docs and queue

- `deploy/common/radio-web.service` — Task 1, **plus Task 7c's one-token comment fix at `:105`.**
- `deploy/common/radio-api.service` — **Task 2 and Task 7.**
- `deploy/debian-x64/setup.sh`, `deploy/raspberry-pi/setup.sh` — Task 3, **four heredocs plus the two
  `Type=notify` corrections (`debian-x64:172`, `raspberry-pi:281`)**.
- `deploy/DEPLOYMENT.md` — Task 4 (`:30`, `:731-732`, and the new *A dark console: wait first, then
  recover* subsection).
- `deploy/Deploy-ToLinux.ps1` — Task 5, comment **or** stop-order change per `S8`.
- `deploy/debian-x64/kiosk/bin/radio-console-open` — Task 6.
- **PR body must carry a Docs Impact section**, and must state in plain words:
  - that the green suite is **not** evidence for this change (§4.1), and that `S1`/`S6`/`S7`/`S8`/`S9` are;
  - that **the row was re-scoped after its plan was written** — plain `BindsTo=` was measured to make the
    appliance worse and does not ship (§0.4.4);
  - the residual cost in §0.4.6's words — **a transient crash now blinks the console out for 10–50 s** —
    rather than presenting `Upholds=` as having made the row free;
  - Task 6's tension with #467 (§1.2): the limiter no longer guarantees a crash loop comes to rest;
  - ~~`C-199`~~ — ✅ **closed**: the second rehearsal reproduced the `Upholds=` result on systemd **255.4**,
    the appliance's exact version. ⚠ **Readiness, however, is unmeasured on any version until §4.11 runs;**
  - that **nothing was verified on `radio`**.

⭐ **The PR body must ALSO carry these five, added by the second amendment. The first two are corrections to
things the PR's own reviewers will have read elsewhere and believe:**

  - ⚠⚠ **`C-202`'s harm was overstated in four documents, this plan included.** The visualization hub does
    **not** stay dead for the process lifetime — `c1fba27a` (#386) shipped a client retry loop in the same
    commit as the deploy poll, and it is still on `main`. The real cost of starting web early is **2–30 s
    of inert console that self-heals**. Readiness is a correctness fix, not a rescue. §0.7b.1.
  - ⚠ **`Type=notify` is rejected although the owner decision names it first**, and the reason is
    `AudioEngineInitializationService` blocking the host on hardware (`C-204`, §1.4). **Say what the
    rejected option gives up** — the systemd-native answer, a readiness signal that cannot lie, and one
    meaning of `active` for all consumers.
  - **What readiness costs the operator:** `systemctl start radio-api` now blocks (≤20 s), and the dark
    window after a crash grows from ~15 s to ~16–18 s typical. §0.7b.4, measured by `S14`.
  - **`C-205` — the ceiling has a hard upper bound of ~50 s**, above which `radio-api` never latches and
    crash-loops forever. 20 s ships; `S15` is the gate.
  - ⛔ **One number this design depends on has no repository source** — the real exec→ready duration on the
    appliance (§0.7b.5 #1, §9.2 #12). It is measured in §5.3 step A and nowhere earlier. **Say so, rather
    than presenting 20 s as measured.**
- `docs/BUILDER_QUEUE.md`, `docs/queue/OPS-3.md`, `docs/queue/ORDERING-NOTES.md`, `docs/ROADMAP.md`,
  `docs/HANDOFF-GA-PUNCH-LIST.md` — ⛔ **not edited by this plan.** §10 carries the wording.
- `design/FUTURE-WORK.md` — add **§8.2, §8.3, `C-206` and `C-207`** as entries, per the project rule that
  stubbed and deferred work is documented rather than dropped.
  ⚠ **§8.1 is not a deferred item** — `Upholds=` was promoted into this row and must not be filed as future
  work. ⚠⚠ **§8.5 is no longer one either, and this changed on the second amendment** — readiness is Task 7,
  option 3 already shipped in `c1fba27a`, and `Type=notify` is rejected rather than deferred (`C-204`).
  ⛔ **Do not file §8.5 as a follow-up row; an earlier revision of this section told you to.**
  ⭐ **One genuinely new candidate row instead, and it is the honest residue of `C-204`:**
  *"`AudioEngineInitializationService` should be a `BackgroundService`, not a raw `IHostedService`."* It
  blocks the whole host on audio-engine init, three device enumerations, BT pre-warm and BlueZ bring-up
  (`:107-187`), with no timeout and every exception swallowed at `:182-186`. That is why `Type=notify` is
  unavailable here, and it makes the host's own `ApplicationStarted` mean nothing useful. **Live audio path,
  so it is not a drive-by** — its own row, its own UAT.

---

## 8. Deliberately not done

### 8.1 ~~`Upholds=` held in reserve~~ — ✅ **PROMOTED INTO SCOPE 2026-09-08. This section is history.**

⚠ **Kept rather than deleted, because the three reasons it gave for deferring were all reasonable and two
of them turned out to be wrong in an instructive way.** The original text deferred `Upholds=` because:

> **(a)** It inverts ownership — the dependency currently lives entirely in `radio-web.service`, and
> `Upholds=` would put half of it in `radio-api.service`. **(b)** Shipping an untested stop-propagation
> change together with an untested revival change means a bad outcome cannot be attributed to either.
> **(c)** The owner approved *this* tradeoff, described in these terms; quietly removing its cost is not
> obviously what he wants — a console that resurrects itself is closer to today's "keeps running" than to
> the honest failure the row asked for.

**How each held up:**

- **(a) stands, and became `C-201`.** The coupling does span two files now, and the half-rollback state is
  a real hazard — §5.2 and §6.2 are rewritten around it. It was a cost worth naming, not a reason to defer.
- **(b) was inverted by the rehearsal.** The concern was that two untested changes could not be attributed
  separately. The fixture attributes them *precisely* — `S6` (`Requires=`), `S1` (`BindsTo=`) and `S7`
  (`BindsTo=` + `Upholds=`) are three runs at the same timestamps, and each directive's contribution is
  visible on its own line. **The attribution problem was a property of not having a fixture, not of the
  change.**
- **(c) was the load-bearing error.** It reasoned that a self-healing console is "closer to today's keeps
  running" than to an honest failure — but today's failure mode is a UI that *lies*, and a console that
  goes dark and comes back does not lie at any point. ⭐ **The honest-failure goal never required the
  failure to be permanent**, and treating "needs a human" as a proxy for "honest" is what kept the better
  option in the reserve pile.

**The deferral was overturned by measurement, not by argument** — which is the case for the rehearsal in
one line.

### 8.2 The two `setup.sh` heredocs' broader staleness

`C-198`. They still say `User=radio` / `Group=radio` where the canonical units say `mmack`
(`radio-web.service:29-30`, `radio-api.service:35-36`), `Type=notify` for an API that is `Type=simple`, and
they carry none of the CPU-affinity, `Nice=`, memory-guard or `HOME=` blocks. They are unreachable in a git
checkout (they run only when `deploy/common/*.service` is missing), so this is latent rather than broken —
but a fallback that would install a unit the box cannot run is worse than no fallback. Wants its own row:
either delete them in favour of a hard failure, or regenerate them.

⚠ **Widened by the re-scope: there are FOUR heredocs, not two.** `Upholds=` goes on `radio-api`, so the two
`radio-api` heredocs (`deploy/debian-x64/setup.sh:165`, `deploy/raspberry-pi/setup.sh:274`) join the two
`radio-web` ones (`:209`, `:318`). Task 3 edits all four.

---

**`C-199` — ⚠ THE `Upholds=` RESULT IS VERSION-TRANSFERRED, NOT VERSION-VERIFIED. Measured on systemd 249;
the appliance runs 255.**

The 2026-09-08 rehearsal ran in `jrei/systemd-ubuntu:22.04` — **systemd 249**. The appliance is Ubuntu
24.04 / **systemd 255**. `Upholds=` was introduced in **249**, so the directive exists on both and will not
be silently ignored as an unknown key. **But six releases separate the measurement from the target, and
nothing in this plan establishes that the behaviour is identical across them.**

⭐ **Why this is a constraint and not a footnote:** `Upholds=` was new in the version that was measured.
A directive's first release is the one most likely to have had its semantics adjusted afterwards, and the
specific behaviours this row depends on — whether it reverses a deliberate stop (§0.4.5), how it interacts
with a start limiter (§0.5.1), how quickly it fires after the upholding unit becomes active (`C-202`) — are
exactly the kind of edges that get tuned between releases.

**Closing it, cheapest first:** (1) re-run `S7` in a **systemd 255** container (`ubuntu:24.04` base) — this
closes the gap without touching the appliance and should be done as part of §4; (2) the supervised box
session's §5.3 confirms the return direction on the real box. ⛔ **The row is not done until one of these
has run on 255**, and (1) does not remove the need for (2) — it just means the box session is confirming an
expected result rather than discovering a new one.

---

**`C-200` — ⚠⚠ MAY CHANGE THE WORK. `Upholds=` may make `radio-web` impossible to stop on its own, which
is what `Deploy-ToLinux.ps1:170` does on every deploy.**

§0.4.5's open question. If a deliberate `systemctl stop radio-web` is reversed while `radio-api` is active,
the deploy's stop path has a race in it — a resurrected `radio-web` running over `rsync --delete` — and the
web UI cannot be stopped independently for any purpose. `S8` measures it; Task 5 has a branch for each
answer; §0.11's prohibition on collapsing the two `stop` calls is conditional on it.

---

**`C-201` — ⚠ CHANGES THE ROLLOUT AND THE ROLLBACK. The coupling now spans two unit files, and half of it
is a defined bad state.**

`BindsTo=` lives in `radio-web.service`; `Upholds=` lives in `radio-api.service`. Three consequences:
**(a)** §5.2 installs both units and verifies both, in one step; **(b)** §6.2's rollback must restore both
files before `daemon-reload`, or the box lands in a half-rolled-back state; **(c)** the worst half is
*`Requires=` restored on web while `Upholds=` remains on api* — an appliance where `systemctl stop
radio-web` is silently undone by systemd and nothing explains why. §6.2 and §6.3 are rewritten to be atomic
across both files.

---

**`C-202` — ⚠⚠ MAY CHANGE THE WORK. `Upholds=` fires at the moment `Deploy-ToLinux.ps1`'s health-poll
exists to wait past, reintroducing a race the repository already fixed.**

Derivation in §0.7. `:445-451`'s comment records that `systemctl start radio-api` returns when the process
launches, not when its listener binds, and that starting `radio-web` at that moment left the visualization
hub *"dead for the lifetime of the radio-web process"*. `Upholds=` triggers on `radio-api` becoming active
— the launch moment — so it can start `radio-web` before the `curl … negotiate` poll succeeds, making the
poll decorative. **This is a regression in a fix that was already paid for, invisible on a fast box, and
surfaces as a symptom nobody would attribute to a unit file.** `S10` measures the window; §8.5 files the
durable fix.

⚠⚠ **RE-GRADED 2026-09-08 (2nd amendment) — the mechanism above is right, the SEVERITY is wrong, and it is
wrong in four documents.** §0.7b.1: `c1fba27a` (#386) shipped the deploy poll **and** a client-side
background retry loop in one commit. `AudioVisualizationHubService.cs:187-193` catches a failed initial
connect and arms `StartRetryLoop` (`:208`, back-off `{2,5,10,30}` s, replaying subscriptions on success);
`AudioStateHubService.cs:355-356` does the same. `git log -S 'StartRetryLoop'` returns that one commit.
**So the hub is not "dead for the lifetime of the process" — it self-heals in 2–30 s.** `C-202` is a
transient degradation, **not a blocking regression**, and the readiness fix is a correctness improvement
rather than a rescue. ⭐ **The comment's own trailing clause said so** — *"regardless of hub-service code
resilience"* — and this plan quoted it twice without reading it.

---

**`C-203` — ⚠⚠ CHANGES THE ORDERING AND THE ROLLOUT. `Upholds=` without readiness is the worst partial
state, and it is the one that looks fine.**

Tasks 1 + 2 without Task 7 gives a console that returns on every crash **against an API that may not be
listening yet**. Nothing errors, nothing is dark, and the client retry loop hides it — the console is simply
inert for 2–30 s each time and then works. §3's table forbids it as an intermediate commit; §5.2 step 4's
`ExecStartPost` check is the only place it is visible on the box; §5.3's `sub=start-post` row is how the
supervised session detects it. ⭐ **It is the state a silently-dropped `ExecStartPost=` line produces**, and
systemd drops unparseable `Exec*` lines without complaint.

---

**`C-204` — ⚠ CHANGES THE WORK. `Type=notify` is rejected on measured grounds, and the owner decision names
it first.**

§1.4. `AudioEngineInitializationService` (`src/Radio.API/Services/AudioEngineInitializationService.cs:19`)
is a raw `IHostedService` whose `StartAsync` (`:107-187`) blocks the host on audio-engine init, three device
enumerations, BT pre-warm and BlueZ bring-up, none of them bounded. .NET sends `READY=1` on
`ApplicationStarted`, i.e. after all of that — so `Type=notify` would mean something far later and less
predictable than "the hub answers", against an absent `TimeoutStartSec` (90 s default) and
`StartLimitBurst=5`. **It would manufacture the wedge this row exists to make rarer**, and it forfeits
`C-191`. ⚠ **A Builder reading only the queue row or the owner decision will reach for `Type=notify`** —
they name it first and this plan rejects it. The PR body must explain the deviation.

---

**`C-205` — ⚠ THE READINESS CEILING IS BOUNDED ABOVE BY THE START LIMITER, not by patience.**

§0.7b.4. Five start attempts must fit inside `StartLimitIntervalSec=300` for `radio-api` to latch; with each
cycle costing `RestartSec=10` plus the readiness wait, a ceiling above ~65 s means **it never latches and
crash-loops forever** — the console flapping dark and light indefinitely, which is worse than the wedge #467
deliberately produces. 20 s ships (2× the deploy poll's 10 s, the only figure with production standing);
`S15` tests that the limiter still latches with it in place. ⛔ **Never raise it past 50 s, and never adjust
it without re-running `S15`.**

---

**`C-206` — `curl` is installed by both `setup.sh` and by neither provisioning script, and the header
comment claims otherwise.**

`deploy/debian-x64/setup.sh:45` and `deploy/raspberry-pi/setup.sh:45` both `apt-get install` it.
`deploy/provision/packages.sh` does **not** — its three arrays (`:77`, `:80`, `:89-98`) omit it, though the
header at `:7` claims *"curl/wget/unzip"* are covered (it scopes them to the setup scripts). In practice
curl is certain: both setup scripts pipe `curl` to install .NET, `Deploy-ToLinux.ps1:452` runs it every
deploy, and it is `Priority: standard` on Ubuntu. **But every one of those call sites is a login shell, and
none is a systemd `Exec*` line.** §5.2 step 0 checks it once; Task 7's `exit 0` means its absence degrades
rather than breaks. ⚠ **Not fixed here** — adding curl to `FEATURE_PKGS` is a provisioning change and
belongs in its own row.

---

**`C-207` — cosmetic, recorded so it is not "fixed" mid-row: the repo uses both `/bin/bash` and
`/usr/bin/bash` in `Exec*` lines.**

`deploy/common/radio-performance.service:7` uses `/bin/bash`;
`deploy/provision/systemd/radio-api-restart.service:7` uses `/usr/bin/bash`. Both resolve under usrmerge.
Task 7 follows the **`ExecStartPost=` precedent** (`/usr/bin/bash`) because that is the closer analogue.
⛔ **Do not normalise the other one in this PR.**

### 8.3 `deploy/DEPLOYMENT.md:45`'s `LimitNICE` mismatch

Noticed while reading. The doc says `LimitNICE=-5:0`; `radio-api.service:48` says `LimitNICE=0:-5`, with a
comment at `:45-47` explaining that soft/hard ordering is exactly what makes it work. The doc has the two
halves the wrong way round. **Unrelated to this row and not fixed in it** — a correction buried in an
unrelated PR is how the stale counts in `docs/BUILDER_QUEUE.md`'s banner got that way. File it.

### 8.4 `Deploy-ToLinux.ps1:611`'s `start radio-api radio-web` hint

Naming both units is redundant under `BindsTo=` but not wrong, and it is correct for a box that has not had
the new units installed yet — which per `C-191` is every box, until someone does §5. Leave it.

### 8.5 ~~`radio-web` should wait for the API's readiness~~ — ✅ **PROMOTED INTO SCOPE 2026-09-08 (2nd amendment). This section is history.**

⚠ **Kept rather than deleted, in the same spirit as §8.1, because it listed three candidate fixes and one of
them had ALREADY SHIPPED — which nobody noticed for the length of two amendments.**

- **Option 1 (`Type=notify`) is now REJECTED on measurement, not deferred.** §1.4 and `C-204`:
  `AudioEngineInitializationService` blocks the host on hardware, so `READY=1` would mean something much
  later and unbounded than "the hub answers". The original text below calls it *"the systemd-native answer"*
  that *"makes `active` mean ready for every consumer"*. **That is true of `Type=notify` in general and
  false of it here**, and the difference is one `IHostedService` that should have been a `BackgroundService`.
- **Option 2 (`ExecStartPost=`) is TAKEN** — Task 7. The original text priced it as *"bigger than a
  unit-file line"*. It is one unit-file line.
- ⭐⭐ **Option 3 (client resilience) WAS ALREADY DONE, in the same commit as the poll.** The original text
  says it *"the existing comment hints was considered"*. It was not merely considered:
  `AudioVisualizationHubService.StartRetryLoop` and its twin in `AudioStateHubService` shipped in
  `c1fba27a` (#386) — the very commit the comment calls "Fix A". §0.7b.1. **The comment's trailing clause
  was the evidence and this plan quoted it twice while reading it as the opposite.**

⭐ **The generalisable lesson, and it is the third instance of this family in this row:** a comment describing
the bug its own commit fixed reads exactly like a live bug report. §0.4.4 caught reasoning from a directive's
*name*; §4.8 caught an instrument that read 0 either way; this one is **reasoning from a comment instead of
from the code it sits next to** — which is precisely the defect class `CLAUDE.md` § *Pre-Merge Review*
enumerates three shipped examples of. **Check the comment against the code, then check `git log -S` on the
thing the comment says is missing.**

*The original text follows, unedited.*

#### ~~8.5 (original) — `radio-web` should wait for the API's *readiness*, not its *launch* (`C-202`)~~

`Deploy-ToLinux.ps1:445-451` documents a race that "Fix A" solved by polling
`/hubs/visualization/negotiate` between starting `radio-api` and starting `radio-web`. **`Upholds=` fires
when `radio-api` becomes active — the launch moment — so it can start `radio-web` before that poll
succeeds, making the poll decorative** (§0.7).

**Not fixed in this row**, because every candidate fix is bigger than a unit-file line:

- `Type=notify` on `radio-api` with the app signalling `READY=1` once its listener is bound. This is the
  systemd-native answer and it makes `active` mean "ready" for *every* consumer, including `Upholds=` and
  `After=`. ⚠ It needs application code (`sd_notify`) and `radio-api.service` is currently `Type=simple`.
  Note the two `setup.sh` heredocs already *claim* `Type=notify` (`C-198`) — for a service that does not
  notify, which would hang until `TimeoutStartSec`. Anyone doing this must fix that at the same time.
- A `radio-api-ready.target` or an `ExecStartPost=` health-poll on `radio-api`, so readiness is a unit-level
  fact rather than a line inside one PowerShell script.
- Making `Radio.Web`'s SignalR client resilient to a not-yet-listening hub, which the existing comment
  hints was considered ("regardless of hub-service code resilience").

**File it as its own row.** `S10` sizes the window so the owner can judge urgency; a wide window on this
hardware makes it a follow-up that should be queued promptly rather than someday.

---

## 9. Self-review

### 9.0 ⭐ What the 2026-09-08 amendment changed, and what it did not

**Re-verified at `084a6bbd`:** `git log 278beefa..084a6bbd -- deploy/` is empty, so every line number in
§9.1 still resolves. Re-read directly at `084a6bbd`: `radio-web.service:3-4`, `radio-api.service:3-4` and
`:28-29`, `radio-console-open:176-182`, the four `setup.sh` heredoc dependency lines
(`debian-x64:165`/`:209`, `raspberry-pi:274`/`:318`), and `Deploy-ToLinux.ps1:170`/`:445-452`.

**Newly measured (not by this plan — recorded in the queue dossier):** §0.4.4's propagation timing,
§0.4.1's non-return, and the `BindsTo=` + `Upholds=` shape. systemd 249, privileged container.

**Newly derived and NOT measured — the three the amendment adds to §9.2:** §0.4.5 / `C-200`,
§0.5.1, and `C-202`.

⚠ **What the amendment deliberately did not touch**, because it survives the re-scope intact: §0.2's
`systemd.unit(5)` derivation, `C-191` (the deploy installs no unit files, which is what makes merging
inert), `C-192` (softened, not discharged — §0.4.2), `C-195`, `C-197`, §0.5's no-wedge derivation *for
`BindsTo=`*, and §6.4's power-cycle floor.

### 9.1 What was verified first-hand at `278beefa`

- `radio-web.service:3-4` — `After=network.target radio-api.service`, `Requires=radio-api.service`. The
  row's citation is exact.
- `radio-web.service:12-22` — the comment block the row points at, read in full. Its content is as the row
  describes.
- `radio-api.service:28-29` and `radio-web.service:23-24` — both `StartLimit*` blocks present in `[Unit]`,
  `300`/`5`, as PR #467 (`6f52f72e`) landed them. The row's dependency note is correct.
- `1b42ece9` — the #467-era commit that corrected the `Requires=` claim *in the unit file only*. It is why
  `deploy/DEPLOYMENT.md` still carries the un-caveated wording (`C-196`).
- `Deploy-ToLinux.ps1` read end to end: `:170` (stop order), `:452` (start order + `daemon-reload`),
  `:145`/`:443`/`:607-611` (`-NoRestart`), and the absence of any unit-file install (`C-191`).
- `deploy/debian-x64/setup.sh:156-251` and `deploy/raspberry-pi/setup.sh:265-359` — the only installers of
  the main units, plus the two stale heredocs (`C-198`).
- `deploy/debian-x64/kiosk/bin/radio-console-open` — `:92-93` probes, `:180-182` starters, `:223`
  `started_or_online`, `:230-250` `repair()` and its `failed` branch (`C-197`).
- `deploy/DEPLOYMENT.md:30` and `:731-732`; `deploy/provision/README.md:92-108`;
  `deploy/provision/systemd/radio-api-restart.service:6`.
- No test in the solution reads a unit file (§4.1), by grep across `tests/`.
- systemd behaviour: `Requires=`, `BindsTo=`, `PartOf=`, `Upholds=` from `systemd.unit(5)`; `Restart=`'s
  exclusion for `systemctl stop`-equivalent operations and its subordination to the start limiter from
  `systemd.service(5)`; `reset-failed`'s effect on the rate counter from `systemd.unit(5)`. All quoted
  verbatim in §0.2, §0.4.1 and §0.5.

### 9.2 ⚠ What could NOT be verified, and what it costs

1. ✅ **RESOLVED 2026-09-08 — and it was the bad answer.** *Whether `BindsTo=` propagates during
   `radio-api`'s `Restart=` back-off (§0.4.4).* It does. This was listed as *"the plan's largest risk and
   one of its two answers should stop the row"*, and that is exactly what happened: it stopped the row as
   scoped and forced the `Upholds=` re-scope. ⭐ **Kept in this list rather than deleted, because it is the
   plan's best evidence that the rehearsal was worth its 2 h** — the alternative was shipping it.
2. ✅ **RESOLVED 2026-09-08 — confirmed.** *That a unit stopped by `BindsTo=` propagation is not
   automatically restarted when the dependency returns (§0.4.1).* Measured `inactive / dead / success`,
   `NRestarts=0`. The derivation (from `Restart=`'s documented exclusion for stop-equivalent operations plus
   the absence of any documented return direction, corroborated by `systemd#2824`) was correct.
3. ⚠ **The empty-assignment list-reset idiom in §6.3.** Widely used, not verified here. `S11` tests it,
   now for `Upholds=` as well as `BindsTo=`; if it fails, §6.3 must be deleted rather than shipped.
4. ⚠ **That `radio` has passwordless sudo.** Asserted only by a comment in `radio-console-open:176-179`;
   no script in this repository provisions it, and project memory says the deploy *does* need a sudo
   password — the two may both be true (a targeted `NOPASSWD` rule for `systemctl` would satisfy both) but
   nothing in the tree settles it. §6.1 depends on it, §6.2 and §6.4 do not. **One command for a human,
   which touches nothing:** `ssh mmack@radio 'sudo -n true && echo passwordless || echo needs-password'`.

⭐ **New on 2026-09-08 — three unverified claims the re-scope introduced. All three are `Upholds=`
behaviours, and none of them was covered by the rehearsal that prompted the re-scope.**

5. ⚠⚠ **Whether `Upholds=` reverses a deliberate `systemctl stop` of `radio-web`** (§0.4.5, `C-200`). The
   man page's *"constantly restarting the unit if necessary"* carves out no exception for an operator's
   stop, and `S7`'s t=2→t=16 gap shows the retry loop is gated only by `BindsTo=`'s satisfiability. `S8`
   measures it. **Cost of getting it wrong: `Deploy-ToLinux.ps1:170` has a race over `rsync --delete`**, and
   Task 4's `DEPLOYMENT.md` note ships an operator-facing claim that is backwards.
6. ⚠⚠ **Whether `Upholds=`'s starts consume `radio-web`'s start-limit budget** (§0.5.1). §0.5's no-wedge
   derivation rests on *"a stop is not a start"*, which `Upholds=` does not satisfy — and the two units'
   burst values are identical, so the margin is exactly zero. `S9` measures it. **Cost of getting it wrong:
   the pair can wedge after all**, which is the row's own original question answered the other way.
7. ⚠ **Whether `Upholds=` fires before `radio-api`'s listener binds** (`C-202`). Follows from `Upholds=`
   triggering on `active`, which `Deploy-ToLinux.ps1:445-451` documents is the launch moment rather than the
   ready moment. `S10` sizes it. **Cost of getting it wrong: a fix the repository already paid for is
   silently undone**, surfacing as a dead visualization hub nobody attributes to a unit file.
8. ✅ **RESOLVED 2026-09-08 by the second rehearsal.** *That the `Upholds=` result transfers from systemd
   249 to 255* (`C-199`). Re-run on `jrei/systemd-ubuntu:24.04`, systemd **255.4-1ubuntu8.16** — the
   appliance's exact version — and it transferred unchanged. §5.3 still confirms on the hardware.
   ⚠ **The readiness work is unmeasured on any version**, which is what §4.11 exists for.

⭐ **New on 2026-09-08 (second amendment) — four more, all from the readiness work. Item 12 is the one that
cannot be closed anywhere but the appliance.**

9. ⛔⛔ **Whether `ExecStartPost=` holds `ActiveState=activating` until it completes.** ⭐ **The entire
    mechanism rests on it, and it is deliberately NOT derived here.** §0.4.4 is this plan's own record of
    reasoning from a directive's name and getting a confident wrong answer; §4.8 is its record of trusting
    an instrument that could not see the phenomenon. **`S12a` measures it before anything else.** Cost of
    getting it wrong: Task 7 does nothing, `C-203`'s state ships, and §1.4 must reopen with `Type=notify`
    as the only candidate.
10. ⚠ **Whether the `<4>` prefix actually reaches the journal at warning priority.** Derived from
    `radio-api.service:96-97` (`SyslogLevelPrefix=true`, `SyslogLevel=debug`) — plausible, standard, and
    unverified here. If it does not, the readiness give-up is **silent** on a box whose journal is already
    filtered to WARNING and above (`LOG-11`). `S13` checks it with `journalctl -p warning`.
11. ⚠ **Whether Task 7's `ExecStartPost=` line survives systemd's own quoting rules.** It nests double
    quotes inside single quotes inside a systemd `Exec*` value, and systemd **silently drops** what it
    cannot parse. `systemd-analyze verify` plus a non-empty `systemctl show -p ExecStartPost` is the check;
    §5.2 step 4 repeats it on the box because a drop is invisible everywhere else.
12. ⛔⛔ **How long `radio-api` actually takes from exec to `/hubs/visualization/negotiate` answering, on
    this appliance, on a cold boot.** ⭐ **This number does not exist in the repository and cannot be
    obtained from it.** The 20 s ceiling is sized at 2× the deploy poll's 10 s — the only figure with
    production standing — and **nobody has recorded whether that poll has ever reached its 20th
    iteration.** §0.7b.5 #1. **§5.3 step A is the only way to get it, and that is a supervised box session,
    not a Builder action.** Cost of getting it wrong: too small and readiness silently no-ops on exactly the
    slow boot it was written for; too large and `C-205`'s crash-loop bound is approached.

### 9.3 What would falsify this plan's central decision

⚠ **The original entry here read:** *"`S1` showing propagation on the first crash rather than on the limiter
trip … it means the change as scoped makes the appliance worse."* ⭐ **That is what `S1` showed, and the
plan's own falsification criterion did its job** — the row was re-scoped rather than shipped. Kept as the
record.

**What would falsify the decision as it now stands:**

- **`S7` failing to reproduce on systemd 255** (`C-199`). The re-scope rests entirely on `Upholds=` supplying
  the return direction; if it does not on the appliance's version, there is no shape left that does what the
  row wants, and the row should be closed as not-achievable rather than shipped in a weaker form.
- **`S9` showing `radio-web` latching after five api cycles** (§0.5.1). That would mean the pair can reach a
  state neither recovers from — the row's own original question — and the only fixes touch values #467 chose
  deliberately.

⚠ **`S8` and `S10` are not falsifiers.** A bad answer to either changes the work (a deploy line, a
follow-up row) without undermining the decision. Do not let a `S8` surprise be read as a reason to abandon
the shape.

⭐ **Added by the second amendment — two more falsifiers, and one non-falsifier that will look like one:**

- ⛔ **`S12a` showing that `ExecStartPost=` does not gate `active`.** It falsifies §1.4's *mechanism*, not
  the row: `BindsTo=` + `Upholds=` still works and was still approved. The row would ship without readiness
  (`C-203`'s state, understood and accepted) or wait for a `Type=notify` row. **Take it to the owner.**
- ⛔ **`S15` showing `radio-api` never latches `failed`.** A service that crash-loops forever is worse than
  one that wedges visibly, so this falsifies the *ceiling*, and possibly the whole readiness approach if no
  ceiling both fits the limiter and covers the real bind time. `C-205`.
- ⚠ **§0.7b.1's correction is NOT a falsifier**, and it will be mistaken for one. Finding that `C-202`'s
  harm was overstated is a reason to re-price the row's urgency, not to abandon readiness — §0.7b.2 lists
  four reasons that never depended on it, including the boot path, which has no poll at all. ⭐ **Do not
  let a corrected premise become a retracted decision.** This repository has already recorded two cases
  where a correction over-corrected (`docs/BUILDER_QUEUE.md`'s banner on `AUD-17`, and the "thirteen
  months" figure it repeated three times).

### 9.4 ⭐ NEW — anchors re-derived at `main` = `ec4f1490` (2nd amendment)

The plan was written against `278beefa`, re-scoped against `084a6bbd`. **`main` has since moved 7 commits
across 6 PRs to `ec4f1490`** (`fcca3d63`, `a529ccf7`, `6601ac15`, `a67c00ae`, `25dc7af4`, `57253256`,
`ec4f1490`).

⭐ **`git log 084a6bbd..ec4f1490 -- deploy/` is EMPTY.** Nothing under `deploy/` moved, which is why every
line number in this plan still resolves. Of the six PRs, only three touched anything this row cites, and all
three were docs: `a529ccf7` wrote this plan's first amendment; `6601ac15` and `57253256` edited
`docs/queue/OPS-3.md`.

**Re-read on the working tree and confirmed accurate, not inferred from the empty diff:**

| Anchor | Status |
|---|---|
| `radio-web.service:3` `After=`, `:4` `Requires=`, `:12-22` comment, `:23-24` `StartLimit*` | ✅ all exact |
| `radio-api.service:3` `After=`, `:4` `Wants=`, `:6-29` limiter block, `:28-29` `StartLimit*`, `:32` `Type=simple`, `:81` `ExecStart=`, `:82-83` `Restart=`/`RestartSec=`, `:96-97` `SyslogLevelPrefix`/`SyslogLevel` | ✅ all exact |
| `Deploy-ToLinux.ps1` — all **twelve** cited anchors (`:145`, `:170`, `:271`, `:314-353`, `:373-425`, `:443`, `:445-451`, `:452`, `:455-458`, `:584-585`, `:605`, `:607-611`) | ✅ **none moved** (file is 619 lines) |
| `setup.sh` heredocs — `debian-x64:165`/`:209`, `raspberry-pi:274`/`:318`, and the block extents `:205-245` / `:314-354` | ✅ all exact |
| `DEPLOYMENT.md:30`, `:731-732` | ✅ both exact |
| `ORDERING-NOTES.md:28` | ✅ exact — ⚠ **and still carries the ORIGINAL pre-approval, pre-re-scope text.** §10.3 has never been applied. |
| `HANDOFF-GA-PUNCH-LIST.md:1110` | ✅ exact |
| `ROADMAP.md:144` | ✅ exact |

⚠⚠ **NEW anchors this amendment introduces, all outside `deploy/` and therefore NOT covered by the empty
`deploy/` diff — a Builder rebasing must re-check these specifically:**
`src/Radio.API/Program.cs:15`, `:128`, `:182-183`, `:218`;
`src/Radio.API/Services/AudioEngineInitializationService.cs:19`, `:107-187`, `:127`, `:130`, `:142-143`,
`:162`, `:167`, `:171`, `:176`, `:180`, `:458`;
`src/Radio.Web/Services/Hub/AudioVisualizationHubService.cs:20-23`, `:187-193`, `:208-218`;
`src/Radio.Web/Services/Hub/AudioStateHubService.cs:31-34`, `:104-110`, `:355-356`, `:390`;
`src/Radio.Core/Constants/ApiPaths.cs:13`;
`src/Radio.API/Radio.API.csproj:21-36`; `Directory.Build.props:3`;
`deploy/debian-x64/setup.sh:45`, `:172`; `deploy/raspberry-pi/setup.sh:45`, `:281`;
`deploy/provision/packages.sh:7`, `:77`, `:80`, `:89-98`;
`deploy/provision/systemd/radio-api-restart.service:7`; `deploy/common/radio-performance.service:7`;
`deploy/common/radio-web.service:105`.

⚠ **`src/` moves on almost every PR**, and `AUD-1`, `AUD-12` and `TEST-2` all claim files in
`src/Radio.Infrastructure` and `src/Radio.Web`. **Re-derive the `src/` anchors at claim time**; the `deploy/`
ones have been stable across three amendments and two `main` advances.

---

## 10. Queue row wording

⛔ **This plan does not apply any of the following.** They are for whoever updates the tracking documents.

⚠ **`docs/BUILDER_QUEUE.md` and `docs/queue/OPS-3.md` already carry the 2026-09-08 rehearsal result** —
they were updated at `084a6bbd` and `43120eb8`. **§10.2 below is therefore a *further* amendment on top of
what is already there, not a replacement for it**, and §10.1 assumes the queue's own banner already says the
row was re-scoped. Read both files before applying anything here.

⭐ **Status of each target, checked on the working tree at `main` = `ec4f1490` (2nd amendment).** ⚠ **Two of
the five have never been applied, and one of them still asserts something the owner reversed.**

| Target | State | Action |
|---|---|---|
| `docs/BUILDER_QUEUE.md:6` (banner) | ✅ carries the owner decision, both rehearsals and the `NRestarts` correction | Add the readiness mechanism + the re-price when this amendment lands |
| `docs/BUILDER_QUEUE.md:36` (`OPS-3` row) | ⚠ carries the owner decision but the Plan cell still reads **"0.5 d + ~20 min supervised box session"** — two re-prices stale | §10.1 |
| `docs/queue/OPS-3.md` | ✅ carries both rehearsals and the owner decision | §10.2 |
| `docs/queue/ORDERING-NOTES.md:28` | ⛔ **NEVER APPLIED.** Still the original text: *"the only row exempt from the auto-merge-on-green policy… the owner confirms the tradeoff on the PR itself"*, and it still describes the row as *"switching `Requires=` to `BindsTo=`"* — the shape that was measured and rejected | §10.3, still pending |
| `docs/ROADMAP.md:144` | ⛔ **NEVER APPLIED.** | §10.4, still pending |
| `docs/HANDOFF-GA-PUNCH-LIST.md:1110` | ⛔ **NEVER APPLIED.** | §10.5, still pending |

⚠⚠ **`HANDOFF-GA-PUNCH-LIST.md` states the auto-merge exemption in TWO places and §10.5 addresses only
one.** `:1110` (the row's cell) and **`:1519`** — *"`OPS-3` does not auto-merge. It is the single row in the
queue exempt from the auto-merge policy."* ⭐ **Correcting one and leaving the other is the exact failure
`docs/BUILDER_QUEUE.md`'s banner records twice** — *"one corrected number beside one stale number is not
better than two stale ones, because it looks authoritative."* **Grep for every copy before declaring it
done.** §10.6 carries `:1519`.

### 10.1 `docs/BUILDER_QUEUE.md` § Queue — replacement for the `OPS-3` Plan cell

> [`OPS-3-bindsto-for-radio-web.md`](../design/plans/OPS-3-bindsto-for-radio-web.md) · **1.25 d + a ~45 min
> supervised box session** *(re-priced twice on 2026-09-08: first to 0.75 d for two unit files, four
> heredocs and the touch-icon task; then to 1.25 d for the readiness work — a third directive, a seventh
> task, four new rehearsal scenarios and a rebuilt fixture)* · ⭐ **READINESS: the mechanism is
> `ExecStartPost=` polling the negotiate endpoint, NOT `Type=notify`.** `Type=notify` sends `READY=1` on
> `ApplicationStarted`, which `AudioEngineInitializationService` (a raw `IHostedService`) blocks on audio
> engine + device enumeration + BlueZ bring-up — so it would report readiness far LATER than "the hub
> answers", unbounded, against a 90 s default `TimeoutStartSec` and `StartLimitBurst=5`. **It would
> manufacture the wedge the row exists to reduce, and it forfeits `C-191`.** ⚠ The owner decision names
> `Type=notify` first; the plan's §1.4 explains the deviation · ⚠⚠ **`C-202`'s harm was OVERSTATED in this
> row, in the dossier and in the owner decision.** `c1fba27a` (#386) shipped a client-side SignalR retry
> loop in the **same commit** as the deploy poll, and it is still on `main` — so a hub that loses the start
> race self-heals in **2–30 s** rather than staying *"dead for the lifetime of the radio-web process"*.
> Readiness is a correctness and boot-ordering fix, **not the rescue of a blocking regression** — the row is
> worth less than it looked, and is still right · ⚠ **The deploy's poll is a SOFT wait, not a gate**: the
> loop exits 0 whether or not the endpoint answered, so "belt and braces" means a second chance, not a
> guarantee · ⭐ **RE-SCOPED — `BindsTo=` alone is measured to make the
> appliance WORSE and does not ship.** It fires during `radio-api`'s ordinary 10 s back-off and never
> returns, so a transient crash darkens the console permanently. The shape that ships is **`BindsTo=` on
> `radio-web` + `Upholds=radio-web.service` on `radio-api`** — measured to fail jointly *and* return
> (`t=2 web=inactive`, `t=16 web=active`) · ⚠ **the gate is a systemd rehearsal, not the suite** — no test
> in this repo reads a unit file and CI has no systemd, so a green run is exactly as green with
> `Requires=`; **§4's `S1`+`S6`+`S7`+`S8`+`S9` are merge-blocking and their output goes in the PR body**,
> and `S8`/`S9` are questions `Upholds=` opened that nothing has yet answered · ⭐ **`Deploy-ToLinux.ps1`
> does NOT install unit files, so merging and deploying both leave the box on `Requires=`** — rollout is a
> separate human act (§5), and no "verified on the box" claim is possible from a deploy · ⚠ **`Upholds=`
> was measured on systemd 249; the appliance runs 255** (`C-199`) — confirm there before calling it done

### 10.2 `docs/queue/OPS-3.md` — replacement for the Plan row of the field table

> | Plan | [`design/plans/OPS-3-bindsto-for-radio-web.md`](../../design/plans/OPS-3-bindsto-for-radio-web.md) — **0.75 d + a ~30 min supervised box session**, re-priced 2026-09-08 for the `Upholds=` re-scope. ✅ **Merge approved by the owner 2026-09-07; the "must NOT auto-merge" line in the Detail below is discharged and is kept for the record.** ⚠ **That approval was given for `BindsTo=` alone, which the rehearsal then disproved** — the plan's §0.9 argues it transfers to the re-scoped shape because that shape is strictly better on the axis the owner weighed, and states the three new costs it does *not* cover. |

And append to § Detail, **after** the existing *Rehearsal result, 2026-09-08* section:

> **⚠ Further corrections from the plan, 2026-09-08.** (1) **`C-192` is softened, not discharged.** Under
> `BindsTo=` alone the recovery command recorded above left the console dark; under `BindsTo=` + `Upholds=`
> it works again, because an active `radio-api` upholds `radio-web`. It is still not the command to
> document: a `radio-web` whose *own* limiter has latched needs `reset-failed` too, so the runbook keeps
> `sudo systemctl reset-failed radio-api radio-web && sudo systemctl start radio-web`. ⭐ **And the runbook
> gains a line that did not exist before the re-scope: WAIT ~30 s first** — a briefly dark console is now
> the coupling working, not failing. (2) **The "check whether any deploy path assumes the two services
> restart independently" item is discharged, and the answer got more interesting.** `Deploy-ToLinux.ps1`
> stops web-then-api (`:170`) and starts api-then-web (`:452`) — both already the only orders `BindsTo=`
> permits — and it **does not install unit files at all**, so this change does not reach the box on a
> deploy. ⚠ **But `Upholds=` touches both paths:** the stop may be reversed while `radio-api` is still up
> (`C-200`), and the start-path health-poll at `:445-451` may be defeated because `Upholds=` fires at the
> API's *launch* rather than its *readiness* (`C-202`). Both are unmeasured and both are plan gates.
> (3) **`Requires=` is not replaced so much as widened** — `systemd.unit(5)` defines `BindsTo=` as *"in
> addition to the effects of `Requires=`"*, so nothing current is given up. (4) **The row now touches two
> unit files, and half of it is a defined bad state** (`C-201`): `Requires=` restored on the web unit while
> `Upholds=` remains on the api unit yields an appliance where `systemctl stop radio-web` is silently undone.
> Install and rollback are both atomic across the pair.

### 10.3 `docs/queue/ORDERING-NOTES.md:28` — replacement

> - **`OPS-3`'s auto-merge exemption is DISCHARGED — the owner reviewed and approved the merge on
>   2026-09-07.** ⚠ **That approval was given for plain `BindsTo=`, which the 2026-09-08 rehearsal then
>   disproved**; the row is re-scoped to `BindsTo=` + `Upholds=` and the plan's §0.9 argues why the approval
>   transfers and what it does not cover. ⚠ **The reason the exemption existed has not gone away and is now
>   a plan section rather than a blocker:** green gates cannot observe unit-file propagation, so the merge
>   gate is [the plan](../../design/plans/OPS-3-bindsto-for-radio-web.md) §4's systemd rehearsal
>   (`S1`+`S6`+`S7`+`S8`+`S9`), not the suite. ⭐ **What makes merging genuinely low-risk is that it changes
>   nothing on the box** — `Deploy-ToLinux.ps1` does not install unit files, so `radio` keeps `Requires=`
>   until a human runs the plan's §5.

### 10.4 `docs/ROADMAP.md:144` — replacement for the trailing sentence of the `OPS-3` cell

> ✅ **Merge approved by the owner 2026-09-07 — the auto-merge exemption is discharged.** ⭐ **Re-scoped
> 2026-09-08 after rehearsal: `BindsTo=` alone was measured to darken the console on every transient API
> crash and never return, so the row ships `BindsTo=` + `Upholds=` instead** — joint failure *and* automatic
> return. ⚠ The gate is the plan's §4 systemd rehearsal, not the test suite: nothing in CI can observe unit
> propagation. ⭐ Merging is inert on the box — `Deploy-ToLinux.ps1` installs no unit files, so rollout is a
> separate written procedure (plan §5) with the owner present. **📋 Queued, planned. 0.75 d + a ~30 min box
> session.**

### 10.5 `docs/HANDOFF-GA-PUNCH-LIST.md:1110` — the justification cell is wrong and should be replaced

⚠ **Not a status edit — a correction.** The cell currently reads *"Correct coupling is what lets a wedged
service recover itself instead of needing SSH into the cabinet."* `C-195`: `BindsTo=` does the opposite.

⭐ **The re-scope gives this correction an unusual ending, and the replacement should say so.** The
punch-list's stated purpose — a service that recovers itself instead of needing SSH — was **false of the
change as filed** and is **true of the change that will ship**, but only because the rehearsal forced the
scope to widen. The cell was describing a benefit the row did not yet buy.

> **Why it matters:** today a `radio-api` that trips its restart limiter leaves the Blazor UI running and
> answering on `:5002` with its backend gone — the appliance asserts an availability it does not have.
> `BindsTo=` makes that failure honest. ⚠ **`BindsTo=` alone does NOT make anything self-healing** — it
> propagates a stop and has no return direction, and it was measured on 2026-09-08 to fire during
> `radio-api`'s ordinary restart back-off, darkening the console on a *transient* crash and never bringing
> it back. **The row therefore ships `BindsTo=` together with `Upholds=radio-web.service` on `radio-api`,
> which supplies the return direction** — measured joint failure at t=2 and automatic return at t=16.
> ⭐ **So this cell's original claim is now true, but it was not true of the change as filed**: self-healing
> is bought by the second directive, not by correcting the first. A latched restart limiter still needs a
> person (the plan's §6 gives the touch-panel, keyboard and power-cycle routes).
> **Merge approved 2026-09-07; exemption discharged.** Estimate **1.25 d + a ~45 min supervised box
> session** — the earlier 2–3 h priced a one-word diff, not the systemd rehearsal that is now the merge
> gate and that is the only reason this row is not shipping a regression, and not the readiness work the
> owner added on 2026-09-08.

### 10.6 ⭐ NEW — `docs/HANDOFF-GA-PUNCH-LIST.md:1519`, the SECOND copy of the exemption

⚠ **§10.5 corrects `:1110` and misses this one.** The file says it twice; correcting one and leaving the
other reads as authoritative and is worse than leaving both.

> - **`OPS-3` no longer carries an auto-merge exemption — the owner reviewed and approved the merge on
>   2026-09-07, and unblocked the readiness question on 2026-09-08.** ⚠ **The reason the exemption existed
>   has not gone away, it has become a plan section:** no gate this repository can run observes unit-file
>   propagation, so the merge gate is the plan's §4 systemd rehearsal — `S1`, `S6`, `S7`, `S8`, `S9`,
>   `S12a`, `S12`, `S13`, `S15` — and not the suite. ⭐ What makes merging genuinely low-risk is that it
>   changes nothing on the box: `Deploy-ToLinux.ps1` installs no unit files, so `radio` keeps `Requires=`
>   until a human runs the plan's §5.

---

## Planned — 2026-09-07 · Amended — 2026-09-08 (twice)

**2026-09-08 amendment, in one paragraph.** The plan's §0.4.4 named one unmeasured behaviour as
merge-gating. It was rehearsed, and the answer disproved the row as specified: `BindsTo=` propagation fires
during `radio-api`'s ordinary 10 s restart back-off and the console never returns, so a transient crash —
the case `Restart=always` exists to absorb invisibly — would darken the appliance permanently. **`BindsTo=`
alone does not ship.** The row is re-scoped to `BindsTo=` on `radio-web` **plus `Upholds=radio-web.service`
on `radio-api`**, measured to give both directions. That doubles the unit files, doubles the heredocs, folds
in the previously-optional touch-icon task, re-prices the row from 0.5 d to 0.75 d, and opens three new
questions — whether `Upholds=` reverses a deliberate stop (`C-200`), whether its starts consume
`radio-web`'s start-limit budget (§0.5.1), and whether it defeats the deploy's health-poll (`C-202`) — plus
a two-file rollback hazard (`C-201`) and a version gap between the container that was measured and the
appliance that will run it (`C-199`). ⭐ **The rehearsal is the reason this row is not currently shipping a regression, and it caught it
before a single line was built.**

**Second 2026-09-08 amendment, in one paragraph.** A second rehearsal on the appliance's own systemd
(255.4) closed `C-199` and confirmed `C-200`, re-diagnosing it onto the *start* path, where it blocked the
row until the owner decided: keep both directives **and** give `radio-api` real readiness. This amendment
builds that — **`ExecStartPost=` polling `/hubs/visualization/negotiate`, not `Type=notify`**, because
`AudioEngineInitializationService` is a raw `IHostedService` that blocks the host on audio-engine init,
three device enumerations and BlueZ bring-up, so `READY=1` would arrive unbounded-ly later than "the hub
answers" and would manufacture the very wedge the row is trying to reduce (`C-204`). It adds Task 7, `S12`–
`S15`, `C-203`–`C-207`, a third directive to install and roll back, and re-prices the row to **1.25 d + a
~45 min box session**. ⚠⚠ **And it corrects the premise the whole readiness question was argued from:
`C-202`'s harm was overstated in four documents, including this plan.** `c1fba27a` shipped a client-side
SignalR retry loop in the *same commit* as the deploy poll it is named for, so a hub that loses the start
race self-heals in 2–30 s — it does not stay dead for the process lifetime. **Readiness is still right, for
four reasons that never depended on that claim** (§0.7b.2), one of which nobody had noticed: **the boot path
runs no poll at all.** ⭐ **Three times now this row has been rescued by refusing to trust something that
looked settled — a directive's name (§0.4.4), an instrument that read 0 either way (§4.8), and a comment
describing the bug its own commit fixed (§0.7b.1). None of the three was caught by reading more carefully;
each was caught by going to the source that the thing was a summary of.**

---

# ⛔ SCOPE NARROWED BY OWNER DECISION, 2026-09-08 — readiness is SPLIT OUT to `OPS-10`

**This row now ships `BindsTo=` on web + `Upholds=` on api, and nothing else.** Every readiness task
in this plan — `ExecStartPost=`, the `S12`/`S12a`/`S13`/`S14` rehearsals, the 20 s ceiling derivation
— **moves to `OPS-10`** ([`queue/OPS-10.md`](../../docs/queue/OPS-10.md)). The analysis is deliberately
left in place here rather than deleted, because it is the reasoning `OPS-10` inherits.

## Why the scope changed: `C-202`'s harm was false

⚠ **The premise that made readiness urgent does not hold.** This plan, its brief, the queue row and
two other documents all asserted that `Upholds=` starting `radio-web` early leaves the SignalR hub
*"dead for the lifetime of the radio-web process"*, quoting `Deploy-ToLinux.ps1:445-451`.

**That comment describes half of what shipped.** "Fix A" (`c1fba27a`, #386) was **two** fixes: the
deploy poll *and* a client-side retry loop. **The loop is still on `main`** —
`AudioVisualizationHubService.cs:20-23`, `:187-193`, `:208-218`, back-off `{2,5,10,30}`s, replaying
subscriptions, with a twin at `AudioStateHubService.cs:355-356`. `git log -S 'StartRetryLoop'` returns
exactly that one commit.

**So an early start costs 2–30 s of inert console that self-heals**, not a permanent outage. The
comment's own trailing clause said so — *"regardless of hub-service code resilience"* — and it was
quoted twice while being read backwards.

Readiness keeps four justifications that never depended on `C-202` (see §0.7b.2), **including one
nobody had noticed: the boot path runs no ordering poll at all**, so this has never been enforced
outside a deploy. But those make it a **worthwhile improvement, not a blocker** — which is why it is
its own row rather than a condition on this one.

## What this row must still carry

- ⚠ **`NRestarts` cannot instrument any of this** — `Upholds=` issues `start`, not `restart`, so it
  reads 0 whether or not web is cycled. §5.3's box drill **reintroduced it** and has been corrected to
  `ActiveEnterTimestamp` with a baseline/after pair. Do not put it back.
- **The wedge and its runbook command.** 5 API failures inside `StartLimitIntervalSec=300` latch
  `radio-api` and take web with it:
  `sudo systemctl reset-failed radio-api.service && sudo systemctl start radio-api.service`
- **Merging is inert** — `Deploy-ToLinux.ps1` does not install unit files (`C-191`). ⭐ **Narrowing to
  the unit files preserves that**, which is most of why this split is cheap.
- **Re-rehearse on the 255.4 image.** `C-199` is closed; there is no reason left to prefer 249.
- ⚠ **`ORDERING-NOTES.md:28` still says *"switching `Requires=` to `BindsTo=`"*** — the shape that was
  measured and rejected. §10.3–§10.5 were never applied. Fix as part of this row.
- **Build gate is 47 warnings / 0 errors**, equality with baseline — not zero, as an earlier draft said.

**Estimate returns to ~0.5 d + ~20 min supervised**, since the rehearsal rebuild and the readiness
tasks leave with `OPS-10`.
