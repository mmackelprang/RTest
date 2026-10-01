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

  private readonly CastReconnectWatcherTests.ListLogger<SoundFlowAudioEngine> _engineLog = new();
  private readonly CastReconnectWatcherTests.ListLogger<GoogleCastOutput> _castLog = new();
  private readonly SoundFlowAudioEngine _engine;
  private readonly Mock<IAudioDeviceManager> _deviceManager = new();
  private readonly FakeVolumeStore _volumes = new(); // empty unless a test remembers a level
  private readonly GoogleCastOutput _castOutput;

  public AudioEngineInitializationServiceCastReconnectHostTests()
  {
    _engine = CreateBareEngine(_engineLog);
    var options = new AudioOutputOptions();
    options.GoogleCast.CacheFilePath = Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");
    _castOutput = new GoogleCastOutput(_castLog, Options.Create(options), volumeStore: _volumes);

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
    // launched to stop. DisconnectAsync sends Cast messages only to release a console mute (AUD-81:
    // app STOP, then SET_MUTE false, possibly over a fresh connection), which it skips for a
    // connection still held for receiver confirmation, as this one is (and a held connection
    // carries no console-mute mark to release; CastConsoleVolumeFollowerTests'
    // AStandDownFromABusyReceiver_SendsNothing… pins that nothing is sent). Beyond that, whether
    // closing sends anything is SharpCaster's behaviour
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
  public async Task AWatcherStartOvertakenByAUserTeardown_LeavesNoStreamingOutput_AndReportsSuperseded()
  {
    // AUD-37 (final review M-1). The host's start runs uninterruptibly (CancellationToken.None), so
    // a user's teardown that runs once CancelCastReconnectAsync's 3 s bound has passed can overtake
    // a slow launch. Before GoogleCastOutput.StartAsync's generation guard, the start went on to
    // Streaming on the torn-down connection and the host — no longer the owner — left it there.
    // The launch is held at its seam while the teardown runs to completion: a rendezvous, not a race.
    using var listener = StartListener();
    var launchEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var launchGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    SetCastSeam("CastLaunchApplicationOverrideForTests",
      (Func<Task<Sharpcaster.Models.ChromecastStatus.ChromecastStatus?>>)(async () =>
      {
        launchEntered.TrySetResult();
        await launchGate.Task;
        return new Sharpcaster.Models.ChromecastStatus.ChromecastStatus();
      }));
    var service = CreateService();
    service.ReceiverApplicationsReadOverride = (_, _) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    var host = service.CreateProductionCastReconnectHost();

    var attempt = host.ConnectAndStartAsync(Device(Port(listener)), CancellationToken.None);
    await launchEntered.Task.WaitAsync(HangGuard);

    await _castOutput.DisconnectAsync().WaitAsync(HangGuard); // the user's teardown
    launchGate.SetResult();

    // Superseded, not failed: the watcher ends CastBusy on this, not with its "retrying" Warning.
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt.WaitAsync(HangGuard));
    Assert.Equal(AudioOutputState.Ready, _castOutput.State);
    Assert.False(_castOutput.IsEnabled);
    Assert.Null(_castOutput.ConnectedDevice);
  }

  [Fact]
  public async Task AWatcherAttemptWhoseConnectFails_LogsNothingAtWarningOrAbove()
  {
    // Review M3: the watcher logs the episode's one Warning itself; the output's own line for an
    // automatic attempt is Debug, or every retry would reach journald.
    using var listener = StartListener();
    SetCastSeam("ConnectTransportOverrideForTests",
      (Func<Sharpcaster.Models.ChromecastReceiver, Task>)(_ => Task.FromException(new IOException("handshake failed"))));
    var host = CreateService().CreateProductionCastReconnectHost();

    await Assert.ThrowsAsync<IOException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), CancellationToken.None).WaitAsync(HangGuard));

    Assert.Contains(_castLog.Entries, e => e.Level == LogLevel.Debug && e.Message.StartsWith("Failed to connect to Chromecast"));
    Assert.DoesNotContain(_castLog.Entries, e => e.Level >= LogLevel.Warning);
  }

  [Fact]
  public async Task AWatcherAttemptWhoseStartAndTearDownFail_LogsNothingAtWarningOrAbove()
  {
    // Review M3. No offline receiver can be launched or refuse a disconnect, so both steps are
    // failed by making the logging call that opens each one throw (ListLogger.ThrowWhen): the
    // failure then lands in that method's own catch, which is the line under test.
    using var listener = StartListener();
    _engine.AttachOutputCoordination(_castOutput, null, null); // the tear-down goes through the engine
    var service = CreateService();
    service.ReceiverApplicationsReadOverride = (_, _) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    _castLog.ThrowWhen = m => m.StartsWith("Starting Google Cast output") || m.StartsWith("Disconnecting from Chromecast");
    var host = service.CreateProductionCastReconnectHost();

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), CancellationToken.None).WaitAsync(HangGuard));

    // The three lines ran, at Debug…
    Assert.Contains(_castLog.Entries, e => e.Level == LogLevel.Debug && e.Message.StartsWith("Failed to start Google Cast output"));
    Assert.Contains(_castLog.Entries, e => e.Level == LogLevel.Debug && e.Message.StartsWith("Error disconnecting from Chromecast"));
    Assert.Contains(_engineLog.Entries, e => e.Level == LogLevel.Debug && e.Message.StartsWith("Graceful Cast tear-down failed"));
    // …and nothing from the output or the engine reached Warning.
    Assert.DoesNotContain(_castLog.Entries, e => e.Level >= LogLevel.Warning);
    Assert.DoesNotContain(_engineLog.Entries, e => e.Level >= LogLevel.Warning);
  }

  [Fact]
  public async Task AUsersFailedConnectStartAndTearDown_StillLogErrorAndWarning()
  {
    // The control for the two tests above: the same failures, not automatic, keep their levels.
    using var listener = StartListener();
    _engine.AttachOutputCoordination(_castOutput, null, null);
    await _castOutput.InitializeAsync();

    SetCastSeam("ConnectTransportOverrideForTests",
      (Func<Sharpcaster.Models.ChromecastReceiver, Task>)(_ => Task.FromException(new IOException("handshake failed"))));
    await Assert.ThrowsAsync<IOException>(() => _castOutput.ConnectAsync(Device(Port(listener))).WaitAsync(HangGuard));
    Assert.Contains(_castLog.Entries, e => e.Level == LogLevel.Error && e.Message.StartsWith("Failed to connect to Chromecast"));

    SetCastSeam("ConnectTransportOverrideForTests", (Func<Sharpcaster.Models.ChromecastReceiver, Task>)(_ => Task.CompletedTask));
    await _castOutput.ConnectAsync(Device(Port(listener))).WaitAsync(HangGuard);
    _castLog.ThrowWhen = m => m.StartsWith("Starting Google Cast output") || m.StartsWith("Disconnecting from Chromecast");
    await Assert.ThrowsAsync<InvalidOperationException>(() => _castOutput.StartAsync().WaitAsync(HangGuard));
    await _engine.TearDownCastOutputAsync(CancellationToken.None).WaitAsync(HangGuard);

    Assert.Contains(_castLog.Entries, e => e.Level == LogLevel.Error && e.Message.StartsWith("Failed to start Google Cast output"));
    Assert.Contains(_castLog.Entries, e => e.Level == LogLevel.Error && e.Message.StartsWith("Error disconnecting from Chromecast"));
    Assert.Contains(_engineLog.Entries, e => e.Level == LogLevel.Warning && e.Message.StartsWith("Graceful Cast tear-down failed"));
  }

  [Fact]
  public async Task ReceiverStatusUnreadable_SendsNothing_AndIsAFailedAttemptNotAnInUseSpeaker()
  {
    // Review M4. Unknown is never launched on (no commands, our channel closed), but it is a failed
    // attempt the watcher retries — a booting speaker whose GET_STATUS times out is the common
    // cause — not CastSpeakerInUseException, which ends the episode for good.
    using var listener = StartListener();
    _volumes.Volumes["cast-a"] = 0.25f;
    var pushes = new List<float>();
    SetCastSeam("CastSetVolumeOverrideForTests", (Func<float, Task>)(v => { pushes.Add(v); return Task.CompletedTask; }));
    var service = CreateService();
    service.ReceiverApplicationsReadOverride = (_, _) =>
      Task.FromException<IReadOnlyList<string>>(new TimeoutException("no RECEIVER_STATUS"));
    var host = service.CreateProductionCastReconnectHost();

    var ex = await Assert.ThrowsAnyAsync<Exception>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), CancellationToken.None).WaitAsync(HangGuard));

    Assert.IsNotType<CastSpeakerInUseException>(ex);
    Assert.IsNotAssignableFrom<OperationCanceledException>(ex);
    Assert.Contains("could not be read", ex.Message);
    Assert.Null(_castOutput.ConnectedDevice);
    Assert.NotEqual(AudioOutputState.Streaming, _castOutput.State);
    Assert.Empty(pushes);
  }

  [Fact]
  public async Task ReceiverStatusUnreadable_TheWatcherKeepsTryingAndReconnectsWhenItIsReadable()
  {
    // Review M4, end to end through the real watcher and host: the first attempt's read times out,
    // the second finds the receiver free — the episode continues to a reconnect rather than ending
    // "in use by another app". One Warning for the failed attempt, as for any failed attempt.
    using var listener = StartListener();
    await _engine.SetActiveOutputAsync("speakers");
    var mark = new CastRecoveryMark("speakers", _engine.OutputSelectionEpoch);
    var service = CreateService();
    service.CastProbeStandardPort = Port(listener);
    var reads = 0;
    service.ReceiverApplicationsReadOverride = (cast, _) =>
    {
      if (++reads == 1)
      {
        return Task.FromException<IReadOnlyList<string>>(new TimeoutException("no RECEIVER_STATUS"));
      }

      typeof(GoogleCastOutput).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cast, null);
      return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    };
    var time = new CastReconnectWatcherTests.SignalingTimeProvider();
    var log = new CastReconnectWatcherTests.ListLogger();
    var watcher = new CastReconnectWatcher(
      service.CreateProductionCastReconnectHost(), Device(Port(listener)), mark,
      new CastReconnectSchedule(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(30)),
      time, log);

    var run = Task.Run(() => watcher.RunAsync(CancellationToken.None));
    for (var waits = 0; !run.IsCompleted; waits++)
    {
      Assert.True(waits < 20, "the watcher never stopped");
      var next = time.NextTimerAsync();
      await Task.WhenAny(next, run).WaitAsync(HangGuard);
      if (!run.IsCompleted)
      {
        time.Advance(await next);
      }
    }

    Assert.Equal(CastReconnectOutcome.Reconnected, await run);
    Assert.Equal(2, reads);
    Assert.Equal("google-cast", _engine.ActiveOutputId);
    Assert.Equal(AudioOutputState.Streaming, _castOutput.State);
    var warning = Assert.Single(log.Entries, e => e.Level >= LogLevel.Warning);
    Assert.Contains("could not be read", warning.Message);
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

  [Theory]
  [InlineData(false)] // a disconnect alone
  [InlineData(true)]  // the user's Stop Casting past the cancel bound: cancel, then disconnect
  public async Task AConnectSupersededWhileOnTheNetwork_EndsCancelled_AndNeverReadsTheReceiver(bool cancelled)
  {
    // Review M2. A superseded ConnectAsync returns normally — State Ready, nothing published. Before
    // the fix the host went on to read the published connection's apps (none: the read threw and
    // the episode ended "in use by another app") and could confirm and start whatever was published.
    using var listener = StartListener();
    using var cts = new CancellationTokenSource();
    SetCastSeam("ConnectTransportOverrideForTests", (Func<Sharpcaster.Models.ChromecastReceiver, Task>)(async _ =>
    {
      if (cancelled)
      {
        await cts.CancelAsync();
      }

      await _castOutput.DisconnectAsync(); // bumps the generation under our connect
    }));
    var service = CreateService();
    var reads = 0;
    service.ReceiverApplicationsReadOverride = (_, _) =>
    {
      reads++;
      return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    };
    var host = service.CreateProductionCastReconnectHost();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), cts.Token).WaitAsync(HangGuard));

    Assert.Equal(0, reads);
    Assert.Null(_castOutput.ConnectedDevice);
    Assert.NotEqual(AudioOutputState.Streaming, _castOutput.State);
  }

  [Fact]
  public async Task AUsersConnectionPublishedDuringTheReceiverRead_IsNeitherConfirmedNorStarted()
  {
    // Review M2: by the time the read returns "free", the published connection is a user's. The
    // reconnect must not launch on it — the user's own pick does that, with its own state machine.
    using var listener = StartListener();
    var service = CreateService();
    var users = Device(Port(listener)) with { FriendlyName = "Office speaker (user)" };
    service.ReceiverApplicationsReadOverride = async (cast, ct) =>
    {
      await cast.ConnectAsync(users, ct);
      // Lets a start, if one were made, reach Streaming offline (see AFreeSpeaker_EndsTheHold…).
      typeof(GoogleCastOutput).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cast, null);
      return new[] { "E8C28D3C" }; // free
    };
    var host = service.CreateProductionCastReconnectHost();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), CancellationToken.None).WaitAsync(HangGuard));

    Assert.Same(users, _castOutput.ConnectedDevice);
    Assert.Equal(AudioOutputState.Ready, _castOutput.State); // not started by the reconnect
  }

  [Fact]
  public async Task CancelledByACastPickDuringTheReceiverRead_TheConnectionIsKeptAndStarted()
  {
    // Review M3: a Cast pick that lost the cancel-bound race. The host leaves its (held, so
    // untouched) connection standing; the watcher asks it to keep that for the Cast choice.
    using var listener = StartListener();
    await _engine.SetActiveOutputAsync("speakers");
    using var cts = new CancellationTokenSource();
    var service = CreateService();
    service.ReceiverApplicationsReadOverride = async (cast, ct) =>
    {
      typeof(GoogleCastOutput).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cast, null);
      await cts.CancelAsync();
      ct.ThrowIfCancellationRequested();
      return Array.Empty<string>();
    };
    var host = service.CreateProductionCastReconnectHost();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), cts.Token).WaitAsync(HangGuard));
    Assert.NotNull(_castOutput.ConnectedDevice);              // left standing…
    Assert.True(_castOutput.IsHoldingForReceiverConfirmation); // …and still untouched

    await _engine.SetActiveOutputAsync("google-cast"); // the user's pick
    Assert.True(await host.TryKeepForCastChoiceAsync(castPickPending: false).WaitAsync(HangGuard));

    Assert.Equal(AudioOutputState.Streaming, _castOutput.State);
    Assert.False(_castOutput.IsHoldingForReceiverConfirmation);
  }

  [Fact]
  public async Task CancelledByALocalPickDuringTheReceiverRead_TheConnectionIsNotKept_AndIsTornDown()
  {
    // Review M3's control.
    using var listener = StartListener();
    await _engine.SetActiveOutputAsync("speakers");
    _engine.AttachOutputCoordination(_castOutput, null, null); // so the tear-down reaches it
    using var cts = new CancellationTokenSource();
    var service = CreateService();
    service.ReceiverApplicationsReadOverride = async (cast, ct) =>
    {
      // As in the Cast-pick test, so a start, if one were made, would reach Streaming offline.
      typeof(GoogleCastOutput).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cast, null);
      await cts.CancelAsync();
      ct.ThrowIfCancellationRequested();
      return Array.Empty<string>();
    };
    var host = service.CreateProductionCastReconnectHost();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), cts.Token).WaitAsync(HangGuard));

    Assert.False(await host.TryKeepForCastChoiceAsync(castPickPending: false).WaitAsync(HangGuard));
    Assert.NotEqual(AudioOutputState.Streaming, _castOutput.State);
    await host.TearDownCastAsync().WaitAsync(HangGuard);
    Assert.Null(_castOutput.ConnectedDevice);
  }

  [Fact]
  public async Task TheConditionalTearDown_WaitsForAPromotionOfCastInTheGate_AndThenDeclines()
  {
    // AUD-85 review MEDIUM-2. A promotion of Cast is inside the output gate — local already muted,
    // the HTTP output activating, _activeOutputId not yet assigned — when the cancelled watcher
    // tears down. A read of ActiveOutputId at that moment says "speakers", and a tear-down acting
    // on it removes the connection the promotion is about to make active: Cast active, local
    // muted, nothing connected. The conditional tear-down must instead wait for the gate and then
    // decline. The promotion is parked at the HTTP activation (a rendezvous, not a race).
    using var listener = StartListener();
    await _engine.SetActiveOutputAsync("speakers");
    var engineCast = new Mock<IAudioOutput>();
    engineCast.SetupGet(c => c.State).Returns(AudioOutputState.Streaming); // a tear-down would StopAsync it
    var httpEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var httpGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var http = new Mock<IAudioOutput>();
    http.SetupGet(h => h.State).Returns(AudioOutputState.Ready);
    http.Setup(h => h.StartAsync(It.IsAny<CancellationToken>())).Returns(async () =>
    {
      httpEntered.TrySetResult();
      await httpGate.Task;
    });
    _engine.AttachOutputCoordination(engineCast.Object, http.Object, null);

    // Our connection, left standing by a cancelled connect (as in CancelledByACastPick… above).
    using var cts = new CancellationTokenSource();
    var service = CreateService();
    service.ReceiverApplicationsReadOverride = async (_, ct) =>
    {
      await cts.CancelAsync();
      ct.ThrowIfCancellationRequested();
      return Array.Empty<string>();
    };
    var host = service.CreateProductionCastReconnectHost();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), cts.Token).WaitAsync(HangGuard));
    var ours = _castOutput.ConnectedDevice;
    Assert.NotNull(ours);

    var promotion = _engine.SetActiveOutputAsync("google-cast");
    await httpEntered.Task.WaitAsync(HangGuard);
    Assert.Equal("speakers", _engine.ActiveOutputId); // the window: not yet assigned

    var tearDown = host.TearDownCastUnlessCastActiveAsync();
    httpGate.SetResult();
    await promotion.WaitAsync(HangGuard);

    Assert.False(await tearDown.WaitAsync(HangGuard)); // declined: Cast is active
    Assert.Equal("google-cast", _engine.ActiveOutputId);
    Assert.Same(ours, _castOutput.ConnectedDevice);    // the connection the promotion serves
    engineCast.Verify(c => c.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task TheConditionalTearDown_WithLocalActive_RemovesOurConnection()
  {
    // The control for the test above.
    using var listener = StartListener();
    await _engine.SetActiveOutputAsync("speakers");
    _engine.AttachOutputCoordination(_castOutput, null, null);
    using var cts = new CancellationTokenSource();
    var service = CreateService();
    service.ReceiverApplicationsReadOverride = async (_, ct) =>
    {
      await cts.CancelAsync();
      ct.ThrowIfCancellationRequested();
      return Array.Empty<string>();
    };
    var host = service.CreateProductionCastReconnectHost();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => host.ConnectAndStartAsync(Device(Port(listener)), cts.Token).WaitAsync(HangGuard));
    Assert.NotNull(_castOutput.ConnectedDevice);

    Assert.True(await host.TearDownCastUnlessCastActiveAsync().WaitAsync(HangGuard));

    Assert.Null(_castOutput.ConnectedDevice);
  }

  [Fact]
  public async Task ReInitialisingTheOutput_EndsAHoldPlacedForTheConnectionItDiscards()
  {
    // LOW: a hold belongs to its connection. Re-initialising discards that connection.
    using var listener = StartListener();
    await _castOutput.InitializeAsync();
    await _castOutput.ConnectAsync(Device(Port(listener)), HeldConnect, CancellationToken.None).WaitAsync(HangGuard);
    Assert.True(_castOutput.IsHoldingForReceiverConfirmation);
    _castLog.ThrowWhen = m => m.StartsWith("Starting Google Cast output"); // a failed start: Error
    await Assert.ThrowsAsync<InvalidOperationException>(() => _castOutput.StartAsync().WaitAsync(HangGuard));
    _castLog.ThrowWhen = null;
    Assert.Equal(AudioOutputState.Error, _castOutput.State);

    await _castOutput.InitializeAsync().WaitAsync(HangGuard);

    Assert.False(_castOutput.IsHoldingForReceiverConfirmation);
  }

  [Fact]
  public async Task AHandledConnectionLoss_EndsAHoldPlacedForTheLostConnection()
  {
    // LOW. Only a Streaming output's loss is handled, and a held connection streams only if a
    // caller starts it unconfirmed — no caller does; this pins the clear for one that would.
    using var listener = StartListener();
    await _castOutput.InitializeAsync();
    await _castOutput.ConnectAsync(Device(Port(listener)), HeldConnect, CancellationToken.None).WaitAsync(HangGuard);
    typeof(GoogleCastOutput).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_castOutput, null);
    await _castOutput.StartAsync().WaitAsync(HangGuard);
    Assert.Equal(AudioOutputState.Streaming, _castOutput.State);
    Assert.True(_castOutput.IsHoldingForReceiverConfirmation);

    typeof(GoogleCastOutput)
      .GetMethod("ReportConnectionLost", BindingFlags.NonPublic | BindingFlags.Instance)!
      .Invoke(_castOutput, new object?[] { _castOutput.PublishedConnectionGeneration, "test", null });
    await ((Task)typeof(GoogleCastOutput)
      .GetProperty("LastConnectionLossHandling", BindingFlags.NonPublic | BindingFlags.Instance)!
      .GetValue(_castOutput)!).WaitAsync(HangGuard);

    Assert.Equal(AudioOutputState.Error, _castOutput.State); // the loss was handled
    Assert.False(_castOutput.IsHoldingForReceiverConfirmation);
  }

  private static readonly CastConnectOptions HeldConnect = new() { HoldUntilReceiverConfirmed = true, AutomaticAttempt = true };

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

  private static SoundFlowAudioEngine CreateBareEngine(ILogger<SoundFlowAudioEngine> logger)
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
      logger,
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
    public Task<bool> TearDownCastUnlessCastActiveAsync() => Task.FromResult(true);
    public Task RestoreLocalOutputAsync(CastRecoveryMark mark) => Task.CompletedTask;
    public Task<bool> TryKeepForCastChoiceAsync(bool castPickPending) => Task.FromResult(false);
  }
}
