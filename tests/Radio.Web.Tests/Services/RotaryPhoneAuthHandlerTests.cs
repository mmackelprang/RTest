using System.Net;
using Microsoft.Extensions.Configuration;
using Radio.Web.Services.Http;

namespace Radio.Web.Tests.Services;

public class RotaryPhoneAuthHandlerTests
{
  private sealed class CapturingHandler : HttpMessageHandler
  {
    public HttpRequestMessage? Last;
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken ct)
    {
      Last = request;
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
  }

  private static HttpClient Build(string? key, CapturingHandler inner)
  {
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
        { ["RotaryPhone:Gv:AuthKey"] = key })
      .Build();
    var handler = new RotaryPhoneAuthHandler(config) { InnerHandler = inner };
    return new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
  }

  [Fact]
  public async Task NoHeader_WhenKeyEmpty()
  {
    var inner = new CapturingHandler();
    await Build("", inner).GetAsync("/api/gvbridge/status");
    Assert.False(inner.Last!.Headers.Contains("X-RotaryPhone-Auth"));
  }

  [Fact]
  public async Task NoHeader_WhenKeyMissing()
  {
    var inner = new CapturingHandler();
    await Build(null, inner).GetAsync("/api/gvbridge/status");
    Assert.False(inner.Last!.Headers.Contains("X-RotaryPhone-Auth"));
  }

  [Fact]
  public async Task AddsHeader_WhenKeySet()
  {
    var inner = new CapturingHandler();
    await Build("secret123", inner).GetAsync("/api/gvbridge/status");
    Assert.True(inner.Last!.Headers.TryGetValues("X-RotaryPhone-Auth", out var vals));
    Assert.Equal("secret123", vals!.Single());
  }
}
