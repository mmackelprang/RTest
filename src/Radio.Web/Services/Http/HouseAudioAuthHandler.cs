namespace Radio.Web.Services.Http;

/// <summary>
/// Adds the RNET controller's password to each request as the <c>X-RNet-Password</c> header. The live
/// controller also accepts <c>?pass=</c>; this client does not use it, so the password is never in a URL.
/// </summary>
public sealed class HouseAudioAuthHandler : DelegatingHandler
{
  private const string HeaderName = "X-RNet-Password";

  private readonly HouseAudioPasswordStore _passwords;

  public HouseAudioAuthHandler(HouseAudioPasswordStore passwords)
  {
    _passwords = passwords;
  }

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    string? password = _passwords.Get();
    if (!string.IsNullOrEmpty(password))
    {
      request.Headers.TryAddWithoutValidation(HeaderName, password);
    }

    return base.SendAsync(request, cancellationToken);
  }
}
