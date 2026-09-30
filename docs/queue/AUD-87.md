# `AUD-87` — owner ruling: what should overlapping announcements do?

[← Builder Queue index](../BUILDER_QUEUE.md)

🟢 **Decision row. Nothing to build until ruled.** Filed 2026-09-30 by the `AUD-73` Builder, on the second pre-merge reviewer's recommendation.

## What `AUD-73` shipped ([#739](https://github.com/mmackelprang/RTest/pull/739))

`AnnouncementService` now arbitrates announcements that overlap. "Higher" means a larger priority number.

| A new announcement arrives while one is speaking | Result |
|---|---|
| **Higher** priority | It replaces the one speaking. |
| **Equal** priority | The one asked for later replaces the earlier one. |
| **Lower** priority | It plays **alongside** the one speaking, which keeps playing. |

- A replaced request returns `200 {"outcome":"interrupted"}`.
- The lower-priority row is the same mixing that every overlap did before `AUD-73`. The Builder left that one case as it was rather than decide it.

## Why it needs you

1. **Precedent points away from mixing.**
   - ADR-029 §6.2 calls two voices at once "unintelligible".
   - For the mirror case in attended playback, your ruling `D28` (2026-09-04) was **wait, then play**, not mix.
2. **The equal-priority rule creates a collision between callers.** Both of these default to **8**:
   - the phone's `PhoneIntegration:AnnouncementPriority` (`PhoneIntegrationOptions.cs`, `appsettings.json`);
   - the notification endpoint.

   If the ring sound is off or missing, a routine doorbell posted after an incoming call now **cuts off "Incoming call from …"**. `PhoneIntegration` is not enabled on the box today, so nothing can hit this yet.
3. **A phone hang-up silences everything.** `StopAsync` stops every announcement, not only the phone's. That includes a doorbell playing alongside.

## Options

| Option | Lower-priority overlap | Cost |
|---|---|---|
| **Keep as shipped** | Mixes | None. Two voices remain possible whenever the newcomer is less important. |
| **Queue (as `D28`)** | Waits, then plays | A queue across every `IAnnouncementService` caller (ADR-029 §6.2 rule 3's "separate work"). An announcement can arrive late, sometimes after it stops being useful. |
| **Drop** | Returns `interrupted` without playing | Cheap. The less important announcement is lost, and a doorbell during a phone-call ring is never heard. |

Separately:
- Should the phone's `AnnouncementPriority` sit above the notification default, for example 9, so a doorbell can never cut off a caller's name?
- Should a phone hang-up stop only the phone's own announcement?
