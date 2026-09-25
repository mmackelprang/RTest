# PLAN — `AUD-10` + `AUD-11` · The capture stream follows the BT node across pause/resume, and refuses everything else

**Rows:** [`AUD-10`](../../docs/queue/AUD-10.md) (resume never restores audio) and
[`AUD-11`](../../docs/queue/AUD-11.md) (the stream falls back to the line-in). **One event, seen from two
sides — planned together.** This plan **amends**
[`AUD-11-the-capture-that-recorded-the-wrong-jack.md`](AUD-11-the-capture-that-recorded-the-wrong-jack.md)
rather than replacing it: that plan's Tasks 1–7 stand, except where §2 below says otherwise.

**Planned:** 2026-09-25, against `main` at `ee628f93`. **Evidence:** measured on `radio` the same day with
the owner at the cabinet — see `AUD-10.md` § *ROOT CAUSE MEASURED 2026-09-25*.

**Estimate:** **2 d** + one supervised box session (~20 min, owner's phone). ⛔ **Not auto-mergeable** — live
audio path, and the only observable proof is a pause/resume on a real handset.

---

## 0. What is established, and what is not

### 0.1 Established by measurement (2026-09-25)

| | |
|---|---|
| **Pause destroys the node** | transport `active → idle` at 11:29:31.9; node serial 58968 gone the same second |
| **Our stream falls to the line-in** | linked to `alsa_input.pci-0000_00_1f.3.analog-stereo` at 11:29:34.1 (`AUD-11`) |
| **Resume recreates the node under the same name, new serial** | `bluez_input.B0_D5_FB_D2_0D_68.2`, serial 59112, transport `pending`, 11:30:04.6 |
| **Nobody binds it; it dies in ~4 s** | transport back to `idle`, node gone, 11:30:08.9 — the phone gives up |
| **The node name on this box** | `bluez_input.<MAC>.<N>` — `.2` in every sighting (2026-09-10 and 2026-09-25) |
| **Registry id ≠ `object.serial`** | our own log: `id=71, serial=58921`; `id=76, serial=58968` |

### 0.2 Established by reading source (PipeWire 1.0.7 / WirePlumber 0.4.17 — not re-verified on the box)

- The A2DP node is **dynamic**: emitted at transport `PENDING`, removed at `IDLE`
  (`spa/plugins/bluez5/bluez5-device.c:769-780`).
- The transport is re-acquired only when the node is **started**, i.e. when something links it
  (`media-source.c:689-709`). **Binding our stream to the new node is what makes audio return.**
- WirePlumber matches a **numeric** `target.object` only against `object.serial`
  (`policy-node.lua:298-300`).

### 0.3 ⛔ NOT established — the plan must not rely on these

- **That the recreated node always carries the same `.N` suffix.** Two sightings, both `.2`. Match by
  `bluez_input.<MAC>` prefix, as the working scrape does, never by the full name.
- **That binding inside the ~4 s window is enough on every handset.** One phone, one run. The box session
  measures the actual latency from node-appeared to transport-`active`.

---

## 1. ⭐ Three defects found while planning — they change the AUD-11 plan

### 1.1 The registry listener has never delivered an event in production

`PipeWireRegistryFilter.TryExtractBtCaptureAddress` accepts only names ending **`.a2dp-source`**
(`PipeWireRegistryFilter.cs:20`, `:40-43`). The node on this box is `bluez_input.B0_D5_FB_D2_0D_68.2`. The
filter never matches, so `OnGlobal` never records an id (`PipeWireRegistryListener.cs:275-280`), so
`OnGlobalRemove` has nothing to forward (`:300-301`) — **both events are dead**.

Measured: across every retained log (2026-09-19 → 2026-09-25) there are **zero** `PW registry: BT node
appeared/disappeared` lines, while the listener reported `PW registry listener active — Plan B periodic
re-scan disabled` at 2026-09-20 03:01:37 — so **its healthy report also switched off the fallback scrape.**

⭐ **How it survived its tests:** `PipeWireRegistryFilterTests.cs:16-19` uses an invented
`bluez_input.<MAC>.a2dp-source`; `PipeWireNodeParsingTests.cs` — the scrape that works — uses real names
(`bluez_input.D4_3A_2C_64_87_9E.0`). The filter is correct against a naming scheme this box never produces.

⚠ **Consequence so far: none visible.** `BluetoothAutoSwitchService.WaitForNodeOrTimeoutAsync` is its only
consumer and has logged neither `PW node arrived` nor `did not appear` in the retained logs — the path has
not run. Do not claim an impact the logs do not show.

### 1.2 The registry id is passed as the serial

`OnRegistryNodeAppeared` publishes `PipeWireSerial = (int)e.Id` (`LinuxBluetoothService.cs:1572`), and its
doc comment (`:1555-1557`) asserts *"The registry's global id doubles as the object.serial"*. **False on this
box** (§0.1: id 76 / serial 58968). Latent today only because §1.1 means it never runs. **Fixing §1.1 alone
would make the first event-driven bind target serial 76** — a different object, or nothing.

This is the `CLAUDE.md` § *Pre-Merge Review* failure mode exactly: a comment asserting an invariant the code
does not have, sitting on the path a fix will route through.

### 1.3 The AUD-11 plan's re-arm path does not re-arm the case AUD-10 needs

AUD-11 §1.2 says *"The re-arm path is already built and already correct: `NodeAppeared` →
`CaptureNodeAvailable` → `BluetoothAutoSwitchService`."* It is neither, for pause/resume:
- it is dead (§1.1);
- `BluetoothAutoSwitchService` only listens inside `WaitForNodeOrTimeoutAsync`, which runs after a
  **connect**, and even then acts only when the active source is **not** Bluetooth
  (`BluetoothAutoSwitchService.cs:166`). On a resume, BT is already the active source — nothing re-binds.

⛔ **So the AUD-11 plan, built as written, would fix the line-in fallback and leave `AUD-10` exactly as it
is** — torn down and parked, with nothing to un-park it. The re-arm needs a consumer of its own (§2, Task C).

---

## 2. Decision

**Event-driven follow, not name-binding.** Keep AUD-11's design — `node.dont-reconnect = true`, tear the
stream down when its node disappears, park in `WaitingForCaptureNode` — and add the missing edge: **when a
node for the connected device's MAC appears while the BT source is parked, bind to its `object.serial`
immediately.**

**Why not bind `target.object` by name instead** (WirePlumber matches non-numeric targets by name,
`policy-node.lua:337-345`). Rejected for this box:
1. In the gap between destroy and recreate — **30 s** in the measured run — the name resolves to nothing,
   and without `dont-reconnect` WirePlumber falls back to the default source. That *is* `AUD-11`.
2. With `dont-reconnect`, whether WirePlumber 0.4.17 re-links an already-created, unlinked stream when a
   matching node appears later is **unverified**, and it would be the one piece of the fix nobody here can
   observe or log.
3. It still depends on the `.N` suffix being stable (§0.3).

The event-driven version keeps every decision in our code, where it can be logged and tested.

---

## 3. Tasks

### Task A — the filter matches the names this box actually produces (§1.1)

- `PipeWireRegistryFilter`: accept `bluez_input.<MAC>` followed by end-of-string **or** `.` plus any suffix
  — the same shape `FindPipeWireBluetoothNodeAsync` has always matched (`LinuxBluetoothService.cs:1417`).
  Keep rejecting `.hfp-ag` / `.hfp-hf` if present (harmless; not observed on `hci0`).
- `PipeWireRegistryFilterTests`: **replace the invented `.a2dp-source` fixtures with measured names** —
  `bluez_input.B0_D5_FB_D2_0D_68.2`, and `bluez_input.D4_3A_2C_64_87_9E.0` from the parsing tests. Keep one
  `.a2dp-source` case only if a source says some PipeWire version emits it; otherwise delete it.
- Rewrite the class doc comment (`:11-15`): it documents a convention, not this box.

### Task B — carry the real serial (§1.2)

- `PipeWireRegistryListener.OnGlobal`: read `object.serial` from the same `spa_dict`
  (`ReadSpaDictKey(props, "object.serial")`) and add `ObjectSerial` to `BtNodeRegistryEventArgs`. If it is
  absent or unparseable, **log a warning and do not raise** — never fall back to `id`.
- `OnRegistryNodeAppeared`: publish `PipeWireSerial = e.ObjectSerial`. **Delete the "doubles as" comment**;
  replace it with the measured counter-example.

### Task C — the missing edge: a parked BT source re-binds on node appearance (§1.3)

- On `NodeAppeared` for the **connected** device's MAC, while capture is parked (`WaitingForCaptureNode` from
  AUD-11 Task 4) and not intentionally stopped (`_captureIntentionallyStopped`): marshal to the thread pool
  (the event fires on the PipeWire loop — AUD-11 §1.2's warning applies) and start the native stream against
  `e.ObjectSerial` **directly**. Do **not** route through `SearchForCaptureDeviceAsync`: it scrapes `pw-cli`
  and waits 500 ms per attempt, and the budget is ~4 s total.
- Log one `Warning`-level line on re-bind with the elapsed time since `NodeAppeared` — `LOG-11` keeps
  `Information` out of the journal, and this latency is the number the box session needs.
- ⚠ **Idempotent:** the pipeline monitor, `OnGeneratorStalled`, the connect path and this handler can all
  try to start capture. Take `_captureDeviceLock` and no-op if a stream bound to that serial already exists.

### Task D — AUD-11's Tasks 1–7, as planned, with two amendments

1. **Task 4** (target lost → tear down + park) now has a live trigger — `NodeDisappeared` — thanks to Task A.
   ⚠ It will fire on **every pause**, not rarely. The tear-down must be cheap and must not emit anything
   above `Information` per pause.
2. **§1.2's "re-arm path is already built"** — amend the AUD-11 plan text to point at Task C here.

### Task E — the health signal means "bound to the BT node"

The four signals in `AUD-10.md` § *Why nothing notices* all stay green today. After Tasks A–D:
`PipelineStatus` reports `Degraded` / `WaitingForCaptureNode` while parked (AUD-11 Task 6), and a stream
bound to the recreated serial is `Healthy`. No new signal is needed beyond AUD-11's peer audit (Task 5).

---

## 4. Order

**A → B → C → D → E, in one PR.** A without B is **worse than today**: it wakes the dead event path and
feeds it the wrong serial. A+B without C/D changes nothing observable. Splitting only adds deploys of states
nobody wants on the box.

---

## 5. Tests

| | What it proves |
|---|---|
| **T1** filter | accepts `bluez_input.B0_D5_FB_D2_0D_68.2` and `.0`; rejects other MACs, other prefixes, malformed MACs |
| **T2** serial | appear event with `id=76, object.serial=58968` publishes **58968**; missing serial → no event + warning |
| **T3** re-bind | fake registry: disappear → parked (no stream); appear for same MAC → stream started against the **new** serial; appear for a different MAC → nothing |
| **T4** idempotence | concurrent appear + pipeline-monitor recovery → exactly one stream |
| **T5** intentional stop | appear while `_captureIntentionallyStopped` → nothing |

⚠ Follow `CLAUDE.md` § *Test Timing*: T3/T4 synchronise on the events, never on `Task.Delay`.

### 5.1 The box session — the only place the fix is observable

**Deploy first, then check the SHA** (`CLAUDE.md` § *Read the deployed SHA BEFORE a human runs UAT*).

Run the 1 Hz recorder from `AUD-10.md`'s 2026-09-25 section (node serial, `radio-bt-stream`'s link,
`MediaTransport1.State`), then with the owner:

1. **Healthy baseline** — play; stream linked to `bluez_input.<MAC>.N`, audible.
2. **Pause 30 s** — PASS: node gone, `radio-bt-stream` **absent or unlinked** — never `alsa_input…`.
3. **Resume** — PASS: new serial appears, stream linked to it **within ~2 s**, transport reaches `active`,
   audio returns with no touch on the handset. Record the re-bind latency from the Warning line.
4. **Connect while paused, then press play** — the 11:27 case; same PASS as step 3.
5. **Repeat 2–3 three times** — pause is routine; a re-bind that works once and leaks is not a fix.

⚠ **Expect the phantom-disconnect defect** (§6) during the session. If it fires, record it and re-run;
it is not a failure of this plan.

---

## 6. Deliberately not done — seen in the same window, belong in their own rows

- **Phantom disconnect** — `Bluetooth device disconnected … reason="Unknown" (user-initiated: false)` at
  11:29:14 and 11:27:50 while the device stayed connected and the node unchanged; our service tore down a
  healthy stream on it. Same family as `AUD-25` / `AUD-21`.
- **Phantom eviction** — `Bluetooth device removed from BlueZ … evicted from cache` at 11:29:43 and 11:28:20
  while `bluetoothctl` reported paired/bonded/connected. Candidate: `InterfacesRemoved` for a child object
  read as the device's — see `AUD-14`.
- **Resampler ratio** — `SetRatio` has zero callers (`AUD-15`); a re-bind creates a new stream with the
  initial ratio each time. Unchanged by this plan.

Both phantoms should be filed as rows before the box session, so a sighting has somewhere to go.

---

## 7. Self-review

**Verified first-hand at `ee628f93`:** the filter's suffix check and its tests; `_idToAddress` gating the
remove path; `PipeWireSerial = (int)e.Id` and its comment; `BluetoothAutoSwitchService.cs:166`; the four
health signals; zero registry events in the retained box logs; id ≠ serial in two log lines.

**Not verified, and what it costs if wrong:**
- *That starting our stream against the new serial within the window makes the transport go `active`.*
  This is the central claim, and it rests on PipeWire source (§0.2), not on a measurement. **If step 3 of
  §5.1 fails with the stream linked but the transport stuck at `pending`, this plan's decision is wrong**,
  and the next candidate is an explicit transport `Acquire` over D-Bus.
- *That `object.serial` is readable from the registry `spa_dict` via `ReadSpaDictKey`.* Expected — it is a
  standard global property — but Task B's warning path exists for the case where it is not.
