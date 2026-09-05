using Microsoft.Extensions.Options;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.External;

/// <summary>
/// Adds X-RotaryPhone-Auth to outbound GV media requests when GvMedia:AuthKey is non-empty
/// (ADR-029 D8 §10.1). Empty today, which matches the current LAN-only posture.
///
/// <para>
/// This handler is the mechanism that closed carried risk #3, and PHN-2 finished the job. The
/// now-deleted GvBridgeApiService.GetVoicemailAudioUrl only ever BUILT a string that the browser then
/// fetched, so no DelegatingHandler could touch it — which is why browser-side voicemail playback
/// would have broken the moment RotaryPhone's gate flipped on. Radio.API fetches the audio itself
/// through this handler now, and since PHN-2 there is no browser fetch left to break: the constraint
/// is dissolved rather than pending. Past tense deliberately — the method this paragraph named no
/// longer exists, and a comment that outlives the code it described is worse than no comment.
/// </para>
///
/// <para>
/// A copy of Radio.Web's RotaryPhoneAuthHandler rather than a shared extraction: the two read
/// different configuration keys, and 31 shared lines are not worth coupling the two services'
/// configuration shapes. It reads IOptionsMonitor rather than raw IConfiguration, per request, so
/// the key stays flippable without a restart.
/// </para>
/// </summary>
public sealed class GvMediaAuthHandler : DelegatingHandler
{
  private const string HeaderName = "X-RotaryPhone-Auth";
  private readonly IOptionsMonitor<GvMediaOptions> _options;

  /// <summary>Creates the handler over the GvMedia options section.</summary>
  public GvMediaAuthHandler(IOptionsMonitor<GvMediaOptions> options)
  {
    _options = options;
  }

  /// <inheritdoc />
  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    var key = _options.CurrentValue.AuthKey;
    if (!string.IsNullOrEmpty(key) && !request.Headers.Contains(HeaderName))
    {
      request.Headers.Add(HeaderName, key);
    }
    return base.SendAsync(request, cancellationToken);
  }
}
