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
  public async Task<ActionResult<BluetoothStatusDto>> GetStatus()
  {
    var status = await BuildStatusAsync();
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

    return Ok(await BuildStatusAsync());
  }

  [HttpPost("stop")]
  [ProducesResponseType(typeof(BluetoothStatusDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<BluetoothStatusDto>> StopAsync()
  {
    await _bluetoothService.StopAsync();
    return Ok(await BuildStatusAsync());
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

    return Ok(await BuildStatusAsync());
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

    return Ok(await BuildStatusAsync());
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

    return Ok(await BuildStatusAsync());
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

    return Ok(await BuildStatusAsync());
  }

  [HttpPost("disconnect")]
  [ProducesResponseType(typeof(BluetoothStatusDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<BluetoothStatusDto>> DisconnectAsync(
    [FromBody] BluetoothDeviceRequest? request = null)
  {
    if (!string.IsNullOrWhiteSpace(request?.DeviceAddress))
    {
      await _bluetoothService.DisconnectAsync(request.DeviceAddress);
    }
    else
    {
      await _bluetoothService.DisconnectAsync();
    }
    return Ok(await BuildStatusAsync());
  }

  [HttpPost("cancel-reconnect")]
  [ProducesResponseType(typeof(BluetoothStatusDto), StatusCodes.Status200OK)]
  public async Task<ActionResult<BluetoothStatusDto>> CancelReconnect()
  {
    _bluetoothService.CancelReconnection();
    return Ok(await BuildStatusAsync());
  }

  private async Task<BluetoothStatusDto> BuildStatusAsync()
  {
    var connected = _bluetoothService.ConnectedDevice;
    var dto = new BluetoothStatusDto
    {
      IsAvailable = _bluetoothService.IsAvailable,
      State = _bluetoothService.State.ToString(),
      IsDiscovering = _bluetoothService.IsDiscovering,
      ConnectedDevice = connected != null ? MapDevice(connected) : null,
      PairedDevices = _bluetoothService.PairedDevices?.Select(MapDevice).ToList() ?? new List<BluetoothDeviceDto>(),
      DiscoveredDevices = _bluetoothService.DiscoveredDevices?.Select(MapDevice).ToList() ?? new List<BluetoothDeviceDto>(),
      IsReconnecting = _bluetoothService.IsReconnecting,
      LastDisconnectReason = _bluetoothService.LastDisconnectReason?.ToString()
    };

    // Plan C / FM-BT-6 — populate negotiated A2DP codec when a transport is
    // attached. GetA2dpCodecInfoAsync is null-safe (returns null if no
    // transport, or on D-Bus read failure).
    if (connected != null)
    {
      try
      {
        var codec = await _bluetoothService.GetA2dpCodecInfoAsync(connected.Address, HttpContext.RequestAborted);
        if (codec != null)
        {
          dto.CodecName = codec.CodecName;
          dto.SampleRateHz = codec.SampleRateHz;
          dto.Bitpool = codec.BitpoolOrNull;
        }
      }
      catch (Exception ex)
      {
        // Codec is diagnostic only — never fail the status read because of it.
        _logger.LogDebug(ex, "Failed to read A2DP codec for {Address}", connected.Address);
      }
    }

    return dto;
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
