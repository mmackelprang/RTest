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
