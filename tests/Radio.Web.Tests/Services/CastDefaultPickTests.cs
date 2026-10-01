using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;
using Xunit;

namespace Radio.Web.Tests.Services;

/// <summary>
/// AUD-85: the one-tap Cast pick with a saved default sends exactly one request — the connect —
/// and never clears the saved default, whatever the connect answers.
/// </summary>
public class CastDefaultPickTests
{
  private const string ConnectPath = "/api/devices/cast/connect";
  private const string DefaultPath = "/api/devices/cast/default";
  private const string OutputPath = "/api/devices/output";

  private static readonly CastDeviceDto Device =
    new("https://192.168.0.25/", "Test speaker", "192.168.0.25", 8009, "Nest Audio");

  private static DevicesApiService BuildApi(HttpMessageHandler handler) =>
    new(new HttpClient(handler) { BaseAddress = new Uri(HermeticTestRig.ApiBaseUrl) },
      NullLogger<DevicesApiService>.Instance);

  [Theory]
  [InlineData(HttpStatusCode.InternalServerError)]
  [InlineData(HttpStatusCode.Conflict)]
  public async Task ConnectFails_DoesNotClearDefault_AndDoesNotSwitchOutput(HttpStatusCode status)
  {
    var handler = new RoutedApiHandler().Post(ConnectPath, status);

    var outcome = await CastDefaultPick.ConnectAsync(BuildApi(handler), Device, NullLogger.Instance);

    Assert.Equal(CastDefaultPickOutcome.Failed, outcome);
    AssertNoClearAndNoOutputSwitch(handler.Requests.Select(r => (r.Method, r.Path)).ToList());
    Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.Path == ConnectPath);
  }

  [Fact]
  public async Task ConnectThrowsInTransport_DoesNotClearDefault_AndDoesNotSwitchOutput()
  {
    var handler = new ThrowingRecordingHandler();

    var outcome = await CastDefaultPick.ConnectAsync(BuildApi(handler), Device, NullLogger.Instance);

    Assert.Equal(CastDefaultPickOutcome.Failed, outcome);
    AssertNoClearAndNoOutputSwitch(handler.Requests);
    Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.Path == ConnectPath);
  }

  [Fact]
  public async Task ConnectSucceeds_SendsExactlyOneConnect_AndNoOutputSwitch()
  {
    var handler = new RoutedApiHandler().Post(ConnectPath, HttpStatusCode.OK);

    var outcome = await CastDefaultPick.ConnectAsync(BuildApi(handler), Device, NullLogger.Instance);

    Assert.Equal(CastDefaultPickOutcome.Connected, outcome);
    var requests = handler.Requests;
    var only = Assert.Single(requests);
    Assert.Equal(HttpMethod.Post, only.Method);
    Assert.Equal(ConnectPath, only.Path);
    Assert.Contains("\"deviceId\":\"https://192.168.0.25/\"", only.Body, StringComparison.OrdinalIgnoreCase);
  }

  private static void AssertNoClearAndNoOutputSwitch(IReadOnlyList<(HttpMethod Method, string Path)> requests)
  {
    Assert.DoesNotContain(requests, r => r.Method == HttpMethod.Delete && r.Path == DefaultPath);
    Assert.DoesNotContain(requests, r => r.Method == HttpMethod.Post && r.Path == OutputPath);
  }

  /// <summary>Records each request, then fails it as a transport error would.</summary>
  private sealed class ThrowingRecordingHandler : HttpMessageHandler
  {
    private readonly object _sync = new();
    private readonly List<(HttpMethod Method, string Path)> _requests = [];

    public IReadOnlyList<(HttpMethod Method, string Path)> Requests
    {
      get
      {
        lock (_sync)
        {
          return _requests.ToList();
        }
      }
    }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken)
    {
      lock (_sync)
      {
        _requests.Add((request.Method, request.RequestUri?.AbsolutePath ?? string.Empty));
      }
      throw new HttpRequestException("simulated transport failure");
    }
  }
}
