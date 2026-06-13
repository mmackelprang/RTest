using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Services.ApiClients;
using Xunit;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Tests for <see cref="DiagnosticsApiService"/> — pins deserialization against the
/// exact JSON shapes RotaryPhone's /api/diagnostics/* endpoints return (captured live
/// 2026-06-13) and the graceful-degradation behaviour when the API is unreachable.
/// </summary>
public class DiagnosticsApiServiceTests
{
  private sealed class StubHandler(string json, HttpStatusCode status) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
      => Task.FromResult(new HttpResponseMessage(status)
      {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
      });
  }

  private static DiagnosticsApiService Make(string json, HttpStatusCode status = HttpStatusCode.OK)
  {
    var http = new HttpClient(new StubHandler(json, status)) { BaseAddress = new Uri("http://radio:5004") };
    return new DiagnosticsApiService(http, NullLogger<DiagnosticsApiService>.Instance);
  }

  [Fact]
  public async Task GetAudioBridge_DeserializesLiveShape()
  {
    var svc = Make("""{"isActive":true,"inboundFramesSent":120,"outboundFramesReceived":118,"inboundErrors":0,"outboundErrors":2,"bidirectionalAudio":true}""");

    var dto = await svc.GetAudioBridgeAsync();

    dto.Should().NotBeNull();
    dto!.IsActive.Should().BeTrue();
    dto.InboundFramesSent.Should().Be(120);
    dto.OutboundFramesReceived.Should().Be(118);
    dto.OutboundErrors.Should().Be(2);
    dto.BidirectionalAudio.Should().BeTrue();
  }

  [Fact]
  public async Task GetSipLog_DeserializesDirectionAndStatus()
  {
    var svc = Make("""[{"timestamp":"2026-06-13T15:17:55.11Z","direction":1,"method":"INVITE","fromAddress":"udp:192.168.86.22:5060","toAddress":"udp:0.0.0.0:5060","statusCode":180,"statusText":"Ringing","diagnosticNote":null,"callId":"abc"}]""");

    var log = await svc.GetSipLogAsync(10);

    log.Should().HaveCount(1);
    log[0].Direction.Should().Be(1);
    log[0].Method.Should().Be("INVITE");
    log[0].StatusCode.Should().Be(180);
    log[0].StatusText.Should().Be("Ringing");
  }

  [Fact]
  public async Task GetTimeline_DeserializesEventAndMetadata()
  {
    var svc = Make("""[{"timestamp":"2026-06-13T15:17:55.10Z","eventType":"INVITE_SENT","description":"INVITE sent","metadata":{"callId":"abc"}}]""");

    var tl = await svc.GetTimelineAsync(10);

    tl.Should().HaveCount(1);
    tl[0].EventType.Should().Be("INVITE_SENT");
    tl[0].Metadata.Should().ContainKey("callId");
  }

  [Fact]
  public async Task GetAudioBridge_ReturnsNull_OnError()
  {
    var svc = Make("error", HttpStatusCode.InternalServerError);
    (await svc.GetAudioBridgeAsync()).Should().BeNull();
  }

  [Fact]
  public async Task GetSipLog_ReturnsEmpty_OnError()
  {
    var svc = Make("error", HttpStatusCode.InternalServerError);
    (await svc.GetSipLogAsync()).Should().BeEmpty();
  }
}
