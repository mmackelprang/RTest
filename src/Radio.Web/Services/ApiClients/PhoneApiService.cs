using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// HTTP client for RotaryPhone.API at http://localhost:5004.
/// Provides access to phone status, contacts, and call history.
/// </summary>
public class PhoneApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<PhoneApiService> _logger;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public PhoneApiService(HttpClient httpClient, ILogger<PhoneApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  // Health check

  public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
  {
    try
    {
      var status = await _httpClient.GetFromJsonAsync<PhoneSystemStatusDto>(
        "/api/phone/system-status", JsonOptions, ct);
      return status != null;
    }
    catch
    {
      return false;
    }
  }

  // Phone status

  public async Task<PhoneSystemStatusDto?> GetSystemStatusAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PhoneSystemStatusDto>(
        "/api/phone/system-status", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get phone system status");
      return null;
    }
  }

  public async Task<PhoneCallStateDto?> GetCallStateAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PhoneCallStateDto>(
        "/api/phone/status", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get phone call state");
      return null;
    }
  }

  /// <summary>
  /// PHN-11: asks RotaryPhone to decline the RINGING call — the banner's Ignore button. True only when the
  /// service answers 2xx with a JSON body carrying <c>"declined": true</c>.
  /// </summary>
  /// <remarks>
  /// ⚠ <b>The route does not exist in RotaryPhone yet.</b> It was requested on 2026-10-02
  /// (<c>RotaryPhone/docs/prompts/2026-10-02-radioconsole-decline-ringing-call-request.md</c>), and the
  /// banner calls this only when <c>RotaryPhone:DeclineSupported</c> is true. The body check is not
  /// decoration: RotaryPhone's front end answers unknown <c>/api/*</c> paths with <c>200</c> and its
  /// <c>index.html</c> (see <c>CLAUDE.md</c>, <c>UI-11</c>), so a status code alone would report a decline
  /// that never happened if the flag were flipped before the route shipped.
  /// <c>simulate/hook?offHook=false</c> would reach the same hang-up today and is deliberately not used:
  /// it is unconditional, so a tap landing just after the handset is lifted would cut off the answered call.
  /// </remarks>
  public async Task<bool> DeclineCallAsync(string? phoneId, CancellationToken ct = default)
  {
    try
    {
      string url = string.IsNullOrEmpty(phoneId)
        ? "/api/phone/decline"
        : $"/api/phone/decline?phoneId={Uri.EscapeDataString(phoneId)}";
      using var response = await _httpClient.PostAsync(url, null, ct);
      if (!response.IsSuccessStatusCode
          || response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
      {
        _logger.LogWarning("The phone service did not accept the decline ({StatusCode})", (int)response.StatusCode);
        return false;
      }

      using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
      return body.RootElement.ValueKind == JsonValueKind.Object
        && body.RootElement.TryGetProperty("declined", out var declined)
        && declined.ValueKind == JsonValueKind.True;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to decline the incoming call");
      return false;
    }
  }

  // Simulate controls (developer tools)

  public async Task<bool> SimulateHookAsync(bool offHook, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync(
        $"/api/phone/simulate/hook?offHook={offHook}", null, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to simulate hook state");
      return false;
    }
  }

  public async Task<bool> SimulateIncomingCallAsync(CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/phone/simulate/incoming", null, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to simulate incoming call");
      return false;
    }
  }

  public async Task<bool> SimulateDialAsync(string digits, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync(
        $"/api/phone/simulate/dial?digits={Uri.EscapeDataString(digits)}", null, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to simulate dial");
      return false;
    }
  }

  // Contacts

  public async Task<List<ContactDto>?> GetContactsAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<ContactDto>>(
        "/api/contacts", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get contacts");
      return null;
    }
  }

  public async Task<bool> CreateContactAsync(ContactFormDto contact, CancellationToken ct = default)
  {
    try
    {
      var content = new StringContent(
        JsonSerializer.Serialize(contact), Encoding.UTF8, "application/json");
      var response = await _httpClient.PostAsync("/api/contacts", content, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to create contact");
      return false;
    }
  }

  public async Task<bool> UpdateContactAsync(string id, ContactFormDto contact, CancellationToken ct = default)
  {
    try
    {
      var content = new StringContent(
        JsonSerializer.Serialize(contact), Encoding.UTF8, "application/json");
      var response = await _httpClient.PutAsync($"/api/contacts/{id}", content, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to update contact {Id}", id);
      return false;
    }
  }

  public async Task<bool> DeleteContactAsync(string id, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync($"/api/contacts/{id}", ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to delete contact {Id}", id);
      return false;
    }
  }

  // Call History

  public async Task<List<CallHistoryEntryDto>?> GetCallHistoryAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<CallHistoryEntryDto>>(
        "/api/callhistory", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get call history");
      return null;
    }
  }

  public async Task<bool> ClearCallHistoryAsync(CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync("/api/callhistory", ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to clear call history");
      return false;
    }
  }
}
