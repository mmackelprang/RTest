# PLAN — `AUD-13` · The empty port is already refused three times over. The fallback that picks the first jack is not refused at all.

> **Row:** `AUD-13`, [`docs/queue/AUD-13.md`](../../docs/queue/AUD-13.md). 🟠 **P1** as filed.
> **Branch:** `fix/aud-13-usb-capture-refuses-instead-of-substituting`
> **Estimate:** **0.5 d** of build, **plus one box session** to confirm the Vinyl port on the appliance. §0.9 derives both.
> **⛔ NOT auto-mergeable on green gates.** §0.10 gives two reasons; neither is "the tests might be flaky".
> **Planned against** `main` at **`c01ab5a1`**. Every line number below was re-read out of the tree at that
> commit rather than copied from the dossier. Where a line is likely to move it is quoted as well as numbered.
> **Nothing on the box was touched while planning this**, and **no build or test run was performed** — a
> Builder is mid-cycle in this working tree. §7 lists every claim that is therefore inference rather than
> measurement, and Task 6 is written so Builder measures them first.
>
> ⚠⚠ **READ §0.2 BEFORE ANYTHING ELSE. The row's headline defect is not reachable in production, and the
> owner's decision was answered against a premise that does not hold.** The row is still worth shipping, for
> a defect one branch away from the one it names.

---

## 0. Read this before Task 1

### 0.1 What this row is, in one paragraph

`USBAudioSourceBase.InitializeUSBCaptureAsync` picks a capture device by testing every enumerated device
name for a configured substring, and when that search comes up empty it does not fail — it takes
`captureDevices[0]`, or, if the machine enumerated no capture devices at all, it hands MiniAudio `null` and
gets the system default. The row filed this as an empty-string problem: `"USBPort": ""` ships in the
configuration, `string.Contains("")` is true of every string, so — the row reasoned — the first non-`"Monitor of"`
device wins and the warning written to catch that is unreachable. **The first half of that is a true fact
about line 199 and the second half does not happen**, because three independent layers refuse an empty port
before control ever reaches line 199, and the innermost of them throws. What *does* happen, on a configured
appliance, is the case the row did not look at: a port string that matches **nothing** (a renamed or
unplugged device) silently captures the first jack in enumeration order with a Warning, and a port string
that matches **more than one** device silently captures the first of them with no warning at all. The fix is
to make the selection say what it found and refuse to substitute — which is the owner's decision, correctly
applied to the branch it actually governs.

### 0.2 ⚠⚠ The row's headline is not reachable. Three guards, traced

`InitializeUSBCaptureAsync` (`USBAudioSourceBase.cs:173`) reaches the match loop at `:199` only after
`ReserveUSBPort(usbPort)` at `:177`. That ordering is load-bearing and the row does not mention it.

| # | Where | What happens to an empty port |
|---|---|---|
| 1 | `AudioSourceFactory.cs:154-162` | **Vinyl never gets built.** `CreateVinylSource` resolves the port and throws before construction: `throw new InvalidOperationException("Vinyl source is not configured. Please configure the USB port in System > Configuration > Devices.");` |
| 1′ | `AudioSourceFactory.cs:199-206` | **GenericUSB never gets built**, same shape, same message pointed at *System > Configuration > Generic Source*. |
| 1″ | `RadioFactory.cs:331-347` | **RF320 is not "available".** `IsRF320Available()` opens with `if (string.IsNullOrWhiteSpace(usbPort)) { return false; }`, so `GetAvailableDeviceTypes` (`:125-141`) omits it and `GetDefaultDeviceType` (`:144-167`) cannot return it. |
| 2 | `GenericUSBAudioSource.cs:120-137` | Second guard on the same source: a blank saved port logs a Warning and parks at `State = Ready` without initializing capture. |
| 3 | `USBAudioSourceBase.cs:122` → `SoundFlowDeviceManager.cs:248-250` | **The backstop, and it throws.** `ReserveUSBPort` calls `_deviceManager.IsUSBPortInUse(usbPort)`, whose production implementation opens with `ArgumentException.ThrowIfNullOrEmpty(usbPort);`. `ReserveUSBPort` itself repeats it at `:261`. |

Layer 3 is the one that settles it. `AudioServiceExtensions.cs:79` and `:285` register
`IAudioDeviceManager` as `SoundFlowDeviceManager` — there is no other production implementation — so even if
every factory guard were removed, an empty port raises `ArgumentException` at `USBAudioSourceBase.cs:122`,
**22 lines before** the `Contains` at `:199`. ⭐ **`Contains("")` at `:199` is unreachable with an empty
`usbPort`, in every path that exists today** (`C-230`).

📌 **Why the row believed otherwise, and it is worth knowing.** `USBAudioSourceTests.cs:58-59` mocks the
device manager —

```csharp
    _deviceManagerMock = new Mock<IAudioDeviceManager>();
    _deviceManagerMock.Setup(d => d.IsUSBPortInUse(It.IsAny<string>())).Returns(false);
```

— so in the one place a reader can watch this code run, layer 3 is absent by construction. The row was
"confirmed by reading the code" (`docs/queue/AUD-13.md:5`), and read on its own the loop at `:199` is
exactly as damning as the row says. Reading the *caller* is what changes the answer.

### 0.3 ⭐ What IS reachable — five cases, three of them defects

Re-derived from `USBAudioSourceBase.cs:173-242` at `c01ab5a1`. `captureDevices` is
`_audioEngine.CaptureDevices` (`:190`); the loop is `:193-205`; the fallback is `:207-214`; the device is
opened at `:221` and the outcome logged at `:229-231`.

| # | Configured port | Today's behaviour | What a person sees |
|---|---|---|---|
| 1 | empty / whitespace | `ArgumentException` at `:122`, or the source is never constructed at all (§0.2) | an exception whose text is about a *port reservation*, not about configuration — but it is loud |
| 2 | matches exactly one non-`Monitor of` device | correct bind | `:229` Information, file sink only |
| 3 | **matches several** | ⚠ **first in enumeration order, silently** | `:229` names the chosen device and says nothing about the other candidates. **No warning.** |
| 4 | **matches nothing, devices exist** | ⚠ **`captureDevices[0]`** — `:213` | `:210` Warning. Reaches journald. **And it binds anyway.** |
| 5 | **matches nothing, no devices enumerated** | ⚠⚠ **`targetDevice` stays `null`**; `:221` calls `InitializeCaptureDevice(null, …)`, which opens the **system default** | `:231` logs `targetDevice?.Name ?? "default"` → the word `"default"`. **No warning at all.** |

Rows 3, 4 and 5 are this row's real content. Row 4 is the one the row's title describes — *"bind to whatever
enumerates first"* — and its trigger is a **stale or misspelt port**, not an empty one. Row 3 is the trigger
the row's title describes *exactly* and nobody has written down: a short pattern (`"USB"`, `"Audio"`,
`"Analog"`) matches two or three devices on this appliance and takes the first, with no warning and no record
that there was a choice. Row 5 is worse than either and is unmentioned anywhere: **the only case that binds
to a device nobody named and logs nothing about it** (`C-231`).

⚠ **Row 4 is the one that makes this the `CLAUDE.md` § *Pre-Merge Review* class.** `:210`'s message —

```csharp
        Logger.LogWarning(
          "Could not find USB capture device for port {USBPort}, using first available capture device",
          usbPort);
```

— is *true*. It is the rare case in this repository of an accurate log line attached to a wrong action. It
warns, and then does the thing anyway, and the source afterwards reports `Ready` and then `Playing` like any
other. The defect is not that the message lies; it is that a Warning is the entire consequence of picking the
wrong physical input.

📌 **The comment at `:195-198` is accurate and the code is still wrong**, exactly as the row states at
`docs/queue/AUD-13.md:49-51`. That observation survives — it is just about a different branch than the row
thought.

### 0.4 ⭐ The owner's decision, applied to the branch it actually governs

The owner's words, from `docs/queue/AUD-13.md:92-94`:

> 1. **The Vinyl source will ALWAYS have a USB port connector. All of its audio arrives over USB.**
> 2. **The Radio USB connection is DEPRECATED.** `SDRRadioAudioSource` (RTL-SDR) is the only supported radio path.

Fact 1 is what makes refusal safe, and its force is larger than the dossier claims. The dossier applies it
to "empty means fault". But if Vinyl **always** has a USB connector, then *"the configured device is not
present"* is equally never a legitimate state — and that is case 4, which is live. The same sentence that
licenses refusing an empty port licenses refusing a port that matches nothing, and refusing a port that
matches several. **The decision the owner gave is sufficient for the whole row; it was simply pointed at
the one branch that was already closed** (`C-232`).

So the semantic this plan ships, for all three sources:

> **A configured USB capture port names exactly one device, or the source does not start.**

Not "any device". Not "system default". Not "the first that matched". The source fails, loudly, with a
message that names the port it wanted and the devices it actually saw.

### 0.5 ⛔ Radio: the deprecation is already implemented. Do nothing to it

The dossier proposes deciding `Devices/Radio` "separately and deliberately", and suspects the entry is
vestigial (`docs/queue/AUD-13.md:103`, `:126-129`). The deliberate decision is **no change**, and the reason
is that the deprecation the owner describes is already in the code and already in the UI.

- `RadioFactory.cs:147` — `GetDefaultDeviceType` reads `Radio:DefaultDevice` and **defaults to
  `DeviceTypes.RTLSDRCore`**. That key appears in neither `src/Radio.API/appsettings.json` nor
  `deploy/debian-x64/appsettings.Production.json`, so the default is what runs.
- `RadioFactory.cs:336-339` — a blank Radio port makes `IsRF320Available()` false, so RF320 is not offered
  and cannot be selected as a fallback.
- `AudioSourceFactory.cs:112-117` is the **only** in-tree caller of `IRadioFactory.CreateRadioSource`, and it
  passes `GetDefaultDeviceType()`. The one API surface that can name a device type,
  `RadioController.cs:942`, gates on `IsDeviceAvailable` first.
- ⭐ **And the UI already says so, in the operator's own words.** `SystemConfigPage.razor:279-280`:

```razor
                <RadzenTextBox @bind-Value="_deviceOptions.Radio.USBPort" Placeholder="Radio USB Audio (RF320 only)" Style="width:100%" />
                <small>Leave empty if using RTL-SDR. Only set for RF320 USB audio capture.</small>
```

⛔ **That copy is why "empty Radio USBPort is a configuration fault" must not ship.** An empty Radio port is
the *documented, intended, shipped* configuration for every box using RTL-SDR — which is every box. Raising a
fault for it would put a permanent red state on an appliance that is correctly configured (`C-233`). The
row's own instinct at `:126-128` was right; this section is the evidence for it.

**Removing `RadioAudioSource` is also not this row.** It implements `IRadioControl`, is branched on at
`RadioController.cs:892`, is referenced from `SourceSelectorService.cs:243` and
`SoundFlowMasterMixer.cs:108`, is covered by `USBAudioSourceTests.cs` and `RadioFactoryTests.cs`, and the
`RaddyRF320BT` submodule exists to serve it. That is a deprecation PR with its own UAT, not a rider on a
device-selection fix. §6.1 files it.

⚠ **The generic fix in Task 2 still applies to `RadioAudioSource`,** because it lives in the shared base
class. That is correct and costs nothing: on a box with an empty Radio port the source is never constructed,
and on a box that *has* named a port, refusing to bind to the wrong jack is the same improvement it is for
Vinyl.

### 0.6 ⚠ `ENC-12` is not a reusable config-fault framework, and adopting it here is out of proportion

`docs/queue/AUD-13.md:119-120` calls `ENC-12`'s tiered config-fault surfacing "the established in-repo
pattern". It shipped in `8df35ddc` (#535) and it is worth knowing what it actually is before planning
against it:

```
 src/Radio.Web/Models/EncoderFault.cs                | 162 +++++++++++++++++
 src/Radio.Web/Services/EncoderFaultAnnouncer.cs     |  97 ++++++++++
 src/Radio.Web/Components/Layout/MainLayout.razor    | 197 ++++++++++++++++++-
 src/Radio.API/Services/AudioStateUpdateService.cs   |  42 +++++
 …23 files changed, 1359 insertions(+)
```

Every type in it is encoder-shaped: `EncoderFault`, `EncoderConfigStatusDto`, `IRotaryEncoderService.ConfigStatus`,
a topbar badge bound to encoder state. **There is no `IConfigFault` for a second subsystem to implement.**
Reusing "the pattern" here means writing a second one — call it ~1,000 lines and a new SignalR payload — for a
fault whose whole content is *"Vinyl cannot find its turntable"* (`C-234`).

⛔ **Do not build a device-fault badge in this row.** The proportionate surfaces already exist and Task 4 uses
them: the source's `State` goes to `Error` (already wired, already visible via `/api/sources`), the exception
message reaches the caller, a Warning reaches journald, and the attempted-vs-available device names go into
the source's own metadata dictionary, which `/api/audio/nowplaying` already serves. If the owner later wants a
badge, that is a UI row against a fault surface this one leaves in place. §6.2.

### 0.7 The `Contains`-on-a-config-value shape, enumerated rather than assumed

Scope question 3 of the row. Every `Contains(<variable>, StringComparison…)` in `src/`:

| Site | Value comes from | Guarded? |
|---|---|---|
| `USBAudioSourceBase.cs:199` | `Devices:*:USBPort` config | ⚠ **no** — this row |
| `SoundFlowDeviceManager.cs:445-446` | `DisplayOptions.FriendlyNames[].Pattern` config | ✅ `!string.IsNullOrEmpty(mapping.Pattern) &&` — the in-tree precedent |
| `SoundFlowDeviceManager.cs:1026-1027` | `namePart` argument | ✅ guarded at `:998-1001`, `if (string.IsNullOrWhiteSpace(namePart)) { return null; }` — the second precedent |
| `AudioEngineInitializationService.cs:344-352` | `preferredDeviceName` config | ⚠ null-guarded, not empty-guarded — **but benign in this direction**: an empty value makes `!activeDevice.Name.Contains("")` false, so the re-search branch is skipped entirely. It cannot pick a wrong device; it can only decline to correct one. Noted, not fixed. |
| `LinuxBluetoothService.cs:2335` | a connected device's own name | n/a — not config |
| `WindowsBluetoothService.cs:880`, `WasapiLoopbackCaptureSource.cs:183-184` | Windows-only paths | n/a on the appliance |

⭐ **So the answer to the row's scope question 3 is: one site, and the repository already carries the correct
guard twice, in the same file, in the same subsystem.** `SoundFlowDeviceManager.FindCaptureDeviceByName`
(`:996-1043`) is doing the same job as `USBAudioSourceBase`'s loop, guards the empty case, and logs what it
matched. Task 1 makes the USB path look like it.

📌 **`FindCaptureDeviceByName` has zero callers in `src/`** (grep over all `.cs`). The right question, written,
tested by nobody, called by nobody — the same shape `AUD-11` §0.5 found in `IsPwRecordLinkedToBtNode`. It is
not deleted here; §6.3 files it.

### 0.8 ⭐ The existing tests encode the defect, which is where the fail-first evidence comes from

`tests/Radio.Infrastructure.Tests/Audio/Sources/Primary/USBAudioSourceTests.cs` configures ports that
**cannot match any real capture device** — `USBPort = "/dev/ttyUSB0"` and `"/dev/ttyUSB1"` (`:37-38`),
`"/dev/ttyUSB2"` (`:218`) — and then asserts the source reaches `Playing`:

```csharp
  [Fact]
  public async Task RadioAudioSource_PlayAsync_InitializesAndPlays()
  {
    var source = CreateRadioSource();
    await source.PlayAsync();
    Assert.Equal(AudioSourceState.Playing, source.State);
```

Those ports are `/dev` node paths; the values matched against them are MiniAudio *device display names*
(`DeviceOptions.cs:33-34` says so: *"Matched as a case-insensitive substring against SoundFlow capture device
names. Example: `\"AB13X\"` matches `\"AB13X USB Audio\"`"*). No device is called `/dev/ttyUSB0`. **So every
one of these tests is currently exercising case 4 or case 5 of §0.3 and asserting that it succeeds**
(`C-235`).

That is the single most useful fact in this plan for verification, and it cuts both ways:

- ✅ **It is a fail-first test that already exists.** Change one assertion to expect the refusal and it fails
  against `main` today and passes after Task 2. §4.2.
- ⚠ **It also means Task 2 breaks roughly ten existing tests** — every one that needs a source to reach
  `Playing`. They are not wrong to want that; they are wrong about how to get there. §4.3 says how, and it is
  the largest single piece of work in this row.

### 0.9 The estimate — **0.5 d**, plus a box question that is not a box session

| Work | Cost |
|---|---|
| Tasks 1–2 (the pure static, the caller that refuses) | 0.15 d |
| Task 3 (observability) + Task 4 (metadata) + Task 5 (comments and UI copy) | 0.1 d |
| Task 6 (tests — mostly repairing `USBAudioSourceTests`) | 0.25 d |

**What holds it to half a day:** the fix is one method extraction and one `if`/`throw`, the guard idiom is
already written twice in `SoundFlowDeviceManager`, the "not configured" message wording is already written in
`AudioSourceFactory`, and the fail-first test already exists and only needs its assertion inverted.

⚠ **What would push it to 1 d:** if `USBAudioSourceTests`' end-to-end `PlayAsync` tests cannot be repaired by
selecting a port from the machine's own enumeration (§4.3, option A) and need a construction seam instead
(option B). Builder decides after running the suite once, which is the first thing Task 6 does.

⚠ **The box question is one command, not a session.** The only thing that needs the appliance is *what
`Devices:Vinyl:USBPort` actually resolves to there* — appsettings ships `""`, but the SQLite config store
overrides it (`DeviceOptionsResolver.cs:55-61`) and the UI writes it (`SystemConfigPage.razor:2866`). §5 is
that command. It reads two HTTP endpoints and touches nothing.

### 0.10 ⛔ Not auto-mergeable. Two reasons

The repository's auto-merge policy permits a merge on green gates when UAT stands in for a user-flow check.
It does not apply here.

1. **This converts a working state into a failing one, on purpose, on a live appliance.** A box whose Vinyl
   port is stale today plays the wrong jack; after this it refuses to start the source. That is the correct
   trade and it is the owner's decision — but it is a decision, and the owner should make it against §5's
   actual reading of his box rather than against this plan's expectation of it.
2. **`USBAudioSourceBase` is the shared base of three user-facing sources, and no gate in this repository can
   observe the outcome that matters.** Every test in §4 pins *which device we chose*; none pins *which
   physical jack made a sound*. A fully green suite is consistent with Vinyl being silent on the appliance.

**Merge decision goes to the owner after §5 reports what the box's Vinyl port resolves to.**

### 0.11 Constraints found while planning — `C-230`–`C-237`

Numbering starts at **`C-230`**, deliberately above `UX-1`'s in-flight `C-220`–`C-224` with a gap, so a
concurrent Planner extending that block cannot collide.

**`C-230` and `C-232` change what this row is.** **`C-233` is a change the dossier invites that must not be
made.** **`C-231` and `C-235` change the work.** **`C-234` is a scope trap.** **`C-236` and `C-237` are
hazards in the fix itself.**

---

**`C-230` — ⚠⚠ FALSIFIES THE ROW'S HEADLINE. The empty-`USBPort` case cannot reach `USBAudioSourceBase.cs:199`
through any path that exists in the tree, and the innermost guard throws.**

Derivation and the three-layer table in §0.2. `SoundFlowDeviceManager.cs:248-250`'s
`ArgumentException.ThrowIfNullOrEmpty(usbPort)` runs from `USBAudioSourceBase.cs:122`, reached at `:177`,
before the loop at `:199`. **Consequence: the empty guard this row is named for is defence in depth, not the
fix.** It still ships — it costs three lines, it makes the intent explicit, and it removes the landmine if a
future caller reaches the base class another way — but it must not be described as fixing an observed
behaviour, in the PR body or anywhere else.

---

**`C-231` — ⚠ CHANGES THE WORK. There is a *third* silent-substitution branch, worse than the one the row
names, and it logs nothing.**

`USBAudioSourceBase.cs:207-214` guards its fallback with `captureDevices.Length > 0`. When the machine
enumerates **no** capture devices, `targetDevice` stays `null`, `:221` calls
`_audioEngine.InitializeCaptureDevice(targetDevice, captureFormat)`, and
`SerializedMiniAudioEngine.cs:77-79` passes a `DeviceInfo?` straight through to MiniAudio, which opens the
**system default capture device**. `:229-231` then logs `targetDevice?.Name ?? "default"` — the string
`"default"` — at Information, which per `CLAUDE.md` § *Deployment* is **file sink only** for `Radio.API`.
Case 5 in §0.3. Task 2 refuses it.

---

**`C-232` — ⚠ WIDENS THE ROW. The owner's fact 1 licenses refusing case 4 and case 3, not only case 1.**

*"The Vinyl source will ALWAYS have a USB port connector"* makes *"the configured device is not present"* an
illegitimate state by the same reasoning that makes *"no port is configured"* one. The dossier applied the
sentence to the branch that was already closed. §0.4. **Consequence: the plan's semantic is "exactly one
device, or the source does not start", and no further owner input is needed to justify it** — but §5 must
still tell the owner what it will do to his box.

---

**`C-233` — ⛔ A CHANGE THE DOSSIER INVITES THAT MUST NOT BE MADE. An empty `Devices:Radio:USBPort` is the
correct, documented, shipped configuration, and raising a fault for it would red-flag a healthy appliance.**

`SystemConfigPage.razor:280` reads *"Leave empty if using RTL-SDR. Only set for RF320 USB audio capture."*
`RadioFactory.cs:147` defaults the device type to `RTLSDRCore`; `:336-339` makes a blank Radio port mean *not
available* rather than *misconfigured*. §0.5. **Consequence: Task 2's not-configured guard fires only where a
source is actually constructed, which for Radio means only on a box that deliberately named a port. Do not
add a startup validation pass over `Devices:Radio:USBPort`.**

---

**`C-234` — the `ENC-12` "pattern" is a per-subsystem implementation, not a framework. Adopting it here is
~1,000 lines for a one-line fault.** §0.6. ⛔ Do not create `DeviceFault.cs`, a `DeviceFaultAnnouncer`, or a
second topbar badge in this row.

---

**`C-235` — ⚠ CHANGES THE WORK. Roughly ten tests in `USBAudioSourceTests.cs` currently assert that a source
with an unmatchable port reaches `Playing`. Task 2 breaks all of them, and that is the point.**

`:37-38` and `:218` configure `/dev/ttyUSB{0,1,2}` against MiniAudio *display names*. §0.8. **Consequence:
the largest single chunk of this row is test repair, §4.3, and Builder must not "fix" it by weakening the
production guard.**

---

**`C-236` — ⚠ HAZARD IN THE FIX. The not-configured guard must sit AFTER `base.InitializeAsync` and BEFORE
`ReserveUSBPort`, and the match guards must sit INSIDE the existing `try`.**

`base.InitializeAsync` (`AudioSourceBase.cs:149-153`) sets `State = Initializing`; throwing before it leaves a
source in `Created` reporting a fault it never entered. `ReserveUSBPort` (`:120-135`) is where the
`ArgumentException` comes from, so the explicit guard must precede it or the clearer message never appears.
And the match guards must throw from inside the `try` at `:179`, because the `catch` at `:234-241` is what
calls `CleanupCaptureDevice(); ReleaseUSBPort();` — a throw outside it leaks the reservation and the engine.

---

**`C-237` — ⚠ HAZARD IN THE FIX. `IsNullOrWhiteSpace`, not `IsNullOrEmpty`. A whitespace-only port matches
almost every device name.**

`" "` is a substring of `"USB Microphone"`, `"Built-in Audio Analog Stereo"` and `"Monitor of Built-in
Audio"`. `IsNullOrEmpty` would let it through to the loop and produce an ambiguous match across the whole
device list. `SoundFlowDeviceManager.cs:998` already uses `IsNullOrWhiteSpace` for exactly this; the factory
guards at `AudioSourceFactory.cs:158` and `:202` do too. Match them.

### 0.12 Things Builder must NOT do

- ⛔ **Do not describe the empty-string case as an observed defect** in the PR body, the commit message, or a
  code comment. `C-230`. It is a latent landmine that this row closes; it is not what was happening.
- ⛔ **Do not make an empty `Devices:Radio:USBPort` a fault.** `C-233`.
- ⛔ **Do not delete `RadioAudioSource`, `RadioFactory.DeviceTypes.RF320`, or the `Devices:Radio` config
  entry.** §0.5, §6.1.
- ⛔ **Do not build a device-fault badge, announcer, or DTO.** `C-234`, §6.2.
- ⛔ **Do not weaken the production guard to keep `USBAudioSourceTests` green.** `C-235`. If a test needs a
  matchable port, give it one (§4.3); if it cannot have one, skip it and say so in the file.
- ⛔ **Do not change `AudioEngineInitializationService.cs:344-352`.** §0.7 shows the empty case is benign
  there, and it is on the output path this row does not touch.
- ⛔ **Do not touch `LinuxBluetoothService.cs`, `PipeWireNativeStream.cs` or anything else `AUD-11` claims.**
  §2 of this plan and `AUD-11`'s tasks share no file.
- ⛔ **Do not edit `docs/BUILDER_QUEUE.md` or `docs/queue/AUD-13.md` from this plan.** §8 carries the wording
  for whoever updates them.
- ⛔ **Do not edit `CLAUDE.md`.** §6.4 files the one note it wants, with the reason.
- ⛔ **Do not run anything against `radio` beyond §5's two `curl`s without the owner present.**

---

## 1. Decision

### 1.1 The semantic: exactly one device, or the source does not start

| Case | Today | This row |
|---|---|---|
| port empty / whitespace | `ArgumentException` about a port reservation | **explicit `InvalidOperationException`** naming the source and where to configure it |
| no capture devices enumerated | silently opens the **system default** | **refuse** — `InvalidOperationException` |
| port matches nothing | Warning, then **`captureDevices[0]`** | **refuse** — `InvalidOperationException` listing what *was* enumerated |
| port matches several | silently **first match** | **refuse** — `InvalidOperationException` listing the candidates |
| port matches exactly one | bind | bind, unchanged, and record which device in metadata |

Three options were considered for the multi-match case.

| Option | Verdict |
|---|---|
| **Refuse** ✅ | **Taken.** An ambiguous pattern is a configuration the operator can fix in ten seconds from a message that names both candidates. Silently choosing is how `Vinyl` ends up on the USB Microphone across a reboot. |
| Prefer the first non-`Monitor of` match, warn | **Rejected.** It is today's behaviour with a louder log, and §0.3 row 4 is the demonstration that a Warning attached to a wrong action is not a fix. |
| Prefer the match that `IsDefault` | **Rejected.** `DeviceInfo.IsDefault` is about the *system* default, which is precisely the thing a named port exists to override. |

⚠ **The honest limitation, and it goes in the PR body.** This makes a misconfiguration fail instead of
mis-bind. It does **not** make a *correct* configuration verifiable: a port that matches exactly one device
still proves only that the name matched, not that the turntable is plugged into that jack. Nothing in this
repository can answer the second question, and this row does not pretend to.

### 1.2 Where the fault surfaces — the four places that already exist

Deliberately not a new surface (`C-234`).

| Signal | Where | Level | Sink |
|---|---|---|---|
| `InvalidOperationException` with the port, the outcome and the enumerated device names | thrown from `InitializeUSBCaptureAsync`, propagated by `PlayAsync` | — | the caller |
| `Logger.LogError` from the existing `catch` at `:236` | `Radio.Infrastructure` under `Radio.API` | **Error** | **journald** |
| Ambiguity / no-match / no-devices detail | new, Task 3 | **Warning** | **journald** |
| The device actually bound, and the pattern that chose it | source metadata, Task 4 | Information | file sink only, plus `/api/audio/nowplaying` → `extendedMetadata` |
| `State = AudioSourceState.Error` | already set by the `catch` at `:239` | — | `/api/sources` |

⚠ **Sink asymmetry, per `CLAUDE.md` § *Deployment*.** Every line this row adds is in `Radio.Infrastructure`,
loaded by `Radio.API`, whose console sink is level-restricted — so **Warning and Error reach
`journalctl -u radio-api` and Information does not.** All three fault lines are Warning or Error for that
reason. ⛔ **The successful bind must stay at Information**: it happens once per source activation, and log
volume on this box correlates with audible distortion.

---

## 2. Tasks

### Task 1 — the selection becomes a pure function that names its outcome

**New file:** `src/Radio.Infrastructure/Audio/Sources/Primary/CaptureDeviceSelector.cs`

Extracted as a static over **names** rather than over `SoundFlow.Structs.DeviceInfo`, so the test needs no
SoundFlow struct and makes no native call — the same reason `LinuxBluetoothService.ParsePwCliOutputForBtNode`
and `PipeWireNativeStream.BuildStreamProperties` are statics.

```csharp
namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Why a capture-device selection ended the way it did. AUD-13.
/// </summary>
internal enum CaptureDeviceSelectionOutcome
{
  /// <summary>Exactly one non-monitor device matched the configured pattern.</summary>
  Matched,

  /// <summary>More than one non-monitor device matched. AUD-13 treats this as a fault.</summary>
  Ambiguous,

  /// <summary>The configured pattern was null, empty or whitespace.</summary>
  PortNotConfigured,

  /// <summary>The engine enumerated no capture devices at all.</summary>
  NoCaptureDevices,

  /// <summary>Devices were enumerated and none matched the pattern.</summary>
  NoMatch,
}

/// <summary>Result of a capture-device selection. AUD-13.</summary>
/// <param name="Outcome">Which of the five cases occurred.</param>
/// <param name="SelectedIndex">
/// Index into the caller's device list, or -1 for every outcome other than
/// <see cref="CaptureDeviceSelectionOutcome.Matched"/>.
/// </param>
/// <param name="Candidates">
/// Every non-monitor device name that matched. One entry for Matched, two or more for Ambiguous,
/// empty otherwise. Used to build the fault message.
/// </param>
internal readonly record struct CaptureDeviceSelectionResult(
  CaptureDeviceSelectionOutcome Outcome,
  int SelectedIndex,
  IReadOnlyList<string> Candidates);

/// <summary>
/// Chooses a capture device by matching a configured substring against enumerated device names.
/// </summary>
/// <remarks>
/// ⚠ AUD-13. This exists as a pure static over NAMES so the selection contract can be pinned by a unit
/// test with no MiniAudio engine, no native library and no capture hardware. The behaviour it replaces
/// lived inline in USBAudioSourceBase.InitializeUSBCaptureAsync, where the only way to observe it was to
/// open a real device — which is why the substitution below went unnoticed for the life of the file.
///
/// ⚠ THE POINT OF THIS METHOD IS THAT IT NEVER SUBSTITUTES. The code it replaced fell back to
/// captureDevices[0] when nothing matched (USBAudioSourceBase.cs:207-214 at c01ab5a1) and to MiniAudio's
/// system default when nothing was enumerated at all, and it reported Ready afterwards either way. Both
/// are wrong-jack binds: on the appliance the candidates are a USB Microphone and the built-in analog
/// input, and enumeration order is not stable across reboots, deploys or hot-plug. A caller that wants a
/// device it did not name must ask for one explicitly; it must not get one by accident.
///
/// ⚠ Ambiguity is a FAULT, not a tie to be broken. A pattern matching two devices is a configuration the
/// operator can correct in seconds once told; silently taking the first is how a source binds to a
/// different jack after a reboot with nothing changed and nothing logged.
///
/// ⚠ IsNullOrWhiteSpace, not IsNullOrEmpty. " " is a substring of essentially every device name, so a
/// whitespace-only pattern would otherwise match the entire list. See plan AUD-13 C-237.
///
/// ⛔ Do NOT reintroduce a "use the first available device" arm here or in the caller, however loudly it
/// logs. Plan AUD-13 §0.3 documents that the existing warning at the old :210 was accurate, reached
/// journald, and still bound the wrong input — an accurate log attached to a wrong action.
/// </remarks>
internal static class CaptureDeviceSelector
{
  /// <summary>
  /// PipeWire loopbacks of output sinks are surfaced by MiniAudio as capture devices whose names begin
  /// with this. They are never a physical input and must never be selected.
  /// </summary>
  internal const string MonitorNamePrefix = "Monitor of";

  private static readonly IReadOnlyList<string> None = Array.Empty<string>();

  /// <summary>
  /// Selects the capture device whose name contains <paramref name="usbPort"/>.
  /// </summary>
  /// <param name="captureDeviceNames">Enumerated capture device names, in engine order.</param>
  /// <param name="usbPort">The configured substring pattern (Devices:*:USBPort).</param>
  internal static CaptureDeviceSelectionResult Select(
    IReadOnlyList<string?> captureDeviceNames, string? usbPort)
  {
    if (string.IsNullOrWhiteSpace(usbPort))
    {
      return new CaptureDeviceSelectionResult(
        CaptureDeviceSelectionOutcome.PortNotConfigured, -1, None);
    }

    if (captureDeviceNames.Count == 0)
    {
      return new CaptureDeviceSelectionResult(
        CaptureDeviceSelectionOutcome.NoCaptureDevices, -1, None);
    }

    var matchedIndexes = new List<int>();
    for (var i = 0; i < captureDeviceNames.Count; i++)
    {
      var name = captureDeviceNames[i];
      if (string.IsNullOrEmpty(name))
      {
        continue;
      }

      // Skip PipeWire loopbacks before matching, not after: a pattern that happens to appear in a
      // monitor's name must not consume the match and must not count towards ambiguity.
      if (name.StartsWith(MonitorNamePrefix, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      if (name.Contains(usbPort, StringComparison.OrdinalIgnoreCase))
      {
        matchedIndexes.Add(i);
      }
    }

    if (matchedIndexes.Count == 0)
    {
      return new CaptureDeviceSelectionResult(
        CaptureDeviceSelectionOutcome.NoMatch, -1, None);
    }

    var candidates = matchedIndexes.Select(i => captureDeviceNames[i]!).ToArray();

    return matchedIndexes.Count == 1
      ? new CaptureDeviceSelectionResult(
          CaptureDeviceSelectionOutcome.Matched, matchedIndexes[0], candidates)
      : new CaptureDeviceSelectionResult(
          CaptureDeviceSelectionOutcome.Ambiguous, -1, candidates);
  }
}
```

⚠ **`SelectedIndex` is `-1` for `Ambiguous`, deliberately.** Returning the first match would leave a caller
one `if` away from reproducing the bug this row exists to remove.

---

### Task 2 — the caller refuses instead of substituting

**File:** `src/Radio.Infrastructure/Audio/Sources/Primary/USBAudioSourceBase.cs`

**2a — the not-configured guard.** Insert between `await base.InitializeAsync(cancellationToken);` (`:175`)
and `ReserveUSBPort(usbPort);` (`:177`). ⚠ Placement is `C-236`: after `base.InitializeAsync` so the source
is in `Initializing` rather than `Created`, and before `ReserveUSBPort` so this message appears instead of
`SoundFlowDeviceManager`'s `ArgumentException`.

```csharp
    // AUD-13: an empty USB port is a missing configuration, not a permissive default. Today it does not
    // reach the device-matching loop below — SoundFlowDeviceManager.IsUSBPortInUse opens with
    // ArgumentException.ThrowIfNullOrEmpty (SoundFlowDeviceManager.cs:250) and ReserveUSBPort below calls
    // it — but that exception talks about a port reservation, and the three callers that construct these
    // sources (AudioSourceFactory.cs:158/:202, RadioFactory.cs:336) each guard it separately with their
    // own wording. This is the one guard on the path every USB source shares, so it is the one that can
    // say what is actually wrong. See plan AUD-13 C-230: this is defence in depth, NOT an observed bug.
    if (string.IsNullOrWhiteSpace(usbPort))
    {
      Logger.LogError(
        "{SourceName}: no USB capture device is configured. Set the device name pattern in "
        + "System > Configuration > Devices.", Name);
      State = AudioSourceState.Error;
      throw new InvalidOperationException(
        $"{Name} is not configured: no USB capture device name pattern is set. "
        + "Configure it in System > Configuration > Devices.");
    }
```

**2b — the selection.** Replace `:189-214` — the `var captureDevices = …` line, the `foreach` loop and the
`captureDevices[0]` fallback — with:

```csharp
      // Find the USB capture device matching the configured pattern.
      //
      // AUD-13: the selection is a pure static so it can be tested without hardware, and it REFUSES rather
      // than substituting. What was here fell back to captureDevices[0] when nothing matched and to
      // MiniAudio's system default when nothing was enumerated, then reported Ready either way — a
      // wrong-jack bind on a box whose enumeration order is not stable across reboots or hot-plug.
      var captureDevices = _audioEngine.CaptureDevices;
      var deviceNames = new string?[captureDevices.Length];
      for (var i = 0; i < captureDevices.Length; i++)
      {
        deviceNames[i] = captureDevices[i].Name;
      }

      var selection = CaptureDeviceSelector.Select(deviceNames, usbPort);
      if (selection.Outcome != CaptureDeviceSelectionOutcome.Matched)
      {
        // Warning, so it reaches `journalctl -u radio-api`: Radio.API's console sink is restricted to
        // Warning and above, and under systemd the console IS the journal. The enumerated names are in
        // the message because "which devices did it actually see" is the first thing anyone asks, and
        // reading it back off the box afterwards means running pw-cli during audio playback.
        Logger.LogWarning(
          "{SourceName}: refusing to start — USB port pattern \"{USBPort}\" {Outcome}. "
          + "Enumerated capture devices: [{Devices}]. Candidates: [{Candidates}]. See AUD-13.",
          Name,
          usbPort,
          DescribeOutcome(selection.Outcome),
          string.Join(", ", deviceNames.Where(n => !string.IsNullOrEmpty(n))),
          string.Join(", ", selection.Candidates));

        throw new InvalidOperationException(
          $"{Name} cannot start: the configured USB capture device pattern \"{usbPort}\" "
          + $"{DescribeOutcome(selection.Outcome)}. "
          + "Correct it in System > Configuration > Devices.");
      }

      var targetDevice = captureDevices[selection.SelectedIndex];
```

⚠ **`targetDevice` changes from `DeviceInfo?` to `DeviceInfo`** — the null case is gone, which is the point
(`C-231`). Its declaration at `:191` (`DeviceInfo? targetDevice = null;`) is deleted by this replacement.
`:221`'s `InitializeCaptureDevice(targetDevice, captureFormat)` still compiles, since the parameter is
`DeviceInfo?` (`SerializedMiniAudioEngine.cs:77-78`).

⚠ **The `throw` is inside the existing `try` that opens at `:179`,** so `catch (Exception ex)` at `:234-241`
runs `CleanupCaptureDevice(); ReleaseUSBPort(); State = AudioSourceState.Error;` and rethrows. `C-236`. Do
not add a second cleanup path.

**2c — the message helper**, added as a private static on the class:

```csharp
  /// <summary>
  /// Renders a <see cref="CaptureDeviceSelectionOutcome"/> as the middle of a sentence, so the log line
  /// and the exception message stay in one voice and cannot drift apart. AUD-13.
  /// </summary>
  private static string DescribeOutcome(CaptureDeviceSelectionOutcome outcome) => outcome switch
  {
    CaptureDeviceSelectionOutcome.NoMatch => "matched no capture device",
    CaptureDeviceSelectionOutcome.Ambiguous => "matched more than one capture device",
    CaptureDeviceSelectionOutcome.NoCaptureDevices => "could not be matched: no capture devices were enumerated",
    CaptureDeviceSelectionOutcome.PortNotConfigured => "is empty",
    _ => outcome.ToString(),
  };
```

⚠ **Keep the `_ =>` arm.** A future outcome added to the enum must render as *something* rather than throwing
from inside a log call on an initialization path.

---

### Task 3 — the successful bind says which device, and stays quiet about it

**File:** `src/Radio.Infrastructure/Audio/Sources/Primary/USBAudioSourceBase.cs`

Replace the log at `:229-231`, currently:

```csharp
      Logger.LogInformation(
        "{SourceName} initialized on USB port {USBPort} using device: {DeviceName}",
        Name, usbPort, targetDevice?.Name ?? "default");
```

with:

```csharp
      // Information: file sink only (Radio.API's console sink is Warning-and-above). Deliberately quiet —
      // this fires once per source activation, and log volume on this box correlates with audible
      // distortion. AUD-13: `?? "default"` is gone because "default" is no longer reachable; a null
      // targetDevice used to mean MiniAudio had silently opened the system default capture device.
      Logger.LogInformation(
        "{SourceName} initialized on USB port pattern \"{USBPort}\" using capture device: {DeviceName}",
        Name, usbPort, targetDevice.Name);
```

---

### Task 4 — the source can say what it bound to, on a surface that already exists

**File:** `src/Radio.Infrastructure/Audio/Sources/Primary/USBAudioSourceBase.cs`

Immediately after the `targetDevice` assignment in Task 2b, before `:216`'s format comment:

```csharp
      // AUD-13: record the binding on the metadata dictionary the source already publishes, so
      // /api/audio/nowplaying can answer "which physical input is this?" without anyone shelling into the
      // box. AudioDtoMapper.ExtractMetadataToNowPlaying (:148-156) copies every key that is not one of
      // Title/Artist/Album/AlbumArtUrl into NowPlayingDto.ExtendedMetadata, so these two need no DTO
      // change and no controller change to become visible.
      // ⛔ This is deliberately NOT a new fault surface: ENC-12's badge is encoder-shaped and reusing it
      // here means writing a second one (~1,000 lines) for a one-line fault. See plan AUD-13 C-234. The
      // failure states are carried by State = Error and the Warning above.
      MetadataInternal["CaptureDeviceName"] = targetDevice.Name ?? "(unnamed)";
      MetadataInternal["CaptureDevicePattern"] = usbPort;
```

⚠ **`ReserveUSBPort` already writes `_metadata["USBPort"] = usbPort;` at `:134`.** `CaptureDevicePattern` is
not a duplicate of it in meaning — `USBPort` records what was *reserved*, this records what the pattern
*resolved to* — but if a reviewer prefers one key, drop `CaptureDevicePattern` and keep
`CaptureDeviceName`, which is the one that carries new information.

⚠ **This is visible only while the source is ACTIVE.** `AudioController.cs:567` reads
`_audioManager.ActiveSource`, so `extendedMetadata.captureDeviceName` answers *"what is Vinyl bound to right
now"*, not *"what would Vinyl bind to"*. Say that in the PR body rather than letting it read as a general
diagnostic.

---

### Task 5 — the two comments and the one line of UI copy that describe the old semantic

**File:** `src/Radio.Core/Configuration/DeviceOptions.cs` — both doc comments, `:31-35` and `:44-47`. The
existing text is accurate about *how* the match works and silent about what an empty value means, which is
now a behaviour rather than an accident:

```csharp
  /// <summary>
  /// Gets or sets the USB audio device name pattern for the radio.
  /// Matched as a case-insensitive substring against SoundFlow capture device names.
  /// Example: "AB13X" matches "AB13X USB Audio".
  /// </summary>
  /// <remarks>
  /// ⚠ AUD-13: the pattern must resolve to EXACTLY ONE non-"Monitor of" capture device or the source
  /// refuses to start. Empty, no match, and more than one match are all faults — there is no
  /// "first available device" fallback any more.
  ///
  /// ⭐ Empty is nevertheless the CORRECT value here, and the only correct value on an RTL-SDR box.
  /// RadioFactory.IsRF320Available() reads a blank port as "the RF320 is not present" rather than as a
  /// misconfiguration, so an empty value means this source is never constructed and the guard above is
  /// never reached. Do not "fix" that into a startup fault — see plan AUD-13 C-233.
  /// </remarks>
```

and, on `VinylDeviceOptions.USBPort`:

```csharp
  /// <remarks>
  /// ⚠ AUD-13: unlike Devices:Radio:USBPort, empty is never correct here. The turntable is USB-only —
  /// all of its audio arrives over USB — so a blank pattern is a missing configuration, and
  /// AudioSourceFactory.CreateVinylSource refuses to construct the source at all.
  /// The pattern must resolve to exactly one non-"Monitor of" capture device.
  /// </remarks>
```

**File:** `src/Radio.Web/Components/Pages/SystemConfigPage.razor:283-284` — the Vinyl helper text currently
reads *"USB audio device name pattern for turntable (e.g., USB Microphone)"*, which does not say it is
required or that it must be unambiguous:

```razor
                <RadzenTextBox @bind-Value="_deviceOptions.Vinyl.USBPort" Placeholder="Vinyl Audio Device" Style="width:100%" />
                <small>Required. Must match exactly one capture device (e.g., <code>USB Microphone</code>). Too short a pattern can match several — the turntable will refuse to start rather than guess.</small>
```

⛔ **Leave `:279-280`'s Radio copy exactly as it is.** *"Leave empty if using RTL-SDR"* is correct and is the
evidence behind `C-233`.

⚠ **`SystemConfigPage.razor` is a `Radio.Web` file and `PHN-5`'s note applies to the project, not to this
line** — the copy above adds no logging. But note that `Radio.Web`'s console sink is unrestricted
(`CLAUDE.md` § *Deployment*), so **do not add a `LogInformation` to this page** while making the edit.

---

### Task 6 — tests

Detailed in §4. Listed as a task so it is not treated as optional, and because §4.3 is the largest single
piece of work in the row.

---

## 3. Ordering

**Task 1 → 2 → 3 → 4 → 5 → 6.**

- **1 before 2.** Task 2 calls the static Task 1 introduces.
- **3 and 4 after 2.** Both read `targetDevice`, which Task 2 makes non-nullable.
- **5 any time.**
- **6 last, but Builder runs the existing suite FIRST** — §4.1 — because `C-235`'s scale is the input to
  §0.9's estimate and to §4.3's choice between two repair strategies.

**One PR.** The deliverable is a property — *this source captures the device it was configured for, or it
does not start* — and it is not true until the refusal and the tests that pin it are both in. Shipping Task 2
without Task 6 would land a change whose only observable effect is that three sources might stop working,
with a test suite that still asserts they work.

---

## 4. Test plan

> ⚠ **This repository has repeatedly found tests that passed against a deliberately broken implementation.**
> Every pin below names the mutation that must make it fail, and **Builder runs each mutation and records the
> result in the PR body**. Where a test cannot falsify something, that is stated rather than implied.

**All of §4.2–§4.4 runs on Windows.** `Radio.Infrastructure.Tests` targets `net10.0`
(`Radio.Infrastructure.Tests.csproj`), `InternalsVisibleTo Radio.Infrastructure.Tests` is granted
(`Radio.Infrastructure.csproj:15`), and `Xunit.SkippableFact` 1.4.13 is already referenced — which §4.3
needs.

### 4.1 `T0` — establish the baseline before writing anything

⚠ **Run this first. §0.9's estimate and §4.3's strategy both depend on the answer**, and this plan could not
run it (a Builder was mid-cycle in the tree). Per `CLAUDE.md`, never pipe `dotnet test` into `tail`:

```bash
dotnet test tests/Radio.Infrastructure.Tests -c Release \
  --filter "FullyQualifiedName~USBAudioSourceTests" > /tmp/aud13-baseline.log 2>&1; echo "exit=$?"
grep -E "Passed!|Failed!|error" /tmp/aud13-baseline.log
```

**Record in the PR body:** how many of these tests pass on `main` today, and — from the run's own output or a
one-off scratch program — **what capture devices the build machine actually enumerates**. Both numbers are
inputs to §4.3.

### 4.2 `T1` — the selector, which is where the fix lives

**File:** `tests/Radio.Infrastructure.Tests/Audio/Sources/Primary/CaptureDeviceSelectorTests.cs` (new)

Fixture list, used by every case below — the appliance's own shape, per `CLAUDE.md` § *Deployment* and the
memory note on its hardware:

```csharp
  private static readonly string?[] AppliancePattern =
  [
    "USB Microphone",
    "Built-in Audio Analog Stereo",
    "Monitor of Built-in Audio Analog Stereo",
  ];
```

| Test | Pins | Falsifying mutation |
|---|---|---|
| `Select_EmptyPort_IsNotConfigured` | ⭐ the row's named case. `""` → `PortNotConfigured`, `SelectedIndex == -1` | delete the `IsNullOrWhiteSpace` arm → returns `Matched` at index 0 → fails |
| `Select_WhitespacePort_IsNotConfigured` | `C-237`. `" "` → `PortNotConfigured` | change to `IsNullOrEmpty` → returns `Ambiguous` → fails |
| `Select_NullPort_IsNotConfigured` | `null` → `PortNotConfigured` | as above |
| `Select_NoDevices_IsNoCaptureDevices` | ⭐ `C-231`, the case nobody had written down. `[]` + `"USB"` → `NoCaptureDevices` | delete the `Count == 0` arm → `NoMatch` → fails (and pins the two apart, which the message depends on) |
| `Select_UnmatchedPort_IsNoMatch` | ⭐ the live defect. `"AB13X"` → `NoMatch`, `SelectedIndex == -1` | add a `captureDevices[0]` fallback → `Matched` at 0 → fails |
| `Select_ExactlyOneMatch_ReturnsThatIndex` | `"Microphone"` → `Matched`, index `0` | swap the index for a constant → fails |
| `Select_MatchIsCaseInsensitive` | `"microphone"` → `Matched`, index `0` | drop `OrdinalIgnoreCase` → fails |
| `Select_TwoMatches_IsAmbiguous` | ⭐ the case the row's title actually describes. `"Audio"` → `Ambiguous`, **`SelectedIndex == -1`**, `Candidates.Count == 1`… see below | return `matchedIndexes[0]` for Ambiguous → the `-1` assertion fails |
| `Select_MonitorDevicesAreNeverSelected` | `"Monitor"` → `NoMatch` | drop the `StartsWith` skip → `Matched` on the loopback → fails |
| `Select_MonitorDoesNotCountTowardsAmbiguity` | ⭐ `"Analog Stereo"` against the appliance list → `Matched` (index 1), **not** `Ambiguous` — the monitor is skipped before matching | move the `StartsWith` check after the `Contains` and count it → `Ambiguous` → fails |
| `Select_NullAndEmptyDeviceNames_AreSkipped` | `[null, "", "USB Microphone"]` + `"USB"` → `Matched` at index `2` | drop the `IsNullOrEmpty` skip → `NullReferenceException` → fails |
| `Select_CandidatesListNamesEveryMatch` | the `Ambiguous` case's `Candidates` carries **both** names, in order — it is what the fault message prints | return `None` for Ambiguous → fails |

⚠ **`Select_TwoMatches_IsAmbiguous` needs a fixture where two *non-monitor* devices match.** The appliance
list has only one device containing `"Audio"` that is not a monitor, so use a second fixture for this case:

```csharp
    string?[] twoUsbDevices = ["USB Microphone", "USB Audio CODEC", "Monitor of Built-in Audio"];
    // "USB" matches indexes 0 and 1; the monitor is skipped.
```

⛔ **Do not assert `Assert.NotEqual(CaptureDeviceSelectionOutcome.Matched, …)` anywhere.** Four of the five
outcomes satisfy that, and the message text differs per outcome — a test that only pins "not Matched" passes
against a fix that reports the wrong reason.

### 4.3 `T2` — the existing suite, which currently asserts the defect

⚠⚠ **`C-235`. This is the largest piece of work in the row and Builder must not shortcut it.**

Every test in `USBAudioSourceTests.cs` that reaches `Playing` does so through case 4 or case 5 of §0.3 —
`/dev/ttyUSB0`, `/dev/ttyUSB1`, `/dev/ttyUSB2` are `/dev` node paths matched against MiniAudio display names,
and nothing is called that. After Task 2 they throw. Affected, by name:

`RadioAudioSource_PlayAsync_InitializesAndPlays` (`:81`), `RadioAudioSource_DisposeAsync_ReleasesUSBPort`
(`:106`), `VinylAudioSource_PlayAsync_InitializesAndPlays` (`:152`),
`VinylAudioSource_DisposeAsync_ReleasesUSBPort` (`:177`),
`GenericUSBAudioSource_InitializeWithPortAsync_InitializesSuccessfully` (`:212`),
`GenericUSBAudioSource_InitializeWithDeviceAsync_InitializesSuccessfully` (`:240`),
`GenericUSBAudioSource_PlayAsync_WhenInitialized_PlaysSuccessfully` (`:281`),
`GenericUSBAudioSource_DisposeAsync_ReleasesUSBPort` (`:295`), `AllUSBSources_PauseAsync_*` (`:315`),
`AllUSBSources_ResumeAsync_*` (`:339`), `AllUSBSources_StopAsync_*` (`:354`),
`AllUSBSources_StateChanged_EventRaised` (`:368`).

**Two of them become the fail-first evidence, and must be written first, against unmodified `main`:**

```csharp
  /// <summary>
  /// AUD-13 — fail-first. Against main at c01ab5a1 this test FAILS: the source reaches Playing because
  /// InitializeUSBCaptureAsync falls back to captureDevices[0] (or, on a machine with no capture devices,
  /// to MiniAudio's system default). "/dev/ttyUSB1" is a device node path; the values it is matched
  /// against are MiniAudio display names, so it matches nothing on any machine.
  /// ⛔ If this ever passes without Task 2's refusal, the refusal has been removed.
  /// </summary>
  [Fact]
  public async Task VinylAudioSource_PlayAsync_WhenPortMatchesNoDevice_RefusesToStart()
  {
    var source = CreateVinylSource();

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.PlayAsync());

    Assert.Contains("/dev/ttyUSB1", ex.Message);
    Assert.Equal(AudioSourceState.Error, source.State);
  }
```

and the empty-port twin, which needs the **real** device manager rather than the mock, because the mock is
what hides `C-230`:

```csharp
  /// <summary>
  /// AUD-13 — the row's named case. NOTE the mock: USBAudioSourceTests stubs IAudioDeviceManager
  /// (:58-59), which is why the empty-port path looks reachable when reading this file. In production
  /// SoundFlowDeviceManager.IsUSBPortInUse throws ArgumentException on an empty port before the match
  /// loop is reached (SoundFlowDeviceManager.cs:250). This test pins the explicit guard added by Task 2a,
  /// which fires FIRST and gives the operator a message about configuration rather than about a port
  /// reservation. See plan AUD-13 C-230 — this is defence in depth, not an observed defect.
  /// </summary>
  [Fact]
  public async Task VinylAudioSource_PlayAsync_WhenPortIsEmpty_RefusesWithAConfigurationMessage()
  {
    _deviceOptions.Vinyl.USBPort = "";
    var source = CreateVinylSource();

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => source.PlayAsync());

    Assert.Contains("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    Assert.IsNotType<ArgumentException>(ex);
  }
```

**The other ten need a source that actually initializes.** Two strategies; Builder picks after §4.1 reports
what the build machine enumerates.

- **Option A — give the tests a matchable port, from the machine's own enumeration.** A fixture helper opens
  `SerializedMiniAudioEngine.Create()`, reads `CaptureDevices`, and picks the first non-`Monitor of` name;
  the tests use a substring of it. `Skip.If(...)` on a machine with none. **Preferred** — it keeps the tests
  exercising the real path they were written for, and `Xunit.SkippableFact` is already referenced. ⚠ It makes
  them machine-dependent, so the skip reason must say so explicitly.
- **Option B — a construction seam.** Make `InitializeUSBCaptureAsync`'s enumeration overridable
  (`protected virtual IReadOnlyList<string?> EnumerateCaptureDeviceNames()`) and have the test subclass
  return a fixed list. Deterministic everywhere, but it means the tests stop exercising the MiniAudio open at
  `:221` — and `GenericUSBAudioSource_DisposeAsync_ReleasesUSBPort` exists to prove that teardown happens.

⛔ **Option C — relaxing the production guard so the old ports still work — is not on the table.** Say so in
the PR body if a reviewer proposes it.

> **What §4.3 cannot falsify:** that the *right* jack is bound. Every test here pins which name was chosen.
> Whether the turntable is plugged into the device with that name is unanswerable in this repository, and §5
> does not answer it either. **Write that sentence in the test file.**

### 4.4 `T3` — the outcome message, so the fault is actionable

**File:** `tests/Radio.Infrastructure.Tests/Audio/Sources/Primary/CaptureDeviceSelectorTests.cs`

| Test | Pins | Falsifying mutation |
|---|---|---|
| `DescribeOutcome_CoversEveryEnumMember` | iterate `Enum.GetValues<CaptureDeviceSelectionOutcome>()`; every one renders to something other than its own `ToString()` | add an enum member without a switch arm → fails, which is the point |

⚠ **`DescribeOutcome` is `private static` on `USBAudioSourceBase`.** `InternalsVisibleTo` does not reach
`private`. Either widen it to `internal static` (preferred — it is a message helper, not an invariant) or
drop this test and say so in the file. **Do not test it by reflection**; a renamed method would silently stop
being covered.

### 4.5 What no test in this row covers

Stated so a green suite is not read as more than it is:

- **That the bound device is the one the turntable is plugged into.** §1.1's honest limitation.
- **That the appliance's Vinyl port is currently correct.** §5 reads it; nothing asserts it.
- **The three factory guards** (`AudioSourceFactory.cs:158`, `:202`, `RadioFactory.cs:336`). They are
  pre-existing, unchanged by this row, and already covered by `RadioFactoryTests` for the RF320 arm. This row
  adds no coverage there and should not claim to.

---

## 5. Existing deployed configuration — what changes on the box

The row ships a behaviour change to a live appliance with a live config store. This section says exactly
what, and it is also the reason §0.10 sends the merge decision to the owner.

### 5.1 Where the value actually comes from — three layers, and the store wins

1. `src/Radio.API/appsettings.json:49-55` ships `"Radio": { "USBPort": "" }` and `"Vinyl": { "USBPort": "" }`.
2. `deploy/debian-x64/appsettings.Production.json:24-27` ships the same, and per `CLAUDE.md` the deploy
   overwrites `appsettings.json` but not the Production overlay.
3. **The SQLite config store overrides both.** `DeviceOptionsResolver.cs:55-61` reads `devices:Radio` /
   `devices:Vinyl` as serialized JSON and prefers them; `SystemConfigPage.razor:2866`'s
   `UpdateConfigurationAsync("devices", …)` is what writes them.

⚠ **A store entry wins even when its field is empty.** `ResolveNestedAsync` returns non-null for
`{"usbPort":""}`, so the `?? fallback.Vinyl` at `:60` is never reached and the appsettings value is not
consulted. Saving the Devices tab with a blank Vinyl box therefore *pins* the blank — it does not revert to
the default. Worth knowing when reading §5.2's output.

### 5.2 The one thing that needs the box — two reads, no session

⚠ **This plan does not know what the appliance's Vinyl port is**, and guessed values are how rows in this
queue end up resting on false premises. Run this before merging. It is two HTTP GETs; it starts nothing,
restarts nothing, and reads no journal:

```bash
curl -s http://radio:5000/api/configuration/devices
```

```bash
curl -s http://radio:5000/api/audio/nowplaying
```

**Then decide, with the owner, against the real value:**

| What `devices` reports for Vinyl | What this PR does to the box |
|---|---|
| a pattern matching exactly one capture device | **nothing changes.** Vinyl behaves identically. |
| a pattern matching **nothing** (stale, renamed, unplugged) | Vinyl goes from *silently capturing the wrong jack* to *refusing to start with a message naming the pattern and the devices it saw*. ⭐ **This is the intended change** and it is the owner's decision from `docs/queue/AUD-13.md:92-93`. |
| a pattern matching **several** | same as above, and this is the case the row's title describes. |
| **empty** | Vinyl was already un-constructible (`AudioSourceFactory.cs:158-162`); the operator gets the same refusal from one layer earlier with clearer wording. **No behaviour change.** |

**And for Radio:** an empty value is unaffected (§0.5) — the source is not constructed, RTL-SDR is the
default, nothing about the RF320 path executes. **A non-empty Radio value** on a box that also has an RTL-SDR
dongle is likewise unaffected, because `GetDefaultDeviceType` returns `RTLSDRCore` before availability
ordering is consulted (`RadioFactory.cs:147-166`). The only box this changes is one with a named Radio port
and **no** RTL-SDR device — where RF320 would be selected, and would now refuse rather than mis-bind. That is
the same trade as Vinyl and needs no separate decision.

### 5.3 No migration is written, and that is the recommendation

Three options were considered.

| Option | Verdict |
|---|---|
| **None — the fault message is the migration** ✅ | **Taken.** The message names the pattern and lists every enumerated device, and the fix is one text box in a UI that is already reachable from the panel. A config rewrite would have to guess which device the operator meant, which is the substitution this row exists to delete. |
| Rewrite a blank Vinyl port to a probed device at startup | **Rejected.** It is `captureDevices[0]` moved into the config store, where it would persist and look deliberate. |
| Clear a stale `devices:Radio` entry on upgrade | **Rejected.** It is not causing harm (§5.2), and `OPS-8` is the record of what a deploy that rewrites operator config costs. |

⛔ **Do not add a config migration, a seed, or a startup rewrite in this row.** The seeding surface has its own
row and its own plan (`OPS-7`).

---

## 6. Found while planning, filed rather than fixed

### 6.1 `RadioAudioSource` / RF320 deprecation — a row, not a rider

The owner has said the USB radio path is deprecated. The code already behaves that way (§0.5), so nothing is
broken by leaving it — but the type, its `IRadioControl` stub surface (`RadioAudioSource.cs:92-330`), the
`RadioController.cs:892` branch, `RadioFactory.DeviceTypes.RF320`, the `Devices:Radio` config entry, the
`SystemConfigPage.razor:279-280` field and the `RaddyRF320BT` submodule are all still carried. **Suggested
row `AUD-16` — "retire the RF320 USB radio path"**, P3, with its own UAT (the SOURCE overlay must not lose a
row the encoder can land on — see `ENC-5`).

### 6.2 A general config-fault surface

`ENC-12` built one for the encoder; this row deliberately builds none (`C-234`). If the owner wants device
faults on the topbar, the honest shape is **generalising `EncoderFault` into a subsystem-agnostic fault
model**, not writing a second parallel one. Suggested row, P3, and it should be planned *after* a second
subsystem actually needs it — which will be the third one, not this one.

### 6.3 `SoundFlowDeviceManager.FindCaptureDeviceByName` has zero callers

`:996-1043`. It asks the right question — *which capture device matches this name* — guards the empty case,
and nothing calls it (grep over all `.cs` in `src/`). It is the shape `AUD-11` §0.5 found in
`IsPwRecordLinkedToBtNode`. **Either Task 2 should call it, or it should be deleted.** It is neither here,
because it opens its own engine and takes `NativeAudioDeviceGate` in a way `USBAudioSourceBase`'s existing
flow does not, and reconciling those is a change to native device lifetime that this row should not carry.
Note it in the PR body.

### 6.4 `USBAudioSourceBase.cs:190` reads `CaptureDevices` outside the gate

`SerializedMiniAudioEngine.cs:49-56` says in its own words that the override only guarantees no two threads
are inside the native call, and that *"callers that need a device list consistent with their own enumeration
should read those properties inside their own `NativeAudioDeviceGate.Run<T>` region"*.
`USBAudioSourceBase.cs:190` does not. It is a **latent** race — the engine here is freshly constructed at
`:187` and not shared — so it is a note, not a defect, and widening the gate on an initialization path is a
change with its own risk. Suggested row, P3.

### 6.5 One `CLAUDE.md` note, not made here

`CLAUDE.md` § *Pre-Merge Review* lists three shipped comment/log mismatches. `USBAudioSourceBase.cs:210` is a
fourth **inverted** instance worth adding: an accurate warning attached to a wrong action. The existing
entries are all "the words claimed more than the code did"; this one is "the words were right and the code
did it anyway", which a reviewer checking comments against code would pass. ⛔ **Do not edit `CLAUDE.md` from
this PR** — propose it to the owner in the PR body.

---

## 7. Claims this plan did not verify

Stated explicitly, because the row this plan corrects was itself confidently wrong.

1. **No build and no test run was performed.** A Builder is mid-cycle on
   `fix/gv-texts-polish-overflow-unread-align` in this working tree, and a concurrent build contends on
   `obj/`. §4.1 is written so Builder measures first. **In particular, §0.8's claim that
   `USBAudioSourceTests`' `PlayAsync` tests currently pass is inference from the file's assertions, not an
   observation.** If they are already failing or skipped on `main`, `C-235`'s scale changes and §0.9's
   estimate with it.
2. **What MiniAudio does with `InitializeCaptureDevice(null, …)` is inferred**, from the parameter being
   `DeviceInfo?` (`SerializedMiniAudioEngine.cs:77-78`), from `:231`'s `?? "default"` fallback text, and from
   `USBAudioSourceTests` asserting `Playing` on a machine that may have no capture devices. **It was not
   observed.** It does not change the fix — Task 2 refuses the case either way — but if the real behaviour is
   a throw rather than a default-device open, then case 5 of §0.3 was already loud and `C-231` is smaller
   than stated. Builder can settle it from §4.1's device count.
3. **The appliance's actual `Devices:Vinyl:USBPort` is unknown.** §5.2 is the read. Nothing in this plan
   assumes a value.
4. **`ENC-12`'s scope was read from `git show --stat 8df35ddc`**, not from the files. The conclusion —
   encoder-shaped types, no reusable abstraction — rests on the file names and the diff size. If an
   `IConfigFault` exists that the file list does not reveal, `C-234` weakens; the recommendation not to build
   a badge in this row does not, because the row still would not need one.
5. **No screenshot, no browser, no box command was run.** `SystemConfigPage.razor:279-284` is read from
   source; what the operator sees on the panel today was not observed.

---

## 8. Wording for the queue and the dossier — for whoever updates them, not for this PR

⛔ **This plan does not edit `docs/BUILDER_QUEUE.md` or `docs/queue/AUD-13.md`.** The corrections below are
material and should land with the plan.

**`docs/BUILDER_QUEUE.md`, the `AUD-13` row** — the *plan* cell, replacing `_plan TBD — ⚠ needs an owner
decision first…_`:

> [`AUD-13-the-fallback-that-picks-the-first-jack.md`](../design/plans/AUD-13-the-fallback-that-picks-the-first-jack.md)
> · **0.5 d + two `curl`s against the box** · ⛔ **NOT auto-mergeable** — converts a working state into a
> failing one on a live appliance, deliberately · ⚠ **the plan FALSIFIED this row's headline**: an empty
> `USBPort` is refused three times over before `Contains("")` is reached (`SoundFlowDeviceManager.cs:250`
> throws), so the row's named defect is not reachable. The live defects are the *other* branches —
> unmatched pattern → `captureDevices[0]`, ambiguous pattern → first match silently, **no devices → the
> system default with no warning at all** · ⚠ **the owner decision is answered and needs no re-asking**:
> "always has a USB connector" licenses refusing all three · ⛔ **`Devices/Radio` empty is CORRECT, not a
> fault** — `SystemConfigPage.razor:280` documents it

**`docs/queue/AUD-13.md`** — the dossier's §*The defect* and §*Who is affected* both need the
`C-230` correction, and `:108-115` ("Radio and Vinyl match the same first non-`Monitor of` device — they are
aliases") should be struck: they are not aliases, they are both un-constructible, and if they were
constructed the second `ReserveUSBPort` would raise `AudioDeviceConflictException` rather than aliasing.
`:119-120`'s "`ENC-12` is the established pattern" wants the `C-234` qualifier.

---

## 9. PR body — what must be in it

- **The `C-230` correction, first and plainly.** The row's headline was not reachable; here is what was.
- §4.1's baseline numbers: how many `USBAudioSourceTests` passed on `main`, and what the build machine
  enumerates.
- Which repair strategy §4.3 used (A or B) and why.
- Every falsifying mutation from §4.2 and §4.4, run, with its result.
- §5.2's actual reading from the box, and the owner's merge decision against it.
- §1.1's honest limitation: this makes misconfiguration fail; it does not verify a correct configuration.
- The four items filed in §6, named, so they are not rediscovered.
- The Release build warning count against the **47/0 baseline** (`CLAUDE.md`), which is an equality gate, not
  a maximum.
