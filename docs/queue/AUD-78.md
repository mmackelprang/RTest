# `AUD-78` — play history cannot finalise its in-flight entry at shutdown

[← Builder Queue index](../BUILDER_QUEUE.md)

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
