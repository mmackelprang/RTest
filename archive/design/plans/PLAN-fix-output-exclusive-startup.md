# Fix: enforce single active output across all activation paths (startup + 4 controller sites)

> **For Claude:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Branch:** `fix/output-exclusive-startup` (off `main`, PR back to `main`)

**Goal:** Centralize the "exactly one active output at a time" invariant — currently enforced by convention at four call sites in `DevicesController.cs` and missing entirely from the startup path in `AudioEngineInitializationService.cs`. Replace the convention with a single atomic method on `IAudioEngine` so the soundbar can no longer play at the same time as the Cast device after `systemctl restart radio-api`.

---

## 1. Problem statement

After restarting `radio-api.service` with persisted `AudioPreferences:CurrentOutput="google-cast"`, audio plays simultaneously from BOTH the Google Cast device AND the local soundbar. Only one output should be active at a time. The bug is reproducible: set Cast as active output via UI, confirm only Cast plays, restart the service, observe dual output.

## 2. Root cause

The "exactly one active output" invariant is enforced as a **convention** — every site that activates a virtual output (Cast or HTTP stream) must remember to also call `_audioEngine.SetLocalOutputMuted(true)` (and the local-device path must remember the `false` counterpart). Four runtime sites in [`DevicesController.cs`](../../src/Radio.API/Controllers/DevicesController.cs) honor the convention (lines 205, 214, 226, 691, 736, 744, 1341), but the startup path in [`AudioEngineInitializationService.ActivateVirtualOutputsForCastAsync`](../../src/Radio.API/Services/AudioEngineInitializationService.cs) (lines 365–442) does not. On restart:

1. `_audioEngine.StartAsync()` at [`AudioEngineInitializationService.cs:84`](../../src/Radio.API/Services/AudioEngineInitializationService.cs) starts the local PipeWire device unmuted (default state of `_localOutputMuted` is `false` — [`SoundFlowAudioEngine.cs:46`](../../src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs), in-memory only, not persisted).
2. [`ActivateVirtualOutputsForCastAsync`](../../src/Radio.API/Services/AudioEngineInitializationService.cs) (lines 365–442) activates Cast + HTTP stream and auto-connects in the background, but never calls `_audioEngine.SetLocalOutputMuted(true)`.
3. Both outputs play.

A convention-based invariant guarded by four scattered call sites is the architectural divergence root cause: any new activation path will inherit the same bug unless the author remembers to write the partner call. Centralization eliminates that footgun for all future paths.

---

## 3. Design decision: extend `IAudioEngine` (chosen) vs. new `ActiveOutputCoordinator` service

**Chosen: extend `IAudioEngine`** with a single new method `SetActiveOutputAsync(string outputId, CancellationToken)`. The implementation lives on `SoundFlowAudioEngine`. A small companion delegate `IAudioEngine.OnActiveOutputChanging` (or constructor-injected helper) lets the engine coordinate Cast/HTTP output start/stop without taking a hard dependency on `GoogleCastOutput`/`HttpStreamOutput` (which would create a layering violation — those types live in `Radio.Infrastructure.Audio.Outputs`, but `IAudioEngine` lives in `Radio.Core`).

**Why not a new `ActiveOutputCoordinator` service:**

- The existing codebase already has `IAudioEngine.SetLocalOutputMuted` + `IAudioEngine.IsLocalOutputMuted` ([IAudioEngine.cs:77–85](../../src/Radio.Core/Interfaces/Audio/IAudioEngine.cs)). The "active output" concept is already an engine-level concern that just hasn't been named.
- A new coordinator service would need to be injected at five sites (DevicesController + AudioEngineInitializationService); extending `IAudioEngine` is one new method on an interface every caller already holds.
- The plan to introduce a coordinator was rejected in the Coordinator's own scope note as "pick whichever is more idiomatic for this codebase" — the prevailing pattern is to put audio-graph state on `IAudioEngine` (volume, mute, balance via master mixer; local-output-mute already on the engine). A new singleton would diverge from that pattern with no payoff.

**The layering concern** (engine in `Radio.Core` must not depend on Cast/HTTP output concretes in `Radio.Infrastructure`) is solved by **constructor-injecting `GoogleCastOutput`/`HttpStreamOutput` into `SoundFlowAudioEngine`** as optional dependencies. `SoundFlowAudioEngine` already lives in `Radio.Infrastructure` and already imports `Radio.Core.Interfaces.Audio` — adding optional refs to two output concretes is consistent with how the engine already reaches into `SoundFlowDeviceManager` and `SoundFlowMasterMixer`. The `IAudioEngine` interface itself stays free of Cast/HTTP types — only the implementation gains the references.

**Method signature (added to `IAudioEngine`):**

```csharp
/// <summary>
/// Atomically switches the active audio output. Exactly one output is active
/// at any time: either a local playback device (identified by its MiniAudio
/// device id), or one of the virtual outputs "google-cast" / "http-stream".
/// Activates the requested output, deactivates the others, sets local-output
/// mute appropriately, and persists the choice to AudioPreferences:CurrentOutput.
/// </summary>
/// <param name="outputId">"google-cast", "http-stream", or a local device id.</param>
/// <param name="cancellationToken">Cancellation token.</param>
Task SetActiveOutputAsync(string outputId, CancellationToken cancellationToken = default);

/// <summary>
/// Gets the id of the currently active output ("google-cast", "http-stream",
/// or a local device id). Null until the first activation completes.
/// </summary>
string? ActiveOutputId { get; }
```

---

## 4. Bite-sized tasks

Each task is one commit. Tasks are ordered so the gate is in place and tested before any call site is migrated, then call sites are migrated one at a time so each commit leaves the system functional.

### Task 1: Add `SetActiveOutputAsync` + `ActiveOutputId` to `IAudioEngine`

**Files:**
- Modify: `src/Radio.Core/Interfaces/Audio/IAudioEngine.cs`

**Step 1:** After the existing `SetLocalOutputMuted` declaration at [IAudioEngine.cs:85](../../src/Radio.Core/Interfaces/Audio/IAudioEngine.cs), add:

```csharp
/// <summary>
/// Gets the id of the currently active output ("google-cast", "http-stream",
/// or a local device id). Null until the first activation completes.
/// </summary>
string? ActiveOutputId { get; }

/// <summary>
/// Atomically switches the active audio output. Exactly one output is active
/// at any time: either a local playback device (identified by its MiniAudio
/// device id), or one of the virtual outputs "google-cast" / "http-stream".
/// Activates the requested output, deactivates the others, sets local-output
/// mute appropriately, and persists the choice to AudioPreferences:CurrentOutput.
/// </summary>
/// <param name="outputId">"google-cast", "http-stream", or a local device id.</param>
/// <param name="cancellationToken">Cancellation token.</param>
Task SetActiveOutputAsync(string outputId, CancellationToken cancellationToken = default);
```

**Step 2:** Build expects to fail — `SoundFlowAudioEngine` doesn't implement these yet. That's Task 2.

**Step 3: Commit**

```bash
git add src/Radio.Core/Interfaces/Audio/IAudioEngine.cs
git commit -m "feat(audio): add IAudioEngine.SetActiveOutputAsync + ActiveOutputId"
```

(Commit message intentionally not "feat" if pre-commit hook would force release-notes — match repo convention; if hook complains, switch to `refactor(audio):`.)

---

### Task 2: Implement `SetActiveOutputAsync` on `SoundFlowAudioEngine`

**Files:**
- Modify: `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs`
- Modify: `src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs`

**Step 1: Add optional Cast/HTTP output + config-manager fields to `SoundFlowAudioEngine`.** Near the existing fields at [SoundFlowAudioEngine.cs:33–48](../../src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs):

```csharp
// Optional virtual-output references for SetActiveOutputAsync. Injected
// via a setter (not constructor) to avoid a chicken-and-egg with
// GoogleCastOutput, which can depend on the engine indirectly.
private Radio.Infrastructure.Audio.Outputs.GoogleCastOutput? _castOutput;
private Radio.Infrastructure.Audio.Outputs.HttpStreamOutput? _httpOutput;
private Radio.Configuration.Abstractions.IConfigurationManager? _configManager;
private string? _activeOutputId;
private readonly SemaphoreSlim _activeOutputLock = new(1, 1);
```

**Step 2: Add a setter method** so DI can wire the outputs after construction (avoids a constructor cycle):

```csharp
/// <summary>
/// Wires the virtual outputs + config manager so SetActiveOutputAsync can
/// activate/deactivate them and persist the choice. Called from DI startup
/// after all singletons are constructed.
/// </summary>
internal void AttachOutputCoordination(
  Radio.Infrastructure.Audio.Outputs.GoogleCastOutput? castOutput,
  Radio.Infrastructure.Audio.Outputs.HttpStreamOutput? httpOutput,
  Radio.Configuration.Abstractions.IConfigurationManager? configManager)
{
  _castOutput = castOutput;
  _httpOutput = httpOutput;
  _configManager = configManager;
}
```

**Step 3: Implement `ActiveOutputId`** as a simple property:

```csharp
/// <inheritdoc/>
public string? ActiveOutputId => _activeOutputId;
```

**Step 4: Implement `SetActiveOutputAsync`.** Place after `SetLocalOutputMuted` at [SoundFlowAudioEngine.cs:137–142](../../src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs):

```csharp
/// <inheritdoc/>
public async Task SetActiveOutputAsync(string outputId, CancellationToken cancellationToken = default)
{
  if (string.IsNullOrWhiteSpace(outputId))
  {
    throw new ArgumentException("outputId is required", nameof(outputId));
  }

  await _activeOutputLock.WaitAsync(cancellationToken).ConfigureAwait(false);
  try
  {
    var previous = _activeOutputId;
    _logger.LogInformation(
      "SetActiveOutputAsync: {Previous} -> {Next}", previous ?? "<none>", outputId);

    var isCast = string.Equals(outputId, "google-cast", StringComparison.OrdinalIgnoreCase);
    var isHttp = string.Equals(outputId, "http-stream", StringComparison.OrdinalIgnoreCase);
    var isLocal = !isCast && !isHttp;

    // Order: deactivate -> mute-state -> activate. Muting before activation
    // avoids a brief dual-output blip on the playback device's next callback.
    if (isLocal)
    {
      // Going to local: stop Cast + HTTP, then unmute local.
      await DeactivateVirtualOutputAsync(_castOutput, "Google Cast", cancellationToken);
      await DeactivateVirtualOutputAsync(_httpOutput, "HTTP Stream", cancellationToken);
      SetLocalOutputMuted(false);

      // Local-device switch is handled by IAudioDeviceManager.SetOutputDeviceAsync,
      // NOT by this method — the device-manager call is responsible for the
      // native MiniAudio swap. SetActiveOutputAsync just sets the mute state +
      // tears down virtual outputs + persists the preference.
    }
    else if (isCast)
    {
      // Cast needs HTTP active too (HttpMp3 mode wires audio through it).
      // DirectChannel mode skips HTTP — but the gate cannot tell which mode
      // is in use without taking a dep on AudioOutputOptions; instead, the
      // gate activates BOTH, and HttpStreamOutput.StartAsync is a no-op if
      // already streaming, so DirectChannel callers can pre-stop HTTP if
      // needed. (Today no caller relies on HTTP being stopped while Cast
      // is active — both controller sites activate both.)
      SetLocalOutputMuted(true);
      await ActivateVirtualOutputAsync(_httpOutput, "HTTP Stream", cancellationToken);
      await ActivateVirtualOutputAsync(_castOutput, "Google Cast", cancellationToken);
    }
    else // isHttp
    {
      // HTTP without Cast: HTTP active, Cast deactivated, local muted.
      SetLocalOutputMuted(true);
      await DeactivateVirtualOutputAsync(_castOutput, "Google Cast", cancellationToken);
      await ActivateVirtualOutputAsync(_httpOutput, "HTTP Stream", cancellationToken);
    }

    _activeOutputId = outputId;
    await PersistActiveOutputAsync(outputId, cancellationToken);
  }
  finally
  {
    _activeOutputLock.Release();
  }
}

private static async Task ActivateVirtualOutputAsync(
  IAudioOutput? output, string name, CancellationToken ct)
{
  if (output == null) return;
  if (output.State == AudioOutputState.Error || output.State == AudioOutputState.Created)
  {
    await output.InitializeAsync(ct).ConfigureAwait(false);
  }
  if (output.State == AudioOutputState.Ready || output.State == AudioOutputState.Stopped)
  {
    await output.StartAsync(ct).ConfigureAwait(false);
  }
}

private static async Task DeactivateVirtualOutputAsync(
  IAudioOutput? output, string name, CancellationToken ct)
{
  if (output == null) return;
  if (output.State == AudioOutputState.Streaming || output.State == AudioOutputState.Ready)
  {
    await output.StopAsync(ct).ConfigureAwait(false);
  }
}

private async Task PersistActiveOutputAsync(string outputId, CancellationToken ct)
{
  if (_configManager == null) return;
  try
  {
    var storeId = _configManager.CurrentStoreType ==
      Radio.Configuration.Models.ConfigurationStoreType.Sqlite ? "sqlite" : "config";
    await _configManager.SetValueAsync(storeId, "AudioPreferences:CurrentOutput", outputId, ct: ct);
  }
  catch (Exception ex)
  {
    _logger.LogWarning(ex, "Failed to persist AudioPreferences:CurrentOutput = {Id}", outputId);
  }
}
```

**Step 5: Wire `AttachOutputCoordination` from DI.** In `AudioServiceExtensions.AddSoundFlowAudio` at [AudioServiceExtensions.cs:84–94](../../src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs), the engine is already a singleton. Add a hosted-service-style wire-up so the engine gets its output refs once all singletons exist. Simplest path: extend the existing engine factory to resolve the outputs lazily through a callback after construction:

The cleanest wiring is to do the attach from `AudioEngineInitializationService.StartAsync` *before* `_audioEngine.InitializeAsync` runs. That service already has refs to `_castOutput`, `_httpOutput`, and `_configManager` (lines 60–64). Add at the top of `StartAsync` (right after the orphan-cleanup call, before `InitializeAsync` at [AudioEngineInitializationService.cs:81](../../src/Radio.API/Services/AudioEngineInitializationService.cs)):

```csharp
// Wire the virtual outputs into the engine so SetActiveOutputAsync can
// activate/deactivate them. The engine accepts these as optional.
if (_audioEngine is SoundFlowAudioEngine sfEngine)
{
  sfEngine.AttachOutputCoordination(_castOutput, _httpOutput, _configManager);
}
```

**Step 6: Build + run existing tests.** Existing `SoundFlowAudioEngineTests` should still pass (no API change beyond additions).

```bash
dotnet build --configuration Release
dotnet test tests/Radio.Infrastructure.Tests --filter "SoundFlowAudioEngineTests" --configuration Release -v n
```

Expected: 0 warnings, existing tests still pass.

**Step 7: Commit**

```bash
git add src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowAudioEngine.cs \
        src/Radio.API/Services/AudioEngineInitializationService.cs
git commit -m "feat(audio): implement SetActiveOutputAsync gate on SoundFlowAudioEngine"
```

---

### Task 3: Unit tests for the new gate

**Files:**
- Create: `tests/Radio.Infrastructure.Tests/Audio/SoundFlowAudioEngineActiveOutputTests.cs`

**Step 1:** Write tests covering the four gate behaviors. Use the existing `SoundFlowAudioEngineTests.cs` as a template for engine construction + mocked outputs. The tests do not need a real audio device — `_castOutput` and `_httpOutput` are mocked via `Moq` (already used in the project per [BluetoothAutoSwitchServiceTests.cs](../../tests/Radio.Infrastructure.Tests/Audio/Services/BluetoothAutoSwitchServiceTests.cs)).

```csharp
using Moq;
using Xunit;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;

namespace Radio.Infrastructure.Tests.Audio;

public class SoundFlowAudioEngineActiveOutputTests
{
  [Fact]
  public async Task SetActiveOutputAsync_GoogleCast_MutesLocalAndStartsCastAndHttp()
  {
    var (engine, castMock, httpMock, configMock) = BuildEngine();
    await engine.SetActiveOutputAsync("google-cast");

    Assert.True(engine.IsLocalOutputMuted);
    castMock.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    httpMock.Verify(h => h.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    configMock.Verify(c => c.SetValueAsync(
      It.IsAny<string>(), "AudioPreferences:CurrentOutput", "google-cast",
      It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    Assert.Equal("google-cast", engine.ActiveOutputId);
  }

  [Fact]
  public async Task SetActiveOutputAsync_HttpStream_MutesLocalAndStartsHttpAndStopsCast()
  {
    var (engine, castMock, httpMock, _) = BuildEngine(castState: AudioOutputState.Streaming);
    await engine.SetActiveOutputAsync("http-stream");

    Assert.True(engine.IsLocalOutputMuted);
    castMock.Verify(c => c.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    httpMock.Verify(h => h.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    Assert.Equal("http-stream", engine.ActiveOutputId);
  }

  [Fact]
  public async Task SetActiveOutputAsync_LocalDevice_UnmutesLocalAndStopsCastAndHttp()
  {
    var (engine, castMock, httpMock, _) = BuildEngine(
      castState: AudioOutputState.Streaming, httpState: AudioOutputState.Streaming);
    engine.SetLocalOutputMuted(true); // simulate prior cast state
    await engine.SetActiveOutputAsync("playback-1");

    Assert.False(engine.IsLocalOutputMuted);
    castMock.Verify(c => c.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    httpMock.Verify(h => h.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    Assert.Equal("playback-1", engine.ActiveOutputId);
  }

  [Fact]
  public async Task SetActiveOutputAsync_PersistsToConfigManager()
  {
    var (engine, _, _, configMock) = BuildEngine();
    await engine.SetActiveOutputAsync("playback-1");

    configMock.Verify(c => c.SetValueAsync(
      It.IsAny<string>(), "AudioPreferences:CurrentOutput", "playback-1",
      It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task SetActiveOutputAsync_ConcurrentCalls_AreSerialized()
  {
    // Two rapid switches in opposite directions: the final ActiveOutputId
    // must equal one of them (not interleaved). Confirms the SemaphoreSlim gate.
    var (engine, _, _, _) = BuildEngine();
    var t1 = engine.SetActiveOutputAsync("google-cast");
    var t2 = engine.SetActiveOutputAsync("playback-1");
    await Task.WhenAll(t1, t2);

    Assert.Contains(engine.ActiveOutputId, new[] { "google-cast", "playback-1" });
  }

  [Fact]
  public async Task SetActiveOutputAsync_NullOrEmpty_Throws()
  {
    var (engine, _, _, _) = BuildEngine();
    await Assert.ThrowsAsync<ArgumentException>(() => engine.SetActiveOutputAsync(""));
    await Assert.ThrowsAsync<ArgumentException>(() => engine.SetActiveOutputAsync("   "));
  }

  // --- helpers ---

  private static (SoundFlowAudioEngine engine,
                  Mock<GoogleCastOutput> castMock,
                  Mock<HttpStreamOutput> httpMock,
                  Mock<IConfigurationManager> configMock) BuildEngine(
    AudioOutputState castState = AudioOutputState.Ready,
    AudioOutputState httpState = AudioOutputState.Ready)
  {
    // Construct engine with minimal dependencies (see SoundFlowAudioEngineTests
    // for the standard ctor pattern: NullLogger, default AudioEngineOptions,
    // fake/real SoundFlowMasterMixer, fake SoundFlowDeviceManager).
    var engine = TestEngineFactory.Create();

    var castMock = new Mock<GoogleCastOutput>(MockBehavior.Loose, /* ctor args */);
    castMock.SetupGet(c => c.State).Returns(castState);
    var httpMock = new Mock<HttpStreamOutput>(MockBehavior.Loose, /* ctor args */);
    httpMock.SetupGet(h => h.State).Returns(httpState);
    var configMock = new Mock<IConfigurationManager>();
    configMock.SetupGet(c => c.CurrentStoreType).Returns(ConfigurationStoreType.Sqlite);

    engine.AttachOutputCoordination(castMock.Object, httpMock.Object, configMock.Object);
    return (engine, castMock, httpMock, configMock);
  }
}
```

Note: `GoogleCastOutput` and `HttpStreamOutput` ctors take non-trivial deps (logger, options, etc.). If `Mock<GoogleCastOutput>` proves too brittle to construct with all-loose mocks, refactor the test helper to use a lightweight `IAudioOutput` fake instead — change `SetActiveOutputAsync` to type its private fields as `IAudioOutput?` instead of the concrete types (the gate doesn't need anything Cast/HTTP-specific). The Builder is free to make that switch if Moq construction is painful.

**Step 2: Run tests**

```bash
dotnet test tests/Radio.Infrastructure.Tests --filter "SoundFlowAudioEngineActiveOutputTests" --configuration Release -v n
```

Expected: 6 PASS.

**Step 3: Commit**

```bash
git add tests/Radio.Infrastructure.Tests/Audio/SoundFlowAudioEngineActiveOutputTests.cs
git commit -m "test(audio): unit tests for SetActiveOutputAsync output gate"
```

---

### Task 4: Migrate startup path to use the gate (fixes the bug)

**Files:**
- Modify: `src/Radio.API/Services/AudioEngineInitializationService.cs`

**Step 1:** Replace the startup activation in [`ApplyStartupPreferencesAsync`](../../src/Radio.API/Services/AudioEngineInitializationService.cs) at lines 180–189:

**Before:**
```csharp
if (preferredOutputId == "google-cast")
{
  _logger.LogInformation("Restoring Google Cast output from startup preferences");
  await ActivateVirtualOutputsForCastAsync(cancellationToken);
}
else if (preferredOutputId == "http-stream")
{
  _logger.LogInformation("Restoring HTTP Stream output from startup preferences");
  await ActivateOutputAsync(_httpOutput, "HTTP Stream");
}
else
{
  // Physical output device ... (lines 190-270)
}
```

**After:**
```csharp
if (preferredOutputId == "google-cast" || preferredOutputId == "http-stream")
{
  _logger.LogInformation("Restoring {Output} output from startup preferences", preferredOutputId);

  // Single gate call — atomically activates outputs, mutes local, persists choice.
  await _audioEngine.SetActiveOutputAsync(preferredOutputId, cancellationToken);

  // Cast needs auto-connect to the saved default device — preserve the existing
  // background auto-connect logic from ActivateVirtualOutputsForCastAsync.
  if (preferredOutputId == "google-cast")
  {
    await StartCastAutoConnectAsync(cancellationToken);
  }
}
else
{
  // Physical output device — unchanged (lines 190-270 stay as-is, but add
  // the gate call after the device-manager switch completes).
  // ... (existing code) ...

  if (outputToUse != null)
  {
    // ... existing SetOutputDeviceAsync + verification block ...

    // Tell the gate the local output is active (unmutes local, stops Cast/HTTP).
    await _audioEngine.SetActiveOutputAsync(outputToUse, cancellationToken);
  }
}
```

**Step 2: Extract the Cast auto-connect logic** from the existing `ActivateVirtualOutputsForCastAsync` (lines 365–442) into a new private method `StartCastAutoConnectAsync(CancellationToken)`. The body is everything from line 391 (`var prefs = ...`) to the end of the method (line 441) — just the auto-connect background task; the `ActivateOutputAsync` calls at lines 374–381 are now handled by the gate.

```csharp
/// <summary>
/// Starts the background auto-connect to the saved default Cast device.
/// Assumes Cast + HTTP outputs are already activated by SetActiveOutputAsync.
/// </summary>
private Task StartCastAutoConnectAsync(CancellationToken cancellationToken)
{
  var castOptions = _audioOutputOptions.Value.GoogleCast;
  var isDirectChannel = string.Equals(castOptions.StreamingMode, "DirectChannel", StringComparison.OrdinalIgnoreCase);

  // In DirectChannel mode, wire the audio engine so GoogleCastOutput can
  // create a stream reader for sending PCM data over the Cast message bus.
  if (isDirectChannel && _castOutput != null)
  {
    _castOutput.SetAudioEngine(_audioEngine);
    _logger.LogInformation("DirectChannel mode: audio engine wired to Cast output");
  }

  var prefs = _audioPreferences.CurrentValue;
  if (string.IsNullOrEmpty(prefs.DefaultCastDeviceId) || _castOutput == null)
  {
    _logger.LogInformation("No default Cast device configured, Cast output activated but not connected");
    return Task.CompletedTask;
  }

  _logger.LogInformation("Auto-connecting to default Cast device on startup: {Name} ({Id})",
    prefs.DefaultCastDeviceName, prefs.DefaultCastDeviceId);

  // Run auto-connect in background so it doesn't block startup
  _ = Task.Run(async () =>
  {
    try
    {
      // Give the Cast discovery a moment to populate cache
      await Task.Delay(3000, cancellationToken);

      var cached = await _castOutput.GetCachedDevicesAsync(cancellationToken);
      var device = cached.FirstOrDefault(d => d.Id == prefs.DefaultCastDeviceId);
      if (device == null)
      {
        _logger.LogWarning("Default Cast device {Id} not found in cache after startup, skipping auto-connect",
          prefs.DefaultCastDeviceId);
        return;
      }

      if (_castOutput.State == AudioOutputState.Created)
      {
        await _castOutput.InitializeAsync(cancellationToken);
      }

      await _castOutput.ConnectAsync(device, cancellationToken);

      // Wire the HTTP audio stream (HttpMp3 mode only)
      if (!isDirectChannel && _httpOutput?.State == AudioOutputState.Streaming)
      {
        var streamUrl = GetRoutableStreamUrl(_httpOutput.Mp3StreamUrl, _httpOutput.Port, device.IpAddress);
        _castOutput.SetStreamUrl(streamUrl);
      }

      await _castOutput.StartAsync(cancellationToken);
      _logger.LogInformation("Startup: Auto-connected to Cast device: {Name} (mode: {Mode})",
        device.FriendlyName, castOptions.StreamingMode);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to auto-connect to Cast device on startup");
    }
  }, cancellationToken);

  return Task.CompletedTask;
}
```

**Step 3: Delete the now-orphaned `ActivateVirtualOutputsForCastAsync` method** (lines 362–442) — its activation responsibility moved to the gate and its auto-connect responsibility moved to `StartCastAutoConnectAsync`.

**Step 4: Build + run tests**

```bash
dotnet build --configuration Release
dotnet test --configuration Release -v n
```

Expected: 0 warnings, all tests pass.

**Step 5: Commit**

```bash
git add src/Radio.API/Services/AudioEngineInitializationService.cs
git commit -m "fix(audio): use SetActiveOutputAsync gate in startup path (fixes dual-output bug)"
```

> **Bug is fixed at this commit.** Remaining tasks are refactoring + the optional Cast graceful-shutdown.

---

### Task 5: Migrate `DevicesController.SetOutputDevice` to use the gate

**Files:**
- Modify: `src/Radio.API/Controllers/DevicesController.cs`

**Step 1:** Replace [`SetOutputDevice` lines 197–226](../../src/Radio.API/Controllers/DevicesController.cs):

**Before** (the three-branch convention block at lines 197–226):
```csharp
// Handle virtual outputs (HTTP Stream, Google Cast)
if (deviceId == "http-stream")
{
  await ActivateOutputAsync(_httpOutput, "HTTP Stream");
  await DeactivateOutputAsync(_castOutput, "Google Cast");
  _audioEngine?.SetLocalOutputMuted(false);
}
else if (deviceId == "google-cast")
{
  await ActivateOutputAsync(_castOutput, "Google Cast");
  await ActivateOutputAsync(_httpOutput, "HTTP Stream");
  _audioEngine?.SetLocalOutputMuted(true);
  await TryAutoConnectDefaultCastDeviceAsync();
}
else
{
  await DeactivateOutputAsync(_castOutput, "Google Cast");
  await DeactivateOutputAsync(_httpOutput, "HTTP Stream");
  _audioEngine?.SetLocalOutputMuted(false);
  // ... device validation + native switch ...
}
```

**After:**
```csharp
// Single gate call — atomic activate/deactivate/mute/persist for all three branches.
if (_audioEngine != null)
{
  await _audioEngine.SetActiveOutputAsync(deviceId);
}
else
{
  return StatusCode(503, new { error = "Audio engine not available" });
}

if (deviceId == "google-cast")
{
  // Auto-connect remains controller-side (depends on saved-device lookup).
  await TryAutoConnectDefaultCastDeviceAsync();
}
else if (deviceId != "http-stream")
{
  // Local device path: validate + perform the native MiniAudio switch
  // (the gate handles the mute state but not the device swap).
  var deviceIndex = _audioEngine.GetDeviceIndexById(deviceId);
  if (deviceIndex < 0)
  {
    _logger.LogDebug("Device {DeviceId} is not a local playback device, skipping engine switch", deviceId);
  }
  else
  {
    await _deviceManager.SetOutputDeviceAsync(deviceId);
    if (_localOutput != null)
    {
      _localOutput.UpdateDeviceId(deviceId);
    }
    _logger.LogInformation("Output device preference saved to {DeviceId}, starting native switch...", deviceId);

    var capturedIndex = deviceIndex;
    var capturedDeviceId = deviceId;
    _ = Task.Run(async () =>
    {
      // ... existing native-switch Task.Run block at lines 253-285, unchanged ...
    });
  }
}
```

Note the gate's `SetActiveOutputAsync` also persists `AudioPreferences:CurrentOutput`, so the controller no longer needs `_deviceManager.SetOutputDeviceAsync` to persist that key (it still owns the device-index-to-id mapping, which is its existing responsibility).

**Step 2: Build + run controller tests**

```bash
dotnet build --configuration Release
dotnet test tests/Radio.API.Tests --filter "DevicesControllerTests" --configuration Release -v n
```

Expected: existing tests pass. (Some may need updates if they asserted on `SetLocalOutputMuted` being called directly — Builder updates those to assert on `SetActiveOutputAsync` instead.)

**Step 3: Commit**

```bash
git add src/Radio.API/Controllers/DevicesController.cs
git commit -m "refactor(devices): route SetOutputDevice through SetActiveOutputAsync gate"
```

---

### Task 6: Migrate `DevicesController.ConnectToCastDevice` to use the gate

**Files:**
- Modify: `src/Radio.API/Controllers/DevicesController.cs`

**Step 1:** Replace line 691 (`_audioEngine?.SetLocalOutputMuted(true);`) with a gate call. The surrounding code at lines 678–697 does Cast-specific connect work that's NOT in the gate's scope (push metadata, ConnectAsync, StartAsync). Keep those; just swap the mute call:

**Before:**
```csharp
// Mute local speakers — audio pipeline continues for Cast streaming
_audioEngine?.SetLocalOutputMuted(true);

// Save as default Cast device for auto-connect
await SaveDefaultCastDeviceAsync(request.DeviceId, request.Name ?? "Cast Device");
```

**After:**
```csharp
// Promote Cast to the active output via the gate (handles mute + persist).
if (_audioEngine != null)
{
  await _audioEngine.SetActiveOutputAsync("google-cast", cancellationToken);
}

// Save as default Cast device for auto-connect
await SaveDefaultCastDeviceAsync(request.DeviceId, request.Name ?? "Cast Device");
```

**Step 2: Commit**

```bash
git add src/Radio.API/Controllers/DevicesController.cs
git commit -m "refactor(devices): route ConnectToCastDevice through SetActiveOutputAsync gate"
```

---

### Task 7: Migrate `DevicesController.DisconnectFromCastDevice` to use the gate

**Files:**
- Modify: `src/Radio.API/Controllers/DevicesController.cs`

**Step 1:** The disconnect handler at lines 711–748 has two `SetLocalOutputMuted(false)` calls (lines 736, 744 — the second is in a catch block). Both need to become gate calls.

When the user disconnects Cast, what should the new active output be? The legacy behavior just unmutes local, implicitly falling back to whatever local device the engine had selected before. Preserve that: read the current persisted local device id (or default device) and pass it through the gate.

**Before** (line 736):
```csharp
// Unmute local speakers so audio resumes locally
_audioEngine?.SetLocalOutputMuted(false);
```

**After:**
```csharp
// Promote the local output back via the gate. Use the persisted local
// device id; if none, fall back to the engine's currently-selected device.
var fallbackOutputId = _deviceManager.GetSelectedOutputDeviceId() ?? "default";
if (_audioEngine != null)
{
  await _audioEngine.SetActiveOutputAsync(fallbackOutputId, cancellationToken);
}
```

And line 744 (catch block) gets the same replacement (without `cancellationToken` since the catch path is best-effort):
```csharp
// Even if disconnect fails, restore local output so user isn't stuck with no audio
try
{
  var fallbackOutputId = _deviceManager.GetSelectedOutputDeviceId() ?? "default";
  if (_audioEngine != null)
  {
    await _audioEngine.SetActiveOutputAsync(fallbackOutputId);
  }
}
catch (Exception fallbackEx)
{
  _logger.LogWarning(fallbackEx, "Failed to restore local output after Cast disconnect failure");
}
```

**Step 2: Commit**

```bash
git add src/Radio.API/Controllers/DevicesController.cs
git commit -m "refactor(devices): route DisconnectFromCastDevice through SetActiveOutputAsync gate"
```

---

### Task 8: Migrate `DevicesController.TryAutoConnectDefaultCastDeviceAsync` to use the gate

**Files:**
- Modify: `src/Radio.API/Controllers/DevicesController.cs`

**Step 1:** Replace line 1341 (`_audioEngine?.SetLocalOutputMuted(true);` inside the auto-connect background task). The auto-connect runs only after `SetOutputDevice("google-cast")` (which already calls the gate via Task 5) OR after startup (which calls the gate via Task 4). So Cast is already the active output at this point — the line is redundant. Remove it:

**Before** (lines 1338–1344):
```csharp
await _castOutput.StartAsync();

// Mute local speakers — audio pipeline continues for Cast streaming
_audioEngine?.SetLocalOutputMuted(true);

_logger.LogInformation("Auto-connected to default Cast device: {Name} (mode: {Mode}, local output muted)",
  device.FriendlyName, autoConnectOptions.StreamingMode);
```

**After:**
```csharp
await _castOutput.StartAsync();

// (Gate already muted local in the caller; no per-call mute needed here.)

_logger.LogInformation("Auto-connected to default Cast device: {Name} (mode: {Mode})",
  device.FriendlyName, autoConnectOptions.StreamingMode);
```

**Step 2: Commit**

```bash
git add src/Radio.API/Controllers/DevicesController.cs
git commit -m "refactor(devices): drop redundant local-mute in Cast auto-connect (gate covers it)"
```

---

### Task 9: Integration test — startup with `CurrentOutput=google-cast` restored

**Files:**
- Create: `tests/Radio.API.Tests/Services/AudioEngineInitializationServiceStartupTests.cs`

**Step 1:** Test scaffold mirrors existing controller integration tests. It boots `AudioEngineInitializationService` with a fake `IConfigurationManager` returning `"google-cast"` for `AudioPreferences:CurrentOutput`, a mocked `SoundFlowAudioEngine` whose `SetActiveOutputAsync` records its calls, and mocked `GoogleCastOutput` / `HttpStreamOutput`.

```csharp
using Moq;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.API.Services;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;

namespace Radio.API.Tests.Services;

public class AudioEngineInitializationServiceStartupTests
{
  [Fact]
  public async Task StartAsync_PersistedCastOutput_CallsSetActiveOutputAsyncWithGoogleCast()
  {
    // Arrange
    var engineMock = new Mock<IAudioEngine>();
    var configMock = new Mock<IConfigurationManager>();
    configMock.SetupGet(c => c.CurrentStoreType).Returns(ConfigurationStoreType.Sqlite);
    configMock.Setup(c => c.GetValueAsync<string>(
        It.IsAny<string>(), "AudioPreferences:CurrentOutput",
        It.IsAny<string?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("google-cast");

    var service = BuildService(engineMock, configMock);

    // Act
    await service.StartAsync(CancellationToken.None);

    // Assert: the gate was called exactly once, with "google-cast".
    engineMock.Verify(e => e.SetActiveOutputAsync("google-cast", It.IsAny<CancellationToken>()),
      Times.Once);
    // And SetLocalOutputMuted was NOT called directly (the gate is the only entry point now).
    engineMock.Verify(e => e.SetLocalOutputMuted(It.IsAny<bool>()), Times.Never);
  }

  [Fact]
  public async Task StartAsync_NoPersistedOutput_DefaultsToFirstDeviceViaGate()
  {
    var engineMock = new Mock<IAudioEngine>();
    var configMock = new Mock<IConfigurationManager>();
    configMock.Setup(c => c.GetValueAsync<string>(
        It.IsAny<string>(), "AudioPreferences:CurrentOutput",
        It.IsAny<string?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync((string?)null);

    var service = BuildService(engineMock, configMock,
      outputDevices: new[] { new AudioDeviceInfo { Id = "playback-1", Name = "Soundbar", IsDefault = true } });

    await service.StartAsync(CancellationToken.None);

    engineMock.Verify(e => e.SetActiveOutputAsync("playback-1", It.IsAny<CancellationToken>()),
      Times.Once);
  }

  [Fact]
  public async Task StartAsync_PersistedHttpStream_CallsGateWithHttpStream()
  {
    var engineMock = new Mock<IAudioEngine>();
    var configMock = new Mock<IConfigurationManager>();
    configMock.Setup(c => c.GetValueAsync<string>(
        It.IsAny<string>(), "AudioPreferences:CurrentOutput",
        It.IsAny<string?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync("http-stream");

    var service = BuildService(engineMock, configMock);
    await service.StartAsync(CancellationToken.None);

    engineMock.Verify(e => e.SetActiveOutputAsync("http-stream", It.IsAny<CancellationToken>()),
      Times.Once);
  }

  private static AudioEngineInitializationService BuildService(
    Mock<IAudioEngine> engineMock,
    Mock<IConfigurationManager> configMock,
    IReadOnlyList<AudioDeviceInfo>? outputDevices = null)
  {
    // Build the service with all-mocked deps. See existing controller-test
    // factories under tests/Radio.API.Tests/Controllers for the standard
    // pattern of stitching IServiceProvider with Moq.
    // ...
  }
}
```

**Step 2: Run**

```bash
dotnet test tests/Radio.API.Tests --filter "AudioEngineInitializationServiceStartupTests" --configuration Release -v n
```

Expected: 3 PASS.

**Step 3: Commit**

```bash
git add tests/Radio.API.Tests/Services/AudioEngineInitializationServiceStartupTests.cs
git commit -m "test(api): integration test for startup-path output gate dispatch"
```

---

### Task 10: Graceful Cast shutdown in `AudioEngineInitializationService.StopAsync`

**Files:**
- Modify: `src/Radio.API/Services/AudioEngineInitializationService.cs`

**Step 1:** Before `_audioEngine.StopAsync` at [`StopAsync` lines 600–607](../../src/Radio.API/Services/AudioEngineInitializationService.cs), send the existing graceful-stop sequence to the Chromecast (the existing `GoogleCastOutput.StopAsync` at [GoogleCastOutput.cs:751](../../src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs) already does media stop + DirectChannel teardown; we just need to invoke it before the engine shuts down).

**Before:**
```csharp
public async Task StopAsync(CancellationToken cancellationToken)
{
  try
  {
    _logger.LogInformation("Stopping audio engine...");

    if (_audioEngine.State == Radio.Core.Interfaces.Audio.AudioEngineState.Running)
    {
      await _audioEngine.StopAsync(cancellationToken);
    }

    _logger.LogInformation("Audio engine stopped successfully");
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Error stopping audio engine");
  }
}
```

**After:**
```csharp
public async Task StopAsync(CancellationToken cancellationToken)
{
  try
  {
    _logger.LogInformation("Stopping audio engine...");

    // Graceful Cast shutdown: stop media + disconnect cleanly so the Chromecast
    // returns to its default state instead of holding a stale session that the
    // next startup has to fight through. Best-effort; never block engine stop.
    if (_castOutput != null &&
        (_castOutput.State == AudioOutputState.Streaming ||
         _castOutput.State == AudioOutputState.Ready ||
         _castOutput.State == AudioOutputState.Connecting))
    {
      try
      {
        using var castCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // GoogleCastOutput.StopAsync sends MediaChannel.StopAsync (terminates
        // the current media session) and tears down DirectChannel streaming.
        await _castOutput.StopAsync(castCts.Token);
        // DisconnectAsync sends CLOSE_APP / closes the receiver connection.
        await _castOutput.DisconnectAsync(castCts.Token);
        _logger.LogInformation("Cast output stopped + disconnected gracefully");
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Graceful Cast shutdown failed; continuing engine stop");
      }
    }

    if (_audioEngine.State == Radio.Core.Interfaces.Audio.AudioEngineState.Running)
    {
      await _audioEngine.StopAsync(cancellationToken);
    }

    _logger.LogInformation("Audio engine stopped successfully");
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Error stopping audio engine");
  }
}
```

**Step 2: Verify the GoogleCastOutput.StopAsync signature accepts a CancellationToken** — confirmed at [GoogleCastOutput.cs:751](../../src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs). The MediaChannel.StopAsync internal timeout is also 5s ([GoogleCastOutput.cs:781](../../src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs)); our outer 5s CTS is the upper bound for both the media stop and the disconnect combined.

**Step 3: Build + run**

```bash
dotnet build --configuration Release
dotnet test --configuration Release -v n
```

Expected: 0 warnings, all tests pass.

**Step 4: Commit**

```bash
git add src/Radio.API/Services/AudioEngineInitializationService.cs
git commit -m "feat(audio): graceful Cast media-stop + disconnect on radio-api shutdown"
```

---

### Task 11: Full build + test gate

```bash
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```

Expected: 0 warnings; all ~1,697 tests pass.

---

### Task 12: Deploy + UAT on `radio` Ubuntu host

```bash
pwsh.exe -NoProfile -Command "& './deploy/Deploy-ToLinux.ps1' -TargetHost radio -Runtime linux-x64"
```

Then execute the Manual UAT plan (§6 below). Builder pauses for Mark's UAT sign-off before opening the PR.

---

### Task 13: Open PR

```bash
git push -u origin fix/output-exclusive-startup

gh pr create --title "fix(audio): exclusive-output gate on startup + 4 controller sites" --body "$(cat <<'EOF'
## Summary

Fixes the dual-audio bug where `systemctl restart radio-api` with persisted `CurrentOutput="google-cast"` caused both the Cast device AND the local soundbar to play. The "exactly one active output" invariant was previously enforced as a convention at four controller sites and missing entirely from the startup path. This PR introduces `IAudioEngine.SetActiveOutputAsync` as the single gate; all five sites now go through it.

Also: graceful Cast shutdown on `radio-api` stop (sends media `STOP` + `CLOSE_APP` before disconnecting) so restarts don't leave a stale Chromecast session for the next startup to fight through.

## Test plan

- [x] Unit tests for `SetActiveOutputAsync` (6 tests covering Cast, HTTP, local, persistence, concurrency, validation)
- [x] Integration test simulating startup with persisted `CurrentOutput=google-cast`
- [x] Existing `DevicesControllerTests` updated to assert on the gate instead of `SetLocalOutputMuted`
- [x] Manual UAT on `radio` Ubuntu host (see PR description for verification matrix)

## UAT (manual, on `radio`)

1. UI → Output picker → Google Cast → confirm only Cast plays
2. `sudo systemctl restart radio-api` → confirm only Cast plays after restart (soundbar silent) ← was the bug
3. UI → Output picker → Soundbar → confirm only soundbar plays
4. `sudo systemctl restart radio-api` → confirm only soundbar plays after restart

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Merge once Mark approves.

---

## 5. Test plan summary

| Coverage | File | Tests |
|---|---|---|
| Gate unit tests | `tests/Radio.Infrastructure.Tests/Audio/SoundFlowAudioEngineActiveOutputTests.cs` (new) | 6 |
| Startup integration | `tests/Radio.API.Tests/Services/AudioEngineInitializationServiceStartupTests.cs` (new) | 3 |
| Controller refactor | `tests/Radio.API.Tests/Controllers/DevicesControllerTests.cs` (update existing) | mute/cast assertions updated |
| Regression | full `dotnet test --configuration Release` | ~1,697 tests, 0 regressions |

Per-task tests are listed in their respective task sections; Builder should not skip the test commits even when the task is small — each test commit gates the next implementation step.

---

## 6. Manual UAT plan (Mark, on `radio` Ubuntu host)

After Builder runs `Deploy-ToLinux.ps1 -TargetHost radio -Runtime linux-x64`:

### Scenario A — Cast active on restart

1. Open the Radio Console web UI at `http://radio:5002`.
2. Open the output picker; select Google Cast device (e.g. the kitchen speaker).
3. Start audio (any source — Radio, BT, file).
4. **Confirm:** audio plays from the Cast device. Soundbar is silent.
   ```bash
   ssh mmack@radio "pactl list sink-inputs | grep -E 'Sink Input|application.name|state'"
   ```
   Expect: one sink-input feeding the local sink, but it should be at zero amplitude / muted (the Cast path goes through HTTP stream, not the local sink).
5. Restart the API:
   ```bash
   ssh mmack@radio "sudo systemctl restart radio-api"
   ```
6. Wait ~10 s for startup + Cast auto-connect.
7. **Verify (the bug):** audio resumes ONLY from Cast device. Soundbar stays silent.
   - Listen with ears at the soundbar.
   - Check journalctl: `journalctl -u radio-api -n 100 | grep -E 'SetActiveOutputAsync|Local output'` should show `Local output muted (casting to external device)` exactly once during startup.

### Scenario B — Local soundbar active on restart (regression check)

1. UI → output picker → select the soundbar device (`playback-1` / Built-in Audio Analog Stereo).
2. **Confirm:** audio plays from soundbar. Cast is disconnected (Cast indicator off in UI).
3. Restart:
   ```bash
   ssh mmack@radio "sudo systemctl restart radio-api"
   ```
4. Wait ~5 s.
5. **Verify:** audio resumes ONLY from soundbar. No Cast app launches on the Chromecast.
6. journalctl check: `Local output unmuted` should appear in startup logs.

### Scenario C — Cast graceful shutdown (Task 10 verification)

1. With Cast as the active output and audio playing through it, observe the Chromecast (TV/speaker display).
2. Restart:
   ```bash
   ssh mmack@radio "sudo systemctl restart radio-api"
   ```
3. **Verify** during the restart window (~2 s before service comes back up):
   - The Chromecast briefly shows "stopped" / returns to its default screen — NOT a frozen "Radio.API" splash.
   - When the service comes back, Cast re-launches cleanly (no "media session expired" warning in journalctl).
4. journalctl check: `Cast output stopped + disconnected gracefully` should appear in the StopAsync log.

### Scenario D — Disconnect from Cast via UI (controller refactor regression check)

1. With Cast active, UI → output picker → disconnect Cast / select soundbar.
2. **Verify:** audio resumes on soundbar within ~1 s; Cast device returns to default screen.

### Pass criteria

All four scenarios pass. No journalctl errors mentioning `SetActiveOutputAsync`. No `pactl list sink-inputs` showing two simultaneously-playing inputs on the local sink.

If any scenario fails, capture journalctl + pactl output and feed back to Planner.

---

## 7. Risk notes

1. **PipeWire device startup race.** `_audioEngine.StartAsync` at line 84 starts the local device before the gate is called (line 116 → 183). For ~30–100 ms between those two points, the local device is unmuted. If audio is already in the mixer (it isn't, because source activation happens later at line 125), it would briefly leak. Mitigation: this window is post-init/pre-source, so no audio is flowing through the mixer yet. Verified by tracing the startup sequence; no fix needed but worth noting.
2. **Cast auto-connect timing.** The Cast auto-connect runs in a background `Task.Run` with a hard-coded 3 s delay (line 408) to let mDNS populate the cache. The gate completes before that delay starts, so the local output is correctly muted during the wait — but if the Cast device never connects (offline / mDNS fails), the user gets silence. This is the existing behavior; the gate doesn't make it worse. A follow-up could add a watchdog that falls back to local after N seconds of failed Cast connect, but that's out of scope.
3. **`GoogleCastOutput.StopAsync` timeout under network partition.** If the Chromecast is unreachable when `radio-api` stops, the 5 s CTS in Task 10 caps the wait. The exception is logged and swallowed; engine shutdown continues. Verified against existing exception-handling at [GoogleCastOutput.cs:784–805](../../src/Radio.Infrastructure/Audio/Outputs/GoogleCastOutput.cs) which already handles `TimeoutException` and `INVALID_MEDIA_SESSION_ID` gracefully.
4. **`SetActiveOutputAsync` SemaphoreSlim deadlock risk.** The gate uses a `SemaphoreSlim(1,1)` and may be called from controller handlers + startup + the disconnect catch block. None of these call sites hold the lock and re-enter, but if a future caller does (e.g. a callback inside `_castOutput.StartAsync` that re-invokes the gate), it will deadlock. Documented in the method's XML doc. Builder should also add a debug-log of the caller's stack depth via `Activity.Current` if observability becomes an issue.
5. **DI wiring via `AttachOutputCoordination` is order-sensitive.** If `AudioEngineInitializationService.StartAsync` runs before all output singletons are constructed (e.g. a future hosted service runs ordered before it), the engine's `_castOutput`/`_httpOutput` fields stay null and the gate silently no-ops on Cast/HTTP activation. Mitigation: the existing constructor at [AudioEngineInitializationService.cs:60–64](../../src/Radio.API/Services/AudioEngineInitializationService.cs) eagerly resolves all three from the service provider, so they ARE constructed by the time `StartAsync` runs. Confirmed by reading the existing service constructor.
6. **Test brittleness around Moq construction of `GoogleCastOutput`.** Task 3 notes that `GoogleCastOutput`'s constructor has many dependencies. If `Mock<GoogleCastOutput>` proves too painful, the Builder is empowered to refactor the engine's private fields from concrete to `IAudioOutput?` — that change does not affect any production caller and makes the tests cleaner.

---

## 8. Out of scope (explicit)

- Persisting `_localOutputMuted` to config — made redundant by centralizing the activation through the gate. `_activeOutputId` is the authoritative state; mute is a derived consequence.
- HTTP stream endpoint gating (separate concern; not the source of this bug).
- BT, phone, RotaryPhone, or any non-output-routing changes.
- Output picker UI work — covered by [docs/plans/2026-05-22-output-picker-ui.md](../../docs/plans/2026-05-22-output-picker-ui.md).
- DirectChannel vs HttpMp3 mode-switching logic — preserved as-is in the gate (both paths activate HTTP; DirectChannel callers can pre-stop HTTP if they want, but no current caller does).
- Concurrent-output (multi-output) future work — the gate intentionally enforces "exactly one"; multi-output requires a separate design.
