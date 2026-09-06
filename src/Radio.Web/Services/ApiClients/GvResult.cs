using System.Net;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// How a GV Bridge call ended. Introduced by GV-8 (UAT F-1): the read methods used to
/// map EVERY non-2xx, timeout and deserialization error onto a bare <c>null</c>, so a
/// caller could not distinguish "the load failed" from "the server returned nothing" —
/// and <c>PhonePage</c> rendered the failure as an empty conversation.
/// </summary>
public enum GvCallOutcome
{
  /// <summary>2xx and the body deserialized.</summary>
  Success,

  /// <summary>A response arrived carrying a non-2xx status (e.g. RotaryPhone's 502
  /// during a GV auth blackout, or a 409 dark-feature rejection).</summary>
  HttpError,

  /// <summary>The request was abandoned on the HttpClient timeout. NOT caller
  /// cancellation — a token the caller cancelled rethrows.</summary>
  Timeout,

  /// <summary>No usable response: DNS failure, connection refused, connection reset.</summary>
  Transport,

  /// <summary>2xx, but the body did not deserialize into the expected DTO — including a
  /// non-JSON body such as an SPA fallback's index.html served with HTTP 200.</summary>
  Malformed
}

/// <summary>
/// Outcome of a GV Bridge call: the value on success, and enough shape on failure for a
/// caller to decide what the user should see and for an operator to read the log.
/// <para>
/// REUSABLE BY DESIGN. GV-6 (distinguish <c>409 markread_disabled</c> from a genuine
/// mark-read failure) adopts this same type for the two mark-read methods rather than
/// inventing a second mechanism — branch on
/// <c>Outcome == GvCallOutcome.HttpError &amp;&amp; StatusCode == HttpStatusCode.Conflict
/// &amp;&amp; ErrorCode == "markread_disabled"</c>. See
/// <c>docs/queue/ORDERING-NOTES.md</c> for why the two rows share the idiom but not
/// the PR.
/// </para>
/// </summary>
public sealed class GvResult<T> where T : class
{
  private GvResult(GvCallOutcome outcome, T? value, HttpStatusCode? statusCode, string? errorCode)
  {
    Outcome = outcome;
    Value = value;
    StatusCode = statusCode;
    ErrorCode = errorCode;
  }

  /// <summary>How the call ended.</summary>
  public GvCallOutcome Outcome { get; }

  /// <summary>The deserialized payload. Non-null if and only if <see cref="IsSuccess"/>.</summary>
  public T? Value { get; }

  /// <summary>The HTTP status, when a response actually arrived. Null otherwise.</summary>
  public HttpStatusCode? StatusCode { get; }

  /// <summary>RotaryPhone's error discriminator from the failure body
  /// (<c>{"error":"..."}</c> / <c>{"code":"..."}</c>), when present.</summary>
  public string? ErrorCode { get; }

  public bool IsSuccess => Outcome == GvCallOutcome.Success;

  public bool IsFailure => Outcome != GvCallOutcome.Success;

  public static GvResult<T> Success(T value) =>
    new(GvCallOutcome.Success, value, null, null);

  public static GvResult<T> HttpError(HttpStatusCode statusCode, string? errorCode = null) =>
    new(GvCallOutcome.HttpError, null, statusCode, errorCode);

  public static GvResult<T> Timeout() =>
    new(GvCallOutcome.Timeout, null, null, null);

  public static GvResult<T> Transport() =>
    new(GvCallOutcome.Transport, null, null, null);

  public static GvResult<T> Malformed() =>
    new(GvCallOutcome.Malformed, null, null, null);
}
