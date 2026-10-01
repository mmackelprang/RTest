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
using IRadioConfigurationManager = Radio.Configuration.Abstractions.IConfigurationManager;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// AUD-85: <c>POST /api/devices/cast/connect</c> treats a connect for the device already
/// streaming as success, and answers 409 — not 500 — when the Cast output is busy connecting.
/// </summary>
/// <remarks>
/// Uses a real <see cref="GoogleCastOutput"/> placed into the state under test by reflection on
/// its non-public setters (<c>State</c>, <c>ConnectedDevice</c>), the same approach
/// <c>AudioEngineInitializationServiceCastLossTests</c> takes for its event: Radio.API.Tests has no
/// access to Radio.Infrastructure internals, and no production seam was added for this.
/// The audio engine is not supplied, so the gate promotion (<c>SetActiveOutputAsync</c>) is not
/// exercised here; the default-device save is, through the configuration manager mock.
/// </remarks>
public class DevicesControllerCastConnectTests
{
  private const string DeviceId = "https://192.168.0.25/";

  private readonly Mock<IRadioConfigurationManager> _config = new();
  private readonly GoogleCastOutput _castOutput;

  public DevicesControllerCastConnectTests()
  {
    var options = new AudioOutputOptions();
    options.GoogleCast.CacheFilePath = Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");
    _castOutput = new GoogleCastOutput(NullLogger<GoogleCastOutput>.Instance, Options.Create(options));

    _config
      .Setup(c => c.SetValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);
  }

  [Fact]
  public async Task Connect_WhenAlreadyStreamingToSameDevice_Returns200_WithoutReconnecting()
  {
    var device = new ChromecastDeviceInfo
    {
      Id = DeviceId, FriendlyName = "Test speaker", IpAddress = "192.168.0.25", Port = 8009, Model = "Nest Audio"
    };
    SetConnectedDevice(device);
    SetState(AudioOutputState.Streaming);

    var result = await CreateController().ConnectToCastDevice(Request(), CancellationToken.None);

    Assert.IsType<OkObjectResult>(result);
    // A reconnect would have stopped the stream (ConnectAsync stops a streaming output first).
    Assert.Equal(AudioOutputState.Streaming, _castOutput.State);
    Assert.Same(device, _castOutput.ConnectedDevice);
    _config.Verify(c => c.SetValueAsync(
      It.IsAny<string>(), "AudioPreferences:DefaultCastDeviceId", DeviceId, It.IsAny<CancellationToken>()), Times.Once);
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

  private DevicesController CreateController(IAudioManager? audioManager = null)
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
