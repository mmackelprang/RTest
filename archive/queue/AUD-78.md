# `AUD-78` — play history cannot finalise its in-flight entry at shutdown

[← Builder Queue index](../../docs/BUILDER_QUEUE.md)

🟢 **P3.** Filed 2026-09-29 from the `radio-api` file sink while shipping `AUD-15`.

## The evidence

On every `radio-api` stop (so on every deploy — five on 2026-09-29):

```
[WRN] [Radio.Infrastructure.Audio.Services.PlayHistoryTracker] Failed to finalize play history entry 87e0fa50-… during shutdown
System.ObjectDisposedException: Cannot access a disposed object.
Object name: 'IServiceProvider'.
   at …ServiceProviderEngineScope.CreateScope()
   at Radio.Infrastructure.Audio.Services.PlayHistoryTracker.Dispose() in …/PlayHistoryTracker.cs:line 883
```

`Dispose()` (`PlayHistoryTracker.cs:870-895`) creates a scope to reach `IPlayHistoryRepository`, but by then the root container is disposed. Its comment calls startup orphan cleanup "the backup for crash/kill scenarios where this doesn't run" — so the entry should be finalised on the next start, and the harm is a Warning with a stack trace per stop, not lost data. Confirm that claim as part of the fix.

## Fix shape

Finalise from `IHostApplicationLifetime.ApplicationStopping` (or a hosted service's `StopAsync`), while the container is still alive; keep `Dispose()` for unsubscription only.

## Verification

A deploy leaves no `Failed to finalize play history entry` line, and the in-flight entry has an end time.

## Shipped

- `PlayHistoryTracker.FinalizeInFlightEntryAsync` stamps the in-flight entry's end time; it is called
  from the new hosted service `PlayHistoryShutdownFinalizer.StopAsync`, which runs before the root
  container is disposed. `Dispose()` now only unsubscribes and never touches the database.
- The finalizer is registered in `AddRadioServices`, before `AudioEngineInitializationService` in
  `Program.cs`; hosted services stop in reverse order, so it runs after the audio engine has stopped.
- **Orphan-cleanup claim checked — true, with two qualifications.**
  `AudioEngineInitializationService.CloseOrphanedPlayHistoryEntriesAsync` runs at every start and
  closes entries with `EndedAt IS NULL`, but only those whose `PlayedAt` is more than **two minutes**
  before that start, and it stamps an *estimated* end (`PlayedAt + Duration`, else the cutoff), not
  the real stop time. So an entry started less than two minutes before a quick restart stayed open
  until the restart after that. The pre-fix harm was therefore a Warning per stop plus imprecise end
  times, not lost rows.
- Tests: `PlayHistoryShutdownFinalizerTests` (host stop finalizes once and container disposal logs
  no Warning; disposal without stop does not reach the repository; no in-flight entry is a no-op;
  a repository failure is logged and swallowed).

## ✅ Owner UAT — passed 2026-10-04

Reported by the coordinator on 2026-10-04: `AUD-78` passes owner UAT. 

**Archived 2026-10-04** in [`BUILDER_QUEUE_ARCHIVE.md`](BUILDER_QUEUE_ARCHIVE.md).
