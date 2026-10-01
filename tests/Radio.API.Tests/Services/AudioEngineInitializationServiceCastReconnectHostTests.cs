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
  private readonly FakeVolumeStore _volumes = new(); // empty unless a test remembers a level
  private readonly GoogleCastOutput _castOutput;

  public AudioEngineInitializationServiceCastReconnectHostTests()
  {
    var options = new AudioOutputOptions();
    options.GoogleCast.CacheFilePath = Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");
    _castOutput = new GoogleCastOutput(
      NullLogger<GoogleCastOutput>.Instance, Options.Create(options), volumeStore: _volumes);

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
    // The port the probe tries first is pointed at one just closed (review L2: 8009 may be in
    // use on the test machine, which would make the "closed" device read as reachable).
    var service = CreateService();
    service.CastProbeStandardPort = ClosedLoopbackPort();
    var host = service.CreateProductionCastReconnectHost();
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var device = Device(((IPEndPoint)listener.LocalEndpoint).Port);

    var answered = await host.ProbeAsync(device, CancellationToken.None).WaitAsync(HangGuard);
    listener.Stop();
    var refused = await host.ProbeAsync(device, CancellationToken.None).WaitAsync(HangGuard);

    Assert.Equal(device, answered);
    Assert.Null(refused);
  }

  [Theory]
  [InlineData(new string[0], true)]
  [InlineData(new[] { "CC1AD845" }, true)]        // our own receiver, still up from before the drop
  [InlineData(new[] { "e8c28d3c" }, true)]        // the Backdrop idle screen
  [InlineData(new[] { "2DB7CC49" }, false)]       // another sender's app
  [InlineData(new[] { "CC1AD845", "2DB7CC49" }, false)]
  public void ReceiverFreeForUs_OnlyWhenNothingButOursOrTheIdleScreenRuns(string[] running, bool free)
  {
    Assert.Equal(free, AudioEngineInitializationService.IsCastReceiverFreeForUs(running, "CC1AD845"));
  }

  [Fact]
  public async Task ReceiverRunningAnotherApp_StandsDownWithoutLaunching_AndRemovesOurConnection()
  {
    // Review M5: launching our receiver would end the other sender's session.
    using var listener = StartListener();
    var service = CreateService();
    service.ReceiverApplicationsReadOverride = (_, _) => Task.FromResult<IReadOnlyList<string>>(new[] { "2DB7CC49" });
    var host = service.CreateProductionCastReconnectHost();
    var states = new List<AudioOutputState>();
    _castOutput.StateChanged += (_, e) => states.Add(e.NewState);

    var ex = await Assert.ThrowsAsync<CastSpeakerInUseException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), CancellationToken.None).WaitAsync(HangGuard));

    Assert.Contains("2DB7CC49", ex.Message);
    Assert.Null(_castOutput.ConnectedDevice);                          // our channel closed
    Assert.NotEqual(AudioOutputState.Streaming, _castOutput.State);    // nothing launched

    // Never Streaming at any point, so no media STOP can have been sent: GoogleCastOutput.StopAsync
    // is a no-op unless the output is Streaming (AudioOutputBase.ValidateCanStop), and nothing was
    // launched to stop. Whether DisconnectAsync itself sends anything is SharpCaster's behaviour
    // (it does not, in 3.0.0), which no offline test can observe.
    Assert.DoesNotContain(AudioOutputState.Streaming, states);
  }

  [Fact]
  public async Task ABusySpeaker_GetsNoVolumeCommand_AndItsStatusDoesNotReachTheConsole()
  {
    // Review M5. The reconnect connects before it reads the receiver's applications. Before the
    // fix that connect pushed the remembered (AUD-80) volume to the speaker — jumping another
    // sender's volume — and any status the speaker sent while our channel was open was reported
    // as an external change, which AudioStateUpdateService writes into master volume and mute.
    using var listener = StartListener();
    _volumes.Volumes["cast-a"] = 0.25f;
    var pushes = new List<float>();
    SetCastSeam("CastSetVolumeOverrideForTests", (Func<float, Task>)(v => { pushes.Add(v); return Task.CompletedTask; }));
    SetCastSeam("CastStatusReadOverrideForTests",
      (Func<Task<(float Volume, bool Muted)?>>)(() => Task.FromResult<(float Volume, bool Muted)?>((0.70f, false))));
    var published = new List<CastVolumeChangedEventArgs>();
    _castOutput.CastVolumeChanged += (_, e) => published.Add(e);

    var service = CreateService();
    var heldDuringTheRead = false;
    service.ReceiverApplicationsReadOverride = (cast, _) =>
    {
      // The pre-confirmation window: the other sender changes the volume and mutes.
      heldDuringTheRead = cast.IsHoldingForReceiverConfirmation;
      RaiseReceiverStatus(0.40, muted: true);
      return Task.FromResult<IReadOnlyList<string>>(new[] { "2DB7CC49" });
    };
    var host = service.CreateProductionCastReconnectHost();

    await Assert.ThrowsAsync<CastSpeakerInUseException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), CancellationToken.None).WaitAsync(HangGuard));

    Assert.True(heldDuringTheRead);
    Assert.Empty(pushes);
    Assert.DoesNotContain(published, e => !e.IsInitialSync);
    Assert.Equal(0.25f, _volumes.Volumes["cast-a"], 3);
  }

  [Fact]
  public async Task AFreeSpeaker_EndsTheHoldBeforeOurAppIsStarted()
  {
    // Review M5, the other half: once the receiver is known to be free the hold ends, so the
    // start's after-launch push (SyncVolumeAfterStartAsync, which applies the held remembered
    // level — GoogleCastOutputVolumeMemoryTests covers that push) and later status reports run
    // as for any connection.
    //
    // No offline receiver can be launched: StartAsync's LaunchApplicationAsync on the unconnected
    // SharpCaster client waits out its 30 s response timeout. So the read override also clears
    // GoogleCastOutput._client (private, hence reflection), which StartAsync treats as "nothing to
    // launch" and goes straight to Streaming. The hold, the confirmation and the host's sequence
    // are real; the launch and the after-launch push are not exercised here.
    using var listener = StartListener();
    _volumes.Volumes["cast-a"] = 0.25f;
    var pushes = new List<float>();
    SetCastSeam("CastSetVolumeOverrideForTests", (Func<float, Task>)(v => { pushes.Add(v); return Task.CompletedTask; }));
    var service = CreateService();
    var heldDuringTheRead = false;
    service.ReceiverApplicationsReadOverride = (cast, _) =>
    {
      heldDuringTheRead = cast.IsHoldingForReceiverConfirmation;
      typeof(GoogleCastOutput)
        .GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!
        .SetValue(cast, null);
      return Task.FromResult<IReadOnlyList<string>>(new[] { "E8C28D3C" }); // the idle screen: free
    };
    var host = service.CreateProductionCastReconnectHost();

    await host.ConnectAndStartAsync(Device(Port(listener)), CancellationToken.None).WaitAsync(HangGuard);

    Assert.Equal(AudioOutputState.Streaming, _castOutput.State);
    Assert.True(heldDuringTheRead);
    Assert.False(_castOutput.IsHoldingForReceiverConfirmation);
    Assert.Empty(pushes); // nothing pushed at connect time
  }

  [Fact]
  public async Task ReceiverStatusUnreadable_IsTreatedAsBusy()
  {
    using var listener = StartListener();
    var service = CreateService();
    service.ReceiverApplicationsReadOverride = (_, _) =>
      Task.FromException<IReadOnlyList<string>>(new TimeoutException("no RECEIVER_STATUS"));
    var host = service.CreateProductionCastReconnectHost();

    await Assert.ThrowsAsync<CastSpeakerInUseException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), CancellationToken.None).WaitAsync(HangGuard));

    Assert.Null(_castOutput.ConnectedDevice);
    Assert.NotEqual(AudioOutputState.Streaming, _castOutput.State);
  }

  [Fact]
  public async Task AConnectionSomeoneElseMadeAfterOurs_IsNotTornDownByTheReconnect()
  {
    // Review M1: the watcher tears down only what its own connect published. Here a user's
    // connect replaces ours while the reconnect is checking the receiver; the reconnect then
    // stands down and must leave the user's connection standing.
    using var listener = StartListener();
    _engine.AttachOutputCoordination(_castOutput, null, null); // so a tear-down would reach it
    var service = CreateService();
    var users = Device(Port(listener)) with { FriendlyName = "Office speaker (user)" };
    service.ReceiverApplicationsReadOverride = async (cast, ct) =>
    {
      await cast.ConnectAsync(users, ct);
      return new[] { "2DB7CC49" };
    };
    var host = service.CreateProductionCastReconnectHost();

    await Assert.ThrowsAsync<CastSpeakerInUseException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), CancellationToken.None).WaitAsync(HangGuard));
    await host.TearDownCastAsync().WaitAsync(HangGuard); // and a later tear-down by the watcher

    Assert.Same(users, _castOutput.ConnectedDevice);
  }

  [Fact]
  public async Task TearDown_WithNothingOfOursPublished_LeavesAUsersConnectionAlone()
  {
    using var listener = StartListener();
    _engine.AttachOutputCoordination(_castOutput, null, null); // so a tear-down would reach it
    var host = CreateService().CreateProductionCastReconnectHost();
    await _castOutput.InitializeAsync();
    var users = Device(Port(listener));
    await _castOutput.ConnectAsync(users).WaitAsync(HangGuard);

    await host.TearDownCastAsync().WaitAsync(HangGuard);

    Assert.Same(users, _castOutput.ConnectedDevice);
  }

  [Fact]
  public async Task OurOwnConnection_IsTornDown()
  {
    // The control for the two tests above: the same stand-down with nobody else connecting.
    using var listener = StartListener();
    var service = CreateService();
    ChromecastDeviceInfo? connected = null;
    service.ReceiverApplicationsReadOverride = (cast, _) =>
    {
      connected = cast.ConnectedDevice;
      return Task.FromResult<IReadOnlyList<string>>(new[] { "2DB7CC49" });
    };
    var host = service.CreateProductionCastReconnectHost();

    await Assert.ThrowsAsync<CastSpeakerInUseException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), CancellationToken.None).WaitAsync(HangGuard));

    Assert.NotNull(connected);                // ours was published before the check…
    Assert.Null(_castOutput.ConnectedDevice); // …and removed after it
  }

  [Fact]
  public async Task ASwitchToCastThatThrowsAfterMutingLocal_IsUndoneByRestoringTheLocalOutput()
  {
    // Review M2: the gate mutes local, then activates the HTTP and Cast outputs. A throw there
    // leaves the active output local — and muted.
    var http = new Mock<IAudioOutput>();
    http.SetupGet(h => h.State).Returns(AudioOutputState.Created);
    http.Setup(h => h.InitializeAsync(It.IsAny<CancellationToken>()))
      .ThrowsAsync(new InvalidOperationException("port in use"));
    _engine.AttachOutputCoordination(null, http.Object, null);
    await _engine.SetActiveOutputAsync("speakers");
    var mark = new CastRecoveryMark("speakers", _engine.OutputSelectionEpoch);
    var host = CreateService().CreateProductionCastReconnectHost();

    await Assert.ThrowsAsync<InvalidOperationException>(() => host.TrySwitchToCastAsync(mark, CancellationToken.None));
    Assert.Equal("speakers", _engine.ActiveOutputId);
    Assert.True(_engine.IsLocalOutputMuted); // the state the fix exists for

    await host.RestoreLocalOutputAsync(mark).WaitAsync(HangGuard);

    Assert.Equal("speakers", _engine.ActiveOutputId);
    Assert.False(_engine.IsLocalOutputMuted);
  }

  [Fact]
  public async Task RestoreLocal_LeavesACastOutputAlone()
  {
    await _engine.SetActiveOutputAsync("google-cast");
    var host = CreateService().CreateProductionCastReconnectHost();

    await host.RestoreLocalOutputAsync(new CastRecoveryMark("speakers", 0)).WaitAsync(HangGuard);

    Assert.Equal("google-cast", _engine.ActiveOutputId);
    Assert.True(_engine.IsLocalOutputMuted);
  }

  // --- helpers ---

  /// <summary>
  /// A loopback listener for GoogleCastOutput's TCP reachability check, with the Cast transport
  /// and status read substituted through its labelled kind-C seams (internal to
  /// Radio.Infrastructure, hence reflection): no fake socket can complete a Cast handshake.
  /// </summary>
  private TcpListener StartListener()
  {
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    typeof(GoogleCastOutput)
      .GetProperty("ConnectTransportOverrideForTests", BindingFlags.NonPublic | BindingFlags.Instance)!
      .SetValue(_castOutput, (Func<Sharpcaster.Models.ChromecastReceiver, Task>)(_ => Task.CompletedTask));
    typeof(GoogleCastOutput)
      .GetProperty("CastStatusReadOverrideForTests", BindingFlags.NonPublic | BindingFlags.Instance)!
      .SetValue(_castOutput, (Func<Task<(float Volume, bool Muted)?>>)(() => Task.FromResult<(float Volume, bool Muted)?>(null)));
    return listener;
  }

  private static int Port(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

  /// <summary>Sets one of GoogleCastOutput's labelled kind-C seams (internal, hence reflection).</summary>
  private void SetCastSeam(string name, object value) =>
    typeof(GoogleCastOutput)
      .GetProperty(name, BindingFlags.NonPublic | BindingFlags.Instance)!
      .SetValue(_castOutput, value);

  /// <summary>Raises a receiver status the way SharpCaster does (private handler, hence reflection).</summary>
  private void RaiseReceiverStatus(double level, bool muted)
  {
    var handler = typeof(GoogleCastOutput).GetMethod(
      "OnReceiverStatusChanged", BindingFlags.NonPublic | BindingFlags.Instance)!;
    var status = new Sharpcaster.Models.ChromecastStatus.ChromecastStatus
    {
      Volume = new() { Level = level, Muted = muted }
    };
    handler.Invoke(_castOutput, new object?[] { null, status });
  }

  private sealed class FakeVolumeStore : ICastDeviceVolumeStore
  {
    public Dictionary<string, float> Volumes { get; } = new();

    public Task<float?> GetVolumeAsync(string deviceId, CancellationToken cancellationToken = default) =>
      Task.FromResult<float?>(Volumes.TryGetValue(deviceId, out var v) ? v : null);

    public void Remember(string deviceId, float volume) => Volumes[deviceId] = volume;
  }

  private static int ClosedLoopbackPort()
  {
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    return port;
  }

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
    public Task RestoreLocalOutputAsync(CastRecoveryMark mark) => Task.CompletedTask;
  }
}
