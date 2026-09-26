# `AUD-30` — a phantom "disconnected" tears down a healthy BT stream while the phone stays connected

[← Builder Queue index](../BUILDER_QUEUE.md)

🟠 **P1.** Filed 2026-09-25 from the owner's cabinet sitting that measured `AUD-10`'s root cause. Seen
twice in 90 seconds on a Pixel 10 Pro XL, box on `9ca4259`.

## What was observed

```
11:29:14.354  PipeWire native stream stopped
11:29:14.381  Bluetooth device disconnected: Pixel 10 Pro XL (B0:D5:FB:D2:0D:68) reason="Unknown" (user-initiated: false)
11:29:14.381  BufferedSoundGenerator #3 disposed …  / source Playing -> Stopped
11:29:14.382  Starting reconnection loop for B0:D5:FB:D2:0D:68 (max 20 attempts)
11:29:14.382  Device already connected — stopping reconnection loop
11:29:25.250  BT device connected but capture stream missing (attempt 1/3) — attempting recovery
```

⛔ **At the same moment a 1 Hz recorder on the box showed nothing disconnecting:** node
`bluez_input.B0_D5_FB_D2_0D_68.2` stayed at **serial 58968**, BlueZ's `MediaTransport1.State` stayed
**`active`**, and only `radio-bt-stream`'s link vanished — because **we** stopped it. Audio was playing and
went silent for **~11 s** until the 30 s pipeline monitor happened to rebuild it.

The same line appeared at **11:27:50.492** on the owner's first connection.

**Two things say the disconnect was not real:**
1. `reason="Unknown"` — the mgmt monitor had **no kernel disconnect event** to hand over
   (`LinuxBluetoothService.cs:825-826`). A real disconnect at 11:28:37 in the same session carried one
   (`Mgmt disconnect event … reason="LocalHost"`).
2. The reconnect loop aborted **in the same millisecond** because `ConnectedDevice` still found a connected
   device (`BluetoothReconnectionLoop.cs:96`).

## Candidate mechanism — ⚠ found in code, NOT yet tied to these two events

The disconnect handler fires on **any** `Device1.Connected=false` for **any watched device path**
(`LinuxBluetoothService.cs:774-848`), and it calls `StopCaptureSubprocess()` (`:837`) unconditionally.

**The watched set is not limited to our adapter.** The adapter filter (`:397-414`) is applied only when
*choosing* the adapter. Device enumeration (`:661-678`), `OnInterfaceAdded` (`:699-730`) and
`OnInterfaceRemoved` (`:927-946`) accept a `Device1` under **any** `/org/bluez/hciN`. This box has two
adapters, and **`hci1` belongs to RotaryPhone** (`CLAUDE.md` § Cross-Service Boundary). The owner's phone
is the obvious device to exist under both.

So if the Pixel ever appears as `/org/bluez/hci1/dev_B0_D5_FB_D2_0D_68`, RotaryPhone's link to it
flipping `Connected=false` would read to us as **our** phone disconnecting — `Unknown` reason (the mgmt
event would be for `hci1`), a torn-down A2DP stream on `hci0`, and a reconnect loop that finds the `hci0`
entry still connected. **That is exactly the observed shape.**

⭐ **Supporting, not proving:** at **11:28:41.095** and **11:28:42.118** the log shows **two**
`Bluetooth device connected: Pixel 10 Pro XL` events for the same address a second apart — what two
object paths would produce.

⛔ **Against:** at 2026-09-25 ~11:40 `busctl tree org.bluez` showed the Pixel **only** under `hci0`.
`bluetoothd`'s journal for 11:27:40–11:29:50 has two lines and settles nothing. **Nobody has seen the
`hci1` path exist at the moment of a phantom.**

## The first task is observability, not a fix

**Neither log line records the object path**, which is the one fact that would settle this. Before
changing any behaviour:
1. Add `{ObjectPath}` to the `disconnected` line (`:846-848`), the `connected` lines (`:808`, `:718`) and
   the eviction line in `AUD-31` (`:944-946`).
2. Deploy, and read the path on the next phantom. **If it is `/org/bluez/hci1/…`, the fix is to scope all
   three device paths to the selected adapter's prefix** — the prefix `:397-399` already builds.
3. **If it is `/org/bluez/hci0/…`**, the hypothesis is wrong: BlueZ really flipped `Connected` on our
   adapter without a kernel disconnect, and the question becomes why — and whether tearing down a stream
   whose transport is still `active` is ever right.

⚠ **Do not scope the watchers to `hci0` before step 2.** It would very likely fix it — and it would
also destroy the only evidence of which of the two explanations was true.

## Related

- **`AUD-31`** — the phantom *eviction*, same session, same candidate mechanism.
- **`AUD-10`** — the phantom interrupted the measured run; every `AUD-10` box session will meet it.
- **`AUD-25`** — a phantom *connected* device in the API. Same family (our device state disagreeing with
  BlueZ), different defect.
- **`AUD-21`** (punch list) — disconnect-reason surfacing. `Unknown` here is not missing data; it is the
  evidence.
- ⚠ **Cross-service:** if the mechanism is confirmed, the boundary doc
  (`D:\prj\RotaryPhone\docs\prompts\RADIO-CONSOLE-BT-AUDIO-BOUNDARY.md`) should say that Radio Console
  ignores `hci1` device objects. Update it first, per `CLAUDE.md`.

## Verification

Owner at the cabinet, BT playing, recorder from `AUD-10.md` running. PASS: over a full sitting, no
`disconnected … reason="Unknown"` line while the recorder shows the transport `active` and the node serial
unchanged — and, with step 1 shipped, every `connected`/`disconnected` line carries an `hci0` path.

---

## ✅ CONFIRMED 2026-09-25 16:23 and 16:48 — RotaryPhone refusing the Pixel on `hci1` is the phantom, two for two

Owner's cabinet sitting for `AUD-10` (box on `da8ead8`). RotaryPhone's journal (`rotary-phone.service`)
read alongside ours:

```
16:23:47.221  RotaryPhone: Mgmt disconnect event: B0:D5:FB:D2:0D:68 reason=LocalHost
16:23:47.239  RotaryPhone: BLOCKED: B0:D5:FB:D2:0D:68 is already paired on hci0 — refusing on /org/bluez/hci1
16:23:47.539  radio-api:   Bluetooth device disconnected … reason="Unknown" (user-initiated: false)      +318 ms

16:48:14.790  RotaryPhone: Mgmt disconnect event: B0:D5:FB:D2:0D:68 reason=LocalHost
16:48:14.824  RotaryPhone: BLOCKED: … refusing on /org/bluez/hci1
16:48:15.137  radio-api:   Bluetooth device disconnected … reason="Unknown" (user-initiated: false)      +347 ms
```

Both times BlueZ still reported the Pixel **connected on `hci0`**. The phantom **tore down a playing stream**
at 16:48:15 (source `Playing → Stopped`) — which is what the owner saw as *"as soon as the pause was hit,
the album art and song title disappeared."* In a 16:51–16:52 window with **zero** `BLOCKED` lines, three
pause/resume cycles all passed.

⭐ **This settles the row's first task without the object-path logging** — the correlation is by timestamp
across two services, twice, 318/347 ms apart. The path logging is still worth shipping as the regression
instrument, but the fix no longer has to wait for it: **scope device enumeration, `OnInterfaceAdded` and
`OnInterfaceRemoved` to the selected adapter's path prefix** (`LinuxBluetoothService.cs:397-399` already
builds it). ⚠ Still update the boundary doc first, per `CLAUDE.md`.

### Also found in the same journal — a boundary question for RotaryPhone, not this row

```
16:23:15.399  RotaryPhone bt_manager: NewConnection: device=/org/bluez/hci0/dev_B0_D5_FB_D2_0D_68 … RFCOMM connected
```

RotaryPhone's HFP profile handler **accepts an RFCOMM connection on `hci0`** — Radio Console's adapter
under the boundary doc. BlueZ profiles are registered system-wide, not per adapter, so its handler answers
on both. Not shown to break anything here, but it is outside the documented boundary; raise it through the
boundary doc's Change Log rather than acting on it from this repo.

---

## 🚧 BUILT 2026-09-25 — branch `fix/aud-30-31-scope-device-objects-to-adapter`, PR [#666](https://github.com/mmackelprang/RTest/pull/666) (with `AUD-31`)

**Not merged, not deployed.** Proof needs the owner's phone with RotaryPhone running — see *Box session*
below. ⚠ The boundary doc's Change Log entry is being filed by a separate housekeeping thread, not by
this PR.

### What changed (`src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`)

- **The selected adapter's object path is recorded** (`_adapterPath`, set at `:573`) and every place
  that takes a device object from a BlueZ path goes through one gate, `AcceptObjectForAdapter`
  (`:2315`) over the pure `IsObjectUnderAdapter` (`:2280`): startup enumeration (`IngestExistingDevices`,
  `:796`), the pre-existing-connection check (`:599`), `InterfacesAdded` for `Device1`, `MediaPlayer1`
  and `MediaTransport1` (`:848`), the property watch (`:904`), the `Connected` handler
  (`OnDeviceConnectedChanged`, `:987`), `InterfacesRemoved` (`:1121`) and the media scan (`:3468`).
- **The match is on a path-segment boundary** — `/org/bluez/hci1` does not claim `/org/bluez/hci10/…` —
  and **Ordinal**: D-Bus paths are case-sensitive and `_adapterPath` comes from BlueZ's own key.
- **No adapter selected ⇒ nothing accepted (fail closed).** `_adapterPath` is reset at the top of every
  `StartAsync` (`:503`), so a UI Bluetooth stop/start re-selects instead of inheriting — the review
  caught that without the reset, a restart would have left `_adapterPath` null and every device ignored.
- **Adapter selection is now an exact match** (`IsConfiguredAdapterPath`, `:2307`). ⚠ The prefix the
  dossier pointed at ("`:397-399` already builds it") was a `StartsWith`, so `AdapterName: "hci1"` would
  have selected `hci10`. Reusing it as-is for device scoping would have carried that bug.
- **`{ObjectPath}` is on every connected / already-connected / disconnected / evicted / pre-existing
  line** — the regression instrument. Ignored foreign objects are logged **once per path at Debug**
  (bounded set of 256), never at Information or above.
- The `Connected` handler was extracted from its lambda unchanged apart from the gate and the log field.

### Tests — `tests/Radio.Infrastructure.Tests/Platform/Bluetooth/AdapterScopingTests.cs` (31)

Handler-level, no D-Bus, no clocks. A `Device1` under `hci1` is not cached and raises nothing; its
`Connected=false` does not raise `DeviceDisconnected`, does not dispose the capture stream, and leaves
`PipelineStatus` `Healthy`; its removal does not evict or log; startup enumeration skips it; a foreign
`MediaTransport1` is ignored; foreign objects log once at Debug; `hci0` behaviour unchanged (disconnect
still tears down and logs its path, removal still evicts); `hci1` vs `hci10`; no adapter ⇒ nothing.

**Mutation-checked, each guard disabled alone:** InterfacesAdded gate (5 fail), Connected-handler gate
(1), InterfacesRemoved gate (1), enumeration gate (1), segment boundary (2), exact adapter match (1),
log-once (1), fail-closed-on-null (3). **All three handler gates off together — the pre-fix shape — 9
fail**, including `ForeignConnectedFalse_DoesNotRaiseDisconnected_OrStopCapture`.
⚠ **Not covered** (each sits behind a live D-Bus call): the gate inside `WatchDevicePropertiesAsync`,
the pre-existing check, the media scan, and the stop/start re-selection. Deleting any one of those
alone fails no test; they are defence in depth behind the tested gates.

### Found while building — the mechanism is tighter than the dossier said

- **Why the reason was `Unknown`:** our mgmt monitor is filtered to our controller index
  (`BluetoothMgmtMonitor.cs:151-158`), so RotaryPhone's `hci1` disconnect event was never offered to us.
- **Why +318 / +347 ms:** `ConsumeDisconnectReason` polls for up to **300 ms** (`BluetoothMgmtMonitor.cs:60`)
  before giving up with `Unknown`. The lag in the confirmed timestamps is that wait.
- **A wider blast radius than the row recorded:** `_deviceCache` is keyed by object path but
  `FindDevicePath` / `ConnectedDevice` search it by address, first match wins. With an `hci1` entry for
  the same MAC cached, UI connect/disconnect, the reconnect loop and unpair could have acted on
  **RotaryPhone's** object. Closed by the same gate.

### Box session — what PASS looks like

1. **Deployed SHA first:** `curl -s http://radio:5000/api/health/version | grep -o '"gitShaShort":"[^"]*"'`
   must match the merged commit.
2. Owner's Pixel streaming on `hci0`, RotaryPhone running, `AUD-10`'s recorder on.
3. **Provoke** the refusal the way the 16:23 / 16:48 sitting did — pause/resume on the phone until
   RotaryPhone's journal shows `BLOCKED: B0:D5:FB:D2:0D:68 is already paired on hci0 — refusing on
   /org/bluez/hci1` (`journalctl -u rotary-phone --since '-10min' | grep BLOCKED`). ⚠ Do not drive
   `hci1` from our side (`bluetoothctl select 10:91:D1:FE:00:46` / `connect`) without the owner's say —
   it is RotaryPhone's adapter.
4. **PASS in radio-api's file log** (`F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1)`), in the
   second after each `BLOCKED`: **no** `Bluetooth device disconnected`, **no** `PipeWire native stream
   stopped`, **no** `removed from BlueZ`; audio continues. Every `connected` / `disconnected` / `removed`
   line that does appear says `at /org/bluez/hci0/…`.
5. **PASS in the journal:** `journalctl -u radio-api --since '-30min' --no-pager` shows no new warnings
   from this (the ignore line is Debug and will not appear in either sink at the shipped levels).
6. **Restart path (untested by unit tests):** Bluetooth off then on from the UI, reconnect the phone —
   `Bluetooth device connected: … at /org/bluez/hci0/…` must appear and audio must route.
7. **Positive control:** a real disconnect on `hci0` (phone BT off) still logs `disconnected … at
   /org/bluez/hci0/… reason=<non-Unknown>` and tears down.

---

## ✅ OWNER UAT 2026-09-25 22:55–23:03 on `029b1c7` — all four box-checklist steps passed

Owner at the cabinet, Pixel 10 Pro XL, RotaryPhone running. Instruments: the 1 Hz recorder, the service
file log, and `rotary-phone.service`'s journal.

| Step | Evidence | Result |
|---|---|---|
| 1. Connect logs our adapter | 22:55:28.495 and 22:57:32.774 `Bluetooth device connected … at /org/bluez/hci0/dev_B0_D5_FB_D2_0D_68` | ✅ |
| 2. RotaryPhone refuses the Pixel on `hci1` **while playing** | RotaryPhone `BLOCKED … refusing on /org/bluez/hci1` at **22:58:05.090**; source `Playing` since 22:57:38, first pause 22:58:12. **Our log: nothing** in the 7 s after — no disconnect, no stream stop, no eviction. Recorder: transport `active`, link intact. Owner: *"Music kept playing."* | ✅ |
| 3. Adapter stop/start (the review's HIGH) | `POST /api/bluetooth/stop` 23:03:01.8 → `start` 23:03:07.1 (both 200); **23:03:07.154 `Selected preferred Bluetooth adapter 78:20:51:F5:FB:A7 at /org/bluez/hci0`**; Pixel reconnected 23:03:12.069 at `hci0`; capture `Playing` 23:03:23.8; recorder transport `active`. Owner confirmed audio. | ✅ |
| 4. Phone Bluetooth off — positive control | 23:00:40.782 mgmt `reason="Remote"`; `disconnected … at /org/bluez/hci0/… reason="Remote" (user-initiated: false)`; stream torn down; auto-reconnect suppressed | ✅ |

**Before this fix, step 2's event produced a phantom disconnect 318–347 ms later** (16:23:47 and 16:48:14,
same day). An earlier refusal at 22:56:00.416, when the Pixel was already disconnected, was also silent.

⭐ **`AUD-10` held under this build too** — five resumes re-bound in 1, 102, 1, 1 and 3 ms, transport
`active` each time, one Warning per resume.

**Also recorded, not a defect:** a disconnect at 22:55:56.938 on `hci0`, `reason="LocalHost"`,
`user-initiated: true`, preceded by BlueZ's `RequestDisconnection` to RotaryPhone's HFP handler at
22:55:54.5 — a real disconnect on our adapter, logged with a real reason.

⚠ **Instrument note:** the first "Bluetooth toggle" at 23:00:06 produced a disconnect/reconnect, not an
adapter restart; step 3 was then run explicitly through the two API endpoints the Bluetooth page's
**Disable**/**Enable** buttons call (`BluetoothPage.razor:68`/`:73`). `bluetoothctl power off/on` would
NOT exercise this path.
