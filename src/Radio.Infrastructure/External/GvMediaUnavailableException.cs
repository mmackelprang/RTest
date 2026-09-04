namespace Radio.Infrastructure.External;

/// <summary>
/// Why a GV media fetch could not produce a local file.
///
/// <para>
/// This enum exists because collapsing every failure into one exception is a bug this repo already
/// carries twice: GV-6 and GV-8 are both open rows whose shared root shape is "maps every non-2xx
/// to null, destroying the distinction the caller needs." A 404 (the recording is gone) and a 502
/// (GV auth is inside its ~9-minutes-in-20 blackout) demand opposite responses from the UI.
/// </para>
/// </summary>
public enum GvMediaFailure
{
  /// <summary>No reason was supplied. Never thrown deliberately.</summary>
  Unknown = 0,

  /// <summary>GvMedia:Enabled is false. No request was made.</summary>
  Disabled,

  /// <summary>
  /// The provider returned 404. ⚠ This does NOT mean the recording is gone. RotaryPhone's audio
  /// route answers 404 both when a recording genuinely has no media and when its authenticated
  /// voicemail list failed — which is what a Google Voice auth blackout looks like from here. See
  /// <see cref="GvMediaUnavailableException.IsPermanent"/> for the code path. Treat as retryable.
  /// </summary>
  NotFound,

  /// <summary>
  /// The provider returned 401 or 403. On this box that most likely means GvMedia:AuthKey and
  /// RotaryPhone's expected key have diverged. ⚠ That state has no boot-time signal:
  /// GvMediaStartupCheck warns only on an EMPTY GvMedia:AuthKey, never on two differing non-empty
  /// keys — it cannot read Radio.Web's per-machine overlay. This exception is the whole diagnosis.
  /// </summary>
  Unauthorized,

  /// <summary>Any other non-success status, 5xx included. Usually the GV auth blackout; retryable.</summary>
  Upstream,

  /// <summary>The fetch exceeded GvMedia:FetchTimeoutSeconds. Retryable.</summary>
  Timeout,

  /// <summary>DNS, connection or TLS failure below HTTP. Retryable.</summary>
  Transport,

  /// <summary>The response exceeded the size bound derived from GvMedia:MaxPlaybackSeconds.</summary>
  TooLarge
}

/// <summary>
/// Thrown by <see cref="GvMediaClient"/> when it cannot produce a local file for a recording.
/// </summary>
/// <remarks>
/// ⚠ <see cref="Exception.Message"/> is masked: it carries the hashed id form, never the raw
/// media id. Callers log exceptions, and an unmasked message would leak through every catch block
/// in the arc — including ones not written yet.
/// </remarks>
public sealed class GvMediaUnavailableException : Exception
{
  /// <summary>Creates an exception carrying the reason a GV media fetch could not complete.</summary>
  /// <param name="reason">Why the fetch failed.</param>
  /// <param name="message">A masked message — it must never carry the raw media id.</param>
  /// <param name="innerException">The underlying failure, when there was one.</param>
  public GvMediaUnavailableException(GvMediaFailure reason, string message, Exception? innerException = null)
    : base(message, innerException)
  {
    Reason = reason;
  }

  /// <summary>Why the fetch failed.</summary>
  public GvMediaFailure Reason { get; }

  /// <summary>
  /// True when retrying the same request cannot succeed.
  /// </summary>
  /// <remarks>
  /// ⚠ NotFound was removed from this set, and the reason is a property of the upstream rather than
  /// of this class. RotaryPhone's <c>GvVoicemailController.GetAudio</c> resolves a recording through
  /// <c>FindNodeAsync</c>, which calls <c>GvVoicemailClient.ListVoicemailsAsync</c> and — unlike the
  /// sibling <c>GetList</c>, which guards it explicitly — does NOT check the result's Succeeded
  /// flag. A failed authenticated list returns <c>GvVoicemailListResult.Empty(succeeded: false)</c>,
  /// an EMPTY item list, so <c>FirstOrDefault</c> yields null and the route answers
  /// 404 "has no recording".
  ///
  /// That failure is exactly the Google Voice auth blackout — roughly 9 minutes in every 20 (XR-3) —
  /// so a 404 from this upstream means "gone" OR "try again in a few minutes", and nothing in the
  /// response distinguishes them. Reporting it as permanent would tell a user a voicemail no longer
  /// exists roughly 45% of the times it is transient, which is the GV-6 / GV-8 failure class the
  /// GvMediaFailure enum was built to prevent, arriving through a different door.
  ///
  /// The distinction is NOT collapsed: NotFound keeps its own name and reaches the snapshot as
  /// "MediaNotFound", distinct from "MediaUpstream". What it no longer carries is a claim about
  /// retrying that this side cannot support.
  ///
  /// Disabled is the only reason that is permanent by construction on OUR side — retrying with the
  /// feature off cannot succeed, and no clock changes that.
  /// </remarks>
  public bool IsPermanent => Reason is GvMediaFailure.Disabled;
}
