using System.Net;

namespace Radio.Web.Tests.TestHelpers;

/// <summary>
/// Shared test double for <see cref="HttpMessageHandler"/> that returns a fixed
/// response body + status code. Lifted from the (formerly private nested) helper
/// in PhoneApiServiceTests so the GV voicemail/SMS service tests reuse one copy.
/// </summary>
public sealed class MockHttpHandler : HttpMessageHandler
{
  private readonly string? _responseContent;
  private readonly HttpStatusCode _statusCode;

  public MockHttpHandler(string? responseContent = null, HttpStatusCode statusCode = HttpStatusCode.OK)
  {
    _responseContent = responseContent;
    _statusCode = statusCode;
  }

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken)
  {
    var response = new HttpResponseMessage(_statusCode);
    if (_responseContent != null)
    {
      response.Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json");
    }
    return Task.FromResult(response);
  }
}
