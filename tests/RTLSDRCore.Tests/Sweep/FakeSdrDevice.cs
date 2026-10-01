using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Models;

namespace RTLSDRCore.Tests.Sweep;

/// <summary>
/// An <see cref="ISdrDevice"/> with no thread of its own: blocks are raised
/// only when a test calls <see cref="RaiseBlock"/>, and every configuration
/// call is recorded in order.
/// </summary>
internal sealed class FakeSdrDevice : ISdrDevice
{
  public const long StationHz = 99_500_000;
  public const int BlockSize = 16384;

  private readonly object _gate = new();
  private readonly List<string> _calls = new();
  private long _frequencyHz;

  public DeviceInfo DeviceInfo { get; } = new()
  {
    Name = "Fake SDR",
    Type = DeviceType.Mock,
    IsAvailable = true,
    MinFrequencyHz = 24_000_000,
    MaxFrequencyHz = 1_766_000_000,
  };

  public bool IsOpen { get; private set; }

  public bool IsStreaming { get; private set; }

  public bool OpenResult { get; set; } = true;

  public bool Disposed { get; private set; }

  /// <summary>Returned by successive ReadSamples calls; null entries mean "fill the buffer".</summary>
  public Queue<int?> ReadCounts { get; } = new();

  /// <summary>Builds the block served at a frequency. Defaults to a strong tone at the station, weak noise elsewhere.</summary>
  public Func<long, int, IqSample[]> BlockFactory { get; set; } = DefaultBlock;

  public event EventHandler<IqSamplesEventArgs>? SamplesAvailable;

  public event EventHandler<DeviceErrorEventArgs>? ErrorOccurred
  {
    add { }
    remove { }
  }

  public IReadOnlyList<string> Calls
  {
    get
    {
      lock (_gate)
      {
        return _calls.ToArray();
      }
    }
  }

  public IReadOnlyList<long> SetFrequencyCalls =>
    Calls.Where(c => c.StartsWith("SetFrequency:", StringComparison.Ordinal))
      .Select(c => long.Parse(c["SetFrequency:".Length..], System.Globalization.CultureInfo.InvariantCulture))
      .ToArray();

  public static IqSample[] DefaultBlock(long frequencyHz, int count)
  {
    IqSample[] noise = SweepTestSignals.Noise(count, 0.01f, seed: (int)(frequencyHz / 100_000));
    return frequencyHz == StationHz
      ? SweepTestSignals.Add(SweepTestSignals.Tone(count, 40_000, 0.5f), noise)
      : noise;
  }

  /// <summary>A block loud enough to open the receiver's squelch and demodulate to non-silent audio.</summary>
  public static IqSample[] StrongBlock(long frequencyHz, int count) =>
    SweepTestSignals.Add(SweepTestSignals.Tone(count, 40_000, 0.5f), SweepTestSignals.Noise(count, 0.3f));

  public void RaiseBlock()
  {
    long hz;
    lock (_gate)
    {
      hz = _frequencyHz;
    }
    RaiseBlockCapturedAt(hz);
  }

  /// <summary>
  /// Raises a block as if it had been captured while tuned to
  /// <paramref name="frequencyHz"/> — models a block read before a retune
  /// and delivered after it.
  /// </summary>
  public void RaiseBlockCapturedAt(long frequencyHz)
  {
    SamplesAvailable?.Invoke(this, new IqSamplesEventArgs(BlockFactory(frequencyHz, BlockSize)));
  }

  private void Record(string call)
  {
    lock (_gate)
    {
      _calls.Add(call);
    }
  }

  public bool Open()
  {
    Record("Open");
    IsOpen = OpenResult;
    return OpenResult;
  }

  public void Close()
  {
    Record("Close");
    IsStreaming = false;
    IsOpen = false;
  }

  public bool SetFrequency(long frequencyHz)
  {
    lock (_gate)
    {
      _calls.Add($"SetFrequency:{frequencyHz}");
      _frequencyHz = frequencyHz;
    }
    return true;
  }

  public long GetFrequency()
  {
    lock (_gate)
    {
      return _frequencyHz;
    }
  }

  public bool SetSampleRate(int sampleRate)
  {
    Record($"SetSampleRate:{sampleRate}");
    return true;
  }

  public int GetSampleRate() => 240_000;

  public bool SetGainMode(bool automatic)
  {
    Record($"SetGainMode:{automatic}");
    return true;
  }

  public bool SetGain(float gainDb)
  {
    Record($"SetGain:{gainDb.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    return true;
  }

  public float GetGain() => 0f;

  public bool SetFrequencyCorrection(int ppm) => true;

  public int GetFrequencyCorrection() => 0;

  public bool StartStreaming()
  {
    Record("StartStreaming");
    IsStreaming = true;
    return true;
  }

  public void StopStreaming()
  {
    Record("StopStreaming");
    IsStreaming = false;
  }

  public int ReadSamples(Span<IqSample> buffer)
  {
    int? requested = ReadCounts.Count > 0 ? ReadCounts.Dequeue() : null;
    int count = Math.Min(requested ?? buffer.Length, buffer.Length);
    Record($"ReadSamples:{count}");
    if (count <= 0)
    {
      return 0;
    }
    BlockFactory(GetFrequency(), count).CopyTo(buffer);
    return count;
  }

  public void Dispose()
  {
    Record("Dispose");
    Disposed = true;
    IsOpen = false;
  }
}
