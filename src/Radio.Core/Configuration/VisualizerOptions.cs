namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for the audio visualizer service.
/// </summary>
public class VisualizerOptions
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "Visualizer";

  /// <summary>
  /// Gets or sets the FFT size for spectrum analysis.
  /// Must be a power of 2 (e.g., 256, 512, 1024, 2048, 4096).
  /// Larger values provide better frequency resolution but slower updates.
  /// Default is 2048.
  /// </summary>
  public int FFTSize { get; set; } = 2048;

  /// <summary>
  /// Gets or sets the number of waveform samples to keep in the buffer.
  /// Default is 512.
  /// </summary>
  public int WaveformSampleCount { get; set; } = 512;

  /// <summary>
  /// Gets or sets the peak hold time in milliseconds for level metering.
  /// Peaks will be held at their maximum value for this duration before decaying.
  /// Default is 1000ms.
  /// </summary>
  public int PeakHoldTimeMs { get; set; } = 1000;

  /// <summary>
  /// Gets or sets the peak decay rate per second (0.0 to 1.0).
  /// Higher values cause faster decay after peak hold expires.
  /// Default is 0.95 (fast decay).
  /// </summary>
  public float PeakDecayRate { get; set; } = 0.95f;

  /// <summary>
  /// Gets or sets the RMS smoothing factor (0.0 to 1.0).
  /// Higher values provide smoother, more stable RMS readings.
  /// Default is 0.3.
  /// </summary>
  public float RmsSmoothing { get; set; } = 0.3f;

  /// <summary>
  /// Gets or sets whether to apply windowing to FFT input.
  /// Default is true (Hann window).
  /// </summary>
  public bool ApplyWindowFunction { get; set; } = true;

  /// <summary>
  /// Gets or sets the minimum frequency to display in spectrum analysis (Hz).
  /// Default is 20 Hz.
  /// </summary>
  public float MinFrequency { get; set; } = 20f;

  /// <summary>
  /// Gets or sets the maximum frequency to display in spectrum analysis (Hz).
  /// Default is 20000 Hz.
  /// </summary>
  public float MaxFrequency { get; set; } = 20000f;

  /// <summary>
  /// Gets or sets the spectrum smoothing factor (0.0 to 1.0).
  /// Higher values provide smoother spectrum display.
  /// Default is 0.5.
  /// </summary>
  public float SpectrumSmoothing { get; set; } = 0.5f;

  /// <summary>
  /// Gets or sets the tilted level, in dBFS, that the spectrum and ring draw as an empty bar.
  /// Default is -65. Read live (IOptionsMonitor), so a config change applies without a restart.
  /// </summary>
  /// <remarks>
  /// With <see cref="SpectrumCeilingDbfs"/> this sets the window of levels that fills a bar. A window
  /// that is too wide makes the display flat — 70 dB (-80..-10) put most music at half height and a
  /// 6 dB swing moved a bar ~8%; measured on the box 2026-10-06, 40 dB with
  /// <see cref="SpectrumCurve"/> 1.5 restored the old contrast and colour spread.
  /// </remarks>
  public float SpectrumFloorDbfs { get; set; } = -65f;

  /// <summary>
  /// Gets or sets the tilted level, in dBFS, that draws as a full bar. Default is -25. Must be above
  /// <see cref="SpectrumFloorDbfs"/>; otherwise both fall back to their defaults.
  /// </summary>
  public float SpectrumCeilingDbfs { get; set; } = -25f;

  /// <summary>
  /// Gets or sets the exponent applied to the 0..1 bar height after the dB window. Default is 1.5.
  /// Above 1 it shortens the quieter bars more than the loud ones, which adds contrast (and colour
  /// variety, since bar colour follows height); 1 is the plain dB scale. Must be positive.
  /// </summary>
  public float SpectrumCurve { get; set; } = 1.5f;

  /// <summary>
  /// Gets or sets the gain, in dB per octave about 1 kHz, that compensates music's natural fall-off
  /// with frequency. Default is 3. 0 disables it.
  /// </summary>
  public float SpectrumTiltDbPerOctave { get; set; } = 3f;
}
