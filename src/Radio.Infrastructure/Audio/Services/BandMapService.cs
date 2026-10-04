using System.Globalization;
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

  /// <summary>A sweep of the band asked for was already running; no second one was started.</summary>
  AlreadyRunning,

  /// <summary>No sweep could be started; see <see cref="BandSweepRequestResult.Reason"/>.</summary>
  Unavailable,
}

/// <summary>Result of <see cref="BandMapService.RequestSweep"/>.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Reason">
/// For <see cref="BandSweepRequestOutcome.Unavailable"/>: one of <see cref="BandMapService.ReasonDisabled"/>,
/// <see cref="BandMapService.ReasonBandNotReceivable"/>, <see cref="BandMapService.ReasonNoSdrDevice"/>,
/// <see cref="BandMapService.ReasonRadioBusy"/>, <see cref="BandMapService.ReasonDeviceBusy"/> or
/// <see cref="BandMapService.ReasonOtherBandSweeping"/>. Null otherwise.
/// </param>
/// <param name="Status">Sweep status after the request.</param>
public sealed record BandSweepRequestResult(BandSweepRequestOutcome Outcome, string? Reason, BandSweepStatus Status);

/// <summary>The radio's current tuning, as <see cref="BandMapService"/> reads it.</summary>
/// <param name="Band">Band code: <c>AM</c>, <c>FM</c>, <c>SW</c>, <c>AIR</c>, <c>WB</c> or <c>VHF</c>.</param>
/// <param name="FrequencyHz">Tuned frequency, in Hz.</param>
public sealed record BandMapTuning(string Band, long FrequencyHz);

/// <summary>
/// Keeps the stored band maps current (AUD-76; one map per band since AUD-91). Decides when a
/// sweep may run and on which device path, runs it on a background thread, and persists the result.
/// </summary>
/// <remarks>
/// <para>
/// The current band is the band of the radio's tuning (the <c>currentTuning</c> constructor
/// delegate), or FM when that delegate returns null or throws. Each band is swept with its
/// <see cref="BandSweepPlans.For"/> plan; the VHF plan is centred on the radio's frequency only
/// while the radio is on VHF. A band with no plan is never swept.
/// </para>
/// <para>Rules:</para>
/// <list type="number">
/// <item>The timer first evaluates <see cref="BandMapOptions.InitialDelaySeconds"/> after
/// <see cref="StartAsync"/>, then <see cref="BandMapOptions.RescanIntervalMinutes"/> after each
/// evaluation or sweep. Each evaluation considers only the current band: when it has no plan the
/// evaluation records a skip (<see cref="ReasonBandNotReceivable"/>); otherwise a sweep is due
/// when that band has no map, its map is at least that old, or its map recorded a range that does
/// not contain the centre of the band's current plan (the radio has moved its VHF window off the
/// stored one).</item>
/// <item>Dongle idle (the gate grants a sweep lease): sweep through the idle path — this service
/// opens its own device. The radio claiming the gate cancels the lease; the sweep stops, closes
/// the device, releases the lease, and the previous map is kept.</item>
/// <item>Radio holds the gate: a timer evaluation sweeps through the live path only while
/// <see cref="ISleepService.IsSleeping"/> is true and the source reports
/// <see cref="ILiveBandSweeper.CanSweepLive"/>; otherwise it records a skip.
/// <see cref="ISleepService.IsSleepScreenVisible"/> is not consulted. Such a sleep-triggered
/// sweep is cancelled after the first progress report (each FM channel, each tune of a grouped
/// plan) at which <see cref="ISleepService.IsSleeping"/> reads false.</item>
/// <item>A timer evaluation starts no sweep while <see cref="BandMapOptions.Enabled"/> is false.</item>
/// <item><see cref="RequestSweep"/> starts a sweep of the band asked for (the current band when
/// none is given) through whichever path is available, including the live path while the radio
/// is playing. A request for VHF while the radio is on another band sweeps 30 to 32 MHz.</item>
/// <item>At most one sweep runs at a time: the decision to start one and the record of the
/// running one are both taken under one lock.</item>
/// </list>
/// <para>
/// Seek-observed stations (AUD-100) are kept per band beside the maps, never inside them: recording
/// one changes no swept channel, no <see cref="BandMap.ScannedAtUtc"/> and no age, and a band with
/// only seek-observed stations still has no map. A completed sweep drops the band's seek-observed
/// stations inside the range it swept.
/// </para>
/// </remarks>
public sealed class BandMapService : IHostedService, IDisposable, IScanStationMap
{
  /// <summary>Why Scan seeks live (<see cref="GetScanStations"/>): <see cref="BandMapOptions.ScanMapMaxAgeMinutes"/> is 0 or less.</summary>
  public const string ScanReasonDisabled = "scan-from-map-disabled";

  /// <summary>Why Scan seeks live: the band has no sweep plan, or is not a band code.</summary>
  public const string ScanReasonNotMappable = "band-not-mappable";

  /// <summary>Why Scan seeks live: the band has never been swept.</summary>
  public const string ScanReasonNoMap = "no-map";

  /// <summary>Why Scan seeks live: the band's map is older than <see cref="BandMapOptions.ScanMapMaxAgeMinutes"/>.</summary>
  public const string ScanReasonStale = "map-stale";

  /// <summary>Why Scan seeks live: the radio's frequency is outside the range the map swept.</summary>
  public const string ScanReasonOutsideMap = "outside-map";

  /// <summary>Why Scan seeks live: the map lists no station other than the one the radio is on.</summary>
  public const string ScanReasonNoOtherStation = "no-other-station";

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

  /// <summary>
  /// Unavailable reason for a request, and skip reason for a timer evaluation: the band has no
  /// sweep plan (<see cref="BandSweepPlans.UnavailableReason"/> says why).
  /// </summary>
  public const string ReasonBandNotReceivable = "band-not-receivable";

  /// <summary>
  /// Unavailable reason for a request: a sweep of a different band is running, so nothing was
  /// started for the band asked for.
  /// </summary>
  public const string ReasonOtherBandSweeping = "other-band-sweeping";

  // A zero due time can fire synchronously inside ITimer.Change on some TimeProviders.
  private static readonly TimeSpan MinimumTimerDue = TimeSpan.FromSeconds(1);

  private readonly ILogger<BandMapService> _logger;
  private readonly IOptionsMonitor<BandMapOptions> _options;
  private readonly BandMapStore _store;
  private readonly SdrDeviceGate _gate;
  private readonly Func<ILiveBandSweeper?> _liveSweeper;
  private readonly Func<ISleepService?> _sleepService;
  private readonly Func<ISdrDevice?> _deviceFactory;
  private readonly Func<BandMapTuning?> _currentTuning;
  private readonly TimeProvider _time;
  private readonly CancellationTokenSource _stopCts = new();

  private readonly object _lock = new();
  private ITimer? _timer;
  private bool _stopping;
  private bool _disposed;
  // Keyed by band code.
  private readonly Dictionary<string, BandMap> _maps = new(StringComparer.OrdinalIgnoreCase);
  // Seek-observed stations (AUD-100), keyed by band code; each list ascending by frequency.
  private readonly Dictionary<string, IReadOnlyList<BandMapSeekStation>> _seekStations = new(StringComparer.OrdinalIgnoreCase);
  // Serializes seek-station file writes so the last write is of the latest list (taken under _lock
  // inside it); never taken while holding _lock.
  private readonly object _seekSaveLock = new();
  private BandSweepOutcome? _last;
  private SweepRun? _running;
  private Task _sweepTask = Task.CompletedTask;
  // Last invalid SamplesPerMeasurement value warned about; null before any warning.
  private int? _warnedSamplesPerMeasurement;

  /// <summary>Creates the service and loads every stored map of a band that has a sweep plan.</summary>
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
  /// <param name="currentTuning">
  /// Returns the radio's current band and frequency, or null when there is no radio source. When
  /// omitted, or when it returns null or throws, the current band is FM.
  /// </param>
  public BandMapService(
    ILogger<BandMapService> logger,
    IOptionsMonitor<BandMapOptions> options,
    BandMapStore store,
    SdrDeviceGate gate,
    Func<ILiveBandSweeper?> liveSweeper,
    Func<ISleepService?>? sleepService = null,
    Func<ISdrDevice?>? deviceFactory = null,
    TimeProvider? timeProvider = null,
    Func<BandMapTuning?>? currentTuning = null)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _store = store ?? throw new ArgumentNullException(nameof(store));
    _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    _liveSweeper = liveSweeper ?? throw new ArgumentNullException(nameof(liveSweeper));
    _sleepService = sleepService ?? (() => null);
    _deviceFactory = deviceFactory ?? CreateFirstRtlSdrOrNull;
    _currentTuning = currentTuning ?? (() => null);
    _time = timeProvider ?? TimeProvider.System;

    foreach (string band in MappableBands())
    {
      BandMap? map = _store.Load(band);
      if (map == null)
      {
        continue;
      }

      // The file name decides the band; a map whose Band field disagrees is filed under its file.
      _maps[band] = string.Equals(map.Band, band, StringComparison.OrdinalIgnoreCase) ? map : map with { Band = band };
      _logger.LogInformation("Loaded {Band} band map from {Path} ({Channels} channels, scanned {ScannedAt:u})",
        band, _store.GetFilePath(band), map.Channels.Count, map.ScannedAtUtc);
    }

    foreach (string band in MappableBands())
    {
      BandMapSeekStations? seek = _store.LoadSeekStations(band);
      if (seek == null || seek.Stations.Count == 0)
      {
        continue;
      }

      // One entry per frequency, ascending, whatever the file holds; the latest observation wins.
      _seekStations[band] = seek.Stations
        .GroupBy(s => s.FrequencyHz)
        .Select(g => g.OrderByDescending(s => s.ObservedAtUtc).First())
        .OrderBy(s => s.FrequencyHz)
        .ToArray();
      _logger.LogInformation("Loaded {Count} seek-observed {Band} stations from {Path}",
        _seekStations[band].Count, band, _store.GetSeekFilePath(band));
    }
  }

  /// <summary>
  /// The seek-observed stations of <paramref name="band"/> (AUD-100), ascending by frequency; empty
  /// when there are none.
  /// </summary>
  /// <param name="band">A band code accepted by <see cref="BandSweepPlans.TryParseBandCode"/>.</param>
  /// <exception cref="ArgumentException"><paramref name="band"/> is not a band code.</exception>
  public IReadOnlyList<BandMapSeekStation> GetSeekStations(string band)
  {
    string code = BandSweepPlans.BandCode(ParseBand(band));
    lock (_lock)
    {
      return _seekStations.GetValueOrDefault(code) ?? Array.Empty<BandMapSeekStation>();
    }
  }

  /// <inheritdoc/>
  /// <remarks>
  /// The list is the swept map's peaks (<see cref="BandMapStations.Peaks"/>, the BAND view's tap rule)
  /// plus the band's seek-observed stations inside the map's range that are at least one channel
  /// spacing from every peak and from each other (the stronger seek reading kept). A peak always
  /// wins over a seek-observed station near it. The map's range is the one it recorded, or for a map
  /// that recorded none (an AUD-76 file) the band's current plan.
  /// </remarks>
  public ScanStationList? GetScanStations(string band, long frequencyHz, out string? whyNot)
  {
    int maxAgeMinutes = _options.CurrentValue.ScanMapMaxAgeMinutes;
    if (maxAgeMinutes <= 0)
    {
      whyNot = ScanReasonDisabled;
      return null;
    }

    if (!BandSweepPlans.TryParseBandCode(band, out BandType bandType) || BandSweepPlans.UnavailableReason(bandType) != null)
    {
      whyNot = ScanReasonNotMappable;
      return null;
    }

    string code = BandSweepPlans.BandCode(bandType);
    BandMap? map;
    IReadOnlyList<BandMapSeekStation> seek;
    lock (_lock)
    {
      map = _maps.GetValueOrDefault(code);
      seek = _seekStations.GetValueOrDefault(code) ?? Array.Empty<BandMapSeekStation>();
    }

    if (map == null || map.Channels.Count == 0)
    {
      whyNot = ScanReasonNoMap;
      return null;
    }

    // A map from the future (the clock went back) counts as fresh.
    if (_time.GetUtcNow() - map.ScannedAtUtc > TimeSpan.FromMinutes(maxAgeMinutes))
    {
      whyNot = ScanReasonStale;
      return null;
    }

    BandSweepPlan? plan = BandSweepPlans.For(bandType, frequencyHz);
    bool storedRange = map.RangeMinHz > 0 && map.RangeMaxHz > map.RangeMinHz;
    long rangeMin = storedRange ? map.RangeMinHz : plan?.DisplayMinHz ?? 0;
    long rangeMax = storedRange ? map.RangeMaxHz : plan?.DisplayMaxHz ?? 0;
    if (frequencyHz < rangeMin || frequencyHz > rangeMax)
    {
      whyNot = ScanReasonOutsideMap;
      return null;
    }

    long spacing = map.ChannelSpacingHz > 0 ? map.ChannelSpacingHz : plan?.ChannelSpacingHz ?? 0;
    List<long> stations = new(BandMapStations.Peaks(map.Channels));
    foreach (BandMapSeekStation s in seek.OrderByDescending(s => s.SeekStrength))
    {
      bool inRange = s.FrequencyHz >= rangeMin && s.FrequencyHz <= rangeMax;
      if (inRange && stations.All(hz => Math.Abs(hz - s.FrequencyHz) >= Math.Max(1, spacing)))
      {
        stations.Add(s.FrequencyHz);
      }
    }

    stations.Sort();
    long minGap = spacing / 2;
    if (RTLSDRCore.StationListScan.Next(stations, frequencyHz, ascending: true, minGap) == null)
    {
      whyNot = ScanReasonNoOtherStation;
      return null;
    }

    whyNot = null;
    return new ScanStationList(stations, minGap, map.ScannedAtUtc);
  }

  /// <inheritdoc/>
  /// <remarks>
  /// Upserts by exact frequency: a second stop on the same frequency replaces the first entry's
  /// reading and time. The band's stations are written to <see cref="BandMapStore.GetSeekFilePath"/>;
  /// a failed write is logged and the station kept in memory.
  /// </remarks>
  public void RecordSeekStation(string band, long frequencyHz, float seekStrength)
  {
    if (!BandSweepPlans.TryParseBandCode(band, out BandType bandType) || BandSweepPlans.UnavailableReason(bandType) != null)
    {
      _logger.LogDebug("Seek stop at {FrequencyHz} Hz on band {Band} not recorded: the band is not mappable", frequencyHz, band);
      return;
    }

    string code = BandSweepPlans.BandCode(bandType);
    BandMapSeekStation station = new(frequencyHz, seekStrength, _time.GetUtcNow());
    lock (_lock)
    {
      IReadOnlyList<BandMapSeekStation> current = _seekStations.GetValueOrDefault(code) ?? Array.Empty<BandMapSeekStation>();
      _seekStations[code] = current
        .Where(s => s.FrequencyHz != frequencyHz)
        .Append(station)
        .OrderBy(s => s.FrequencyHz)
        .ToArray();
    }

    _logger.LogInformation("Seek-observed {Band} station recorded at {Frequency} MHz (seek strength {Strength:F3})",
      code, (frequencyHz / 1_000_000.0).ToString("F4", CultureInfo.InvariantCulture), seekStrength);
    SaveSeekStations(code);
  }

  /// <summary>
  /// Drops <paramref name="band"/>'s seek-observed stations from <paramref name="minHz"/> to
  /// <paramref name="maxHz"/> inclusive, after a completed sweep of that range; returns how many.
  /// Must be called holding <c>_lock</c>.
  /// </summary>
  private int DropSeekStationsLocked(string band, long minHz, long maxHz)
  {
    IReadOnlyList<BandMapSeekStation>? current = _seekStations.GetValueOrDefault(band);
    if (current == null || current.Count == 0)
    {
      return 0;
    }

    BandMapSeekStation[] kept = current.Where(s => s.FrequencyHz < minHz || s.FrequencyHz > maxHz).ToArray();
    _seekStations[band] = kept;
    return current.Count - kept.Length;
  }

  /// <summary>Writes <paramref name="band"/>'s current seek-observed stations; logs and keeps going on failure.</summary>
  private void SaveSeekStations(string band)
  {
    lock (_seekSaveLock)
    {
      IReadOnlyList<BandMapSeekStation> snapshot;
      lock (_lock)
      {
        snapshot = _seekStations.GetValueOrDefault(band) ?? Array.Empty<BandMapSeekStation>();
      }

      try
      {
        _store.SaveSeekStations(new BandMapSeekStations { Band = band, Stations = snapshot });
      }
      catch (Exception ex)
      {
        // Any failure, not only IO: this runs on the scan thread and at the end of Finish, and
        // neither may be ended by a file that could not be written.
        _logger.LogWarning(ex, "Seek stations could not be written to {Path}; keeping them in memory only", _store.GetSeekFilePath(band));
      }
    }
  }

  /// <summary>
  /// Band code of the radio's current band, or <c>"FM"</c> when it is not known. See the class remarks.
  /// </summary>
  public string CurrentBand => BandSweepPlans.BandCode(ResolveTuning().Band);

  /// <summary>The stored map of <see cref="CurrentBand"/>, or null before its first completed sweep.</summary>
  public BandMap? CurrentMap => GetMap(CurrentBand);

  /// <summary>The stored map of <paramref name="band"/>, or null before its first completed sweep.</summary>
  /// <param name="band">A band code accepted by <see cref="BandSweepPlans.TryParseBandCode"/>.</param>
  /// <exception cref="ArgumentException"><paramref name="band"/> is not a band code.</exception>
  public BandMap? GetMap(string band)
  {
    string code = BandSweepPlans.BandCode(ParseBand(band));
    lock (_lock)
    {
      return _maps.GetValueOrDefault(code);
    }
  }

  /// <summary>
  /// The plan a sweep of <paramref name="band"/> would use now, or null when the band has none.
  /// </summary>
  /// <param name="band">A band code accepted by <see cref="BandSweepPlans.TryParseBandCode"/>.</param>
  /// <exception cref="ArgumentException"><paramref name="band"/> is not a band code.</exception>
  public BandSweepPlan? GetPlan(string band) => PlanFor(ParseBand(band));

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
  /// Starts a sweep of <paramref name="band"/> for an explicit request, unless one is already
  /// running. A band with no sweep plan is <see cref="BandSweepRequestOutcome.Unavailable"/> with
  /// <see cref="ReasonBandNotReceivable"/>, whether or not a sweep is running. While a sweep is
  /// running, a request for the same band is <see cref="BandSweepRequestOutcome.AlreadyRunning"/>
  /// and a request for another band is <see cref="BandSweepRequestOutcome.Unavailable"/> with
  /// <see cref="ReasonOtherBandSweeping"/> (the running band is <see cref="BandSweepStatus.Band"/>
  /// of the result's status). Neither of these refusals is recorded as the last outcome.
  /// </summary>
  /// <remarks>
  /// A request for VHF while the radio is not on VHF sweeps the plan <see cref="BandSweepPlans.For"/>
  /// builds without a frequency: the band's lowest 2 MHz (30 to 32 MHz).
  /// </remarks>
  /// <param name="band">A band code; null for <see cref="CurrentBand"/>.</param>
  /// <exception cref="ArgumentException"><paramref name="band"/> is not null and not a band code.</exception>
  public BandSweepRequestResult RequestSweep(string? band = null)
  {
    BandSweepPlan? plan = PlanFor(band == null ? ResolveTuning().Band : ParseBand(band));
    BandMapOptions options = _options.CurrentValue;
    lock (_lock)
    {
      if (plan == null)
      {
        return new BandSweepRequestResult(BandSweepRequestOutcome.Unavailable, ReasonBandNotReceivable, BuildStatusLocked());
      }

      if (_running != null)
      {
        // A running sweep of another band does not satisfy this request; the caller must be
        // told that nothing was started for the band it asked for.
        return string.Equals(_running.Plan.Band, plan.Band, StringComparison.OrdinalIgnoreCase)
          ? new BandSweepRequestResult(BandSweepRequestOutcome.AlreadyRunning, null, BuildStatusLocked())
          : new BandSweepRequestResult(BandSweepRequestOutcome.Unavailable, ReasonOtherBandSweeping, BuildStatusLocked());
      }

      if (!options.Enabled)
      {
        return new BandSweepRequestResult(BandSweepRequestOutcome.Unavailable, ReasonDisabled, BuildStatusLocked());
      }

      string? reason = TryStartLocked(isRequest: true, options, plan);
      if (reason != null)
      {
        _last = SkippedOutcome(BandSweepTriggers.Request, reason, plan.Band);
        _logger.LogInformation("{Band} band sweep requested but not started: {Reason}", plan.Band, reason);
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
      _logger.LogInformation("Band map sweeps are disabled (BandMap:Enabled=false)");
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
    BandType band = ResolveTuning().Band;
    BandSweepPlan? plan = PlanFor(band);
    lock (_lock)
    {
      if (_stopping)
      {
        return;
      }

      // Enabled is re-read on every evaluation: switching it off at runtime stops timer
      // sweeps; the timer keeps evaluating so switching it back on resumes them.
      if (!options.Enabled)
      {
        _logger.LogDebug("Band map timer: sweeps are disabled (BandMap:Enabled=false)");
        ArmTimerLocked(interval);
        return;
      }

      if (_running != null)
      {
        // Re-armed when the running sweep finishes.
        _logger.LogDebug("Band map timer: a sweep is already running");
        return;
      }

      if (plan == null)
      {
        string code = BandSweepPlans.BandCode(band);
        _last = SkippedOutcome(BandSweepTriggers.Timer, ReasonBandNotReceivable, code);
        _logger.LogDebug("Band map timer: the {Band} band has no sweep plan; skipped", code);
        ArmTimerLocked(interval);
        return;
      }

      DateTimeOffset now = _time.GetUtcNow();
      BandMap? map = _maps.GetValueOrDefault(plan.Band);
      if (map != null && !CoversPlan(map, plan))
      {
        // The radio moved its VHF window away from the stored one: the stored map no longer
        // shows where the radio is, so it is due whatever its age.
        _logger.LogDebug("Band map timer: the stored {Band} map does not cover the radio's window; due", plan.Band);
      }
      else if (map != null && now - map.ScannedAtUtc < interval)
      {
        TimeSpan untilDue = map.ScannedAtUtc + interval - now;
        _logger.LogDebug("Band map timer: {Band} map is {Age} old, next evaluation in {UntilDue}", plan.Band, now - map.ScannedAtUtc, untilDue);
        ArmTimerLocked(untilDue);
        return;
      }

      string? reason = TryStartLocked(isRequest: false, options, plan);
      if (reason != null)
      {
        _last = SkippedOutcome(BandSweepTriggers.Timer, reason, plan.Band);
        _logger.LogDebug("Band map timer: {Band} sweep skipped ({Reason})", plan.Band, reason);
        ArmTimerLocked(interval);
      }
    }
  }

  /// <summary>
  /// Picks a path and starts the sweep. Must be called holding <c>_lock</c> with no sweep running.
  /// </summary>
  /// <returns>Null when a sweep was started; otherwise why not.</returns>
  private string? TryStartLocked(bool isRequest, BandMapOptions options, BandSweepPlan plan)
  {
    int samplesPerMeasurement = EffectiveSamplesPerMeasurementLocked(options);
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

      SweepRun idleRun = BeginRunLocked(isRequest ? BandSweepTriggers.Request : BandSweepTriggers.Timer, BandSweepPaths.Idle, plan);
      _sweepTask = Task.Run(() => RunIdle(idleRun, lease, device, options, samplesPerMeasurement));
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

    SweepRun liveRun = BeginRunLocked(isRequest ? BandSweepTriggers.Request : BandSweepTriggers.Sleep, BandSweepPaths.Live, plan);
    _sweepTask = Task.Run(() => RunLiveAsync(liveRun, live, options, samplesPerMeasurement));
    return null;
  }

  /// <summary>
  /// <see cref="BandMapOptions.SamplesPerMeasurement"/>, or
  /// <see cref="BandSweeper.DefaultSamplesPerMeasurement"/> when the configured value fails
  /// <see cref="BandSweeper.IsValidSamplesPerMeasurement"/>. Warns once per distinct invalid
  /// value. Must be called holding <c>_lock</c>.
  /// </summary>
  private int EffectiveSamplesPerMeasurementLocked(BandMapOptions options)
  {
    int configured = options.SamplesPerMeasurement;
    if (BandSweeper.IsValidSamplesPerMeasurement(configured))
    {
      return configured;
    }

    if (_warnedSamplesPerMeasurement != configured)
    {
      _warnedSamplesPerMeasurement = configured;
      _logger.LogWarning(
        "BandMap:SamplesPerMeasurement {Configured} is invalid (must be at least {Minimum} and a multiple of {Multiple}); using {Default}",
        configured, ChannelPowerMeter.FftSize, BandSweeper.SamplesPerMeasurementMultiple, BandSweeper.DefaultSamplesPerMeasurement);
    }
    return BandSweeper.DefaultSamplesPerMeasurement;
  }

  private SweepRun BeginRunLocked(string trigger, string path, BandSweepPlan plan)
  {
    SweepRun run = new(trigger, path, _time.GetUtcNow(), _time.GetTimestamp(), plan);
    _running = run;
    _logger.LogInformation("Band sweep started (band {Band}, trigger {Trigger}, path {Path})", plan.Band, trigger, path);
    return run;
  }

  private void RunIdle(SweepRun run, SdrDeviceGate.SweepLease lease, ISdrDevice device, BandMapOptions options, int samplesPerMeasurement)
  {
    IReadOnlyList<ChannelLevel>? levels = null;
    string result = BandSweepResults.Failed;
    string? reason = null;
    Exception? failure = null;

    // Anything escaping the body still reaches Finish, which clears _running and re-arms the timer.
    try
    {
      CancellationToken leaseToken = lease.Token;
      try
      {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(leaseToken, _stopCts.Token);
        try
        {
          // Declared inside the try so the device is closed and disposed before
          // the lease is released below.
          using DeviceSweepTuner tuner = new(device, options.SweepGainDb);
          // A radio claim may already have cancelled the lease: do not open the device then.
          linked.Token.ThrowIfCancellationRequested();
          tuner.Open();
          levels = BandSweeper.Sweep(tuner, run.Plan, samplesPerMeasurement, run, linked.Token);
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
          reason = ExceptionReason(ex);
          failure = ex;
        }
      }
      finally
      {
        lease.Dispose();
      }
    }
    catch (Exception ex)
    {
      levels = null;
      result = BandSweepResults.Failed;
      reason = ExceptionReason(ex);
      failure = ex;
    }

    Finish(run, result, reason, levels, failure, options);
  }

  private async Task RunLiveAsync(SweepRun run, ILiveBandSweeper live, BandMapOptions options, int samplesPerMeasurement)
  {
    IReadOnlyList<ChannelLevel>? levels = null;
    string result = BandSweepResults.Failed;
    string? reason = null;
    Exception? failure = null;
    bool woke = false;

    // Anything escaping the body still reaches Finish, which clears _running and re-arms the timer.
    try
    {
      using CancellationTokenSource runCts = CancellationTokenSource.CreateLinkedTokenSource(_stopCts.Token);
      if (run.Trigger == BandSweepTriggers.Sleep)
      {
        // A sleep-triggered sweep must not outlive the sleep. Waking does not
        // necessarily resume the source (SleepService resumes it only when it
        // was playing before sleep and is paused), so the source's own cancel
        // on resume is not enough: check IsSleeping after every progress report (every channel
        // for FM, every tune for a grouped plan).
        run.AfterChannel = () =>
        {
          if (_sleepService()?.IsSleeping == false && !woke)
          {
            woke = true;
            _logger.LogInformation("Console woke during a sleep-triggered band sweep; cancelling it");
            runCts.Cancel();
          }
        };
      }

      try
      {
        levels = await live.SweepLiveAsync(
          run.Plan, options.SweepGainDb, samplesPerMeasurement, run, runCts.Token).ConfigureAwait(false);
        result = BandSweepResults.Completed;
      }
      catch (OperationCanceledException)
      {
        // The receiver cancels its own sweep on a user tune, band change, gain change, seek
        // scan, source resume or shutdown; this service cancels it on stop and, for a
        // sleep-triggered sweep, once IsSleeping reads false after a progress report.
        result = BandSweepResults.Cancelled;
        reason = woke ? "woke" : _stopCts.IsCancellationRequested ? "service-stopping" : "interrupted";
      }
      catch (Exception ex)
      {
        result = BandSweepResults.Failed;
        reason = ExceptionReason(ex);
        failure = ex;
      }
      finally
      {
        run.AfterChannel = null;
      }
    }
    catch (Exception ex)
    {
      levels = null;
      result = BandSweepResults.Failed;
      reason = ExceptionReason(ex);
      failure = ex;
    }

    Finish(run, result, reason, levels, failure, options);
  }

  /// <summary>
  /// <see cref="BandSweepOutcome.Reason"/> for a failure. Only the exception type: the
  /// outcome is served by an unauthenticated endpoint, and the message goes to the log.
  /// </summary>
  private static string ExceptionReason(Exception ex) => $"exception:{ex.GetType().Name}";

  /// <summary>
  /// Records the outcome, stores a completed map, clears the running sweep and re-arms the
  /// timer. The last three happen even when building or storing the result throws.
  /// </summary>
  private void Finish(
    SweepRun run, string result, string? reason, IReadOnlyList<ChannelLevel>? levels, Exception? failure, BandMapOptions options)
  {
    TimeSpan duration = TimeSpan.Zero;
    int measured = 0;
    int seekDropped = 0;
    BandMap? newMap = null;
    BandSweepOutcome outcome;
    try
    {
      duration = _time.GetElapsedTime(run.StartTimestamp);
      measured = levels?.Count ?? 0;
      if (result == BandSweepResults.Completed && measured == 0)
      {
        result = BandSweepResults.Failed;
        reason = "no-channels-measured";
      }

      if (result == BandSweepResults.Completed)
      {
        BandMap map = new()
        {
          Band = run.Plan.Band,
          RangeMinHz = run.Plan.DisplayMinHz,
          RangeMaxHz = run.Plan.DisplayMaxHz,
          ChannelSpacingHz = run.Plan.ChannelSpacingHz,
          ScannedAtUtc = _time.GetUtcNow(),
          Channels = levels!.Select(l => new BandMapChannel(l.FrequencyHz, l.LevelDbfs)).ToArray(),
          Trigger = run.Trigger,
          Path = run.Path,
        };

        try
        {
          _store.Save(map);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
          _logger.LogWarning(ex, "Band map could not be written to {Path}; keeping it in memory only", _store.GetFilePath(map.Band));
        }
        newMap = map;
      }
    }
    catch (Exception ex)
    {
      newMap = null;
      result = BandSweepResults.Failed;
      reason = ExceptionReason(ex);
      failure = ex;
    }
    finally
    {
      outcome = new BandSweepOutcome
      {
        Band = run.Plan.Band,
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
          _maps[newMap.Band] = newMap;
          // AUD-100: the sweep measured every channel of its range, so it is the authority there;
          // seek-observed stations outside it (another VHF window) are kept.
          seekDropped = DropSeekStationsLocked(newMap.Band, newMap.RangeMinHz, newMap.RangeMaxHz);
        }
        _last = outcome;
        _running = null;
        if (!_stopping)
        {
          ArmTimerLocked(RescanInterval(options));
        }
      }
    }

    if (seekDropped > 0)
    {
      _logger.LogInformation("{Band} sweep replaced {Count} seek-observed stations", run.Plan.Band, seekDropped);
      SaveSeekStations(run.Plan.Band);
    }

    if (result == BandSweepResults.Failed)
    {
      _logger.LogWarning(failure,
        "Band sweep finished: {Result} ({Reason}); band {Band}, trigger {Trigger}, path {Path}, {Measured} channels measured in {DurationMs} ms",
        result, reason, run.Plan.Band, run.Trigger, run.Path, measured, outcome.DurationMs);
    }
    else
    {
      // One decimal is enough on the 200 kHz FM grid; the narrowband grids need four.
      string format = run.Plan.ChannelSpacingHz >= 100_000 ? "F1" : "F4";
      string strongest = levels == null
        ? "none"
        : string.Join(", ", levels.OrderByDescending(l => l.LevelDbfs).Take(3)
          .Select(l => $"{(l.FrequencyHz / 1_000_000.0).ToString(format, CultureInfo.InvariantCulture)} MHz {l.LevelDbfs:F1} dB"));
      _logger.LogInformation(
        "Band sweep finished: {Result}{Reason}; band {Band}, trigger {Trigger}, path {Path}, {Measured} channels measured in {DurationMs} ms; strongest: {Strongest}",
        result, reason == null ? string.Empty : $" ({reason})", run.Plan.Band, run.Trigger, run.Path, measured, outcome.DurationMs, strongest);
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
      Band = run.Plan.Band,
      Trigger = run.Trigger,
      Path = run.Path,
      Progress = progress,
      StartedAtUtc = run.StartedAtUtc,
      EstimatedSecondsRemaining = remaining,
      Last = _last,
    };
  }

  private BandSweepOutcome SkippedOutcome(string trigger, string reason, string band) => new()
  {
    Band = band,
    Trigger = trigger,
    StartedAtUtc = _time.GetUtcNow(),
    Result = BandSweepResults.Skipped,
    Reason = reason,
  };

  private void ArmTimerLocked(TimeSpan due)
  {
    _timer?.Change(ClampDue(due), Timeout.InfiniteTimeSpan);
  }

  /// <summary>
  /// False when <paramref name="map"/> recorded its range and that range does not contain the
  /// centre of <paramref name="plan"/>'s display range. Only the VHF plan moves (its window follows
  /// the radio), so in practice this is false only for a VHF map of a window the radio has left.
  /// A map without a recorded range (an AUD-76 file) is treated as covering.
  /// </summary>
  private static bool CoversPlan(BandMap map, BandSweepPlan plan)
  {
    if (map.RangeMinHz <= 0 || map.RangeMaxHz <= map.RangeMinHz)
    {
      return true;
    }

    long centre = plan.DisplayMinHz + (plan.DisplayMaxHz - plan.DisplayMinHz) / 2;
    return centre >= map.RangeMinHz && centre <= map.RangeMaxHz;
  }

  private static TimeSpan ClampDue(TimeSpan due) => due < MinimumTimerDue ? MinimumTimerDue : due;

  private static TimeSpan RescanInterval(BandMapOptions options) =>
    TimeSpan.FromMinutes(Math.Max(1, options.RescanIntervalMinutes));

  /// <summary>
  /// The radio's band and frequency from the <c>currentTuning</c> delegate; FM with no frequency
  /// when it returns null, throws, or names no known band.
  /// </summary>
  private (BandType Band, long? FrequencyHz) ResolveTuning()
  {
    BandMapTuning? tuning;
    try
    {
      tuning = _currentTuning();
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Band map: reading the radio's tuning failed; using FM");
      return (BandType.FM, null);
    }

    return tuning != null && BandSweepPlans.TryParseBandCode(tuning.Band, out BandType band)
      ? (band, tuning.FrequencyHz)
      : (BandType.FM, null);
  }

  /// <summary>
  /// <paramref name="band"/>'s plan. The radio's frequency is passed only while the radio is on
  /// that band, so the VHF window follows the radio only on VHF.
  /// </summary>
  private BandSweepPlan? PlanFor(BandType band)
  {
    (BandType current, long? frequencyHz) = ResolveTuning();
    return BandSweepPlans.For(band, current == band ? frequencyHz : null);
  }

  private static BandType ParseBand(string band)
  {
    ArgumentNullException.ThrowIfNull(band);
    return BandSweepPlans.TryParseBandCode(band, out BandType parsed)
      ? parsed
      : throw new ArgumentException($"'{band}' is not a band code ({string.Join(", ", BandSweepPlans.BandCodes)}).", nameof(band));
  }

  private static IEnumerable<string> MappableBands() =>
    BandSweepPlans.BandCodes.Where(code =>
      BandSweepPlans.TryParseBandCode(code, out BandType band) && BandSweepPlans.UnavailableReason(band) == null);

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

    public SweepRun(string trigger, string path, DateTimeOffset startedAtUtc, long startTimestamp, BandSweepPlan plan)
    {
      Trigger = trigger;
      Path = path;
      StartedAtUtc = startedAtUtc;
      StartTimestamp = startTimestamp;
      Plan = plan;
      ChannelsTotal = plan.Channels.Count;
    }

    public BandSweepPlan Plan { get; }

    public string Trigger { get; }

    public string Path { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public long StartTimestamp { get; }

    public int ChannelsTotal { get; }

    public int ChannelsDone => Volatile.Read(ref _channelsDone);

    /// <summary>Invoked after each progress report is recorded, on the reporting thread; null when unused.</summary>
    public Action? AfterChannel { get; set; }

    public void Report(BandSweepProgress value)
    {
      Volatile.Write(ref _channelsDone, value.ChannelsDone);
      AfterChannel?.Invoke();
    }
  }
}
