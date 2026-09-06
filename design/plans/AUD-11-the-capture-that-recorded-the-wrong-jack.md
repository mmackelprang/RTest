# PLAN — `AUD-11` · The capture stream stops being allowed to record the wrong jack, and starts being able to say so

> **Row:** `AUD-11`, [`docs/queue/AUD-11.md`](../../docs/queue/AUD-11.md). 🟠 **P1.** Filed 2026-09-06 from a live
> observation on `radio`.
> **Branch:** `fix/aud-11-bt-capture-refuses-the-wrong-node`
> **Estimate:** **1.5 d** of build, **plus one box session with the owner's handset**. §0.7 derives both.
> **⛔ NOT auto-mergeable on green gates.** §0.8 gives the three reasons. A human must be at the appliance.
> **Planned against** `main` at **`066a0d5c`**. Every line number below was read out of the tree at that
> commit. Where a line is likely to move it is quoted as well as numbered.
> **Nothing on the box was touched while planning this.** The owner was listening to music. Every claim
> below is from source, from `git`, or from PipeWire's own published documentation, and §7.2 says which
> claims are none of those.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`radio-bt-stream` is a PipeWire capture stream that asks for one specific node — the phone's
`bluez_input` A2DP source — and then asks the session manager to connect it to *something*. Those are
two different requests, and the second one has no upper bound. When the first request cannot be
satisfied, PipeWire honours the second: it links the stream to the default source, which on this
appliance is `alsa_input.pci-0000_00_1f.3.analog-stereo` — the unplugged line-in jack on the back of
the box. Nothing fails. Nothing logs. `pw-link -l` shows `[active]`. The watchdog sees callbacks
arriving on schedule and stays quiet, because callbacks *are* arriving on schedule — they are just
carrying silence from a jack with nothing in it. The fix is one property. The other 90 % of this plan is
the reason the row is P1: **a stream that binds to the wrong node must be able to say so**, and today
the one artefact a person would check to find out is the artefact that lies.

### 0.2 ⭐ The binding mechanism, traced

Three files, one chain. Read this before touching anything; two of the four steps are not where you
would look for them.

| # | Where | What happens |
|---|---|---|
| 1 | `LinuxBluetoothService.cs:1305` | `FindPipeWireBluetoothNodeAsync` scrapes `pw-cli list-objects` and returns `(NodeName, PipeWireId, PipeWireSerial)`. `PipeWireId` is the registry **global id**; `PipeWireSerial` is `object.serial`. **They are different numbers and the difference is load-bearing** (`C-164`). |
| 2 | `LinuxBluetoothService.cs:1814` | `var nodeId = (uint)(targetSerial > 0 ? targetSerial : 0);` — the serial, **or zero**. §0.4 is about the zero. |
| 3 | `PipeWireNativeStream.cs:204` | The props string: `node.autoconnect = true target.object = {_targetNodeId}` |
| 4 | `PipeWireNativeStream.cs:236-239` | `pw_stream_connect(_stream, Input, PW_ID_ANY, Autoconnect \| MapBuffers, …)` |

**The fallback decision does not happen in this repository.** It happens in the session manager, and
steps 3 and 4 are what invite it. From PipeWire's `pipewire-props(7)`, verbatim:

> **`node.autoconnect = true`** — "Instructs the session manager to automatically connect this node to
> some other node, usually a sink or source."
>
> **`target.object = <node.name|object.serial>`** — "Where the node should link to, this can be a
> node.name or an object.serial."

`node.autoconnect` is an instruction with no failure mode. `target.object` is a *preference*. When the
preference cannot be resolved, the instruction is still outstanding, and the session manager satisfies
it with the default source. That is the whole mechanism, and it is working as documented.

⭐ **This repository already knew this, in this file, about the other capture path, and worked around it
there.** `LinuxBluetoothService.cs:1985-1987`:

```csharp
  /// Manually links pw-record's input ports to the BT node's output ports.
  /// WirePlumber overrides pw-record's --target flag and links it to the default
  /// audio source instead. This method: (1) disconnects any existing links to
  /// pw-record inputs, (2) creates explicit links from the BT node outputs.
```

*"links it to the default audio source instead"* — that is `AUD-11`, written down in March, about
`pw-record`. The `pw-record` fallback is defended twice over: `LinuxBluetoothService.cs:1856` passes
`-P node.autoconnect=false`, and `LinkPipeWireRecordToBtNode` re-links explicitly afterwards. **The
native path inherited neither defence** (`C-162`). It is not that nobody knew; it is that the knowledge
did not travel from the subprocess path to the path that replaced it.

### 0.3 ⚠ The `MEMORY.md` note survives code reading — but not in the shape the row states it

The row says `PW_ID_ANY` "was the resolution to an earlier targeting problem" and warns against
reverting it. Both halves needed checking. Traced through `git`:

- The file was created by **`69001fcdf`** (PR #262). **`node.autoconnect = true` and
  `target.object = {_targetNodeId}` were in the very first version of that props string.**
- `PW_ID_ANY` arrived later in the same PR, in **`5353f020a`**, "fix: Pin PwStreamEvents at stable
  address for PipeWire callback dispatch". The whole diff to the connect call:

```diff
       var paramPods = new[] { podBuffer };
+      // Use PW_ID_ANY so PipeWire resolves target from the target.object property
+      const uint PW_ID_ANY = 0xffffffff;
       var result = pw_stream_connect(
-        _stream, PwDirection.Input, _targetNodeId,
-        PwStreamFlags.Autoconnect | PwStreamFlags.MapBuffers | PwStreamFlags.RtProcess,
+        _stream, PwDirection.Input, PW_ID_ANY,
+        PwStreamFlags.Autoconnect | PwStreamFlags.MapBuffers,
         paramPods, 1);
```

- Both mainline lines are still at their original state; `git blame` shows nothing has touched either
  since the PR #262 squash (`ae8b8721`).

**So `PW_ID_ANY` did not enable `target.object` resolution — that was already there. It removed a
*competing* numeric target.** `_targetNodeId` is an `object.serial` (the ctor's own XML doc says so at
`PipeWireNativeStream.cs:101`: *"PipeWire object serial of the node to capture from"*), and it was being
passed in a parameter that means node **id**. The purchase was: *stop handing `pw_stream_connect` a
serial in an id-shaped slot.* That is the entire purchase (`C-161`).

⚠ **And it bought that on a theory, not on a measurement.** `5353f020a` shipped three changes at once
against a single symptom — *"the native PipeWire stream not delivering audio"* — and its bullet 1 (a
GC-movable `PwStreamEvents` struct) is on its own sufficient to explain a callback that never fires.
Which of the three actually fixed the silence was never isolated and is recorded nowhere. **The row's
"do not simply revert it" is still the right instruction, but for a narrower reason than the row gives:**
the un-buyable move is passing `_targetNodeId` as the `targetId` argument again. Keeping `PW_ID_ANY` is
free, so this plan keeps it, and the fix is **additive** — there is no conflict to trade off, and §1.1
says so rather than papering over one.

📌 **Two citation corrections, so the next reader does not re-derive them.** (a) PR #262's *body* says
nothing about `PW_ID_ANY`; the only written rationale anywhere is the `5353f020a` commit message quoted
above. (b) `MEMORY.md` is **not a repo file** — `docs/BUILDER_QUEUE.md:47` and `docs/queue/AUD-11.md:30`
both cite it as though it were, and `git log --all -- "*MEMORY.md"` is empty. It is Claude project
auto-memory at `~/.claude/projects/D--prj-rtest-rtest/memory/MEMORY.md`. §5 corrects the two citations.

### 0.4 ⚠ There are TWO routes into the wrong jack, and only one of them is in the row

The row describes the mid-session route: the node vanishes, the stream is moved. There is a second,
present at *acquisition* time, and it is one line.

`LinuxBluetoothService.cs:1814`:

```csharp
    var nodeId = (uint)(targetSerial > 0 ? targetSerial : 0);
```

`targetSerial` comes from `ParsePwCliOutputForBtNode`, which returns a serial of **0** on two of its
three success paths — `:1724` and `:1789`, both reached when the node's `object.serial` line was not
seen before the object block ended. The ternary converts "I could not read the serial" into the literal
target `0`, the props string becomes `target.object = 0`, that matches no node, and `node.autoconnect`
then does what §0.2 describes. **A serial of zero and "any node you like" are the same value here**
(`C-163`).

That is the same bug arriving through the front door instead of the back, and it is why Task 2 exists
separately from Task 1. It also means the failure is reachable on a cold connect, not only after a
pause — which matters for reproduction, because the row's repro (pause on the handset) exercises only
the mid-session route.

### 0.5 ⭐ Why every indicator stays green — and why this half of the row is co-equal with the fix

The row says the false signal is *the graph itself*. That is correct and it is not the only one. Four
indicators, four independent reasons each one is wrong, none of them a bug in isolation:

| Indicator | Where | Why it says green |
|---|---|---|
| `pw-link -l` shows `[active]` | the graph | The link genuinely is active. The graph has no concept of *intended* peer, so it cannot render the defect. |
| `BluetoothCaptureWatchdog` stays quiet | `BluetoothCaptureWatchdog.cs:88`, `snapshot.Value.ElapsedMs >= opts.OnProcessStallThresholdMs` | ⭐ **It measures liveness only.** The ALSA line-in delivers buffers on a perfectly regular schedule, so `MillisecondsSinceLastOnProcess()` stays small forever. **A wrong-but-live source is structurally invisible to it** — not a threshold that needs tuning, a question it does not ask (`C-165`). |
| `PipelineStatus` reports `Healthy` | `LinuxBluetoothService.cs:184-185`, `if (_nativeStream != null …) return BluetoothPipelineStatus.Healthy;` | The predicate is *"does a stream object exist"*. It has never been *"is the stream attached to the thing we asked for"*. `BluetoothHealthCheck.cs:35` then reports Healthy to `/health`. |
| The stream logs nothing | `PipeWireNativeStream.cs:196-200` | `PwStreamEvents.StateChanged` is **declared** at `PipeWireNative.cs:208` and **never assigned**. Only `Process` is wired. The stream cannot report a state change because nobody asked it to (`C-166`). |

⚠ **This is the third instance this month of the class `CLAUDE.md` § *Pre-Merge Review* exists for**, and
this one is worse than its two siblings. `AUD-2`'s silent gain/ducking miss and `SoundFlowMasterMixer`'s
imaginary detach were *log messages* asserting more than the code did. Here the untrue statement is made
by the operating system's own graph, which is the first thing a person reaches for and the last thing
they would think to doubt. **A fix that re-targets without making a wrong binding observable leaves the
next occurrence exactly as invisible**, and the next occurrence is guaranteed: `node.dont-reconnect` is a
request to a session manager this repository does not own, on a box whose WirePlumber configuration is
maintained by hand.

📌 **The repository has already written the observability and left it dead.**
`LinuxBluetoothService.cs:2097-2150`, `IsPwRecordLinkedToBtNode(string btNodeName)` — a `pw-link -l`
parse that answers exactly *"is this stream reading the node we meant?"*. It has **zero callers in the
tree** (verified by grep over all `.cs`). And on every failure path it returns `true`:

```csharp
      if (process == null)
      {
        return true; // assume ok if we can't check
      }
```

An unasked question that fails open. Task 5 replaces it with one that is asked, is testable, and fails
**closed**.

### 0.6 The other capture sources — enumerated, not assumed

Every capture path in the tree was walked. **`PipeWireNativeStream` is constructed in exactly one place**
(`LinuxBluetoothService.cs:1817`); no other source uses `target.object`/`PW_ID_ANY`. So `AUD-11` as
literally described is unique to Bluetooth. But the *class* — silently substituting a device for the one
that was asked for — is not.

| Source | Acquisition | On a missing device | Same class? |
|---|---|---|---|
| **BT (native)** | `LinuxBluetoothService.cs:1817` → `PipeWireNativeStream` | **silently re-links to the default source** | **this row** |
| BT (pw-record fallback) | `LinuxBluetoothService.cs:1856` | immune — `-P node.autoconnect=false`, plus an explicit re-link at `:1989` | no |
| **Vinyl / Radio (Raddy) / GenericUSB** | `VinylAudioSource.cs:68`, `RadioAudioSource.cs:89`, `GenericUSBAudioSource.cs:86` → `USBAudioSourceBase.cs:187-221` | ⚠ **silently captures `captureDevices[0]`** | **yes — different mechanism, same defect** |
| BT (Windows A2DP) | `WindowsBluetoothService.cs:855-911` | returns `null` cleanly at `:913-920`, no first-in-list fallback | no — the correct precedent |
| BT (Windows loopback) | `WasapiLoopbackCaptureSource.cs:63-64` | targets the default render endpoint **by design** | no |
| SDR | `SdrDeviceFactory.cs:87-96` | falls back to a **mock** device, loudly logged | not an audio capture path |

⚠ **The USB family is arguably worse than `AUD-11` and is NOT fixed here** (`C-167`, §6.1).
`USBAudioSourceBase.cs:207-214`:

```csharp
      // If no specific device found, use the default capture device if available
      if (targetDevice == null && captureDevices.Length > 0)
      {
        Logger.LogWarning(
          "Could not find USB capture device for port {USBPort}, using first available capture device",
          usbPort);
        targetDevice = captureDevices[0];
      }
```

That path at least warns. **The shipped configuration never reaches it.** `src/Radio.API/appsettings.json:50-55`
ships `"Radio": { "USBPort": "" }` and `"Vinyl": { "USBPort": "" }`, and in .NET
`anyString.Contains("")` is `true` — so the match loop at `USBAudioSourceBase.cs:199-200` succeeds on
the **first non-`"Monitor of"` device in enumeration order**, silently, and `:210`'s warning never fires.
On a stock box, Vinyl and Radio pick whatever MiniAudio enumerated first. That is a wrong-jack bug with
no warning at all, and it wants its own row rather than a rider on a PipeWire targeting fix.

Also noted and **also not this row**: `SoundFlowDeviceManager.cs:555` and `:747` mint device ids as
`$"capture-{i}"` from the raw enumeration index, and `:367` persists one as
`AudioPreferences:CurrentInput`, restored by exact-id match at `:135`. Indices shift when a device
disappears. It is **latent** — the only consumer is `DevicesController.cs:102` and no capture-open path
reads it — so it is a note, not a defect. §6.1 files both.

### 0.7 The estimate — **1.5 d**, and a box session that is not optional

| Work | Cost |
|---|---|
| Tasks 1–3 (props, zero-serial guard, `state_changed`) | 0.5 d |
| Tasks 4–6 (target-lost teardown, the peer audit, status + health) | 0.5 d |
| Task 7 (comments) + Task 8 (tests) | 0.5 d |

**What holds it to 1.5 d:** the hard parts already exist. `PipeWireRegistryListener` already delivers
`NodeDisappeared` (`:86`, `:303-345`) and it is currently *informational only* — `LinuxBluetoothService.cs:1576-1582`
says so in its own words — so the teardown trigger is a handler body, not a subscription.
`PwStreamEvents.StateChanged` is already at the right struct offset. `IsPwRecordLinkedToBtNode` is the
parser to copy. And the test fixture for the peer audit is **already written down**: the row's own
`pw-link -l` before/after lines are real measured output.

⚠ **What would push it to 2.5 d:** if `state_changed`'s C signature is not what Task 3 assumes
(`C-168`). That is a callback on the PipeWire thread; a wrong arity reads garbage rather than failing
loudly. Verify the header first — the command is in Task 3 — and if it differs, the delegate and every
call-site assumption move with it.

⚠ **And the box session is a separate cost the number above does not contain.** §4.5 is the only place
the central fix can be observed at all. It needs the owner, his handset, and an interruption to whatever
he is listening to.

### 0.8 ⛔ Not auto-mergeable. Three reasons, and none of them is "the tests might be flaky"

The repository's auto-merge policy allows a merge on green gates when UAT stands in for a user-flow
check. It does not apply here.

1. **The central fix cannot be observed by any gate this repository can run.** `node.dont-reconnect` is
   a request to WirePlumber. Every unit test in Task 8 pins *that we asked*; not one can pin *that it was
   honoured*. A fully green suite is consistent with the bug being completely unfixed.
2. **Task 4 changes what happens to live audio when a device event arrives.** A teardown predicate that
   is too broad kills working BT playback on a spurious registry event. That is a regression the suite
   cannot see and the owner would meet as silence.
3. **UAT requires the appliance, the handset, and an interruption.** The repro is *pause on the phone*,
   which per `AUD-10` destroys the transport — so verifying this row means deliberately breaking the
   owner's music while he is in the room.

**Merge decision goes to the owner after §4.5 runs.**

### 0.9 ⚠ Eight constraints found while planning — numbering continues from `C-160` (`AUD-4`)

**`C-161` narrows what `PW_ID_ANY` bought.** **`C-162`, `C-163`, `C-164` and `C-166` change the work.**
**`C-165` is why the existing watchdog cannot be the fix.** **`C-167` is a defect in a different
subsystem found while enumerating.** **`C-168` is an unverified external ABI and the plan's largest
single risk.**

---

**`C-161` — ⚠ NARROWS THE ROW'S PREMISE. `PW_ID_ANY` bought exactly one thing: it stopped an
`object.serial` being passed in the `targetId` parameter, which means node id. It did not enable
`target.object` resolution — that predated it — and it bought no binding guarantee whatsoever.**

Derivation in §0.3, with the `5353f020a` diff. **Consequence for this plan: there is no trade-off to
make.** The row asks for "a form that keeps what `PW_ID_ANY` bought while refusing the wrong node", and
anticipates the two might conflict. They do not. `PW_ID_ANY` stays exactly as it is; the refusal is a new
property in the props string. ⛔ **The one move that would un-buy it is `pw_stream_connect(…, _targetNodeId, …)`.
Never write that.**

---

**`C-162` — ⚠ CHANGES THE WORK. The `pw-record` fallback is immune to this bug and the native path that
replaced it is not, because the native path inherited neither of the two defences.**

`LinuxBluetoothService.cs:1856` passes `-P node.autoconnect=false`; `LinkPipeWireRecordToBtNode`
(`:1989-2012`) re-links explicitly afterwards and its own doc comment at `:1985-1987` names the exact
failure this row reports. PR #262 replaced the subprocess with a native stream and set
`node.autoconnect = true`. **The fix is to give the native path an equivalent of the first defence** —
`node.dont-reconnect`, which is stronger than `autoconnect = false` because it also refuses a *later*
move. §1.1.

---

**`C-163` — ⚠ CHANGES THE WORK. A missing `object.serial` is silently converted to the target `0`,
which is a second and independent route into the same wrong jack.**

`LinuxBluetoothService.cs:1814`, `var nodeId = (uint)(targetSerial > 0 ? targetSerial : 0);`.
`ParsePwCliOutputForBtNode` returns serial `0` at `:1724` and `:1789` while still returning a valid node
**name**. §0.4. Task 2 refuses it and routes to the fallback, which can target by name.

---

**`C-164` — ⚠⚠ TWO COMMENTS ASSERT THAT A REGISTRY GLOBAL ID IS USABLE AS `target.object`. IT IS NOT,
AND ACTING ON THEM WOULD INTRODUCE THIS EXACT BUG.**

`PipeWireRegistryListener.cs:16`:

```csharp
  /// <summary>PipeWire registry global id (also used as object.serial for streams).</summary>
```

and `LinuxBluetoothService.cs:1555-1557`:

> *"The registry's global id doubles as the object.serial that PipeWireNativeStream.Connect needs as
> target.object."*

PipeWire's `pipewire-props(7)` is explicit that these are different keys taking different values:
`target.object` takes `<node.name|object.serial>`, while the **deprecated** `node.target` takes
`<node.name|object.id>` — described as *"This property is deprecated, the target.object property should
be used instead, which uses the more unique object.serial as a possible target."* A global id is reused
after an object is destroyed; a serial is not. They are not the same number.

⭐ **Today the code is right and only the comments are wrong**, which is the dangerous configuration.
`SearchForCaptureDeviceAsync:1305` gets its serial from `ParsePwCliOutputForBtNode`, which reads real
`object.serial =` lines (`:1746-1754`); the registry path's `Id` only feeds the autoswitch gate
(`:1569-1573`) and never reaches `target.object`. **The comments describe a shortcut nobody has taken
yet, and taking it would produce an unresolvable `target.object` — i.e. this row.** This is precisely the
`CLAUDE.md` § *Pre-Merge Review* class: a remark asserting a property the code does not have. Task 7
corrects both.

---

**`C-165` — `BluetoothCaptureWatchdog` cannot be extended to catch this, and trying is the obvious wrong
turn.**

`BluetoothCaptureWatchdog.cs:88` compares `snapshot.Value.ElapsedMs` against a stall threshold, and
`GetCaptureStreamSnapshot` (`LinuxBluetoothService.cs:215-224`) returns nothing but an address and an
elapsed time. **The line-in delivers callbacks perfectly**, so no threshold value distinguishes the two
cases — the watchdog is asking about liveness and this row is about identity. ⛔ **Do not "fix" this by
lowering `OnProcessStallThresholdMs`.** It would fire on healthy BT and never on this. The identity
question needs a different observer, which is Tasks 4 and 5.

---

**`C-166` — ⚠ CHANGES THE WORK. The stream's `state_changed` callback has been declared and unwired
since PR #262, so the stream has never been able to report an error at all.**

`PipeWireNative.cs:208` declares `public IntPtr StateChanged;` in `PwStreamEvents`.
`PipeWireNativeStream.cs:196-200` assigns only `Version` and `Process`. Every field left `IntPtr.Zero`
is a callback PipeWire will not invoke. So `PW_STREAM_STATE_ERROR` — including the one Task 1's
`node.dont-reconnect` is *supposed* to produce when the target is destroyed — currently goes nowhere.
**Task 1 without Task 3 makes the failure loud to PipeWire and still silent to us**, which is why they
are not separable.

---

**`C-167` — the same defect class exists in the USB capture family, is worse there, and is NOT fixed
here.** §0.6. `USBAudioSourceBase.cs:213` falls back to `captureDevices[0]`, and the shipped
`"USBPort": ""` (`src/Radio.API/appsettings.json:51`, `:54`) makes `Contains("")` match the first device
before the warning at `:210` is ever reached. Not fixed here because it is MiniAudio rather than
PipeWire, it touches three user-facing sources, and *"what should an empty `USBPort` mean"* is a
config-surface decision belonging to the owner. §6.1 files it.

---

**`C-168` — ⚠⚠ THE `state_changed` C SIGNATURE IS THIS PLAN'S ONE UNVERIFIED EXTERNAL ABI. Verify it
before writing Task 3, not after.**

`pw_stream_state`'s enumerator values **are** verified from PipeWire's published API reference
(`PW_STREAM_STATE_ERROR = -1`, `UNCONNECTED = 0`, `CONNECTING = 1`, `PAUSED = 2`, `STREAMING = 3`), as
is `uint32_t pw_stream_get_node_id(struct pw_stream *stream)`. **The `state_changed` member's signature
is not** — the reference page does not render `struct pw_stream_events`' members, and this plan did not
have a machine with the headers on it. Task 3 assumes:

```c
void (*state_changed)(void *data, enum pw_stream_state old,
                      enum pw_stream_state state, const char *error);
```

⚠ **A wrong arity here does not throw.** Under the x86-64 SysV convention the callee reads whatever
registers hold, so a mismatched delegate produces plausible garbage on the PipeWire thread rather than a
crash — the same failure shape as the rest of this row. **Builder verifies first, with one command:**

```bash
grep -A6 "state_changed" /usr/include/pipewire-0.3/pipewire/stream.h
```

This is a read of a header on the box and touches nothing. If the signature differs, adjust the delegate
and say so in the PR body.

### 0.10 Things Builder must NOT do

- ⛔ **Do not pass `_targetNodeId` as `pw_stream_connect`'s `targetId` again.** `C-161`. That is the one
  regression PR #262 already paid for.
- ⛔ **Do not remove `node.autoconnect = true`** as an alternative to Task 1 without measuring it. It is
  plausible that `autoconnect = false` alone would stop the fallback, and it is also plausible that it
  stops the stream connecting *at all* on a box whose WirePlumber rules assume the current shape
  (`deploy/common/41-disable-bt-input-restore-target.lua` exists precisely because this graph is
  hand-tuned). §6.2 records it as the fallback option if Task 1 does not hold on the box.
- ⛔ **Do not lower `OnProcessStallThresholdMs`.** `C-165`.
- ⛔ **Do not touch `USBAudioSourceBase.cs` or the `Devices:*:USBPort` defaults.** `C-167`, §6.1.
- ⛔ **Do not delete `deploy/common/41-disable-bt-input-restore-target.lua` or the companion
  `90-disable-bt-input-autolink.lua`.** They solve the *opposite* direction — the BT node auto-linking to
  the default **sink** — and both are documented as needed. Nothing in this row replaces them.
- ⛔ **Do not edit `CLAUDE.md`.** §6.4 files the one correction it wants, with the reason.
- ⛔ **Do not edit `docs/BUILDER_QUEUE.md` or `docs/queue/AUD-11.md` from this plan.** §8 carries the
  wording for whoever updates them.
- ⛔ **Do not run anything against `radio` until §4.5, and not then without the owner present.**

---

## 1. Decision

### 1.1 The targeting form — additive, and there is no trade-off to make

**Add `node.dont-reconnect = true` to the props string. Keep `PW_ID_ANY`. Keep `target.object`. Keep
`node.autoconnect = true`.**

From `pipewire-props(7)`, verbatim:

> **`node.dont-reconnect`** — "When the node has a target configured and the target is destroyed, destroy
> the node as well. This property also inhibits that the node is moved to another sink/source."

Both sentences are wanted, and the **second** is the one this row is about: *inhibits that the node is
moved to another sink/source*. That is the property whose entire purpose is refusing the substitution
`AUD-11` reports.

Four options were considered.

| Option | Verdict |
|---|---|
| **`node.dont-reconnect = true`** ✅ | **Taken.** Purpose-built, documented, one line, additive — `C-161` shows there is nothing to give up. Keeps the stream's identity as *"the node I asked for, or nothing"*. |
| **Revert to `pw_stream_connect(…, _targetNodeId, …)`** | **Rejected.** Un-buys PR #262's fix by putting a serial back in an id parameter (`C-161`), and the row explicitly forbids it. |
| **Drop `node.autoconnect`, link manually with `pw-link`** | **Rejected as the primary.** It is what the `pw-record` fallback does (`:1989-2012`), and it is exactly the subprocess link-management PR #262 existed to delete. It also races: between connect and re-link there is a window where the wrong link is live and audible. Kept in reserve — §6.2. |
| **Post-connect verify and tear down, with no property change** | **Rejected as the primary, adopted as the backstop.** Detection after the fact still means recording the wrong jack for the detection interval. But it is the only thing that survives a session manager that ignores the property, so it ships too — as Tasks 4 and 5. |

⚠ **The honest limitation, and it goes in the PR body.** `node.dont-reconnect` is a request. Whether the
session manager honours it is WirePlumber's business, on a box whose WirePlumber configuration is
hand-maintained and version-pinned (`CLAUDE.md`: `bluez.lua` is patched, and the patch is *"lost on WP
package upgrade"*). **That is the entire reason Tasks 4 and 5 are not optional.** The property is the
fix; the verification is what makes the fix falsifiable and what catches the day it stops working.

### 1.2 What should happen when the target is gone — **tear down and park in `WaitingForCaptureNode`**

The row offers stop, retry, or an explicit waiting state. Recommendation: **all three, in that order —
tear the stream down immediately (stop), enter an explicit `WaitingForCaptureNode` status (park), and
let the existing node-appearance event re-arm it (retry, but event-driven, not polled).**

**Why not stop-and-stay-stopped.** Per `AUD-10`, the node vanishes on every *pause*. The owner pauses
music constantly. A terminal stop would make Bluetooth single-use in a second, self-inflicted way.

**Why not a blind retry loop.** The retry machinery already exists and is already known to misbehave in
exactly this situation: `SearchForCaptureDeviceAsync` retries 20 × 1 s (`:1289-1401`) and
`MonitorBtPipelineAsync` retries 3 × 30 s (`:245-312`). Project memory records what that costs —
`project_autoswitch_bt_bug.md`: *"switches to BT source even when no PipeWire capture node exists,
causing hours of failed retries + triggers capture lifecycle degradation."* Re-entering that loop the
instant the node disappears is the failure mode we already have a note about.

**Why park, and why it is cheap.** The re-arm path is already built and already correct:
`PipeWireRegistryListener.NodeAppeared` → `OnRegistryNodeAppeared` (`:1559-1574`) → `CaptureNodeAvailable`
→ `BluetoothAutoSwitchService` (`:142-181`). And the tear-down trigger is already delivered and
deliberately unused — `OnRegistryNodeDisappeared` (`:1583-1593`), whose own doc comment says
*"Currently informational + metric-only"*. **Parking turns a fired-and-ignored event into the state
machine's edge**, which is a handler body rather than new infrastructure.

⭐ **Tearing down is the safety property, not an optimisation.** A parked stream must not be *connected*.
As long as the `pw_stream` exists with `node.autoconnect` set, PipeWire may bind it to something; the
only state in which the wrong jack is impossible is the state where there is no stream.

⚠ **`NodeDisappeared` fires on the PipeWire thread loop.** `PipeWireRegistryListener.cs:81-83` says so
and says what to do: *"consumers should marshal heavy work onto the thread pool if needed."* Task 4 does.
Calling `PipeWireNativeStream.Stop()` inline would run `pw_thread_loop_stop` on one loop from inside
another loop's callback — do not.

### 1.3 Observability — three signals, and which of them reaches journald

Co-equal with the fix, per the row. Three signals, deliberately independent, so no single mechanism
failing takes the diagnosis with it.

| Signal | Task | Level | Sink |
|---|---|---|---|
| Stream state transitions, incl. `PW_STREAM_STATE_ERROR` + PipeWire's own error string | 3 | **Warning** on error, Information otherwise | **Warning → journald**; Information → file only |
| Target lost / stream torn down and parked | 4 | **Warning** | **journald** |
| Peer audit: *"bound to X, expected Y"* | 5 | **Warning** on mismatch, Debug on match | mismatch → **journald**; match → **nothing** |
| `PipelineStatus` = `WaitingForCaptureNode`, `/health` degraded | 6 | n/a | HTTP |

⚠ **Sink asymmetry, per `CLAUDE.md` § *Deployment*.** Every log line in this row lives in
**`Radio.Infrastructure`**, loaded by **`Radio.API`**. `Radio.API`'s console sink is restricted to
Warning (`Radio.API/Program.cs:48-53`), and under systemd the console *is* the journal. So:

- **Warning and above reach `journalctl -u radio-api`.** All four "something is wrong" lines are
  Warning, deliberately — they must be visible to the first command a person runs.
- **Information reaches the file sink only** (`/opt/radio-console/logs/radio-*.txt`). The healthy-state
  transitions sit there, where they cost nothing.
- ⛔ **The healthy path must not log at Warning, and the audit must not log at all when it passes.** Log
  volume on this box correlates with audible audio distortion (`CLAUDE.md`, and the memory note on SSH
  activity). A per-tick "still correctly bound" line would trade this row's bug for a worse one.
- ⚠ **`Radio.Web`'s console sink has no level restriction** (`PHN-5` `C-93`) — its Information lines *do*
  reach journald. **Nothing in this row is in `Radio.Web`**, so it does not apply; it is stated so that a
  Builder who moves a line there knows the rule changes underneath them.

---

## 2. Tasks

### Task 1 — the props string becomes testable, and gains the one property that refuses

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs`

Replace `:203-205`, currently:

```csharp
    // Create stream with properties targeting our node
    var propsStr = $"{{ media.type = Audio media.category = Capture media.role = Music node.autoconnect = true target.object = {_targetNodeId} }}";
    var props = pw_properties_new_string(propsStr);
```

with a call to a new pure static:

```csharp
    // Create stream with properties targeting our node (AUD-11: see BuildStreamProperties).
    var propsStr = BuildStreamProperties(_targetNodeId);
    var props = pw_properties_new_string(propsStr);
```

and add the static next to it:

```csharp
  /// <summary>
  /// Builds the <c>pw_properties</c> string for the capture stream.
  /// </summary>
  /// <remarks>
  /// ⚠ Extracted as a pure static so AUD-11's targeting contract can be pinned by a unit test with no
  /// PipeWire daemon and no native library — the same reason
  /// <c>LinuxBluetoothService.ParsePwCliOutputForBtNode</c> is a static. Radio.Infrastructure.Tests
  /// targets net10.0, so WINDOWS_TARGET is undefined there and this file compiles into the test build
  /// on a Windows dev box; only a native CALL would fail, and this method makes none.
  ///
  /// ⚠ node.dont-reconnect = true IS THE AUD-11 FIX and it is the only token here that refuses
  /// anything. pipewire-props(7): "When the node has a target configured and the target is destroyed,
  /// destroy the node as well. This property also inhibits that the node is moved to another
  /// sink/source." The SECOND sentence is the one this row needs. Without it, node.autoconnect = true
  /// is an instruction with no failure mode: when target.object cannot be resolved the session manager
  /// satisfies it with the DEFAULT source, which on the appliance is
  /// alsa_input.pci-0000_00_1f.3.analog-stereo — the unplugged line-in. Measured live 2026-09-06.
  ///
  /// ⚠ node.autoconnect STAYS. Removing it instead was considered and rejected without measurement:
  /// this box's WirePlumber graph is hand-tuned (deploy/common/41-disable-bt-input-restore-target.lua)
  /// and it is not established that the stream would connect at all without it. Plan AUD-11 §6.2 holds
  /// that option in reserve.
  ///
  /// ⚠ targetNodeId is an object.serial, NOT a registry global id. pipewire-props(7) gives
  /// target.object as &lt;node.name|object.serial&gt;, and says the DEPRECATED node.target is the one
  /// that took an object.id. A global id here would resolve to nothing, and "resolves to nothing" plus
  /// node.autoconnect is precisely the AUD-11 failure. See plan AUD-11 C-164.
  ///
  /// ⛔ Do NOT respond to a targeting problem by passing targetNodeId to pw_stream_connect's targetId
  /// argument. That parameter means node id, and handing it a serial is the bug PR #262 (commit
  /// 5353f020a) already paid to remove. See C-161.
  /// </remarks>
  internal static string BuildStreamProperties(uint targetNodeId) =>
    $"{{ media.type = Audio media.category = Capture media.role = Music "
    + "node.autoconnect = true node.dont-reconnect = true "
    + $"target.object = {targetNodeId} }}";
```

⛔ **Leave `:234-239` exactly as they are.** `PW_ID_ANY` and the flags do not change (`C-161`). Add one
line to the existing comment there so the next reader does not undo the reasoning:

```csharp
      // Use PW_ID_ANY so PipeWire resolves target from the target.object property.
      // ⛔ AUD-11: this stays. It is not the cause of the wrong-jack binding — see plan AUD-11 C-161.
      // The refusal lives in node.dont-reconnect, in BuildStreamProperties above.
      const uint PW_ID_ANY = 0xffffffff;
```

---

### Task 2 — a serial of zero stops meaning "any node you like"

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`

Replace `:1814`, currently `var nodeId = (uint)(targetSerial > 0 ? targetSerial : 0);`:

```csharp
    // AUD-11 C-163: targetSerial == 0 means "pw-cli gave us the node NAME but we never saw its
    // object.serial line" (ParsePwCliOutputForBtNode returns exactly that at :1724 and :1789). The old
    // ternary turned that into the literal target `target.object = 0`, which matches no node — and an
    // unresolvable target.object plus node.autoconnect is how this stream ends up on the built-in
    // line-in. Prefer the pw-record fallback: it targets by node NAME (a valid target.object value) and
    // it runs with node.autoconnect=false, so it cannot be moved. See :1985-1987, which has described
    // this exact substitution since March.
    if (targetSerial <= 0)
    {
      _logger.LogWarning(
        "BT capture: no object.serial for node {Node} — refusing a native stream whose target.object "
        + "would be unresolvable, using the pw-record fallback (targets by name) instead. See AUD-11.",
        targetNode);
      _metricsCollector?.Increment("bluetooth.capture_target_serial_missing_total");
      StartCaptureSubprocessFallback(generator, format, targetNode, targetSerial);
      return;
    }

    var nodeId = (uint)targetSerial;
```

⚠ **Placement matters.** This goes exactly where `var nodeId = …` is now — *after*
`StopCaptureSubprocess()` and `_captureCts = new CancellationTokenSource()` (`:1804-1806`) and after
`generator.PreFillSilence(0.5f)` (`:1809`), because `StartCaptureSubprocessFallback` dereferences
`_captureCts!` at `:1851`. Moving it earlier is a null-reference on the fallback path.

**Defence in depth**, in `PipeWireNativeStream`'s constructor (`PipeWireNativeStream.cs:132`, first
statement of the body):

```csharp
    // AUD-11 C-163: 0 is not a valid object.serial. Accepting it writes `target.object = 0`, which
    // resolves to nothing, and with node.autoconnect the session manager then picks the default source.
    // The caller is expected to have refused this already (LinuxBluetoothService.StartCaptureSubprocess);
    // this guard exists so a FUTURE caller cannot reintroduce the same silent substitution.
    ArgumentOutOfRangeException.ThrowIfZero(targetNodeId);
```

⚠ **Know what this guard does and does not do.** `StartCaptureSubprocess` wraps the construction in
`catch (Exception ex)` at `:1835` and falls back to `pw-record`, so a throw here is *diverted*, not
surfaced. That is an acceptable outcome — the fallback is the immune path — but it means **the guard is
not the diagnostic; the `LogWarning` above is.** Do not delete the caller-side check on the grounds that
the constructor covers it.

---

### Task 3 — wire `state_changed`, so the stream can report an error at all

**⚠ Run `C-168`'s header check before writing any of this.**

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNative.cs`

Add next to the existing registry delegates:

```csharp
  /// <summary>
  /// <c>enum pw_stream_state</c> from &lt;pipewire/stream.h&gt;. Values verified against PipeWire's
  /// published API reference; <c>ERROR</c> is negative, so the marshalled parameter must be a signed
  /// <c>int</c> and not a <c>uint</c>.
  /// </summary>
  public enum PwStreamState
  {
    Error = -1,
    Unconnected = 0,
    Connecting = 1,
    Paused = 2,
    Streaming = 3,
  }

  /// <summary>
  /// <c>pw_stream_events.state_changed</c>:
  /// <c>void (*)(void *data, enum pw_stream_state old, enum pw_stream_state state, const char *error)</c>.
  /// </summary>
  /// <remarks>
  /// ⚠ This signature was NOT verified from a header when the plan was written (AUD-11 C-168) — the
  /// published API reference does not render struct pw_stream_events' members. It was confirmed against
  /// /usr/include/pipewire-0.3/pipewire/stream.h before this code was committed; if that file ever
  /// disagrees, this delegate is what must change. A wrong arity here does NOT throw: under the x86-64
  /// SysV convention the callee simply reads whatever registers hold, so the failure is plausible
  /// garbage on the PipeWire thread — the same shape as the bug this row fixes.
  /// </remarks>
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
  public delegate void PwStreamStateChangedDelegate(
    IntPtr userData, int oldState, int newState, IntPtr error);

  /// <summary>Returns the global id of the stream's node, or PW_ID_ANY if not yet connected.</summary>
  [DllImport(PipeWireLib, CallingConvention = CallingConvention.Cdecl)]
  public static extern uint pw_stream_get_node_id(IntPtr stream);
```

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireNativeStream.cs`

Keep the delegate alive for the stream's lifetime, exactly as `_processDelegate` is
(`PipeWireNativeStream.cs:92`):

```csharp
  // Pinned delegate references to prevent GC collection during native callbacks
  private readonly ProcessDelegate _processDelegate;
  // AUD-11 C-166: state_changed was declared in PwStreamEvents from PR #262 and never assigned, so the
  // stream has never been able to report PW_STREAM_STATE_ERROR — including the error that
  // node.dont-reconnect is supposed to raise when the target is destroyed. Same GC-lifetime rule as
  // _processDelegate: Marshal.GetFunctionPointerForDelegate only stays valid while the delegate object
  // is reachable.
  private readonly PwStreamStateChangedDelegate _stateChangedDelegate;
```

In the constructor, beside `_processDelegate = OnProcess;` (`:155`):

```csharp
    _stateChangedDelegate = OnStateChanged;
```

In `Start()`, extend the events struct (`:196-200`):

```csharp
    _events = new PwStreamEvents
    {
      Version = PW_STREAM_EVENTS_VERSION,
      StateChanged = Marshal.GetFunctionPointerForDelegate(_stateChangedDelegate),
      Process = Marshal.GetFunctionPointerForDelegate(_processDelegate)
    };
```

Add a public surface for the two things the rest of the system needs, and the handler:

```csharp
  /// <summary>
  /// The stream's most recent PipeWire state. <see cref="PwStreamState.Unconnected"/> until the first
  /// state_changed callback. Safe to read from any thread.
  /// </summary>
  public PwStreamState State => (PwStreamState)Volatile.Read(ref _state);

  /// <summary>
  /// PipeWire's own error string from the last transition into
  /// <see cref="PwStreamState.Error"/>, or null. Safe to read from any thread.
  /// </summary>
  public string? LastError => Volatile.Read(ref _lastError);

  /// <summary>The intended capture target, as passed to the constructor. Used by the AUD-11 peer
  /// audit to say what it EXPECTED, not just what it found.</summary>
  public uint TargetNodeSerial => _targetNodeId;

  private int _state = (int)PwStreamState.Unconnected;
  private string? _lastError;

  /// <summary>
  /// Fires on the PipeWire thread loop for every stream state transition.
  /// </summary>
  /// <remarks>
  /// ⚠ Must not throw and must not block — this runs on the loop that also drives OnProcess.
  /// ⚠ AUD-11: an ERROR transition is the loud form of this row's defect. With
  /// node.dont-reconnect = true, a destroyed target is supposed to arrive here rather than being
  /// silently satisfied by the default source. That makes this handler the first place a person can
  /// SEE the target go away — but it is not the only guard, because whether the property is honoured
  /// is WirePlumber's decision. The peer audit is the independent one.
  /// </remarks>
  private static void OnStateChanged(IntPtr userData, int oldState, int newState, IntPtr error)
  {
    if (userData == IntPtr.Zero)
    {
      return;
    }

    PipeWireNativeStream? self;
    try
    {
      self = GCHandle.FromIntPtr(userData).Target as PipeWireNativeStream;
    }
    catch
    {
      return;
    }
    if (self == null)
    {
      return;
    }

    try
    {
      var message = error == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(error);
      Volatile.Write(ref self._state, newState);
      Volatile.Write(ref self._lastError, newState == (int)PwStreamState.Error ? message : null);

      if (newState == (int)PwStreamState.Error)
      {
        // Warning, so it reaches `journalctl -u radio-api` — Radio.API's console sink is restricted to
        // Warning and above (Program.cs:48-53) and under systemd the console IS the journal.
        self._logger.LogWarning(
          "PipeWire stream error: {Old} -> {New} for target.object {Serial}: {Error}",
          (PwStreamState)oldState, (PwStreamState)newState, self._targetNodeId,
          message ?? "(no message)");
      }
      else
      {
        // Information: file sink only. Deliberately quiet — log volume on this box correlates with
        // audible audio distortion, and healthy transitions are a handful per capture session.
        self._logger.LogInformation(
          "PipeWire stream state: {Old} -> {New} (target.object {Serial})",
          (PwStreamState)oldState, (PwStreamState)newState, self._targetNodeId);
      }
    }
    catch
    {
      // Must not throw on the PipeWire thread loop.
    }
  }
```

---

### Task 4 — the target disappearing stops being informational

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`

Replace the body of `OnRegistryNodeDisappeared` (`:1583-1593`) and correct its doc comment, whose first
sentence — *"Currently informational + metric-only"* — this task falsifies:

```csharp
  /// <summary>
  /// Handler for <see cref="PipeWireRegistryListener.NodeDisappeared"/>.
  /// </summary>
  /// <remarks>
  /// ⚠ AUD-11: this was informational + metric-only until 2026-09-06, and that is why the defect was
  /// silent. When the bluez_input node goes away, `radio-bt-stream` does not fail — PipeWire re-links
  /// it to the DEFAULT source and it keeps recording an unplugged jack, [active], with every downstream
  /// indicator green. node.dont-reconnect (BuildStreamProperties) is supposed to stop that at the
  /// PipeWire layer; this handler is the independent guard, because whether the property is honoured
  /// is the session manager's decision and not ours.
  ///
  /// ⭐ Tearing the stream down is a SAFETY property, not tidiness. While a pw_stream with
  /// node.autoconnect exists, PipeWire may bind it to something. The only state in which the wrong
  /// jack is impossible is the state where there is no stream.
  ///
  /// ⚠ This fires on the PipeWire thread loop (see PipeWireRegistryListener's own event docs), so the
  /// teardown is marshalled onto the thread pool. Calling PipeWireNativeStream.Stop() inline would run
  /// pw_thread_loop_stop on the capture loop from inside the registry loop's callback.
  ///
  /// ⛔ Re-acquisition is NOT started here. It is event-driven via NodeAppeared → CaptureNodeAvailable
  /// → BluetoothAutoSwitchService, which already exists. Kicking the retry loop from here reproduces
  /// the "hours of failed retries" recorded in project_autoswitch_bt_bug.md — the node genuinely is
  /// gone, and asking 20 more times does not change that. See plan AUD-11 §1.2.
  /// </remarks>
  private void OnRegistryNodeDisappeared(object? sender, BtNodeRegistryEventArgs e)
  {
    lock (_knownNodesLock)
    {
      _knownNodeAddresses.Remove(e.DeviceAddress);
    }
    _metricsCollector?.Increment("bluetooth.capture_node_disappeared_total");
    _logger.LogInformation(
      "PW registry: BT node disappeared id={Id} address={Address}",
      e.Id, e.DeviceAddress);

    // Only act when the node that vanished is the one we are actually capturing. A registry removal
    // for some other device must not touch a healthy stream.
    var connected = ConnectedDevice;
    if (_nativeStream == null
      || connected == null
      || !string.Equals(connected.Address, e.DeviceAddress, StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    _metricsCollector?.Increment("bluetooth.capture_target_lost_total");
    _logger.LogWarning(
      "BT capture target lost: node for {Address} left the PipeWire registry while a native capture "
      + "stream was running. Tearing the stream down and parking in WaitingForCaptureNode rather than "
      + "letting PipeWire re-link it to the default source. See AUD-11.",
      e.DeviceAddress);

    _captureTargetLost = true;
    var address = e.DeviceAddress;

    _ = Task.Run(() =>
    {
      try
      {
        StopCaptureSubprocess();
        CaptureTargetLost?.Invoke(this, new CaptureTargetLostEventArgs
        {
          DeviceAddress = address,
          Reason = CaptureTargetLostReason.NodeRemoved,
        });
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "BT capture target-lost teardown failed for {Address}", address);
      }
    });
  }
```

Add the backing field beside the other capture flags (near `_captureIntentionallyStopped`):

```csharp
  // AUD-11: set when the capture target left the registry under a running stream; cleared by
  // GetAudioCaptureDeviceAsync, which is the one entry point that means "someone is asking for capture
  // again". Deliberately mirrors _captureIntentionallyStopped's lifecycle so PipelineStatus has one
  // idiom rather than two.
  private volatile bool _captureTargetLost;
```

Clear it in `GetAudioCaptureDeviceAsync` beside the existing clear at `:1210-1211`:

```csharp
    // Clear the intentional-stop flag — someone is requesting capture again
    _captureIntentionallyStopped = false;
    _captureTargetLost = false;
```

**File:** `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs`

Add the event and its args beside the existing capture events:

```csharp
  /// <summary>
  /// Raised when the capture stream's intended PipeWire target went away and the stream was torn
  /// down rather than being allowed to re-link to another source. AUD-11.
  /// </summary>
  event EventHandler<CaptureTargetLostEventArgs>? CaptureTargetLost;
```

```csharp
/// <summary>Why a capture target was lost. AUD-11.</summary>
public enum CaptureTargetLostReason
{
  /// <summary>The target node left the PipeWire registry.</summary>
  NodeRemoved,

  /// <summary>A post-connect audit found the stream bound to a node other than the intended one.</summary>
  WrongPeerBound,
}

/// <summary>Payload for <see cref="IBluetoothService.CaptureTargetLost"/>. AUD-11.</summary>
public class CaptureTargetLostEventArgs : EventArgs
{
  public required string DeviceAddress { get; init; }
  public required CaptureTargetLostReason Reason { get; init; }

  /// <summary>The node the stream was actually bound to, when known. Null for
  /// <see cref="CaptureTargetLostReason.NodeRemoved"/>.</summary>
  public string? BoundPeerNodeName { get; init; }
}
```

⚠ **Three other implementations of `IBluetoothService` must declare the event or they will not
compile:** `WindowsBluetoothService.cs`, `MockBluetoothService.cs`, `BluetoothServiceFactory.cs` (each
already carries a `PipelineStatus` stub — add the event next to it, never raised).

---

### Task 5 — the peer audit: the stream learns to say what it is actually attached to

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`

⛔ **Delete `IsPwRecordLinkedToBtNode` (`:2097-2150`).** It is the right question, it has **zero callers
in the tree**, it is hard-coded to `pw-record:input_FL`, and it returns `true` on every failure path —
*"assume ok if we can't check"* at `:2114` and `:2148`. **A check that fails open is the defect class this
row is about, one layer down.** Replace it with a testable static plus a caller that fails closed.

```csharp
  /// <summary>
  /// Parses <c>pw-link -l</c> output and returns the distinct node names feeding the given stream's
  /// input ports. Extracted as a static for testability — same reason as
  /// <see cref="ParsePwCliOutputForBtNode"/>.
  /// </summary>
  /// <remarks>
  /// ⚠ AUD-11. This is the ONLY check in the system that observes the stream's actual PEER rather than
  /// its liveness. BluetoothCaptureWatchdog cannot answer this: it compares elapsed time since the last
  /// OnProcess callback (BluetoothCaptureWatchdog.cs:88), and the built-in line-in delivers callbacks
  /// perfectly on schedule, so a wrong-but-live source is invisible to it at every threshold (C-165).
  ///
  /// ⚠ TWO OUTPUT SHAPES ARE ACCEPTED, deliberately. The existing parsers in this file
  /// (DisconnectAllLinksToPort:2039-2064) read the indented form:
  ///     radio-bt-stream:input_FL
  ///       |&lt;- bluez_input.B0_D5_FB_D2_0D_68.2:output_FL
  /// while the evidence measured on the box for AUD-11 is the single-line form:
  ///     radio-bt-stream:input_FL  &lt;- bluez_input.B0_D5_FB_D2_0D_68.2:output_FL   [active]
  /// The shape varies by pw-link version and flags, and the plan did not have a box to settle it on.
  /// Accepting both costs four lines and removes an entire class of "the audit silently found nothing".
  ///
  /// ⛔ Returning an empty set means "no peers found", which callers MUST treat as a failed audit and
  /// not as a pass. The method it replaced (IsPwRecordLinkedToBtNode) returned true — "assume ok" — on
  /// every failure path, which is why it could never have caught this.
  /// </remarks>
  internal static IReadOnlyCollection<string> ParsePwLinkOutputForStreamPeers(
    string pwLinkOutput, string streamNodeName)
  {
    var peers = new HashSet<string>(StringComparer.Ordinal);
    var inputPrefix = streamNodeName + ":input";
    var inTargetPort = false;

    foreach (var rawLine in pwLinkOutput.Split('\n'))
    {
      var line = rawLine.TrimEnd();
      var trimmed = line.Trim();

      // Single-line form: "<our port>  <- <peer port>   [active]"
      var arrowIdx = trimmed.IndexOf("<-", StringComparison.Ordinal);
      if (arrowIdx > 0 && trimmed.StartsWith(inputPrefix, StringComparison.Ordinal))
      {
        AddPeerNode(peers, trimmed[(arrowIdx + 2)..]);
        inTargetPort = false;
        continue;
      }

      // Indented form: our port on its own line, then "  |<- <peer port>" continuation lines.
      if (trimmed.StartsWith(inputPrefix, StringComparison.Ordinal) && arrowIdx < 0)
      {
        inTargetPort = true;
        continue;
      }

      if (inTargetPort && trimmed.StartsWith("|<-", StringComparison.Ordinal))
      {
        AddPeerNode(peers, trimmed[3..]);
        continue;
      }

      // A non-indented, non-empty line ends the current port's continuation block.
      if (!line.StartsWith("  ", StringComparison.Ordinal) && line.Length > 0)
      {
        inTargetPort = false;
      }
    }

    return peers;
  }

  /// <summary>
  /// Strips the trailing "[active]"/"[inactive]" marker and the ":port" suffix from a pw-link peer
  /// token, leaving the bare node name. Empty tokens are dropped.
  /// </summary>
  private static void AddPeerNode(HashSet<string> peers, string token)
  {
    var value = token.Trim();

    var bracketIdx = value.IndexOf('[');
    if (bracketIdx >= 0)
    {
      value = value[..bracketIdx].TrimEnd();
    }

    // Node names contain dots but not colons; the port suffix is everything after the LAST colon.
    var colonIdx = value.LastIndexOf(':');
    if (colonIdx > 0)
    {
      value = value[..colonIdx];
    }

    if (value.Length > 0)
    {
      peers.Add(value);
    }
  }
```

And the caller, which fails closed:

```csharp
  /// <summary>
  /// Audits what <c>radio-bt-stream</c> is actually linked to and raises
  /// <see cref="CaptureTargetLost"/> when it is anything other than <paramref name="expectedNodeName"/>.
  /// </summary>
  /// <remarks>
  /// ⚠ AUD-11's second, independent guard. node.dont-reconnect is a request to WirePlumber, whose
  /// configuration on this box is hand-maintained and whose bluez.lua patch is documented as "lost on
  /// WP package upgrade". This audit is what notices the day the request stops being honoured.
  ///
  /// ⚠ Runs ONCE per capture acquisition, on the existing +1500 ms deferred task that already shells
  /// out to `pw-link -l` for DisconnectPipeWireBtAutoLinks — so it adds no new subprocess. It is NOT
  /// periodic: log volume and subprocess churn on this box correlate with audible audio distortion,
  /// and the ongoing case is covered event-free by OnRegistryNodeDisappeared.
  ///
  /// ⛔ Fails CLOSED. An empty peer set, an unparseable output, or a dead pw-link is a FAILED audit
  /// that logs at Warning — not a pass. Its predecessor returned "assume ok if we can't check", which
  /// is why it never fired.
  /// </remarks>
  private void AuditCaptureStreamPeer(string expectedNodeName, string deviceAddress)
  {
    const string StreamNodeName = "radio-bt-stream";

    string output;
    try
    {
      var psi = new ProcessStartInfo
      {
        FileName = "pw-link",
        Arguments = "-l",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };
      using var process = Process.Start(psi);
      if (process == null)
      {
        _logger.LogWarning(
          "BT capture peer audit: could not start pw-link; the stream's actual target is UNVERIFIED. "
          + "Expected {Expected}. See AUD-11.", expectedNodeName);
        return;
      }
      output = process.StandardOutput.ReadToEnd();
      process.WaitForExit(3000);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex,
        "BT capture peer audit: pw-link failed; the stream's actual target is UNVERIFIED. "
        + "Expected {Expected}. See AUD-11.", expectedNodeName);
      return;
    }

    var peers = ParsePwLinkOutputForStreamPeers(output, StreamNodeName);

    if (peers.Count == 0)
    {
      _logger.LogWarning(
        "BT capture peer audit: {Stream} has NO input links. Expected {Expected}. See AUD-11.",
        StreamNodeName, expectedNodeName);
      _metricsCollector?.Increment("bluetooth.capture_peer_audit_unverified_total");
      return;
    }

    var wrong = peers.Where(p => !string.Equals(p, expectedNodeName, StringComparison.Ordinal)).ToList();
    if (wrong.Count == 0)
    {
      // Debug, not Information: the healthy case must cost nothing on a box where log volume
      // correlates with audible distortion. Nothing below Warning reaches journald here anyway.
      _logger.LogDebug(
        "BT capture peer audit OK: {Stream} <- {Expected}", StreamNodeName, expectedNodeName);
      return;
    }

    _metricsCollector?.Increment("bluetooth.capture_wrong_peer_total");
    _logger.LogWarning(
      "🔴 BT capture is bound to the WRONG node: {Stream} <- [{Actual}], expected {Expected}. "
      + "This is AUD-11 — the graph shows an active capture and every downstream indicator reads "
      + "healthy while the audio comes from somewhere else. Tearing the stream down.",
      StreamNodeName, string.Join(", ", wrong), expectedNodeName);

    _captureTargetLost = true;
    StopCaptureSubprocess();
    CaptureTargetLost?.Invoke(this, new CaptureTargetLostEventArgs
    {
      DeviceAddress = deviceAddress,
      Reason = CaptureTargetLostReason.WrongPeerBound,
      BoundPeerNodeName = string.Join(", ", wrong),
    });
  }
```

Call it from the existing deferred task in `SearchForCaptureDeviceAsync`, immediately after
`DisconnectPipeWireBtAutoLinks(capturedNodeName);` at `:1362`:

```csharp
              DisconnectPipeWireBtAutoLinks(capturedNodeName);

              // AUD-11: verify what we ACTUALLY got, not what we asked for. Only meaningful for the
              // native stream — the pw-record fallback runs with node.autoconnect=false and does its
              // own explicit re-link below.
              if (_nativeStream != null)
              {
                AuditCaptureStreamPeer(capturedNodeName, connected.Address);
              }
```

⚠ **`connected` is in scope** — it is `SearchForCaptureDeviceAsync`'s parameter (`:1264`) and the lambda
already closes over `capturedNodeName` / `capturedNodeId`. Capture the address into a local beside those
two if a reviewer prefers the existing idiom.

---

### Task 6 — `PipelineStatus` stops reporting `Healthy` for a stream that lost its target

**File:** `src/Radio.Core/Interfaces/Audio/IBluetoothService.cs`, in `BluetoothPipelineStatus`
(`:201-214`) — append, do not reorder:

```csharp
  /// <summary>
  /// Device connected, but the capture target left PipeWire (or the stream was found bound to the
  /// wrong node) and the stream was torn down deliberately. Waiting for the node to reappear.
  /// </summary>
  /// <remarks>
  /// ⚠ AUD-11. This state exists because "Healthy" used to mean "a stream object exists"
  /// (LinuxBluetoothService.cs:184-185) — which was equally true of a stream recording the unplugged
  /// line-in. Parking is a real, expected state on this appliance: per AUD-10, pausing on the handset
  /// destroys the bluez_input node, so this is what a normal pause looks like from here. It is
  /// Degraded rather than Unhealthy for exactly that reason.
  /// </remarks>
  WaitingForCaptureNode
```

⚠ **Append it after `Broken`, and do not reorder the existing members.** `BluetoothPipelineStatus` is
serialized by name in the health endpoint today, but the enum's underlying integer values are the kind of
thing a future `[JsonConverter]` or a persisted preference could start depending on. Appending costs
nothing; reordering is a change nobody would think to look for.

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs`, `PipelineStatus`
(`:177-190`):

```csharp
  public BluetoothPipelineStatus PipelineStatus
  {
    get
    {
      if (!_started) return BluetoothPipelineStatus.Inactive;
      var connected = ConnectedDevice;
      if (connected == null) return BluetoothPipelineStatus.Degraded;
      if (_nativeStream != null || _captureProcess is { HasExited: false })
        return BluetoothPipelineStatus.Healthy;
      // AUD-11: the capture target left PipeWire (or the stream was audited onto the wrong node) and we
      // tore the stream down on purpose. Checked BEFORE _captureIntentionallyStopped because the two
      // are different facts with the same shape, and collapsing them into Degraded is what made this
      // failure indistinguishable from a user switching sources.
      if (_captureTargetLost) return BluetoothPipelineStatus.WaitingForCaptureNode;
      // Capture was intentionally stopped (user switched sources) — not broken
      if (_captureIntentionallyStopped) return BluetoothPipelineStatus.Degraded;
      return BluetoothPipelineStatus.Broken;
    }
  }
```

**File:** `src/Radio.API/Health/BluetoothHealthCheck.cs` — add the arm to the switch at `:35-41`:

```csharp
      BluetoothPipelineStatus.WaitingForCaptureNode => HealthCheckResult.Degraded(
        "Bluetooth device connected but its PipeWire capture node is gone; capture is parked and will "
        + "resume when the node reappears (AUD-11)"),
```

⚠ **Check whether that switch has a discard arm (`_ =>`).** If it does, the build stays green without
this edit and the new state silently reports as whatever the discard says — which would reproduce this
row's own defect in the health endpoint. If it does not, the build breaks until the arm is added, which
is the better outcome. **Say which it was in the PR body.**

---

### Task 7 — the two comments that describe a shortcut nobody should take

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/Native/PipeWireRegistryListener.cs:16`

```csharp
  /// <summary>
  /// PipeWire registry global id.
  /// </summary>
  /// <remarks>
  /// ⛔ This is NOT an object.serial and must NOT be used as target.object. An earlier version of this
  /// comment said "also used as object.serial for streams", which is false: pipewire-props(7) gives
  /// target.object as &lt;node.name|object.serial&gt;, and says the DEPRECATED node.target is the one
  /// that took an object.id. A global id is reused after its object is destroyed; a serial is not.
  /// Handing a global id to target.object resolves to nothing, and with node.autoconnect the session
  /// manager then binds the stream to the DEFAULT source — which is AUD-11, measured live on the box
  /// 2026-09-06. The capture path gets its serial from ParsePwCliOutputForBtNode, which reads real
  /// `object.serial =` lines; this Id feeds the autoswitch gate only. Keep it that way.
  /// </remarks>
```

**File:** `src/Radio.Infrastructure/Platform/Bluetooth/LinuxBluetoothService.cs:1552-1558` — the same
correction on `OnRegistryNodeAppeared`'s doc comment, whose sentence *"The registry's global id doubles
as the object.serial that PipeWireNativeStream.Connect needs as target.object"* is the one that must go.

**File:** `src/Radio.Infrastructure/Audio/Services/BluetoothCaptureWatchdog.cs`, class `<remarks>`:

```csharp
/// ⚠ THIS WATCHDOG MEASURES LIVENESS, NOT CORRECTNESS, and the distinction is load-bearing. It compares
/// elapsed time since the last OnProcess callback and nothing else. A capture stream bound to the WRONG
/// source — AUD-11, where PipeWire re-links radio-bt-stream to the built-in analog capture after the
/// bluez_input node vanishes — delivers callbacks perfectly on schedule, so this watchdog stays silent
/// at every possible threshold. ⛔ Do not try to make it catch that by lowering
/// OnProcessStallThresholdMs: it would fire on healthy BT and still never fire on this. Peer identity
/// is answered by LinuxBluetoothService.AuditCaptureStreamPeer and by OnRegistryNodeDisappeared.
```

---

### Task 8 — tests

Detailed in §4. Listed as a task so it is not treated as optional.

---

## 3. Ordering

**Task 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8.**

- **1 before 3.** Task 3's error handler exists mainly to surface the error Task 1's property is supposed
  to cause. Landing 3 first gives a handler with nothing to report.
- **4 before 6.** Task 6 reads `_captureTargetLost`, which Task 4 introduces.
- **5 after 4.** Both raise `CaptureTargetLost`; the event and its args are defined in Task 4.
- **7 any time.**

**One PR.** The deliverable is a property — *this stream captures the node it asked for, or it captures
nothing and says so* — and it is not true until both the targeting change and both guards are in. A
split where Task 1 lands alone would ship a change whose only observable effect on the box is that BT
capture might stop working, with nothing in the log to say why. `PHN-5` §3 made the same argument for the
same reason.

---

## 4. Test plan

> ⚠ **This repository has repeatedly found tests that passed against a deliberately broken
> implementation.** Every pin below names the mutation that must make it fail, and **Builder runs each
> mutation and records the result in the PR body**. Where a test cannot falsify something, that is
> stated rather than implied.

> ⚠ **`CLAUDE.md` § *Test Timing* applies to Task 4 and nothing else.** Task 4's teardown is
> `Task.Run(...)` fired from an event handler, so a test that asserts on its effect after a
> `Task.Delay` is racing two clocks with no rendezvous — the exact shape `BluetoothCaptureWatchdogTests`
> was rewritten to remove (`TEST-4`). **Synchronize on the observation:** assert on the
> `CaptureTargetLost` event via a `TaskCompletionSource` (or a `SemaphoreSlim` released in the handler)
> with a generous timeout, and assert `PipelineStatus` only after that completes. ⛔ **No
> `await Task.Delay(200)` followed by an assertion.**

**All of §4.1–§4.3 runs on Windows.** `Radio.Infrastructure.Tests` targets **`net10.0`**
(`Radio.Infrastructure.Tests.csproj`, `<TargetFramework>net10.0</TargetFramework>`), so `WINDOWS_TARGET`
is undefined in the test build and the `#if !WINDOWS_TARGET` PipeWire types compile into it. Only a
native *call* would fail, and none of these make one — which is exactly why Tasks 1 and 5 extract pure
statics. `PipeWireRegistryListenerTests` is the shipped precedent.

### 4.1 `T1` — the props string

**File:** `tests/Radio.Infrastructure.Tests/Platform/Bluetooth/Native/PipeWireNativeStreamPropertiesTests.cs` (new)

| Test | Pins | Falsifying mutation |
|---|---|---|
| `BuildStreamProperties_SetsDontReconnect` | ⭐ the fix. `Contains("node.dont-reconnect = true")` | delete the token → fails |
| `BuildStreamProperties_KeepsAutoconnect` | `Contains("node.autoconnect = true")` — the thing §6.2 holds in reserve, pinned so removing it is a deliberate act | delete it → fails |
| `BuildStreamProperties_PutsTheSerialInTargetObject` | `Contains("target.object = 1234")` for input `1234u` | swap in a different field → fails |
| `BuildStreamProperties_IsBalancedAndSingleLine` | exactly one `{` and one `}`, no `\n` — `pw_properties_new_string` takes one flat dict | drop a brace → fails |
| `BuildStreamProperties_ExactShape` | the whole string against a literal, so any silent reordering or lost space is visible | any character change → fails |

⚠ **`BuildStreamProperties_ExactShape` needs the literal, and this plan does not invent one.** Builder
runs the method once and pastes the output, exactly as `LogSafeTextTests` hard-codes `"txt:3c48591d/5"`.

⛔ **Do not write `Assert.Contains("dont-reconnect", …)` without the `= true`.** `node.dont-reconnect = false`
contains it too, and that is the default — a test that passes against the broken default is worse than
no test.

### 4.2 `T2` — the zero-serial refusal

**File:** `tests/Radio.Infrastructure.Tests/Platform/Bluetooth/Native/PipeWireNativeStreamPropertiesTests.cs`

| Test | Pins | Falsifying mutation |
|---|---|---|
| `Constructor_ZeroTargetSerial_Throws` | `Assert.Throws<ArgumentOutOfRangeException>` on `new PipeWireNativeStream(0, 48000, 2, …)` | remove `ThrowIfZero` → fails |
| `Constructor_NonZeroTargetSerial_DoesNotThrow` | that the guard is a guard and not a wall | invert the condition → fails |

⚠ **The constructor calls `EnsurePwInit()` → `pw_init`, which is a native call.** The zero guard must be
the **first statement in the constructor body**, before `EnsurePwInit()` at `:157`, or
`Constructor_ZeroTargetSerial_Throws` will `DllNotFoundException` on Windows instead of throwing what it
asserts. **Builder must confirm ordering before writing the test**, and
`Constructor_NonZeroTargetSerial_DoesNotThrow` **cannot run on Windows at all** for the same reason —
mark it `[SkippableFact]` on `!OperatingSystem.IsLinux()` (`Xunit.SkippableFact` is already referenced by
this project) and say so in the file.

> **What `T2` cannot falsify:** the `LinuxBluetoothService.cs:1814` caller-side guard, which needs a
> `LinuxBluetoothService` instance and therefore a D-Bus connection. It is pinned by the §4.5 box check
> alone. **Write that sentence in the test file** rather than letting a green `T2` imply the whole of
> Task 2 is covered.

### 4.3 `T3` — the peer audit parser, against the row's own measured output

**File:** `tests/Radio.Infrastructure.Tests/Platform/Bluetooth/PwLinkPeerParsingTests.cs` (new)

⭐ **The fixtures are real.** `docs/queue/AUD-11.md:18` and `:21` are `pw-link -l` output measured on the
box during the incident. Use them verbatim as the healthy and defective cases; do not paraphrase them.

| Test | Fixture | Expected |
|---|---|---|
| `Parses_TheHealthyBinding` | `radio-bt-stream:input_FL  <- bluez_input.B0_D5_FB_D2_0D_68.2:output_FL   [active]` | one peer, `bluez_input.B0_D5_FB_D2_0D_68.2` |
| `Parses_TheDefectiveBinding` | `radio-bt-stream:input_FL  <- alsa_input.pci-0000_00_1f.3.analog-stereo:capture_FL   [active]` | one peer, `alsa_input.pci-0000_00_1f.3.analog-stereo` |
| `Parses_TheIndentedForm` | the two-line `port` / `  \|<- peer` shape the existing parsers read | same peer set |
| `StripsThePortSuffixAndTheActiveMarker` | both forms | node name only; no `:capture_FL`, no `[active]` |
| `IgnoresOtherStreamsPorts` | a fixture with `pw-record:input_FL` and `chromium:input_FL` blocks alongside ours | only our peers |
| `BothChannelsFromOnePeer_CollapseToOne` | `input_FL` and `input_FR` from the same node | one entry — it is a set |
| `EmptyOrGarbageInput_ReturnsEmpty` | `""`, `"\n\n"`, unrelated text | empty set (⛔ which the caller treats as a **failed** audit) |
| `NodeNamesContainingDots_SurviveIntact` | `bluez_input.B0_D5_FB_D2_0D_68.2` | the trailing `.2` is kept — it is part of the name, not a suffix |

> **Falsifying mutations, all four to be run:** (1) make `AddPeerNode` split on the **first** colon
> instead of the last → `NodeNamesContainingDots_SurviveIntact` and both `Parses_*` fail; (2) drop the
> `[active]` strip → the marker leaks into the name and every comparison mismatches; (3) drop the
> single-line arm → `Parses_TheDefectiveBinding` fails, which is the arm the incident evidence needs;
> (4) drop the `startsWith(inputPrefix)` filter → `IgnoresOtherStreamsPorts` fails.

⚠ **The most important mutation is (3), and it is the one a reviewer would skip.** The single-line shape
is the one measured on the box; the indented shape is the one the existing in-tree parsers assume. If
only the indented arm works, the audit silently finds zero peers on the real appliance — and a
fail-closed audit that always fails is noise that gets muted. **Run it.**

### 4.4 `T4` — the target-lost teardown and the parked state

**File:** `tests/Radio.Infrastructure.Tests/Platform/Bluetooth/CaptureTargetLostTests.cs` (new)

⚠ **Reachability is unresolved and Builder decides.** `OnRegistryNodeDisappeared` is `private` on
`LinuxBluetoothService`, whose construction needs a D-Bus connection. `InternalsVisibleTo` is already
granted for `Radio.Infrastructure.Tests` (`Radio.Infrastructure.csproj:15`) but does not reach `private`.
Three options, in order of preference:

1. **Extract the decision** as a pure static —
   `ShouldTearDownForRemovedNode(string? connectedAddress, string removedAddress, bool streamRunning)`
   — and test that exhaustively. The handler becomes a two-line caller. **Preferred**: it is the
   `ParsePwCliOutputForBtNode` idiom this file already uses, and the branch logic is where the risk is
   (a too-broad predicate kills healthy playback — §0.8 reason 2).
2. Widen the handler to `internal`.
3. Leave it uncovered and **say so in the test file**.

Against option 1:

| Case | Expected |
|---|---|
| removed address == connected address, stream running | **tear down** |
| removed address == connected address, no stream | no action |
| removed address ≠ connected address, stream running | **no action** — the regression that would kill healthy playback |
| no connected device | no action |
| case-differing addresses (`b0:d5:…` vs `B0:D5:…`) | **tear down** — PipeWire node names are upper-case, BlueZ is mixed |

Plus one integration-shaped test on `PipelineStatus`, if a `LinuxBluetoothService` can be constructed at
all in this suite (**Builder establishes this first — if it cannot, say so and drop the test rather than
faking a construction**): with a connected device, no stream, and `_captureTargetLost` set, the property
returns `WaitingForCaptureNode` and **not** `Broken` or `Degraded`.

> **Falsifying mutations:** drop the address comparison → row 3 fails; drop the `_nativeStream != null`
> check → row 2 fails; use `StringComparison.Ordinal` → row 5 fails.

### 4.5 `T5` — the box check, which is the ONLY place the fix itself is observable

⚠ **Everything above pins that we *asked*. Not one line of it pins that PipeWire *obeyed*.** This section
is the gate, it needs the owner and his handset, and it interrupts music.

**Run in this order. Read `--since`-bounded logs only** — `CLAUDE.md` records that heavy log reads on
this box correlate with audible distortion.

```bash
# 0. Before deploying: confirm the state_changed signature this PR was built against (C-168).
ssh mmack@radio 'grep -A6 "state_changed" /usr/include/pipewire-0.3/pipewire/stream.h'
```

```bash
# 1. Deploy, then confirm both services carry this build.
curl -s http://radio:5000/api/health/version
```

```bash
# 2. Play over BT. Confirm the HEALTHY binding first — a fix that breaks normal capture is not a fix.
ssh mmack@radio "pw-link -l | grep -A2 radio-bt-stream"
#    Expect: radio-bt-stream inputs fed by bluez_input.<MAC>.<N>, and the audio audible in the room.
```

```bash
# 3. THE REPRO. Pause on the handset (per AUD-10 this destroys the node), then immediately:
ssh mmack@radio "pw-link -l | grep -A2 radio-bt-stream"
#    PASS: radio-bt-stream is GONE from the graph (the stream was torn down).
#    FAIL: it is present and fed by alsa_input.pci-0000_00_1f.3.analog-stereo — the bug is not fixed.
#    ⚠ ALSO FAIL, and less obvious: it is present and fed by NOTHING. That means node.dont-reconnect
#      refused the move but our teardown did not run — i.e. Task 1 worked and Task 4 did not.
```

```bash
# 4. The state must be readable by a person, which is half the row.
curl -s http://radio:5000/health
#    Expect the BT check Degraded, naming WaitingForCaptureNode.
ssh mmack@radio "journalctl -u radio-api --since '-5min' --no-pager | grep -E 'AUD-11|target lost|WRONG node|stream error'"
#    Expect at least the "BT capture target lost" Warning. ⚠ Warning is the bar: LOG-11 restricted
#    Radio.API's console sink, so an Information line would NOT be here even if it were emitted.
```

```bash
# 5. Recovery. Resume on the handset. Per AUD-10 the node may not come back at all — that is AUD-10's
#    defect, not this row's, and it must not be recorded as a failure here. Reconnect from the handset
#    if needed, then re-run step 2: capture must re-arm and audio must return with no restart.
```

```bash
# 6. The Information-level detail lives in the file sink, not the journal.
ssh mmack@radio 'F=$(ls -t /opt/radio-console/logs/radio-*.txt | head -1); grep -E "PipeWire stream state|peer audit" $F | tail -20'
```

⚠ **Step 3's second failure mode is the one to watch for.** "Torn down" and "still connected to nothing"
look similar in a hurry and mean different things about which half of this PR works.

⚠ **Step 5 is entangled with `AUD-10` and cannot be cleanly separated.** `AUD-10` is unfixed and may be
upstream. **Record what happened; do not tune this row until step 5 passes.**

### 4.6 Gates

- `dotnet build --configuration Release` — 0 warnings (warnings are errors in Release).
- `dotnet test --configuration Release` — full suite green.
  ⛔ **Never pipe it to `tail`** (`CLAUDE.md`): redirect, echo `$?`, then grep the file. Read the
  **per-project** summary lines.
  Known-failing on Windows and not regressions: four `SrcVariableResamplerTests` (`libsamplerate.so.0`,
  `TEST-5`) and `NwsObservationIntegrationTests.RealNwsCall_*` (live network, CI-excluded).
- ⚠ **If run from a git worktree, `LogSafetyLintTests` is red for an unrelated reason** (`PHN-5` `C-100`).
  Not this row.
- ⚠ **Four files gain a `CaptureTargetLost` member they never raise** (`WindowsBluetoothService`,
  `MockBluetoothService`, `BluetoothServiceFactory`, and the `Radio.Infrastructure.Tests` fake at
  `BluetoothAutoSwitchServiceTests.cs:34`). Expect compile errors there first; they are the change
  landing, not a problem.
- Every mutation in §4.1–§4.4 run, with its result in the PR body. **A mutation that does not make its
  test fail is a finding, not a formality.**

---

## 5. Docs and queue

| # | Task |
|---|---|
| 1 | `design/FUTURE-WORK.md` — add §6.1's two filed items (`USBAudioSourceBase` first-device fallback; `capture-{i}` index ids). |
| 2 | `design/INTEGRATIONS.md` — no change. This row touches no integration service. Stated so its absence is not read as an oversight. |
| 3 | `docs/BUILDER_QUEUE.md` — Builder marks `AUD-11` ✅ at merge, adds a cycle banner entry, and **corrects the `MEMORY.md` citation at `:47`** (§0.3): it is Claude project auto-memory at `~/.claude/projects/D--prj-rtest-rtest/memory/MEMORY.md`, not a repo file. The primary source is commit `5353f020a`. |
| 4 | `docs/queue/AUD-11.md` — same citation correction at `:30`, and mark the row's three scope questions answered with pointers to §1.1, §1.2 and §0.6. |
| 5 | ⛔ **`CLAUDE.md` — nothing.** §6.4. |

---

## 6. Deliberately not done

### 6.1 The USB capture family, and the index-shaped device ids

`C-167`. Both real, both verified first-hand, both out of scope:

1. **`USBAudioSourceBase.cs:207-214` falls back to `captureDevices[0]`**, and the shipped
   `"USBPort": ""` (`src/Radio.API/appsettings.json:51`, `:54`) makes the match loop at `:199-200`
   succeed on the first non-`"Monitor of"` device **before the warning is reached**. Affects Vinyl,
   Radio (Raddy) and GenericUSB. ⭐ **The honest assessment is that this is worse than `AUD-11`** — it
   fires on a stock configuration with no log line at all, where `AUD-11` needs a device to disappear
   first. It wants its own row, at P1.
2. **`SoundFlowDeviceManager.cs:555` / `:747` mint `$"capture-{i}"` ids from enumeration index**, and
   `:367` persists one as `AudioPreferences:CurrentInput`, restored by exact match at `:135`. **Latent
   today** — the only consumer is `DevicesController.cs:102` and no capture-open path reads it. A note,
   not a defect, and it should be filed as one so it is not "discovered" again.

⛔ **Neither is fixed here.** Both are MiniAudio device selection, not PipeWire targeting; both touch
three user-facing sources; and item 1 requires deciding what an empty `USBPort` should mean, which is a
config-surface decision belonging to the owner. Folding either in would put a UAT-bearing change to
Vinyl and Radio inside a diff whose entire value is that it is confined to the BT capture path.

### 6.2 Dropping `node.autoconnect` — held in reserve, not rejected

If §4.5 step 3 shows `node.dont-reconnect` is **not** honoured on this box's WirePlumber, the next option
is `node.autoconnect = false` plus explicit `pw-link` establishment — which is what the `pw-record`
fallback already does (`:1856`, `:1989-2012`) and is known to work there.

⛔ **Not taken pre-emptively, and the reason is measurement rather than preference.** It reintroduces the
subprocess link management PR #262 existed to delete; it opens a window between connect and re-link in
which the wrong link is live and audible; and it is genuinely unknown whether the stream connects at all
without autoconnect on a graph this hand-tuned (`deploy/common/41-disable-bt-input-restore-target.lua`
and its companion `90-disable-bt-input-autolink.lua` both exist because this graph needed correcting by
hand). **Try the documented property first; fall back only on evidence.**

### 6.3 A periodic peer audit

Task 5 runs the audit **once** per acquisition. A periodic version would catch a re-link that happens
without a registry removal event.

⛔ **Not done**, for a cost reason rather than scope discipline: it means a `pw-link` subprocess on a
timer on a box where subprocess churn and log volume both correlate with audible audio distortion, and
where PR #262's entire purpose was removing exactly that churn. The ongoing case is covered event-free by
`OnRegistryNodeDisappeared`, which is the event that precedes every instance of this failure we have
actually seen. **If a wrong binding is ever observed without a preceding node removal, that is the
evidence that justifies the timer** — and it should be a new row, not a quiet addition here.

### 6.4 The `CLAUDE.md` note

`CLAUDE.md` § *Deployment* is the file where box facts live so they stop being rediscovered, and *"the BT
capture stream can be silently re-bound to the line-in, and the graph will not tell you"* is exactly such
a fact. ⛔ **Not added here**: `CLAUDE.md` is the repository's shared context file and editing it inside a
feature PR is how two sessions end up disagreeing about it. The fact is written where the code is
instead — `OnRegistryNodeDisappeared`'s remarks and `BuildStreamProperties`'s — and **recommended to the
owner for `CLAUDE.md` separately.**

### 6.5 `AUD-10` and `AUD-12`

Untouched, deliberately. `AUD-10` (the node vanishing on pause) is this row's **trigger**, not its cause;
this row makes that trigger loud instead of silent, which is what its own queue entry says it is for.
`AUD-12` (the source stalling at `Ready`) is independent. ⚠ **Expect to hit all three while testing any
one of them** — `AUD-10`'s dossier says so, and §4.5 step 5 is where it will happen.

---

## 7. Self-review

### 7.1 What was verified first-hand at `066a0d5c`

- `PipeWireNativeStream.cs` in full — the props string at `:204`, the connect call at `:236-239`, the
  events struct assignment at `:196-200` (`Process` only), and the ctor's own XML doc at `:101` naming
  `targetNodeId` an object **serial**.
- `PipeWireNative.cs` in full — `PwStreamEvents` including the **unassigned** `StateChanged` at `:208`,
  the `PwStreamFlags` enum at `:187-194`, and the `pw_core_get_registry` helper-binding comment.
- `PipeWireRegistryListener.cs` in full — `NodeAppeared`/`NodeDisappeared`, the thread-loop warning at
  `:81-83`, and the global-id comment at `:16` that `C-164` corrects.
- `LinuxBluetoothService.cs` — the acquisition chain `:1208-1406`, `StartCaptureSubprocess` `:1800-1842`
  including the zero-serial ternary at `:1814`, the `pw-record` fallback `:1847-1871`,
  `ParsePwCliOutputForBtNode` `:1704-1793`, the registry handlers `:1521-1593`, `PipelineStatus`
  `:177-190`, `GetCaptureStreamSnapshot` `:215-224`, `MonitorBtPipelineAsync` `:245-312`, the pw-link
  helpers `:2017-2150`, and the `:1985-1987` doc comment that describes this exact defect for
  `pw-record`.
- `BluetoothCaptureWatchdog.cs` in full — `:88` is the liveness-only comparison behind `C-165`.
- `BluetoothAudioSource.cs:373-427` — the recovery interlock a naive fix would have reused.
- `USBAudioSourceBase.cs:187-232`, and `src/Radio.API/appsettings.json:48-55` — the `""` `USBPort`.
- `IBluetoothService.cs:177-214` — `BluetoothPipelineStatus`'s four members; `BluetoothHealthCheck.cs:22-41`;
  every implementation of `PipelineStatus` (four).
- `Radio.Infrastructure.Tests.csproj` — `net10.0`, which is what makes §4.1–§4.4 runnable on Windows.
- `IsPwRecordLinkedToBtNode` has **zero callers** — grep over every `.cs` in the repo returns only the
  definition.
- **`git` archaeology**: the `5353f020a` diff, `git blame` confirming both lines untouched since
  `ae8b8721`, and that PR #262's body contains no rationale.
- **PipeWire's published documentation** for `node.dont-reconnect`, `target.object`, `node.autoconnect`,
  `node.target`, the `pw_stream_state` enumerator values, and `pw_stream_get_node_id`'s signature.

### 7.2 What could not be verified, and what it costs

1. **No box was touched.** Nothing here was built, run, or deployed. Every code block is written against
   read source and is unexecuted.
2. **`state_changed`'s C signature** (`C-168`). The largest single risk in the plan, because a wrong
   arity fails quietly. Task 3 gives the one-line header check; run it first.
3. **Whether WirePlumber on this box honours `node.dont-reconnect`.** The property is documented; the
   session manager's compliance is not something this repository controls, and it is precisely why the
   plan ships two independent guards instead of one.
4. **Which `pw-link -l` output shape the box actually produces.** The row's evidence shows the
   single-line form; the in-tree parsers assume the indented form. §4.3 handles both and mutation (3)
   is the one that proves it.
5. **Whether `LinuxBluetoothService` can be constructed in the test suite at all.** §4.4's integration
   test depends on it and the plan did not establish it. **Builder checks first and drops the test with
   a written reason if not** — do not fake a construction to get a green.
6. **Whether `BluetoothHealthCheck`'s switch has a discard arm.** Task 6 says to check and to report
   which; if it does, the new state would report as the discard's value and this row would have
   reproduced its own defect in the health endpoint.
7. **The `target.object = 0` behaviour was reasoned, not measured.** That an unresolvable target plus
   autoconnect yields the default source is documented behaviour and matches the observed incident, but
   the specific value `0` was never fed to PipeWire to watch what it does. Task 2 refuses it either way,
   so the cost of being wrong is a slightly over-cautious guard.

### 7.3 What would falsify this plan's central decision

§1.1 rests on `node.dont-reconnect`'s second documented sentence — *"also inhibits that the node is moved
to another sink/source"* — being honoured by WirePlumber 0.4.x as configured on this box. **If §4.5 step
3 shows the stream still lands on `alsa_input…analog-stereo`, §1.1 is wrong and §6.2 is the answer**: drop
`node.autoconnect`, link explicitly, and accept the connect-to-relink window. Everything else in this
plan — the zero-serial guard, `state_changed`, the target-lost teardown, the peer audit, the parked
status, the comment corrections — is unaffected by that outcome and still worth shipping, because **the
half of this row that makes a wrong binding visible does not depend on which half stops it.**

---

## 8. Queue row wording

⛔ **This plan does not edit `docs/BUILDER_QUEUE.md` or `docs/queue/AUD-11.md`.** The wording below is for
whoever does.

### 8.1 Replacement for the `AUD-11` line in `docs/BUILDER_QUEUE.md` § Queue

Same seven-column shape as the rows around it; only the **Plan** and **Depends on** cells change.

```
| AUD-11 | ⭐ **NEW 2026-09-06, observed live — the BT capture stream silently re-links to the built-in line-in when its target disappears, and every indicator stays green.** — [detail](queue/AUD-11.md) | 📋 | [`AUD-11-the-capture-that-recorded-the-wrong-jack.md`](../design/plans/AUD-11-the-capture-that-recorded-the-wrong-jack.md) · **1.5 d + a box session** · ⛔ **NOT auto-mergeable — the central fix is a request to WirePlumber that no gate in this repo can observe, and the repro means interrupting the owner's music.** ✅ The `PW_ID_ANY` question is **SETTLED — do not re-investigate it**: it stays exactly as it is, the fix is additive (`node.dont-reconnect`), and there is no trade-off (plan §0.3, `C-161`). | _no spec doc — measured on `radio` 2026-09-06_ · rationale traced to commit `5353f020a` (PR #262) · ⚠ **`MEMORY.md` is Claude project auto-memory (`~/.claude/projects/D--prj-rtest-rtest/memory/MEMORY.md`), NOT a repo file** — this cell used to imply otherwise | — _(no row dependency. Makes `AUD-10` silent rather than loud; neither blocks the other. ⚠ **Expect to meet `AUD-10` during this row's UAT** — the repro is `AUD-10`'s own trigger, so a failure to resume at §4.5 step 5 is `AUD-10`, not a regression here. **No file overlap with `AUD-12`**, which touches `BluetoothAudioSource` state; this row touches the PipeWire interop, `LinuxBluetoothService`'s capture path, and one Core enum.)_ | `fix/aud-11-bt-capture-refuses-the-wrong-node` |
```

### 8.2 Addition to `docs/queue/AUD-11.md`

Append a section; **change nothing above it** — the measured evidence is the row's value.

```markdown
## Planned — 2026-09-06

**Plan:** [`design/plans/AUD-11-the-capture-that-recorded-the-wrong-jack.md`](../../design/plans/AUD-11-the-capture-that-recorded-the-wrong-jack.md).
**1.5 d + a box session.** ⛔ **Not auto-mergeable** — plan §0.8.

The three scope questions above are answered:

1. **Can the stream bind to the BT node specifically and refuse anything else, without reintroducing
   what `PW_ID_ANY` solved?** **Yes, and there is no conflict to trade off.** `PW_ID_ANY` bought exactly
   one thing — it stopped an `object.serial` being passed in `pw_stream_connect`'s `targetId` argument,
   which means node **id**. It did *not* enable `target.object` resolution; that was in the file's first
   version. So `PW_ID_ANY` stays untouched and the refusal is additive: `node.dont-reconnect = true`,
   documented by `pipewire-props(7)` as inhibiting exactly the substitution this row reports. Plan §0.3,
   §1.1, `C-161`.
   ⚠ **A second route into the same wrong jack was found and is not in this row's symptom section:**
   `LinuxBluetoothService.cs:1814` converts a missing `object.serial` into the literal target
   `target.object = 0`, which matches nothing — reachable on a cold connect, with no device having to
   disappear. Plan §0.4, `C-163`.
2. **Stop, retry, or park?** **All three, in that order: tear the stream down, park in an explicit
   `WaitingForCaptureNode`, and let the existing `NodeAppeared` → `CaptureNodeAvailable` path re-arm it.**
   A terminal stop is wrong because `AUD-10` means the node vanishes on every pause. A blind retry is
   wrong because `project_autoswitch_bt_bug.md` already records what that costs. The teardown is a
   **safety** property, not tidiness: while a `pw_stream` with `node.autoconnect` exists, PipeWire may
   bind it to something. Plan §1.2.
3. **Does any other capture source share the pattern?** **No other source uses `target.object`/`PW_ID_ANY`** —
   `PipeWireNativeStream` is constructed in exactly one place. **But the USB family has the same defect
   class through a different mechanism, and it is worse.** `USBAudioSourceBase.cs:213` falls back to
   `captureDevices[0]` for Vinyl, Radio (Raddy) and GenericUSB — and because the shipped config is
   `"USBPort": ""` (`src/Radio.API/appsettings.json:51`, `:54`), `Contains("")` matches the first
   enumerated device *before* the warning is reached. **Not fixed in this row; it wants its own P1.**
   Plan §0.6, `C-167`, §6.1.

**Two things the plan makes co-equal with the fix**, because the row asked for it:

- ⭐ **Four indicators say green for four independent reasons**, and only one of them is the graph.
  `BluetoothCaptureWatchdog.cs:88` measures elapsed time since the last callback — and the line-in
  delivers callbacks perfectly — so a wrong-but-live source is invisible to it **at every threshold**.
  ⛔ Do not try to fix this row by lowering `OnProcessStallThresholdMs`. Plan §0.5, `C-165`.
- ⭐ **The repository already wrote this check and left it dead.**
  `LinuxBluetoothService.IsPwRecordLinkedToBtNode` (`:2097-2150`) asks exactly the right question, has
  **zero callers**, and returns `true` on every failure path (*"assume ok if we can't check"*). The plan
  deletes it and replaces it with a testable static that fails **closed**.

⚠ **Citation correction:** `MEMORY.md` (cited at `:30` above and at `BUILDER_QUEUE.md:47`) is **not a
file in this repo** — `git log --all -- "*MEMORY.md"` is empty. It is Claude project auto-memory at
`~/.claude/projects/D--prj-rtest-rtest/memory/MEMORY.md`. The primary, in-repo source for the `PW_ID_ANY`
decision is commit **`5353f020a`** (PR #262), whose message is the only written rationale that exists.
```
