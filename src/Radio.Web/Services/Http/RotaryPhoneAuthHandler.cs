using Microsoft.Extensions.Configuration;

namespace Radio.Web.Services.Http;

/// <summary>
/// Injects the X-RotaryPhone-Auth header on outbound GV requests ONLY when
/// RotaryPhone:Gv:AuthKey is non-empty. Today the key is empty → no header is
/// sent (honors the current LAN-only no-auth posture). One place to flip on
/// when the inter-service auth gate ships (ADR-022 §8.1).
/// </summary>
public sealed class RotaryPhoneAuthHandler : DelegatingHandler
{
  private const string HeaderName = "X-RotaryPhone-Auth";
  private readonly IConfiguration _configuration;

  public RotaryPhoneAuthHandler(IConfiguration configuration)
  {
    _configuration = configuration;
  }

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    var key = _configuration.GetValue<string>("RotaryPhone:Gv:AuthKey");
    if (!string.IsNullOrEmpty(key) && !request.Headers.Contains(HeaderName))
    {
      request.Headers.Add(HeaderName, key);
    }
    return base.SendAsync(request, cancellationToken);
  }
}
