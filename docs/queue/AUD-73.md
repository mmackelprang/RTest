# `AUD-73` — a second announcement does not stop the first

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2 pending a by-ear check.** Filed 2026-09-28 from the Phase 2b Builder's out-of-scope findings (code read, not observed).

`AnnouncementService.SetActiveSource` (`src/Radio.Infrastructure/Audio/Services/AnnouncementService.cs`, called at `:51`, `:134`, `:156`) cancels a `CancellationToken` that no playback path reads. So by code read a new announcement arriving while one is speaking does not interrupt it — both play, two voices in the room. `NotificationsController`'s comment was corrected to say so in #696 rather than claim preemption.

**First task:** confirm by ear — send two long test notifications a second apart. If two voices overlap, promote to P1 (it is the same "two sounds at once" class `PHN-2` U8 fixed for voicemail). **Fix shapes:** honour the token in the TTS/file playback loop, or route announcements through `EventPlaybackService`'s priority/preempt path (`PHN-1d`), which already has the semantics.

---

## By-ear check done 2026-09-29 — no overlap heard

The owner ran the return-checklist item "two announcements at once" (two long test notifications a second apart) and reported *"Audio / Notifications are good"* — no two voices. So the code-read defect (a cancelled token that no playback path reads) is not audible in practice; something downstream already serialises announcements. **Recommend: keep at P2 as a correctness cleanup (the dead token and the comment that implies it works), not a user-visible defect.** Worth a short code trace of which layer actually serialises them before touching `SetActiveSource`, so a cleanup does not remove the thing that is saving it.

---

## Why the owner heard no overlap — the layer that "serialised" was the UI, not the audio path (2026-09-30)

**Nothing in the audio path serialised announcements.** Two findings establish this.

1. **Measured on `radio` at `af9bc2b` (pre-fix, box muted).** Two `POST /api/notifications/announce` requests were sent 1.5 s apart, and `duckingState` was polled every 150 ms.
   - Both requests returned `200 {"outcome":"completed"}`. The first returned at 12.3 s and the second at 3.6 s.
   - `activeEventCount` went 1 → **2** → 1 from 2.0 s to 3.7 s.

   The second voice played on top of the first for 1.7 s. `SoundFlowPlaybackService.PlayStreamAsync` stops only the same `sourceId`, so nothing across sources could have stopped it.
2. **The owner's by-ear check could not produce an overlap.** System Config → Notifications → **Send Test** is `Disabled="@(_isSendingNotification …)"` (`SystemConfigPage.razor:1925`). `_isSendingNotification` stays true until its own request returns, and the request waits for the announcement to finish. A second click therefore cannot be sent while the first is speaking. The "serialising layer" was the button.

So the row's defect was real, and the dead token in `SetActiveSource` was the only thing meant to stop the first announcement. ADR-029 §6.2 rule 1 already described announcements as having *"`AnnouncementService`'s existing single-slot cancel"*. That cancel never worked.

## ✅ SHIPPED — PR #TBD — overlapping announcements are arbitrated

**`AnnouncementService` now tracks every announcement call that has become active.** Each call has its own linked `CancellationTokenSource`, and the call actually waits on it. `BecomeActive` decides which announcement plays when two overlap:

| New announcement's priority vs the one speaking | Result |
|---|---|
| **Higher** | The new one replaces it. |
| **Equal** | The one *requested* later replaces the earlier one. The arrival number is taken before TTS synthesis, so a long message that synthesises slowly cannot cut off a shorter one sent after it. |
| **Lower** | The new one plays **alongside** and never cuts off the higher one. This was the behaviour for every overlap before this change, so the one case this row does not decide is left as it was. |

- **What a replaced call does:** it unwinds through its own cleanup (stop ducking, stop, dispose) and returns `Interrupted`. The API returns `200 {"outcome":"interrupted"}`. The message is now "stopped or replaced before it finished", because a call can be replaced before it speaks.
- **AUD-74 is kept.** The new announcement **ducks before it takes over**, so the duck is held across the handover and the count goes 1 → 2 → 1.

**Review fixes, from two review passes:**
- Cleanup runs once per source (`ConditionalWeakTable`). `StopAsync` and the owning call used to both stop and dispose the same source.
- `PlaySoundWithAnnouncementAsync` stays registered between the ring and the name, so a hang-up or a replacement still reaches the name while it is being synthesised.
- A superseded call checks before ducking, so it does not raise `DuckingStateChanged(Started)`, which `EventPlaybackService` reads as a preemption signal.
- A call whose caller already cancelled cannot take over.
- The phase-2 duck comment is corrected. The duck is released between the ring and the name. That predates this change and is not fixed here.

**Tests:** `AnnouncementServicePreemptionTests` has five tests, covering replace, duck-held handover, a late-synthesised older one giving way, a lower priority not cutting off a higher one, and a higher one replacing a lower one even when it was requested first. Four falsifying mutations were each run and each went red:

| Mutation | Tests that went red |
|---|---|
| Remove the cancel | 3 |
| Remove the arrival check | 1 |
| Compare arrival only, ignoring priority | 2 |
| Duck after taking over | 1 |

**Not covered by a unit test:** preemption inside `PlaySoundWithAnnouncementAsync`. `AudioFileEventSourceFactory` is concrete and not mockable.

**For the owner to overrule if wanted:** the lower-priority-plays-alongside rule, above. It is documented in `design/INTEGRATIONS.md` § *How Audio Ducking Works*.

**Still true and out of scope:**
- Nothing *queues*; ADR-029 §6.2 rule 3.
- `PhoneCallIntegrationService` stops announcements on `Ended`/`Idle` but not on `InCall`.
- `PhoneIntegration` is not enabled on the box.
