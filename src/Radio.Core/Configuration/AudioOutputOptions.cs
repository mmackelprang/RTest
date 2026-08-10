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

  /// <summary>
  /// Gets or sets device display options (filtering, friendly names).
  /// </summary>
  public DeviceDisplayOptions DeviceDisplay { get; set; } = new();
}

/// <summary>
/// Configuration for device list filtering and friendly name mapping.
/// </summary>
public class DeviceDisplayOptions
{
  /// <summary>
  /// Regex patterns for device names to hide from the UI.
  /// Matched against the raw device name (case-insensitive).
  /// Default: hides PulseAudio monitor devices.
  /// </summary>
  public List<string> HiddenDevicePatterns { get; set; } = ["^Monitor of "];

  /// <summary>
  /// Ordered list of device name mappings. The first matching entry
  /// (case-insensitive substring) wins. List ordering is guaranteed,
  /// unlike Dictionary enumeration.
  /// </summary>
  public List<DeviceNameMapping> FriendlyNames { get; set; } = [];

  /// <summary>
  /// Raw device names explicitly hidden by the user.
  /// Takes precedence over regex patterns (checked first).
  /// </summary>
  public List<string> HiddenDeviceNames { get; set; } = [];

  /// <summary>
  /// Raw device names explicitly shown by the user.
  /// Overrides regex HiddenDevicePatterns matches (force-show).
  /// </summary>
  public List<string> VisibleDeviceNames { get; set; } = [];

  /// <summary>
  /// Per-device friendly name overrides keyed by raw device name.
  /// Takes precedence over substring FriendlyNames mappings.
  /// </summary>
  public Dictionary<string, string> DeviceFriendlyNames { get; set; } = new();
}

/// <summary>
/// Maps a raw device name substring to a friendly display name.
/// </summary>
public class DeviceNameMapping
{
  /// <summary>
  /// Substring to match against raw device names (case-insensitive).
  /// </summary>
  public string Pattern { get; set; } = "";

  /// <summary>
  /// Display name to show when the pattern matches.
  /// </summary>
  public string FriendlyName { get; set; } = "";
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
  /// Gets or sets the Cast Application ID.
  /// Default is "CC1AD845" (Google's Default Media Receiver).
  /// Set to a custom receiver app ID for low-latency streaming
  /// (see deploy/cast-receiver/receiver.html).
  /// </summary>
  public string ApplicationId { get; set; } = "CC1AD845";

  /// <summary>
  /// Gets or sets the discovery timeout in seconds.
  /// </summary>
  public int DiscoveryTimeoutSeconds { get; set; } = 10;

  /// <summary>
  /// Gets or sets the default volume level for cast (0.0 to 1.0).
  /// </summary>
  public float DefaultVolume { get; set; } = 0.7f;

  /// <summary>
  /// Gets or sets how long startup waits for a persisted "google-cast" output
  /// to actually reach <see cref="Radio.Core.Interfaces.Audio.AudioOutputState.Streaming"/>
  /// before giving up and falling back to the local output.
  ///
  /// Selecting Cast in the output picker mutes the local sink; if the Cast
  /// device never connects, nothing unmutes it and the wired speakers stay
  /// silent indefinitely — across restarts, because the bad preference is
  /// persisted. This timeout is the watchdog that bounds that failure.
  ///
  /// Must comfortably exceed the connect chain's own bounded worst case, which
  /// is roughly 25 s before any unbounded network time: 3 s discovery settle +
  /// up to 6 s of port probing (two 3 s TCP timeouts in FindReachablePortAsync)
  /// + 250 ms receiver settle + ~16 s of media-load retries (5 s attempt, 1 s
  /// delay, 10 s retry). Default: 40 seconds, which leaves real headroom for a
  /// slow-but-working Chromecast rather than tearing one down mid-connect.
  /// </summary>
  public int StartupConnectTimeoutSeconds { get; set; } = 40;

  /// <summary>
  /// Gets or sets the file path for caching discovered Cast devices.
  /// </summary>
  public string CacheFilePath { get; set; } = "./data/config/cast-devices.json";

  /// <summary>
  /// Gets or sets how many days before a cached device is considered stale and removed.
  /// </summary>
  public int CacheExpirationDays { get; set; } = 14;

  /// <summary>
  /// Gets or sets the streaming mode for Cast audio.
  /// "HttpMp3" (default): audio flows via HTTP MP3 stream that the Cast device fetches.
  /// "DirectChannel": audio chunks are sent directly over the Cast protocol's custom
  /// message bus as Base64-encoded WAV, bypassing HTTP entirely.
  /// DirectChannel is experimental and requires a custom receiver app.
  /// </summary>
  public string StreamingMode { get; set; } = "HttpMp3";

  /// <summary>
  /// Gets or sets the chunk size in milliseconds for DirectChannel streaming.
  /// Smaller values reduce latency but increase message rate.
  /// Valid range: 50-200ms. At 48kHz stereo 16-bit (192KB/s PCM):
  ///   50ms  = ~9.6KB PCM, ~12.9KB Base64, 20 msgs/sec
  ///   100ms = ~19.2KB PCM, ~25.7KB Base64, 10 msgs/sec
  ///   200ms = ~38.4KB PCM, ~51.3KB Base64, 5 msgs/sec
  /// All sizes fit within the Cast protocol's 64KB message limit.
  /// </summary>
  public int DirectChannelChunkSizeMs { get; set; } = 100;

  /// <summary>
  /// Gets or sets the Cast custom namespace for DirectChannel audio streaming.
  /// The receiver app must listen on this same namespace.
  /// </summary>
  public string DirectChannelNamespace { get; set; } = "urn:x-cast:com.radioconsole.audio";

  /// <summary>
  /// Gets or sets the maximum seconds of buffer-ahead before drift protection drops chunks.
  /// Lower values reduce latency but cause more frequent chunk drops.
  /// Default: 3.0. Valid range: 1.0-5.0.
  /// </summary>
  public float DirectChannelMaxBufferAhead { get; set; } = 3.0f;

  /// <summary>
  /// Gets or sets the number of chunks to buffer before starting playback.
  /// Lower values reduce startup latency but increase risk of initial stutter.
  /// Default: 3. Valid range: 1-10.
  /// </summary>
  public int DirectChannelBufferBeforePlay { get; set; } = 3;

  /// <summary>
  /// Gets or sets the seconds of reader lag behind the write position.
  /// Lower values reduce latency but provide less cushion for jitter.
  /// Default: 1.0. Valid range: 0.1-2.0.
  /// </summary>
  public float DirectChannelReaderLagSeconds { get; set; } = 1.0f;
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

  /// <summary>
  /// Gets or sets the MP3 encoder bitrate in kbps for the Cast MP3 stream.
  /// Higher bitrates improve bass quality and reduce LAME encoder startup
  /// artifacts (the bit reservoir fills faster at higher rates).
  /// Default: 320kbps. Valid range: 128-320.
  /// </summary>
  public int Mp3Bitrate { get; set; } = 320;
}
