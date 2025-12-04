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

  /// <summary>Duration of audio to capture for fingerprinting (seconds).</summary>
  public int SampleDurationSeconds { get; set; } = 15;

  /// <summary>Interval between identification attempts (seconds).</summary>
  public int IdentificationIntervalSeconds { get; set; } = 30;

  /// <summary>Minimum confidence threshold for accepting a match (0.0 to 1.0).</summary>
  public double MinimumConfidenceThreshold { get; set; } = 0.5;

  /// <summary>Minutes to suppress duplicate identifications of the same track.</summary>
  public int DuplicateSuppressionMinutes { get; set; } = 5;

  /// <summary>AcoustID API configuration.</summary>
  public AcoustIdOptions AcoustId { get; set; } = new();

  /// <summary>MusicBrainz API configuration.</summary>
  public MusicBrainzOptions MusicBrainz { get; set; } = new();

  /// <summary>
  /// SQLite database path for fingerprint cache.
  /// </summary>
  public string DatabasePath { get; set; } = "./data/fingerprints.db";
}

/// <summary>
/// Configuration options for AcoustID API.
/// </summary>
public sealed class AcoustIdOptions
{
  /// <summary>AcoustID API key (register at https://acoustid.org/new-application).</summary>
  public string ApiKey { get; set; } = string.Empty;

  /// <summary>AcoustID API base URL.</summary>
  public string BaseUrl { get; set; } = "https://api.acoustid.org/v2";

  /// <summary>Maximum requests per second (AcoustID limit is 3).</summary>
  public int MaxRequestsPerSecond { get; set; } = 3;

  /// <summary>Request timeout in seconds.</summary>
  public int TimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Configuration options for MusicBrainz API.
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
