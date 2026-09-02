using System.Net;
using Radio.Core.Configuration;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Tests.External;

public class GvMediaAuthHandlerTests
{
  private const string HeaderName = "X-RotaryPhone-Auth";

  private sealed class CapturingHandler : HttpMessageHandler
  {
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      LastRequest = request;
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
  }

  private static async Task<HttpRequestMessage> SendThrough(string authKey)
  {
    var inner = new CapturingHandler();
    var handler = new GvMediaAuthHandler(
      new StaticOptionsMonitor<GvMediaOptions>(new GvMediaOptions { AuthKey = authKey }))
    {
      InnerHandler = inner
    };

    using var client = new HttpClient(handler);
    await client.GetAsync("http://radio:5004/api/gvbridge/voicemail/abc/audio");

    Assert.NotNull(inner.LastRequest);
    return inner.LastRequest!;
  }

  [Fact]
  public async Task NoHeader_WhenKeyIsEmpty()
  {
    // The shipping default. A header sent against a service that does not expect one is not
    // harmless: it is the kind of difference that makes a cross-repo bug report ambiguous.
    var request = await SendThrough("");

    Assert.False(request.Headers.Contains(HeaderName));
  }

  [Fact]
  public async Task AddsHeader_WhenKeyIsSet()
  {
    var request = await SendThrough("s3cret");

    Assert.True(request.Headers.Contains(HeaderName));
    Assert.Equal("s3cret", Assert.Single(request.Headers.GetValues(HeaderName)));
  }

  [Fact]
  public async Task DoesNotDuplicate_WhenTheHeaderIsAlreadyPresent()
  {
    var inner = new CapturingHandler();
    var handler = new GvMediaAuthHandler(
      new StaticOptionsMonitor<GvMediaOptions>(new GvMediaOptions { AuthKey = "s3cret" }))
    {
      InnerHandler = inner
    };

    using var client = new HttpClient(handler);
    using var message = new HttpRequestMessage(HttpMethod.Get, "http://radio:5004/x");
    message.Headers.Add(HeaderName, "already-there");
    await client.SendAsync(message);

    Assert.Equal("already-there", Assert.Single(inner.LastRequest!.Headers.GetValues(HeaderName)));
  }
}
