using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Bluetooth;
using Radio.Core.Utilities;

namespace Radio.Infrastructure.External;

/// <summary>
/// REST client for looking up contact names from the RotaryPhone contacts API.
/// Falls back to the raw phone number if the API is unavailable or no match is found.
/// Now checks PBAP-synced contacts first (local, fast) before hitting the REST API.
/// </summary>
public class PhoneContactLookupService
{
  private readonly ILogger<PhoneContactLookupService> _logger;
  private readonly IOptionsMonitor<PhoneIntegrationOptions> _options;
  private readonly HttpClient _httpClient;
  private readonly IPbapContactRepository? _pbapRepo;
  private readonly IBluetoothService? _bluetoothService;

  public PhoneContactLookupService(
    ILogger<PhoneContactLookupService> logger,
    IOptionsMonitor<PhoneIntegrationOptions> options,
    HttpClient httpClient,
    IPbapContactRepository? pbapRepo = null,
    IBluetoothService? bluetoothService = null)
  {
    _logger = logger;
    _options = options;
    _httpClient = httpClient;
    _pbapRepo = pbapRepo;
    _bluetoothService = bluetoothService;
  }

  /// <summary>
  /// Look up a contact name by phone number.
  /// Checks PBAP contacts first, then falls back to RotaryPhone REST API.
  /// Returns the contact name if found, otherwise the raw phone number.
  /// </summary>
  public async Task<string> FindCallerNameAsync(string phoneNumber, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(phoneNumber))
    {
      return "Unknown caller";
    }

    // Try PBAP contacts first (local, fast)
    if (_pbapRepo != null && _bluetoothService != null)
    {
      try
      {
        var connectedDevice = _bluetoothService.ConnectedDevice;
        if (connectedDevice != null)
        {
          var normalized = PhoneNumberNormalizer.Normalize(phoneNumber);
          var contact = await _pbapRepo.FindByPhoneNumberAsync(connectedDevice.Address, normalized, cancellationToken);
          if (contact != null)
          {
            _logger.LogInformation("PBAP contact resolved for {Number}",
              LogSafeText.ForPhone(phoneNumber));
            return contact.DisplayName;
          }
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "PBAP contact lookup failed, falling through to REST lookup");
      }
    }

    try
    {
      var baseUrl = _options.CurrentValue.ContactsApiBaseUrl.TrimEnd('/');
      var url = $"{baseUrl}/api/contacts/lookup?phone={Uri.EscapeDataString(phoneNumber)}";

      _logger.LogDebug("Looking up contact for {PhoneNumber}", LogSafeText.ForPhone(phoneNumber));

      var response = await _httpClient.GetAsync(url, cancellationToken);

      if (response.IsSuccessStatusCode)
      {
        var contact = await response.Content.ReadFromJsonAsync<ContactLookupResponse>(cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(contact?.Name))
        {
          // ⚠ The inline "***{last4}" mask that used to be computed here is GONE, and its removal
          // is the point of PHN-5 rather than a side effect. It was the file's own local idiom,
          // applied on exactly one of six lines, and it left contact.Name in clear on the one line
          // it masked. One mask, one shape, every line — see plan PHN-5 §1.2.
          _logger.LogDebug("Contact lookup resolved {PhoneNumber}", LogSafeText.ForPhone(phoneNumber));
          return contact.Name;
        }
      }
      else
      {
        _logger.LogDebug("Contact lookup returned {StatusCode} for {PhoneNumber}",
          response.StatusCode, LogSafeText.ForPhone(phoneNumber));
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Contact lookup failed for {PhoneNumber}",
        LogSafeText.ForPhone(phoneNumber));
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
