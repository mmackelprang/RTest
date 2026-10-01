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

  // --- helpers ---

  private AudioEngineInitializationService CreateService(bool autoReconnect = true)
  {
    var provider = new Mock<IServiceProvider>();
    provider.Setup(p => p.GetService(typeof(GoogleCastOutput))).Returns(_castOutput);

    var preferences = new Mock<IOptionsMonitor<AudioPreferences>>();
    preferences.SetupGet(p => p.CurrentValue).Returns(new AudioPreferences());

    var outputOptions = new AudioOutputOptions();
    outputOptions.GoogleCast.AutoReconnect = autoReconnect;

    var service = new AudioEngineInitializationService(
      new Mock<ILogger<AudioEngineInitializationService>>().Object,
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
    for (var waits = 0; ; waits++)
    {
      Assert.True(waits < 500, "the watcher never stopped");
      var next = _time.NextTimerAsync();
      await Task.WhenAny(next, run).WaitAsync(HangGuard);

      // The run is checked first, not whichever WhenAny names: a run that ends by starting another
      // watcher (which creates a timer as it starts) completes both, and advancing that new
      // watcher's timer would drive work the caller never asked for.
      if (run.IsCompleted)
      {
        return await run;
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
    public string? ConnectedDeviceId;
    public CastRecoveryMark? SwitchedWith;

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

    public Task ConnectAndStartAsync(ChromecastDeviceInfo device, CancellationToken ct)
    {
      ConnectedDeviceId = device.Id;
      return Task.CompletedTask;
    }

    public async Task<bool> TrySwitchToCastAsync(CastRecoveryMark mark, CancellationToken ct)
    {
      SwitchedWith = mark;
      if (!IsStillOnRecoveryOutput(mark))
      {
        return false;
      }

      await Engine!._engine.Object.SetActiveOutputAsync("google-cast", ct);
      return true;
    }

    public Task TearDownCastAsync() => Task.CompletedTask;
  }
}
