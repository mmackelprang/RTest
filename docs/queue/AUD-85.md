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

## Verification

⛔ **NOT auto-mergeable** — output-selection path; needs an owner Cast-pick check.

1. **Unit:** a test that drives the UI's pick with a saved default and an API that reports the device already connecting (or 409/500 for a busy state) asserts the default is **not** cleared. It must fail on `646be99`.
2. **On the box, with the owner:** with a saved default and a local output active, pick Cast several times (switch back to local between picks). Each time: Cast plays, no `POST /api/devices/cast/connect responded 500`, and `AudioPreferences:DefaultCastDeviceId` still holds the speaker's id afterwards.
3. Restart `radio-api` with Cast selected; it comes back on Cast.

## Related

- `AUD-84` ✅ ([#736](https://github.com/mmackelprang/RTest/pull/736)) — the UAT this was found in.
- `AUD-54` (punch list §5) — Cast lifecycle defects in the same client-lifetime code; evidence ① is this race.
- `AUD-37` (punch list §4.2) — auto-reconnect when the speaker returns; any reconnect must not reintroduce a second connecting party.
