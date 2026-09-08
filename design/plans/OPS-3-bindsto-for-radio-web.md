# PLAN — `OPS-3` · `BindsTo=` for `radio-web` — one line of unit file, and the rollout that earns it

> **Row:** `OPS-3`, [`docs/queue/OPS-3.md`](../../docs/queue/OPS-3.md). Filed 2026-08-10 out of PR
> [#467](https://github.com/mmackelprang/RTest/pull/467)'s own pre-merge review.
> **Branch:** `fix/systemd-bindsto-radio-web`
> **Estimate:** **0.5 d** of build — of which **~2 h is a systemd rehearsal that is not optional** — **plus a
> ~20 min supervised session at the appliance**. §0.8 derives both.
> **✅ MERGE APPROVED BY THE OWNER 2026-09-07.** The standing *"must NOT auto-merge"* exemption is
> **discharged**; a Builder may merge on green gates. ⚠ **§0.9 is the part that did not change** — green
> gates cannot observe what this change does, which is why the exemption existed. The approval accepts the
> risk; it does not shrink it, and it authorizes **nothing on the box**.
> **Planned against** `main` at **`278beefa`**. Every line number below was read out of the tree at that
> commit.
> **Nothing on the box was touched while planning this.** No SSH, no `systemctl`, no deploy. Every claim
> below comes from the repository, from `git`, or from systemd's own manual pages, quoted verbatim where it
> matters. §9.2 lists the three claims that are none of those.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`deploy/common/radio-web.service:4` says `Requires=radio-api.service`. It reads like *"the web UI cannot
outlive the API"*, and it is not that. `Requires=` propagates only a **deliberate** stop or restart of
`radio-api`; a `radio-api` that dies on its own — the exact thing it does after PR #467 gave it a working
start limiter — is not propagated. So the terminal failure of the audio service leaves the Blazor UI up,
answering on `:5002`, painted on the kiosk, with every backend call behind it dead. The fix is one word.
**The whole of the rest of this plan is about the fact that the one word changes how two production
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
   oversight — there is no behaviour to document. This is the source of the largest new failure mode, and
   §0.4.1 is about it.

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

#### 0.4.1 ⚠⚠ The console goes dark and **stays** dark. Nothing brings it back on its own.

`BindsTo=` propagation enqueues an explicit **stop job** against `radio-web`. Two documented facts meet
here and the meeting is the whole problem:

- `systemd.service(5)`, on `Restart=`: *"the service will not be restarted if the exit code or signal is
  specified in `RestartPreventExitStatus=` … or the service is stopped with **systemctl stop** or an
  equivalent operation."* A propagated stop job **is** an equivalent operation. So `radio-web`'s
  `Restart=always` does not fight the propagation, and does not fire afterwards.
- `BindsTo=` has no recovery direction (§0.2.3). When `radio-api` returns, **nothing starts `radio-web`.**
  The systemd directive that would do that is `Upholds=` (v249+), and this row does not add it (§8.1).

So the resting state after the failure is: `radio-api` `failed`, `radio-web` `inactive`, **and it stays
that way until a person issues a start.** Today's resting state is `radio-api` `failed`, `radio-web`
`active`. The change trades a lying UI for a dark screen that needs a human. That is the deal, and it is
the right one for this appliance — but it must be written down in exactly those words, because the next
person to meet it at 11 p.m. will otherwise read a dark console as "the deploy broke everything".

#### 0.4.2 ⚠ The row's own recovery command becomes wrong the moment this lands

`docs/queue/OPS-3.md:32` records, for the runbook:

```bash
sudo systemctl reset-failed radio-api.service && sudo systemctl start radio-api.service
```

**After this change that command brings the API back and leaves the console dark**, for the reason in
§0.4.1 — starting `radio-api` propagates nothing to `radio-web`. An operator would run the documented
recovery, watch `radio-api` go green, look up at a black screen, and conclude the recovery failed. It is
recorded as `C-192`. The correct form, which Task 3 puts in the runbook:

```bash
sudo systemctl reset-failed radio-api radio-web && sudo systemctl start radio-web
```

`start radio-web` alone is sufficient and is the point: the dependency pulls `radio-api` in. `reset-failed`
covers both because `reset-failed` on a unit that is not failed is a harmless no-op, and an operator at a
dark panel should not be asked to work out which of the two needs it.

#### 0.4.3 What it does **not** create — stated so it is not over-claimed

- **A deliberate `systemctl stop radio-api` taking `radio-web` with it is not new.** `Requires=` already
  does that (§0.2's quote: *"explicitly stopped (or restarted)"*). `deploy/DEPLOYMENT.md:731-732` already
  documents it, and it is already true. **Not a delta.**
- **`systemctl restart radio-api` restarting `radio-web` is not new either**, for the same clause. This
  matters because `deploy/provision/systemd/radio-api-restart.service:6` (`systemctl restart
  radio-api.service`, daily) exists in-tree — but `deploy/provision/README.md:102-108` records it as
  **captured, not installed**, and disabled on the live box. Checked; **not a delta** even if it were
  enabled.
- **A restart loop between the two units is not reachable.** Propagation moves in one direction only
  (`radio-web` depends on `radio-api`, never the reverse), and it propagates a stop, which suppresses
  `Restart=`. There is no cycle to oscillate.

#### 0.4.4 ⚠⚠ The one behaviour that decides whether this change is good, and the man page does not settle it

`radio-api` has `Restart=always` / `RestartSec=10`. An ordinary crash is therefore not terminal: the
process dies, systemd waits 10 s, and starts it again. The question this plan cannot answer from
documentation is:

> **During that 10-second back-off, does `radio-api` count as "not in active state" for the purposes of
> `BindsTo=` + `After=`, and does `radio-web` therefore get stopped?**

The two possible answers are not close together:

| If propagation fires on **every** auto-restart cycle | If it fires only on a **terminal** stop (`failed` / limiter trip) |
|---|---|
| A single transient API crash — the kind that self-heals in 10 s today, unnoticed — **permanently darkens the console** until a human intervenes. | The change does exactly what the row wants and nothing more. |
| The appliance gets materially worse, and the owner would likely decline. | The tradeoff is the one that was approved. |

systemd holds a `Restart=`-ing service in the `activating (auto-restart)` sub-state rather than letting it
reach `inactive`, which is the mechanism by which the second answer would hold — but **this plan did not
verify that, and reasoning about it from sub-state names is exactly the error the row's own history warns
against.** `1b42ece9` exists because someone reasoned from `Requires=`'s name.

⛔ **This is not a question to settle on the box, and it is not a question to settle by merging and
watching.** §4 settles it in a disposable systemd instance before anything is installed anywhere, and §4.7
makes the answer a merge gate.

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
followed by `systemctl start radio-web` does not clear**, and §4.4 tests exactly that rather than trusting
this derivation.

⚠ **One adjacent case that is NOT a wedge but will be mistaken for one.** Five `systemctl restart
radio-api` inside 300 s propagates five *restarts* to `radio-web` (§0.4.3) and trips **`radio-web`'s own**
limiter, after which `start radio-web` is refused with *"start request repeated too quickly"* until
`reset-failed radio-web`. **That is true today under `Requires=` and is not caused by this change** — but a
Builder hand-testing the change is very likely to restart things five times in a row, hit it, and file it
as a `BindsTo=` regression. Recorded as `C-193`. §4.6 is the control that distinguishes them.

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

### 0.7 What this does to a deploy, and to `-NoRestart`

The row asks for this specifically. Both paths were read.

**Stop, `Deploy-ToLinux.ps1:170`:**

```
sudo systemctl stop radio-web 2>/dev/null; sudo systemctl stop radio-api 2>/dev/null; …
```

⭐ **`radio-web` is stopped first, so by the time `radio-api` stops there is nothing left to propagate to.
The deploy is already safe — but it is safe by accident.** Nothing in that line says the order matters, and
the two `stop`s are separated by `;` precisely so either can fail harmlessly. After this change the order
becomes load-bearing: reversed, or collapsed into `systemctl stop radio-api radio-web`, the second stop
would be acting on a unit systemd had already stopped underneath it. That is still *harmless* — it is a
no-op, not an error — but the comment must say why the order is what it is, or the next tidy-up will
collapse it. Task 4 adds that one line. There is **no functional change to the deploy's stop path**.

**Start, `Deploy-ToLinux.ps1:452:**

```
sudo systemctl daemon-reload && sudo systemctl start radio-api && for i in $(seq 1 20); do curl -sf … && break || sleep 0.5; done && sudo systemctl start radio-web
```

API first, health-poll, then web — already the only order `BindsTo=` permits, and `&&`-chained, so a
`radio-api` that fails to start short-circuits before `start radio-web` is reached. **No functional change.**
⚠ **One narrow new delta:** if `radio-api` starts and then dies *inside* the ~10 s poll window, today
`start radio-web` still succeeds and the deploy comes up serving a dead backend; after this change
`start radio-web` fails. Both paths end at the `is-active` check (`:455-458`) and `exit 1` (`:605`), so the
deploy's outcome is the same — it just reports the real reason sooner. Recorded as `C-194`.

⚠ **`daemon-reload` is on the *start* path, not the stop path.** That is worth knowing for §5: a deploy run
after the new unit is installed picks it up at `:452`, mid-deploy, at a moment when `radio-api` is stopped
and `radio-web` is stopped. Nothing propagates from a reload against two inactive units. Safe.

**`-NoRestart` (`:145`, `:443`, `:607-611`):** skips the stop, skips the start, and therefore skips
`daemon-reload` entirely. Binaries are replaced under two running services — already true today, unchanged
by this row. Its closing hint, `Start manually: sudo systemctl start radio-api radio-web` (`:611`), stays
correct: naming both is redundant under `BindsTo=` but not wrong. **No change needed, and none should be
made** — §8.4.

### 0.8 The estimate — **0.5 d**, and a supervised session that is not part of it

| Work | Cost |
|---|---|
| Tasks 1–4 (the unit line + comment rewrite, two heredocs, docs, one deploy comment) | 1 h |
| §4 the systemd rehearsal — build the fixture, run S1–S7, write the results into the PR body | **2 h** |
| Task 5 if the owner takes it (`radio-console-open`, one line + comment) | 0.5 h |
| PR, review, merge | 0.5 h |

**What holds it to half a day:** the diff is four lines of substance and the reasoning is already written
down — `radio-web.service:12-22` states the gap and names the fix, and `1b42ece9`'s commit message derives
it. Nothing here needs re-deriving.

⚠ **What is not in the number, and must not be folded into it: the box session.** Installing the unit,
confirming the loaded dependency, and running the one controlled kill-drill in §5.3 needs the owner
physically present and costs ~20 min plus a brief interruption to whatever is playing. It is his to
schedule.

📌 **`docs/HANDOFF-GA-PUNCH-LIST.md:1110` prices this row at 2–3 h.** That estimate is right for the diff
and does not price §4, which did not exist when it was written. The two are not in conflict; this plan
simply says what the other 2 h buys.

### 0.9 ✅ Merge posture — approved, and what the approval does and does not cover

The row, `docs/queue/ORDERING-NOTES.md:28`, `docs/ROADMAP.md:144` and
`docs/HANDOFF-GA-PUNCH-LIST.md:1110` all carry a standing *"exempt from auto-merge-on-green"* /
*"must NOT auto-merge"* marker. **The owner reviewed and approved the merge on 2026-09-07. The exemption is
discharged.** §10 carries the wording to update all four.

⚠ **The reason the exemption existed has not gone away, and the approval is not a substitute for it.** It
existed because *no gate this repository can run observes what this change does* — there is no unit test
for systemd propagation, CI has no systemd, and a green suite is exactly as green with `Requires=` as with
`BindsTo=`. What the approval means is that the owner has accepted that risk **on the strength of this
plan**. So:

**A Builder may:** merge the PR on green gates once §4's rehearsal has run and its S1/S6 results are pasted
into the PR body.

**A Builder must NOT, unattended, even with the merge approved:**

- ⛔ **Deploy to `radio`.** Not as verification, not as a smoke test, not because the change is small.
- ⛔ **Install the unit on the box**, by `cp`, by re-running `setup.sh`, or by any other route. §5 is a
  procedure for a human with the owner present.
- ⛔ **Run the §5.3 kill-drill**, or any `systemctl kill` / `stop` / `restart` against `radio-api`.
- ⛔ **Run §4's rehearsal on `radio`, or on the self-hosted CI runner** (`[self-hosted, linux, x64,
  appserver]`). It deliberately drives a service into a tripped start limiter; that belongs in a container
  nobody will miss.
- ⛔ **Merge before §4 has run.** The rehearsal is the gate, not a nice-to-have — §0.4.4 is unresolved
  until it does, and one of its two answers should stop this row.
- ⛔ **Write "verified on the box" for anything.** Per §0.6 a deploy does not install the unit, so a
  post-merge deploy proves the change is *absent*, not present.

### 0.10 ⚠ Eight constraints found while planning — numbering continues from `C-190` (`AUD-15`)

**`C-191` and `C-192` change the work. `C-193` and `C-197` are traps a Builder will otherwise walk into.
`C-195` is a claim in the row's own tracking documents that does not survive reading the code. `C-194`,
`C-196` and `C-198` are findings recorded so they are not rediscovered.**

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
worked. Task 3 records `reset-failed radio-api radio-web && systemctl start radio-web` instead. ⭐ **The row
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
so the file has carried the un-caveated version for a month.** Task 3 updates both to name `BindsTo=` and
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
`OPS-3` produces. Task 5 is a one-line fix; §1.2 recommends taking it and marks it as the owner's call.

---

**`C-198` — the two `setup.sh` heredoc fallbacks carry `Requires=` too, and they are already badly stale.**

`deploy/debian-x64/setup.sh:205-245` and `deploy/raspberry-pi/setup.sh:314-354` each write a unit inline,
but **only if `deploy/common/radio-web.service` is missing** — which in a git checkout it never is, so
neither has run in a long time. It shows: they still say `User=radio` / `Group=radio` where the canonical
unit says `mmack` (`radio-web.service:29-30`), and they carry none of the CPU-affinity or memory-guard
blocks. Task 2 updates the dependency line in both anyway, because leaving three copies of `Requires=` in
the tree guarantees the next person greps, finds one, and concludes the change did not land. **Their
broader staleness is out of scope and filed in §8.2.**

### 0.11 Things Builder must NOT do

- ⛔ **Everything in §0.9's list.** It is the operative one; this list does not repeat it.
- ⛔ **Do not touch `After=radio-api.service`** (`radio-web.service:3`). The row says leave it, and §0.2.2
  says why it matters that it is there — removing it would silently weaken `BindsTo=` out of the
  "even stronger" clause.
- ⛔ **Do not add `Upholds=`, `PartOf=`, or a `Wants=`/`Requires=` on the `radio-api` side** as a way of
  making the console come back by itself. §8.1 explains why not in this PR.
- ⛔ **Do not "fix" §0.4.1 by removing `radio-web`'s `StartLimit*` block, or by lengthening `RestartSec`.**
  Neither touches the propagation, and #467 paid for those values.
- ⛔ **Do not collapse `Deploy-ToLinux.ps1:170`'s two `stop` calls into one.** §0.7.
- ⛔ **Do not delete `deploy/provision/systemd/radio-web.service.d/10-dataprotection-home.conf`** or any
  other drop-in while in this file's neighbourhood. They are the documented fallback for a box whose main
  unit predates a fold (`deploy/provision/README.md:92-100`).
- ⛔ **Do not edit `docs/BUILDER_QUEUE.md`, `docs/queue/OPS-3.md`, `docs/queue/ORDERING-NOTES.md`,
  `docs/ROADMAP.md` or `docs/HANDOFF-GA-PUNCH-LIST.md` from this plan.** §10 carries the wording for
  whoever updates them.

---

## 1. Decision

### 1.1 The dependency form — replace `Requires=` with `BindsTo=`, keep `After=`, change nothing else

Four options were considered.

| Option | Verdict |
|---|---|
| **`BindsTo=radio-api.service`, `After=` unchanged** ✅ | **Taken.** It is a strict superset of the current behaviour (§0.2.1), it is the directive the documentation names for this exact case, `After=` is already present so the stronger clause applies, and it is one word. |
| `BindsTo=` **plus** `Upholds=radio-web.service` on `radio-api` | **Rejected for this PR, held in reserve.** It would make the console come back by itself and would erase §0.4.1 — but it is a second, larger coupling change, it inverts which unit owns the relationship, and pairing an untested propagation change with an untested revival change means a failure could not be attributed to either. §8.1. |
| Keep `Requires=`, add a health-check watchdog that stops `radio-web` when `/api/health` fails | **Rejected.** Replaces a one-word declarative dependency with a new moving part on a resource-constrained box, and re-implements badly what systemd already does correctly. |
| Do nothing; document the gap harder | **Rejected — that is what #467 already did.** `radio-web.service:12-22` is a good comment and it has not stopped the appliance from serving a UI whose backend is gone. |

The diff, `deploy/common/radio-web.service:4`:

```diff
-Requires=radio-api.service
+BindsTo=radio-api.service
```

### 1.2 The recovery affordance — **recommended: take Task 5**, and it is the owner's call

`C-197` is the finding that matters most for how this lands in practice. Without Task 5, `OPS-3`'s failure
mode requires SSH — on a box whose only link is WiFi, in the one situation where the audio service is
already unwell. With it, the existing **Radio Console** touch icon recovers the pair, unattended, with no
keyboard, in one tap.

**Recommendation: take it.** It is one line, in a script whose entire purpose is already *"repair what's
down, then open the kiosk"*, and it makes the change's cost recoverable by the person who will actually
meet it.

**Cost of the recommendation:** it edits a script that runs from the touch panel, so it widens this PR
beyond a unit file, and `reset-failed` is a slightly blunter instrument than `start` — it clears the
limiter that #467 added specifically so a crash loop would *stop* and be visible. ⚠ **That is a real
tension and it should be named on the PR:** a one-tap `reset-failed` lets an operator re-arm an infinitely
crashing service by tapping repeatedly. Mitigated by the fact that `radio-console-open` reports each
outcome on the glass and a genuinely broken service produces a red row every time — it does not hide the
failure, it just permits a retry.

**What declining gives up:** the dark console then needs SSH or a power cycle (§6.4) every time. That is
survivable — it is a home appliance, not a pager rotation — which is why this is presented as a choice
rather than folded in silently.

### 1.3 Where the reasoning lives

`radio-web.service:12-22` is currently a well-written comment explaining why the change was *not* made. It
must not survive as-is — a comment saying *"switching `Requires=` to `BindsTo=` is deliberately out of
scope"* sitting above a `BindsTo=` line is exactly the class `CLAUDE.md` § *Pre-Merge Review* exists for.
Task 1 replaces it with a comment that states what the new behaviour is, what it costs, and the recovery
command, so the operator-facing fact lives next to the directive that causes it.

---

## 2. Tasks

### Task 1 — the unit file, and the comment that must change with it

**File:** `deploy/common/radio-web.service`

Line 4:

```diff
-Requires=radio-api.service
+BindsTo=radio-api.service
```

Replace the comment block at `:12-22` — everything from `# Note what this deliberately does NOT do.` down
to `# production service coupling and is deliberately out of scope for this change.` — with:

```
# === Joint failure (OPS-3, 2026-09-07) — read this before changing the line above ===
# BindsTo=, not Requires=, and the difference is only visible when radio-api dies on
# its OWN. systemd.unit(5): Requires= propagates only an EXPLICIT stop or restart of
# the required unit. BindsTo= "in addition to the effects of Requires= ... also does
# so when a listed unit stops unexpectedly (which includes when it fails)" — which is
# exactly what radio-api does when it exhausts StartLimitBurst and lands in `failed`.
#
# Before OPS-3 that left radio-web RUNNING, serving a Blazor UI on :5002 whose API was
# gone: pages rendered, the kiosk held connections, and nothing on the appliance said
# the console was dead. A dark screen is a worse-looking failure and an honest one.
#
# After= on line 3 is load-bearing and must stay. Same man page: with After= present,
# "the unit bound to strictly has to be in active state for this unit to also be in
# active state."
#
# ⚠ WHAT THIS COSTS, because an operator meets it and not a developer: BindsTo=
# propagates a STOP, systemd.service(5) says Restart= does not act on a unit stopped
# by an equivalent-to-`systemctl stop` operation, and BindsTo= has NO return
# direction. So when radio-api recovers, radio-web DOES NOT come back on its own, and
# `systemctl start radio-api` alone leaves the console dark. Recovery is:
#
#   sudo systemctl reset-failed radio-api radio-web && sudo systemctl start radio-web
#
# `start radio-web` pulls radio-api in via this dependency; that is the point of
# naming only one unit. Upholds= (systemd v249+) would make radio-web return by
# itself and was deliberately NOT added here — see design/plans/OPS-3-bindsto-for-
# radio-web.md section 8.1.
#
# ⛔ Reverting is one word back to Requires=. Do NOT instead remove the StartLimit*
# block below or lengthen RestartSec — neither affects propagation, and PR #467 paid
# for those values.
# === end joint failure ===
```

⚠ **Leave `StartLimitIntervalSec=300` / `StartLimitBurst=5` (`:23-24`) exactly as they are**, and leave the
`# === Restart rate limit ===` block above them intact — this task replaces only the *second* comment
block, the one whose subject is the dependency.

---

### Task 2 — the two heredoc fallbacks stop disagreeing with the canonical unit

**Files:** `deploy/debian-x64/setup.sh`, `deploy/raspberry-pi/setup.sh`

`deploy/debian-x64/setup.sh:209` and `deploy/raspberry-pi/setup.sh:318` both read
`Requires=radio-api.service` inside a heredoc that only runs when `deploy/common/radio-web.service` is
absent. In each, replace that line with:

```
BindsTo=radio-api.service
# OPS-3: BindsTo=, not Requires= — a radio-api that dies on its own must take the UI
# with it rather than leaving a console whose backend is gone. Full rationale in
# common/radio-web.service, which is what actually gets installed; this heredoc is the
# fallback for a checkout that is missing it.
```

⚠ **Do not modernise the rest of these heredocs** — they still say `User=radio` and carry none of the
affinity or memory blocks (`C-198`). Fixing that is §8.2, not this row.

---

### Task 3 — the docs say what the coupling is, and the runbook gains the recovery that works

**File:** `deploy/DEPLOYMENT.md`

`:30`:

```diff
-`radio-web` has `Requires=radio-api.service` — stopping the API automatically stops the Web UI.
+`radio-web` has `BindsTo=radio-api.service` (OPS-3) — stopping the API stops the Web UI, and so does
+`radio-api` **failing on its own**. Before OPS-3 only a deliberate stop propagated, so a `radio-api` that
+tripped its restart limiter left a UI running against a dead backend. ⚠ `radio-web` does **not** come back
+by itself when `radio-api` recovers — see **Recovering from a tripped restart limiter** below.
```

`:731-732`:

```diff
-**Note:** When stopping, stop `radio-web` first (or let systemd handle it — stopping
-`radio-api` automatically stops `radio-web` due to the `Requires=` dependency).
+**Note:** When stopping, stop `radio-web` first (or let systemd handle it — stopping
+`radio-api` automatically stops `radio-web` due to the `BindsTo=` dependency). Starting is the
+other way round: `systemctl start radio-web` pulls `radio-api` in, so one unit name is enough.
```

Add immediately after the § *Service Management* fenced block (i.e. after `:733`). ⚠ **The outer fence
here is four backticks because the inserted text contains a fenced block of its own — insert the inner
content, not the outer fence:**

````markdown
### Recovering from a tripped restart limiter

Both units halt after 5 failed starts in 300 s (`StartLimitIntervalSec` / `StartLimitBurst`, PR #467) —
deliberately, so a crash loop is visible instead of silent. `Restart=always` does **not** clear that state.
Since OPS-3, a `radio-api` in that state also stops `radio-web`, so the symptom is a **dark console**, not
a broken one.

```bash
sudo systemctl reset-failed radio-api radio-web && sudo systemctl start radio-web
```

`start radio-web` pulls `radio-api` in via `BindsTo=`; naming both in `reset-failed` saves the operator
working out which one latched.

⚠ **`systemctl start radio-api` on its own is not enough** — it returns the API and leaves the console
dark, because `BindsTo=` propagates a stop and has no return direction.

At the appliance with no SSH: tap **Exit to Desktop**, then **Radio Console** — the launcher probes both
services and starts what is down. If it reports a red **AUDIO** row, the limiter is latched and the
command above is needed (a keyboard, or a power cycle, which clears the limiter counters).
````

---

### Task 4 — one comment on the deploy's stop order, which is now load-bearing

**File:** `deploy/Deploy-ToLinux.ps1`

Immediately above `:170` (inside the existing comment block, at its end, before the `ssh` line):

```powershell
  # OPS-3: the stop order is radio-web THEN radio-api, and it is now load-bearing rather
  # than incidental. radio-web BindsTo=radio-api, so stopping the API first would have
  # systemd stop the web unit underneath this line. That is harmless (a no-op, not an
  # error) but it makes the deploy's own actions invisible in the journal. Do not collapse
  # these into `systemctl stop radio-api radio-web`, and do not reorder them.
```

⛔ **No functional change to this file.** The `ssh` line itself is untouched.

---

### Task 5 — ⚠ DECISION REQUIRED: let the one-touch launcher clear a tripped limiter

**Take only if the owner agrees — §1.2. If declined, delete this task and note the decision in the PR
body.**

**File:** `deploy/debian-x64/kiosk/bin/radio-console-open`

Replace `start_audio()` / `start_console()` at `:180-181`:

```bash
# OPS-3: reset-failed BEFORE start, because the state this icon most needs to clear is the
# one `systemctl start` alone cannot. Both units halt after 5 failed starts in 300s (#467)
# and Restart=always does not clear that; `start` on a latched unit returns non-zero, which
# lands in repair()'s `failed` branch and puts a red row on the glass with nothing done.
# Since OPS-3 radio-web BindsTo=radio-api, so a latched radio-api is ALSO why the console is
# dark — the one dark-console cause this icon exists for was the one it could not fix.
# reset-failed on a healthy unit is a no-op, so this costs nothing on the common path.
# ⚠ It does mean an operator can re-arm an endlessly crashing service by tapping again. That
# is accepted: every attempt still reports its own outcome on the glass, so the failure stays
# visible — which is the property #467's limiter was protecting.
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

## 3. Ordering

Task 1 → Task 2 → Task 3 → Task 4 → Task 5 (if taken). None depends on another to compile; the order is
just so the canonical unit is right before anything is written that describes it.

**§4's rehearsal is independent of all of them and should be run FIRST**, before any file is edited. It can
falsify the row (§0.4.4), and discovering that after writing four files' worth of comments is wasted work.

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

**The real gate is §4.3–§4.6: a rehearsal against a real systemd, in a disposable instance.**

### 4.2 Where the rehearsal runs — ⛔ not the box, not the runner

**Preferred: WSL2 with systemd enabled** on the dev machine (`/etc/wsl.conf` → `[boot]` `systemd=true`).
It is a real systemd as PID 1, unit semantics are the stock ones, and Builder is already on Windows.
**Fallback:** any disposable Ubuntu VM, or a privileged container running `/sbin/init`.

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
```

⚠ **`RestartSec=10` and `StartLimitBurst=5` must match production exactly.** The whole question in §0.4.4
is about what happens *inside* the 10 s back-off; shortening it to make the rehearsal quicker removes the
thing being measured.

### 4.4 `S1` — ⭐ **the measurement that decides the row.** When does `ops3-web` stop?

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
| `ops3-web` stops **only after** the fifth failure, when `ops3-api` reaches `failed` | Propagation fires on terminal failure only. | ✅ **The row is good.** Proceed. |
| `ops3-web` stops **at the first crash**, within seconds, before any retry | Propagation fires on every `Restart=` cycle. | ⛔ **STOP. Do not merge.** One transient API crash permanently darkens the console. Take the result to the owner — this is a different product decision from the one that was approved, and §8.1's `Upholds=` option becomes a prerequisite rather than a follow-up. |
| `ops3-web` never stops | The fixture is not reproducing the mechanism. | ⚠ Fixture bug — `BindsTo=` typo, wrong unit, or `daemon-reload` not run. Fix before reading anything else. |

Then record the resting state:

```bash
systemctl is-active ops3-api ops3-web         # expect: failed / inactive
systemctl show ops3-web -p NRestarts -p ActiveState -p SubState
```

### 4.5 `S2`–`S4` — recovery, and the wedge probe

**`S2` — the row's command, which §0.4.2 predicts is insufficient:**

```bash
sudo rm -f /run/ops3-crash
sudo systemctl reset-failed ops3-api && sudo systemctl start ops3-api
systemctl is-active ops3-api ops3-web    # PREDICTED: active / inactive  ← console still dark
```

A result of `active / active` here **falsifies §0.4.1** and means `BindsTo=` has a revival direction this
plan says it does not. Say so loudly; Tasks 1 and 3's comments would both need rewriting.

**`S3` — the command Task 3 puts in the runbook:**

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

**`S6` — ⭐ the control, and it is not optional.** Rewrite `ops3-web.service` with `Requires=` instead of
`BindsTo=`, `daemon-reload`, and re-run `S1`.

```
Expect: ops3-api reaches `failed`, and ops3-web stays ACTIVE.
```

**If `ops3-web` stops under `Requires=` too, the fixture is not measuring what it claims** and every result
above is worthless. This run is what converts "web stopped" from an observation into evidence — it is the
same discipline `docs/BUILDER_QUEUE.md`'s banner records for counters: *verify the instrument against the
number you already believe before trusting it to produce a new one.*

**`S7` — the rollback drop-in (§6.3), because it uses an idiom this plan did not verify:**

```bash
sudo mkdir -p /etc/systemd/system/ops3-web.service.d
printf '[Unit]\nBindsTo=\nRequires=ops3-api.service\n' | sudo tee /etc/systemd/system/ops3-web.service.d/99-ops3-rollback.conf
sudo systemctl daemon-reload
systemctl show ops3-web -p BindsTo -p Requires   # expect: BindsTo= empty, Requires=ops3-api.service
```

Then re-run `S1` and confirm `ops3-web` survives. **If the empty-assignment reset does not clear
`BindsTo=`, §6.3's no-backup fallback does not work** and §6 must say so rather than offering it.

**Teardown:**

```bash
sudo systemctl disable --now ops3-web ops3-api 2>/dev/null
sudo rm -f /etc/systemd/system/ops3-{api,web}.service /usr/local/bin/ops3-{api,web} /run/ops3-crash
sudo rm -rf /etc/systemd/system/ops3-web.service.d
sudo systemctl daemon-reload && sudo systemctl reset-failed
```

### 4.7 Gates

| Gate | Requirement |
|---|---|
| `dotnet build --configuration Release` | 0 warnings. (Proves nothing about this row — §4.1.) |
| `dotnet test --configuration Release` | Green, minus the known-failing set `CLAUDE.md` names. (Same caveat.) |
| **`S1` + `S6`** | ⭐ **Merge-blocking.** Both results pasted into the PR body verbatim, with timestamps. |
| `S2`–`S5`, `S7` | Run and reported. A surprise in `S2`, `S4` or `S7` changes the plan text before merge. |
| Shell syntax, if Task 5 is taken | `bash -n deploy/debian-x64/kiosk/bin/radio-console-open` |
| Box verification | ⛔ **None, and none is claimed.** §0.6 / §0.9. |

---

## 5. Rollout — a written procedure for a human, not a Builder step

⛔ **Nothing in this section is a Builder action.** It runs with the owner present, at a time he chooses,
after the PR has merged.

### 5.1 Why it is a separate event

Per `C-191`, merging changes nothing on `radio` and neither does a deploy. The box keeps `Requires=` until
someone installs the new unit. That separation is the safety property this row leans on — take advantage of
it and do not rush the two together.

### 5.2 Install — five commands, and the first one is the rollback

```bash
# 1. THE BACKUP. Do this first. Section 6.2's one-line rollback depends on this file existing.
sudo cp /etc/systemd/system/radio-web.service /etc/systemd/system/radio-web.service.pre-ops3

# 2. Install the new unit from a checkout of main.
sudo cp deploy/common/radio-web.service /etc/systemd/system/radio-web.service

# 3. Apply it. This does NOT restart anything.
sudo systemctl daemon-reload

# 4. Confirm what systemd actually loaded — not what the file says.
systemctl show radio-web -p BindsTo -p Requires -p After
#    expect: BindsTo=radio-api.service / Requires= (empty) / After=... radio-api.service ...

# 5. Confirm nothing moved.
systemctl is-active radio-api radio-web    # expect: active / active
```

⚠ **Step 3 is safe against two running units.** `daemon-reload` re-evaluates dependencies; `radio-api` is
`active`, so the `BindsTo=` invariant is already satisfied and there is nothing to propagate.

⚠ **Prefer the targeted `cp` over re-running `deploy/debian-x64/setup.sh`.** The setup script installs
units, enables them, and does a good deal more besides; running the whole thing to change one line is a
much larger blast radius than the row has.

### 5.3 The acceptance test on the box — one controlled kill, owner present

This is the only place the change is observable on the real appliance, and it costs a brief interruption to
whatever is playing.

```bash
# One kill. radio-api's Restart=always brings it back in ~10s.
sudo systemctl kill -s KILL radio-api
sleep 15
systemctl is-active radio-api radio-web
```

| Result | Meaning |
|---|---|
| `active / active` | ✅ Expected, **if `S1` said propagation is terminal-only**. A transient crash is survivable and the console did not blink. |
| `active / inactive` | ⚠ Propagation fired on the auto-restart cycle — i.e. `S1`'s second branch, on real hardware. Recover with §6.1 and **roll back** (§6.2); this is the outcome that should not have reached the box. |

⛔ **Do not run this five times.** Five kills inside 300 s trips the limiter for real (`C-193`), and the
recovery then costs the owner his music for longer than the test is worth.

⛔ **Do not deliberately trip the limiter on the box to "test the dark console".** `S1` already measured
that in a container. There is nothing left to learn here that is worth a dark appliance.

### 5.4 Afterwards

Leave `/etc/systemd/system/radio-web.service.pre-ops3` in place for at least a week. It is 3 KB and it is
the difference between a one-line rollback and an editing session at a touch panel.

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

⚠ **It does not work if `radio-api`'s limiter is latched** — `C-197`. `systemctl start` on a latched unit
returns non-zero, and the launcher paints a red **AUDIO** row and stops. **And a latched `radio-api` is the
only thing that stops `radio-web` under this change**, so without Task 5 the affordance covers the easy
half and fails on the case `OPS-3` creates. **If Task 5 was taken, this step handles that case too and no
keyboard is needed at all.**

⚠ **It also depends on passwordless sudo**, which the launcher's own comment (`:176-179`) asserts the box
has and which **no script in this repository installs** — `grep` for `sudoers` / `NOPASSWD` across
`setup.sh`, `setup-kiosk.sh` and `provision.sh` returns nothing. It is box-only configuration. `C-196`'s
neighbour, recorded in §9.2 as unverified. If it is not configured, every start in the launcher fails red
and §6.2 is the only route.

### 6.2 Rollback — "undo OPS-3". Needs a keyboard, one line, no editor.

Exit the kiosk (§6.1 step 1), open a terminal from the GNOME desktop, then:

```bash
sudo cp /etc/systemd/system/radio-web.service.pre-ops3 /etc/systemd/system/radio-web.service \
  && sudo systemctl daemon-reload \
  && sudo systemctl reset-failed radio-api radio-web \
  && sudo systemctl start radio-web
```

**That is the whole rollback**, and it is one line precisely because §5.2 step 1 took the backup. The box is
back to pre-`OPS-3` behaviour — `radio-web` will once again survive a dead `radio-api`.

### 6.3 If the backup is missing — a drop-in, no editor, still one paste

```bash
sudo mkdir -p /etc/systemd/system/radio-web.service.d
printf '[Unit]\nBindsTo=\nRequires=radio-api.service\n' \
  | sudo tee /etc/systemd/system/radio-web.service.d/99-ops3-rollback.conf
sudo systemctl daemon-reload
systemctl show radio-web -p BindsTo -p Requires   # confirm BindsTo= is now empty
sudo systemctl reset-failed radio-api radio-web && sudo systemctl start radio-web
```

An empty assignment resets a list in systemd, so `BindsTo=` on its own clears the binding and the
`Requires=` line reinstates the old dependency. ⚠ **This plan did not verify that idiom against a running
systemd** — `S7` does, and if `S7` fails this subsection must be deleted rather than shipped as advice.

Undo the drop-in later with `sudo rm -rf /etc/systemd/system/radio-web.service.d/99-ops3-rollback.conf &&
sudo systemctl daemon-reload`.

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

- `deploy/common/radio-web.service` — Task 1.
- `deploy/debian-x64/setup.sh`, `deploy/raspberry-pi/setup.sh` — Task 2.
- `deploy/DEPLOYMENT.md` — Task 3 (`:30`, `:731-732`, and the new *Recovering from a tripped restart
  limiter* subsection).
- `deploy/Deploy-ToLinux.ps1` — Task 4, comment only.
- `deploy/debian-x64/kiosk/bin/radio-console-open` — Task 5, if taken.
- **PR body must carry a Docs Impact section**, and must state in plain words that the green suite is not
  evidence for this change (§4.1), that `S1`/`S6` are, and that **nothing was verified on `radio`**.
- `docs/BUILDER_QUEUE.md`, `docs/queue/OPS-3.md`, `docs/queue/ORDERING-NOTES.md`, `docs/ROADMAP.md`,
  `docs/HANDOFF-GA-PUNCH-LIST.md` — ⛔ **not edited by this plan.** §10 carries the wording.
- `design/FUTURE-WORK.md` — add §8.1 and §8.2 as entries, per the project rule that stubbed and deferred
  work is documented rather than dropped.

---

## 8. Deliberately not done

### 8.1 `Upholds=radio-web.service` — the follow-up that erases §0.4.1, held in reserve

`Upholds=` (systemd v249+; Ubuntu 24.04 ships 255, so it is available) is documented as: *"as long as this
unit is up, all units listed in `Upholds=` are started whenever found to be inactive or failed."* On
`radio-api`, it would bring `radio-web` back automatically when the API recovers, deleting this row's
largest new failure mode.

**Not done here for three reasons.** (a) It inverts ownership — the dependency currently lives entirely in
`radio-web.service`, and `Upholds=` would put half of it in `radio-api.service`. (b) Shipping an untested
stop-propagation change together with an untested revival change means a bad outcome cannot be attributed
to either. (c) The owner approved *this* tradeoff, described in these terms; quietly removing its cost is
not obviously what he wants — a console that resurrects itself is closer to today's "keeps running" than to
the honest failure the row asked for.

**File it, and revisit if §0.4.1 proves annoying in practice.**

### 8.2 The two `setup.sh` heredocs' broader staleness

`C-198`. They still say `User=radio` / `Group=radio` where the canonical units say `mmack`
(`radio-web.service:29-30`, `radio-api.service:35-36`), `Type=notify` for an API that is `Type=simple`, and
they carry none of the CPU-affinity, `Nice=`, memory-guard or `HOME=` blocks. They are unreachable in a git
checkout (they run only when `deploy/common/*.service` is missing), so this is latent rather than broken —
but a fallback that would install a unit the box cannot run is worse than no fallback. Wants its own row:
either delete them in favour of a hard failure, or regenerate them.

### 8.3 `deploy/DEPLOYMENT.md:45`'s `LimitNICE` mismatch

Noticed while reading. The doc says `LimitNICE=-5:0`; `radio-api.service:48` says `LimitNICE=0:-5`, with a
comment at `:45-47` explaining that soft/hard ordering is exactly what makes it work. The doc has the two
halves the wrong way round. **Unrelated to this row and not fixed in it** — a correction buried in an
unrelated PR is how the stale counts in `docs/BUILDER_QUEUE.md`'s banner got that way. File it.

### 8.4 `Deploy-ToLinux.ps1:611`'s `start radio-api radio-web` hint

Naming both units is redundant under `BindsTo=` but not wrong, and it is correct for a box that has not had
the new unit installed yet — which per `C-191` is every box, until someone does §5. Leave it.

---

## 9. Self-review

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

1. ⚠⚠ **Whether `BindsTo=` propagates during `radio-api`'s `Restart=` back-off (§0.4.4).** Not stated by
   either man page. **This is the plan's largest risk and one of its two answers should stop the row.**
   `S1` settles it, and §4.7 makes it merge-blocking. Cost of getting it wrong: a transient API crash
   permanently darkens the console.
2. ⚠ **That a unit stopped by `BindsTo=` propagation is not automatically restarted when the dependency
   returns (§0.4.1).** The man pages are silent; the conclusion is derived from `Restart=`'s documented
   exclusion for stop-equivalent operations plus the absence of any documented return direction, and is
   corroborated by systemd's own issue tracker (`systemd#2824`, *"Restart= in combination with BindsTo=
   ineffective on device rediscovery"*) — which is community evidence, not documentation. `S2` measures it
   directly. Cost of getting it wrong: Tasks 1 and 3 would document a recovery step that is not needed —
   harmless, but wrong.
3. ⚠ **The empty-assignment list-reset idiom in §6.3.** Widely used, not verified here. `S7` tests it; if
   it fails, §6.3 must be deleted rather than shipped.
4. ⚠ **That `radio` has passwordless sudo.** Asserted only by a comment in `radio-console-open:176-179`;
   no script in this repository provisions it, and project memory says the deploy *does* need a sudo
   password — the two may both be true (a targeted `NOPASSWD` rule for `systemctl` would satisfy both) but
   nothing in the tree settles it. §6.1 depends on it, §6.2 and §6.4 do not. **One command for a human,
   which touches nothing:** `ssh mmack@radio 'sudo -n true && echo passwordless || echo needs-password'`.

### 9.3 What would falsify this plan's central decision

`S1` showing propagation on the first crash rather than on the limiter trip. That result does not mean
"adjust the plan"; it means the change as scoped makes the appliance worse, and the owner's approval was
given for a different behaviour than the one that would ship.

---

## 10. Queue row wording

⛔ **This plan does not apply any of the following.** They are for whoever updates the tracking documents.

### 10.1 `docs/BUILDER_QUEUE.md` § Queue — replacement for the `OPS-3` Plan cell

> [`OPS-3-bindsto-for-radio-web.md`](../design/plans/OPS-3-bindsto-for-radio-web.md) · **0.5 d + a ~20 min
> supervised box session** · ✅ **auto-merge OK — the owner approved the merge 2026-09-07 and the standing
> exemption is discharged**; ⚠ **but the gate is a systemd rehearsal, not the suite** — no test in this
> repo reads a unit file and CI has no systemd, so a green run is exactly as green with `Requires=`;
> **§4's `S1`+`S6` are merge-blocking and their output goes in the PR body** · ⭐ **the plan found that
> `Deploy-ToLinux.ps1` does NOT install unit files, so merging and deploying both leave the box on
> `Requires=`** — rollout is a separate human act (§5), and no "verified on the box" claim is possible
> from a deploy · ⚠ **the row's own recovery command becomes wrong**: `start radio-api` alone leaves the
> console dark, because `BindsTo=` propagates a stop and has no return direction

### 10.2 `docs/queue/OPS-3.md` — replacement for the Plan row of the field table

> | Plan | [`design/plans/OPS-3-bindsto-for-radio-web.md`](../../design/plans/OPS-3-bindsto-for-radio-web.md) — **0.5 d + a supervised box session**. ✅ **Merge approved by the owner 2026-09-07; the "must NOT auto-merge" line in the Detail below is discharged and is kept for the record.** |

And append to § Detail:

> **⚠ Corrections from the plan, 2026-09-07.** (1) **The recovery command recorded above is wrong for the
> world this row creates.** `reset-failed radio-api && start radio-api` returns the API and leaves
> `radio-web` `inactive`, because `BindsTo=` propagates a *stop*, `Restart=` does not act on a propagated
> stop, and `BindsTo=` has no return direction. Use
> `sudo systemctl reset-failed radio-api radio-web && sudo systemctl start radio-web`. (2) **The
> "check whether any deploy path assumes the two services restart independently" item is discharged:**
> `Deploy-ToLinux.ps1` stops web-then-api (`:170`) and starts api-then-web (`:452`), both already the only
> orders `BindsTo=` permits — but it **does not install unit files at all**, so this change does not reach
> the box on a deploy. (3) **`Requires=` is not replaced so much as widened** — `systemd.unit(5)` defines
> `BindsTo=` as *"in addition to the effects of `Requires=`"*, so nothing current is given up.

### 10.3 `docs/queue/ORDERING-NOTES.md:28` — replacement

> - **`OPS-3`'s auto-merge exemption is DISCHARGED — the owner reviewed and approved the merge on
>   2026-09-07.** ⚠ **The reason it existed has not gone away and is now a plan section rather than a
>   blocker:** green gates cannot observe unit-file propagation, so the merge gate is
>   [the plan](../../design/plans/OPS-3-bindsto-for-radio-web.md) §4's systemd rehearsal (`S1` + `S6`),
>   not the suite. ⭐ **What makes merging genuinely low-risk is that it changes nothing on the box** —
>   `Deploy-ToLinux.ps1` does not install unit files, so `radio` keeps `Requires=` until a human runs the
>   plan's §5.

### 10.4 `docs/ROADMAP.md:144` — replacement for the trailing sentence of the `OPS-3` cell

> ✅ **Merge approved by the owner 2026-09-07 — the auto-merge exemption is discharged.** ⚠ The gate is
> the plan's §4 systemd rehearsal, not the test suite: nothing in CI can observe unit propagation.
> ⭐ Merging is inert on the box — `Deploy-ToLinux.ps1` installs no unit files, so rollout is a separate
> written procedure (plan §5) with the owner present. **📋 Queued, planned.**

### 10.5 `docs/HANDOFF-GA-PUNCH-LIST.md:1110` — the justification cell is wrong and should be replaced

⚠ **Not a status edit — a correction.** The cell currently reads *"Correct coupling is what lets a wedged
service recover itself instead of needing SSH into the cabinet."* `C-195`: `BindsTo=` does the opposite.

> **Why it matters:** today a `radio-api` that trips its restart limiter leaves the Blazor UI running and
> answering on `:5002` with its backend gone — the appliance asserts an availability it does not have.
> `BindsTo=` makes that failure honest. ⚠ **It does not make anything self-healing** — it propagates a
> stop and has no return direction, so the console stays dark until a person acts (the plan's §6 gives
> the touch-panel, keyboard and power-cycle routes). **Merge approved 2026-09-07; exemption discharged.**
> Estimate **0.5 d + a supervised box session** — the earlier 2–3 h priced the diff, not the systemd
> rehearsal that is now the merge gate.

---

## Planned — 2026-09-07
