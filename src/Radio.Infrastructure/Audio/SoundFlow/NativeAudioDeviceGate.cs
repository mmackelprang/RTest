namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// Process-wide serialization gate for every native MiniAudio call that drives a
/// PulseAudio main loop.
///
/// <para><b>Why this exists.</b> MiniAudio's PulseAudio backend enumerates devices by
/// pumping <c>pa_mainloop_iterate</c> on the context's <c>pa_mainloop</c>. That main
/// loop is a single-threaded state machine — libpulse asserts on its internal state
/// and aborts the process if two threads drive it at once. On 2026-08-10 <c>radio-api</c>
/// took SIGABRT twice from exactly that: a volume-slider drag wrote the config store on
/// every tick, each write fired <c>IOptionsMonitor&lt;AudioOutputOptions&gt;.OnChange</c>,
/// and each change kicked off an unserialized device enumeration on a fresh thread pool
/// thread. The core dump caught two threads inside the same main loop —
/// <c>ma_context_get_devices</c> on one, <c>ma_context_get_device_info</c> on the other —
/// asserting in <c>mainloop.c:808</c> and <c>pstream.c:873</c> across the two crashes.</para>
///
/// <para><b>Why a distinct lock.</b> <c>SoundFlowDeviceManager._devicesLock</c> guards only
/// the managed cache assignment; the native call sat outside it. This gate is the separate
/// lock that is held <i>across the native call itself</i>, which is the only thing that
/// keeps two threads out of the main loop.</para>
///
/// <para><b>Why static.</b> The gate must be shared by everything that can reach a native
/// context, including the temporary <c>MiniAudioEngine</c> instances the device manager
/// creates before the shared engine is available. A per-instance lock would let a temporary
/// engine's enumeration overlap the shared engine's.</para>
///
/// <para><b>Why <see cref="Monitor"/> rather than <c>SemaphoreSlim</c>.</b> Monitor is
/// re-entrant on the owning thread, so a gated region that reaches another gated region cannot
/// self-deadlock — e.g. a caller wrapping <c>UpdateAudioDevicesInfo</c> plus its device snapshot
/// in its own <see cref="Run{T}"/> while the engine override takes the gate again underneath.
/// Every gated region is fully synchronous, so nothing needs to hold the gate across an
/// <c>await</c>.</para>
///
/// <para><b>Keep gated regions narrow.</b> The gate is process-wide, so anything held inside it
/// is serialized across every audio path at once. Gate the individual native call, never a
/// surrounding operation that merely contains one. Wrapping <c>MiniAudioEngine</c> construction
/// (a multi-second backend probe) was tried and reverted — see
/// <see cref="SerializedMiniAudioEngine.Create"/>.</para>
///
/// <para><b>Scope.</b> This gate covers device <i>enumeration</i> and engine construction —
/// the surface the core dump implicates. Device lifecycle calls that also touch the same main
/// loop (<c>AudioPlaybackDevice.Start/Stop/Dispose</c>) live on SoundFlow types this gate
/// cannot intercept and are deliberately out of scope here; see the PR discussion.</para>
/// </summary>
internal static class NativeAudioDeviceGate
{
  private static readonly object SyncRoot = new();

  private static long _totalEntries;
  private static long _contendedEntries;

  /// <summary>
  /// Total number of gated regions entered since process start. Diagnostic only.
  /// </summary>
  internal static long TotalEntryCount => Interlocked.Read(ref _totalEntries);

  /// <summary>
  /// Number of gated regions that had to wait because another thread held the gate.
  /// A non-zero value is expected and healthy — it is the count of aborts avoided.
  /// Diagnostic only.
  /// </summary>
  internal static long ContendedEntryCount => Interlocked.Read(ref _contendedEntries);

  /// <summary>
  /// Runs <paramref name="action"/> with exclusive access to the native audio device layer.
  /// </summary>
  internal static void Run(Action action)
  {
    ArgumentNullException.ThrowIfNull(action);

    Run<object?>(() =>
    {
      action();
      return null;
    });
  }

  /// <summary>
  /// Runs <paramref name="func"/> with exclusive access to the native audio device layer
  /// and returns its result.
  /// </summary>
  /// <remarks>
  /// Callers should keep the gated region as small as possible: take the native call plus
  /// whatever device snapshot it produces, then do managed filtering/mapping outside.
  /// </remarks>
  internal static T Run<T>(Func<T> func)
  {
    ArgumentNullException.ThrowIfNull(func);

    Interlocked.Increment(ref _totalEntries);

    var lockTaken = false;
    try
    {
      // Non-blocking attempt first purely so contention is observable. On a
      // re-entrant call this succeeds and bumps Monitor's recursion count, which
      // the single Exit below unwinds.
      Monitor.TryEnter(SyncRoot, 0, ref lockTaken);
      if (!lockTaken)
      {
        Interlocked.Increment(ref _contendedEntries);
        Monitor.Enter(SyncRoot, ref lockTaken);
      }

      return func();
    }
    finally
    {
      if (lockTaken)
      {
        Monitor.Exit(SyncRoot);
      }
    }
  }
}
