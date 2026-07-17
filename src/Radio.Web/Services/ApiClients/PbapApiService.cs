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

  /// <summary>
  /// Resolve a single phone number to a contact display name via the connected
  /// device's synced phone book. Returns the name on a match, or null on a 404
  /// (no match) / unreachable API. Never throws — the Messages feed treats a null
  /// as "no name yet" and falls back to the formatted number. Callers should cache
  /// the result (see ContactResolutionService) so the feed issues at most one
  /// request per unique number.
  /// </summary>
  public async Task<string?> LookupNumberAsync(string phoneNumber, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(phoneNumber))
    {
      return null;
    }
    try
    {
      var response = await _httpClient.GetAsync(
        $"/api/bluetooth/pbap/lookup?phoneNumber={Uri.EscapeDataString(phoneNumber)}", ct);
      if (!response.IsSuccessStatusCode)
      {
        return null;   // 404 = no contact; any other non-success = treat as unresolved
      }
      var dto = await response.Content.ReadFromJsonAsync<PbapLookupDto>(JsonOptions, ct);
      return string.IsNullOrWhiteSpace(dto?.DisplayName) ? null : dto.DisplayName;
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "PBAP number lookup failed for {Number}", phoneNumber);
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
