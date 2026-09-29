# `AUD-73` — a second announcement does not stop the first

[← Builder Queue index](../BUILDER_QUEUE.md)

🟡 **P2 pending a by-ear check.** Filed 2026-09-28 from the Phase 2b Builder's out-of-scope findings (code read, not observed).

`AnnouncementService.SetActiveSource` (`src/Radio.Infrastructure/Audio/Services/AnnouncementService.cs`, called at `:51`, `:134`, `:156`) cancels a `CancellationToken` that no playback path reads. So by code read a new announcement arriving while one is speaking does not interrupt it — both play, two voices in the room. `NotificationsController`'s comment was corrected to say so in #696 rather than claim preemption.

**First task:** confirm by ear — send two long test notifications a second apart. If two voices overlap, promote to P1 (it is the same "two sounds at once" class `PHN-2` U8 fixed for voicemail). **Fix shapes:** honour the token in the TTS/file playback loop, or route announcements through `EventPlaybackService`'s priority/preempt path (`PHN-1d`), which already has the semantics.

---

## By-ear check done 2026-09-29 — no overlap heard

The owner ran the return-checklist item "two announcements at once" (two long test notifications a second apart) and reported *"Audio / Notifications are good"* — no two voices. So the code-read defect (a cancelled token that no playback path reads) is not audible in practice; something downstream already serialises announcements. **Recommend: keep at P2 as a correctness cleanup (the dead token and the comment that implies it works), not a user-visible defect.** Worth a short code trace of which layer actually serialises them before touching `SetActiveSource`, so a cleanup does not remove the thing that is saving it.
