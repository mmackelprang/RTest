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

  [HttpGet("lookup")]
  public async Task<IActionResult> LookupNumber([FromQuery] string phoneNumber, CancellationToken ct)
  {
    var deviceAddress = _bluetoothService.ConnectedDevice?.Address;
    if (string.IsNullOrEmpty(deviceAddress))
      return NotFound("No device currently connected");

    var normalized = PhoneNumberNormalizer.Normalize(phoneNumber);
    var contact = await _contactRepo.FindByPhoneNumberAsync(deviceAddress, normalized, ct);

    if (contact == null)
      return NotFound($"No contact found for {phoneNumber}");

    return Ok(new { contact.DisplayName, PhoneNumber = phoneNumber });
  }

  [HttpGet("status")]
  public async Task<IActionResult> GetStatus()
  {
    var status = await _syncService.GetSyncStatusAsync();
    return Ok(status);
  }
}
