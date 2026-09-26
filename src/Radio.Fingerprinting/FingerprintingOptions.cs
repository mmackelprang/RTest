namespace Radio.Fingerprinting;

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
  /// When true, runs SongRec (Shazam) on ALL sources, even when AVRCP or ID3 tags already
  /// supply a title and artist. This is a <b>gate</b>: it decides whether fingerprinting runs
  /// at all. It does <b>not</b> decide what is done with the answer.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠ <b>Since AUD-1 there is no "overwrite" decision to pair this with.</b> A fingerprint result
  /// may only fill fields the source left missing, decided per field by
  /// <c>Radio.Core.Models.Audio.SourceMetadataPrecedence</c>. Turning this flag off does not stop
  /// source metadata being replaced — nothing replaces it any more — it only stops fingerprinting
  /// for tracks whose source already supplied a title and artist.
  /// </para>
  /// <para>
  /// ⚠ <b>Do not set this false on the appliance.</b> Bluetooth fingerprinting then hard-returns for
  /// any track whose AVRCP supplied a title and artist, so no cover art is ever found. AVRCP has
  /// <b>never</b> supplied cover art there: the Bluetooth service reads the MPRIS attribute names
  /// <c>ArtUrl</c>/<c>mpris:artUrl</c> from a proxy on <c>org.bluez.MediaPlayer1</c>, which publishes
  /// <c>ImgHandle</c> (AUD-17). SongRec is the only art source Bluetooth has.
  /// </para>
  /// <para>
  /// ⚠ <b>The name is imprecise and is kept deliberately.</b> It reads as "use Shazam's answer", but
  /// it only decides whether Shazam is <i>asked</i>. Renaming it would orphan the
  /// <c>fingerprinting:useShazamForAllSources</c> row in the appliance's SQLite config store —
  /// which outranks both JSON layers — and the live <c>appsettings.Production.json</c>, which the
  /// deploy never overwrites. The renamed key would then fall through to this <c>false</c> default
  /// and take Bluetooth album art with it. <c>fingerprinting:fpcalcPath</c> is already orphaned in
  /// that store from the AcoustID→SongRec rename, so this is observed, not hypothetical (AUD-1).
  /// </para>
  /// </remarks>
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
