# `AUD-87` — owner ruling: what should overlapping announcements do?

[← Builder Queue index](../../docs/BUILDER_QUEUE.md)

✅ **RULED AND BUILT 2026-10-02.** Filed 2026-09-30 by the `AUD-73` Builder, on the second pre-merge reviewer's recommendation, as a decision row.

## Owner ruling, 2026-10-02

> *"#1 - a:keep, b:move to priority 9, c:hangup should only affect the phone announcement."*

| Question | Ruling | What was done |
|---|---|---|
| (a) Lower-priority overlap | **Keep** — mix | Nothing to change. Pinned by `AHangUpStopsThePhonesAnnouncementAndLeavesAConcurrentDoorbellPlaying` (a doorbell at 8 plays alongside the caller's name at 9) beside `AUD-73`'s `ALowerPriorityAnnouncementDoesNotCutOffAHigherOne`. |
| (b) Phone priority | **9** | `PhoneIntegrationOptions.AnnouncementPriority` and `src/Radio.API/appsettings.json` default to 9. No `deploy/*/appsettings.Production.json` seed carries the key. `RingPriority` was already 9. |
| (c) Hang-up scope | **Only the phone's announcement** | `PhoneCallIntegrationService` gives each ringing call its own `CancellationTokenSource` and passes the token to `AnnounceAsync` / `PlaySoundWithAnnouncementAsync`. `Ended` / `Idle` cancel that token instead of calling `IAnnouncementService.StopAsync`. The swap happens before the handler's first await, so a hang-up that arrives while the ring is still being broadcast still reaches the announcement. `AnnouncementService` now also refuses a caller-cancelled announcement before it ducks (and before phase 2 of sound + announcement), which keeps the stop path `StopAsync`'s stop-generation used to give a hang-up during synthesis. |

**Other callers of the stop-all path:** none. `PhoneCallIntegrationService` was the only production
caller of `IAnnouncementService.StopAsync`; it keeps its stop-everything behaviour and its tests, and
now has no production caller.

### ⚠ The box does not get (b) from this change

Read-only on `radio`, 2026-10-02: the SQLite config store (`/opt/radio-console/data/config/configuration.db`,
`Config_sqlite`) holds **`phoneintegration:announcementPriority = 8`** (written 2026-03-12), which outranks
both JSON layers. Until the owner changes that row, the box keeps 8. **The same store holds
`phoneintegration:enabled = true`**, and the file sink shows `PhoneCallIntegrationService` connecting to
the RotaryPhone hub — so the "`PhoneIntegration` is not enabled on the box" premise below is **wrong**.
Neither row was written.

⚠ **But whether the box ever announces a call is unproven, and probably it does not.** The pre-merge
reviewer found that the RotaryPhone server sends `CallStateChanged(phoneId, state)`
(`D:/prj/RotaryPhone/src/RotaryPhoneController.Server/Services/SignalRNotifierService.cs:260`), while
`PhoneCallClient` binds the two-argument form as `(state, phoneNumber)` (`PhoneCallClient.cs:65`, `:117`),
so the parser would see a phone id where it expects a state and map it to `Idle`. The box's seven
retained file-sink logs (read 2026-10-02) contain three `Phone call state:` lines, all `"Idle"`, and no
`Ringing` at all. So the point-2 collision is reachable in code but has not been seen on the box.
**Not fixed here** — it predates `AUD-87`, is a separate contract defect, and needs its own row (not
minted by this Builder: a concurrent Builder holds `PHN-11`, and the coordinator should take the next
number).

---

_The decision row as filed follows._

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
