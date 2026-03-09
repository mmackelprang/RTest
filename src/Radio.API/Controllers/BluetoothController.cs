using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for managing Bluetooth adapter and devices.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BluetoothController : ControllerBase
{
  private readonly ILogger<BluetoothController> _logger;
  private readonly IBluetoothService _bluetoothService;

  public BluetoothController(
    ILogger<BluetoothController> logger,
    IBluetoothService bluetoothService)
  {
    _logger = logger;
    _bluetoothService = bluetoothService;
  }

  [HttpGet("status")]
  [ProducesResponseType(typeof(BluetoothStatusDto), StatusCodes.Status200OK)]
  public ActionResult<BluetoothStatusDto> GetStatus()
  {
    var status = BuildStatus();
    return Ok(status);
  }

  [HttpPost("start")]
  [ProducesResponseType(typeof(BluetoothStatusDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<BluetoothStatusDto>> StartAsync([FromBody] BluetoothStartRequest request)
  {
    if (!_bluetoothService.IsAvailable)
    {
      return BadRequest(new { error = "Bluetooth not available on this platform" });
    }

    var deviceName = string.IsNullOrWhiteSpace(request.DeviceName)
      ? "SoundFlow Bluetooth"
      : request.DeviceName!;

    var started = await _bluetoothService.StartAsync(deviceName);
    if (!started)
    {
      return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to start Bluetooth adapter" });
    }

    return Ok(BuildStatus());
  }

  [HttpPost("stop")]
  [ProducesResponseType(typeof(BluetoothStatusDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<BluetoothStatusDto>> StopAsync()
  {
    await _bluetoothService.StopAsync();
    return Ok(BuildStatus());
  }

  [HttpPost("discovery/start")]
  [ProducesResponseType(StatusCodes.Status202Accepted)]
  public async Task<IActionResult> StartDiscoveryAsync()
  {
    await _bluetoothService.StartDiscoveryAsync();
    return Accepted();
  }

  [HttpPost("discovery/stop")]
  [ProducesResponseType(StatusCodes.Status202Accepted)]
  public async Task<IActionResult> StopDiscoveryAsync()
  {
    await _bluetoothService.StopDiscoveryAsync();
    return Accepted();
  }

  [HttpPost("pair")]
  [ProducesResponseType(typeof(BluetoothStatusDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<BluetoothStatusDto>> PairAsync([FromBody] BluetoothDeviceRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.DeviceAddress))
    {
      return BadRequest(new { error = "DeviceAddress is required" });
    }

    var paired = await _bluetoothService.PairDeviceAsync(request.DeviceAddress);
    if (!paired)
    {
      return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to pair device" });
    }

    return Ok(BuildStatus());
  }

  [HttpPost("unpair")]
  [ProducesResponseType(typeof(BluetoothStatusDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<BluetoothStatusDto>> UnpairAsync([FromBody] BluetoothDeviceRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.DeviceAddress))
    {
      return BadRequest(new { error = "DeviceAddress is required" });
    }

    var unpaired = await _bluetoothService.UnpairDeviceAsync(request.DeviceAddress);
    if (!unpaired)
    {
      return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to unpair device" });
    }

    return Ok(BuildStatus());
  }

  [HttpPost("accept")]
  [ProducesResponseType(typeof(BluetoothStatusDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<BluetoothStatusDto>> AcceptAsync([FromBody] BluetoothDeviceRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.DeviceAddress))
    {
      return BadRequest(new { error = "DeviceAddress is required" });
    }

    var accepted = await _bluetoothService.AcceptConnectionAsync(request.DeviceAddress);
    if (!accepted)
    {
      return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to accept connection" });
    }

    return Ok(BuildStatus());
  }

  [HttpPost("connect")]
  [ProducesResponseType(typeof(BluetoothStatusDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<BluetoothStatusDto>> ConnectAsync([FromBody] BluetoothDeviceRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.DeviceAddress))
    {
      return BadRequest(new { error = "DeviceAddress is required" });
    }

    var connected = await _bluetoothService.ConnectAsync(request.DeviceAddress);
    if (!connected)
    {
      return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to connect to device" });
    }

    return Ok(BuildStatus());
  }

  [HttpPost("disconnect")]
  [ProducesResponseType(typeof(BluetoothStatusDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<BluetoothStatusDto>> DisconnectAsync(
    [FromBody] BluetoothDeviceRequest? request = null)
  {
    if (!string.IsNullOrWhiteSpace(request?.DeviceAddress))
      await _bluetoothService.DisconnectAsync(request.DeviceAddress);
    else
      await _bluetoothService.DisconnectAsync();
    return Ok(BuildStatus());
  }

  private BluetoothStatusDto BuildStatus()
  {
    return new BluetoothStatusDto
    {
      IsAvailable = _bluetoothService.IsAvailable,
      State = _bluetoothService.State.ToString(),
      IsDiscovering = _bluetoothService.IsDiscovering,
      ConnectedDevice = _bluetoothService.ConnectedDevice != null
        ? MapDevice(_bluetoothService.ConnectedDevice)
        : null,
      PairedDevices = _bluetoothService.PairedDevices?.Select(MapDevice).ToList() ?? new List<BluetoothDeviceDto>(),
      DiscoveredDevices = _bluetoothService.DiscoveredDevices?.Select(MapDevice).ToList() ?? new List<BluetoothDeviceDto>()
    };
  }

  private static BluetoothDeviceDto MapDevice(BluetoothDeviceInfo device)
  {
    return new BluetoothDeviceDto
    {
      Address = device.Address,
      Name = device.Name,
      IsPaired = device.IsPaired,
      IsConnected = device.IsConnected,
      LastConnected = device.LastConnected
    };
  }
}
