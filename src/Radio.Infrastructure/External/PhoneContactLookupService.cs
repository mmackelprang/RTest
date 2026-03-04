using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.External;

/// <summary>
/// REST client for looking up contact names from the RotaryPhone contacts API.
/// Falls back to the raw phone number if the API is unavailable or no match is found.
/// Note: The RotaryPhone contacts API schema is assumed — expect mismatches during integration.
/// </summary>
public class PhoneContactLookupService
{
  private readonly ILogger<PhoneContactLookupService> _logger;
  private readonly IOptionsMonitor<PhoneIntegrationOptions> _options;
  private readonly HttpClient _httpClient;

  public PhoneContactLookupService(
    ILogger<PhoneContactLookupService> logger,
    IOptionsMonitor<PhoneIntegrationOptions> options,
    HttpClient httpClient)
  {
    _logger = logger;
    _options = options;
    _httpClient = httpClient;
  }

  /// <summary>
  /// Look up a contact name by phone number.
  /// Returns the contact name if found, otherwise the raw phone number.
  /// </summary>
  public async Task<string> FindCallerNameAsync(string phoneNumber, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(phoneNumber))
      return "Unknown caller";

    try
    {
      var baseUrl = _options.CurrentValue.ContactsApiBaseUrl.TrimEnd('/');
      var url = $"{baseUrl}/api/contacts/lookup?phone={Uri.EscapeDataString(phoneNumber)}";

      _logger.LogDebug("Looking up contact for {PhoneNumber}", phoneNumber);

      var response = await _httpClient.GetAsync(url, cancellationToken);

      if (response.IsSuccessStatusCode)
      {
        var contact = await response.Content.ReadFromJsonAsync<ContactLookupResponse>(cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(contact?.Name))
        {
          var maskedNumber = phoneNumber.Length > 4
            ? $"***{phoneNumber[^4..]}"
            : "***";
          _logger.LogDebug("Resolved {PhoneNumber} → {Name}", maskedNumber, contact.Name);
          return contact.Name;
        }
      }
      else
      {
        _logger.LogDebug("Contact lookup returned {StatusCode} for {PhoneNumber}",
          response.StatusCode, phoneNumber);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Contact lookup failed for {PhoneNumber}", phoneNumber);
    }

    // Fall back to the raw phone number
    return phoneNumber;
  }

  /// <summary>
  /// Expected response shape from the RotaryPhone contacts API.
  /// Schema is assumed and may need adjustment during integration.
  /// </summary>
  private class ContactLookupResponse
  {
    public string? Name { get; set; }
    public string? PhoneNumber { get; set; }
  }
}
