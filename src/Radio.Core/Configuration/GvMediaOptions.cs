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
  /// Hard cap on one attended playback (ADR-029 D7 §7.1). Read by
  /// <c>EventPlaybackService.ArmDurationCap</c>, which arms a one-shot timer when the source starts
  /// producing audio and stops the playback when it fires — with no client cooperation, no heartbeat
  /// and no poll. D5 rule 1 bounds the count of armed timers at one. Two other readers derive bounds
  /// from it, in different types: <c>GvMediaClient</c> turns it into the maximum response size it
  /// will accept, and <c>GvMediaCache</c> turns it into the no-cache sweep window.
  ///
  /// <para>
  /// ⚠ There is no "off". A value below 1 clamps to 1 rather than disabling the cap: this is the one
  /// stop condition that survives every client going away, and the arc already has a worked example
  /// of a knob that disables a feature while leaving it looking intact (see PreemptAtPriority).
  /// </para>
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
  /// Priority at or above which a starting event source stops attended playback (ADR-029 D5 §6.1).
  /// Read by <c>EventPlaybackService.OnDuckingStateChanged</c>, which stops an in-flight playback when
  /// such a source starts. ⚠ The mirror direction — a playback STARTING while such a source is
  /// already sounding — is not implemented yet: it mixes. The owner's decision is that it must WAIT
  /// for the blocking source and then play, and that lands with the server-owned playback state that
  /// can broadcast a waiting state to a client. Do not implement it as a refusal; that option was
  /// considered and rejected.
  ///
  /// <para>
  /// ⚠ This value is safe to LOWER and a trap to RAISE, and only the lowering case is argued in the
  /// ADR. ⚠ Be exact about WHY, because the obvious reason is not the live one: every source that
  /// reaches this rule had its priority set explicitly. All four <c>StartDuckingAsync</c> call sites in
  /// the tree — three in <c>AnnouncementService</c>, one in <c>EventPlaybackService</c> — call
  /// <c>SetPriority</c> on the same source immediately before, so <c>GetPriority</c>'s category-default
  /// fallback is never what answers a start raise.
  ///
  /// What makes 9 a trap is where the live 8 comes from: <c>NotificationsController.Announce</c>
  /// clamps <c>request.Priority ?? 8</c>, so every external notification that does not name a priority
  /// — the doorbell, the laundry, anything Home Assistant posts — arrives at exactly 8. Raise this to
  /// 9 and all of those silently stop preempting, while the dormant <c>PhoneIntegration:RingPriority</c>
  /// (9) still would, so the feature keeps looking intact while it has stopped happening for everything
  /// that can actually make a sound on this box. 7 widens it to the documented high-importance band.
  /// Keep this at or below <c>DuckingService.DefaultEventPriority</c>, which is what ADR-029 §6.1
  /// anchored the number on; a test pins those two compile-time defaults against each other, and it
  /// can see neither a per-machine override nor the controller's <c>?? 8</c>.
  /// </para>
  /// </summary>
  public int PreemptAtPriority { get; set; } = 8;

  /// <summary>HTTP timeout for one media fetch. Consumed by GvMediaClient.</summary>
  public int FetchTimeoutSeconds { get; set; } = 15;
}
