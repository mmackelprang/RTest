# `OPS-10` — give `radio-api` a readiness signal, so "active" means "the hub answers"

[← Builder Queue index](../BUILDER_QUEUE.md)

🔵 **P3.** Split out of `OPS-3` by owner decision 2026-09-08, when the premise that made it urgent
turned out to be false. **The analysis already exists** — see `design/plans/OPS-3-bindsto-for-radio-web.md`,
whose readiness sections are this row's inheritance and should be moved here when it is planned.

## The gap

`radio-api` is a `Type=simple` unit, so systemd calls it **active at exec** — not when Kestrel has
bound port 5000 and the visualization hub answers. Anything keyed on "api is active" therefore fires
too early.

Two consumers care:

1. **`Deploy-ToLinux.ps1:452`** already works around it, polling
   `/hubs/visualization/negotiate?negotiateVersion=1` before starting `radio-web`.
2. ⭐ **The boot path does not.** Nothing polls on boot, so **the ordering has never been enforced
   outside a deploy** — and `OPS-3`'s `Upholds=` will start web at the API's exec on every boot.

## ⚠ What this is NOT — the harm was overstated and the correction is the point

`OPS-3` and three other documents asserted that starting web early leaves the SignalR hub *"dead for
the lifetime of the radio-web process"*, quoting `Deploy-ToLinux.ps1:445-451`.

**That comment describes half of what shipped.** "Fix A" (`c1fba27a`, #386) was **two** fixes: the
deploy poll **and a client-side retry loop that is still on `main`** —
`AudioVisualizationHubService.cs:20-23`, `:187-193`, `:208-218` (back-off `{2,5,10,30}`s, replays
subscriptions), twin at `AudioStateHubService.cs:355-356`.

**So the real cost of an early start is 2–30 s of inert console that heals itself.** The comment's own
trailing clause — *"regardless of hub-service code resilience"* — said as much, and was quoted twice
while being read backwards. **Do not restore the stronger claim.**

That is why this is P3 and its own row: a worthwhile correctness improvement, not a blocker.

## ⛔ `Type=notify` was investigated and rejected — do not re-propose it without reading this

.NET sends `READY=1` on `ApplicationStarted`, which fires only after **every** `IHostedService.StartAsync`
returns. `src/Radio.API/Services/AudioEngineInitializationService.cs:19` is a raw `IHostedService`, not
a `BackgroundService`, and its `StartAsync` (`:107-187`) awaits **inline**: audio-engine init, three
device enumerations, source activation, BT pre-warm, BlueZ adapter bring-up. None bounded; every
exception swallowed at `:182-186`. Kestrel binds *first* (implicit at `Program.cs:15`).

**So `READY=1` would arrive unboundedly LATER than "the hub answers"** — gated on hardware, against a
default `TimeoutStartSec=90` and `StartLimitBurst=5`. A slow BT adapter on a cold boot would not delay
the console; it would latch `radio-api` in `failed` and, with `OPS-3`'s `BindsTo=`, take the console
down with it. **It would manufacture the exact wedge `OPS-3` exists to reduce.**

It also forfeits `C-191` — that merging *and* deploying are inert — by moving half the change into the
binary, which reaches the box on a different schedule from the unit files. That yields a half-installed
state (`Type=notify` + a binary that never notifies → 90 s hang → crash loop). ⚠ **That trap is already
latent** at `deploy/debian-x64/setup.sh:172` and `raspberry-pi/setup.sh:281`.

**What rejecting it gives up**, stated honestly: the systemd-native answer; a signal that cannot lie
(an `ExecStartPost=` probe must `exit 0` on timeout, so a never-ready API is still eventually called
`active`); one meaning of `active` for every consumer; and any prospect of deleting the deploy poll.

## The shape that was chosen

**`ExecStartPost=` polling the negotiate endpoint** — character-for-character the probe
`Deploy-ToLinux.ps1:452` already runs. **Ready is defined as the narrowest thing that works:** a 2xx
from `POST /hubs/visualization/negotiate?negotiateVersion=1`. Explicitly **not** "the audio engine is up".

⚠ **The ceiling is bounded by the start limiter, not by patience.** Five attempts must fit inside
`StartLimitIntervalSec=300`, so `4 × (RestartSec + t_ready) ≤ 300` → `t_ready ≤ ~65 s`. Past that
`radio-api` **never latches and crash-loops forever**, which is worse than the wedge. The proposed 20 s
is 2× the deploy poll's own 10 s — the only figure with production standing.

## ⛔ What cannot be established without the box

**How long `radio-api` actually takes from exec to the hub answering, on this hardware, cold.** That
number does not exist in the repository, and **nobody has recorded whether the deploy poll has ever
reached its 20th iteration.** Measure it in the supervised session before fixing any ceiling; do not
write a task that assumes the answer.

Three more are container-closable: whether `ExecStartPost=` really holds `ActiveState=activating`
(⚠ **derive this by test, not from the directive's name** — `OPS-3`'s §0.4.4 records exactly that
mistake producing a confident wrong answer), the `<4>` syslog prefix (`radio-api.service:97` sets
`SyslogLevel=debug`, so an unprefixed echo is invisible to `journalctl -p warning`), and systemd's
quoting of a long `Exec` line — **systemd silently drops what it cannot parse**, so verify
`ExecStartPost` is non-empty on the box after install.

## Depends on

**`OPS-3`** — ships the coupling this row makes safe. Claiming this first is possible but pointless:
without `Upholds=` nothing starts web off the API's active edge except the deploy, which already polls.

⚠ **Not auto-mergeable.** Changes production service startup; rollout is a separate supervised step.
