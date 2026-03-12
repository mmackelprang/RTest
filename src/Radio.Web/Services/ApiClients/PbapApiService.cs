using System.Net.Http.Json;
using System.Text.Json;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for PBAP (Phone Book Access Profile) endpoints
/// </summary>
public class PbapApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<PbapApiService> _logger;

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public PbapApiService(HttpClient httpClient, ILogger<PbapApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<PbapSyncResultDto?> SyncContactsAsync(string deviceAddress, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync(
        $"/api/bluetooth/pbap/sync?deviceAddress={Uri.EscapeDataString(deviceAddress)}", null, ct);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<PbapSyncResultDto>(JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to sync PBAP contacts for {Address}", deviceAddress);
      return null;
    }
  }

  public async Task<List<PbapContactDto>?> GetContactsAsync(string deviceAddress, CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<PbapContactDto>>(
        $"/api/bluetooth/pbap/contacts?deviceAddress={Uri.EscapeDataString(deviceAddress)}", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get PBAP contacts for {Address}", deviceAddress);
      return null;
    }
  }

  public async Task<PbapSyncStatusDto?> GetSyncStatusAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PbapSyncStatusDto>(
        "/api/bluetooth/pbap/status", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get PBAP sync status");
      return null;
    }
  }
}
