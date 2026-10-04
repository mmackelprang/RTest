using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>How RotaryPhone answered a decline (PHN-13). See <see cref="PhoneApiService.DeclineCallAsync"/>.</summary>
public enum DeclineCallResult
{
  /// <summary><c>200 {"declined": true}</c>: the ringing call is being torn down.</summary>
  Declined,

  /// <summary><c>409</c>: the call was not ringing any more (answered, or the caller gave up). Benign.</summary>
  NotRinging,

  /// <summary>Transport failure, 5xx, 404, or a 2xx without <c>"declined": true</c>. The only error case.</summary>
  Failed,
}

/// <summary>The result of a decline, with the phone's state when RotaryPhone reported one on a <c>409</c>.</summary>
/// <param name="Result">What happened.</param>
/// <param name="State">The <c>409</c> body's <c>"state"</c> (<c>InCall</c>, <c>Idle</c>, <c>Dialing</c>); null otherwise.</param>
public readonly record struct DeclineCallOutcome(DeclineCallResult Result, string? State = null)
{
  public static DeclineCallOutcome Declined => new(DeclineCallResult.Declined);
  public static DeclineCallOutcome Failed => new(DeclineCallResult.Failed);

  /// <summary>True for a <c>409</c> reporting <c>InCall</c>: the handset was lifted before the decline.</summary>
  public bool WasAnswered => Result == DeclineCallResult.NotRinging
    && string.Equals(State, "InCall", StringComparison.OrdinalIgnoreCase);
}

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
  /// PHN-11/PHN-13: asks RotaryPhone to decline the RINGING call — Ignore, on the banner and on the Phone
  /// page's hero. RotaryPhone's <c>POST /api/phone/decline?phoneId=</c> (its <c>PhoneController.Decline</c>,
  /// default phone id <c>"default"</c>) answers:
  /// <list type="bullet">
  ///   <item><c>200 {"declined": true}</c> — declined; the <c>Idle</c> broadcast follows synchronously.
  ///   → <see cref="DeclineCallResult.Declined"/>.</item>
  ///   <item><c>409 {"declined": false, "state": "Idle|Dialing|InCall"}</c> — the call was no longer ringing:
  ///   <c>InCall</c>, the handset was lifted first (the call continues — the route never hangs up an answered
  ///   call); <c>Idle</c>, the caller gave up or the ring timed out. Benign, not a failure.
  ///   → <see cref="DeclineCallResult.NotRinging"/>, with the state.</item>
  ///   <item><c>404</c> — an unknown phone id. → <see cref="DeclineCallResult.Failed"/>.</item>
  /// </list>
  /// Anything else — a transport failure, a 5xx, a 2xx without <c>"declined": true</c> — is
  /// <see cref="DeclineCallResult.Failed"/>, and only that shows "Couldn't end the call".
  /// </summary>
  /// <remarks>
  /// <para>
  /// The body check on a 2xx still holds against an older RotaryPhone build, whose SPA fallback answered an
  /// unknown route <c>200</c> with <c>index.html</c>: not JSON, so a failure rather than a decline that never
  /// happened.
  /// </para>
  /// <para>
  /// <b>Every <c>409</c> is benign, whatever its body says.</b> RotaryPhone returns <c>409</c> from this route
  /// only when the phone is not ringing, so the call on screen is over or answered either way; the state is
  /// read for the banner's exit beat and for the debug line, and a <c>409</c> whose body cannot be read is still
  /// <see cref="DeclineCallResult.NotRinging"/> (state <c>null</c>).
  /// </para>
  /// <para>
  /// <c>simulate/hook?offHook=false</c> would reach the same hang-up and is deliberately not used: it is
  /// unconditional, so a tap landing just after the handset is lifted would cut off the answered call.
  /// </para>
  /// </remarks>
  public async Task<DeclineCallOutcome> DeclineCallAsync(string? phoneId, CancellationToken ct = default)
  {
    try
    {
      string url = string.IsNullOrEmpty(phoneId)
        ? "/api/phone/decline"
        : $"/api/phone/decline?phoneId={Uri.EscapeDataString(phoneId)}";
      using var response = await _httpClient.PostAsync(url, null, ct);

      if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
      {
        string? state = await TryReadStateAsync(response, ct);
        _logger.LogDebug("The call was no longer ringing when it was declined ({State})", state ?? "unknown");
        return new DeclineCallOutcome(DeclineCallResult.NotRinging, state);
      }

      if (!response.IsSuccessStatusCode)
      {
        _logger.LogWarning("The phone service did not accept the decline ({StatusCode})", (int)response.StatusCode);
        return DeclineCallOutcome.Failed;
      }

      using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
      bool declined = body.RootElement.ValueKind == JsonValueKind.Object
        && body.RootElement.TryGetProperty("declined", out var declinedProperty)
        && declinedProperty.ValueKind == JsonValueKind.True;
      if (!declined)
      {
        _logger.LogWarning("The phone service answered the decline without \"declined\": true");
        return DeclineCallOutcome.Failed;
      }
      return DeclineCallOutcome.Declined;
    }
    catch (JsonException)
    {
      // A 2xx that is not JSON: an older RotaryPhone's SPA fallback answering a route it does not have.
      _logger.LogWarning("The phone service answered the decline with something other than JSON; is the route deployed?");
      return DeclineCallOutcome.Failed;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to decline the incoming call");
      return DeclineCallOutcome.Failed;
    }
  }

  // The 409 body's "state"; null when the body is not the documented JSON.
  private static async Task<string?> TryReadStateAsync(HttpResponseMessage response, CancellationToken ct)
  {
    try
    {
      using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
      return body.RootElement.ValueKind == JsonValueKind.Object
        && body.RootElement.TryGetProperty("state", out var state)
        && state.ValueKind == JsonValueKind.String
          ? state.GetString()
          : null;
    }
    catch (JsonException)
    {
      return null;
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
