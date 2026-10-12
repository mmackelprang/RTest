using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Models;
using Radio.Core.Models.Audio;
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

  /// <summary>AUD-103: the receiving device; null is no device known (no device limit).</summary>
  private SdrHardwareInfo? Device { get; set; }

  private BandMapStore Store => new(_root, NullLogger.Instance);

  /// <summary>An RTL-SDR whose lower limit is <paramref name="minHz"/>: 100 kHz with HF direct sampling, 24 MHz without.</summary>
  internal static SdrHardwareInfo DeviceWithFloor(long minHz) => new(
    0, "Generic RTL2832U", "Realtek", "NESDR SMArt v5", "00000001", "R820T",
    minHz < RtlSdrDevice.TunerMinFrequencyHz ? "Q" : "Off", "Auto", false, false, minHz, RtlSdrDevice.TunerMaxFrequencyHz);

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
      () => Tuning,
      () => Device);
    _services.Add(service);
    return service;
  }

  /// <summary>
  /// Stores a map of <paramref name="band"/>'s plan (AM, or SW's segment around <paramref name="planHz"/>)
  /// at a flat -50 dB floor with <paramref name="stations"/> at the levels given.
  /// </summary>
  private void SeedHfMap(RTLSDRCore.Enums.BandType band, long? planHz, params (long Hz, float Level)[] stations)
  {
    BandSweepPlan plan = BandSweepPlans.For(band, planHz)!;
    Store.Save(new BandMap
    {
      Band = plan.Band,
      RangeMinHz = plan.DisplayMinHz,
      RangeMaxHz = plan.DisplayMaxHz,
      ChannelSpacingHz = plan.ChannelSpacingHz,
      ScannedAtUtc = Now,
      Channels = plan.Channels
        .Select(hz => new BandMapChannel(hz, stations.FirstOrDefault(s => s.Hz == hz) is { Hz: > 0 } s ? s.Level : -50f))
        .ToArray(),
      Trigger = BandSweepTriggers.Request,
      Path = BandSweepPaths.Idle,
    });
  }

  [Fact]
  public void GetScanStations_OnAm_HopsBetweenItsPeaks_ButNeverTheCrystalSpur()
  {
    // AUD-103, the box's night pattern on the in-channel scale: the 960 kHz spur is the strongest
    // channel, 970 carries the 968 kHz carrier, 1030 is skywave. Floor -50.
    Device = DeviceWithFloor(HfDirectSampling.HfMinFrequencyHz);
    Tuning = new BandMapTuning("AM", 1_000_000);
    SeedHfMap(RTLSDRCore.Enums.BandType.AM, null, (960_000, -34f), (970_000, -43f), (1_030_000, -42f), (680_000, -43f));
    BandMapService service = CreateService();

    ScanStationList list = Assert.IsType<ScanStationList>(service.GetScanStations("AM", 1_000_000, out string? whyNot));

    Assert.Null(whyNot);
    // 970 is a peak although its neighbour 960 is higher: the spur is left out before the neighbour rule.
    Assert.Equal(new long[] { 680_000, 970_000, 1_030_000 }, list.StationsHz);
    Assert.Equal(5_000, list.MinGapHz);
  }

  [Fact]
  public void GetScanStations_OnSw_IsTheStoredSegmentsPeaks_AndOutsideItSeeksLive()
  {
    Device = DeviceWithFloor(HfDirectSampling.HfMinFrequencyHz);
    Tuning = new BandMapTuning("SW", 9_600_000);
    SeedHfMap(RTLSDRCore.Enums.BandType.Shortwave, 9_600_000, (9_580_000, -40f), (9_700_000, -38f));
    BandMapService service = CreateService();

    ScanStationList list = service.GetScanStations("SW", 9_600_000, out _)!;
    Assert.Equal(new long[] { 9_580_000, 9_700_000 }, list.StationsHz);
    Assert.Equal(2_500, list.MinGapHz);

    // 49 m is another segment: the 31 m map does not describe it.
    Assert.Null(service.GetScanStations("SW", 6_000_000, out string? whyNot));
    Assert.Equal(BandMapService.ScanReasonOutsideMap, whyNot);
  }

  [Fact]
  public void GetScanStations_ASeekStopOnTheSpur_IsNotAStation()
  {
    Device = DeviceWithFloor(HfDirectSampling.HfMinFrequencyHz);
    Tuning = new BandMapTuning("AM", 1_000_000);
    SeedHfMap(RTLSDRCore.Enums.BandType.AM, null, (1_030_000, -42f));
    BandMapService service = CreateService();
    service.RecordSeekStation("AM", 960_000, 0.5f);
    service.RecordSeekStation("AM", 1_400_000, 0.3f);

    ScanStationList list = service.GetScanStations("AM", 700_000, out _)!;

    // Recorded (the seek did stop there), but not hopped to.
    Assert.Equal(new long[] { 960_000, 1_400_000 }, service.GetSeekStations("AM").Select(s => s.FrequencyHz));
    Assert.Equal(new long[] { 1_030_000, 1_400_000 }, list.StationsHz);
  }

  [Fact]
  public void GetScanStations_OnAFlatAmMap_SeeksLive()
  {
    // On-box UAT: Scan on AM with a flat map neither hangs nor crashes; it falls back to live seek.
    Device = DeviceWithFloor(HfDirectSampling.HfMinFrequencyHz);
    Tuning = new BandMapTuning("AM", 1_000_000);
    SeedHfMap(RTLSDRCore.Enums.BandType.AM, null, (960_000, -34f));
    BandMapService service = CreateService();

    Assert.Null(service.GetScanStations("AM", 1_000_000, out string? whyNot));
    Assert.Equal(BandMapService.ScanReasonNoOtherStation, whyNot);
  }

  [Theory]
  [InlineData("AM", 1_000_000L)]
  [InlineData("SW", 9_600_000L)]
  public void RecordSeekStation_OnAnHfBand_WithAnHfDevice_IsRecorded(string band, long hz)
  {
    Device = DeviceWithFloor(HfDirectSampling.HfMinFrequencyHz);
    BandMapService service = CreateService();

    service.RecordSeekStation(band, hz, 0.4f);

    Assert.Equal(hz, Assert.Single(service.GetSeekStations(band)).FrequencyHz);
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
    // AUD-103: AM and SW are unmappable on a device without HF direct sampling.
    Device = DeviceWithFloor(RtlSdrDevice.TunerMinFrequencyHz);
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
    Device = DeviceWithFloor(RtlSdrDevice.TunerMinFrequencyHz);
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
