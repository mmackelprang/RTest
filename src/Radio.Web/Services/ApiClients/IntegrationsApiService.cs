using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for integration status endpoints (encoders, phone, notifications).
/// </summary>
public class IntegrationsApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<IntegrationsApiService> _logger;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public IntegrationsApiService(HttpClient httpClient, ILogger<IntegrationsApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<EncoderStatusDto?> GetEncoderStatusAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<EncoderStatusDto>(
        "/api/integrations/encoder/status", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get encoder status");
      return null;
    }
  }

  public async Task<PhoneIntegrationStatusDto?> GetPhoneStatusAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PhoneIntegrationStatusDto>(
        "/api/integrations/phone/status", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get phone integration status");
      return null;
    }
  }

  public async Task<bool> SendTestNotificationAsync(string message, int priority, CancellationToken cancellationToken = default)
  {
    try
    {
      var payload = new { Message = message, Priority = priority };
      var content = new StringContent(
        JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
      var response = await _httpClient.PostAsync("/api/notifications/announce", content, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to send test notification");
      return false;
    }
  }
}
