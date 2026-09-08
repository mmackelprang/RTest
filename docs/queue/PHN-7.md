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
