# `PHN-7` — `BellHealthService` polls the transport that lies, so predictive-degrade cannot fire

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-08. Surfaced when the RotaryPhone session ratified the six-week-old `XR-5`
bell contract and found that **their own endpoint means two different things depending on transport.**

## The defect

`SystemStatus` is served over two transports and they are **not** the same data:

| Transport | `Ht801Reachable` | `Ht801LastCheckedUtc` |
|---|---|---|
| **SignalR** | a genuine 30 s background probe of the **resolved registrar binding** — the address INVITEs actually use | real timestamp |
| **REST** | a synchronous, in-request **ICMP ping of the CONFIGURED address** | **`DateTime.UtcNow`** |

**`BellHealthService` polls the REST path every 15 seconds.** Measured on the box, two calls 10 ms
apart:

```
wall clock          16:02:32.077
ht801LastCheckedUtc 16:02:32.0867356Z
ht801LastCheckedUtc 16:02:32.0968027Z
```

**It returns `now()`, every time.**

## Two user-visible consequences

1. **Our *"last checked 14:32"* sub-line renders the current time forever**, so it looks like it is
   working. A staleness indicator that can never go stale is worse than none — it actively asserts a
   freshness it has not checked.
2. ⭐ **Predictive-degrade sits on a ping of the CONFIGURED address.** RotaryPhone's own XML doc on that
   endpoint says it *"reported the CORRECT address throughout the entire 2026-07 outage while every
   INVITE went to a stale one."* **So as shipped, predictive-degrade would not have fired during the
   incident it exists to prevent.**

## What is ours and what is theirs

**Theirs (ADR written, not built):** converge the REST path onto the SignalR probe cache. **No wire
change, no field change, nothing for us to rebuild.**

**Ours:** decide whether to keep polling REST and wait for their convergence, or move
`BellHealthService` onto the SignalR path we already hold a connection to.

⚠ **Do not build the predictive-degrade rule against the REST signal in the meantime.** The rule is
ours and it is correct; the signal is theirs and it is wrong until convergence ships. RotaryPhone
ratified the same split: *"the rule is yours; the signal is ours, and ours is wrong."*

## ⚠ Two over-claims in their six-week-old bell reply — do not build on either

- Its §2 promises a *"30-second reachability probe"* behind the recovery guarantee. **True of the
  SignalR path only**, not the REST path we poll.
- Its §5 says `acknowledged` *"survives a service restart."* **It does not** — `BellFailureTracker` is
  in-memory and deliberately not persisted.

⚠ **Our `Q4` asked exactly this** — whether a nightly-restarting kiosk resurrects a dismissed bell note
— **and the answer we were given was wrong. It does.** Either treat the note as session-scoped and say
so in the UI copy, or ask them to persist it. ⛔ **That is an owner decision, not a Builder one.**

## Verification

The staleness half is directly testable: poll twice a second apart and assert `Ht801LastCheckedUtc`
does **not** advance by the wall-clock delta. **Must fail first** — today it advances on every call, so
confirm the test is RED against the live endpoint before trusting it.

The predictive-degrade half **cannot be verified here** — it needs a stale-registrar condition only
RotaryPhone can produce. Say so in the plan rather than writing a task that quietly assumes otherwise.

---

## ⭐ OWNER DECISION 2026-09-08 — the bell note is SESSION-SCOPED

> "treat the bell note as session-scoped"

**Do not ask RotaryPhone to persist `acknowledged`.** Their `BellFailureTracker` is in-memory and
deliberately not persisted; that behaviour stands as-is.

**What this row must now do instead:** make the UI copy honest about it. A dismissed bell note
**reappears after a restart**, and this box restarts nightly — so a note that says or implies
"dismissed for good" is asserting something untrue. Either the copy says the note is for this session,
or the affordance stops looking permanent. ⚠ **That is a Designer question, not a Builder one** if the
current copy implies permanence — check before writing the task.

RotaryPhone has been told not to build persistence on spec.

---

## ⛔ SUPERSEDED 2026-09-08 — `acknowledged` WILL be persisted. The session-scoped decision above is REVERSED.

**The owner decision recorded above ("treat the bell note as session-scoped") is no longer current.**
RotaryPhone's third handoff of 2026-09-08 reports the owner chose **persistence**, and the owner has
confirmed to this session that **their reading is the current one**.

⚠ **This was a genuine conflict between two repos, not a misreading.** This session recorded
session-scoped and told RotaryPhone not to build persistence on spec; RotaryPhone recorded persistence
and dispatched the work. Both were acting on what the owner had said to them. **It was caught because
the inbound reply stated its decision explicitly rather than assuming ours matched** — which is the ack
protocol earning its keep in the direction nobody designed it for.

### What this row must now do

- **Write the copy for a note that survives a restart.** ⛔ **Do NOT add session-scoped wording** — the
  correction above told this row to do exactly that, and it is now wrong.
- `BellFailureTracker` becomes durable on RotaryPhone's side: a dismissal will hold across the nightly
  restart, a crash **and** a deploy. **Work is dispatched on their side but has NOT shipped — do not
  build against it until they confirm.**
- Their six-week-old bell reply §5 claimed this was *already* true. It was not. **After their change
  ships the claim becomes right** — but it was wrong when we read it, and our `Q4` was the correct
  question to have asked.

### Still true and unchanged

The transport defect in this row's headline is untouched by any of the above. **`BellHealthService`
still polls the REST path, which returns `now()` and pings the configured address**, and their
REST→SignalR convergence is dispatched but not shipped. ⛔ **Do not build the predictive-degrade rule
until that lands** — they agree: *"your predictive-degrade rule becomes safe to build the moment this
lands, and not before."*

---

## ⭐ TASK ADDED 2026-09-08 — render a null `Ht801IpAddress` as "Unknown", not `--`

**Time-boxed: this must land BEFORE RotaryPhone deploys PR #77.**

In their PR #77, `SystemStatus.Ht801IpAddress` became **`string?`**. Previously the configured value
defaulted to `""`, so a null was structurally impossible and our parser has never seen one. It is now
**null at cold start** until the first background probe resolves an address, and stays null for the
process lifetime if none ever resolves.

Its **meaning** also changed: it now reports the **resolved** address — the one INVITEs actually go to —
rather than the **configured** one. That is the whole point of their fix, and it is the difference that
mattered in the 2026-07 outage, where the configured address was correct throughout while every INVITE
went somewhere stale.

**Our parser is already safe** — `Radio.Web/Models/ApiModels.cs:983` declares `public string?
Ht801IpAddress`. Nothing breaks.

⚠ **Our rendering is not.** `PhoneDashboardPanel.razor:63`:

```razor
HT801 &middot; @(SystemStatus?.Ht801IpAddress ?? "--")
```

**A null renders as `--`, which reads as absence.** Their doc comment: *render null as "Unknown", never
as "no HT801 configured."* The distinction matters — **null means we have not yet learned where the bell
is, not that there is not one.** As it stands, their semantic improvement would arrive on a panel that
converts "not yet resolved" into "no bell."

⭐ It is the same failure in miniature as everything else this week: **a display asserting a fact it has
not established.**

**Verification:** render the component with `Ht801IpAddress = null` and assert the visible text is
`Unknown`. **Must fail first** — today it renders `--`, so the test is RED against `main` by
construction. Confirm that rather than assuming it.
