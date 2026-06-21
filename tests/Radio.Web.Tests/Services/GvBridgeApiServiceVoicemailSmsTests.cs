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
    // No HTTP call should be made; returns false (not-persisted).
    var handler = new MockHttpHandler("{}");  // would 200 if called
    var client = new HttpClient(handler) { BaseAddress = new Uri("http://radio:5004") };
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
        { ["RotaryPhone:Gv:MarkReadEnabled"] = "false" })
      .Build();
    var svc = new GvBridgeApiService(client,
      NullLogger<GvBridgeApiService>.Instance, config);

    var result = await svc.MarkVoicemailReadAsync("vm1");

    Assert.False(result);
    Assert.Equal(0, handler.RequestCount);  // never hit the network
  }
}
