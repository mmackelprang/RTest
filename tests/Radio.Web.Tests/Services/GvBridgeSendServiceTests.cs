using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

public class GvBridgeSendServiceTests
{
  private static GvBridgeSendService Build(bool sendEnabled, HttpClient client,
    GvBridgeStatusService status)
  {
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
        { ["RotaryPhone:Gv:SendEnabled"] = sendEnabled.ToString() })
      .Build();
    return new GvBridgeSendService(client,
      NullLogger<GvBridgeSendService>.Instance, config, status);
  }

  private static GvBridgeStatusService AvailableStatus()
  {
    var s = new GvBridgeStatusService(null!, NullLogger<GvBridgeStatusService>.Instance, 10);
    s.ApplyStatusForTest(new Radio.Web.Models.GvBridgeStatusDto { Available = true });
    return s;
  }

  [Fact]
  public async Task Throws_WhenFlagOff()
  {
    var client = new HttpClient(new MockHttpHandler("{}")) { BaseAddress = new Uri("http://radio:5004") };
    var svc = Build(sendEnabled: false, client, AvailableStatus());

    await Assert.ThrowsAsync<SendNotAvailableException>(
      () => svc.SendAsync("t1", "hi"));
  }

  [Fact]
  public async Task Throws_WhenDegraded_EvenIfFlagOn()
  {
    var client = new HttpClient(new MockHttpHandler("{}")) { BaseAddress = new Uri("http://radio:5004") };
    var status = new GvBridgeStatusService(null!, NullLogger<GvBridgeStatusService>.Instance, 10);
    status.ApplyStatusForTest(null);  // degraded
    var svc = Build(sendEnabled: true, client, status);

    await Assert.ThrowsAsync<SendUnavailableException>(
      () => svc.SendAsync("t1", "hi"));
  }

  [Fact]
  public async Task RateLimited_MapsTo429Result_WhenFlagOn()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.TooManyRequests);
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
    var svc = Build(sendEnabled: true, client, AvailableStatus());

    await Assert.ThrowsAsync<SendRateLimitedException>(
      () => svc.SendAsync("t1", "hi"));
  }

  [Fact]
  public async Task InFlightGuard_RejectsSecondConcurrentSendOnSameThread()
  {
    // MockHttpHandler cannot model an infinite delay, so use a local
    // TaskCompletionSource-backed handler that never completes until released.
    // The contract under test: a second SendAsync on the same thread while the
    // first is still outstanding throws SendInFlightException.
    var gate = new TaskCompletionSource<HttpResponseMessage>();
    var handler = new BlockingHandler(gate.Task);
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
    var svc = Build(sendEnabled: true, client, AvailableStatus());

    var first = svc.SendAsync("t1", "one");          // takes the slot, blocks on the gate
    await Assert.ThrowsAsync<SendInFlightException>(
      () => svc.SendAsync("t1", "two"));             // rejected while first outstanding

    // Release the first send so the in-flight key is cleaned up in the finally.
    gate.SetResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    await Assert.ThrowsAsync<HttpRequestException>(() => first);
  }

  /// <summary>
  /// Handler that blocks SendAsync on a provided task so a send can be held
  /// "in flight" while a second concurrent send is attempted.
  /// </summary>
  private sealed class BlockingHandler : HttpMessageHandler
  {
    private readonly Task<HttpResponseMessage> _gate;
    public BlockingHandler(Task<HttpResponseMessage> gate) => _gate = gate;

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken) => _gate;
  }
}
