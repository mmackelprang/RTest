namespace Radio.API.Models;

/// <summary>
/// Spectrum visualization data for SignalR broadcasts.
/// </summary>
public class SpectrumDataDto
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
  /// Gets or sets the timestamp.
  /// </summary>
  public long TimestampMs { get; set; }
}

/// <summary>
/// Level meter data for SignalR broadcasts.
/// </summary>
public class LevelDataDto
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
  /// Gets or sets whether audio is clipping.
  /// </summary>
  public bool IsClipping { get; set; }

  /// <summary>
  /// Gets or sets the timestamp.
  /// </summary>
  public long TimestampMs { get; set; }
}

/// <summary>
/// Waveform data for SignalR broadcasts.
/// </summary>
public class WaveformDataDto
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
  /// Gets or sets the duration in milliseconds.
  /// </summary>
  public double DurationMs { get; set; }

  /// <summary>
  /// Gets or sets the timestamp.
  /// </summary>
  public long TimestampMs { get; set; }
}

/// <summary>
/// Combined visualization data for SignalR broadcasts.
/// </summary>
public class VisualizationDataDto
{
  /// <summary>
  /// Gets or sets the spectrum data.
  /// </summary>
  public SpectrumDataDto? Spectrum { get; set; }

  /// <summary>
  /// Gets or sets the level data.
  /// </summary>
  public LevelDataDto? Levels { get; set; }

  /// <summary>
  /// Gets or sets the waveform data.
  /// </summary>
  public WaveformDataDto? Waveform { get; set; }

  /// <summary>
  /// Gets or sets whether visualization is active.
  /// </summary>
  public bool IsActive { get; set; }
}
