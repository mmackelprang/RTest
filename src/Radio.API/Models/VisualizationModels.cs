using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Visualization;

namespace Radio.API.Models;

/// <summary>
/// Spectrum visualization data for SignalR broadcasts: the FFT grouped into log-spaced display bands.
/// </summary>
/// <remarks>
/// Not the raw FFT bins. <see cref="FromBins"/> groups them with <see cref="LogSpectrumBands"/>
/// (128 bands, 40 Hz – 16 kHz, tilted absolute dBFS), which is what the spectrum and ring visualizers draw —
/// and is ~2.5 KB of JSON per frame where the raw 1024 bins plus 1024 frequencies were ~20–25 KB.
/// </remarks>
public class SpectrumDataDto
{
  /// <summary>
  /// Gets or sets the display value of each band, 0.0–1.0: absolute dBFS with a +3 dB/octave tilt,
  /// mapped from −80 dBFS (0) to −10 dBFS (1). See <see cref="LogSpectrumBands"/>.
  /// </summary>
  public float[] Magnitudes { get; set; } = [];

  /// <summary>
  /// Gets or sets the centre frequency of each band in Hz.
  /// </summary>
  public float[] Frequencies { get; set; } = [];

  /// <summary>
  /// Gets or sets the number of bands (the length of <see cref="Magnitudes"/>).
  /// </summary>
  public int BinCount { get; set; }

  /// <summary>
  /// Gets or sets the FFT's resolution (Hz per underlying FFT bin, not per band).
  /// </summary>
  public float FrequencyResolution { get; set; }

  /// <summary>
  /// Gets or sets the FFT's maximum frequency (Nyquist), not the top band's edge.
  /// </summary>
  public float MaxFrequency { get; set; }

  /// <summary>
  /// Gets or sets the timestamp.
  /// </summary>
  public long TimestampMs { get; set; }

  /// <summary>
  /// Builds the DTO from the analyzer's raw bins, grouping them into display bands. The one mapping
  /// for both the hub's <c>GetSpectrum</c> and the broadcast stream, so the two cannot drift apart.
  /// </summary>
  public static SpectrumDataDto FromBins(SpectrumData data)
  {
    (float[] magnitudes, float[] frequencies) = LogSpectrumBands.Compute(data.Magnitudes, data.FrequencyResolution);
    return new SpectrumDataDto
    {
      Magnitudes = magnitudes,
      Frequencies = frequencies,
      BinCount = magnitudes.Length,
      FrequencyResolution = data.FrequencyResolution,
      MaxFrequency = data.MaxFrequency,
      TimestampMs = data.Timestamp.ToUnixTimeMilliseconds()
    };
  }
}

/// <summary>
/// Level meter data for SignalR broadcasts.
/// Contains peak and RMS level measurements for both channels.
/// </summary>
/// <remarks>
/// Linear values are in the 0.0-1.0 range where 1.0 represents digital full scale.
/// dB values are in dBFS (decibels relative to full scale), where 0 dBFS is the maximum level.
/// </remarks>
public class LevelDataDto
{
  /// <summary>
  /// Gets or sets the left channel peak level (0.0 to 1.0, where 1.0 is full scale).
  /// </summary>
  public float LeftPeak { get; set; }

  /// <summary>
  /// Gets or sets the right channel peak level (0.0 to 1.0, where 1.0 is full scale).
  /// </summary>
  public float RightPeak { get; set; }

  /// <summary>
  /// Gets or sets the left channel RMS level (0.0 to 1.0, where 1.0 is full scale).
  /// </summary>
  public float LeftRms { get; set; }

  /// <summary>
  /// Gets or sets the right channel RMS level (0.0 to 1.0, where 1.0 is full scale).
  /// </summary>
  public float RightRms { get; set; }

  /// <summary>
  /// Gets or sets the left channel peak level in dBFS (≤ 0, where 0 is full scale).
  /// </summary>
  public float LeftPeakDb { get; set; }

  /// <summary>
  /// Gets or sets the right channel peak level in dBFS (≤ 0, where 0 is full scale).
  /// </summary>
  public float RightPeakDb { get; set; }

  /// <summary>
  /// Gets or sets whether audio is clipping (at or near maximum level).
  /// </summary>
  public bool IsClipping { get; set; }

  /// <summary>
  /// Gets or sets the timestamp.
  /// </summary>
  public long TimestampMs { get; set; }
}

/// <summary>
/// Waveform data for SignalR broadcasts.
/// Contains time-domain samples for oscilloscope-style display.
/// </summary>
/// <remarks>
/// Sample values are in the -1.0 to 1.0 range representing the audio amplitude.
/// The number of samples depends on the WaveformSampleCount configuration setting.
/// </remarks>
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
