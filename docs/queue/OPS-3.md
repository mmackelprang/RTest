# OPS-3 — `BindsTo=` for `radio-web` — make joint failure actually joint.

> Queue dossier for row **`OPS-3`** of [`BUILDER_QUEUE.md`](../BUILDER_QUEUE.md).
> The detail below was moved verbatim out of that row's Item cell on 2026-09-06; only
> whitespace, the table's `\|` escapes and docs-relative link prefixes changed.

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
