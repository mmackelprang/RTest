using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Radio.Core.Utilities;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// Outcome of a single contact-number lookup. Distinguishes a definitive
/// "no such contact" (safe to cache) from a transient backend failure (must NOT
/// be cached, so the next poll retries) — a long-lived kiosk circuit must recover
/// its contact names after a backend hiccup rather than poisoning them for the
/// session.
/// </summary>
public enum ContactLookupOutcome
{
  Found,
  NotFound,
  Unavailable
}

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
  /// device's synced phone book. Returns Found+name on a match, NotFound on a 404
  /// (no such contact — a definitive answer the caller may cache), or Unavailable
  /// on any transient failure (5xx, timeout, connection error — the caller must
  /// NOT cache these so the number is retried later). Never throws.
  /// </summary>
  public async Task<(ContactLookupOutcome Outcome, string? Name)> LookupNumberAsync(
    string phoneNumber, CancellationToken ct = default)
  {
    var (outcome, name, _) = await LookupNumberWithTierAsync(phoneNumber, ct);
    return (outcome, name);
  }

  /// <summary>
  /// <see cref="LookupNumberAsync"/>, also reporting whether the match was exact (false: a stored 7-digit local
  /// entry). The incoming-call banner uses it to rank across sources (owner ruling 2026-10-03, PHN-14).
  /// </summary>
  public async Task<(ContactLookupOutcome Outcome, string? Name, bool IsExact)> LookupNumberWithTierAsync(
    string phoneNumber, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(phoneNumber))
    {
      return (ContactLookupOutcome.NotFound, null, false);   // nothing to look up
    }
    try
    {
      var response = await _httpClient.GetAsync(
        $"/api/bluetooth/pbap/lookup?phoneNumber={Uri.EscapeDataString(phoneNumber)}", ct);
      if (response.StatusCode == HttpStatusCode.NotFound)
      {
        return (ContactLookupOutcome.NotFound, null, false);   // definitive: no such contact
      }
      if (!response.IsSuccessStatusCode)
      {
        return (ContactLookupOutcome.Unavailable, null, false);   // transient: retry later
      }
      var dto = await response.Content.ReadFromJsonAsync<PbapLookupDto>(JsonOptions, ct);
      return string.IsNullOrWhiteSpace(dto?.DisplayName)
        ? (ContactLookupOutcome.NotFound, null, false)
        : (ContactLookupOutcome.Found, dto.DisplayName, dto.IsExactMatch ?? true);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "PBAP number lookup failed for {Number}", LogSafeText.ForPhone(phoneNumber));
      return (ContactLookupOutcome.Unavailable, null, false);   // transient: retry later
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
