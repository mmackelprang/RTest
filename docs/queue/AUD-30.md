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
