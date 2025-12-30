using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Controllers;

/// <summary>
/// API controller for audio device management.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DevicesController : ControllerBase
{
  private readonly ILogger<DevicesController> _logger;
  private readonly IAudioDeviceManager _deviceManager;

  /// <summary>
  /// Initializes a new instance of the DevicesController.
  /// </summary>
  public DevicesController(
    ILogger<DevicesController> logger,
    IAudioDeviceManager deviceManager)
  {
    _logger = logger;
    _deviceManager = deviceManager;
  }

  /// <summary>
  /// Gets all available audio output devices.
  /// </summary>
  /// <returns>List of output devices.</returns>
  [HttpGet("output")]
  [ProducesResponseType(typeof(List<AudioDeviceDto>), StatusCodes.Status200OK)]
  public async Task<ActionResult<List<AudioDeviceDto>>> GetOutputDevices()
  {
    try
    {
      var devices = await _deviceManager.GetOutputDevicesAsync();
      return Ok(devices.Select(MapToDeviceDto).ToList());
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting output devices");
      return StatusCode(500, new { error = "Failed to get output devices" });
    }
  }

  /// <summary>
  /// Gets all available audio input devices.
  /// </summary>
  /// <returns>List of input devices.</returns>
  [HttpGet("input")]
  [ProducesResponseType(typeof(List<AudioDeviceDto>), StatusCodes.Status200OK)]
  public async Task<ActionResult<List<AudioDeviceDto>>> GetInputDevices()
  {
    try
    {
      var devices = await _deviceManager.GetInputDevicesAsync();
      return Ok(devices.Select(MapToDeviceDto).ToList());
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting input devices");
      return StatusCode(500, new { error = "Failed to get input devices" });
    }
  }

  /// <summary>
  /// Gets the default output device.
  /// </summary>
  /// <returns>The default output device.</returns>
  [HttpGet("output/default")]
  [ProducesResponseType(typeof(AudioDeviceDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<AudioDeviceDto>> GetDefaultOutputDevice()
  {
    try
    {
      var device = await _deviceManager.GetDefaultOutputDeviceAsync();
      if (device == null)
      {
        return NotFound(new { error = "No default output device found" });
      }
      return Ok(MapToDeviceDto(device));
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting default output device");
      return StatusCode(500, new { error = "Failed to get default output device" });
    }
  }

  /// <summary>
  /// Sets the preferred output device.
  /// </summary>
  /// <param name="request">The device selection request.</param>
  /// <returns>Success or error response.</returns>
  [HttpPost("output")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> SetOutputDevice([FromBody] SetOutputDeviceRequest request)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(request.DeviceId))
      {
        return BadRequest(new { error = "DeviceId is required" });
      }

      await _deviceManager.SetOutputDeviceAsync(request.DeviceId);
      _logger.LogInformation("Output device set to {DeviceId}", request.DeviceId);

      return Ok(new { message = "Output device set", deviceId = request.DeviceId });
    }
    catch (ArgumentException ex)
    {
      _logger.LogWarning(ex, "Invalid device ID: {DeviceId}", request.DeviceId);
      return NotFound(new { error = ex.Message });
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
    {
      _logger.LogWarning(ex, "Device not found: {DeviceId}", request.DeviceId);
      return NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting output device");
      return StatusCode(500, new { error = "Failed to set output device" });
    }
  }

  /// <summary>
  /// Refreshes the device list.
  /// </summary>
  /// <returns>Success or error response.</returns>
  [HttpPost("refresh")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<IActionResult> RefreshDevices()
  {
    try
    {
      await _deviceManager.RefreshDevicesAsync();
      _logger.LogInformation("Device list refreshed");
      return Ok(new { message = "Device list refreshed" });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error refreshing devices");
      return StatusCode(500, new { error = "Failed to refresh devices" });
    }
  }

  /// <summary>
  /// Gets all known USB ports and their reservation status.
  /// </summary>
  /// <returns>List of USB ports.</returns>
  [HttpGet("usb")]
  [ProducesResponseType(typeof(List<UsbPortDto>), StatusCodes.Status200OK)]
  public async Task<ActionResult<List<UsbPortDto>>> GetUsbPorts()
  {
    try
    {
      // Get USB port info from the device manager's reservations
      var reservations = (_deviceManager as Radio.Infrastructure.Audio.SoundFlow.SoundFlowDeviceManager)?
        .GetUSBPortReservations() ?? new Dictionary<string, string>();

      // Get actual USB audio devices from the device manager
      var inputDevices = await _deviceManager.GetInputDevicesAsync();
      var outputDevices = await _deviceManager.GetOutputDevicesAsync();
      
      // Combine and filter for USB devices only
      var allDevices = inputDevices.Concat(outputDevices)
        .Where(d => d.IsUSBDevice && !string.IsNullOrEmpty(d.USBPort))
        .ToList();
      
      // Create USB port DTOs with reservation status
      // USBPort is guaranteed non-null by the Where clause filtering, so we use the null-forgiving operator
      var usbPorts = allDevices
        .GroupBy(d => d.USBPort!)  // Non-null due to Where clause
        .Select(g => g.First())
        .Select(device => new UsbPortDto
        {
          Id = device.USBPort!,
          Name = $"{device.Name} ({device.USBPort})",
          IsReserved = reservations.ContainsKey(device.USBPort!),
          ReservedBy = reservations.TryGetValue(device.USBPort!, out var sourceId) ? sourceId : null
        })
        .ToList();
      
      // If no USB devices found, return empty list with message
      if (!usbPorts.Any())
      {
        _logger.LogInformation("No USB audio devices found");
      }
      else
      {
        _logger.LogInformation("Found {Count} USB audio devices", usbPorts.Count);
      }

      return Ok(usbPorts);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting USB ports");
      return StatusCode(500, new { error = "Failed to get USB ports" });
    }
  }

  /// <summary>
  /// Gets USB port reservations.
  /// </summary>
  /// <returns>Map of USB port to source ID reservations.</returns>
  [HttpGet("usb/reservations")]
  [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
  public ActionResult<Dictionary<string, string>> GetUSBReservations()
  {
    try
    {
      // The device manager tracks reservations internally
      // For now, we return information about common USB ports
      var commonPorts = new[] { "/dev/ttyUSB0", "/dev/ttyUSB1", "/dev/ttyUSB2" };
      var reservations = new Dictionary<string, object>();

      foreach (var port in commonPorts)
      {
        reservations[port] = new
        {
          isInUse = _deviceManager.IsUSBPortInUse(port)
        };
      }

      return Ok(reservations);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting USB reservations");
      return StatusCode(500, new { error = "Failed to get USB reservations" });
    }
  }

  /// <summary>
  /// Checks if a USB port is in use.
  /// </summary>
  /// <param name="port">The USB port to check (URL encoded).</param>
  /// <returns>Whether the port is in use.</returns>
  [HttpGet("usb/check")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public ActionResult CheckUSBPort([FromQuery] string port)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(port))
      {
        return BadRequest(new { error = "Port parameter is required" });
      }

      var isInUse = _deviceManager.IsUSBPortInUse(port);
      return Ok(new { port, isInUse });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error checking USB port");
      return StatusCode(500, new { error = "Failed to check USB port" });
    }
  }

  private static AudioDeviceDto MapToDeviceDto(AudioDeviceInfo device)
  {
    return new AudioDeviceDto
    {
      Id = device.Id,
      Name = device.Name,
      Type = device.Type.ToString(),
      IsDefault = device.IsDefault,
      IsUSBDevice = device.IsUSBDevice,
      USBPort = device.USBPort,
      MaxChannels = device.MaxChannels,
      SupportedSampleRates = device.SupportedSampleRates
    };
  }
}
