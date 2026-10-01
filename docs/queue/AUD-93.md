# `AUD-93` — every Stop Casting logs a Warning: the output gate stops a Cast output the controller already stopped

[← Builder Queue index](../BUILDER_QUEUE.md)

🟢 **P3, log noise only.** Filed 2026-10-01 by the batch D Builder (the Cast arc), from the `AUD-54` box UAT. **The Warning is MEASURED in the box's file sink; the mechanism below is read from code** (`main` at `45a220e`).

## What is logged (MEASURED)

Every **Stop Casting** logs, at Warning, from `GoogleCastOutput`. File sink `/opt/radio-console/logs/radio-20260930.txt` on `radio` holds 4 such lines on 2026-09-30 (EDT): 17:28:52, 17:34:02, 20:46:35, and 22:04:24.739. The last one came 117 ms after `DevicesController` logged `Disconnecting from Cast device: Office speaker` at 22:04:24.622, the disconnect that ended the `AUD-54` UAT:

```
2026-09-30 22:04:24.739 -04:00 [WRN] [Radio.Infrastructure.Audio.Outputs.GoogleCastOutput] Stop requested but output is not streaming (state: "Ready")
```

Warning is the level `radio-api`'s console sink passes, so the line also reaches journald (`LOG-11`). The box is one where journald volume correlates with audible distortion. Audio is not affected.

## Mechanism (FROM CODE, `main` at `45a220e`)

1. `DevicesController.DisconnectFromCastDevice` (`src/Radio.API/Controllers/DevicesController.cs:701`) stops the Cast output itself, `_castOutput.StopAsync` (`:713`). It then calls `_castOutput.DisconnectAsync` (`:714`).
2. Next it promotes the local output through the gate, `_audioEngine.SetActiveOutputAsync(fallbackOutputId)` (`:728`).
3. The gate sees it is leaving Cast and calls `TearDownCastOutputAsync` (`src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs:283-285`).
4. `TearDownCastOutputAsync` calls `_castOutput.StopAsync` when the state is `Streaming`, `Ready` **or** `Connecting` (`:380-384`). After step 1 the output is `Ready`, so it stops a second time.
5. `GoogleCastOutput.StopAsync` goes through `AudioOutputBase.ValidateCanStop` (`src/Radio.Infrastructure/Audio/Outputs/AudioOutputBase.cs:212-223`). That method logs the Warning for any state other than `Streaming` (`:216-218`) and returns false. Nothing else happens; the second stop is a no-op apart from the log line.

## Fix options (for the plan)

1. **Controller:** let the gate do the teardown. Drop the controller's own `StopAsync`/`DisconnectAsync` (`:713-714`) and rely on `SetActiveOutputAsync`'s `TearDownCastOutputAsync`, which already does both. Check the exception path at `:734-746` keeps its "restore local even if disconnect fails" guarantee.
2. **Gate:** in `TearDownCastOutputAsync`, call `StopAsync` only when the state is `Streaming`, not `Ready`. A stop on a `Connecting` output would still log the same Warning, because `ValidateCanStop` warns for every state but `Streaming`.
3. **Base class:** log a stop of an already-`Ready`/`Stopped` output at Debug and keep Warning for genuinely unexpected states. This is broader, because every output shares it.

⚠ The line numbers above are at `45a220e`. `AUD-81` (#749, #750) has since merged and moved them. On `590c40d`: `DevicesController.cs:770` (method), `:782-783` (stop, disconnect), `:797` (gate); `SoundFlowAudioEngine.cs:288` (teardown call), `:394-398` (the second stop); `AudioOutputBase.cs:212-223` unchanged. The mechanism still holds there: `AUD-81`'s re-UAT on `590c40d` logged the same Warning at 05:10:36.805 EDT. `AUD-81` also changed `GoogleCastOutput.StopAsync` (it stops the receiver application before unmuting a console-muted speaker), so re-read it before planning.

## Verification

On the box, with `Radio.Infrastructure.Audio` at Information (`LOG-5`), cast to a speaker with the source stopped, then Stop Casting. Expect no `Stop requested but output is not streaming` line, and `Cast output stopped + disconnected gracefully` still logged. A unit test that drives `DisconnectFromCastDevice` against a real gate asserts the Warning is not logged; it must fail on `45a220e`.

## Related

- `AUD-54` (punch list §5, [#748](https://github.com/mmackelprang/RTest/pull/748)) — the Cast lifecycle fixes whose UAT surfaced this.
- `AUD-85` — the other Stop Casting side effect: it clears the saved default Cast speaker (see that dossier).
