using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Services;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;

namespace Radio.API.Tests.Services;

/// <summary>
/// AUD-84 / AUD-37: when the Cast connection is LOST mid-stream, the console switches the active
/// output back to the local speakers (unmuting them through the output gate). A deliberate
/// disconnect, or an output the user has already moved away from Cast, is left alone.
/// </summary>
/// <remarks>
/// The recovery is awaited through <c>LastCastLossRecovery</c>, not a sleep (CLAUDE.md § Test
/// Timing). The event is raised on a real <see cref="GoogleCastOutput"/> by invoking its event
/// delegate — its <c>ReportConnectionLost</c> is internal to Radio.Infrastructure, and the
/// teardown that precedes the event is covered by <c>GoogleCastOutputConnectionLossTests</c>.
/// </remarks>
public class AudioEngineInitializationServiceCastLossTests
{
  private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(15);

  private readonly Mock<IAudioEngine> _engine = new();
  private readonly Mock<IAudioDeviceManager> _deviceManager = new();
  private readonly GoogleCastOutput _castOutput;

  public AudioEngineInitializationServiceCastLossTests()
  {
    var options = new AudioOutputOptions();
    options.GoogleCast.CacheFilePath = Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");
    _castOutput = new GoogleCastOutput(NullLogger<GoogleCastOutput>.Instance, Options.Create(options));

    _deviceManager
      .Setup(d => d.GetOutputDevicesAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(new List<AudioDeviceInfo>
      {
        new() { Id = "hdmi", Name = "HDMI", Type = AudioDeviceType.Output, IsDefault = false },
        new() { Id = "speakers", Name = "Built-in Audio", Type = AudioDeviceType.Output, IsDefault = true }
      });
  }

  [Fact]
  public async Task LostCastConnection_WhileCastIsActive_SwitchesToTheDefaultLocalOutput()
  {
    _engine.SetupGet(e => e.ActiveOutputId).Returns("google-cast");
    var service = CreateService();

    RaiseCastDisconnected(new ChromecastDisconnectedEventArgs
    {
      Device = Device(),
      Reason = "a SharpCaster background send failed",
      IsConnectionLost = true
    });
    await service.LastCastLossRecovery.WaitAsync(HangGuard);

    _engine.Verify(e => e.SetActiveOutputAsync("speakers", It.IsAny<CancellationToken>()), Times.Once);
    _deviceManager.Verify(d => d.SetOutputDeviceAsync("speakers", It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task LostCastConnection_AfterTheUserAlreadyPickedAnotherOutput_LeavesItAlone()
  {
    _engine.SetupGet(e => e.ActiveOutputId).Returns("hdmi");
    var service = CreateService();

    RaiseCastDisconnected(new ChromecastDisconnectedEventArgs { Device = Device(), IsConnectionLost = true });
    await service.LastCastLossRecovery.WaitAsync(HangGuard);

    _engine.Verify(e => e.SetActiveOutputAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task DeliberateCastDisconnect_DoesNotTriggerTheRecovery()
  {
    // A user-requested disconnect already went through the gate, which chose the next output.
    _engine.SetupGet(e => e.ActiveOutputId).Returns("google-cast");
    var service = CreateService();

    RaiseCastDisconnected(new ChromecastDisconnectedEventArgs
    {
      Device = Device(),
      Reason = "User requested disconnect",
      IsConnectionLost = false
    });
    await service.LastCastLossRecovery.WaitAsync(HangGuard);

    _engine.Verify(e => e.SetActiveOutputAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  // --- helpers ---

  private AudioEngineInitializationService CreateService()
  {
    var provider = new Mock<IServiceProvider>();
    provider.Setup(p => p.GetService(typeof(GoogleCastOutput))).Returns(_castOutput);

    var preferences = new Mock<IOptionsMonitor<AudioPreferences>>();
    preferences.SetupGet(p => p.CurrentValue).Returns(new AudioPreferences());

    return new AudioEngineInitializationService(
      new Mock<ILogger<AudioEngineInitializationService>>().Object,
      _engine.Object,
      _deviceManager.Object,
      preferences.Object,
      new Mock<IMasterMixer>().Object,
      Options.Create(new BluetoothOptions { Enabled = false, EnableOnStartup = false }),
      // AUD-37's reconnect watcher is off here: these tests are about the fallback itself, and a
      // watcher left running would probe the device address on the real clock after the test.
      // The watcher is covered by AudioEngineInitializationServiceCastReconnectTests.
      Options.Create(new AudioOutputOptions { GoogleCast = { AutoReconnect = false } }),
      provider.Object);
  }

  private void RaiseCastDisconnected(ChromecastDisconnectedEventArgs args)
  {
    // The backing field of a field-like event has the event's name.
    var field = typeof(GoogleCastOutput).GetField("Disconnected", BindingFlags.NonPublic | BindingFlags.Instance);
    Assert.NotNull(field);
    var handler = field!.GetValue(_castOutput) as EventHandler<ChromecastDisconnectedEventArgs>;
    Assert.NotNull(handler); // the service must have subscribed
    handler!.Invoke(_castOutput, args);
  }

  private static ChromecastDeviceInfo Device() => new()
  {
    Id = "cast-a",
    FriendlyName = "Office speaker",
    IpAddress = "192.0.2.10",
    Port = 8009,
    Model = "Google Home Mini"
  };
}
