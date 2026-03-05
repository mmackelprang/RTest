namespace Radio.Core.Configuration;

/// <summary>
/// Configuration options for the audio fingerprinting system.
/// </summary>
public sealed class FingerprintingOptions
{
  /// <summary>Configuration section name for binding.</summary>
  public const string SectionName = "Fingerprinting";

  /// <summary>Enable or disable automatic fingerprinting.</summary>
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// When true, uses Shazam (SongRec) for metadata and cover art on ALL sources,
  /// even when AVRCP/ID3 tags provide title and artist. Shazam typically returns
  /// higher-quality cover art from Apple Music CDN.
  /// </summary>
  public bool UseShazamForAllSources { get; set; } = false;

  /// <summary>Duration of audio to capture for fingerprinting (seconds).</summary>
  public int SampleDurationSeconds { get; set; } = 15;

  /// <summary>Interval between identification attempts (seconds).</summary>
  public int IdentificationIntervalSeconds { get; set; } = 15;

  /// <summary>Minimum confidence threshold for accepting a match (0.0 to 1.0).</summary>
  public double MinimumConfidenceThreshold { get; set; } = 0.5;

  /// <summary>Minutes to suppress duplicate identifications of the same track.</summary>
  public int DuplicateSuppressionMinutes { get; set; } = 5;

  /// <summary>Minutes to suppress duplicate identifications for high-confidence matches (score > 0.9).</summary>
  public int HighConfidenceDuplicateSuppressionMinutes { get; set; } = 30;

  /// <summary>
  /// Minimum seconds between song change events.
  /// Prevents rapid-fire entry creation from noisy fingerprints at song boundaries.
  /// </summary>
  public int MinimumSecondsBetweenSongChanges { get; set; } = 20;

  /// <summary>MusicBrainz API configuration (used for cover art search).</summary>
  public MusicBrainzOptions MusicBrainz { get; set; } = new();

  /// <summary>
  /// SQLite database path for fingerprint cache.
  /// </summary>
  public string DatabasePath { get; set; } = "./data/fingerprints.db";

  /// <summary>SongRec (Shazam) recognizer configuration.</summary>
  public SongRecOptions SongRec { get; set; } = new();
}

/// <summary>
/// Configuration options for SongRec (Shazam) audio recognition.
/// SongRec is the sole recognizer for all audio sources (radio, vinyl, Bluetooth, USB, file).
/// Install via: sudo add-apt-repository ppa:marin-m/songrec &amp;&amp; sudo apt install songrec
/// </summary>
public sealed class SongRecOptions
{
  /// <summary>Enable or disable SongRec recognition.</summary>
  public bool Enabled { get; set; } = true;

  /// <summary>
  /// Path to the songrec binary. If empty, searches PATH.
  /// </summary>
  public string SongRecPath { get; set; } = string.Empty;

  /// <summary>Timeout in seconds for the songrec process.</summary>
  public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// Configuration options for MusicBrainz API (used for cover art search).
/// </summary>
public sealed class MusicBrainzOptions
{
  /// <summary>MusicBrainz API base URL.</summary>
  public string BaseUrl { get; set; } = "https://musicbrainz.org/ws/2";

  /// <summary>Application name for User-Agent header.</summary>
  public string ApplicationName { get; set; } = "RadioConsole";

  /// <summary>Application version for User-Agent header.</summary>
  public string ApplicationVersion { get; set; } = "1.0.0";

  /// <summary>Contact email for User-Agent header.</summary>
  public string ContactEmail { get; set; } = string.Empty;

  /// <summary>Maximum requests per second (MusicBrainz limit is 1 for anonymous).</summary>
  public int MaxRequestsPerSecond { get; set; } = 1;

  /// <summary>Request timeout in seconds.</summary>
  public int TimeoutSeconds { get; set; } = 10;
}
