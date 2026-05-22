using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.Services;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// Unit tests for <see cref="BluetoothCaptureWatchdog"/>. Uses a fake
/// <see cref="ICaptureStreamSnapshotSource"/> so the watchdog's threshold +
/// consecutive-check logic can be exercised without instantiating
/// <c>LinuxBluetoothService</c> or its D-Bus dependencies.
/// </summary>
public class BluetoothCaptureWatchdogTests
{
  private static BluetoothCaptureWatchdog CreateWatchdog(
    ICaptureStreamSnapshotSource source,
    int thresholdMs,
    int tickMs,
    int consecutive)
  {
    var opts = new BluetoothOptions
    {
      OnProcessStallThresholdMs = thresholdMs,
      WatchdogTickIntervalMs = tickMs,
      ConsecutiveStalledChecks = consecutive,
    };
    var monitor = new StaticOptionsMonitor<BluetoothOptions>(opts);
    return new BluetoothCaptureWatchdog(
      NullLogger<BluetoothCaptureWatchdog>.Instance, monitor, source);
  }

  private static async Task RunForMsAsync(BluetoothCaptureWatchdog watchdog, int durationMs)
  {
    using var cts = new CancellationTokenSource();
    await watchdog.StartAsync(cts.Token);
    await Task.Delay(durationMs);
    cts.Cancel();
    await watchdog.StopAsync(CancellationToken.None);
  }

  [Fact]
  public async Task NoActiveStream_DoesNotRaise()
  {
    var source = new FakeSnapshotSource(snapshot: null);
    var watchdog = CreateWatchdog(source, thresholdMs: 1000, tickMs: 20, consecutive: 2);
    await RunForMsAsync(watchdog, 250);
    Assert.Equal(0, source.RaiseCount);
  }

  [Fact]
  public async Task BelowThreshold_DoesNotRaise()
  {
    var source = new FakeSnapshotSource(("AA:BB:CC:DD:EE:FF", 500L));
    var watchdog = CreateWatchdog(source, thresholdMs: 1000, tickMs: 20, consecutive: 2);
    await RunForMsAsync(watchdog, 250);
    Assert.Equal(0, source.RaiseCount);
  }

  [Fact]
  public async Task AboveThreshold_RaisesAfterConsecutiveChecks()
  {
    var source = new FakeSnapshotSource(("AA:BB:CC:DD:EE:FF", 6000L));
    var watchdog = CreateWatchdog(source, thresholdMs: 5000, tickMs: 20, consecutive: 3);
    await RunForMsAsync(watchdog, 300); // plenty of ticks for 3 consecutive
    Assert.True(source.RaiseCount >= 1,
      $"Expected RaiseCount >= 1, got {source.RaiseCount}");
    Assert.Equal("AA:BB:CC:DD:EE:FF", source.LastRaiseAddress);
    Assert.Equal(6000L, source.LastRaiseElapsedMs);
    Assert.True(source.LastRaiseConsecutive >= 3);
  }

  [Fact]
  public async Task DisabledByZeroThreshold_DoesNotRaise()
  {
    var source = new FakeSnapshotSource(("AA:BB:CC:DD:EE:FF", 99999L));
    var watchdog = CreateWatchdog(source, thresholdMs: 0, tickMs: 20, consecutive: 1);
    await RunForMsAsync(watchdog, 200);
    Assert.Equal(0, source.RaiseCount);
  }

  [Fact]
  public async Task IntermittentStall_ResetsCounter()
  {
    var source = new FakeSnapshotSource(snapshot: null);
    var watchdog = CreateWatchdog(source, thresholdMs: 5000, tickMs: 20, consecutive: 5);

    using var cts = new CancellationTokenSource();
    await watchdog.StartAsync(cts.Token);

    // Phase 1: above threshold for ~2 ticks
    source.Set(("AA:BB:CC:DD:EE:FF", 6000L));
    await Task.Delay(60);
    // Phase 2: recover for ~2 ticks (resets counter)
    source.Set(("AA:BB:CC:DD:EE:FF", 100L));
    await Task.Delay(60);
    // Phase 3: above threshold for ~2 ticks (never reaches 5 consecutive)
    source.Set(("AA:BB:CC:DD:EE:FF", 6000L));
    await Task.Delay(60);

    cts.Cancel();
    await watchdog.StopAsync(CancellationToken.None);

    Assert.Equal(0, source.RaiseCount);
  }

  /// <summary>
  /// Verifies that the watchdog idles (never enters its main loop) when a
  /// <see cref="NullCaptureStreamSnapshotSource"/> is registered. This is the
  /// Windows / Mock / BT-disabled production path.
  /// </summary>
  [Fact]
  public async Task NullSnapshotSource_ExitsImmediatelyWithoutRaising()
  {
    var watchdog = CreateWatchdog(
      NullCaptureStreamSnapshotSource.Instance,
      thresholdMs: 1000,
      tickMs: 20,
      consecutive: 2);
    using var cts = new CancellationTokenSource();
    await watchdog.StartAsync(cts.Token);
    await Task.Delay(100);
    Assert.Equal(0, watchdog.ConsecutiveStalledChecksForTest);
    cts.Cancel();
    await watchdog.StopAsync(CancellationToken.None);
  }

  // ---- Test helpers --------------------------------------------------------

  private sealed class FakeSnapshotSource : ICaptureStreamSnapshotSource
  {
    private (string Address, long ElapsedMs)? _snapshot;
    private readonly object _lock = new();
    public int RaiseCount { get; private set; }
    public string? LastRaiseAddress { get; private set; }
    public long LastRaiseElapsedMs { get; private set; }
    public int LastRaiseConsecutive { get; private set; }

    public FakeSnapshotSource(
      (string Address, long ElapsedMs)? snapshot = null)
    {
      _snapshot = snapshot;
    }

    public void Set((string Address, long ElapsedMs)? snapshot)
    {
      lock (_lock) { _snapshot = snapshot; }
    }

    public (string Address, long ElapsedMs)? GetCaptureStreamSnapshot()
    {
      lock (_lock) { return _snapshot; }
    }

    public void RaiseCaptureStreamStalled(string address, long elapsedMs, int consecutiveChecks)
    {
      lock (_lock)
      {
        RaiseCount++;
        LastRaiseAddress = address;
        LastRaiseElapsedMs = elapsedMs;
        LastRaiseConsecutive = consecutiveChecks;
      }
    }
  }

  private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
  {
    private readonly T _value;
    public StaticOptionsMonitor(T value) { _value = value; }
    public T CurrentValue => _value;
    public T Get(string? name) => _value;
    public IDisposable OnChange(Action<T, string?> listener) => new NullDisposable();

    private sealed class NullDisposable : IDisposable { public void Dispose() { } }
  }
}
