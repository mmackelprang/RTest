using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// HTTP client for RotaryPhone.API GV Bridge endpoints.
/// Provides GV availability status and call-adapter mode switching. SMS lives on
/// the GV Trunk side (<see cref="GvTrunkApiService"/>) — there are no SMS routes
/// under /api/gvbridge/* and there never have been.
/// </summary>
public class GvBridgeApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<GvBridgeApiService> _logger;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public GvBridgeApiService(HttpClient httpClient, ILogger<GvBridgeApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
  {
    try
    {
      var status = await _httpClient.GetFromJsonAsync<GvBridgeStatusDto>(
        "/api/gvbridge/status", JsonOptions, ct);
      return status != null;
    }
    catch
    {
      return false;
    }
  }

  public async Task<GvBridgeStatusDto?> GetStatusAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<GvBridgeStatusDto>(
        "/api/gvbridge/status", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get GV Bridge status");
      return null;
    }
  }

  public async Task<GvAdapterModeDto?> GetAdapterModeAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<GvAdapterModeDto>(
        "/api/gvbridge/adapter/mode", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get adapter mode");
      return null;
    }
  }

  public async Task<bool> SetAdapterModeAsync(string mode, CancellationToken ct = default)
  {
    try
    {
      var content = new StringContent(
        JsonSerializer.Serialize(new { mode }), Encoding.UTF8, "application/json");
      var response = await _httpClient.PutAsync("/api/gvbridge/adapter/mode", content, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set adapter mode to {Mode}", mode);
      return false;
    }
  }

}
