using Radio.Core.Configuration;
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
  /// Gets or sets the display value of each band, 0.0–1.0: absolute dBFS with a dB/octave tilt, mapped
  /// through a dB window and a contrast curve (defaults +3 dB/oct, −65..−25 dBFS, 1.5; configurable via
  /// <see cref="VisualizerOptions"/>). See <see cref="LogSpectrumBands"/>.
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
  /// <param name="data">The analyzer's raw bins.</param>
  /// <param name="options">
  /// The display scale (<c>Spectrum*</c> values). Read per call, so a live config change applies on
  /// the next frame. An unusable combination falls back to the defaults rather than throwing: this
  /// runs 20 times a second, and one bad config value must not turn into an exception per frame.
  /// </param>
  public static SpectrumDataDto FromBins(SpectrumData data, VisualizerOptions? options = null)
  {
    (float floor, float ceiling, float curve, float tilt) = DisplayScale(options);
    (float[] magnitudes, float[] frequencies) = LogSpectrumBands.Compute(
      data.Magnitudes,
      data.FrequencyResolution,
      floorDbfs: floor,
      ceilingDbfs: ceiling,
      tiltDbPerOctave: tilt,
      curve: curve);
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

  // The configured scale, or the defaults for any part that is unusable (NaN/infinite, a ceiling not
  // above the floor, a non-positive curve). The window falls back as a pair so a half-valid window
  // cannot invert.
  private static (float Floor, float Ceiling, float Curve, float Tilt) DisplayScale(VisualizerOptions? o)
  {
    if (o is null)
    {
      return (LogSpectrumBands.DefaultFloorDbfs, LogSpectrumBands.DefaultCeilingDbfs,
        LogSpectrumBands.DefaultCurve, LogSpectrumBands.DefaultTiltDbPerOctave);
    }

    bool windowOk = float.IsFinite(o.SpectrumFloorDbfs) && float.IsFinite(o.SpectrumCeilingDbfs)
      && o.SpectrumCeilingDbfs > o.SpectrumFloorDbfs;
    return (
      windowOk ? o.SpectrumFloorDbfs : LogSpectrumBands.DefaultFloorDbfs,
      windowOk ? o.SpectrumCeilingDbfs : LogSpectrumBands.DefaultCeilingDbfs,
      float.IsFinite(o.SpectrumCurve) && o.SpectrumCurve > 0f ? o.SpectrumCurve : LogSpectrumBands.DefaultCurve,
      float.IsFinite(o.SpectrumTiltDbPerOctave) ? o.SpectrumTiltDbPerOctave : LogSpectrumBands.DefaultTiltDbPerOctave);
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
