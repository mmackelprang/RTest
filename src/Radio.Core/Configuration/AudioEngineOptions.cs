namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for the audio engine.
/// </summary>
public class AudioEngineOptions
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "AudioEngine";

  /// <summary>
  /// Gets or sets the sample rate in Hz. Default is 48000.
  /// </summary>
  public int SampleRate { get; set; } = 48000;

  /// <summary>
  /// Gets or sets the number of audio channels. Default is 2 (stereo).
  /// </summary>
  public int Channels { get; set; } = 2;

  /// <summary>
  /// Gets or sets the buffer size in samples. Default is 1024.
  /// </summary>
  public int BufferSize { get; set; } = 1024;

  /// <summary>
  /// Gets or sets the hot-plug detection interval in seconds. Default is 5.
  /// </summary>
  public int HotPlugIntervalSeconds { get; set; } = 5;

  /// <summary>
  /// Gets or sets the ring buffer size for the output stream in seconds. Default is 5.
  /// </summary>
  public int OutputBufferSizeSeconds { get; set; } = 5;

  /// <summary>
  /// Gets or sets whether hot-plug detection is enabled. Default is true.
  /// </summary>
  public bool EnableHotPlugDetection { get; set; } = true;
}
