using System.Reflection;
using Radio.Infrastructure.Audio.SoundFlow;
using SoundFlow.Backends.MiniAudio;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// Tests for <see cref="NativeAudioDeviceGate"/> and <see cref="SerializedMiniAudioEngine"/> —
/// the choke point that keeps two threads out of MiniAudio's PulseAudio main loop.
///
/// The production failure these guard against is a native <c>abort()</c>, which no managed
/// test can reproduce directly. So they assert the two properties that actually prevent it:
/// (1) no two callers are ever inside a gated region at the same time, and (2) the shared
/// engine type routes its enumeration through the gate.
/// </summary>
public class NativeAudioDeviceGateTests
{
  /// <summary>
  /// The headline property. Without the lock, several of these threads observe each other
  /// inside the region and <c>maxObserved</c> climbs above 1 — which in production is two
  /// threads inside <c>pa_mainloop_iterate</c> and a SIGABRT.
  /// </summary>
  [Fact]
  public void Run_NeverAllowsTwoCallersInsideTheRegionAtOnce()
  {
    const int threadCount = 8;
    var concurrent = 0;
    var maxObserved = 0;
    var ready = new Barrier(threadCount);

    var threads = Enumerable.Range(0, threadCount).Select(_ => new Thread(() =>
    {
      // Start all threads together so they genuinely contend.
      ready.SignalAndWait();

      for (var i = 0; i < 10; i++)
      {
        NativeAudioDeviceGate.Run(() =>
        {
          var now = Interlocked.Increment(ref concurrent);

          // Track the high-water mark of simultaneous occupants.
          int seen;
          while (now > (seen = Volatile.Read(ref maxObserved)))
          {
            Interlocked.CompareExchange(ref maxObserved, now, seen);
          }

          // Widen the window an unsynchronized implementation would race through.
          // Sleep rather than SpinWait: it widens the window by far more per unit of
          // wall-clock (so it detects a missing lock more reliably) while burning no CPU,
          // which keeps this test from starving the wall-clock-sensitive timing tests that
          // xUnit runs in parallel with it in this same assembly.
          Thread.Sleep(1);

          Interlocked.Decrement(ref concurrent);
        });
      }
    })).ToList();

    foreach (var thread in threads)
    {
      thread.Start();
    }

    foreach (var thread in threads)
    {
      Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Gated worker did not finish — possible deadlock");
    }

    Assert.Equal(1, Volatile.Read(ref maxObserved));
    Assert.Equal(0, Volatile.Read(ref concurrent));
  }

  /// <summary>
  /// The gate must be re-entrant: <see cref="SerializedMiniAudioEngine.Create"/> holds it
  /// across construction, and the base constructor's own enumeration dispatches straight back
  /// into the overridden <c>UpdateAudioDevicesInfo</c>. A non-re-entrant primitive
  /// (SemaphoreSlim) would self-deadlock on that path.
  /// </summary>
  [Fact]
  public void Run_IsReentrantOnTheSameThread()
  {
    var completed = false;

    var worker = new Thread(() =>
    {
      NativeAudioDeviceGate.Run(() =>
      {
        NativeAudioDeviceGate.Run(() =>
        {
          NativeAudioDeviceGate.Run(() => { completed = true; });
        });
      });
    });

    worker.Start();

    Assert.True(worker.Join(TimeSpan.FromSeconds(10)), "Re-entrant Run deadlocked");
    Assert.True(completed);
  }

  /// <summary>
  /// An exception from the native call must not strand the gate — otherwise the first
  /// enumeration failure would wedge every later one, including the hot-plug timer.
  /// </summary>
  [Fact]
  public void Run_ReleasesTheGateWhenTheCallbackThrows()
  {
    Assert.Throws<InvalidOperationException>(
      () => NativeAudioDeviceGate.Run(() => throw new InvalidOperationException("native boom")));

    var reacquired = false;
    var worker = new Thread(() => NativeAudioDeviceGate.Run(() => { reacquired = true; }));
    worker.Start();

    Assert.True(worker.Join(TimeSpan.FromSeconds(10)), "Gate was not released after a throwing callback");
    Assert.True(reacquired);
  }

  [Fact]
  public void Run_ReturnsTheCallbackResult()
  {
    var result = NativeAudioDeviceGate.Run(() => 42);
    Assert.Equal(42, result);
  }

  [Fact]
  public void Run_CountsContendedEntries()
  {
    var before = NativeAudioDeviceGate.ContendedEntryCount;
    var entered = new ManualResetEventSlim(false);
    var release = new ManualResetEventSlim(false);

    var holder = new Thread(() => NativeAudioDeviceGate.Run(() =>
    {
      entered.Set();
      release.Wait(TimeSpan.FromSeconds(10));
    }));
    holder.Start();

    Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

    var waiter = new Thread(() => NativeAudioDeviceGate.Run(() => { }));
    waiter.Start();

    // Give the waiter time to actually block on the gate.
    Thread.Sleep(150);
    release.Set();

    Assert.True(waiter.Join(TimeSpan.FromSeconds(10)));
    Assert.True(holder.Join(TimeSpan.FromSeconds(10)));

    Assert.True(NativeAudioDeviceGate.ContendedEntryCount > before,
      "A caller that had to wait for the gate should be counted as contended");
  }

  /// <summary>
  /// Serialization is a property of the engine type, not of the call sites: this is what
  /// makes the call sites in <c>SoundFlowAudioEngine</c> — and any call site added later —
  /// safe without anyone remembering to take a lock. Deleting the override would leave those
  /// call sites hitting the native layer unsynchronized, so assert the override exists.
  /// </summary>
  [Fact]
  public void SerializedMiniAudioEngine_OverridesUpdateAudioDevicesInfo()
  {
    var method = typeof(SerializedMiniAudioEngine)
      .GetMethod(nameof(MiniAudioEngine.UpdateAudioDevicesInfo), BindingFlags.Public | BindingFlags.Instance);

    Assert.NotNull(method);
    Assert.Equal(typeof(SerializedMiniAudioEngine), method!.DeclaringType);
  }

  /// <summary>
  /// Construction must go through the gated factory, so an ungated engine cannot be
  /// introduced by writing <c>new</c>.
  /// </summary>
  [Fact]
  public void SerializedMiniAudioEngine_ExposesNoPublicConstructor()
  {
    var publicConstructors = typeof(SerializedMiniAudioEngine)
      .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

    Assert.Empty(publicConstructors);
  }
}
