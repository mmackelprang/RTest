using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Models;
using Radio.Infrastructure.Audio.Services;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;
using RTLSDRCore.Sweep;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// AUD-91: <see cref="BandMapService"/> sweeps, stores and reports per band. Same discipline as
/// <see cref="BandMapServiceTests"/>: a <see cref="FakeTimeProvider"/>, and every sweep awaited
/// through <see cref="BandMapService.RunningSweep"/>.
/// </summary>
public sealed class BandMapServiceBandTests : IDisposable
{
  private const long WeatherStationHz = 162_475_000;
  private static readonly TimeSpan FailSafe = TimeSpan.FromSeconds(30);

  private readonly string _root = Path.Combine(Path.GetTempPath(), "aud91-" + Guid.NewGuid().ToString("N"));
  private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
  private readonly SdrDeviceGate _gate = new();
  private readonly PlanLiveSweeper _live = new();
  private readonly Mock<ISleepService> _sleep = new();
  private readonly List<StationDevice> _devices = new();
  private readonly BandMapOptions _options = new()
  {
    InitialDelaySeconds = 120,
    RescanIntervalMinutes = 60,
    SweepGainDb = 28f,
    SamplesPerMeasurement = ChannelPowerMeter.FftSize,
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

  /// <summary>What the radio is tuned to; null means no radio source.</summary>
  private BandMapTuning? Tuning { get; set; }

  private Func<BandMapTuning?>? TuningOverride { get; set; }

  private BandMapService CreateService(ILiveBandSweeper? live = null)
  {
    Mock<IOptionsMonitor<BandMapOptions>> monitor = new();
    monitor.Setup(m => m.CurrentValue).Returns(_options);
    BandMapService service = new(
      NullLogger<BandMapService>.Instance,
      monitor.Object,
      new BandMapStore(_root, NullLogger.Instance),
      _gate,
      () => live ?? _live,
      () => _sleep.Object,
      () =>
      {
        StationDevice device = new();
        _devices.Add(device);
        return device;
      },
      _time,
      TuningOverride ?? (() => Tuning));
    _services.Add(service);
    return service;
  }

  private static Task Settle(BandMapService service) => service.RunningSweep.WaitAsync(FailSafe);

  private string MapPath(string file) => Path.Combine(_root, "bandmap", file);

  [Fact]
  public async Task Timer_SweepsTheRadiosBand_AndStoresItAsWbJson_WithRangeAndSpacing()
  {
    Tuning = new BandMapTuning("WB", 162_400_000);
    BandMapService service = CreateService();
    await service.StartAsync(CancellationToken.None);

    _time.Advance(TimeSpan.FromSeconds(_options.InitialDelaySeconds));
    await Settle(service);

    BandMap map = Assert.IsType<BandMap>(service.GetMap("WB"));
    Assert.Equal("WB", map.Band);
    Assert.Equal(7, map.Channels.Count);
    Assert.Equal(WeatherStationHz, map.Channels.MaxBy(c => c.LevelDbfs)!.FrequencyHz);
    Assert.Equal(162_387_500, map.RangeMinHz);
    Assert.Equal(162_562_500, map.RangeMaxHz);
    Assert.Equal(25_000, map.ChannelSpacingHz);
    Assert.True(File.Exists(MapPath("wb.json")));
    Assert.False(File.Exists(MapPath("fm.json")));
    Assert.Null(service.GetMap("FM"));
    Assert.Same(map, service.CurrentMap);
    Assert.Equal("WB", service.GetStatus().Last!.Band);
    // One tune for the whole band: tune, settling read, measurement read.
    Assert.Equal(new long[] { 162_487_500 }, Assert.Single(_devices).Tunes);
  }

  [Theory]
  [InlineData("AM")]
  [InlineData("SW")]
  public async Task Timer_OnAnUnmappableBand_SkipsWithBandNotReceivable_AndOpensNoDevice(string band)
  {
    Tuning = new BandMapTuning(band, 1_000_000);
    BandMapService service = CreateService();
    await service.StartAsync(CancellationToken.None);

    _time.Advance(TimeSpan.FromSeconds(_options.InitialDelaySeconds));
    await Settle(service);

    BandSweepOutcome last = service.GetStatus().Last!;
    Assert.Equal(BandSweepResults.Skipped, last.Result);
    Assert.Equal(BandMapService.ReasonBandNotReceivable, last.Reason);
    Assert.Equal(band, last.Band);
    Assert.Empty(_devices);
    Assert.False(_gate.IsLeasedForSweep);
    Assert.Equal(0, _live.Calls);
  }

  [Fact]
  public async Task Request_ForAnExplicitBand_SweepsThatBand_AndStatusCarriesIt()
  {
    Tuning = new BandMapTuning("FM", 101_100_000);
    await _gate.ClaimForRadioAsync();
    _live.CanSweepLive = true;
    _live.Block = new TaskCompletionSource();
    BandMapService service = CreateService();

    BandSweepRequestResult result = service.RequestSweep("wb");
    await _live.Entered.Task.WaitAsync(FailSafe);
    BandSweepStatus running = service.GetStatus();
    _live.Block.SetResult();
    await Settle(service);

    Assert.Equal(BandSweepRequestOutcome.Started, result.Outcome);
    Assert.Equal("WB", result.Status.Band);
    Assert.Equal("WB", running.Band);
    Assert.Equal("WB", Assert.Single(_live.Plans).Band);
    Assert.Equal("WB", service.GetStatus().Last!.Band);
    Assert.Equal(BandSweepResults.Completed, service.GetStatus().Last!.Result);
    Assert.NotNull(service.GetMap("WB"));
    Assert.Null(service.GetMap("FM"));
    Assert.Null(service.CurrentMap);
  }

  [Fact]
  public async Task Request_WithNoBand_SweepsTheRadiosBand()
  {
    Tuning = new BandMapTuning("AIR", 118_300_000);
    BandMapService service = CreateService();

    BandSweepRequestResult result = service.RequestSweep();
    await Settle(service);

    Assert.Equal(BandSweepRequestOutcome.Started, result.Outcome);
    Assert.Equal("AIR", result.Status.Band);
    Assert.Equal(1161, service.GetMap("AIR")!.Channels.Count);
    Assert.Equal(146, Assert.Single(_devices).Tunes.Count);
  }

  [Fact]
  public async Task Request_ForAnotherBand_WhileASweepRuns_IsOtherBandSweeping_AndStartsNothing()
  {
    Tuning = new BandMapTuning("FM", 101_100_000);
    await _gate.ClaimForRadioAsync();
    _live.CanSweepLive = true;
    _live.Block = new TaskCompletionSource();
    BandMapService service = CreateService();

    BandSweepRequestResult fm = service.RequestSweep("FM");
    await _live.Entered.Task.WaitAsync(FailSafe);
    BandSweepRequestResult wb = service.RequestSweep("WB");
    BandSweepRequestResult sameBand = service.RequestSweep("fm");
    _live.Block.SetResult();
    await Settle(service);

    Assert.Equal(BandSweepRequestOutcome.Started, fm.Outcome);
    Assert.Equal(BandSweepRequestOutcome.Unavailable, wb.Outcome);
    Assert.Equal(BandMapService.ReasonOtherBandSweeping, wb.Reason);
    // The status names the band that is running, for the 409's message.
    Assert.True(wb.Status.IsSweeping);
    Assert.Equal("FM", wb.Status.Band);
    Assert.Equal(BandSweepRequestOutcome.AlreadyRunning, sameBand.Outcome);
    Assert.Null(sameBand.Reason);
    Assert.Equal("FM", Assert.Single(_live.Plans).Band);
    Assert.Null(service.GetMap("WB"));
    // The refusal is not recorded: the last outcome is the FM sweep's.
    Assert.Equal("FM", service.GetStatus().Last!.Band);
    Assert.Equal(BandSweepResults.Completed, service.GetStatus().Last!.Result);
  }

  [Fact]
  public async Task Timer_TreatsAFreshVhfMapAsDue_OnceTheRadioHasLeftItsWindow()
  {
    Tuning = new BandMapTuning("VHF", 146_523_000);
    BandMapService service = CreateService();
    service.RequestSweep();
    await Settle(service);
    Assert.Single(_devices);
    Assert.Equal(145_525_000, service.GetMap("VHF")!.RangeMinHz);

    // Still inside the stored window: a fresh map is not due.
    Tuning = new BandMapTuning("VHF", 146_900_000);
    service.OnTimerTick();
    await Settle(service);
    Assert.Single(_devices);

    // The radio moved its VHF window: the map is seconds old but no longer covers it.
    Tuning = new BandMapTuning("VHF", 162_000_000);
    service.OnTimerTick();
    await Settle(service);

    Assert.Equal(2, _devices.Count);
    BandMap map = service.GetMap("VHF")!;
    Assert.Equal(161_000_000, map.RangeMinHz);
    Assert.Equal(163_000_000, map.RangeMaxHz);
    Assert.Equal(BandSweepTriggers.Timer, map.Trigger);
  }

  [Fact]
  public void Request_ForAnUnmappableBand_IsUnavailable_TouchesNothing_AndIsNotRecorded()
  {
    Tuning = new BandMapTuning("FM", 101_100_000);
    BandMapService service = CreateService();

    BandSweepRequestResult result = service.RequestSweep("AM");

    Assert.Equal(BandSweepRequestOutcome.Unavailable, result.Outcome);
    Assert.Equal(BandMapService.ReasonBandNotReceivable, result.Reason);
    Assert.Null(service.GetStatus().Last);
    Assert.Empty(_devices);
    Assert.False(_gate.IsLeasedForSweep);
  }

  [Fact]
  public void Request_ForAnUnknownBand_Throws()
  {
    BandMapService service = CreateService();

    Assert.Throws<ArgumentException>(() => service.RequestSweep("LW"));
    Assert.Empty(_devices);
  }

  [Fact]
  public async Task Vhf_FollowsTheRadiosFrequencyOnlyWhileTheRadioIsOnVhf()
  {
    await _gate.ClaimForRadioAsync();
    _live.CanSweepLive = true;

    Tuning = new BandMapTuning("VHF", 146_523_000);
    BandMapService service = CreateService();
    service.RequestSweep("VHF");
    await Settle(service);

    Tuning = new BandMapTuning("FM", 101_100_000);
    service.RequestSweep("VHF");
    await Settle(service);

    Assert.Equal(2, _live.Plans.Count);
    Assert.Equal(145_525_000, _live.Plans[0].Channels[0]);
    Assert.Equal(30_000_000, _live.Plans[1].Channels[0]);
    BandMap map = service.GetMap("VHF")!;
    Assert.Equal(30_000_000, map.RangeMinHz);
    Assert.Equal(32_000_000, map.RangeMaxHz);
    Assert.Equal(12_500, map.ChannelSpacingHz);
  }

  [Fact]
  public async Task FmSweep_StillWritesFmJson_WithTheAud76ChannelsAndARecordedRange()
  {
    Tuning = new BandMapTuning("FM", 101_100_000);
    BandMapService service = CreateService();

    service.RequestSweep();
    await Settle(service);

    BandMap map = service.GetMap("FM")!;
    Assert.True(File.Exists(MapPath("fm.json")));
    Assert.Equal(FmChannelPlan.Channels, map.Channels.Select(c => c.FrequencyHz));
    Assert.Equal(FmChannelPlan.DisplayMinHz, map.RangeMinHz);
    Assert.Equal(FmChannelPlan.DisplayMaxHz, map.RangeMaxHz);
    Assert.Equal(FmChannelPlan.ChannelSpacingHz, map.ChannelSpacingHz);
    Assert.Equal(FmChannelPlan.Channels, _devices.Single().Tunes);
  }

  [Fact]
  public void Aud76ShapedFmJson_StillLoads_WithNoRecordedRange()
  {
    Directory.CreateDirectory(Path.Combine(_root, "bandmap"));
    File.WriteAllText(MapPath("fm.json"),
      "{\"band\":\"FM\",\"scannedAtUtc\":\"2026-09-30T05:00:00+00:00\",\"channels\":[{\"frequencyHz\":92300000,\"levelDbfs\":-31.5},{\"frequencyHz\":92500000,\"levelDbfs\":-60.25}],\"trigger\":\"request\",\"path\":\"live\"}");

    BandMapService service = CreateService();

    BandMap map = Assert.IsType<BandMap>(service.GetMap("FM"));
    Assert.Equal(2, map.Channels.Count);
    Assert.Equal(new BandMapChannel(92_300_000, -31.5f), map.Channels[0]);
    Assert.Equal(0, map.RangeMinHz);
    Assert.Equal(0, map.RangeMaxHz);
    Assert.Equal(0, map.ChannelSpacingHz);
    Assert.Same(map, service.CurrentMap);
  }

  [Fact]
  public async Task Staleness_IsPerBand_AFreshFmMapDoesNotStopTheWbSweep()
  {
    Tuning = new BandMapTuning("FM", 101_100_000);
    BandMapService service = CreateService();
    service.RequestSweep();
    await Settle(service);
    Assert.Single(_devices);
    await service.StartAsync(CancellationToken.None);

    // The owner switches to WB; the FM map is seconds old, WB has none.
    Tuning = new BandMapTuning("WB", 162_400_000);
    _time.Advance(TimeSpan.FromSeconds(_options.InitialDelaySeconds));
    await Settle(service);

    Assert.Equal(2, _devices.Count);
    Assert.NotNull(service.GetMap("WB"));
    Assert.NotNull(service.GetMap("FM"));

    // Back on FM: its map is still fresh, so the next evaluation does not sweep.
    Tuning = new BandMapTuning("FM", 101_100_000);
    service.OnTimerTick();
    await Settle(service);
    Assert.Equal(2, _devices.Count);
  }

  [Fact]
  public async Task TuningDelegateThatThrows_MeansFm()
  {
    TuningOverride = () => throw new InvalidOperationException("audio manager not built");
    BandMapService service = CreateService();

    Assert.Equal("FM", service.CurrentBand);
    service.RequestSweep();
    await Settle(service);
    Assert.NotNull(service.GetMap("FM"));
  }

  [Fact]
  public async Task LiveSweeperWithoutAPlanOverride_FailsAGroupedPlan_ButStillSweepsFm()
  {
    await _gate.ClaimForRadioAsync();
    ChannelListOnlySweeper channelListOnly = new();
    Tuning = new BandMapTuning("WB", 162_400_000);
    BandMapService service = CreateService(channelListOnly);

    service.RequestSweep();
    await Settle(service);
    BandSweepOutcome wb = service.GetStatus().Last!;

    service.RequestSweep("FM");
    await Settle(service);

    Assert.Equal(BandSweepResults.Failed, wb.Result);
    Assert.Equal("exception:NotSupportedException", wb.Reason);
    Assert.Equal(1, channelListOnly.Calls);
    Assert.Equal(BandSweepResults.Completed, service.GetStatus().Last!.Result);
    Assert.Equal(FmChannelPlan.Channels.Count, service.GetMap("FM")!.Channels.Count);
  }

  /// <summary>Overrides the plan overload; reports progress per tune like the receiver does.</summary>
  private sealed class PlanLiveSweeper : ILiveBandSweeper
  {
    private int _calls;

    public bool CanSweepLive { get; set; } = true;

    public int Calls => Volatile.Read(ref _calls);

    public List<BandSweepPlan> Plans { get; } = new();

    public TaskCompletionSource? Block { get; set; }

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<IReadOnlyList<ChannelLevel>> SweepLiveAsync(
      IReadOnlyList<long> channels, float gainDb, int samplesPerMeasurement,
      IProgress<BandSweepProgress>? progress, CancellationToken cancellationToken) =>
      throw new InvalidOperationException("The service must call the plan overload.");

    public async Task<IReadOnlyList<ChannelLevel>> SweepLiveAsync(
      BandSweepPlan plan, float gainDb, int samplesPerMeasurement,
      IProgress<BandSweepProgress>? progress, CancellationToken cancellationToken)
    {
      Interlocked.Increment(ref _calls);
      lock (Plans)
      {
        Plans.Add(plan);
      }
      Entered.TrySetResult();
      if (Block != null)
      {
        await Block.Task.WaitAsync(cancellationToken);
      }
      int done = 0;
      foreach (SweepTune tune in plan.Tunes)
      {
        done += tune.ChannelHz.Count;
        progress?.Report(new BandSweepProgress(done, plan.Channels.Count));
      }
      return plan.Channels.Select(hz => new ChannelLevel(hz, -50f)).ToArray();
    }
  }

  /// <summary>An AUD-76-era sweeper: only the channel-list overload.</summary>
  private sealed class ChannelListOnlySweeper : ILiveBandSweeper
  {
    private int _calls;

    public bool CanSweepLive => true;

    public int Calls => Volatile.Read(ref _calls);

    public Task<IReadOnlyList<ChannelLevel>> SweepLiveAsync(
      IReadOnlyList<long> channels, float gainDb, int samplesPerMeasurement,
      IProgress<BandSweepProgress>? progress, CancellationToken cancellationToken)
    {
      Interlocked.Increment(ref _calls);
      return Task.FromResult<IReadOnlyList<ChannelLevel>>(channels.Select(hz => new ChannelLevel(hz, -50f)).ToArray());
    }
  }

  /// <summary>
  /// Idle-path device: serves a tone at the weather station's offset from the tuned frequency
  /// when it is within the capture, over weak noise. Records every tune.
  /// </summary>
  private sealed class StationDevice : ISdrDevice
  {
    private long _frequencyHz;
    private int _reads;

    public List<long> Tunes { get; } = new();

    public DeviceInfo DeviceInfo { get; } = new() { Name = "Fake", MinFrequencyHz = 24_000_000, MaxFrequencyHz = 1_766_000_000 };

    public bool IsOpen { get; private set; }

    public bool IsStreaming => false;

    public event EventHandler<IqSamplesEventArgs>? SamplesAvailable
    {
      add { }
      remove { }
    }

    public event EventHandler<DeviceErrorEventArgs>? ErrorOccurred
    {
      add { }
      remove { }
    }

    public bool Open()
    {
      IsOpen = true;
      return true;
    }

    public void Close() => IsOpen = false;

    public bool SetFrequency(long frequencyHz)
    {
      Tunes.Add(frequencyHz);
      _frequencyHz = frequencyHz;
      return true;
    }

    public long GetFrequency() => _frequencyHz;

    public bool SetSampleRate(int sampleRate) => true;

    public int GetSampleRate() => DeviceSweepTuner.SweepSampleRate;

    public bool SetGainMode(bool automatic) => true;

    public bool SetGain(float gainDb) => true;

    public float GetGain() => 28f;

    public bool SetFrequencyCorrection(int ppm) => true;

    public int GetFrequencyCorrection() => 0;

    public bool StartStreaming() => false;

    public void StopStreaming()
    {
    }

    public int ReadSamples(Span<IqSample> buffer)
    {
      Random random = new(++_reads);
      long offset = WeatherStationHz - _frequencyHz;
      float tone = Math.Abs(offset) < DeviceSweepTuner.SweepSampleRate / 2 ? 0.5f : 0f;
      for (int n = 0; n < buffer.Length; n++)
      {
        double phase = 2.0 * Math.PI * offset * n / DeviceSweepTuner.SweepSampleRate;
        float i = (float)(tone * Math.Cos(phase) + (random.NextDouble() * 2 - 1) * 0.01);
        float q = (float)(tone * Math.Sin(phase) + (random.NextDouble() * 2 - 1) * 0.01);
        buffer[n] = new IqSample(i, q);
      }
      return buffer.Length;
    }

    public void Dispose() => Close();
  }
}
