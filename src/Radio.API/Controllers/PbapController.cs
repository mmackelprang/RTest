using Microsoft.AspNetCore.Mvc;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Bluetooth;
using Radio.Core.Utilities;

namespace Radio.API.Controllers;

[ApiController]
[Route("api/bluetooth/pbap")]
public class PbapController : ControllerBase
{
  private readonly IPbapSyncService _syncService;
  private readonly IPbapContactRepository _contactRepo;
  private readonly IBluetoothService _bluetoothService;

  public PbapController(
    IPbapSyncService syncService,
    IPbapContactRepository contactRepo,
    IBluetoothService bluetoothService)
  {
    _syncService = syncService;
    _contactRepo = contactRepo;
    _bluetoothService = bluetoothService;
  }

  [HttpPost("sync")]
  public async Task<IActionResult> SyncContacts([FromQuery] string? deviceAddress, CancellationToken ct)
  {
    deviceAddress ??= _bluetoothService.ConnectedDevice?.Address;
    if (string.IsNullOrEmpty(deviceAddress))
      return BadRequest("No device address specified and no device currently connected");

    var result = await _syncService.SyncContactsAsync(deviceAddress, ct);
    return Ok(result);
  }

  [HttpGet("contacts")]
  public async Task<IActionResult> GetContacts([FromQuery] string deviceAddress, CancellationToken ct)
  {
    if (string.IsNullOrEmpty(deviceAddress))
      return BadRequest("deviceAddress is required");

    var contacts = await _contactRepo.GetContactsAsync(deviceAddress, ct);
    return Ok(contacts);
  }

  /// <summary>
  /// Resolves a caller's number to a name from the stored synced phone books — every phone ever synced, not
  /// only the connected one (PHN-14). Until 2026-10-03 this answered <c>404 "No device currently
  /// connected"</c> whenever no phone was connected, though the box held three synced phone books.
  /// </summary>
  /// <remarks>
  /// Order: an exact digits match on any phone beats a last-seven-digits match on any phone; within each,
  /// the connected phone first, then the most recently synced. A <c>404</c> means no stored contact has the
  /// number, and nothing else: the Web caches it as a definitive miss. The <c>404</c> body does not echo the
  /// number (PHN-5).
  /// </remarks>
  [HttpGet("lookup")]
  public async Task<IActionResult> LookupNumber([FromQuery] string phoneNumber, CancellationToken ct)
  {
    var normalized = PhoneNumberNormalizer.Normalize(phoneNumber ?? "");
    if (normalized.Length == 0)
      return NotFound("No contact found");

    var match = await _contactRepo.FindByPhoneNumberAnyDeviceAsync(
      normalized, _bluetoothService.ConnectedDevice?.Address, ct);

    if (match == null)
      return NotFound("No contact found");

    return Ok(new { match.DisplayName, PhoneNumber = phoneNumber });
  }

  [HttpGet("status")]
  public async Task<IActionResult> GetStatus()
  {
    var status = await _syncService.GetSyncStatusAsync();
    return Ok(status);
  }
}
