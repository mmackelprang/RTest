using RTLSDRCore.Enums;
using RTLSDRCore.Tests.Sweep;

namespace RTLSDRCore.Tests.AmChain;

/// <summary>
/// Drives a started <see cref="RadioReceiver"/> over a <see cref="FakeSdrDevice"/> whose blocks
/// come from a <see cref="SyntheticRf"/>, one block at a time: each <see cref="Run"/> step raises
/// one block and waits for the DSP thread's audio event for that block (a rendezvous, not a
/// clock; the timeout is only a fail-safe against a hang).
/// </summary>
internal sealed class ReceiverHarness : IDisposable
{
  private readonly SemaphoreSlim _audioEvents = new(0);
  private readonly List<float> _audio = new();
  private readonly List<bool> _silentEvents = new();
  private readonly List<float[]> _events = new();

  public ReceiverHarness(SyntheticRf rf, BandType band, long frequencyHz)
  {
    Rf = rf;
    Device = new FakeSdrDevice { BlockFactory = rf.Block };
    Receiver = new RadioReceiver(Device);
    Assert.True(Receiver.SetBand(band, frequencyHz));   // device closed: recorded for the start
    Receiver.AudioDataAvailable += (_, e) =>
    {
      lock (_audio)
      {
        float[] block = e.Samples.AsSpan(0, e.SampleCount).ToArray();
        _audio.AddRange(block);
        _silentEvents.Add(Array.TrueForAll(block, s => s == 0f));
        _events.Add(block);
      }
      _audioEvents.Release();
      // Runs on the DSP thread: while the gate is closed the DSP thread is held here, so blocks
      // raised meanwhile wait in the receiver's processing queue (as under load on the box).
      DspGate.Wait(TimeSpan.FromSeconds(10));
    };
    Assert.True(Receiver.Startup());
  }

  public SyntheticRf Rf { get; }

  /// <summary>
  /// Holds the DSP thread inside its audio event while reset (closed). Open by default. The wait
  /// has a 10 s fail-safe so a test that forgets to open it cannot hang.
  /// </summary>
  public ManualResetEventSlim DspGate { get; } = new(true);

  /// <summary>Raises a block without waiting for its audio event (for building a queue backlog).</summary>
  public void RaiseOnly(long? capturedAtDeviceHz = null)
  {
    if (capturedAtDeviceHz is long hz)
    {
      Device.RaiseBlockCapturedAt(hz);
    }
    else
    {
      Device.RaiseBlock();
    }
  }

  /// <summary>Waits for <paramref name="count"/> audio events (a rendezvous, not a clock).</summary>
  public void WaitForEvents(int count)
  {
    for (int i = 0; i < count; i++)
    {
      Assert.True(_audioEvents.Wait(TimeSpan.FromSeconds(10)), "DSP thread produced no audio event");
    }
  }

  public FakeSdrDevice Device { get; }

  public RadioReceiver Receiver { get; }

  /// <summary>Raises <paramref name="blocks"/> blocks, one at a time, each followed by its audio event.</summary>
  public void Run(int blocks)
  {
    for (int i = 0; i < blocks; i++)
    {
      Device.RaiseBlock();
      Assert.True(_audioEvents.Wait(TimeSpan.FromSeconds(10)), "DSP thread produced no audio event");
    }
  }

  /// <summary>
  /// Raises one block captured at <paramref name="deviceHz"/> whatever the device is tuned to now
  /// (a block read before a retune and delivered after it), and waits for its audio event.
  /// </summary>
  public void RunCapturedAt(long deviceHz)
  {
    Device.RaiseBlockCapturedAt(deviceHz);
    Assert.True(_audioEvents.Wait(TimeSpan.FromSeconds(10)), "DSP thread produced no audio event");
  }

  /// <summary>Every audio sample delivered so far, in order (interleaved L,R for stereo).</summary>
  public float[] Audio
  {
    get
    {
      lock (_audio)
      {
        return _audio.ToArray();
      }
    }
  }

  /// <summary>Each audio event's samples so far, in order.</summary>
  public float[][] Events
  {
    get
    {
      lock (_audio)
      {
        return _events.ToArray();
      }
    }
  }

  /// <summary>For each audio event so far, in order: whether every sample in it was zero.</summary>
  public bool[] SilentEvents
  {
    get
    {
      lock (_audio)
      {
        return _silentEvents.ToArray();
      }
    }
  }

  public void Dispose()
  {
    DspGate.Set();
    Receiver.Dispose();
    _audioEvents.Dispose();
    DspGate.Dispose();
  }
}

/// <summary>Spectral measurements on the receiver's 48 kHz audio.</summary>
internal static class AudioMeasure
{
  public const int AudioRate = 48_000;

  /// <summary>
  /// The amplitude of the <paramref name="hz"/> component of <paramref name="x"/> (Goertzel over the
  /// whole span): a sine of amplitude a at an integer number of cycles in the span reads a.
  /// </summary>
  public static double ToneAmplitude(ReadOnlySpan<float> x, double hz, int sampleRate = AudioRate)
  {
    double w = 2.0 * Math.PI * hz / sampleRate;
    double coeff = 2.0 * Math.Cos(w);
    double s1 = 0;
    double s2 = 0;
    for (int n = 0; n < x.Length; n++)
    {
      double s0 = x[n] + coeff * s1 - s2;
      s2 = s1;
      s1 = s0;
    }
    double power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
    return 2.0 * Math.Sqrt(Math.Max(power, 0)) / x.Length;
  }

  /// <summary>RMS of the span.</summary>
  public static double Rms(ReadOnlySpan<float> x)
  {
    double sum = 0;
    foreach (float v in x)
    {
      sum += (double)v * v;
    }
    return Math.Sqrt(sum / x.Length);
  }

  public static double Db(double ratio) => 20.0 * Math.Log10(Math.Max(ratio, 1e-12));

  /// <summary>The last <paramref name="count"/> samples of <paramref name="audio"/>.</summary>
  public static float[] Tail(float[] audio, int count)
  {
    Assert.True(audio.Length >= count, $"only {audio.Length} audio samples, wanted {count}");
    return audio[^count..];
  }
}
