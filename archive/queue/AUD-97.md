# `AUD-97` — after a restart the panel showed the Soundbar as the output while the saved Cast device played

[← Builder Queue index](../../docs/BUILDER_QUEUE.md)

✅🔬 **SHIPPED 2026-10-02, owner check outstanding.**

## Report

Owner, 2026-10-02: *"Note that after a restart/redeploy, the console UI shows that the soundbar is the output, but the saved cast device is actually playing (office speaker).  This situation is showing right now on the console."* and *"The cast device was selected before the deploy, so behavior is correct, display is wrong."*

## Measured on the box, before the fix (`b22bd85`)

- `GET /api/devices/output`: `google-cast` "Cast (Office speaker)" `isActive:true`; Soundbar `isActive:false`.
- `GET /api/devices/output/default`: the Soundbar (`out:Built-in Audio Analog Stereo`, `isDefault:true`, `isActive:false`).

## Cause

`MainLayout.LoadOutputDevicesAsync` set `_selectedOutputId` from `DevicesApiService.GetCurrentOutputDeviceAsync`, which calls `/output/default` — the hardware default sink, not the output in use. Within a session an `OutputChanged` broadcast corrects the display; after a restart the page loads fresh and only that lookup ran. The doc comment on `SyncConnectedCastDeviceToOutput` stated the defect as an invariant (*"the device it reads there is never google-cast"*).

## Fix

Page load uses the visible output list's `IsActive` entry (and so also shows the saved Cast device as connected), falling back to `/output/default` only when no output is active.

## Tests

`MainLayoutCastPickTests.PageLoad_WithCastActive_ShowsCast_NotTheDefaultSink` and `PageLoad_WithNoOutputActive_FallsBackToTheDefaultLookup`. Mutant: the active lookup removed → the Cast test fails.

## Owner check

With Cast selected, redeploy or restart; the Out picker shows the Cast output, and the Cast dropdown shows the Office speaker as connected.
