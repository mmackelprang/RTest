namespace Radio.API.Models;

/// <summary>
/// Configuration settings response.
/// </summary>
public class ConfigurationSettingsDto
{
  /// <summary>
  /// Gets or sets the audio configuration.
  /// </summary>
  public AudioConfigurationDto Audio { get; set; } = new();

  /// <summary>
  /// Gets or sets the visualizer configuration.
  /// </summary>
  public VisualizerConfigurationDto Visualizer { get; set; } = new();

  /// <summary>
  /// Gets or sets the output configuration.
  /// </summary>
  public OutputConfigurationDto Output { get; set; } = new();
}

/// <summary>
/// Audio system configuration.
/// </summary>
public class AudioConfigurationDto
{
  /// <summary>
  /// Gets or sets the default source.
  /// </summary>
  public string DefaultSource { get; set; } = "Spotify";

  /// <summary>
  /// Gets or sets the ducking percentage.
  /// </summary>
  public int DuckingPercentage { get; set; } = 20;

  /// <summary>
  /// Gets or sets the ducking policy.
  /// </summary>
  public string DuckingPolicy { get; set; } = "FadeSmooth";

  /// <summary>
  /// Gets or sets the ducking attack time in milliseconds.
  /// </summary>
  public int DuckingAttackMs { get; set; } = 100;

  /// <summary>
  /// Gets or sets the ducking release time in milliseconds.
  /// </summary>
  public int DuckingReleaseMs { get; set; } = 500;
}

/// <summary>
/// Visualizer configuration.
/// </summary>
public class VisualizerConfigurationDto
{
  /// <summary>
  /// Gets or sets the FFT size.
  /// </summary>
  public int FFTSize { get; set; } = 2048;

  /// <summary>
  /// Gets or sets the waveform sample count.
  /// </summary>
  public int WaveformSampleCount { get; set; } = 512;

  /// <summary>
  /// Gets or sets the peak hold time in milliseconds.
  /// </summary>
  public int PeakHoldTimeMs { get; set; } = 1000;

  /// <summary>
  /// Gets or sets whether to apply window function.
  /// </summary>
  public bool ApplyWindowFunction { get; set; } = true;

  /// <summary>
  /// Gets or sets the spectrum smoothing factor.
  /// </summary>
  public float SpectrumSmoothing { get; set; } = 0.5f;
}

/// <summary>
/// Output configuration.
/// </summary>
public class OutputConfigurationDto
{
  /// <summary>
  /// Gets or sets local output settings.
  /// </summary>
  public LocalOutputSettingsDto Local { get; set; } = new();

  /// <summary>
  /// Gets or sets HTTP stream output settings.
  /// </summary>
  public HttpStreamSettingsDto HttpStream { get; set; } = new();

  /// <summary>
  /// Gets or sets Google Cast output settings.
  /// </summary>
  public GoogleCastSettingsDto GoogleCast { get; set; } = new();
}

/// <summary>
/// Local output settings.
/// </summary>
public class LocalOutputSettingsDto
{
  /// <summary>
  /// Gets or sets whether local output is enabled.
  /// </summary>
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// Gets or sets the preferred device ID.
  /// </summary>
  public string PreferredDeviceId { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the default volume.
  /// </summary>
  public float DefaultVolume { get; set; } = 0.8f;
}

/// <summary>
/// HTTP stream output settings.
/// </summary>
public class HttpStreamSettingsDto
{
  /// <summary>
  /// Gets or sets whether HTTP streaming is enabled.
  /// </summary>
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// Gets or sets the stream port.
  /// </summary>
  public int Port { get; set; } = 8080;

  /// <summary>
  /// Gets or sets the endpoint path.
  /// </summary>
  public string EndpointPath { get; set; } = "/stream/audio";

  /// <summary>
  /// Gets or sets the sample rate.
  /// </summary>
  public int SampleRate { get; set; } = 48000;

  /// <summary>
  /// Gets or sets the number of channels.
  /// </summary>
  public int Channels { get; set; } = 2;
}

/// <summary>
/// Google Cast output settings.
/// </summary>
public class GoogleCastSettingsDto
{
  /// <summary>
  /// Gets or sets whether Google Cast is enabled.
  /// </summary>
  public bool Enabled { get; set; }

  /// <summary>
  /// Gets or sets the discovery timeout in seconds.
  /// </summary>
  public int DiscoveryTimeoutSeconds { get; set; } = 10;

  /// <summary>
  /// Gets or sets the default volume.
  /// </summary>
  public float DefaultVolume { get; set; } = 0.7f;
}

/// <summary>
/// Request to update configuration.
/// </summary>
public class UpdateConfigurationRequest
{
  /// <summary>
  /// Gets or sets the section to update.
  /// </summary>
  public string Section { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the key to update.
  /// </summary>
  public string Key { get; set; } = string.Empty;

  /// <summary>
  /// Gets or sets the value to set.
  /// </summary>
  public string Value { get; set; } = string.Empty;
}
