using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Services;
using Radio.Configuration.Abstractions;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.API.Tests.Services;

/// <summary>
/// AUD-37: the production reconnect host against a real <see cref="SoundFlowAudioEngine"/> gate
/// (no native audio — the gate needs none): the recovery hands the watcher the epoch read with
/// its own switch, and any output selection after it — even one that ends on the same local
/// device — makes the switch back to Cast refuse.
/// </summary>
public class AudioEngineInitializationServiceCastReconnectHostTests
{
  private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(15);

  private readonly SoundFlowAudioEngine _engine = CreateBareEngine();
  private readonly Mock<IAudioDeviceManager> _deviceManager = new();
  private readonly GoogleCastOutput _castOutput;

  public AudioEngineInitializationServiceCastReconnectHostTests()
  {
    var options = new AudioOutputOptions();
    options.GoogleCast.CacheFilePath = Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");
    _castOutput = new GoogleCastOutput(NullLogger<GoogleCastOutput>.Instance, Options.Create(options));

    _deviceManager
      .Setup(d => d.GetOutputDevicesAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(new List<AudioDeviceInfo>
      {
        new() { Id = "speakers", Name = "Built-in Audio", Type = AudioDeviceType.Output, IsDefault = true }
      });
  }

  [Fact]
  public async Task Recovery_HandsTheWatcherTheEpochOfItsOwnSwitch()
  {
    await _engine.SetActiveOutputAsync("google-cast");
    var time = new CastReconnectWatcherTests.SignalingTimeProvider();
    var capture = new MarkCapturingHost();
    var service = CreateService();
    service.ReconnectTimeProvider = time;
    service.CastReconnectHostOverride = capture;

    RaiseLoss();
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    time.Advance(await time.NextTimerAsync().WaitAsync(HangGuard));
    await service.CastReconnectTask.WaitAsync(HangGuard);

    Assert.Equal("speakers", _engine.ActiveOutputId);
    Assert.Equal(new CastRecoveryMark("speakers", _engine.OutputSelectionEpoch), capture.Seen);
  }

  [Fact]
  public async Task NoSelectionSinceTheRecovery_SwitchesBackToCast()
  {
    await _engine.SetActiveOutputAsync("speakers");
    var mark = new CastRecoveryMark("speakers", _engine.OutputSelectionEpoch);
    var host = CreateService().CreateProductionCastReconnectHost();

    Assert.True(host.IsStillOnRecoveryOutput(mark));
    Assert.True(await host.TrySwitchToCastAsync(mark, CancellationToken.None));
    Assert.Equal("google-cast", _engine.ActiveOutputId);
  }

  [Fact]
  public async Task UserWentToAnotherOutputAndBack_TheSwitchBackToCastIsRefused()
  {
    await _engine.SetActiveOutputAsync("speakers");
    var mark = new CastRecoveryMark("speakers", _engine.OutputSelectionEpoch);
    var host = CreateService().CreateProductionCastReconnectHost();

    await _engine.SetActiveOutputAsync("hdmi");
    await _engine.SetActiveOutputAsync("speakers");

    Assert.False(host.IsStillOnRecoveryOutput(mark));
    Assert.False(await host.TrySwitchToCastAsync(mark, CancellationToken.None));
    Assert.Equal("speakers", _engine.ActiveOutputId);
  }

  [Fact]
  public void AnIdleCastOutput_IsIdle()
  {
    var host = CreateService().CreateProductionCastReconnectHost();

    Assert.Equal(AudioOutputState.Created, _castOutput.State);
    Assert.True(host.IsCastIdle);
    Assert.False(host.IsCastStreaming);
  }

  [Fact]
  public async Task ACastOutputThatSomeoneElseConnected_IsNotIdle_EvenWhileReady()
  {
    // AUD-85: connected but not yet started (state Ready) is exactly the moment a manual pick is
    // between its ConnectAsync and StartAsync. The state alone reads "idle"; the connected
    // device is what says the output is taken. The transport is substituted through
    // GoogleCastOutput's labelled kind-C seam (internal to Radio.Infrastructure, hence
    // reflection): no fake socket can complete a Cast handshake.
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    await _castOutput.InitializeAsync();
    typeof(GoogleCastOutput)
      .GetProperty("ConnectTransportOverrideForTests", BindingFlags.NonPublic | BindingFlags.Instance)!
      .SetValue(_castOutput, (Func<Sharpcaster.Models.ChromecastReceiver, Task>)(_ => Task.CompletedTask));
    typeof(GoogleCastOutput)
      .GetProperty("CastStatusReadOverrideForTests", BindingFlags.NonPublic | BindingFlags.Instance)!
      .SetValue(_castOutput, (Func<Task<(float Volume, bool Muted)?>>)(() => Task.FromResult<(float Volume, bool Muted)?>(null)));
    await _castOutput.ConnectAsync(Device(((IPEndPoint)listener.LocalEndpoint).Port)).WaitAsync(HangGuard);
    var host = CreateService().CreateProductionCastReconnectHost();

    Assert.Equal(AudioOutputState.Ready, _castOutput.State);
    Assert.NotNull(_castOutput.ConnectedDevice);
    Assert.False(host.IsCastIdle);
  }

  [Fact]
  public async Task Probe_AnsweringPort_ReturnsTheDevice_ClosedPort_ReturnsNull()
  {
    // Loopback only: a listening port answers, a closed one is refused (on Windows a refused
    // loopback connect can take ~2 s to report, inside the probe's 3 s per-port timeout).
    // Assumes nothing on this machine listens on 8009, the port the probe tries first.
    var host = CreateService().CreateProductionCastReconnectHost();
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var device = Device(((IPEndPoint)listener.LocalEndpoint).Port);

    var answered = await host.ProbeAsync(device, CancellationToken.None).WaitAsync(HangGuard);
    listener.Stop();
    var refused = await host.ProbeAsync(device, CancellationToken.None).WaitAsync(HangGuard);

    Assert.Equal(device, answered);
    Assert.Null(refused);
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
      _engine,
      _deviceManager.Object,
      preferences.Object,
      new Mock<IMasterMixer>().Object,
      Options.Create(new BluetoothOptions { Enabled = false, EnableOnStartup = false }),
      Options.Create(new AudioOutputOptions()),
      provider.Object);
  }

  private void RaiseLoss()
  {
    var field = typeof(GoogleCastOutput).GetField("Disconnected", BindingFlags.NonPublic | BindingFlags.Instance);
    var handler = (EventHandler<ChromecastDisconnectedEventArgs>)field!.GetValue(_castOutput)!;
    handler.Invoke(_castOutput, new ChromecastDisconnectedEventArgs
    {
      Device = Device(8009),
      Reason = "test",
      IsConnectionLost = true
    });
  }

  private static ChromecastDeviceInfo Device(int port) => new()
  {
    Id = "cast-a",
    FriendlyName = "Office speaker",
    IpAddress = "127.0.0.1",
    Port = port,
    Model = "Google Home Mini"
  };

  private static SoundFlowAudioEngine CreateBareEngine()
  {
    var options = Options.Create(new AudioEngineOptions
    {
      SampleRate = 48000,
      Channels = 2,
      BufferSize = 1024,
      EnableHotPlugDetection = false
    });

    var preferences = new Mock<IOptionsMonitor<AudioPreferences>>();
    preferences.Setup(x => x.CurrentValue).Returns(new AudioPreferences());
    var outputOptions = new Mock<IOptionsMonitor<AudioOutputOptions>>();
    outputOptions.Setup(x => x.CurrentValue).Returns(new AudioOutputOptions());

    var deviceManager = new SoundFlowDeviceManager(
      NullLogger<SoundFlowDeviceManager>.Instance,
      new Mock<IConfigurationManager>().Object,
      preferences.Object,
      outputOptions.Object);

    return new SoundFlowAudioEngine(
      NullLogger<SoundFlowAudioEngine>.Instance,
      options,
      new SoundFlowMasterMixer(NullLogger<SoundFlowMasterMixer>.Instance),
      deviceManager);
  }

  /// <summary>Records the mark the watcher checks, then stands the watcher down.</summary>
  private sealed class MarkCapturingHost : ICastReconnectHost
  {
    public CastRecoveryMark? Seen;

    public bool IsStillOnRecoveryOutput(CastRecoveryMark mark)
    {
      Seen = mark;
      return false;
    }

    public bool IsCastIdle => true;
    public bool IsCastStreaming => false;
    public string? ActiveOutputId => null;
    public Task<ChromecastDeviceInfo?> ProbeAsync(ChromecastDeviceInfo device, CancellationToken ct) =>
      Task.FromResult<ChromecastDeviceInfo?>(null);
    public Task ConnectAndStartAsync(ChromecastDeviceInfo device, CancellationToken ct) => Task.CompletedTask;
    public Task<bool> TrySwitchToCastAsync(CastRecoveryMark mark, CancellationToken ct) => Task.FromResult(false);
    public Task TearDownCastAsync() => Task.CompletedTask;
  }
}
