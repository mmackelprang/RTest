using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Models;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;
using RTLSDRCore.Sweep;

namespace Radio.Infrastructure.Audio.Services;

/// <summary>Outcome of <see cref="BandMapService.RequestSweep"/>.</summary>
public enum BandSweepRequestOutcome
{
  /// <summary>A new sweep was started.</summary>
  Started,

  /// <summary>A sweep was already running; no second one was started.</summary>
  AlreadyRunning,

  /// <summary>No sweep could be started; see <see cref="BandSweepRequestResult.Reason"/>.</summary>
  Unavailable,
}

/// <summary>Result of <see cref="BandMapService.RequestSweep"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Reason">
/// For <see cref="BandSweepRequestOutcome.Unavailable"/>: one of <see cref="BandMapService.ReasonDisabled"/>,
/// <see cref="BandMapService.ReasonNoSdrDevice"/>, <see cref="BandMapService.ReasonRadioBusy"/> or
/// <see cref="BandMapService.ReasonDeviceBusy"/>. Null otherwise.
/// </param>
/// <param name="Status">Sweep status after the request.</param>
public sealed record BandSweepRequestResult(BandSweepRequestOutcome Outcome, string? Reason, BandSweepStatus Status);

/// <summary>
/// Keeps the stored FM band map current (AUD-76). Decides when a sweep may run and on which
/// device path, runs it on a background thread, and persists the result.
/// </summary>
/// <remarks>
/// <para>Rules:</para>
/// <list type="number">
/// <item>The timer first evaluates <see cref="BandMapOptions.InitialDelaySeconds"/> after
/// <see cref="StartAsync"/>, then <see cref="BandMapOptions.RescanIntervalMinutes"/> after each
/// evaluation or sweep. A sweep is due when there is no map or the map is at least that old.</item>
/// <item>Dongle idle (the gate grants a sweep lease): sweep through the idle path — this service
/// opens its own device. The radio claiming the gate cancels the lease; the sweep stops, closes
/// the device, releases the lease, and the previous map is kept.</item>
/// <item>Radio holds the gate: a timer evaluation sweeps through the live path only while
/// <see cref="ISleepService.IsSleeping"/> is true and the source reports
/// <see cref="ILiveBandSweeper.CanSweepLive"/>; otherwise it records a skip.
/// <see cref="ISleepService.IsSleepScreenVisible"/> is not consulted.</item>
/// <item><see cref="RequestSweep"/> starts a sweep through whichever path is available, including
/// the live path while the radio is playing.</item>
/// <item>At most one sweep runs at a time: the decision to start one and the record of the
/// running one are both taken under one lock.</item>
/// </list>
/// </remarks>
public sealed class BandMapService : IHostedService, IDisposable
{
  /// <summary>Unavailable reason: <see cref="BandMapOptions.Enabled"/> is false.</summary>
  public const string ReasonDisabled = "disabled";

  /// <summary>Unavailable reason: the dongle is idle but no RTL-SDR device could be created.</summary>
  public const string ReasonNoSdrDevice = "no-sdr-device";

  /// <summary>Unavailable reason: the radio source holds the dongle and cannot sweep live right now.</summary>
  public const string ReasonRadioBusy = "radio-busy";

  /// <summary>Unavailable reason: another sweep lease is live.</summary>
  public const string ReasonDeviceBusy = "device-busy";

  /// <summary>Skip reason for a timer evaluation while the radio source holds the dongle and the console is awake.</summary>
  public const string ReasonRadioPlaying = "radio-playing";

  // A zero due time can fire synchronously inside ITimer.Change on some TimeProviders.
  private static readonly TimeSpan MinimumTimerDue = TimeSpan.FromSeconds(1);

  private readonly ILogger<BandMapService> _logger;
  private readonly IOptionsMonitor<BandMapOptions> _options;
  private readonly BandMapStore _store;
  private readonly SdrDeviceGate _gate;
  private readonly Func<ILiveBandSweeper?> _liveSweeper;
  private readonly Func<ISleepService?> _sleepService;
  private readonly Func<ISdrDevice?> _deviceFactory;
  private readonly TimeProvider _time;
  private readonly CancellationTokenSource _stopCts = new();

  private readonly object _lock = new();
  private ITimer? _timer;
  private bool _stopping;
  private bool _disposed;
  private BandMap? _map;
  private BandSweepOutcome? _last;
  private SweepRun? _running;
  private Task _sweepTask = Task.CompletedTask;

  /// <summary>Creates the service and loads any stored map.</summary>
  /// <param name="logger">Logger.</param>
  /// <param name="options">Band map options.</param>
  /// <param name="store">Map file store.</param>
  /// <param name="gate">The SDR device gate shared with the radio source.</param>
  /// <param name="liveSweeper">Returns the cached radio source as a live sweeper, or null.</param>
  /// <param name="sleepService">
  /// Returns the sleep service, or null. A delegate because the sleep service is registered by
  /// the API host, after this service.
  /// </param>
  /// <param name="deviceFactory">
  /// Creates an unopened device for an idle sweep, or null when none exists. Defaults to the
  /// first enumerated RTL-SDR; unlike <see cref="SdrDeviceFactory.CreateFirstAvailable"/> it
  /// does not fall back to the mock device.
  /// </param>
  /// <param name="timeProvider">Clock; <see cref="TimeProvider.System"/> when omitted.</param>
  public BandMapService(
    ILogger<BandMapService> logger,
    IOptionsMonitor<BandMapOptions> options,
    BandMapStore store,
    SdrDeviceGate gate,
    Func<ILiveBandSweeper?> liveSweeper,
    Func<ISleepService?>? sleepService = null,
    Func<ISdrDevice?>? deviceFactory = null,
    TimeProvider? timeProvider = null)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _store = store ?? throw new ArgumentNullException(nameof(store));
    _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    _liveSweeper = liveSweeper ?? throw new ArgumentNullException(nameof(liveSweeper));
    _sleepService = sleepService ?? (() => null);
    _deviceFactory = deviceFactory ?? CreateFirstRtlSdrOrNull;
    _time = timeProvider ?? TimeProvider.System;

    _map = _store.Load();
    if (_map != null)
    {
      _logger.LogInformation("Loaded FM band map from {Path} ({Channels} channels, scanned {ScannedAt:u})",
        _store.FilePath, _map.Channels.Count, _map.ScannedAtUtc);
    }
  }

  /// <summary>The stored map, or null before the first completed sweep.</summary>
  public BandMap? CurrentMap
  {
    get
    {
      lock (_lock)
      {
        return _map;
      }
    }
  }

  /// <summary>
  /// Age of <paramref name="map"/> by this service's clock, or null when <paramref name="map"/> is null.
  /// </summary>
  public TimeSpan? GetAge(BandMap? map) => map == null ? null : _time.GetUtcNow() - map.ScannedAtUtc;

  /// <summary>Test rendezvous: the running sweep's task, or the last one's once it has finished.</summary>
  internal Task RunningSweep
  {
    get
    {
      lock (_lock)
      {
        return _sweepTask;
      }
    }
  }

  /// <summary>Current sweep status.</summary>
  public BandSweepStatus GetStatus()
  {
    lock (_lock)
    {
      return BuildStatusLocked();
    }
  }

  /// <summary>
  /// Starts a sweep for an explicit request, unless one is already running.
  /// </summary>
  public BandSweepRequestResult RequestSweep()
  {
    BandMapOptions options = _options.CurrentValue;
    lock (_lock)
    {
      if (_running != null)
      {
        return new BandSweepRequestResult(BandSweepRequestOutcome.AlreadyRunning, null, BuildStatusLocked());
      }

      if (!options.Enabled)
      {
        return new BandSweepRequestResult(BandSweepRequestOutcome.Unavailable, ReasonDisabled, BuildStatusLocked());
      }

      string? reason = TryStartLocked(isRequest: true, options);
      if (reason != null)
      {
        _last = SkippedOutcome(BandSweepTriggers.Request, reason);
        _logger.LogInformation("Band sweep requested but not started: {Reason}", reason);
        return new BandSweepRequestResult(BandSweepRequestOutcome.Unavailable, reason, BuildStatusLocked());
      }

      return new BandSweepRequestResult(BandSweepRequestOutcome.Started, null, BuildStatusLocked());
    }
  }

  /// <inheritdoc/>
  public Task StartAsync(CancellationToken cancellationToken)
  {
    BandMapOptions options = _options.CurrentValue;
    if (!options.Enabled)
    {
      _logger.LogInformation("FM band map sweeps are disabled (BandMap:Enabled=false)");
      return Task.CompletedTask;
    }

    lock (_lock)
    {
      TimeSpan initialDelay = TimeSpan.FromSeconds(Math.Max(0, options.InitialDelaySeconds));
      _timer = _time.CreateTimer(_ => OnTimerTick(), null, ClampDue(initialDelay), Timeout.InfiniteTimeSpan);
    }
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  public async Task StopAsync(CancellationToken cancellationToken)
  {
    Task running;
    lock (_lock)
    {
      if (_disposed)
      {
        return;
      }
      _stopping = true;
      _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
      running = _sweepTask;
    }

    _stopCts.Cancel();
    try
    {
      await running.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      // Host shutdown deadline reached; the sweep will finish on its own.
    }
  }

  /// <inheritdoc/>
  /// <remarks>
  /// Idempotent: when both the <see cref="BandMapService"/> registration and the
  /// <see cref="IHostedService"/> factory registration that returns the same instance have been
  /// resolved, the container disposes the instance once for each.
  /// </remarks>
  public void Dispose()
  {
    lock (_lock)
    {
      if (_disposed)
      {
        return;
      }
      _disposed = true;
      _stopping = true;
      _timer?.Dispose();
      _timer = null;
    }
    _stopCts.Cancel();
    _stopCts.Dispose();
  }

  /// <summary>Timer callback: evaluates whether a sweep is due and may run.</summary>
  internal void OnTimerTick()
  {
    BandMapOptions options = _options.CurrentValue;
    TimeSpan interval = RescanInterval(options);
    lock (_lock)
    {
      if (_stopping)
      {
        return;
      }

      if (_running != null)
      {
        // Re-armed when the running sweep finishes.
        _logger.LogDebug("Band map timer: a sweep is already running");
        return;
      }

      DateTimeOffset now = _time.GetUtcNow();
      if (_map != null && now - _map.ScannedAtUtc < interval)
      {
        TimeSpan untilDue = _map.ScannedAtUtc + interval - now;
        _logger.LogDebug("Band map timer: map is {Age} old, next evaluation in {UntilDue}", now - _map.ScannedAtUtc, untilDue);
        ArmTimerLocked(untilDue);
        return;
      }

      string? reason = TryStartLocked(isRequest: false, options);
      if (reason != null)
      {
        _last = SkippedOutcome(BandSweepTriggers.Timer, reason);
        _logger.LogDebug("Band map timer: sweep skipped ({Reason})", reason);
        ArmTimerLocked(interval);
      }
    }
  }

  /// <summary>
  /// Picks a path and starts the sweep. Must be called holding <c>_lock</c> with no sweep running.
  /// </summary>
  /// <returns>Null when a sweep was started; otherwise why not.</returns>
  private string? TryStartLocked(bool isRequest, BandMapOptions options)
  {
    SdrDeviceGate.SweepLease? lease = _gate.TryAcquireForSweep();
    if (lease != null)
    {
      ISdrDevice? device;
      try
      {
        device = _deviceFactory();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Band sweep: creating the SDR device failed");
        device = null;
      }

      if (device == null)
      {
        lease.Dispose();
        return ReasonNoSdrDevice;
      }

      SweepRun idleRun = BeginRunLocked(isRequest ? BandSweepTriggers.Request : BandSweepTriggers.Timer, BandSweepPaths.Idle);
      _sweepTask = Task.Run(() => RunIdle(idleRun, lease, device, options));
      return null;
    }

    if (!_gate.IsHeldByRadio)
    {
      return ReasonDeviceBusy;
    }

    // The radio source holds the dongle.
    if (!isRequest && _sleepService()?.IsSleeping != true)
    {
      return ReasonRadioPlaying;
    }

    ILiveBandSweeper? live = _liveSweeper();
    if (live == null || !live.CanSweepLive)
    {
      return ReasonRadioBusy;
    }

    SweepRun liveRun = BeginRunLocked(isRequest ? BandSweepTriggers.Request : BandSweepTriggers.Sleep, BandSweepPaths.Live);
    _sweepTask = Task.Run(() => RunLiveAsync(liveRun, live, options));
    return null;
  }

  private SweepRun BeginRunLocked(string trigger, string path)
  {
    SweepRun run = new(trigger, path, _time.GetUtcNow(), _time.GetTimestamp(), FmChannelPlan.Channels.Count);
    _running = run;
    _logger.LogInformation("Band sweep started (trigger {Trigger}, path {Path})", trigger, path);
    return run;
  }

  private void RunIdle(SweepRun run, SdrDeviceGate.SweepLease lease, ISdrDevice device, BandMapOptions options)
  {
    IReadOnlyList<ChannelLevel>? levels = null;
    string result;
    string? reason = null;
    Exception? failure = null;
    CancellationToken leaseToken = lease.Token;

    try
    {
      using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(leaseToken, _stopCts.Token);
      try
      {
        // Declared inside the try so the device is closed and disposed before
        // the lease is released below.
        using DeviceSweepTuner tuner = new(device, options.SweepGainDb);
        tuner.Open();
        levels = BandSweeper.Sweep(tuner, FmChannelPlan.Channels, options.SamplesPerMeasurement, run, linked.Token);
        result = BandSweepResults.Completed;
      }
      catch (OperationCanceledException) when (linked.IsCancellationRequested)
      {
        result = BandSweepResults.Cancelled;
        reason = leaseToken.IsCancellationRequested ? "radio-claimed" : "service-stopping";
      }
      catch (Exception ex)
      {
        result = BandSweepResults.Failed;
        reason = ex.Message;
        failure = ex;
      }
    }
    finally
    {
      lease.Dispose();
    }

    Finish(run, result, reason, levels, failure, options);
  }

  private async Task RunLiveAsync(SweepRun run, ILiveBandSweeper live, BandMapOptions options)
  {
    IReadOnlyList<ChannelLevel>? levels = null;
    string result;
    string? reason = null;
    Exception? failure = null;

    try
    {
      levels = await live.SweepLiveAsync(
        FmChannelPlan.Channels, options.SweepGainDb, options.SamplesPerMeasurement, run, _stopCts.Token).ConfigureAwait(false);
      result = BandSweepResults.Completed;
    }
    catch (OperationCanceledException)
    {
      // The receiver cancels its own sweep on a user tune, band change, seek scan, wake or shutdown.
      result = BandSweepResults.Cancelled;
      reason = _stopCts.IsCancellationRequested ? "service-stopping" : "interrupted";
    }
    catch (Exception ex)
    {
      result = BandSweepResults.Failed;
      reason = ex.Message;
      failure = ex;
    }

    Finish(run, result, reason, levels, failure, options);
  }

  private void Finish(
    SweepRun run, string result, string? reason, IReadOnlyList<ChannelLevel>? levels, Exception? failure, BandMapOptions options)
  {
    TimeSpan duration = _time.GetElapsedTime(run.StartTimestamp);
    int measured = levels?.Count ?? 0;
    if (result == BandSweepResults.Completed && measured == 0)
    {
      result = BandSweepResults.Failed;
      reason = "no-channels-measured";
    }

    BandMap? newMap = null;
    if (result == BandSweepResults.Completed)
    {
      newMap = new BandMap
      {
        Band = "FM",
        ScannedAtUtc = _time.GetUtcNow(),
        Channels = levels!.Select(l => new BandMapChannel(l.FrequencyHz, l.LevelDbfs)).ToArray(),
        Trigger = run.Trigger,
        Path = run.Path,
      };

      try
      {
        _store.Save(newMap);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        _logger.LogWarning(ex, "Band map could not be written to {Path}; keeping it in memory only", _store.FilePath);
      }
    }

    BandSweepOutcome outcome = new()
    {
      Trigger = run.Trigger,
      Path = run.Path,
      StartedAtUtc = run.StartedAtUtc,
      DurationMs = (long)duration.TotalMilliseconds,
      Result = result,
      Reason = reason,
      ChannelsMeasured = measured,
    };

    lock (_lock)
    {
      if (newMap != null)
      {
        _map = newMap;
      }
      _last = outcome;
      _running = null;
      if (!_stopping)
      {
        ArmTimerLocked(RescanInterval(options));
      }
    }

    if (result == BandSweepResults.Failed)
    {
      _logger.LogWarning(failure,
        "Band sweep finished: {Result} ({Reason}); trigger {Trigger}, path {Path}, {Measured} channels measured in {DurationMs} ms",
        result, reason, run.Trigger, run.Path, measured, outcome.DurationMs);
    }
    else
    {
      string strongest = levels == null
        ? "none"
        : string.Join(", ", levels.OrderByDescending(l => l.LevelDbfs).Take(3)
          .Select(l => $"{l.FrequencyHz / 1_000_000.0:F1} MHz {l.LevelDbfs:F1} dB"));
      _logger.LogInformation(
        "Band sweep finished: {Result}{Reason}; trigger {Trigger}, path {Path}, {Measured} channels measured in {DurationMs} ms; strongest: {Strongest}",
        result, reason == null ? string.Empty : $" ({reason})", run.Trigger, run.Path, measured, outcome.DurationMs, strongest);
    }
  }

  private BandSweepStatus BuildStatusLocked()
  {
    SweepRun? run = _running;
    if (run == null)
    {
      return new BandSweepStatus { IsSweeping = false, Last = _last };
    }

    int done = run.ChannelsDone;
    double progress = run.ChannelsTotal == 0 ? 0 : (double)done / run.ChannelsTotal;
    double? remaining = null;
    if (done > 0)
    {
      double elapsedSeconds = _time.GetElapsedTime(run.StartTimestamp).TotalSeconds;
      remaining = elapsedSeconds / done * (run.ChannelsTotal - done);
    }

    return new BandSweepStatus
    {
      IsSweeping = true,
      Trigger = run.Trigger,
      Path = run.Path,
      Progress = progress,
      StartedAtUtc = run.StartedAtUtc,
      EstimatedSecondsRemaining = remaining,
      Last = _last,
    };
  }

  private BandSweepOutcome SkippedOutcome(string trigger, string reason) => new()
  {
    Trigger = trigger,
    StartedAtUtc = _time.GetUtcNow(),
    Result = BandSweepResults.Skipped,
    Reason = reason,
  };

  private void ArmTimerLocked(TimeSpan due)
  {
    _timer?.Change(ClampDue(due), Timeout.InfiniteTimeSpan);
  }

  private static TimeSpan ClampDue(TimeSpan due) => due < MinimumTimerDue ? MinimumTimerDue : due;

  private static TimeSpan RescanInterval(BandMapOptions options) =>
    TimeSpan.FromMinutes(Math.Max(1, options.RescanIntervalMinutes));

  private static ISdrDevice? CreateFirstRtlSdrOrNull()
  {
    DeviceInfo? info = SdrDeviceFactory.EnumerateDevices()
      .FirstOrDefault(d => d.Type == DeviceType.RTLSDR && d.IsAvailable);
    return info == null ? null : SdrDeviceFactory.CreateDevice(info);
  }

  /// <summary>State of the running sweep; also its progress sink.</summary>
  private sealed class SweepRun : IProgress<BandSweepProgress>
  {
    private int _channelsDone;

    public SweepRun(string trigger, string path, DateTimeOffset startedAtUtc, long startTimestamp, int channelsTotal)
    {
      Trigger = trigger;
      Path = path;
      StartedAtUtc = startedAtUtc;
      StartTimestamp = startTimestamp;
      ChannelsTotal = channelsTotal;
    }

    public string Trigger { get; }

    public string Path { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public long StartTimestamp { get; }

    public int ChannelsTotal { get; }

    public int ChannelsDone => Volatile.Read(ref _channelsDone);

    public void Report(BandSweepProgress value) => Volatile.Write(ref _channelsDone, value.ChannelsDone);
  }
}
