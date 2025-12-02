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
}
