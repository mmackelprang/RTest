# `KIOSK-3` — the launcher's VOICE row reads a field RotaryPhone is about to delete, and it was ours to catch

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1 — deploy-gating.** Filed 2026-09-09. ⛔ **Blocks the `rotary-phone` deploy the owner assigned
to us** (`RotaryPhone/docs/handoffs/2026-09-09-radioconsole-deploy-handoff.md`).

## The defect

`deploy/debian-x64/kiosk/bin/radio-console-open` — installed to `/usr/local/bin/` on the box — derives
the desktop launcher's **VOICE** row from `psidtsAgeSeconds` on RotaryPhone's `/api/gvbridge/status`:

```
:109-113  gv_psidts_age()   greps the field out of the status payload
:115-127  classify_voice()  age > 1200 → needsignin, else online
:650      prints it in the diagnostics line
```

**RotaryPhone's PR #79 removes that field** (absent from the payload, not `null`). After it deploys,
`gv_psidts_age()` returns empty and `classify_voice()`'s guard fires **every time**:

```sh
case "$age" in ''|*[!0-9]*) echo needsignin; return ;; esac
```

⭐ **The launcher would report VOICE = "needs sign-in" permanently, on every launch, regardless of GV's
actual state.** It fails **safe, not silent** — the author anticipated an absent field and wrote that
guard specifically so an empty value could not report a dead session as Online (`:118-123`), which is
good design. **But a permanently-wrong indicator is how an owner learns to ignore an indicator**, which
is worse than a missing one.

## ⛔ Do NOT ask RotaryPhone to keep the field. The bug is ours.

They offered — *"cheap to bring back and expensive to discover missing at 2am"* — and **we declined on
purpose.** `psidtsAgeSeconds` is the field **they proved dishonest**: an age-of-last-*load* clock, not
a PSIDTS clock, reading **608** inside the "healthy" band while their bridge was dead for 83 minutes on
2026-09-08. Keeping it leaves this launcher trusting a liar — **the exact defect `GV-12` existed to
remove.**

## What to build

Repoint `classify_voice()` at the honest predicate. ⭐ **Do not invent one — `GV-12` already shipped
and proved it** (`GvBridgeHealth.IsHealthy`, ADR-032):

- **Asymmetric by design:** *present-and-bad* makes it unhealthy; **absent contributes nothing.** That
  is what makes it survive a field being removed — the property this row exists because the old
  predicate lacked.
- `lastApiSuccessAt` **advances on a measured 60 s cadence** (measured on `radio` 2026-09-09T01:58–02:02Z
  and pinned by `MeasuredSixtySecondCadence_StaysHealthy`), so a ~2 min staleness gate is a 2× margin,
  not a guess.
- ⛔ **Do NOT latch `authBlackout`** — `GV-12` established latching is right for a banner and wrong for
  an edge; and it can be true for under a second (920 ms measured, zero true-samples across 411 polls).

**Verified live on the box 2026-09-09T14:14Z — the fields exist and are populated** on the *current*
(pre-#79) build:

```json
"cookiesValid": true, "lastApiSuccessAt": "2026-09-09T14:12:40Z",
"available": true, "degraded": false, "authBlackout": false, "psidtsAgeSeconds": 140
```

⚠ **`psidtsAgeSeconds: 140` is why this is urgent rather than theoretical** — the consumer works today
and breaks the moment #79 lands.

⚠ **This is `sh`, not C#.** No test project covers it. Parse with `grep`/`sed` as the file already does
— `:106-108` explains why it deliberately avoids `jq`: *"the launcher is the thing that runs when the
appliance is already unwell, so every dependency it does not need is one fewer way for it to fail."*
**Honour that.** Date arithmetic on `lastApiSuccessAt` in POSIX `sh` is the real work; `date -d` is
available on this box (GNU coreutils) but confirm before relying on it.

## ⛔ How this was found, and the lesson that outlives the row

**We told RotaryPhone three times that `psidtsAgeSeconds` had zero consumers**, once explicitly citing
a positive control as proof of method. **The grep was scoped to `src/`. This is a shell script.**

⭐ **A positive control validates the INSTRUMENT, never the SEARCH SPACE.** The grep worked perfectly;
it was pointed at the wrong half of the repo. RotaryPhone predicted this exact class — *"a grep does not
see a dashboard template, a saved query, an alert rule, or an operator runbook"* — and asked us to
**re-derive rather than re-assert**. They were right, and it would have silently broken the owner's
launcher.

⚠ **Consequence for every future cross-repo consumer claim: search `deploy/`, `tools/`, `docs/` and all
shell scripts, not `src/`.** State the scope searched alongside the claim.

## Verification

⚠ **The honest test is on the box, and it needs the NEW payload shape.** Until #79 deploys, the field
is still present, so a passing launcher proves nothing about the fix.

**Simulate first:** feed `classify_voice()` a payload with `psidtsAgeSeconds` removed and assert it does
**not** return `needsignin` when the honest fields say healthy. ⭐ **Must fail against today's script**
— confirm RED before trusting green.

Then, post-deploy: launch the desktop icon and confirm VOICE reads correctly against a *known* bridge
state, cross-checked against `curl localhost:5004/api/gvbridge/status`.

## Depends on

None to build. ⛔ **But it must LAND AND DEPLOY BEFORE RotaryPhone's #79**, per the owner's sequencing
decision: *fix our launcher first, then deploy both together*, so no window exists where VOICE is
permanently wrong.
