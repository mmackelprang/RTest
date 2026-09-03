namespace Radio.Core.Configuration;

/// <summary>
/// Server-side GV media fetch, caching and event-playback limits (ADR-029 D8, §10.2).
///
/// <para>
/// This section deliberately does NOT reuse <c>PhoneIntegration:ContactsApiBaseUrl</c>, even though
/// both point at the same host today: that key means "where the contacts API is", and overloading
/// it would couple two features that can be deployed and disabled independently
/// (<c>PhoneIntegration:Enabled</c> is <c>false</c> and has never been true).
/// </para>
/// </summary>
public sealed class GvMediaOptions
{
  /// <summary>Configuration section name.</summary>
  public const string SectionName = "GvMedia";

  /// <summary>
  /// Master gate for the RemoteMedia arm. Consumed by PR 3's EventPlaybackService and, in this PR,
  /// by GvMediaClient, which refuses to fetch when false.
  /// </summary>
  public bool Enabled { get; set; } = false;

  /// <summary>Base URL of the gvbridge host. Consumed by GvMediaClient.</summary>
  public string BaseUrl { get; set; } = "http://radio:5004";

  /// <summary>
  /// Value for the X-RotaryPhone-Auth header. Empty means no header is sent, which matches the
  /// current LAN-only posture; set it when RotaryPhone's gate ships (ADR-022 §8.1, ADR-029 §10.1).
  ///
  /// <para>
  /// This is the API-side twin of Radio.Web's RotaryPhone:Gv:AuthKey. ⚠ The two are NOT one shared
  /// file: /opt/radio-console/api/ and /opt/radio-console/web/ each hold their OWN
  /// appsettings.Production.json, and on the appliance they have already diverged — measured
  /// 2026-09-02 at 1057 B (mtime 2026-03-05) and 75 B (mtime 2026-07-31), with different content —
  /// because Deploy-ToLinux.ps1 excludes that file from rsync and seeds it only when it is absent.
  /// Setting the secret is therefore two hand edits on two files, and a mismatch surfaces only as a
  /// 401 on voicemail playback. See design/INTEGRATIONS.md for the runbook.
  /// </para>
  ///
  /// <para>
  /// ⚠ The boot check does not catch that mismatch, and it is worth being exact about why, because
  /// the obvious reading is wrong. GvMediaStartupCheck warns only when THIS key is EMPTY; two
  /// non-empty keys that differ — which is what "a mismatch" means, and the only state that
  /// produces the 401 above — pass it in silence, since Radio.API cannot see Radio.Web's overlay at
  /// all. Its remarks state the same limitation from the other side.
  /// </para>
  /// </summary>
  public string AuthKey { get; set; } = "";

  /// <summary>Where fetched recordings are written. Consumed by GvMediaCache.</summary>
  public string CacheDirectory { get; set; } = "./data/gvmedia";

  /// <summary>
  /// Cache cap in megabytes. 50 holds roughly 35-100 recordings — comfortably the whole visible
  /// list. 0 is the supported escape hatch and means NO CACHE: recordings are still written (a
  /// local path is what playback needs) but no fetch is ever served from disk, and a short-TTL
  /// sweep reclaims them. Choosing 0 re-exposes replay to the ~9-in-20-minute GV auth blackout
  /// (ADR-029 §5.3, ⟨A1·2⟩).
  /// </summary>
  public int CacheMaxMegabytes { get; set; } = 50;

  /// <summary>
  /// Hard cap on one attended playback. Consumed by PR 5 (ADR-029 D7 §7.1). In this PR it is used
  /// only to bound the download size and the no-cache sweep window.
  /// </summary>
  public int MaxPlaybackSeconds { get; set; } = 300;

  /// <summary>
  /// Cap on EventPlaybackRequest.Text, passed to Validate by PR 3's controller.
  /// ⚠ The behaviour is REJECTION, not truncation: over-length text is refused as
  /// EventPlaybackRejection.TextTooLong and mapped to a 400 with that reason. ADR-029 §4.2 says
  /// "truncated with a spoken tail"; that is overridden, because §4.2's own rule is that utterance
  /// composition belongs to Radio.Web, and a server that silently speaks less than it was asked to
  /// while returning 200 is the same untruth PR 1 refused when it made a non-seekable SeekAsync
  /// throw rather than no-op. Radio.Web truncates visibly before sending (PHN-3).
  /// </summary>
  public int MaxSpeechChars { get; set; } = 1000;

  /// <summary>
  /// Priority at or above which a starting source preempts attended playback. Consumed by PR 4
  /// (ADR-029 D5 §6.1). Not read by this PR.
  /// </summary>
  public int PreemptAtPriority { get; set; } = 8;

  /// <summary>HTTP timeout for one media fetch. Consumed by GvMediaClient.</summary>
  public int FetchTimeoutSeconds { get; set; } = 15;
}
