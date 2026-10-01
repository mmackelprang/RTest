using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Services;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;

namespace Radio.API.Tests.Services;

/// <summary>
/// AUD-37: wiring of the reconnect watcher into the AUD-84 lost-Cast recovery — one watcher per
/// drop, started only when the recovery actually moved the output, replaced (never doubled) by
/// a newer drop, and cancelled promptly when the service stops. The watcher's own decisions are
/// covered by <see cref="CastReconnectWatcherTests"/>.
/// </summary>
/// <remarks>
/// Deterministic: the watcher waits on a <see cref="CastReconnectWatcherTests.SignalingTimeProvider"/>
/// and the probe/connect/switch steps go through a fake host; the recovery and the watcher are
/// awaited through <c>LastCastLossRecovery</c> and <c>CastReconnectTask</c>, never slept on.
/// </remarks>
public class AudioEngineInitializationServiceCastReconnectTests
{
  private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(15);

  private readonly Mock<IAudioEngine> _engine = new();
  private readonly Mock<IAudioDeviceManager> _deviceManager = new();
  private readonly GoogleCastOutput _castOutput;
  private readonly CastReconnectWatcherTests.SignalingTimeProvider _time = new();
  private readonly RecordingHost _host = new();
  private string? _active = "google-cast";

  public AudioEngineInitializationServiceCastReconnectTests()
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

    _engine.SetupGet(e => e.ActiveOutputId).Returns(() => _active);
    _engine
      .Setup(e => e.SetActiveOutputAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .Callback<string, CancellationToken>((id, _) => _active = id)
      .Returns(Task.CompletedTask);

    _host.Engine = this;
  }

  [Fact]
  public async Task LostCastConnection_StartsAWatcherForTheDroppedSpeaker_AndReturnsToCastWhenItAnswers()
  {
    _host.Reachable = true;
    var service = CreateService();

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    var outcome = await DriveAsync(service.CastReconnectTask);

    Assert.Equal(CastReconnectOutcome.Reconnected, outcome);
    Assert.Equal("cast-a", _host.ConnectedDeviceId);
    // The watcher's baseline is the local output the recovery chose, not any other.
    Assert.Equal("speakers", _host.SwitchedWith?.LocalOutputId);
    Assert.Equal("google-cast", _active);
  }

  [Fact]
  public async Task AutoReconnectDisabled_StartsNoWatcher()
  {
    _host.Reachable = true;
    var service = CreateService(autoReconnect: false);
    var initial = service.CastReconnectTask;

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);

    Assert.Same(initial, service.CastReconnectTask);
    Assert.Equal(0, _host.Probes);
    Assert.Equal("speakers", _active); // the AUD-84 fallback itself still happened
  }

  [Fact]
  public async Task RecoveryDeclinedBecauseTheUserAlreadyMoved_StartsNoWatcher()
  {
    _active = "hdmi";
    var service = CreateService();
    var initial = service.CastReconnectTask;

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);

    Assert.Same(initial, service.CastReconnectTask);
  }

  [Fact]
  public async Task ASecondDropReplacesTheWatcher_TheFirstIsCancelledBeforeTheSecondActs()
  {
    var service = CreateService();

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    var first = service.CastReconnectTask;
    await _time.NextTimerAsync().WaitAsync(HangGuard); // first watcher is in its first wait

    _active = "google-cast"; // Cast was picked and connected again, then dropped again
    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    var second = service.CastReconnectTask;

    Assert.NotSame(first, second);
    Assert.Equal(CastReconnectOutcome.Cancelled, await first.WaitAsync(HangGuard));

    // The replacement waits for the first to finish before it starts its own first wait.
    await _time.NextTimerAsync().WaitAsync(HangGuard);
    Assert.True(first.IsCompleted);
    Assert.False(second.IsCompleted);
    Assert.Equal(0, _host.Probes);
  }

  [Fact]
  public async Task ASecondDropWhileTheFirstWatcherIsMidProbe_TheReplacementWaitsForItToFinish()
  {
    // The first watcher is inside a step that does not observe cancellation (as a SharpCaster
    // connect does not). Its replacement must not start acting until it comes out.
    _host.ProbeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var service = CreateService();

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    var first = service.CastReconnectTask;
    _time.Advance(await _time.NextTimerAsync().WaitAsync(HangGuard));
    await _host.ProbeEntered.Task.WaitAsync(HangGuard);

    _active = "google-cast";
    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);

    // Bounded NEGATIVE check (CLAUDE.md § Test Timing): with the first watcher parked, a
    // correct replacement creates no timer at all, so this cannot fail on a slow machine; a
    // replacement that does not wait would create its first timer at once.
    var early = _time.NextTimerAsync();
    await Assert.ThrowsAsync<TimeoutException>(() => early.WaitAsync(TimeSpan.FromMilliseconds(500)));

    _host.ProbeGate.SetResult();
    Assert.Equal(CastReconnectOutcome.Cancelled, await first.WaitAsync(HangGuard));
    await early.WaitAsync(HangGuard); // now the replacement starts its first wait
    Assert.Equal(1, _host.Probes);
  }

  [Fact]
  public async Task ServiceStop_CancelsAWaitingWatcherPromptly()
  {
    var service = CreateService();

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    await _time.NextTimerAsync().WaitAsync(HangGuard);

    await service.StopAsync(CancellationToken.None).WaitAsync(HangGuard);

    Assert.Equal(CastReconnectOutcome.Cancelled, await service.CastReconnectTask.WaitAsync(HangGuard));
    Assert.Equal(0, _host.Probes);
  }

  [Fact]
  public async Task NewConnectionLostAgainAfterTheSwitch_RunsTheRecoveryAgainAndStartsAFreshWatcher()
  {
    _host.Reachable = true;
    _host.CastStreaming = false;
    var service = CreateService();

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    var first = service.CastReconnectTask;
    Assert.Equal(CastReconnectOutcome.LostAgainAfterSwitch, await DriveAsync(first));

    // The re-run recovery found Cast active, moved back to the local speakers, and replaced
    // the watcher.
    Assert.Equal("speakers", _active);
    _engine.Verify(e => e.SetActiveOutputAsync("speakers", It.IsAny<CancellationToken>()), Times.Exactly(2));
    Assert.NotSame(first, service.CastReconnectTask);
  }

  [Fact]
  public async Task ASpeakerThatDropsRightAfterEveryReconnect_IsGivenUpOnWithinTheOriginalWindow()
  {
    // Review H1: each drop of a watcher-made reconnect used to start a fresh 30 min window at a
    // fresh 5 s backoff, so a speaker that accepts the session and then dies cycled forever.
    _host.Reachable = true;
    var service = CreateService();
    var droppedAt = _time.GetUtcNow();

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);

    var outcomes = new List<CastReconnectOutcome>();
    for (var cycle = 0; ; cycle++)
    {
      Assert.True(cycle < 100, "the reconnect never gave up on a flapping speaker");
      var outcome = await DriveAsync(service.CastReconnectTask);
      outcomes.Add(outcome);
      if (outcome != CastReconnectOutcome.Reconnected)
      {
        break;
      }

      // Accepted the session, then died at once — the output is Cast, so the recovery runs.
      RaiseLoss(service);
      await service.LastCastLossRecovery.WaitAsync(HangGuard);
    }

    Assert.Equal(CastReconnectOutcome.GaveUp, outcomes[^1]);
    Assert.True(outcomes.Count > 5, $"only {outcomes.Count} cycles");
    Assert.True(_time.GetUtcNow() - droppedAt <= TimeSpan.FromMinutes(30));

    // Each watcher waits once at its backoff, then once for the 5 s confirmation. The backoff
    // keeps growing across the cycles instead of restarting at 5 s.
    var firstWaits = _time.DueTimes.Where((_, i) => i % 2 == 0).Select(d => (int)d.TotalSeconds).ToArray();
    Assert.Equal(new[] { 5, 10, 20, 40, 60, 60 }, firstWaits.Take(6).ToArray());
    Assert.All(firstWaits.Skip(4), w => Assert.Equal(60, w));
  }

  [Fact]
  public async Task TheFailedAttemptWarning_IsLoggedOncePerEpisode_NotOncePerWatcher()
  {
    // Review M3: a speaker that drops right after each reconnect gets a new watcher each time,
    // within one episode. Each watcher's first connect fails here; only the first logs a Warning.
    _host.Reachable = true;
    _host.ConnectFailure = connect => connect % 2 == 1 ? new InvalidOperationException("handshake failed") : null;
    var log = new CastReconnectWatcherTests.ListLogger<AudioEngineInitializationService>();
    var service = CreateService(logger: log);

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    Assert.Equal(CastReconnectOutcome.Reconnected, await DriveAsync(service.CastReconnectTask));

    RaiseLoss(service); // at once: inside the stability period, so the same episode
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    Assert.Equal(CastReconnectOutcome.Reconnected, await DriveAsync(service.CastReconnectTask));

    Assert.Equal(4, _host.Connects);
    Assert.Single(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("would not come up"));
  }

  [Fact]
  public async Task AReconnectThatStaysUpPastTheStabilityPeriod_LetsTheNextDropStartAFreshWindow()
  {
    _host.Reachable = true;
    var service = CreateService();

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    Assert.Equal(CastReconnectOutcome.Reconnected, await DriveAsync(service.CastReconnectTask));

    _time.Advance(TimeSpan.FromSeconds(121)); // the default stability period is 120 s

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    Assert.Equal(CastReconnectOutcome.Reconnected, await DriveAsync(service.CastReconnectTask));

    // Second watcher started at 5 s again, not at the inherited 10 s.
    Assert.Equal(new[] { 5, 5, 5, 5 }, _time.DueTimes.Select(d => (int)d.TotalSeconds).ToArray());
  }

  [Fact]
  public async Task ASpeakerThatStaysUpPastStabilityThenDrops_IsReconnectedAtMostSixTimesAnHour()
  {
    // Review H1, slow flapping: each reconnect here outlives the 120 s stability period, so each
    // drop starts a fresh window. The hourly cap is what ends it.
    _host.Reachable = true;
    var service = CreateService();

    var outcomes = new List<CastReconnectOutcome>();
    for (var drop = 0; drop < 7; drop++)
    {
      RaiseLoss(service);
      await service.LastCastLossRecovery.WaitAsync(HangGuard);
      outcomes.Add(await DriveAsync(service.CastReconnectTask));
      _time.Advance(TimeSpan.FromSeconds(121));
    }

    Assert.Equal(Enumerable.Repeat(CastReconnectOutcome.Reconnected, 6).Append(CastReconnectOutcome.GaveUp), outcomes);
    Assert.Equal(6, _host.Connects);

    // The user picks Cast again (DevicesController cancels the watcher, then switches): the
    // count resets and the next drop is watched.
    await service.CancelCastReconnectAsync().WaitAsync(HangGuard);
    await _engine.Object.SetActiveOutputAsync("google-cast", CancellationToken.None);
    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    Assert.Equal(CastReconnectOutcome.Reconnected, await DriveAsync(service.CastReconnectTask));
  }

  [Fact]
  public async Task TheHourlyCap_CountsOnlyTheLastHour()
  {
    // Seven reconnects eleven minutes apart: by the seventh drop the first has aged out.
    _host.Reachable = true;
    var service = CreateService();

    for (var drop = 0; drop < 7; drop++)
    {
      RaiseLoss(service);
      await service.LastCastLossRecovery.WaitAsync(HangGuard);
      Assert.Equal(CastReconnectOutcome.Reconnected, await DriveAsync(service.CastReconnectTask));
      _time.Advance(TimeSpan.FromMinutes(11));
    }

    Assert.Equal(7, _host.Connects);
  }

  [Fact]
  public async Task AUserActionEndsTheEpisode_TheNextDropStartsAFreshWindow()
  {
    _host.Reachable = true;
    var service = CreateService();

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    Assert.Equal(CastReconnectOutcome.Reconnected, await DriveAsync(service.CastReconnectTask));

    await service.CancelCastReconnectAsync().WaitAsync(HangGuard); // e.g. the user re-picked Cast

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    Assert.Equal(CastReconnectOutcome.Reconnected, await DriveAsync(service.CastReconnectTask));

    Assert.Equal(new[] { 5, 5, 5, 5 }, _time.DueTimes.Select(d => (int)d.TotalSeconds).ToArray());
  }

  [Fact]
  public async Task CancelCastReconnect_DuringTheWait_EndsTheWatcherBeforeItProbes()
  {
    // Review M1: a user's output or Cast action takes the Cast output over from the watcher.
    var log = new CastReconnectWatcherTests.ListLogger<AudioEngineInitializationService>();
    var service = CreateService(logger: log);

    // With no watcher running, a user action logs nothing (the controller calls this every time).
    await service.CancelCastReconnectAsync().WaitAsync(HangGuard);
    Assert.DoesNotContain(log.Entries, e => e.Message.Contains("no longer trying to reconnect"));

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    await _time.NextTimerAsync().WaitAsync(HangGuard); // the watcher is in its first wait

    await service.CancelCastReconnectAsync().WaitAsync(HangGuard);

    // The owner's script reads this line from the file sink: a user's pick of another output
    // while the speaker is away ends the reconnect visibly, at Information.
    Assert.Single(log.Entries, e => e.Level == LogLevel.Information
      && e.Message.Contains("no longer trying to reconnect"));

    // Awaited under the hang guard, not asserted IsCompleted: CancelCastReconnectAsync waits at
    // most the REAL 3 s CastReconnectCancelBound, so on a starved runner the watcher's exit could
    // land after it returns. That timing dependency could only fail this test, never pass a broken
    // cancel — a watcher that ignored the cancel stays parked on the fake clock (never advanced
    // here) and this wait times out.
    Assert.Equal(CastReconnectOutcome.Cancelled, await service.CastReconnectTask.WaitAsync(HangGuard));
    Assert.Equal(0, _host.Probes);
  }

  [Fact]
  public async Task CancelCastReconnect_DuringAConnectThatIgnoresCancellation_ReturnsAtTheBound_ThenTheWatcherRemovesItsConnection()
  {
    // Review M1 + L1. The watcher's connect is parked (a SharpCaster connect does not observe
    // cancellation). The user's action must not wait on it indefinitely, and when the connect
    // does come back the watcher must not switch the output — it removes what it made.
    _host.Reachable = true;
    _host.ConnectGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var service = CreateService();
    service.CastReconnectCancelBound = TimeSpan.FromMilliseconds(200);

    RaiseLoss(service);
    await service.LastCastLossRecovery.WaitAsync(HangGuard);
    await DriveUntilAsync(_host.ConnectEntered.Task);

    await service.CancelCastReconnectAsync().WaitAsync(HangGuard);
    Assert.False(service.CastReconnectTask.IsCompleted); // still parked: the bound let the caller go

    _host.ConnectGate.SetResult();
    Assert.Equal(CastReconnectOutcome.Cancelled, await service.CastReconnectTask.WaitAsync(HangGuard));
    Assert.Equal(0, _host.Switches);
    Assert.Equal(1, _host.TearDowns);
    Assert.Equal("speakers", _active);
  }

  // --- helpers ---

  private AudioEngineInitializationService CreateService(
    bool autoReconnect = true, ILogger<AudioEngineInitializationService>? logger = null)
  {
    var provider = new Mock<IServiceProvider>();
    provider.Setup(p => p.GetService(typeof(GoogleCastOutput))).Returns(_castOutput);

    var preferences = new Mock<IOptionsMonitor<AudioPreferences>>();
    preferences.SetupGet(p => p.CurrentValue).Returns(new AudioPreferences());

    var outputOptions = new AudioOutputOptions();
    outputOptions.GoogleCast.AutoReconnect = autoReconnect;

    var service = new AudioEngineInitializationService(
      logger ?? new Mock<ILogger<AudioEngineInitializationService>>().Object,
      _engine.Object,
      _deviceManager.Object,
      preferences.Object,
      new Mock<IMasterMixer>().Object,
      Options.Create(new BluetoothOptions { Enabled = false, EnableOnStartup = false }),
      Options.Create(outputOptions),
      provider.Object)
    {
      ReconnectTimeProvider = _time,
      CastReconnectHostOverride = _host
    };
    return service;
  }

  private void RaiseLoss(AudioEngineInitializationService service)
  {
    var field = typeof(GoogleCastOutput).GetField("Disconnected", BindingFlags.NonPublic | BindingFlags.Instance);
    var handler = (EventHandler<ChromecastDisconnectedEventArgs>)field!.GetValue(_castOutput)!;
    handler.Invoke(_castOutput, new ChromecastDisconnectedEventArgs
    {
      Device = new ChromecastDeviceInfo
      {
        Id = "cast-a",
        FriendlyName = "Office speaker",
        IpAddress = "192.0.2.10",
        Port = 8009,
        Model = "Google Home Mini"
      },
      Reason = "DirectChannel audio sends kept failing",
      IsConnectionLost = true
    });
  }

  private async Task<CastReconnectOutcome> DriveAsync(Task<CastReconnectOutcome> run)
  {
    await DriveUntilAsync(run);
    return await run;
  }

  // A read of the next timer that a drive left pending when its run ended. Kept for the next
  // drive rather than abandoned: an abandoned channel read would still take the next timer
  // created, and the next drive would wait forever for one after it.
  private Task<TimeSpan>? _pendingTimer;

  /// <summary>
  /// Advances the clock by each timer's due time, as each timer is created, until
  /// <paramref name="until"/> completes. Bounded: a watcher that never stops fails the test.
  /// </summary>
  private async Task DriveUntilAsync(Task until)
  {
    for (var waits = 0; ; waits++)
    {
      Assert.True(waits < 500, "the watcher never stopped");
      var next = _pendingTimer ?? _time.NextTimerAsync();
      _pendingTimer = null;
      await Task.WhenAny(next, until).WaitAsync(HangGuard);

      // The run is checked first, not whichever WhenAny names: a run that ends by starting another
      // watcher (which creates a timer as it starts) completes both, and advancing that new
      // watcher's timer would drive work the caller never asked for.
      if (until.IsCompleted)
      {
        _pendingTimer = next;
        return;
      }

      _time.Advance(await next);
    }
  }

  /// <summary>Drives the mocked engine the way the production host drives the real one.</summary>
  private sealed class RecordingHost : ICastReconnectHost
  {
    public AudioEngineInitializationServiceCastReconnectTests? Engine;
    public bool Reachable;
    public bool CastStreaming = true;
    public int Probes;
    public int Switches;
    public int TearDowns;
    public string? ConnectedDeviceId;
    public CastRecoveryMark? SwitchedWith;

    /// <summary>When set, a connect parks here, ignoring cancellation, until the test completes it.</summary>
    public TaskCompletionSource? ConnectGate;
    public readonly TaskCompletionSource ConnectEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsStillOnRecoveryOutput(CastRecoveryMark mark) =>
      string.Equals(Engine!._active, mark.LocalOutputId, StringComparison.OrdinalIgnoreCase);

    public bool IsCastIdle => true;

    public bool IsCastStreaming => CastStreaming;

    public string? ActiveOutputId => Engine!._active;

    /// <summary>When set, a probe parks here, ignoring cancellation, until the test completes it.</summary>
    public TaskCompletionSource? ProbeGate;
    public readonly TaskCompletionSource ProbeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<ChromecastDeviceInfo?> ProbeAsync(ChromecastDeviceInfo device, CancellationToken ct)
    {
      Interlocked.Increment(ref Probes);
      ProbeEntered.TrySetResult();
      if (ProbeGate is { } gate)
      {
        await gate.Task;
      }

      return Reachable ? device : null;
    }

    /// <summary>The exception the Nth connect (1-based) throws, or null to succeed.</summary>
    public Func<int, Exception?> ConnectFailure = _ => null;
    public int Connects;

    public async Task ConnectAndStartAsync(ChromecastDeviceInfo device, CancellationToken ct)
    {
      ConnectEntered.TrySetResult();
      if (ConnectGate is { } gate)
      {
        await gate.Task;
      }

      if (ConnectFailure(Interlocked.Increment(ref Connects)) is { } failure)
      {
        throw failure;
      }

      ConnectedDeviceId = device.Id;
    }

    public async Task<bool> TrySwitchToCastAsync(CastRecoveryMark mark, CancellationToken ct)
    {
      Interlocked.Increment(ref Switches);
      SwitchedWith = mark;
      if (!IsStillOnRecoveryOutput(mark))
      {
        return false;
      }

      await Engine!._engine.Object.SetActiveOutputAsync("google-cast", ct);
      return true;
    }

    public Task TearDownCastAsync()
    {
      Interlocked.Increment(ref TearDowns);
      return Task.CompletedTask;
    }

    public Task<bool> TearDownCastUnlessCastActiveAsync()
    {
      if (string.Equals(Engine!._active, "google-cast", StringComparison.OrdinalIgnoreCase))
      {
        return Task.FromResult(false);
      }

      Interlocked.Increment(ref TearDowns);
      return Task.FromResult(true);
    }

    public Task RestoreLocalOutputAsync(CastRecoveryMark mark) => Task.CompletedTask;

    public int Keeps;

    public Task<bool> TryKeepForCastChoiceAsync()
    {
      Interlocked.Increment(ref Keeps);
      return Task.FromResult(ConnectedDeviceId != null);
    }
  }
}
