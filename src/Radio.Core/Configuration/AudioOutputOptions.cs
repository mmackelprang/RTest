namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for audio outputs.
/// Loaded from the 'AudioOutput' configuration section.
/// </summary>
public class AudioOutputOptions
{
  /// <summary>
  /// The configuration section name.
  /// </summary>
  public const string SectionName = "AudioOutput";

  /// <summary>
  /// Gets or sets the local audio output options.
  /// </summary>
  public LocalAudioOutputOptions Local { get; set; } = new();

  /// <summary>
  /// Gets or sets the Google Cast output options.
  /// </summary>
  public GoogleCastOutputOptions GoogleCast { get; set; } = new();

  /// <summary>
  /// Gets or sets the HTTP stream output options.
  /// </summary>
  public HttpStreamOutputOptions HttpStream { get; set; } = new();
}

/// <summary>
/// Configuration options for local audio output.
/// </summary>
public class LocalAudioOutputOptions
{
  /// <summary>
  /// Gets or sets whether the local output is enabled by default.
  /// </summary>
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// Gets or sets the preferred device ID for local output.
  /// If empty, uses the system default device.
  /// </summary>
  public string PreferredDeviceId { get; set; } = "";

  /// <summary>
  /// Gets or sets the default volume level (0.0 to 1.0).
  /// </summary>
  public float DefaultVolume { get; set; } = 0.8f;
}

/// <summary>
/// Configuration options for Google Cast output.
/// </summary>
public class GoogleCastOutputOptions
{
  /// <summary>
  /// Gets or sets whether Google Cast output is enabled.
  /// </summary>
  public bool Enabled { get; set; } = false;

  /// <summary>
  /// Gets or sets the discovery timeout in seconds.
  /// </summary>
  public int DiscoveryTimeoutSeconds { get; set; } = 10;

  /// <summary>
  /// Gets or sets the preferred cast device name.
  /// If empty, uses the first discovered device.
  /// </summary>
  public string PreferredDeviceName { get; set; } = "";

  /// <summary>
  /// Gets or sets the default volume level for cast (0.0 to 1.0).
  /// </summary>
  public float DefaultVolume { get; set; } = 0.7f;

  /// <summary>
  /// Gets or sets whether to automatically reconnect on disconnect.
  /// </summary>
  public bool AutoReconnect { get; set; } = true;

  /// <summary>
  /// Gets or sets the reconnect delay in seconds.
  /// </summary>
  public int ReconnectDelaySeconds { get; set; } = 5;
}

/// <summary>
/// Configuration options for HTTP stream output.
/// </summary>
public class HttpStreamOutputOptions
{
  /// <summary>
  /// Gets or sets whether the HTTP stream output is enabled.
  /// </summary>
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// Gets or sets the HTTP stream server port.
  /// </summary>
  public int Port { get; set; } = 8080;

  /// <summary>
  /// Gets or sets the stream endpoint path.
  /// </summary>
  public string EndpointPath { get; set; } = "/stream/audio";

  /// <summary>
  /// Gets or sets the audio format for the stream.
  /// </summary>
  public string ContentType { get; set; } = "audio/wav";

  /// <summary>
  /// Gets or sets the sample rate for the stream.
  /// </summary>
  public int SampleRate { get; set; } = 48000;

  /// <summary>
  /// Gets or sets the number of channels for the stream.
  /// </summary>
  public int Channels { get; set; } = 2;

  /// <summary>
  /// Gets or sets the bits per sample for the stream.
  /// </summary>
  public int BitsPerSample { get; set; } = 16;

  /// <summary>
  /// Gets or sets the maximum number of concurrent clients.
  /// </summary>
  public int MaxConcurrentClients { get; set; } = 10;

  /// <summary>
  /// Gets or sets the buffer size in bytes for each client.
  /// </summary>
  public int ClientBufferSize { get; set; } = 65536;
}
