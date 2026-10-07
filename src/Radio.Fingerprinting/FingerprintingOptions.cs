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
  /// <b>No longer read by anything.</b> It used to make Bluetooth and file sources run SongRec (Shazam) even
  /// when their own metadata was complete. Since the call policy, a known-start source asks SongRec exactly
  /// when its title, artist <i>or album art</i> is missing — and since Bluetooth never supplies art on the
  /// appliance (AUD-17), a Bluetooth track still gets the recognition that fetches its art without this flag.
  /// The owner's rule is that a track whose metadata is complete makes no calls, so nothing may force them.
  /// </summary>
  /// <remarks>
  /// ⚠ <b>Kept, not renamed and not removed, deliberately.</b> The appliance's SQLite config store holds a
  /// <c>fingerprinting:useShazamForAllSources</c> row and its <c>appsettings.Production.json</c> sets it
  /// (AUD-1), and the Web's <c>FingerprintingConfigDto</c> still carries the field, so the System Config
  /// page writes back whatever it loaded. Removing it would change none of that and gain nothing. Its value
  /// has no effect: setting it false no longer costs Bluetooth album art, and setting it true no longer
  /// adds calls.
  /// </remarks>
  public bool UseShazamForAllSources { get; set; } = false;

  /// <summary>
  /// Duration of audio to capture for fingerprinting (seconds). How often a capture starts is decided by
  /// the call policy (<see cref="UnknownStartIntervalSeconds"/> and the <c>KnownStart*</c> settings), whose
  /// intervals are measured start-to-start and so include this capture. (<c>IdentificationIntervalSeconds</c>,
  /// an older setting read by nothing but a start-up log line, was removed by AUD-36; the call policy
  /// settings replace it under new names, so that orphaned store row stays unread.)
  /// </summary>
  public int SampleDurationSeconds { get; set; } = 13;

  /// <summary>
  /// The longest (ms, minimum 100) the identification loop waits before re-checking the active source when
  /// it is not capturing — no active source, no lookup needed, a capture that returned no samples, or a
  /// scheduled attempt not yet due (it then waits until the attempt is due, or this long, whichever is
  /// sooner). <c>RequestImmediateIdentification</c> cancels the wait. AUD-35: without this wait the loop
  /// spun a CPU core indefinitely on the appliance.
  /// </summary>
  public int IdlePollIntervalMs { get; set; } = 1000;

  // --- SongRec call policy (FingerprintCallPolicy). Every interval below is measured from the START of one
  // attempt's capture to the start of the next ("start-to-start"), so the SampleDurationSeconds capture and
  // the SongRec call itself fit inside it. All are read live through IOptionsMonitor on every scheduling
  // decision, so a config-store change applies without a restart (an interval change takes effect from the
  // attempt after the one already scheduled). Values below the
  // documented minimum are clamped up to it.

  /// <summary>
  /// Known-start sources (file player, Bluetooth — the source knows when each track begins): seconds after
  /// the track starts before the first capture for it begins. Only used while the track's own metadata is
  /// missing a title, an artist or album art; a track whose metadata is complete is never sent to SongRec.
  /// Minimum 0.
  /// </summary>
  public int KnownStartFirstCallDelaySeconds { get; set; } = 5;

  /// <summary>
  /// Known-start sources: seconds between the first attempt for a track and the retry after it found no
  /// match. Later retries use <see cref="KnownStartRetryIntervalSeconds"/>. Minimum 1.
  /// </summary>
  public int KnownStartFirstRetryDelaySeconds { get; set; } = 30;

  /// <summary>
  /// Known-start sources: seconds between retries after the second and later no-matches for the same track.
  /// Minimum 1.
  /// </summary>
  public int KnownStartRetryIntervalSeconds { get; set; } = 60;

  /// <summary>
  /// Known-start sources: once SongRec has matched the current track, seconds between validation calls that
  /// confirm the match for the rest of the track. A validation naming a different song is raised like any
  /// new identification. Minimum 1.
  /// </summary>
  public int KnownStartValidationIntervalSeconds { get; set; } = 60;

  /// <summary>
  /// Unknown-start sources (radio, vinyl, USB and anything else): seconds between attempts, whether or not
  /// the last one matched. The default 15 s is the 13 s <see cref="SampleDurationSeconds"/> capture plus ~2 s
  /// for the SongRec call: 4 calls/minute, ~240/hour — the whole <see cref="MaxCallsPerHour"/> budget. A
  /// source change, a re-tune, or a capture that found only silence makes the next attempt immediate, and
  /// that attempt RESTARTS the schedule (the following one is this interval after it) rather than adding a
  /// call on top, so sustained use stays within the budget and the cap is only a backstop. When the capture
  /// plus the call take longer than this interval, the next attempt simply starts as soon as the last ends.
  /// Minimum 1.
  /// </summary>
  public int UnknownStartIntervalSeconds { get; set; } = 15;

  /// <summary>
  /// Hard cap on SongRec calls in any rolling 60-minute window, across every source. When it is reached,
  /// attempts wait until the oldest call in the window ages out, and one Warning is logged per episode.
  /// Minimum 1.
  /// </summary>
  public int MaxCallsPerHour { get; set; } = 240;

  /// <summary>
  /// Back-off after the first consecutive SongRec failure (timeout, non-zero exit, unparsable output),
  /// doubling with each further failure up to <see cref="ErrorBackoffMaxSeconds"/>. No SongRec process is
  /// started while backing off. Reset by the next call that runs cleanly (match or no-match). Minimum 1.
  /// </summary>
  public int ErrorBackoffInitialSeconds { get; set; } = 30;

  /// <summary>Ceiling for the SongRec failure back-off, in seconds. Never below <see cref="ErrorBackoffInitialSeconds"/>.</summary>
  public int ErrorBackoffMaxSeconds { get; set; } = 600;

  /// <summary>
  /// Consecutive SongRec failures after which one Warning is logged (possible Shazam throttling or a ban).
  /// Logged once per run of failures, not once per failure. Minimum 1.
  /// </summary>
  public int ErrorWarnThreshold { get; set; } = 5;

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
