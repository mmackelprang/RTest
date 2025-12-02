namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Service for real-time audio visualization.
/// Provides spectrum analysis (FFT), level metering (VU), and waveform display data.
/// </summary>
public interface IVisualizerService : IDisposable
{
  /// <summary>
  /// Gets the current spectrum data (FFT frequency bins).
  /// </summary>
  /// <returns>Spectrum analysis data.</returns>
  SpectrumData GetSpectrumData();

  /// <summary>
  /// Gets the current audio levels (peak and RMS).
  /// </summary>
  /// <returns>Level meter data.</returns>
  LevelData GetLevelData();

  /// <summary>
  /// Gets the current waveform samples for display.
  /// </summary>
  /// <returns>Waveform data for time-domain visualization.</returns>
  WaveformData GetWaveformData();

  /// <summary>
  /// Gets whether the visualizer is actively processing audio.
  /// </summary>
  bool IsActive { get; }

  /// <summary>
  /// Gets the configured sample rate.
  /// </summary>
  int SampleRate { get; }

  /// <summary>
  /// Gets the configured FFT size.
  /// </summary>
  int FFTSize { get; }

  /// <summary>
  /// Processes incoming audio samples for visualization.
  /// </summary>
  /// <param name="samples">The audio samples to process (float -1.0 to 1.0).</param>
  void ProcessSamples(float[] samples);

  /// <summary>
  /// Processes incoming audio samples for visualization.
  /// </summary>
  /// <param name="samples">The audio samples span to process.</param>
  /// <param name="count">The number of samples to process.</param>
  void ProcessSamples(Span<float> samples, int count);

  /// <summary>
  /// Resets all visualization data to initial state.
  /// </summary>
  void Reset();
}

/// <summary>
/// Represents spectrum analysis data from FFT processing.
/// </summary>
public sealed class SpectrumData
{
  /// <summary>
  /// Gets or sets the magnitude values for each frequency bin (0.0 to 1.0).
  /// </summary>
  public float[] Magnitudes { get; set; } = [];

  /// <summary>
  /// Gets or sets the frequency value for each bin in Hz.
  /// </summary>
  public float[] Frequencies { get; set; } = [];

  /// <summary>
  /// Gets or sets the number of frequency bins.
  /// </summary>
  public int BinCount { get; set; }

  /// <summary>
  /// Gets or sets the frequency resolution (Hz per bin).
  /// </summary>
  public float FrequencyResolution { get; set; }

  /// <summary>
  /// Gets or sets the maximum frequency represented.
  /// </summary>
  public float MaxFrequency { get; set; }

  /// <summary>
  /// Gets or sets the timestamp of this data.
  /// </summary>
  public DateTimeOffset Timestamp { get; set; }

  /// <summary>
  /// Creates empty spectrum data with the specified bin count.
  /// </summary>
  /// <param name="binCount">The number of frequency bins.</param>
  /// <param name="sampleRate">The sample rate in Hz.</param>
  /// <param name="fftSize">The FFT size.</param>
  /// <returns>An initialized SpectrumData instance.</returns>
  public static SpectrumData CreateEmpty(int binCount, int sampleRate, int fftSize)
  {
    var frequencies = new float[binCount];
    var frequencyResolution = (float)sampleRate / fftSize;

    for (var i = 0; i < binCount; i++)
    {
      frequencies[i] = i * frequencyResolution;
    }

    return new SpectrumData
    {
      Magnitudes = new float[binCount],
      Frequencies = frequencies,
      BinCount = binCount,
      FrequencyResolution = frequencyResolution,
      MaxFrequency = binCount * frequencyResolution,
      Timestamp = DateTimeOffset.UtcNow
    };
  }
}

/// <summary>
/// Represents audio level data for VU metering.
/// </summary>
public sealed class LevelData
{
  /// <summary>
  /// Gets or sets the left channel peak level (0.0 to 1.0).
  /// </summary>
  public float LeftPeak { get; set; }

  /// <summary>
  /// Gets or sets the right channel peak level (0.0 to 1.0).
  /// </summary>
  public float RightPeak { get; set; }

  /// <summary>
  /// Gets or sets the left channel RMS level (0.0 to 1.0).
  /// </summary>
  public float LeftRms { get; set; }

  /// <summary>
  /// Gets or sets the right channel RMS level (0.0 to 1.0).
  /// </summary>
  public float RightRms { get; set; }

  /// <summary>
  /// Gets or sets the left channel peak level in decibels (dBFS).
  /// </summary>
  public float LeftPeakDb { get; set; }

  /// <summary>
  /// Gets or sets the right channel peak level in decibels (dBFS).
  /// </summary>
  public float RightPeakDb { get; set; }

  /// <summary>
  /// Gets or sets the left channel RMS level in decibels (dBFS).
  /// </summary>
  public float LeftRmsDb { get; set; }

  /// <summary>
  /// Gets or sets the right channel RMS level in decibels (dBFS).
  /// </summary>
  public float RightRmsDb { get; set; }

  /// <summary>
  /// Gets or sets the mono/combined peak level (0.0 to 1.0).
  /// </summary>
  public float MonoPeak { get; set; }

  /// <summary>
  /// Gets or sets the mono/combined RMS level (0.0 to 1.0).
  /// </summary>
  public float MonoRms { get; set; }

  /// <summary>
  /// Gets or sets whether audio is clipping (peak >= 1.0).
  /// </summary>
  public bool IsClipping { get; set; }

  /// <summary>
  /// Gets or sets the timestamp of this data.
  /// </summary>
  public DateTimeOffset Timestamp { get; set; }

  /// <summary>
  /// Creates empty level data with minimum values.
  /// </summary>
  /// <returns>An initialized LevelData instance with zero levels.</returns>
  public static LevelData CreateEmpty()
  {
    return new LevelData
    {
      LeftPeak = 0f,
      RightPeak = 0f,
      LeftRms = 0f,
      RightRms = 0f,
      LeftPeakDb = float.NegativeInfinity,
      RightPeakDb = float.NegativeInfinity,
      LeftRmsDb = float.NegativeInfinity,
      RightRmsDb = float.NegativeInfinity,
      MonoPeak = 0f,
      MonoRms = 0f,
      IsClipping = false,
      Timestamp = DateTimeOffset.UtcNow
    };
  }
}

/// <summary>
/// Represents waveform data for time-domain visualization.
/// </summary>
public sealed class WaveformData
{
  /// <summary>
  /// Gets or sets the left channel sample values (-1.0 to 1.0).
  /// </summary>
  public float[] LeftSamples { get; set; } = [];

  /// <summary>
  /// Gets or sets the right channel sample values (-1.0 to 1.0).
  /// </summary>
  public float[] RightSamples { get; set; } = [];

  /// <summary>
  /// Gets or sets the number of samples.
  /// </summary>
  public int SampleCount { get; set; }

  /// <summary>
  /// Gets or sets the time span represented by the samples.
  /// </summary>
  public TimeSpan Duration { get; set; }

  /// <summary>
  /// Gets or sets the timestamp of this data.
  /// </summary>
  public DateTimeOffset Timestamp { get; set; }

  /// <summary>
  /// Creates empty waveform data with the specified sample count.
  /// </summary>
  /// <param name="sampleCount">The number of samples per channel.</param>
  /// <param name="sampleRate">The sample rate for duration calculation.</param>
  /// <returns>An initialized WaveformData instance.</returns>
  public static WaveformData CreateEmpty(int sampleCount, int sampleRate)
  {
    return new WaveformData
    {
      LeftSamples = new float[sampleCount],
      RightSamples = new float[sampleCount],
      SampleCount = sampleCount,
      Duration = TimeSpan.FromSeconds((double)sampleCount / sampleRate),
      Timestamp = DateTimeOffset.UtcNow
    };
  }
}
