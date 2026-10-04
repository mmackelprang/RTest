using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Bluetooth;
using Radio.Core.Utilities;

namespace Radio.Infrastructure.External;

/// <summary>
/// Resolves a caller's number to a name for the spoken incoming-call announcement
/// (<c>PhoneCallIntegrationService</c>). Two sources, in order (PHN-14):
/// <list type="number">
///   <item>The stored synced phone books (PBAP), on every phone ever synced — the connected phone first,
///   then the most recently synced. A name resolves with no phone connected.</item>
///   <item>RotaryPhone's own contacts list, <c>GET {ContactsApiBaseUrl}/api/contacts</c>, matched here.</item>
/// </list>
/// Ranking (owner ruling, 2026-10-03): <b>match quality first, then source</b> — synced-exact, then
/// RotaryPhone-exact, then synced local-entry, then RotaryPhone local-entry. An exact match on either source beats
/// a local-entry match on either. Returns the raw number when nothing matches.
/// The Web's incoming-call banner (<c>IncomingCallBannerService</c>) uses the same two sources, the same order
/// and the same matching rule (<see cref="PhoneNumberNormalizer.FindMatch{T}"/>).
/// </summary>
/// <remarks>
/// ⚠ <b>Until PHN-14 the second source never answered.</b> This class asked for
/// <c>/api/contacts/lookup?phone=</c>, a route RotaryPhone has never had: its <c>ContactsController</c> routes
/// <c>GET /api/contacts/{id}</c>, so <c>lookup</c> was read as a contact id and every request returned 404
/// (read from RotaryPhone's <c>main</c>, 2026-10-03). The list route is the one the Web has always read.
/// </remarks>
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
  /// Look up a contact name by phone number: the stored synced phone books first, then RotaryPhone's contacts.
  /// Returns the contact name if found, otherwise the raw phone number.
  /// </summary>
  public async Task<string> FindCallerNameAsync(string phoneNumber, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(phoneNumber))
    {
      return "Unknown caller";
    }

    // 1. The stored synced phone books — every phone, not only a connected one (PHN-14). An exact hit returns at
    // once; a local-entry hit waits until RotaryPhone has had the chance to match exactly.
    string? pbapLocalEntry = null;
    if (_pbapRepo != null)
    {
      try
      {
        var normalized = PhoneNumberNormalizer.Normalize(phoneNumber);
        var match = normalized.Length == 0
          ? null
          : await _pbapRepo.FindByPhoneNumberAnyDeviceAsync(
            normalized, _bluetoothService?.ConnectedDevice?.Address, cancellationToken);
        if (match != null)
        {
          // Debug, not Information: the file sink is the only place this would land, and the device it came
          // from is the useful part. The number is masked and the name never logged (PHN-5).
          _logger.LogDebug("PBAP contact resolved for {Number} from {Device} ({Kind} match)",
            LogSafeText.ForPhone(phoneNumber), match.DeviceAddress, match.IsExactMatch ? "exact" : "local-entry");
          if (match.IsExactMatch)
          {
            return match.DisplayName;
          }
          pbapLocalEntry = match.DisplayName;
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "PBAP contact lookup failed, falling through to REST lookup");
      }
    }

    // 2. RotaryPhone's contacts list, matched by the same rule.
    try
    {
      var baseUrl = _options.CurrentValue.ContactsApiBaseUrl.TrimEnd('/');
      var url = $"{baseUrl}/api/contacts";

      _logger.LogDebug("Looking up contact for {PhoneNumber}", LogSafeText.ForPhone(phoneNumber));

      var response = await _httpClient.GetAsync(url, cancellationToken);

      if (response.IsSuccessStatusCode)
      {
        var contacts = await response.Content.ReadFromJsonAsync<List<ContactListEntry>>(cancellationToken: cancellationToken);
        bool found = PhoneNumberNormalizer.TryFindMatch(
          contacts?.Where(c => !string.IsNullOrWhiteSpace(c.Name)), c => c.PhoneNumber, phoneNumber,
          out var contact, out bool rotaryExact);
        if (pbapLocalEntry is not null && !(found && rotaryExact))
        {
          // 3. A synced local entry beats RotaryPhone's local entry, but not RotaryPhone's exact match.
          return pbapLocalEntry;
        }
        if (found && contact is not null)
        {
          // ⚠ The inline "***{last4}" mask that used to be computed here is GONE, and its removal
          // is the point of PHN-5 rather than a side effect. It was the file's own local idiom,
          // applied on exactly one of six lines, and it left contact.Name in clear on the one line
          // it masked. One mask, one shape, every line — see plan PHN-5 §1.2.
          _logger.LogDebug("Contact lookup resolved {PhoneNumber}", LogSafeText.ForPhone(phoneNumber));
          return contact.Name!.Trim();
        }
        _logger.LogDebug("Contact lookup found no RotaryPhone contact for {PhoneNumber}",
          LogSafeText.ForPhone(phoneNumber));
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

    // A synced local entry still stands when RotaryPhone could not be read.
    return pbapLocalEntry ?? phoneNumber;
  }

  /// <summary>One entry of RotaryPhone's <c>GET /api/contacts</c> (its <c>Contact</c>; other fields ignored).</summary>
  private sealed class ContactListEntry
  {
    public string? Name { get; set; }
    public string? PhoneNumber { get; set; }
  }
}
