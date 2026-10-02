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

  /// <summary>Reads one phone's call state from RotaryPhone's <c>GET /api/phone/status</c>; null on failure.</summary>
  /// <param name="phoneId">The phone to ask about; null asks about RotaryPhone's default (first) phone.</param>
  /// <param name="ct">Cancels the request.</param>
  public async Task<PhoneCallStateDto?> GetCallStateAsync(string? phoneId = null, CancellationToken ct = default)
  {
    try
    {
      string url = string.IsNullOrEmpty(phoneId)
        ? "/api/phone/status"
        : $"/api/phone/status?phoneId={Uri.EscapeDataString(phoneId)}";
      return await _httpClient.GetFromJsonAsync<PhoneCallStateDto>(url, JsonOptions, ct);
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
  /// banner calls this only when <c>RotaryPhone:DeclineSupported</c> is true. A current RotaryPhone answers
  /// an unknown <c>/api/*</c> route with a JSON <c>404</c> (its <c>Program.cs</c> API fallback, added after
  /// <c>UI-11</c>), which fails the status check. The body check is what still holds against an older
  /// RotaryPhone build, whose SPA fallback answered such a route <c>200</c> with <c>index.html</c>: that
  /// body is not JSON, so it counts as a failure rather than a decline that never happened.
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
      if (!response.IsSuccessStatusCode)
      {
        _logger.LogWarning("The phone service did not accept the decline ({StatusCode})", (int)response.StatusCode);
        return false;
      }

      using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
      return body.RootElement.ValueKind == JsonValueKind.Object
        && body.RootElement.TryGetProperty("declined", out var declined)
        && declined.ValueKind == JsonValueKind.True;
    }
    catch (JsonException)
    {
      // A 2xx that is not JSON: an older RotaryPhone's SPA fallback answering a route it does not have.
      _logger.LogWarning("The phone service answered the decline with something other than JSON; is the route deployed?");
      return false;
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
