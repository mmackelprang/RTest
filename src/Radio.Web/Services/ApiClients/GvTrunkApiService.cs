using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Radio.Core.Utilities;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// HTTP client for RotaryPhone.API GV Trunk endpoints.
/// Provides access to SIP trunk status, call log, SMS notifications, and dialing.
/// </summary>
public class GvTrunkApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<GvTrunkApiService> _logger;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public GvTrunkApiService(HttpClient httpClient, ILogger<GvTrunkApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
  {
    try
    {
      var status = await _httpClient.GetFromJsonAsync<GvTrunkStatusDto>(
        "/api/gvtrunk/status", JsonOptions, ct);
      return status != null;
    }
    catch
    {
      return false;
    }
  }

  public async Task<GvTrunkStatusDto?> GetStatusAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<GvTrunkStatusDto>(
        "/api/gvtrunk/status", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get GV Trunk status");
      return null;
    }
  }

  public async Task<List<GvTrunkCallLogEntryDto>?> GetCallHistoryAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<GvTrunkCallLogEntryDto>>(
        "/api/gvtrunk/calls", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get GV Trunk call history");
      return null;
    }
  }

  public async Task<List<GvSmsNotificationDto>?> GetRecentSmsAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<GvSmsNotificationDto>>(
        "/api/gvtrunk/sms", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get GV Trunk SMS notifications");
      return null;
    }
  }

  public async Task<bool> DialAsync(string number, CancellationToken ct = default)
  {
    try
    {
      var content = new StringContent(
        JsonSerializer.Serialize(new { number }), Encoding.UTF8, "application/json");
      var response = await _httpClient.PostAsync("/api/gvtrunk/dial", content, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to dial {Number} via GV Trunk", LogSafeText.ForPhone(number));
      return false;
    }
  }

  public async Task<bool> ReregisterAsync(CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/gvtrunk/reregister", null, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to reregister GV Trunk");
      return false;
    }
  }
}
