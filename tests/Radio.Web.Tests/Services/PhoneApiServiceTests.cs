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
  public async Task GetCallHistoryAsync_ReturnsList()
  {
    var expected = new List<CallHistoryEntryDto>
    {
      new() { Direction = "Incoming", PhoneNumber = "555-9876", AnsweredOn = "RotaryPhone" }
    };
    var handler = new MockHttpHandler(JsonSerializer.Serialize(expected, JsonOptions));
    var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5004") };
    var service = CreateService(httpClient);

    var result = await service.GetCallHistoryAsync();

    Assert.NotNull(result);
    Assert.Single(result);
    Assert.Equal("RotaryPhone", result[0].AnsweredOn);
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
