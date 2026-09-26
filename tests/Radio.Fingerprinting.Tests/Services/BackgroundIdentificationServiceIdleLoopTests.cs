using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Fingerprinting.Services;

namespace Radio.Fingerprinting.Tests.Services;

/// <summary>
/// AUD-35: when there is nothing to identify, the identification loop must wait rather than spin.
/// Measured on the appliance before the fix: one thread-pool thread at 99.9 % CPU, indefinitely.
/// </summary>
/// <remarks>
/// Each test pays the service's fixed 5 s start-up delay once and synchronises on the tap being polled —
/// the loop's own observation. The spin test then measures over a 2 s wall-clock window, a bounded check
/// that is safe in its direction (see its remarks).
/// </remarks>
public class BackgroundIdentificationServiceIdleLoopTests
{
  /// <summary>The three ways a cycle can return without capturing samples.</summary>
  public enum NothingToCapture
  {
    /// <summary>The tap reports no active source.</summary>
    SourceInactive,

    /// <summary>Active, but the source does not need a lookup — the appliance's steady state.</summary>
    NoLookupNeeded,

    /// <summary>Active and needing a lookup, but the capture returns no samples at once.</summary>
    CaptureReturnsNothing
  }

  /// <summary>A tap that counts how often the loop asks whether it is active (once per cycle).</summary>
  private sealed class CountingTap(NothingToCapture mode) : IAudioSampleProvider
  {
    private int _polls;
    private readonly List<(int AtPoll, TaskCompletionSource Reached)> _waiters = new();

    public int Polls => Volatile.Read(ref _polls);

    public bool IsActive
    {
      get
      {
        var n = Interlocked.Increment(ref _polls);
        lock (_waiters)
        {
          foreach (var w in _waiters.Where(w => n >= w.AtPoll))
          {
            w.Reached.TrySetResult();
          }
        }
        return mode != NothingToCapture.SourceInactive;
      }
    }

    public Task WhenPolledAsync(int count)
    {
      var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      lock (_waiters)
      {
        if (Polls >= count)
        {
          tcs.TrySetResult();
        }
        else
        {
          _waiters.Add((count, tcs));
        }
      }
      return tcs.Task;
    }

    public string SourceName => "Idle";
    public PlaySource SourceType => PlaySource.File;
    public string? SourceFilePath => null;
    public bool NeedsFingerprintingLookup => mode == NothingToCapture.CaptureReturnsNothing;
    public Task<AudioSampleBuffer?> CaptureAsync(TimeSpan duration, CancellationToken ct = default) =>
      Task.FromResult<AudioSampleBuffer?>(null);
  }

  private static BackgroundIdentificationService CreateService(CountingTap tap, FingerprintingOptions options)
  {
    var services = new ServiceCollection();
    services.AddSingleton<IAudioSampleProvider>(tap);
    var monitor = new Mock<IOptionsMonitor<FingerprintingOptions>>();
    monitor.Setup(o => o.CurrentValue).Returns(options);
    return new BackgroundIdentificationService(
      new Mock<ILogger<BackgroundIdentificationService>>().Object,
      services.BuildServiceProvider(),
      monitor.Object);
  }

  /// <summary>
  /// ⚠ Deliberately a bounded check on a wall-clock window, and safe in that shape: CPU starvation can
  /// only LOWER the poll count, so starvation can weaken this test but never fail it. Before the fix the
  /// loop polled the tap hundreds of thousands of times in the same window.
  /// </summary>
  [Theory]
  [InlineData(NothingToCapture.SourceInactive)]
  [InlineData(NothingToCapture.NoLookupNeeded)]
  [InlineData(NothingToCapture.CaptureReturnsNothing)]
  public async Task NothingToIdentify_TheLoopWaitsInsteadOfSpinning(NothingToCapture mode)
  {
    var tap = new CountingTap(mode);
    using var service = CreateService(tap, new FingerprintingOptions { Enabled = true, IdlePollIntervalMs = 500 });

    await service.StartAsync(CancellationToken.None);
    try
    {
      await tap.WhenPolledAsync(1).WaitAsync(TimeSpan.FromSeconds(60));
      var pollsAtStart = tap.Polls;

      await Task.Delay(TimeSpan.FromSeconds(2));

      // At a 500 ms idle poll, ~4 polls fit in 2 s. 20 leaves room for scheduling jitter while still
      // being four orders of magnitude below a spinning loop.
      Assert.InRange(tap.Polls - pollsAtStart, 0, 20);
    }
    finally
    {
      await service.StopAsync(CancellationToken.None);
    }
  }

  /// <summary>
  /// The idle wait must not delay a track change: RequestImmediateIdentification cuts it short, as it
  /// already does for the SongRec back-off wait.
  /// </summary>
  [Fact]
  public async Task RequestImmediateIdentification_CutsTheIdleWaitShort()
  {
    var tap = new CountingTap(NothingToCapture.SourceInactive);
    // An idle poll far longer than the test's own safety timeout: the next poll can only arrive in time
    // if the request cancelled the wait.
    using var service = CreateService(tap, new FingerprintingOptions { Enabled = true, IdlePollIntervalMs = 600_000 });

    await service.StartAsync(CancellationToken.None);
    try
    {
      await tap.WhenPolledAsync(1).WaitAsync(TimeSpan.FromSeconds(60));
      var nextPoll = tap.WhenPolledAsync(tap.Polls + 1);

      // The loop may not have entered its idle wait yet when we ask; keep asking until it moves on.
      // Each request is harmless — it only cancels a wait that is already under way.
      // A safety net only (the idle poll is 10 min): the rendezvous is the poll, not the clock.
      var deadline = DateTime.UtcNow.AddSeconds(60);
      while (!nextPoll.IsCompleted && DateTime.UtcNow < deadline)
      {
        service.RequestImmediateIdentification();
        await Task.WhenAny(nextPoll, Task.Delay(50));
      }

      Assert.True(nextPoll.IsCompleted, "RequestImmediateIdentification did not end the idle wait");
    }
    finally
    {
      await service.StopAsync(CancellationToken.None);
    }
  }
}
