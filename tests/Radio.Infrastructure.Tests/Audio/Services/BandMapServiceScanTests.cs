using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Models;
using Radio.Infrastructure.Audio.Services;
using RTLSDRCore.Hardware;
using RTLSDRCore.Sweep;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// AUD-100: <see cref="BandMapService"/> as Scan Up/Down's station list — what Scan may hop between
/// (<see cref="BandMapService.GetScanStations"/>) and the stations a live seek records
/// (<see cref="BandMapService.RecordSeekStation"/>). Maps are seeded as files, the clock is a
/// <see cref="FakeTimeProvider"/>, and the one real sweep is awaited through
/// <see cref="BandMapService.RunningSweep"/>.
/// </summary>
public sealed class BandMapServiceScanTests : IDisposable
{
  private static readonly TimeSpan FailSafe = TimeSpan.FromSeconds(30);
  private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

  // Three FM stations standing well above a flat -60 dB floor.
  private static readonly long[] FmStations = { 88_100_000, 92_300_000, 100_100_000 };

  private readonly string _root = Path.Combine(Path.GetTempPath(), "aud100-" + Guid.NewGuid().ToString("N"));
  private readonly FakeTimeProvider _time = new(Now);
  private readonly SdrDeviceGate _gate = new();
  private readonly FixedLiveSweeper _live = new();
  private readonly BandMapOptions _options = new()
  {
    InitialDelaySeconds = 120,
    RescanIntervalMinutes = 60,
    SweepGainDb = 28f,
    SamplesPerMeasurement = ChannelPowerMeter.FftSize,
    ScanMapMaxAgeMinutes = 1440,
  };
  private readonly List<BandMapService> _services = new();

  public void Dispose()
  {
    foreach (BandMapService service in _services)
    {
      service.Dispose();
    }
    if (Directory.Exists(_root))
    {
      Directory.Delete(_root, recursive: true);
    }
  }

  private BandMapTuning? Tuning { get; set; } = new("FM", 92_300_000);

  private BandMapStore Store => new(_root, NullLogger.Instance);

  private BandMapService CreateService()
  {
    Mock<IOptionsMonitor<BandMapOptions>> monitor = new();
    monitor.Setup(m => m.CurrentValue).Returns(_options);
    BandMapService service = new(
      NullLogger<BandMapService>.Instance,
      monitor.Object,
      Store,
      _gate,
      () => _live,
      () => new Mock<ISleepService>().Object,
      () => null,
      _time,
      () => Tuning);
    _services.Add(service);
    return service;
  }

  /// <summary>Stores an FM map swept <paramref name="age"/> ago with <paramref name="stations"/> at -30 dB.</summary>
  private void SeedFmMap(TimeSpan age, params long[] stations)
  {
    BandSweepPlan plan = BandSweepPlans.For(RTLSDRCore.Enums.BandType.FM, null)!;
    Store.Save(new BandMap
    {
      Band = "FM",
      RangeMinHz = plan.DisplayMinHz,
      RangeMaxHz = plan.DisplayMaxHz,
      ChannelSpacingHz = plan.ChannelSpacingHz,
      ScannedAtUtc = Now - age,
      Channels = plan.Channels.Select(hz => new BandMapChannel(hz, stations.Contains(hz) ? -30f : -60f)).ToArray(),
      Trigger = BandSweepTriggers.Timer,
      Path = BandSweepPaths.Idle,
    });
  }

  private string BandMapFile(string name) => Path.Combine(_root, "bandmap", name);

  [Fact]
  public void GetScanStations_FreshMap_IsItsPeaks_WithHalfASpacingAsTheGap()
  {
    SeedFmMap(TimeSpan.FromHours(3), FmStations);
    BandMapService service = CreateService();

    ScanStationList list = Assert.IsType<ScanStationList>(service.GetScanStations("FM", 92_300_000, out string? whyNot));

    Assert.Null(whyNot);
    Assert.Equal(FmStations, list.StationsHz);
    Assert.Equal(100_000, list.MinGapHz);
    Assert.Equal(Now - TimeSpan.FromHours(3), list.ScannedAtUtc);
  }

  [Fact]
  public void GetScanStations_AtExactlyTheMaxAge_IsFresh_AndOneTickOlder_IsStale()
  {
    SeedFmMap(TimeSpan.FromMinutes(1440), FmStations);
    BandMapService service = CreateService();

    Assert.NotNull(service.GetScanStations("FM", 92_300_000, out _));

    _time.Advance(TimeSpan.FromTicks(1));

    Assert.Null(service.GetScanStations("FM", 92_300_000, out string? whyNot));
    Assert.Equal(BandMapService.ScanReasonStale, whyNot);
  }

  [Fact]
  public void GetScanStations_MaxAgeZero_AlwaysSeeksLive()
  {
    _options.ScanMapMaxAgeMinutes = 0;
    SeedFmMap(TimeSpan.Zero, FmStations);
    BandMapService service = CreateService();

    Assert.Null(service.GetScanStations("FM", 92_300_000, out string? whyNot));
    Assert.Equal(BandMapService.ScanReasonDisabled, whyNot);
  }

  [Fact]
  public void GetScanStations_NoMap_SeeksLive_EvenWithSeekObservedStations()
  {
    BandMapService service = CreateService();
    service.RecordSeekStation("FM", 95_500_000, 0.9f);
    service.RecordSeekStation("FM", 101_100_000, 0.9f);

    // A seek proves presence, never absence: its stops alone are not a list to hop between.
    Assert.Null(service.GetScanStations("FM", 92_300_000, out string? whyNot));
    Assert.Equal(BandMapService.ScanReasonNoMap, whyNot);
  }

  [Theory]
  [InlineData("AM")]
  [InlineData("SW")]
  [InlineData("nonsense")]
  public void GetScanStations_UnmappableBand_SeeksLive(string band)
  {
    BandMapService service = CreateService();

    Assert.Null(service.GetScanStations(band, 1_000_000, out string? whyNot));
    Assert.Equal(BandMapService.ScanReasonNotMappable, whyNot);
  }

  [Fact]
  public void GetScanStations_FrequencyOutsideTheMapsRange_SeeksLive()
  {
    // A VHF map of the 146–148 MHz window does not describe 150 MHz.
    Store.Save(new BandMap
    {
      Band = "VHF",
      RangeMinHz = 146_000_000,
      RangeMaxHz = 148_000_000,
      ChannelSpacingHz = 12_500,
      ScannedAtUtc = Now,
      // Two stations over a noise floor (the median, which the peak rule measures from).
      Channels = Enumerable.Range(0, 161)
        .Select(i => 146_000_000L + i * 12_500L)
        .Select(hz => new BandMapChannel(hz, hz is 146_520_000 or 147_000_000 ? -20f : -60f))
        .ToArray(),
    });
    BandMapService service = CreateService();

    Assert.Null(service.GetScanStations("VHF", 150_000_000, out string? whyNot));
    Assert.Equal(BandMapService.ScanReasonOutsideMap, whyNot);
    Assert.NotNull(service.GetScanStations("VHF", 146_520_000, out _));
  }

  [Fact]
  public void GetScanStations_OnlyTheCurrentStationMapped_SeeksLive()
  {
    SeedFmMap(TimeSpan.Zero, 92_300_000);
    BandMapService service = CreateService();

    Assert.Null(service.GetScanStations("FM", 92_300_000, out string? whyNot));
    Assert.Equal(BandMapService.ScanReasonNoOtherStation, whyNot);
    // From anywhere else, that one station is somewhere to go.
    Assert.Equal(new long[] { 92_300_000 }, service.GetScanStations("FM", 100_000_000, out _)!.StationsHz);
  }

  [Fact]
  public void GetScanStations_AddsSeekObservedStations_ButAPeakWinsOverOneNearIt()
  {
    SeedFmMap(TimeSpan.Zero, FmStations);
    BandMapService service = CreateService();
    service.RecordSeekStation("FM", 95_500_000, 0.9f);   // nowhere near a peak: added
    service.RecordSeekStation("FM", 92_200_000, 1.1f);   // 100 kHz from the 92.3 peak: the peak wins
    service.RecordSeekStation("FM", 104_300_000, 0.4f);  // two seek stops 100 kHz apart: the stronger wins
    service.RecordSeekStation("FM", 104_400_000, 0.8f);

    ScanStationList list = service.GetScanStations("FM", 92_300_000, out _)!;

    Assert.Equal(new long[] { 88_100_000, 92_300_000, 95_500_000, 100_100_000, 104_400_000 }, list.StationsHz);
  }

  [Fact]
  public void RecordSeekStation_LeavesTheSweptMapItsTimeAndItsAgeExactlyAsTheyWere()
  {
    SeedFmMap(TimeSpan.FromHours(2), FmStations);
    BandMapService service = CreateService();
    BandMap before = service.GetMap("FM")!;
    TimeSpan? ageBefore = service.GetAge(before);
    byte[] fileBefore = File.ReadAllBytes(BandMapFile("fm.json"));

    service.RecordSeekStation("FM", 95_500_000, 0.9f);
    service.RecordSeekStation("FM", 92_300_000, 1.2f);   // on a swept station: overrides nothing

    Assert.Same(before, service.GetMap("FM"));
    Assert.Equal(Now - TimeSpan.FromHours(2), service.GetMap("FM")!.ScannedAtUtc);
    Assert.Equal(ageBefore, service.GetAge(service.GetMap("FM")));
    Assert.Equal(fileBefore, File.ReadAllBytes(BandMapFile("fm.json")));
    Assert.Equal(new long[] { 92_300_000, 95_500_000 }, service.GetSeekStations("FM").Select(s => s.FrequencyHz));
  }

  [Fact]
  public void RecordSeekStation_OnANeverSweptBand_LeavesItWithNoMap()
  {
    Tuning = new BandMapTuning("WB", 162_400_000);
    BandMapService service = CreateService();

    service.RecordSeekStation("WB", 162_475_000, 0.7f);

    Assert.Null(service.GetMap("WB"));
    Assert.Null(service.CurrentMap);
    Assert.Null(service.GetAge(service.GetMap("WB")));
    Assert.False(File.Exists(BandMapFile("wb.json")));
    BandMapSeekStation seek = Assert.Single(service.GetSeekStations("WB"));
    Assert.Equal(162_475_000, seek.FrequencyHz);
    Assert.Equal(0.7f, seek.SeekStrength);
    Assert.Equal(Now, seek.ObservedAtUtc);
  }

  [Fact]
  public void RecordSeekStation_UpsertsByFrequency_AndSurvivesARestart()
  {
    BandMapService service = CreateService();
    service.RecordSeekStation("FM", 101_100_000, 0.5f);
    _time.Advance(TimeSpan.FromMinutes(5));
    service.RecordSeekStation("FM", 95_500_000, 0.6f);
    service.RecordSeekStation("FM", 101_100_000, 0.9f);

    BandMapService restarted = CreateService();

    Assert.Equal(
      new[] { (95_500_000L, 0.6f, Now.AddMinutes(5)), (101_100_000L, 0.9f, Now.AddMinutes(5)) },
      restarted.GetSeekStations("FM").Select(s => (s.FrequencyHz, s.SeekStrength, s.ObservedAtUtc)));
    Assert.True(File.Exists(BandMapFile("fm-seek.json")));
  }

  [Theory]
  [InlineData("AM")]
  [InlineData("SW")]
  public void RecordSeekStation_OnAnUnmappableBand_RecordsNothing(string band)
  {
    BandMapService service = CreateService();

    service.RecordSeekStation(band, 1_000_000, 0.9f);

    Assert.Empty(service.GetSeekStations(band));
    Assert.False(Directory.Exists(Path.Combine(_root, "bandmap")));
  }

  [Fact]
  public async Task ACompletedSweep_ReplacesTheSeekObservedStationsInItsRange_OnDiskToo()
  {
    BandMapService service = CreateService();
    service.RecordSeekStation("FM", 95_500_000, 0.9f);
    service.RecordSeekStation("FM", 101_100_000, 0.9f);
    await _gate.ClaimForRadioAsync();

    Assert.Equal(BandSweepRequestOutcome.Started, service.RequestSweep("FM").Outcome);
    await service.RunningSweep.WaitAsync(FailSafe);

    Assert.NotNull(service.GetMap("FM"));
    Assert.Empty(service.GetSeekStations("FM"));
    Assert.Empty(CreateService().GetSeekStations("FM"));
  }

  [Fact]
  public async Task ACompletedVhfSweep_KeepsSeekObservedStationsOutsideItsWindow()
  {
    Tuning = new BandMapTuning("VHF", 146_520_000);
    BandMapService service = CreateService();
    service.RecordSeekStation("VHF", 146_520_000, 0.9f);   // inside the 145.52–147.52 window
    service.RecordSeekStation("VHF", 40_000_000, 0.9f);    // far outside it
    await _gate.ClaimForRadioAsync();

    Assert.Equal(BandSweepRequestOutcome.Started, service.RequestSweep("VHF").Outcome);
    await service.RunningSweep.WaitAsync(FailSafe);

    Assert.Equal(new long[] { 40_000_000 }, service.GetSeekStations("VHF").Select(s => s.FrequencyHz));
  }

  [Fact]
  public async Task ACancelledSweep_KeepsTheSeekObservedStations()
  {
    BandMapService service = CreateService();
    service.RecordSeekStation("FM", 95_500_000, 0.9f);
    await _gate.ClaimForRadioAsync();
    _live.Fail = new OperationCanceledException();

    service.RequestSweep("FM");
    await service.RunningSweep.WaitAsync(FailSafe);

    Assert.Equal(BandSweepResults.Cancelled, service.GetStatus().Last!.Result);
    Assert.Single(service.GetSeekStations("FM"));
  }

  /// <summary>A live sweeper that measures every channel at -50 dB at once, or throws <see cref="Fail"/>.</summary>
  private sealed class FixedLiveSweeper : ILiveBandSweeper
  {
    public bool CanSweepLive => true;

    public Exception? Fail { get; set; }

    public Task<IReadOnlyList<ChannelLevel>> SweepLiveAsync(
      IReadOnlyList<long> channels, float gainDb, int samplesPerMeasurement,
      IProgress<BandSweepProgress>? progress, CancellationToken cancellationToken) =>
      Fail != null
        ? Task.FromException<IReadOnlyList<ChannelLevel>>(Fail)
        : Task.FromResult<IReadOnlyList<ChannelLevel>>(channels.Select(hz => new ChannelLevel(hz, -50f)).ToArray());

    public Task<IReadOnlyList<ChannelLevel>> SweepLiveAsync(
      BandSweepPlan plan, float gainDb, int samplesPerMeasurement,
      IProgress<BandSweepProgress>? progress, CancellationToken cancellationToken) =>
      SweepLiveAsync(plan.Channels, gainDb, samplesPerMeasurement, progress, cancellationToken);
  }
}
