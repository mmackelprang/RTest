using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Radio.API.Services;
using Radio.Infrastructure.Audio.Outputs;

namespace Radio.API.Tests.Services;

/// <summary>
/// AUD-37: the reconnect watcher that moves the output back to a dropped Cast speaker when it
/// returns — only if nobody has chosen an output since, never while anyone else owns the Cast
/// output, with capped exponential backoff over a bounded window.
/// </summary>
/// <remarks>
/// Fully deterministic (CLAUDE.md § Test Timing): every wait is a timer on a
/// <see cref="SignalingTimeProvider"/>, which reports each timer as it is created, and the test
/// advances the fake clock by exactly that timer's due time only after it exists. Nothing here
/// sleeps or races a wall clock.
/// </remarks>
public class CastReconnectWatcherTests
{
  private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(15);
  private static readonly CastRecoveryMark Mark = new("speakers", 7);

  private static readonly CastReconnectSchedule DefaultSchedule = new(
    TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(30));

  private readonly SignalingTimeProvider _time = new();
  private readonly FakeHost _host = new();

  [Fact]
  public async Task SpeakerAnswersOnTheThirdProbe_ConnectsThenSwitchesBackToCast()
  {
    _host.Reachable = probe => probe >= 3;

    var outcome = await DriveAsync(Start());

    Assert.Equal(CastReconnectOutcome.Reconnected, outcome);
    Assert.Equal(3, _host.Probes);
    Assert.Equal(1, _host.Connects);
    Assert.Equal(1, _host.Switches);
    Assert.Equal(Mark, _host.SwitchedWith);
    Assert.Equal(0, _host.TearDowns);
    // Connect strictly before the switch: the switch is what moves the output to Cast.
    Assert.Equal(new[] { "probe", "probe", "probe", "connect", "switch" }, _host.Calls);
  }

  [Fact]
  public async Task UserChangedTheOutputDuringTheWait_StopsWithoutConnecting()
  {
    _host.OnProbe = probe =>
    {
      if (probe == 1)
      {
        _host.StillOnRecovery = false; // the user picked something between probes
      }
    };

    var outcome = await DriveAsync(Start());

    Assert.Equal(CastReconnectOutcome.OutputChangedByUser, outcome);
    Assert.Equal(1, _host.Probes);
    Assert.Equal(0, _host.Connects);
    Assert.Equal(0, _host.Switches);
  }

  [Fact]
  public async Task UserChangedTheOutputDuringTheProbe_DoesNotConnect()
  {
    // The speaker answers, but the user moved while the probe was on the network.
    _host.Reachable = _ => true;
    _host.OnProbe = _ => _host.StillOnRecovery = false;

    var outcome = await DriveAsync(Start());

    Assert.Equal(CastReconnectOutcome.OutputChangedByUser, outcome);
    Assert.Equal(0, _host.Connects);
  }

  [Fact]
  public async Task SwitchRefusedBecauseTheUserMovedToAnotherOutput_TearsCastDownAndStops()
  {
    _host.Reachable = _ => true;
    _host.SwitchResult = false;
    _host.Active = "hdmi";

    var outcome = await DriveAsync(Start());

    Assert.Equal(CastReconnectOutcome.OutputChangedByUser, outcome);
    Assert.Equal(1, _host.Connects);
    Assert.Equal(1, _host.TearDowns);
  }

  [Fact]
  public async Task SwitchRefusedBecauseTheUserPickedCast_LeavesTheConnectionServingThatChoice()
  {
    _host.Reachable = _ => true;
    _host.SwitchResult = false;
    _host.Active = "google-cast";

    var outcome = await DriveAsync(Start());

    Assert.Equal(CastReconnectOutcome.OutputChangedByUser, outcome);
    Assert.Equal(0, _host.TearDowns);
  }

  [Fact]
  public async Task CastConnectedByAnotherPartyDuringTheWait_StopsWithoutConnecting()
  {
    // AUD-85: exactly one connecting party. A manual pick/connect owns the Cast output now.
    _host.OnProbe = probe =>
    {
      if (probe == 1)
      {
        _host.CastIdle = false;
      }
    };

    var outcome = await DriveAsync(Start());

    Assert.Equal(CastReconnectOutcome.CastBusy, outcome);
    Assert.Equal(1, _host.Probes);
    Assert.Equal(0, _host.Connects);
  }

  [Fact]
  public async Task CastMidTransitionWhenTheSpeakerAnswers_DoesNotConnect()
  {
    _host.Reachable = _ => true;
    _host.OnProbe = _ => _host.CastIdle = false; // another party started connecting meanwhile

    var outcome = await DriveAsync(Start());

    Assert.Equal(CastReconnectOutcome.CastBusy, outcome);
    Assert.Equal(0, _host.Connects);
  }

  [Fact]
  public async Task Backoff_DoublesFromFiveSecondsAndHoldsAtTheSixtySecondCap()
  {
    _host.OnProbe = probe =>
    {
      if (probe == 7)
      {
        _host.StillOnRecovery = false; // end the run after the 8th wait
      }
    };

    await DriveAsync(Start());

    Assert.Equal(
      new[] { 5, 10, 20, 40, 60, 60, 60, 60 },
      _time.DueTimes.Select(d => (int)d.TotalSeconds).ToArray());
  }

  [Fact]
  public async Task SpeakerNeverReturns_GivesUpWhenTheWindowRunsOut()
  {
    // 5 + 10 + 20 + 40 = 75 s of waits fit in two minutes; the next 60 s wait would end at
    // 135 s, past the window, so the watcher stops instead of starting it.
    var schedule = DefaultSchedule with { Window = TimeSpan.FromMinutes(2) };

    var outcome = await DriveAsync(Start(schedule));

    Assert.Equal(CastReconnectOutcome.GaveUp, outcome);
    Assert.Equal(4, _host.Probes);
    Assert.All(_host.ProbeTimes, t => Assert.True(t <= TimeSpan.FromMinutes(2)));
    Assert.Equal(0, _host.Connects);
  }

  [Fact]
  public async Task DefaultWindow_StopsAfterThirtyMinutesOfProbing()
  {
    var outcome = await DriveAsync(Start());

    Assert.Equal(CastReconnectOutcome.GaveUp, outcome);
    // 75 s in the first four waits, then one probe a minute until 30 min: 75 + 28 * 60 = 1755.
    Assert.Equal(4 + 28, _host.Probes);
    Assert.Equal(TimeSpan.FromSeconds(1755), _host.ProbeTimes[^1]);
  }

  [Fact]
  public async Task ConnectFails_KeepsBackingOffAndReconnectsOnALaterProbe()
  {
    _host.Reachable = _ => true;
    _host.ConnectFailure = connect => connect == 1 ? new InvalidOperationException("receiver not ready") : null;

    var outcome = await DriveAsync(Start());

    Assert.Equal(CastReconnectOutcome.Reconnected, outcome);
    Assert.Equal(2, _host.Connects);
    Assert.Equal(new[] { 5, 10 }, _time.DueTimes.Select(d => (int)d.TotalSeconds).ToArray());
  }

  [Fact]
  public async Task Cancelled_WhileWaiting_EndsAtOnceWithoutProbing()
  {
    using var cts = new CancellationTokenSource();
    var run = Start(ct: cts.Token);
    await _time.NextTimerAsync().WaitAsync(HangGuard);

    await cts.CancelAsync();
    var outcome = await run.WaitAsync(HangGuard);

    Assert.Equal(CastReconnectOutcome.Cancelled, outcome);
    Assert.Equal(0, _host.Probes);
  }

  [Fact]
  public async Task NewConnectionLostBeforeTheSwitchSettled_ReportsLostAgain()
  {
    _host.Reachable = _ => true;
    _host.CastStreaming = false;

    var outcome = await DriveAsync(Start());

    Assert.Equal(CastReconnectOutcome.LostAgainAfterSwitch, outcome);
  }

  // --- helpers ---

  private Task<CastReconnectOutcome> Start(CastReconnectSchedule? schedule = null, CancellationToken ct = default)
  {
    _host.Clock = _time;
    var watcher = new CastReconnectWatcher(
      _host, Device(), Mark, schedule ?? DefaultSchedule, _time, NullLogger.Instance);
    var started = _time.GetUtcNow();
    _host.StartedAt = started;
    return Task.Run(() => watcher.RunAsync(ct));
  }

  /// <summary>
  /// Advances the clock by each timer's due time, as each timer is created, until the run ends.
  /// Bounded: a watcher that never stops (no window, say) fails the test instead of spinning.
  /// </summary>
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

  private static ChromecastDeviceInfo Device() => new()
  {
    Id = "cast-a",
    FriendlyName = "Office speaker",
    IpAddress = "192.0.2.10",
    Port = 8009,
    Model = "Google Home Mini"
  };

  /// <summary>A <see cref="FakeTimeProvider"/> that reports every timer it creates.</summary>
  internal sealed class SignalingTimeProvider : FakeTimeProvider
  {
    private readonly Channel<TimeSpan> _created = Channel.CreateUnbounded<TimeSpan>();

    public List<TimeSpan> DueTimes { get; } = new();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
      var timer = base.CreateTimer(callback, state, dueTime, period);
      lock (DueTimes)
      {
        DueTimes.Add(dueTime);
      }

      _created.Writer.TryWrite(dueTime);
      return timer;
    }

    /// <summary>Completes with the due time of the next timer created.</summary>
    public Task<TimeSpan> NextTimerAsync() => _created.Reader.ReadAsync().AsTask();
  }

  private sealed class FakeHost : ICastReconnectHost
  {
    public SignalingTimeProvider? Clock;
    public DateTimeOffset StartedAt;

    public bool StillOnRecovery = true;
    public bool CastIdle = true;
    public bool CastStreaming = true;
    public string? Active = "speakers";
    public bool SwitchResult = true;
    public Func<int, bool> Reachable = _ => false;
    public Func<int, Exception?> ConnectFailure = _ => null;
    public Action<int>? OnProbe;

    public int Probes;
    public int Connects;
    public int Switches;
    public int TearDowns;
    public CastRecoveryMark? SwitchedWith;
    public List<string> Calls { get; } = new();
    public List<TimeSpan> ProbeTimes { get; } = new();

    public bool IsStillOnRecoveryOutput(CastRecoveryMark mark) => StillOnRecovery;

    public bool IsCastIdle => CastIdle;

    public bool IsCastStreaming => CastStreaming;

    public string? ActiveOutputId => Active;

    public Task<ChromecastDeviceInfo?> ProbeAsync(ChromecastDeviceInfo device, CancellationToken ct)
    {
      var probe = ++Probes;
      Calls.Add("probe");
      if (Clock != null)
      {
        ProbeTimes.Add(Clock.GetUtcNow() - StartedAt);
      }

      var reachable = Reachable(probe);
      OnProbe?.Invoke(probe);
      return Task.FromResult(reachable ? device : null);
    }

    public Task ConnectAndStartAsync(ChromecastDeviceInfo device, CancellationToken ct)
    {
      var connect = ++Connects;
      Calls.Add("connect");
      var failure = ConnectFailure(connect);
      return failure == null ? Task.CompletedTask : Task.FromException(failure);
    }

    public Task<bool> TrySwitchToCastAsync(CastRecoveryMark mark, CancellationToken ct)
    {
      Switches++;
      Calls.Add("switch");
      SwitchedWith = mark;
      return Task.FromResult(SwitchResult);
    }

    public Task TearDownCastAsync()
    {
      TearDowns++;
      Calls.Add("teardown");
      return Task.CompletedTask;
    }
  }
}
