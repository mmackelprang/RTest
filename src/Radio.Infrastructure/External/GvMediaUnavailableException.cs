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

  /// <summary>The provider returned 404 — the recording does not exist. Retrying will not help.</summary>
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
  /// True when retrying the same request cannot succeed. Consumed by PR 3 to choose between a
  /// retryable error and a terminal one; false for the whole blackout class, which is the case the
  /// cache exists to mitigate.
  /// </summary>
  public bool IsPermanent => Reason is GvMediaFailure.NotFound or GvMediaFailure.Disabled;
}
