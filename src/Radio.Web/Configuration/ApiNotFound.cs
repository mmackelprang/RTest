namespace Radio.Web.Configuration;

using Microsoft.AspNetCore.Http;

/// <summary>
/// The terminal 404 for any request under <c>/api/</c> that no real endpoint claimed.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>This did not fix a wrong status code.</b> Measured on the appliance 2026-09-08 and again in
/// the test host 2026-09-09, an unmatched <c>/api/*</c> path already returned <c>404</c> — with
/// <c>Content-Length: 0</c> and no <c>Content-Type</c> at all. This app is a Blazor Web App:
/// <c>MapRazorComponents</c> registers one endpoint per <c>@page</c> template and no catch-all, so
/// there is no SPA fallback to swallow anything. The <c>200</c>-with-<c>index.html</c> symptom that
/// prompted UI-11 came from the other service on this box. What this type adds is the <em>body</em>:
/// a JSON caller that mistypes a path now learns which service it reached instead of receiving zero
/// bytes.
/// </para>
/// <para>
/// <b>The body is a compile-time constant, and that is a security decision rather than a
/// simplification.</b> The paths callers mistype here are <c>/api/gvbridge/sms/threads/&lt;id&gt;</c> and
/// <c>/api/phone/…</c>, where a thread id is a phone number. Radio.Web's Console sink carries no
/// <c>restrictedToMinimumLevel</c> and <c>radio-web.service</c> sets no <c>SyslogLevelPrefix</c>, so
/// anything this code logged or echoed would be a journald line on a box where log volume correlates
/// with audible audio distortion — the whole of PHN-5. Hence: no log statement at any level, no
/// <c>instance</c> member, and <c>Results.Text</c> over <c>Results.Problem</c> or <c>Results.Json</c>,
/// so the exact bytes on the wire are readable in this file and no serializer,
/// <c>ProblemDetailsFactory</c> or framework extension can add a request-derived field to them later.
/// </para>
/// <para>
/// Registered with <c>MapFallback</c>, which stamps <c>Order = int.MaxValue</c>. Endpoint selection is
/// by route precedence then order — never by <c>Map*</c> call sequence — so both real Web-side API
/// routes (<c>/api/health/version</c>, <c>/api/albumart/{filename}</c>) win on both counts, and the
/// twelve <c>@page</c> routes cannot match an <c>/api/</c>-scoped pattern at all.
/// </para>
/// </remarks>
internal static class ApiNotFound
{
  /// <summary>RFC 9457 media type. Asserted on the wire by <c>ApiNotFoundPipelineTests</c>.</summary>
  internal const string ContentType = "application/problem+json";

  /// <summary>
  /// The entire response body, verbatim. Names both services and both real Web-side routes, because
  /// the request this exists to answer is "which of the two services did I just hit?" — the question
  /// RotaryPhone burned a probe on. It names nothing from the request itself.
  /// </summary>
  /// <remarks>
  /// ⚠ <b>This string hardcodes two ports and the Web-side route list, and nothing pins them.</b>
  /// Accurate as written against <c>Program.cs</c>'s <c>/api/health/version</c> and
  /// <c>/api/albumart/{filename}</c> and the 5000/5002 split. <b>Add a third <c>/api/*</c> route to
  /// Radio.Web and this body silently becomes wrong</b> — a well-formed document asserting something
  /// untrue, which is the exact defect class UI-11 is about. If you map another <c>/api/</c> route on
  /// this service, update this constant in the same commit.
  /// </remarks>
  internal const string Body =
    """{"type":"about:blank","title":"Not Found","status":404,"detail":"No API route on this service matches the request path. Radio.Web (port 5002) serves only /api/health/version and /api/albumart/{filename}. The audio, radio, bluetooth, queue and configuration API is Radio.API on port 5000."}""";

  /// <summary>Handler for the terminal <c>/api/{**rest}</c> route. Takes no request input, by design.</summary>
  internal static IResult Handler() =>
    Results.Text(Body, ContentType, contentEncoding: null, statusCode: StatusCodes.Status404NotFound);
}
