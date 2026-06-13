using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Services;

public class PhoneApiServiceTests
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  private PhoneApiService CreateService(HttpClient httpClient)
  {
    return new PhoneApiService(httpClient, NullLogger<PhoneApiService>.Instance);
  }

  [Fact]
  public async Task GetSystemStatusAsync_ReturnsStatus_WhenApiAvailable()
  {
    var expected = new PhoneSystemStatusDto
    {
      Platform = "Linux",
      BluetoothConnected = true,
      SipListening = true,
      Ht801Reachable = true
    };
    var handler = new MockHttpHandler(JsonSerializer.Serialize(expected, JsonOptions));
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.GetSystemStatusAsync();

    Assert.NotNull(result);
    Assert.Equal("Linux", result.Platform);
    Assert.True(result.BluetoothConnected);
  }

  [Fact]
  public async Task GetSystemStatusAsync_ReturnsNull_WhenApiUnavailable()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.InternalServerError);
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.GetSystemStatusAsync();

    Assert.Null(result);
  }

  [Fact]
  public async Task GetCallStateAsync_ReturnsState()
  {
    var expected = new PhoneCallStateDto { CallState = "Ringing", IncomingNumber = "+15551234567" };
    var handler = new MockHttpHandler(JsonSerializer.Serialize(expected, JsonOptions));
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.GetCallStateAsync();

    Assert.NotNull(result);
    Assert.Equal("Ringing", result.CallState);
    Assert.Equal("+15551234567", result.IncomingNumber);
  }

  [Fact]
  public async Task GetContactsAsync_ReturnsList()
  {
    var expected = new List<ContactDto>
    {
      new() { Id = "1", Name = "Alice", PhoneNumber = "555-1234" }
    };
    var handler = new MockHttpHandler(JsonSerializer.Serialize(expected, JsonOptions));
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.GetContactsAsync();

    Assert.NotNull(result);
    Assert.Single(result);
    Assert.Equal("Alice", result[0].Name);
  }

  [Fact]
  public async Task GetCallHistoryAsync_DeserializesLiveNumericShape()
  {
    // Captured live from RotaryPhone GET http://radio:5004/api/callhistory on
    // 2026-06-13. RotaryPhone serializes the CallDirection/CallAnsweredOn enums
    // as JSON numbers (no JsonStringEnumConverter registered). Regression guard:
    // a string-typed DTO threw JsonException at $[0].direction, dropping the
    // entire list so the Call History tab silently showed "No calls recorded".
    const string liveJson = """
      [
        {"id":"473e6334-2514-4d3e-9ab3-f9eca4c3b2de","phoneNumber":"9193718044","callerName":null,"direction":1,"answeredOn":0,"startTime":"2026-06-13T14:09:48.33-04:00","endTime":"2026-06-13T14:10:26.30-04:00","duration":"00:00:37.97","phoneId":"default"},
        {"id":"b9e4962b-9e99-400f-83c9-fa01bcf83d67","phoneNumber":"+19193718044","callerName":null,"direction":0,"answeredOn":1,"startTime":"2026-06-13T14:08:58.68-04:00","endTime":"2026-06-13T14:09:22.48-04:00","duration":"00:00:23.80","phoneId":"default"}
      ]
      """;
    var handler = new MockHttpHandler(liveJson);
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.GetCallHistoryAsync();

    // The list must NOT be dropped (null/empty would mean the deserialize threw).
    Assert.NotNull(result);
    Assert.Equal(2, result.Count);

    // First entry: outgoing, not answered.
    Assert.Equal(CallDirection.Outgoing, result[0].Direction);
    Assert.Equal(CallAnsweredOn.NotAnswered, result[0].AnsweredOn);
    Assert.Equal("9193718044", result[0].PhoneNumber);

    // Second entry: incoming, answered on the rotary phone.
    Assert.Equal(CallDirection.Incoming, result[1].Direction);
    Assert.Equal(CallAnsweredOn.RotaryPhone, result[1].AnsweredOn);
  }

  [Fact]
  public async Task IsAvailableAsync_ReturnsTrue_WhenApiReachable()
  {
    var handler = new MockHttpHandler(JsonSerializer.Serialize(
      new PhoneSystemStatusDto { Platform = "Linux" }, JsonOptions));
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.IsAvailableAsync();

    Assert.True(result);
  }

  [Fact]
  public async Task IsAvailableAsync_ReturnsFalse_WhenApiUnreachable()
  {
    var handler = new MockHttpHandler(statusCode: HttpStatusCode.InternalServerError);
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.IsAvailableAsync();

    Assert.False(result);
  }

  private class MockHttpHandler : HttpMessageHandler
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
}
