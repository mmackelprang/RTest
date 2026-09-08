# OPS-3 — `BindsTo=` for `radio-web` — make joint failure actually joint.

> Queue dossier for row **`OPS-3`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.
>
> ⚠ **Directional words in the prose were written when every row shared one file.**
> *above*, *below* and *this file* may now point across files — most often at
> [`BUILDER_QUEUE_ARCHIVE.md`](../BUILDER_QUEUE_ARCHIVE.md) or a sibling in this
> directory. They were left verbatim rather than reworded, which would be a content edit.

| Field | Value |
|---|---|
| Status | 📋 |
| Plan | _plan TBD (small in diff, large in blast radius — the plan should be mostly test/rollout, not code)_ |
| Spec / handoff | [`deploy/common/radio-web.service:12-22`](../../deploy/common/radio-web.service) (the gap, already documented by #467) · [`deploy/common/radio-api.service`](../../deploy/common/radio-api.service) (limiter rationale) |
| Depends on | — _(no row dependency; **PR #467 is merged**, so both `StartLimit*` blocks are on `main` to build on.)_ |
| Branch | `fix/systemd-bindsto-radio-web` |

## Detail

**`BindsTo=` for `radio-web` — make joint failure actually joint.** **⚠ This is a real change to production service coupling: own PR, owner review, and it is the one row in this tranche that must NOT auto-merge on green gates.** PR #467 made the systemd restart limiter functional — `StartLimitIntervalSec=300` / `StartLimitBurst=5`, correctly placed in `[Unit]` — on both units. It stopped deliberately short of this.

**The gap:** `deploy/common/radio-web.service:4` declares `Requires=radio-api.service`, which **does not couple the two lifecycles the way it appears to**. systemd propagates only an **explicit** stop/restart of `radio-api` across `Requires=`; a unit that deactivates **on its own** — exactly what `radio-api` does when it exhausts `StartLimitBurst` and lands in `failed` — is **not** propagated (`systemd.unit(5)`; `BindsTo=` is the dependency type that gives joint failure).

**So when `radio-api` trips its limiter, `radio-web` keeps running and serves a UI whose backend is gone.**

**The reasoning is already written down in-tree and does not need re-deriving — read `deploy/common/radio-web.service:12-22` first.** #467 states the gap, names `BindsTo=` as the fix, and records why it was scoped out. This row is the follow-through, not a rediscovery.

**The tradeoff being accepted is explicit:** the console going dark is a *worse-looking* failure than a UI with a dead backend, and a *more honest* one — that judgement is the owner's to confirm on the PR, which is why this does not auto-merge.

**Recovery command worth recording in the runbook**, because it becomes reachable exactly when the limiter starts latching and `Restart=always` will **not** clear a tripped start limiter on its own: `sudo systemctl reset-failed radio-api.service && sudo systemctl start radio-api.service`.

**Also in scope:** leave `After=` as-is, and check whether any deploy path assumes the two services restart independently — `Deploy-ToLinux.ps1` restarts them separately today, and `BindsTo=` changes what a `radio-api` failure does to an in-flight web deploy.

**Do not deploy or restart anything on the box as part of writing this row's plan;** the unit files are the deliverable, and rollout is the owner's call.

---

## ⭐ Rehearsal result, 2026-09-08 — the open question is answered, and it is the bad answer

Run in a **privileged systemd container on appserver** (`jrei/systemd-ubuntu:22.04`, systemd 249,
PID 1 = `systemd`, `is-system-running` = `running`), with stand-in units carrying the production
timings: `Restart=always`, `RestartSec=10`, `StartLimitIntervalSec=300`, `StartLimitBurst=5`. The
appliance was not touched. The container was destroyed afterwards.

The failure was induced with `systemctl kill --signal=SIGKILL rehearse-api`, i.e. an *unexpected*
stop — the only case `BindsTo=` treats differently from `Requires=`.

### S6 — the `Requires=` control

```
t=0   (baseline)      api=active      web=active
t=1   (api backoff)   api=activating  web=active
t=9   (api backoff)   api=activating  web=active
t=15  (api RECOVERED) api=active      web=active
```

Web never stops. This is the control that proves the fixture can see the difference at all.

### S1 — `BindsTo=`

```
t=0   (baseline)      api=active      web=active
t=1   (api backoff)   api=activating  web=inactive     ← fires during the ordinary back-off
t=9   (api backoff)   api=activating  web=inactive
t=15  (api RECOVERED) api=active      web=inactive     ← and never returns
```

`ActiveState/SubState/Result` = **`inactive / dead / success`** — a *clean stop*, which is precisely
why `Restart=` does not act on it. `NRestarts=0`.

**Both halves of the concern are confirmed:**

1. ⚠ **Propagation fires during `radio-api`'s ordinary 10 s `RestartSec` back-off.** So a single
   *transient* crash — the exact case `Restart=always` exists to absorb invisibly today — darkens
   the console.
2. ⚠ **The console does not come back when the API recovers.** It stays dark until someone
   intervenes.

**As specified, this row makes the appliance worse in its most common failure mode**, and does the
opposite of the purpose `HANDOFF-GA-PUNCH-LIST.md:1110` filed it for (`C-195`). `BindsTo=` alone must
not ship.

### ⭐ But the row's goal IS achievable — `BindsTo=` on web **plus `Upholds=` on api**

```
t=0   (baseline)      api=active      web=active
t=2   (api backoff)   api=activating  web=inactive     ← joint failure, as wanted
t=16  (api RECOVERED) api=active      web=active       ← AND it returns
```

`web: NRestarts=0, active running` — the limiter still never moves, so the no-wedge derivation
holds under this shape too.

`Upholds=` continuously restores the named unit while the upholding unit is active, which supplies
the return direction `BindsTo=` structurally lacks. Together they give **both** directions: the
console dies with the API and comes back with it.

⚠ **Caveat on the version.** Measured on **systemd 249** (the container). The appliance is Ubuntu
24.04 / **systemd 255**. `Upholds=` was introduced in 249, so it is present on both — but this
result was *not* measured on the appliance's version, and the supervised box session should confirm
it there before the row is called done.

### What this means for the row

The plan's §4.5 open question is discharged, and the answer redirects the row rather than closing
it. **Re-scope to `BindsTo=` + `Upholds=`**, keep the rehearsal fixture (it is cheap and it caught
this), and keep every finding about recovery commands and the touch icon — they still apply, because
the console can still be found dark during the back-off window even when it self-heals afterwards.
## ⭐ Second rehearsal, 2026-09-08 — on the appliance's own systemd version

Run in a second privileged container on appserver (`jrei/systemd-ubuntu:24.04`, **systemd
255.4-1ubuntu8.16** — the appliance's exact version), same stand-in units and production timings as
the 249 rehearsal: `Restart=always`, `RestartSec=10`, `StartLimitIntervalSec=300`,
`StartLimitBurst=5`. The appliance was not touched. Both containers were destroyed afterwards.

Three tests: `T1` closes the version caveat, `T3` probes the deploy interaction, `T2` measures the
wedge margin.

### T1 — `C-199` closed. `BindsTo=` + `Upholds=` behaves identically on 255

```
t=0   api=active     web=active
t=2   api=activating web=inactive     ← joint failure, as on 249
t=16  api=active     web=active       ← and it returns, as on 249
```

The version caveat recorded above is discharged: the 249 result transfers. **This is the only good
news in this section.**

### ⚠ T3 — `C-200` CONFIRMED. `Upholds=` reverses a deliberate stop, in under a second

```
>>> systemctl stop rehearse-web   (api left ACTIVE, as the deploy does)
t=1   api=active     web=active
t=3   api=active     web=active
t=20  api=active     web=active
```

Web is back **before the first sample at t=1** and never stays down. `Upholds=` is continuous, so
for as long as api is active, *no one can stop web and have it remain stopped.*

**⚠ Correcting my own earlier framing of this risk.** I recorded the hazard as the deploy's
`rsync --delete` running against a resurrected web. **That was wrong, and re-reading the script
disproves it.** `Deploy-ToLinux.ps1:170` stops web and then *immediately* stops api in the same
command — and stopping api propagates through `BindsTo=` and takes web down again. The rsync at
`:271` happens long after both are down. The resurrection window is sub-second and lands nowhere
near the file operations.

**The real interaction is on the START path, and it is worse.** `Deploy-ToLinux.ps1:452`:

```
systemctl start radio-api && for i in $(seq 1 20); do
  curl -sf -X POST http://localhost:5000/hubs/visualization/negotiate... && break || sleep 0.5
done && systemctl start radio-web
```

The comment at `:445-451` says exactly why that poll exists:

> `systemctl` returns when the process is launched (not when its listener is bound) so the previous
> "api && web" parallel-launch always lost the race on slow boxes: radio-web's SignalR client tried
> to negotiate before radio-api had opened port 5000, the initial `StartAsync` threw, and **the
> visualization hub stayed dead for the lifetime of the radio-web process** (pre-Fix A behavior).

`Upholds=radio-web.service` on api starts web the moment **api is active — which is at exec, not at
listener-bound.** So systemd starts web while the poll loop is still curling. The poll becomes
decorative, and **every deploy reintroduces the pre-Fix-A dead-visualization-hub bug the poll was
written to eliminate.** `After=` does not help: it orders web after api's start *job*, and that job
completes at exec for a `Type=simple` unit.

This is a genuine regression introduced by the `Upholds=` half of the re-scope, on the appliance's
most frequent operation. **It must be resolved before this row ships.**

Three candidate fixes, none rehearsed, and the choice is the owner's:

1. **Make api's readiness real** — `Type=notify` with `sd_notify`, or an `ExecStartPost=` that polls
   the negotiate endpoint. Then "api is active" means "the hub answers", `Upholds=` becomes safe,
   and the deploy's poll becomes redundant rather than defeated. This is the version that fixes the
   cause.
2. **Drop `Upholds=`** and obtain the return direction some other way, accepting that `BindsTo=`
   alone leaves the console dark (already measured, already rejected).
3. **Fix the client** — the deeper defect is that a failed initial `StartAsync` leaves the hub dead
   *for the process lifetime*; a reconnecting client would make the whole ordering question moot.
   The comment's own last clause concedes the poll works "regardless of hub-service code
   resilience", i.e. it is a workaround for this. Largest scope, best end state.

### T2 — the wedge, measured: the cliff is the 5th failure

Six SIGKILLs of api at 12 s spacing:

```
cycle 1  api=active   web=active     webNRestarts=0
cycle 2  api=active   web=active     webNRestarts=0
cycle 3  api=active   web=active     webNRestarts=0
cycle 4  api=active   web=active     webNRestarts=0
cycle 5  api=failed   web=inactive   webNRestarts=0
cycle 6  api=failed   web=inactive   webNRestarts=0
final api: signal failed failed
final web: success inactive dead
```

Five failures inside `StartLimitIntervalSec=300` exhaust api's `StartLimitBurst=5`; api latches
`failed`, and web stays down because its upholder is dead. **Both services down, no auto-recovery,
manual intervention required** — which makes the runbook command in this row's Detail section
load-bearing rather than a nicety:

```
sudo systemctl reset-failed radio-api.service && sudo systemctl start radio-api.service
```

**This is not a new wedge.** api's limiter is `main` today (PR #467) and `OPS-3` does not touch it.
What changes is the *consequence*: today web survives and serves a dead backend (`C-195`, the bug
this row exists to fix); after the change web goes dark with it. That is precisely the trade the row
states, now with a measured cliff — **5 failures in 5 minutes** — attached to it.

### ⚠ Instrument correction: `NRestarts` cannot see this, and my earlier derivation rested on it

`webNRestarts=0` at *every* cycle, including the ones where web was demonstrably stopped and
restarted four times. **`Upholds=` issues `start`, not `restart`, so `NRestarts` never increments.**

I previously reported the no-wedge property as settled on `NRestarts=0` from a single cycle. That
reading was **vacuous** — the counter reads 0 whether or not web is being cycled, so it cannot
distinguish the healthy case from the pathological one. It was never evidence.

The property does hold, but the derivation has to be structural rather than instrumental: **web is
started only when api goes active, so web's start count can never exceed api's failure count, and
api latches at 5.** Web therefore cannot reach its own `StartLimitBurst=5` before api has already
wedged. That is why the rehearsal never observed web's limiter move — not because it is immune.

### Status after this rehearsal

`C-199` closed. `C-200` confirmed and re-diagnosed onto the start path, where it is a blocking
regression. The wedge is quantified and is the row's stated trade rather than a surprise.

**`OPS-3` is not buildable as scoped.** It needs a decision on readiness (the three options above)
before the unit change can ship. Recorded rather than acted on.
