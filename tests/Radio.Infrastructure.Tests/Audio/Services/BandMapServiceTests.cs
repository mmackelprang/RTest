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
/// AUD-76 band-map rules. Time is a <see cref="FakeTimeProvider"/>; every sweep is awaited
/// through <see cref="BandMapService.RunningSweep"/> rather than slept on. The only wall-clock
/// bound is <see cref="FailSafe"/>, which limits how long a broken test hangs.
/// </summary>
public sealed class BandMapServiceTests : IDisposable
{
  private const long IdleStationHz = 99_500_000;
  private const long LiveStationHz = 101_100_000;
  private static readonly TimeSpan FailSafe = TimeSpan.FromSeconds(30);

  private readonly string _root = Path.Combine(Path.GetTempPath(), "aud76-" + Guid.NewGuid().ToString("N"));
  private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
  private readonly SdrDeviceGate _gate = new();
  private readonly FakeLiveSweeper _live = new();
  private readonly Mock<ISleepService> _sleep = new();
  private readonly List<FakeSweepDevice> _devices = new();
  private readonly BandMapOptions _options = new()
  {
    InitialDelaySeconds = 120,
    RescanIntervalMinutes = 60,
    SweepGainDb = 28f,
    // One FFT frame per read keeps the 101-channel sweep cheap.
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

  private Func<ISdrDevice?>? DeviceFactory { get; set; }

  private BandMapService CreateService()
  {
    DeviceFactory ??= () =>
    {
      FakeSweepDevice device = new(_gate);
      _devices.Add(device);
      return device;
    };
    Mock<IOptionsMonitor<BandMapOptions>> monitor = new();
    monitor.Setup(m => m.CurrentValue).Returns(_options);
    BandMapService service = new(
      NullLogger<BandMapService>.Instance,
      monitor.Object,
      new BandMapStore(_root, NullLogger.Instance),
      _gate,
      () => _live,
      () => _sleep.Object,
      () => DeviceFactory!(),
      _time);
    _services.Add(service);
    return service;
  }

  private static Task Settle(BandMapService service) => service.RunningSweep.WaitAsync(FailSafe);

  [Fact]
  public async Task Timer_FirstSweepAfterInitialDelay_WhenIdle_PersistsMapWithPeakAtStation()
  {
    BandMapService service = CreateService();
    await service.StartAsync(CancellationToken.None);

    _time.Advance(TimeSpan.FromSeconds(119));
    Assert.Empty(_devices);
    Assert.False(service.GetStatus().IsSweeping);

    _time.Advance(TimeSpan.FromSeconds(1));
    await Settle(service);

    BandMap map = Assert.IsType<BandMap>(service.CurrentMap);
    Assert.Equal(FmChannelPlan.Channels.Count, map.Channels.Count);
    Assert.Equal(IdleStationHz, map.Channels.MaxBy(c => c.LevelDbfs)!.FrequencyHz);
    Assert.Equal(BandSweepTriggers.Timer, map.Trigger);
    Assert.Equal(BandSweepPaths.Idle, map.Path);
    Assert.True(File.Exists(Path.Combine(_root, "bandmap", "fm.json")));

    BandSweepOutcome last = service.GetStatus().Last!;
    Assert.Equal(BandSweepResults.Completed, last.Result);
    FakeSweepDevice device = Assert.Single(_devices);
    Assert.True(device.Disposed);
    Assert.False(_gate.IsLeasedForSweep);
    Assert.Equal(0, _live.Calls);
  }

  [Fact]
  public async Task Timer_WhileRadioHoldsGate_AndAwake_SkipsAndDoesNotTouchEitherPath()
  {
    await _gate.ClaimForRadioAsync();
    _sleep.Setup(s => s.IsSleeping).Returns(false);
    // The idle screen leaves audio playing: a visible sleep screen alone is not "asleep".
    _sleep.Setup(s => s.IsSleepScreenVisible).Returns(true);
    _live.CanSweepLive = true;
    BandMapService service = CreateService();
    await service.StartAsync(CancellationToken.None);

    _time.Advance(TimeSpan.FromSeconds(_options.InitialDelaySeconds));
    await Settle(service);

    BandSweepOutcome last = service.GetStatus().Last!;
    Assert.Equal(BandSweepResults.Skipped, last.Result);
    Assert.Equal(BandMapService.ReasonRadioPlaying, last.Reason);
    Assert.Equal(0, _live.Calls);
    Assert.Empty(_devices);
    Assert.Null(service.CurrentMap);
  }

  [Fact]
  public async Task Timer_WhileRadioHoldsGate_AndAsleep_SweepsThroughLivePath()
  {
    await _gate.ClaimForRadioAsync();
    _sleep.Setup(s => s.IsSleeping).Returns(true);
    _live.CanSweepLive = true;
    BandMapService service = CreateService();
    await service.StartAsync(CancellationToken.None);

    _time.Advance(TimeSpan.FromSeconds(_options.InitialDelaySeconds));
    await Settle(service);

    Assert.Equal(1, _live.Calls);
    Assert.Empty(_devices);
    BandMap map = Assert.IsType<BandMap>(service.CurrentMap);
    Assert.Equal(BandSweepTriggers.Sleep, map.Trigger);
    Assert.Equal(BandSweepPaths.Live, map.Path);
    Assert.Equal(LiveStationHz, map.Channels.MaxBy(c => c.LevelDbfs)!.FrequencyHz);
  }

  [Fact]
  public async Task Timer_AsleepButSourceCannotSweepLive_Skips()
  {
    await _gate.ClaimForRadioAsync();
    _sleep.Setup(s => s.IsSleeping).Returns(true);
    _live.CanSweepLive = false;
    BandMapService service = CreateService();
    await service.StartAsync(CancellationToken.None);

    _time.Advance(TimeSpan.FromSeconds(_options.InitialDelaySeconds));
    await Settle(service);

    Assert.Equal(BandSweepResults.Skipped, service.GetStatus().Last!.Result);
    Assert.Equal(0, _live.Calls);
  }

  [Fact]
  public async Task RadioClaimMidIdleSweep_CancelsIt_ClosesDeviceBeforeRelease_KeepsPreviousMap()
  {
    BandMap previous = new()
    {
      Band = "FM",
      ScannedAtUtc = _time.GetUtcNow().AddDays(-1),
      Channels = new[] { new BandMapChannel(90_100_000, -40f) },
      Trigger = BandSweepTriggers.Timer,
      Path = BandSweepPaths.Idle,
    };
    new BandMapStore(_root, NullLogger.Instance).Save(previous);
    string fileBefore = File.ReadAllText(Path.Combine(_root, "bandmap", "fm.json"));

    Task? claim = null;
    DeviceFactory = () =>
    {
      FakeSweepDevice device = new(_gate)
      {
        // Mid-sweep, the owner switches to Radio: the source claims the gate.
        OnRead = reads =>
        {
          if (reads == 10)
          {
            claim = _gate.ClaimForRadioAsync();
          }
        },
      };
      _devices.Add(device);
      return device;
    };
    BandMapService service = CreateService();

    BandSweepRequestResult request = service.RequestSweep();
    Assert.Equal(BandSweepRequestOutcome.Started, request.Outcome);
    await Settle(service);
    Assert.NotNull(claim);
    await claim!.WaitAsync(FailSafe);

    FakeSweepDevice device = Assert.Single(_devices);
    Assert.True(device.Disposed);
    Assert.True(device.LeaseWasLiveWhenClosed, "device must be closed before the lease is released");
    Assert.False(_gate.IsLeasedForSweep);
    Assert.True(device.Reads < 2 * FmChannelPlan.Channels.Count, "sweep should stop early");

    BandSweepOutcome last = service.GetStatus().Last!;
    Assert.Equal(BandSweepResults.Cancelled, last.Result);
    Assert.Equal("radio-claimed", last.Reason);
    BandMap kept = Assert.IsType<BandMap>(service.CurrentMap);
    Assert.Equal(previous.ScannedAtUtc, kept.ScannedAtUtc);
    Assert.Equal(previous.Channels, kept.Channels);
    Assert.Equal(fileBefore, File.ReadAllText(Path.Combine(_root, "bandmap", "fm.json")));
  }

  [Fact]
  public async Task Request_WhileRadioHoldsGate_UsesLivePathEvenWhenAwake()
  {
    await _gate.ClaimForRadioAsync();
    _sleep.Setup(s => s.IsSleeping).Returns(false);
    _live.CanSweepLive = true;
    BandMapService service = CreateService();

    BandSweepRequestResult result = service.RequestSweep();
    await Settle(service);

    Assert.Equal(BandSweepRequestOutcome.Started, result.Outcome);
    Assert.Equal(1, _live.Calls);
    Assert.Empty(_devices);
    Assert.Equal(BandSweepTriggers.Request, service.CurrentMap!.Trigger);
    Assert.Equal(BandSweepPaths.Live, service.CurrentMap.Path);
  }

  [Fact]
  public async Task Request_WhileRadioHoldsGate_AndCannotSweepLive_IsUnavailable()
  {
    await _gate.ClaimForRadioAsync();
    _live.CanSweepLive = false;
    BandMapService service = CreateService();

    BandSweepRequestResult result = service.RequestSweep();

    Assert.Equal(BandSweepRequestOutcome.Unavailable, result.Outcome);
    Assert.Equal(BandMapService.ReasonRadioBusy, result.Reason);
    Assert.Equal(0, _live.Calls);
  }

  [Fact]
  public void Request_WhenIdleButNoSdrDevice_IsUnavailable_AndReleasesLease()
  {
    DeviceFactory = () => null;
    BandMapService service = CreateService();

    BandSweepRequestResult result = service.RequestSweep();

    Assert.Equal(BandSweepRequestOutcome.Unavailable, result.Outcome);
    Assert.Equal(BandMapService.ReasonNoSdrDevice, result.Reason);
    Assert.False(_gate.IsLeasedForSweep);
    Assert.False(result.Status.IsSweeping);
  }

  [Fact]
  public async Task SecondRequestOrTimerTick_DuringASweep_DoesNotStartAnother()
  {
    await _gate.ClaimForRadioAsync();
    _sleep.Setup(s => s.IsSleeping).Returns(true);
    _live.CanSweepLive = true;
    _live.Block = new TaskCompletionSource();
    BandMapService service = CreateService();

    BandSweepRequestResult first = service.RequestSweep();
    await _live.Entered.Task.WaitAsync(FailSafe);
    BandSweepRequestResult second = service.RequestSweep();
    service.OnTimerTick();

    Assert.Equal(BandSweepRequestOutcome.Started, first.Outcome);
    Assert.Equal(BandSweepRequestOutcome.AlreadyRunning, second.Outcome);
    Assert.True(second.Status.IsSweeping);
    Assert.Equal(1, _live.Calls);

    _live.Block.SetResult();
    await Settle(service);
    Assert.Equal(1, _live.Calls);
    Assert.False(service.GetStatus().IsSweeping);
  }

  [Fact]
  public async Task PersistedMap_ReloadsOnANewServiceInstance()
  {
    BandMapService first = CreateService();
    first.RequestSweep();
    await Settle(first);
    BandMap stored = first.CurrentMap!;

    BandMapService second = CreateService();

    BandMap reloaded = Assert.IsType<BandMap>(second.CurrentMap);
    Assert.Equal(stored.ScannedAtUtc, reloaded.ScannedAtUtc);
    Assert.Equal(stored.Channels, reloaded.Channels);
    Assert.Equal(stored.Trigger, reloaded.Trigger);
  }

  [Fact]
  public void CorruptMapFile_StartsWithNoMap()
  {
    string path = Path.Combine(_root, "bandmap", "fm.json");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, "{ not json");

    BandMapService service = CreateService();

    Assert.Null(service.CurrentMap);
  }

  [Fact]
  public async Task Timer_WithFreshMap_DoesNotSweepUntilItIsDue()
  {
    BandMapService service = CreateService();
    service.RequestSweep();
    await Settle(service);
    _devices.Clear();
    await service.StartAsync(CancellationToken.None);

    // Initial evaluation: the map is seconds old, so nothing runs.
    _time.Advance(TimeSpan.FromSeconds(_options.InitialDelaySeconds));
    await Settle(service);
    Assert.Empty(_devices);

    // Once the map reaches the rescan interval, the next evaluation sweeps.
    _time.Advance(TimeSpan.FromMinutes(_options.RescanIntervalMinutes));
    await Settle(service);
    Assert.Single(_devices);
  }

  [Fact]
  public async Task Dispose_Twice_DoesNotThrow_AndStopAfterDisposeIsHarmless()
  {
    // The host's container disposes this singleton once per registration that resolved it
    // (the concrete type and the IHostedService factory), so a second Dispose is real.
    BandMapService service = CreateService();
    await service.StartAsync(CancellationToken.None);

    service.Dispose();
    service.Dispose();
    await service.StopAsync(CancellationToken.None);
  }

  private sealed class FakeLiveSweeper : ILiveBandSweeper
  {
    private int _calls;

    public bool CanSweepLive { get; set; }

    public int Calls => Volatile.Read(ref _calls);

    public TaskCompletionSource? Block { get; set; }

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<IReadOnlyList<ChannelLevel>> SweepLiveAsync(
      IReadOnlyList<long> channels, float gainDb, int samplesPerMeasurement,
      IProgress<BandSweepProgress>? progress, CancellationToken cancellationToken)
    {
      Interlocked.Increment(ref _calls);
      Entered.TrySetResult();
      if (Block != null)
      {
        await Block.Task.WaitAsync(cancellationToken);
      }
      return channels.Select(hz => new ChannelLevel(hz, hz == LiveStationHz ? -20f : -70f)).ToArray();
    }
  }

  /// <summary>
  /// Idle-path device: serves a strong in-band tone only when tuned to
  /// <see cref="IdleStationHz"/>. Synchronous reads, no streaming thread.
  /// </summary>
  private sealed class FakeSweepDevice : ISdrDevice
  {
    private readonly SdrDeviceGate _gate;
    private long _frequencyHz;
    private int _reads;

    public FakeSweepDevice(SdrDeviceGate gate)
    {
      _gate = gate;
    }

    public Action<int>? OnRead { get; init; }

    public int Reads => Volatile.Read(ref _reads);

    public bool Disposed { get; private set; }

    public bool LeaseWasLiveWhenClosed { get; private set; }

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

    public void Close()
    {
      if (IsOpen)
      {
        LeaseWasLiveWhenClosed = _gate.IsLeasedForSweep;
      }
      IsOpen = false;
    }

    public bool SetFrequency(long frequencyHz)
    {
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
      int reads = Interlocked.Increment(ref _reads);
      OnRead?.Invoke(reads);

      Random random = new(reads);
      bool station = _frequencyHz == IdleStationHz;
      for (int n = 0; n < buffer.Length; n++)
      {
        double phase = 2.0 * Math.PI * 40_000 * n / DeviceSweepTuner.SweepSampleRate;
        float tone = station ? 0.5f : 0f;
        float i = (float)(tone * Math.Cos(phase) + (random.NextDouble() * 2 - 1) * 0.01);
        float q = (float)(tone * Math.Sin(phase) + (random.NextDouble() * 2 - 1) * 0.01);
        buffer[n] = new IqSample(i, q);
      }
      return buffer.Length;
    }

    public void Dispose()
    {
      Close();
      Disposed = true;
    }
  }
}
