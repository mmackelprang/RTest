using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.API.Services;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.SoundFlow;
using IRadioConfigurationManager = Radio.Configuration.Abstractions.IConfigurationManager;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// AUD-85 (review MEDIUM-1): <c>POST /api/devices/cast/connect</c> arriving while the AUD-37
/// reconnect watcher is still running. The controller must not read the Cast output until the run
/// has finished — a read taken while the watcher connects answers for the watcher, not the user —
/// and must then act on what the run left: a kept connection to the picked device is the
/// already-streaming 200; anything else is the normal connect, with its local-restoring failures.
/// </summary>
/// <remarks>
/// The watcher is a fake <see cref="ICastReconnectControl"/> whose run is a
/// <see cref="TaskCompletionSource{TResult}"/> the test completes after putting the Cast output
/// into the state that run would leave — a rendezvous, not a race (CLAUDE.md § Test Timing). What
/// the real watcher leaves for each case is covered by <c>CastReconnectWatcherTests</c> and
/// <c>AudioEngineInitializationServiceCastReconnectTests</c>. The Cast output and engine set-up
/// follow <see cref="DevicesControllerCastConnectTests"/>.
/// </remarks>
public sealed class DevicesControllerCastPickReconnectTests : IAsyncDisposable
{
  private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(15);
  private const string DeviceId = "https://192.168.0.25/";

  private readonly Mock<IRadioConfigurationManager> _config = new();
  private readonly Mock<ICastReconnectControl> _reconnect = new();
  private readonly TaskCompletionSource<bool> _run = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private readonly GoogleCastOutput _castOutput;
  private readonly string _cacheFilePath;

  public DevicesControllerCastPickReconnectTests()
  {
    _cacheFilePath = Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");
    var options = new AudioOutputOptions();
    options.GoogleCast.CacheFilePath = _cacheFilePath;
    _castOutput = new GoogleCastOutput(NullLogger<GoogleCastOutput>.Instance, Options.Create(options));

    _config
      .Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    // The short-bound cancel answers at once — what a controller that used it would see.
    _reconnect.Setup(r => r.CancelCastReconnectAsync()).Returns(Task.CompletedTask);
    _reconnect.Setup(r => r.CancelCastReconnectForCastPickAsync(It.IsAny<string?>())).Returns(_run.Task);
  }

  public async ValueTask DisposeAsync()
  {
    await _castOutput.DisposeAsync();
    try
    {
      File.Delete(_cacheFilePath);
    }
    catch (IOException)
    {
      // Best effort: a leftover temp file is harmless.
    }
  }

  [Fact]
  public async Task APickOfTheDeviceBeingReconnected_WaitsForTheRun_ThenReports200OnTheKeptConnection()
  {
    // The watcher is mid-connect to this device. Read now, the output says Connecting — a 409, and
    // (before the fix) the run then tore the speaker down.
    SetState(AudioOutputState.Connecting);
    var (engine, _) = CreateEngine();
    await engine.SetActiveOutputAsync("speakers");

    var response = CreateController(engine).ConnectToCastDevice(Request(), CancellationToken.None);
    Assert.False(response.IsCompleted); // parked on the run, having read nothing

    // The run keeps its connection for the pick and switches the output back to Cast.
    var kept = StreamToTestDevice();
    await engine.SetActiveOutputAsync("google-cast");
    _run.SetResult(true);

    var result = await response.WaitAsync(HangGuard);

    Assert.IsType<OkObjectResult>(result);
    Assert.Same(kept, _castOutput.ConnectedDevice);              // not reconnected, not torn down
    Assert.Equal(AudioOutputState.Streaming, _castOutput.State);
    Assert.Equal("google-cast", engine.ActiveOutputId);
    VerifyDefaultSaved(Times.Once());
    _reconnect.Verify(r => r.CancelCastReconnectForCastPickAsync(DeviceId), Times.Once);
    _reconnect.Verify(r => r.CancelCastReconnectAsync(), Times.Never);
  }

  [Fact]
  public async Task APickOfAnotherDevice_WaitsForTheRunToRemoveItsConnection_ThenConnects()
  {
    // The watcher is mid-connect to ANOTHER speaker. After the run it has removed its connection,
    // and the pick connects its own device — the cancelled request token makes that connect fail
    // at a deterministic point (see DevicesControllerCastConnectTests), so reaching it shows as
    // Error and a 500, where a read during the run would have answered 409.
    SetState(AudioOutputState.Connecting);
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();

    var response = CreateController().ConnectToCastDevice(Request(), cancelled.Token);
    Assert.False(response.IsCompleted);

    SetState(AudioOutputState.Stopped); // the run's own tear-down
    _run.SetResult(true);

    var error = Assert.IsAssignableFrom<ObjectResult>(await response.WaitAsync(HangGuard));
    Assert.Equal(StatusCodes.Status500InternalServerError, error.StatusCode);
    Assert.Equal(AudioOutputState.Error, _castOutput.State); // the pick's own connect ran
    VerifyDefaultSaved(Times.Never());
  }

  [Fact]
  public async Task APickOfTheDeviceBeingReconnected_WhoseRunCouldNotConnect_ConnectsItself_AndLeavesLocalPlaying()
  {
    // The run's connect failed after the cancel: it returns with nothing of its own left and the
    // output still local. The pick then connects itself; its failure must not leave local muted.
    var (engine, _) = CreateEngine();
    await engine.SetActiveOutputAsync("speakers");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();

    var response = CreateController(engine).ConnectToCastDevice(Request(), cancelled.Token);
    SetState(AudioOutputState.Ready);
    _run.SetResult(true);

    var error = Assert.IsAssignableFrom<ObjectResult>(await response.WaitAsync(HangGuard));
    Assert.Equal(StatusCodes.Status500InternalServerError, error.StatusCode);
    Assert.Equal("speakers", engine.ActiveOutputId);
    Assert.False(engine.IsLocalOutputMuted);
    VerifyDefaultSaved(Times.Never());
  }

  [Fact]
  public async Task APickOfTheDeviceBeingReconnected_WhoseKeptConnectionIsLostDuringThePromotion_Returns502_AndRestoresLocal()
  {
    // The run kept its connection for the pick (Cast now active, local muted), and the speaker is
    // lost while the pick re-applies Cast through the gate. Not reported as success; local back.
    var (engine, http) = CreateEngine();
    await engine.SetActiveOutputAsync("speakers");

    var response = CreateController(engine).ConnectToCastDevice(Request(), CancellationToken.None);
    StreamToTestDevice();
    await engine.SetActiveOutputAsync("google-cast");
    http.Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
      .Callback(() => SetState(AudioOutputState.Error))
      .Returns(Task.CompletedTask);
    _run.SetResult(true);

    var error = Assert.IsAssignableFrom<ObjectResult>(await response.WaitAsync(HangGuard));
    Assert.Equal(StatusCodes.Status502BadGateway, error.StatusCode);
    Assert.Equal("default", engine.ActiveOutputId);
    Assert.False(engine.IsLocalOutputMuted);
    VerifyDefaultSaved(Times.Never());
  }

  [Fact]
  public async Task APickWhoseWaitForTheRunRunsOut_Returns409_AndTouchesNothing()
  {
    // The run is still inside a step that ignores cancellation at the bound. The Cast output is
    // still the run's: the pick neither reads it nor changes the outputs.
    SetState(AudioOutputState.Ready);
    var (engine, _) = CreateEngine();
    await engine.SetActiveOutputAsync("speakers");
    var epoch = engine.OutputSelectionEpoch;
    _run.SetResult(false);

    var result = await CreateController(engine).ConnectToCastDevice(Request(), CancellationToken.None).WaitAsync(HangGuard);

    var conflict = Assert.IsAssignableFrom<ObjectResult>(result);
    Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    Assert.Equal(AudioOutputState.Ready, _castOutput.State);
    Assert.Equal(epoch, engine.OutputSelectionEpoch);
    VerifyDefaultSaved(Times.Never());
  }

  [Fact]
  public async Task AnInvalidPick_Returns400_WithoutCancellingTheRunOrMarkingItsCancelAsAPick()
  {
    // AUD-85 re-review LOW-1. The request names a device but no address: refused before the
    // reconnect is touched. Cancelling would have ended the watcher's episode and — for a request
    // naming the device being reconnected — set its keep-for-pick flag for a pick that never
    // connects anything.
    var request = Request();
    request.IpAddress = "";

    var result = await CreateController().ConnectToCastDevice(request, CancellationToken.None).WaitAsync(HangGuard);

    Assert.IsType<BadRequestObjectResult>(result);
    _reconnect.Verify(r => r.CancelCastReconnectForCastPickAsync(It.IsAny<string?>()), Times.Never);
    _reconnect.Verify(r => r.CancelCastReconnectAsync(), Times.Never);
  }

  // --- helpers (as DevicesControllerCastConnectTests) ---

  private ChromecastDeviceInfo StreamToTestDevice()
  {
    var device = new ChromecastDeviceInfo
    {
      Id = DeviceId, FriendlyName = "Test speaker", IpAddress = "192.168.0.25", Port = 8009, Model = "Nest Audio"
    };
    SetConnectedDevice(device);
    SetState(AudioOutputState.Streaming);
    return device;
  }

  private void VerifyDefaultSaved(Times times) =>
    _config.Verify(c => c.SetValueAsync(
      It.IsAny<string>(), "AudioPreferences:DefaultCastDeviceId", DeviceId, It.IsAny<CancellationToken>()), times);

  /// <summary>A bare engine with mocked Cast (Streaming) and HTTP (Ready) outputs on its gate.</summary>
  private static (SoundFlowAudioEngine Engine, Mock<IAudioOutput> Http) CreateEngine()
  {
    var engineOptions = new Mock<IOptions<AudioEngineOptions>>();
    engineOptions.Setup(o => o.Value).Returns(new AudioEngineOptions { EnableHotPlugDetection = false });
    var prefs = new Mock<IOptionsMonitor<AudioPreferences>>();
    prefs.Setup(p => p.CurrentValue).Returns(new AudioPreferences());
    var outputOptions = new Mock<IOptionsMonitor<AudioOutputOptions>>();
    outputOptions.Setup(o => o.CurrentValue).Returns(new AudioOutputOptions());

    var engine = new SoundFlowAudioEngine(
      NullLogger<SoundFlowAudioEngine>.Instance,
      engineOptions.Object,
      new SoundFlowMasterMixer(NullLogger<SoundFlowMasterMixer>.Instance),
      new SoundFlowDeviceManager(
        NullLogger<SoundFlowDeviceManager>.Instance,
        new Mock<IRadioConfigurationManager>().Object,
        prefs.Object,
        outputOptions.Object));

    var cast = new Mock<IAudioOutput>();
    cast.SetupGet(c => c.State).Returns(AudioOutputState.Streaming);
    var http = new Mock<IAudioOutput>();
    http.SetupGet(h => h.State).Returns(AudioOutputState.Ready);
    engine.AttachOutputCoordination(cast.Object, http.Object, configManager: null);
    return (engine, http);
  }

  private DevicesController CreateController(SoundFlowAudioEngine? audioEngine = null)
  {
    var prefs = new Mock<IOptionsMonitor<AudioPreferences>>();
    prefs.SetupGet(p => p.CurrentValue).Returns(new AudioPreferences());

    return new DevicesController(
      NullLogger<DevicesController>.Instance,
      new Mock<IAudioDeviceManager>().Object,
      _config.Object,
      prefs.Object,
      Options.Create(new AudioOutputOptions()),
      audioEngine: audioEngine,
      castOutput: _castOutput,
      castReconnect: _reconnect.Object);
  }

  private static ConnectCastDeviceRequest Request() => new()
  {
    DeviceId = DeviceId,
    Name = "Test speaker",
    IpAddress = "192.168.0.25",
    Port = 8009,
    Model = "Nest Audio"
  };

  private void SetState(AudioOutputState state) =>
    typeof(AudioOutputBase)
      .GetProperty(nameof(AudioOutputBase.State), BindingFlags.Public | BindingFlags.Instance)!
      .SetValue(_castOutput, state);

  private void SetConnectedDevice(ChromecastDeviceInfo device) =>
    typeof(GoogleCastOutput)
      .GetProperty(nameof(GoogleCastOutput.ConnectedDevice), BindingFlags.Public | BindingFlags.Instance)!
      .SetValue(_castOutput, device);
}
