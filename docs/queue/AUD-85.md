# `AUD-85` — picking Cast erases the saved default Cast speaker: the UI's connect races the API's own auto-connect, gets a 500, and clears the default

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **User-visible, cheap.** Filed 2026-09-30 during the `AUD-84` owner UAT. **The symptom is MEASURED on the box; the mechanism is read from code** (line numbers below re-read on `main` at `646be99`).

## Provenance

Found while verifying [`AUD-84`](AUD-84.md) at the console on 2026-09-30 (box on the branch build `079d46c`). After the fallback to local, the owner re-picked Cast: *"Re-picked Cast, it's playing on the office speaker again"*. Cast played — but reading the file sink for that re-pick showed the pick had erased the saved default Cast speaker. Record: [`RETURN-CHECKLIST.md`](../uat/RETURN-CHECKLIST.md) § Casting baseline.

Related punch-list row: **`AUD-54`** ([`HANDOFF-GA-PUNCH-LIST.md`](../HANDOFF-GA-PUNCH-LIST.md) §5), whose 2026-09-30 evidence ① is the **same race** at 16:39:12 (500 on connect, `Cannot connect in state Connecting`). `AUD-54` recorded the 500; this row is what the 500 does next.

## What happened (MEASURED, 2026-09-30, times EDT)

File sink `/opt/radio-console/logs/radio-20260930.txt` on `radio`:

```
17:32:55.053 [INF] DevicesController Switching audio output to: google-cast
17:32:55.212 [INF] DevicesController Auto-connecting to default Cast device: Office speaker (https://192.168.86.25/)
17:32:55.217 [ERR] DevicesController Error connecting to Cast device: Office speaker   (InvalidOperationException: Cannot connect in state Connecting. Output must be in Ready or Stopped state.)
17:32:55.217 [ERR] HTTP POST /api/devices/cast/connect responded 500 in 1.5824 ms
17:32:55.411 [INF] DevicesController Saved default Cast device:  ()
17:32:59.795 [INF] DevicesController Auto-connected to default Cast device: Office speaker (mode: DirectChannel)
```

Config store afterwards: `AudioPreferences:DefaultCastDeviceId` = `''` (LastModified `2026-09-30T21:32:55Z`, i.e. 17:32:55 EDT — nothing saved it again later).

So Cast **plays** (the API's own auto-connect won at 17:32:59), and the saved default is **gone**.

Also observed in the same run, **cause not established**: at 17:33:33 the output was switched back to local by an ordinary `Switching audio output to: out:Built-in Audio Analog Stereo` request. It may have been the owner. Do not assume it was the Cast dropdown the UI opened after clearing the default.

## Mechanism (FROM CODE, `main` at `646be99`)

Two parties both connect the Cast device on one pick:

1. **UI** — first click on Cast with a saved default goes to `AutoConnectCastDeviceAsync` (`src/Radio.Web/Components/Layout/MainLayout.razor:836-838`), which first calls `DevicesApi.SetOutputDeviceAsync(outputId)` (`:931`).
2. **API** — that output switch (`src/Radio.API/Controllers/DevicesController.cs:195-211`) calls `TryAutoConnectDefaultCastDeviceAsync()` (`:211`; method `:1268-1356`), which starts a **fire-and-forget** `Task.Run` (`:1280`) that calls `_castOutput.ConnectAsync(device)` (`:1302`) and returns `Ok` immediately.
3. **UI again** — as soon as the switch returns, the UI calls `DevicesApi.ConnectToCastDeviceAsync(_defaultCastDevice)` (`MainLayout.razor:940`) → `POST /api/devices/cast/connect` (`DevicesController.cs:552`). The API's background connect is already in `Connecting`, so `GoogleCastOutput.ConnectAsync` throws `Cannot connect in state Connecting` (`src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs:601`), caught and returned as 500 (`DevicesController.cs:688-689`).
4. **UI treats any failure as "device not reachable"** (`MainLayout.razor:946-955`, comment `:948`): it calls `DevicesApi.ClearDefaultCastDeviceAsync()` (`:950`), nulls `_defaultCastDevice`, and opens the Cast dropdown. The `catch` at `:957-965` does the same (`:960`). The API's `DELETE cast/default` (`DevicesController.cs:874`) persists the empty values through `SaveDefaultCastDeviceAsync("", "")` (`:880`) — hence `Saved default Cast device:  ()`.

## Effect

- **Every Cast pick that loses the race erases the default speaker, even though the connection succeeds.**
- After a restart the console cannot restore Cast: with no default, it came back on local.
- The next Cast pick takes the "no default" path (`MainLayout.razor:842-845`) and opens the dropdown instead of connecting — the one-tap pick is lost until a device is chosen from the dropdown again (a successful explicit connect re-saves the default, `DevicesController.cs:681`).
- The race is timing-dependent: it lost at 16:39:12 (recorded under `AUD-54`) and again at 17:32:55 the same day.

## Fix direction (for the plan — NOT decided here)

- **Make exactly one party connect**: either the API's auto-connect on the output switch, **or** the UI's explicit connect — not both. Which one owns it is the plan's call; note the API path runs for any caller of `POST /api/devices/output` with `google-cast`, not only this UI, and the UI path saves the default on success (`DevicesController.cs:681`).
- **and/or** have the UI treat "already connecting / already connected to the same device" as success, rather than clearing the default. A 500 caused by a busy state is not evidence the device is unreachable.
- Either way, clearing a user's saved preference should require evidence the device is actually gone, not any failed request.

### ⚠ A second path that clears the default, by design: Stop Casting (noted 2026-10-01, batch D)

Separate from the race above, and **existing behaviour, not a defect found in a measurement**. The owner may not want it, so it is recorded here for the plan to decide:

- **Stop Casting** in the Cast dropdown (`src/Radio.Web/Components/Shared/CastDeviceDropdown.razor:301-325`) calls `DevicesApi.DisconnectFromCastDeviceAsync()`. On success it invokes `OnDisconnect` (`:324`).
- `OnDisconnect` is bound to `MainLayout.OnCastDeviceDisconnected` (`src/Radio.Web/Components/Layout/MainLayout.razor:106`). That method calls `DevicesApi.ClearDefaultCastDeviceAsync()` (`:1139-1146`) every time, and it is the code's explicit intent.
- So **every deliberate Stop Casting erases the saved default Cast speaker.** The next Cast pick then opens the dropdown instead of the one-tap reconnect, and a restart cannot restore Cast.

Line numbers were read on `main` at `45a220e`. Whether "stop casting" should also mean "forget this speaker" is an owner question. If the answer is no, the fix belongs in this row's plan, since it is the same preference being cleared by a different path.

## As built (2026-10-02, with AUD-37 merged)

**One connecting party per pick.** A Cast pick with a saved default sends only `POST /api/devices/cast/connect` (`CastDefaultPick`). That endpoint promotes Cast through the output gate and re-saves the default on success. A failed pick keeps the saved default and opens the Cast dropdown. The API's fire-and-forget auto-connect still runs for any other caller of `POST /api/devices/output` with `google-cast`.

**The pick and the AUD-37 reconnect watcher.** `cast/connect` validates the request first (503 with no Cast output, 400 without `DeviceId` or `IpAddress`). It then calls `ICastReconnectControl.CancelCastReconnectForCastPickAsync` before it reads the Cast output.
- That call cancels a running watcher and waits up to 15 s (`CastPickReconnectWaitBound`). The 15 s is a budget, not a guarantee: SharpCaster's connect has no bound of its own. Other output and Cast actions keep the 3 s cancel.
- **Same device.** If the watcher is reconnecting the picked device, the cancel is marked as a pick of it. A run that has a connection up keeps it, starts it and switches the output to Cast. The switch is conditional on no output selection since the drop. The pick then finds Cast streaming to its device and answers 200 on the already-streaming path. If Cast is not streaming once that switch has gone through, the run switches the output from Cast back to the recovery's local output itself.
- **Another device.** The watcher is cancelled as usual and removes only a connection it made itself. The pick waits for that run, then connects its own device. ⚠ Exception: a run still carrying the keep mark of an earlier pick of its own device that answered 409 keeps that device, starts it and switches to Cast (a cancel is not an output selection). This pick's connect then replaces it, so the user ends on the picked device after briefly hearing the other.
- **Bound passed.** If the run is still going after 15 s, the pick answers 409 and touches nothing. After that 409 the run may still keep its connection and switch to Cast while the UI shows the request as busy, and the default is not re-saved in that case.
- **Brief dual output.** When a pick adopts the watcher's connection, the connection is started while the local output is still active, so Cast and local can play together until the switch. The same happens when a short-bound cancel finds a run marked by an earlier pick that answered 409. What that run does next depends on timing: if the caller's selection lands first, its conditional switch is refused, and it tears the connection down, except that it keeps it when that selection made Cast active (`SetOutputDevice("google-cast")`). If the run's switch lands first, it succeeds, and the caller's selection then moves the output on.

**Atomic conditional tear-down.** A cancelled run that does not keep its connection removes it only while Cast is not the active output. On the production engine that check and the tear-down run under one acquisition of the output lock (`SoundFlowAudioEngine.TearDownCastOutputUnlessActiveAsync`). A promotion of Cast either lands first, and the connection is kept for it, or waits until the tear-down has finished.

**Status codes and restoring local** (`DevicesController.ConnectToCastDevice`):
- **409, busy.** The output was `Connecting` before the attempt, or `GoogleCastOutput.ConnectAsync`'s state guard refused. The refusal carries the refused state (`GoogleCastOutput.TryGetConnectRefusedState`). Local is restored only when Cast was streaming on entry (this call's own stop-before-switch stopped it) and the refused state is neither `Connecting` nor `Streaming`. A `Connecting` or `Streaming` state means another party's connect owns the output, and restoring local would tear that connect down. If that party's connect then fails, local is restored only by its own failure path. This endpoint's 500 and 502 paths do that; the fire-and-forget auto-connect of `POST /api/devices/output` does not.
- **502.** Cast is not streaming to the requested device after the gate. Local is restored if Cast is still the active output. The default is not saved.
- **500.** The connect threw. Local is restored if Cast is the active output and not streaming.

## Pre-merge reviews (2026-10-02)

Three hostile reviews were run after `AUD-37` was merged in. The first found two MEDIUMs (a pick past the 3 s cancel got a 409 and the cancelled watcher tore down the speaker the user picked; the cancelled watcher's check-then-teardown raced the gate on the 200 path). The second found two more in those fixes (a 409 restore tore down another party's in-flight connect; the keep path lost the `AUD-84` recovery on `LostAgainAfterSwitch`). All four were fixed, with mutation-checked tests. The third found no HIGH or MEDIUM. **Deferred LOWs**, each a narrow window or wording:
- A refused `Stopping` is still restored. If it was another request's stop-before-switch (two picks within the same synchronous window), the restore supersedes that request's connect, which is the double-tap outcome in a much narrower window. The code cannot tell a disconnect's `Stopping` from a connect's.
- `SwitchFromCastToLocalAsync` is conditional on the output id, not the epoch. A `POST /output google-cast` that lands between the run's switch and its not-streaming check can have its auto-connect torn down. `SetActiveOutputIfEpochAsync` would close it.
- The comment at `CastReconnectWatcher.cs` (keep path, `LostAgainAfterSwitch`) implies that no watcher can start. The call starts none, but a deferred `AUD-84` loss replay can still start one. That is harmless because both switches are conditional.
- `ICastReconnectControl`'s remarks repeat the two doc inaccuracies corrected above (a run marked by an earlier 409'd pick).

## Verification

⛔ **NOT auto-mergeable** — output-selection path; needs an owner Cast-pick check.

1. **Unit:** a test that drives the UI's pick with a saved default and an API that reports the device already connecting (or 409/500 for a busy state) asserts the default is **not** cleared. It must fail on `646be99`.
2. **On the box, with the owner:** with a saved default and a local output active, pick Cast several times (switch back to local between picks). Each time: Cast plays, no `POST /api/devices/cast/connect responded 500`, and `AudioPreferences:DefaultCastDeviceId` still holds the speaker's id afterwards.
3. Restart `radio-api` with Cast selected; it comes back on Cast.

## Related

- `AUD-84` ✅ ([#736](https://github.com/mmackelprang/RTest/pull/736)) — the UAT this was found in.
- `AUD-54` (punch list §5) — Cast lifecycle defects in the same client-lifetime code; evidence ① is this race.
- `AUD-37` (punch list §4.2) — auto-reconnect when the speaker returns; any reconnect must not reintroduce a second connecting party.
