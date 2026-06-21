using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Services;

public class GvBridgeApiServiceVoicemailSmsTests
{
  private static readonly JsonSerializerOptions JsonOptions =
    new() { PropertyNameCaseInsensitive = true };

  private static GvBridgeApiService CreateService(HttpClient client) =>
    new(client, NullLogger<GvBridgeApiService>.Instance,
      new ConfigurationBuilder().Build());

  // GV-4: mark-read routes are gated on RotaryPhone:Gv:MarkReadEnabled; this builds
  // a service with that flag set so the flag-on/flag-off paths are both exercised.
  private static GvBridgeApiService BuildSvc(MockHttpHandler handler, bool markReadEnabled)
  {
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
        { ["RotaryPhone:Gv:MarkReadEnabled"] = markReadEnabled.ToString() })
      .Build();
    return new GvBridgeApiService(client, NullLogger<GvBridgeApiService>.Instance, config);
  }

  [Fact]
  public async Task GetVoicemailsAsync_ReturnsList()
  {
    var dto = new VoicemailListDto(
      new[]
      {
        new VoicemailItemDto("vm1", "t1", "+15551234567", "Jane",
          DateTime.UtcNow, 42, false, "hi", "/api/gvbridge/voicemail/vm1/audio")
      },
      null, DateTime.UtcNow);
    var handler = new MockHttpHandler(JsonSerializer.Serialize(dto, JsonOptions));
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetVoicemailsAsync();

    Assert.NotNull(result);
    Assert.Single(result!.Items);
    Assert.Equal("vm1", result.Items[0].Id);
  }

  [Fact]
  public async Task GetVoicemailsAsync_ReturnsNull_OnError()
  {
    var handler = new MockHttpHandler(statusCode: System.Net.HttpStatusCode.InternalServerError);
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetVoicemailsAsync();

    Assert.Null(result);
  }

  [Fact]
  public async Task GetSmsThreadsAsync_ReturnsThreads()
  {
    var dto = new SmsThreadListDto(
      new[] { new SmsThreadDto("t1", "+15551234567", "Mom",
        DateTime.UtcNow, true, "Did you eat?") },
      DateTime.UtcNow);
    var handler = new MockHttpHandler(JsonSerializer.Serialize(dto, JsonOptions));
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadsAsync();

    Assert.NotNull(result);
    Assert.Single(result!.Threads);
    Assert.True(result.Threads[0].HasUnread);
  }

  [Fact]
  public async Task GetSmsThreadMessagesAsync_ReturnsMessages()
  {
    var dto = new SmsThreadMessagesDto("t1",
      new[] { new SmsMessageDto("m1", "t1", "Inbound", "+15551234567",
        "hello", DateTime.UtcNow, false) },
      DateTime.UtcNow);
    var handler = new MockHttpHandler(JsonSerializer.Serialize(dto, JsonOptions));
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };

    var result = await CreateService(client).GetSmsThreadMessagesAsync("t1");

    Assert.NotNull(result);
    Assert.Single(result!.Messages);
  }

  [Fact]
  public void GetVoicemailAudioUrl_BuildsAbsoluteUrl_AgainstBaseAddress()
  {
    var client = new HttpClient(new MockHttpHandler("{}"))
    { BaseAddress = new Uri("http://radio:5004") };

    var url = CreateService(client).GetVoicemailAudioUrl("vm1");

    Assert.Equal("http://radio:5004/api/gvbridge/voicemail/vm1/audio", url);
  }

  [Fact]
  public async Task MarkVoicemailReadAsync_NoOps_WhenFlagOff()
  {
    var handler = new MockHttpHandler("{}");
    var svc = BuildSvc(handler, markReadEnabled: false);

    var result = await svc.MarkVoicemailReadAsync("vm1");

    Assert.Null(result);                  // no DTO when flag off
    Assert.Equal(0, handler.RequestCount); // never hit the network
  }

  [Fact]
  public async Task MarkVoicemailReadAsync_ReturnsDto_On200_WhenFlagOn()
  {
    // Frozen VoicemailItemDto read shape (ADR-024 §3.1).
    const string body = """
      { "id":"vm1","threadId":"t1","fromNumber":"+15551234567","fromName":"Jane",
        "receivedAt":"2026-06-20T18:03:11Z","durationSeconds":42,"isRead":true,
        "transcript":"hi","audioUrl":"/api/gvbridge/voicemail/vm1/audio" }
      """;
    var handler = new MockHttpHandler(body);   // 200 OK
    var svc = BuildSvc(handler, markReadEnabled: true);

    var dto = await svc.MarkVoicemailReadAsync("vm1");

    Assert.NotNull(dto);
    Assert.True(dto!.IsRead);
    Assert.Equal("vm1", dto.Id);
    Assert.Equal(1, handler.RequestCount);
  }

  [Fact]
  public async Task MarkVoicemailReadAsync_ReturnsNull_On404_WhenFlagOn()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.NotFound);
    var svc = BuildSvc(handler, markReadEnabled: true);

    Assert.Null(await svc.MarkVoicemailReadAsync("gone"));
  }

  [Fact]
  public async Task MarkVoicemailReadAsync_ReturnsNull_On502_NoRetry_WhenFlagOn()
  {
    // 502 = GV unreachable. Caller keeps the optimistic flip; client never auto-retries.
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.BadGateway);
    var svc = BuildSvc(handler, markReadEnabled: true);

    Assert.Null(await svc.MarkVoicemailReadAsync("vm1"));
    Assert.Equal(1, handler.RequestCount);  // exactly one attempt, no retry
  }

  [Fact]
  public async Task MarkSmsThreadReadAsync_ReturnsDto_On200_WhenFlagOn()
  {
    // Frozen SmsThreadDto read shape (ADR-024 §3.2).
    const string body = """
      { "threadId":"t1","counterpartyNumber":"+15551234567","counterpartyName":"Mom",
        "lastMessageAt":"2026-06-20T18:03:11Z","hasUnread":false,"lastMessagePreview":"ok" }
      """;
    var handler = new MockHttpHandler(body);
    var svc = BuildSvc(handler, markReadEnabled: true);

    var dto = await svc.MarkSmsThreadReadAsync("t1");

    Assert.NotNull(dto);
    Assert.False(dto!.HasUnread);
    Assert.Equal("t1", dto.ThreadId);
    Assert.Equal(1, handler.RequestCount);
  }

  [Fact]
  public async Task MarkSmsThreadReadAsync_NoOps_WhenFlagOff()
  {
    var handler = new MockHttpHandler("{}");
    var svc = BuildSvc(handler, markReadEnabled: false);

    Assert.Null(await svc.MarkSmsThreadReadAsync("t1"));
    Assert.Equal(0, handler.RequestCount);
  }
}
