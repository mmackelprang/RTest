using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.SoundFlow;
using IRadioConfigurationManager = Radio.Configuration.Abstractions.IConfigurationManager;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// AUD-85: <c>POST /api/devices/cast/connect</c> treats a connect for the device already
/// streaming as success, answers 409 — not 500 — when the Cast output is mid-transition, and
/// does not report success (or save the default) when Cast is not live after the output gate.
/// A device switch that throws while Cast is the active output puts local output back (re-review M-a).
/// </summary>
/// <remarks>
/// <para>
/// Uses a real <see cref="GoogleCastOutput"/> placed into the state under test by reflection on
/// its non-public setters (<c>State</c>, <c>ConnectedDevice</c>), the same approach
/// <c>AudioEngineInitializationServiceCastLossTests</c> takes for its event: Radio.API.Tests has no
/// access to Radio.Infrastructure internals, and no production seam was added for this.
/// </para>
/// <para>
/// The output-gate tests use a bare, never-initialised <see cref="SoundFlowAudioEngine"/> (no native
/// device is opened) with mocked virtual outputs attached. The engine's own Cast reference is a
/// mock rather than the controller's <see cref="GoogleCastOutput"/>, so the gate never drives the
/// real output's network paths; the speaker loss is simulated by flipping the real output's state
/// from inside the gate's HTTP-output activation, which runs before the gate's Cast activation.
/// </para>
/// <para>
/// The post-gate check on the full connect path (after a real <c>ConnectAsync</c>/<c>StartAsync</c>)
/// is not covered here: reaching it needs a successful <c>ConnectAsync</c>, which needs a Cast
/// receiver on the network, and the output's test seams are internal to Radio.Infrastructure.
/// Both paths share <c>RestoreLocalIfCastNotLiveAsync</c>.
/// </para>
/// </remarks>
public sealed class DevicesControllerCastConnectTests : IAsyncDisposable
{
  private const string DeviceId = "https://192.168.0.25/";

  private readonly Mock<IRadioConfigurationManager> _config = new();
  private readonly GoogleCastOutput _castOutput;
  private readonly string _cacheFilePath;

  public DevicesControllerCastConnectTests()
  {
    _cacheFilePath = Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");
    var options = new AudioOutputOptions();
    options.GoogleCast.CacheFilePath = _cacheFilePath;
    _castOutput = new GoogleCastOutput(NullLogger<GoogleCastOutput>.Instance, Options.Create(options));

    _config
      .Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);
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
  public async Task Connect_WhenAlreadyStreamingToSameDevice_Returns200_WithoutReconnecting()
  {
    var device = StreamToTestDevice();

    var result = await CreateController().ConnectToCastDevice(Request(), CancellationToken.None);

    Assert.IsType<OkObjectResult>(result);
    // A reconnect would have stopped the stream (ConnectAsync stops a streaming output first).
    Assert.Equal(AudioOutputState.Streaming, _castOutput.State);
    Assert.Same(device, _castOutput.ConnectedDevice);
    VerifyDefaultSaved(Times.Once());
  }

  [Fact]
  public async Task Connect_WhenAlreadyStreamingToSameDevice_WithEngine_PromotesCast_Returns200()
  {
    StreamToTestDevice();
    var (engine, _) = CreateEngine();

    var result = await CreateController(audioEngine: engine).ConnectToCastDevice(Request(), CancellationToken.None);

    Assert.IsType<OkObjectResult>(result);
    Assert.Equal("google-cast", engine.ActiveOutputId);
    VerifyDefaultSaved(Times.Once());
  }

  [Fact]
  public async Task Connect_WhenCastIsLostDuringThePromotion_Returns502_RestoresLocal_AndKeepsDefaultUnsaved()
  {
    StreamToTestDevice();
    var (engine, http) = CreateEngine();
    // The gate activates the HTTP output before the Cast output. Losing the speaker there stands
    // in for AUD-84's deferred loss replaying while Cast is being promoted.
    http.Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
      .Callback(() => SetState(AudioOutputState.Error))
      .Returns(Task.CompletedTask);

    var result = await CreateController(audioEngine: engine).ConnectToCastDevice(Request(), CancellationToken.None);

    var error = Assert.IsAssignableFrom<ObjectResult>(result);
    Assert.Equal(StatusCodes.Status502BadGateway, error.StatusCode);
    // Back on local: the device manager reports no selection, so the fallback id is "default",
    // the same target DisconnectFromCastDevice restores.
    Assert.Equal("default", engine.ActiveOutputId);
    Assert.False(engine.IsLocalOutputMuted);
    VerifyDefaultSaved(Times.Never());
  }

  [Fact]
  public async Task Connect_SwitchingDevicesWhileCastIsActive_ConnectThrows_Returns500_AndRestoresLocal()
  {
    // Cast is the active output, streaming to another speaker; the request is for this one.
    StreamToTestDevice(id: "https://192.168.0.99/");
    var (engine, _) = CreateEngine();
    await engine.SetActiveOutputAsync("google-cast", CancellationToken.None);
    Assert.True(engine.IsLocalOutputMuted);

    // A cancelled request token makes the switch fail at a deterministic point AFTER the old
    // stream was stopped: ConnectAsync's stop-before-switch ends in Stopped (its lock wait throws
    // and its catch still stops), then the connect's own lock wait throws, leaving Error. It also
    // stands in for a real failure mode — the client giving up mid-switch.
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();

    var result = await CreateController(audioEngine: engine).ConnectToCastDevice(Request(), cancelled.Token);

    var error = Assert.IsAssignableFrom<ObjectResult>(result);
    Assert.Equal(StatusCodes.Status500InternalServerError, error.StatusCode);
    Assert.Equal(AudioOutputState.Error, _castOutput.State);
    // Not left on a dead Cast output with local muted: back on the same fallback the 502 path uses.
    Assert.Equal("default", engine.ActiveOutputId);
    Assert.False(engine.IsLocalOutputMuted);
    VerifyDefaultSaved(Times.Never());
  }

  [Fact]
  public async Task Connect_WhenOutputIsConnecting_Returns409_AndLeavesTheBusyOutputAlone()
  {
    SetState(AudioOutputState.Connecting);
    var audioManager = new Mock<IAudioManager>();

    var result = await CreateController(audioManager.Object).ConnectToCastDevice(Request(), CancellationToken.None);

    var conflict = Assert.IsAssignableFrom<ObjectResult>(result);
    Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    Assert.Equal(AudioOutputState.Connecting, _castOutput.State);
    _config.Verify(c => c.SetValueAsync(
      It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    // Refused before the connect preparation: the now-playing metadata push (which reads the
    // active source) never ran against an output another party is connecting.
    audioManager.VerifyGet(m => m.ActiveSource, Times.Never);
  }

  [Fact]
  public async Task Connect_WhenConnectAsyncRefusesItsState_Returns409_Not500()
  {
    // Stopping passes the controller's own checks and reaches ConnectAsync, whose state guard
    // throws "Cannot connect in state Stopping" — the same refusal a connect that began after
    // the controller's Connecting check produces.
    SetState(AudioOutputState.Stopping);

    var result = await CreateController().ConnectToCastDevice(Request(), CancellationToken.None);

    var conflict = Assert.IsAssignableFrom<ObjectResult>(result);
    Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    _config.Verify(c => c.SetValueAsync(
      It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  private ChromecastDeviceInfo StreamToTestDevice(string id = DeviceId)
  {
    var device = new ChromecastDeviceInfo
    {
      Id = id, FriendlyName = "Test speaker", IpAddress = "192.168.0.25", Port = 8009, Model = "Nest Audio"
    };
    SetConnectedDevice(device);
    SetState(AudioOutputState.Streaming);
    return device;
  }

  private void VerifyDefaultSaved(Times times) =>
    _config.Verify(c => c.SetValueAsync(
      It.IsAny<string>(), "AudioPreferences:DefaultCastDeviceId", DeviceId, It.IsAny<CancellationToken>()), times);

  /// <summary>
  /// A bare engine (never initialised, so no native device) with mocked Cast and HTTP outputs
  /// attached to its output gate. Both start Ready; the Cast mock reports Streaming once started.
  /// </summary>
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

  private DevicesController CreateController(
    IAudioManager? audioManager = null, SoundFlowAudioEngine? audioEngine = null)
  {
    var prefs = new Mock<IOptionsMonitor<AudioPreferences>>();
    prefs.SetupGet(p => p.CurrentValue).Returns(new AudioPreferences());

    return new DevicesController(
      NullLogger<DevicesController>.Instance,
      new Mock<IAudioDeviceManager>().Object,
      _config.Object,
      prefs.Object,
      Options.Create(new AudioOutputOptions()),
      audioManager: audioManager,
      audioEngine: audioEngine,
      castOutput: _castOutput);
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
