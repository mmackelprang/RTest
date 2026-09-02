using Microsoft.Extensions.Hosting;
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
/// <remarks>
/// <para>
/// The fake is a <em>rendezvous gate</em>, not a passive stub. The watchdog
/// blocks on entry to every <c>GetCaptureStreamSnapshot</c> call until the test
/// grants that poll, so a test decides exactly how many polls observe each
/// value. In the gated tests every assertion therefore runs while the watchdog
/// is held at its next poll, unable to read a snapshot or touch the counter
/// until the test grants it.
/// </para>
/// <para>
/// This shape replaces phase sequencing built on <c>Task.Delay</c>, which turned
/// <c>main</c> red on 2026-09-02 (commit <c>2a81f56</c>). The old
/// <c>IntermittentStall_ResetsCounter</c> held a below-threshold value for a
/// 60 ms wall-clock window while the watchdog polled on an independent 20 ms
/// tick; nothing made the watchdog observe that window. Under load a single tick
/// stretched past the whole window, the counter-resetting value was never
/// polled, and the two above-threshold phases concatenated into enough
/// consecutive stalled checks to fire. Reproduced at 13/200 under CPU
/// saturation. See TEST-4 in <c>docs/BUILDER_QUEUE.md</c>.
/// </para>
/// <para>
/// The timeouts below are deadlock guards: they bound how a broken test fails
/// and are not reached on a passing run. <c>WatchdogTickIntervalMs</c> now
/// affects only how fast these tests run, not whether they pass. The one test
/// the gate does not drive is <see cref="DisabledByZeroThreshold_DoesNotRaise"/>
/// — a disabled watchdog never polls, so there is no poll to grant — and its own
/// remarks say what it can and cannot establish.
/// </para>
/// </remarks>
public class BluetoothCaptureWatchdogTests
{
  /// <summary>
  /// Deadlock guard for both sides of the rendezvous. Generous on purpose: it is
  /// only ever reached when the watchdog has stopped polling, which is a failure.
  /// </summary>
  private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

  [Fact]
  public async Task NoActiveStream_DoesNotRaise()
  {
    await using var h = await WatchdogHarness.StartGatedAsync(
      thresholdMs: 1000, consecutive: 2);

    // A null snapshot means "no active native capture stream" and must never
    // accumulate toward a stall, however many polls it spans.
    await h.Source.GrantPollsAsync(snapshot: null, count: 6);

    Assert.Equal(0, h.Source.RaiseCount);
    Assert.Equal(0, h.Watchdog.ConsecutiveStalledChecksForTest);
  }

  [Fact]
  public async Task BelowThreshold_DoesNotRaise()
  {
    await using var h = await WatchdogHarness.StartGatedAsync(
      thresholdMs: 1000, consecutive: 2);

    await h.Source.GrantPollsAsync(("AA:BB:CC:DD:EE:FF", 500L), count: 6);

    Assert.Equal(0, h.Source.RaiseCount);
    Assert.Equal(0, h.Watchdog.ConsecutiveStalledChecksForTest);
  }

  [Fact]
  public async Task AboveThreshold_RaisesAfterConsecutiveChecks()
  {
    await using var h = await WatchdogHarness.StartGatedAsync(
      thresholdMs: 5000, consecutive: 3);

    // Two consecutive stalled polls is one short of the configured threshold.
    await h.Source.GrantPollsAsync(("AA:BB:CC:DD:EE:FF", 6000L), count: 2);
    Assert.Equal(0, h.Source.RaiseCount);
    Assert.Equal(2, h.Watchdog.ConsecutiveStalledChecksForTest);

    // The third crosses it and fires exactly once.
    await h.Source.GrantPollsAsync(("AA:BB:CC:DD:EE:FF", 6000L), count: 1);
    Assert.Equal(1, h.Source.RaiseCount);
    Assert.Equal("AA:BB:CC:DD:EE:FF", h.Source.LastRaiseAddress);
    Assert.Equal(6000L, h.Source.LastRaiseElapsedMs);
    Assert.Equal(3, h.Source.LastRaiseConsecutive);

    // The watchdog resets after firing so it does not re-fire every tick while
    // BluetoothAudioSource rebuilds the stream.
    Assert.Equal(0, h.Watchdog.ConsecutiveStalledChecksForTest);
    await h.Source.GrantPollsAsync(("AA:BB:CC:DD:EE:FF", 6000L), count: 2);
    Assert.Equal(1, h.Source.RaiseCount);
  }

  /// <summary>
  /// A zero threshold disables detection: the watchdog must not even query the
  /// snapshot source.
  /// </summary>
  /// <remarks>
  /// This is the one test the rendezvous gate cannot drive, because the disabled
  /// path returns before polling, so there is no poll to grant. It runs with the
  /// gate open and asserts over a bounded window instead. That is a weaker
  /// guarantee, and deliberately the safe direction: starvation can only reduce
  /// the number of loop iterations, so it can mask a regression but cannot turn a
  /// correct implementation red.
  /// </remarks>
  [Fact]
  public async Task DisabledByZeroThreshold_DoesNotRaise()
  {
    await using var h = await WatchdogHarness.StartUngatedAsync(
      thresholdMs: 0, consecutive: 1);
    h.Source.Set(("AA:BB:CC:DD:EE:FF", 99999L));

    await Task.Delay(200);

    Assert.Equal(0, h.Source.PollCount);
    Assert.Equal(0, h.Source.RaiseCount);
  }

  [Fact]
  public async Task IntermittentStall_ResetsCounter()
  {
    await using var h = await WatchdogHarness.StartGatedAsync(
      thresholdMs: 5000, consecutive: 5);

    // Phase 1: four consecutive stalled polls — one short of firing.
    await h.Source.GrantPollsAsync(("AA:BB:CC:DD:EE:FF", 6000L), count: 4);
    Assert.Equal(4, h.Watchdog.ConsecutiveStalledChecksForTest);

    // Phase 2: a single healthy poll. This is the observation the old test could
    // lose under load; the gate makes it impossible to skip.
    await h.Source.GrantPollsAsync(("AA:BB:CC:DD:EE:FF", 100L), count: 1);
    Assert.Equal(0, h.Watchdog.ConsecutiveStalledChecksForTest);

    // Phase 3: four more stalled polls. Eight above-threshold polls have now been
    // observed against a threshold of five, so only the reset keeps this quiet.
    await h.Source.GrantPollsAsync(("AA:BB:CC:DD:EE:FF", 6000L), count: 4);
    Assert.Equal(4, h.Watchdog.ConsecutiveStalledChecksForTest);

    Assert.Equal(0, h.Source.RaiseCount);
  }

  /// <summary>
  /// Verifies that the watchdog idles when a
  /// <see cref="NullCaptureStreamSnapshotSource"/> is registered — the Windows /
  /// Mock / BT-disabled production path.
  /// </summary>
  /// <remarks>
  /// Asserts on <see cref="BackgroundService.ExecuteTask"/> reaching
  /// <see cref="TaskStatus.RanToCompletion"/> without cancellation, which is what
  /// actually distinguishes the early return from the main loop. The stalled
  /// counter does not: it reads 0 on both paths, because a null snapshot resets
  /// it on every tick.
  /// </remarks>
  [Fact]
  public async Task NullSnapshotSource_ExitsImmediatelyWithoutRaising()
  {
    var watchdog = CreateWatchdog(
      NullCaptureStreamSnapshotSource.Instance,
      thresholdMs: 1000,
      tickMs: 10,
      consecutive: 2);

    using var cts = new CancellationTokenSource();
    await watchdog.StartAsync(cts.Token);

    var executeTask = Assert.IsAssignableFrom<Task>(watchdog.ExecuteTask);
    await executeTask.WaitAsync(GateTimeout);

    // RanToCompletion rather than Canceled: the loop was never entered, so
    // nothing had to be cancelled to make it stop.
    Assert.Equal(TaskStatus.RanToCompletion, executeTask.Status);
    Assert.Equal(0, watchdog.ConsecutiveStalledChecksForTest);

    await watchdog.StopAsync(CancellationToken.None);
  }

  // ---- Test helpers --------------------------------------------------------

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

  /// <summary>
  /// Owns a started watchdog and its fake source, and guarantees the watchdog is
  /// released from the gate and stopped even when a test fails mid-way.
  /// </summary>
  private sealed class WatchdogHarness : IAsyncDisposable
  {
    public FakeSnapshotSource Source { get; }
    public BluetoothCaptureWatchdog Watchdog { get; }

    private WatchdogHarness(FakeSnapshotSource source, BluetoothCaptureWatchdog watchdog)
    {
      Source = source;
      Watchdog = watchdog;
    }

    /// <summary>
    /// Starts the watchdog and returns once it is parked at its first poll, which
    /// is the precondition <see cref="FakeSnapshotSource.GrantPollsAsync"/>
    /// expects.
    /// </summary>
    public static async Task<WatchdogHarness> StartGatedAsync(int thresholdMs, int consecutive)
    {
      var source = new FakeSnapshotSource(gateClosed: true);
      var harness = await StartAsync(source, thresholdMs, consecutive);
      try
      {
        await source.WaitUntilParkedAsync();
      }
      catch
      {
        // The caller never receives the harness on this path, so nothing else
        // will stop the watchdog we just started.
        await harness.DisposeAsync();
        throw;
      }

      return harness;
    }

    /// <summary>
    /// Starts the watchdog with the gate open, so polls run freely and are merely
    /// counted. Used where the watchdog is not expected to poll at all.
    /// </summary>
    public static Task<WatchdogHarness> StartUngatedAsync(int thresholdMs, int consecutive)
      => StartAsync(new FakeSnapshotSource(gateClosed: false), thresholdMs, consecutive);

    private static async Task<WatchdogHarness> StartAsync(
      FakeSnapshotSource source, int thresholdMs, int consecutive)
    {
      // 10 ms is the watchdog's own floor (Math.Max(10, ...)). It sets how fast
      // the gated tests run and has no bearing on whether they pass.
      var watchdog = CreateWatchdog(source, thresholdMs, tickMs: 10, consecutive);
      await watchdog.StartAsync(CancellationToken.None);
      return new WatchdogHarness(source, watchdog);
    }

    public async ValueTask DisposeAsync()
    {
      // Order matters. Parking no longer blocks, and a null snapshot makes any
      // free-running teardown poll inert, so shutdown cannot alter counters a
      // test has already asserted on. Only then is it safe to stop and dispose:
      // StopAsync awaits ExecuteTask, so no poll is in flight afterwards.
      Source.Set(null);
      Source.OpenGate();
      await Watchdog.StopAsync(CancellationToken.None);
      Source.Dispose();
    }
  }

  /// <summary>
  /// Fake snapshot source that parks the watchdog at the start of every poll
  /// until the test grants it, so polls are counted rather than timed.
  /// </summary>
  private sealed class FakeSnapshotSource : ICaptureStreamSnapshotSource, IDisposable
  {
    private readonly SemaphoreSlim _pollPending = new(0);
    private readonly SemaphoreSlim _pollGranted = new(0);
    private readonly CancellationTokenSource _gateOpened = new();
    private readonly object _lock = new();
    private (string Address, long ElapsedMs)? _snapshot;
    private int _pollCount;
    private int _raiseCount;
    private string? _lastRaiseAddress;
    private long _lastRaiseElapsedMs;
    private int _lastRaiseConsecutive;

    public FakeSnapshotSource(bool gateClosed)
    {
      if (!gateClosed)
      {
        _gateOpened.Cancel();
      }
    }

    public int PollCount { get { lock (_lock) { return _pollCount; } } }
    public int RaiseCount { get { lock (_lock) { return _raiseCount; } } }
    public string? LastRaiseAddress { get { lock (_lock) { return _lastRaiseAddress; } } }
    public long LastRaiseElapsedMs { get { lock (_lock) { return _lastRaiseElapsedMs; } } }
    public int LastRaiseConsecutive { get { lock (_lock) { return _lastRaiseConsecutive; } } }

    public void Set((string Address, long ElapsedMs)? snapshot)
    {
      lock (_lock) { _snapshot = snapshot; }
    }

    public (string Address, long ElapsedMs)? GetCaptureStreamSnapshot()
    {
      if (!_gateOpened.IsCancellationRequested)
      {
        // Announce arrival before reading, so a test holding the pending signal
        // can still choose the value this poll will see.
        _pollPending.Release();
        try
        {
          if (!_pollGranted.Wait(GateTimeout, _gateOpened.Token))
          {
            throw new TimeoutException(
              "The test never granted a watchdog poll within " +
              $"{GateTimeout.TotalSeconds:0}s.");
          }
        }
        catch (OperationCanceledException)
        {
          // Gate opened during teardown — fall through and serve the poll.
        }
      }

      lock (_lock)
      {
        _pollCount++;
        return _snapshot;
      }
    }

    public void RaiseCaptureStreamStalled(string address, long elapsedMs, int consecutiveChecks)
    {
      lock (_lock)
      {
        _raiseCount++;
        _lastRaiseAddress = address;
        _lastRaiseElapsedMs = elapsedMs;
        _lastRaiseConsecutive = consecutiveChecks;
      }
    }

    /// <summary>
    /// Waits until the watchdog is parked at the start of a poll, before it has
    /// read the snapshot.
    /// </summary>
    public async Task WaitUntilParkedAsync()
    {
      if (!await _pollPending.WaitAsync(GateTimeout).ConfigureAwait(false))
      {
        throw new TimeoutException(
          $"The watchdog did not reach a poll within {GateTimeout.TotalSeconds:0}s.");
      }
    }

    /// <summary>
    /// Lets the watchdog complete exactly <paramref name="count"/> polls, each
    /// observing <paramref name="snapshot"/>.
    /// </summary>
    /// <remarks>
    /// Precondition and postcondition are identical: the watchdog is parked at
    /// the start of a poll, before reading. On return every granted poll has been
    /// fully processed — the counter update for the last one has already
    /// happened, because the watchdog only reaches the next park after
    /// processing — and it cannot advance again without another grant. That is
    /// what lets a caller assert on the counter with no wait.
    /// </remarks>
    public async Task GrantPollsAsync((string Address, long ElapsedMs)? snapshot, int count)
    {
      for (var i = 0; i < count; i++)
      {
        Set(snapshot);
        _pollGranted.Release();
        await WaitUntilParkedAsync().ConfigureAwait(false);
      }
    }

    /// <summary>Releases the gate permanently so teardown cannot deadlock.</summary>
    public void OpenGate()
    {
      if (!_gateOpened.IsCancellationRequested)
      {
        _gateOpened.Cancel();
      }
    }

    public void Dispose()
    {
      _gateOpened.Dispose();
      _pollPending.Dispose();
      _pollGranted.Dispose();
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
