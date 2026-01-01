using Microsoft.AspNetCore.Mvc;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.SoundFlow;

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
  private readonly SoundFlowAudioEngine? _audioEngine;
  private readonly LocalAudioOutput? _localOutput;
  private readonly GoogleCastOutput? _castOutput;
  private readonly HttpStreamOutput? _httpOutput;

  /// <summary>
  /// Initializes a new instance of the DevicesController.
  /// </summary>
  public DevicesController(
    ILogger<DevicesController> logger,
    IAudioDeviceManager deviceManager,
    SoundFlowAudioEngine? audioEngine = null,
    LocalAudioOutput? localOutput = null,
    GoogleCastOutput? castOutput = null,
    HttpStreamOutput? httpOutput = null)
  {
    _logger = logger;
    _deviceManager = deviceManager;
    _audioEngine = audioEngine;
    _localOutput = localOutput;
    _castOutput = castOutput;
    _httpOutput = httpOutput;
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

      var deviceId = request.DeviceId;
      _logger.LogInformation("Switching audio output to: {DeviceId}", deviceId);

      // Handle virtual outputs (HTTP Stream, Google Cast)
      if (deviceId == "http-stream")
      {
        // Activate HTTP stream output, deactivate others
        await ActivateOutputAsync(_httpOutput, "HTTP Stream");
        await DeactivateOutputAsync(_castOutput, "Google Cast");
        // Local output stays active as the source
      }
      else if (deviceId == "google-cast")
      {
        // Activate Cast output, deactivate HTTP (Cast uses HTTP stream internally)
        await ActivateOutputAsync(_castOutput, "Google Cast");
        await DeactivateOutputAsync(_httpOutput, "HTTP Stream");
        // Local output stays active as the source
      }
      else
      {
        // Local audio device - switch the local output device
        await DeactivateOutputAsync(_castOutput, "Google Cast");
        await DeactivateOutputAsync(_httpOutput, "HTTP Stream");

        // Switch the actual SoundFlow playback device
        if (_audioEngine != null)
        {
          var deviceIndex = _audioEngine.GetDeviceIndexById(deviceId);
          if (deviceIndex >= 0)
          {
            var success = _audioEngine.SwitchPlaybackDevice(deviceIndex);
            if (!success)
            {
              _logger.LogWarning("Failed to switch SoundFlow playback device to {DeviceId}", deviceId);
            }
          }
          else
          {
            _logger.LogDebug("Device {DeviceId} is not a local playback device, skipping engine switch", deviceId);
          }
        }

        if (_localOutput != null)
        {
          await _localOutput.SelectDeviceAsync(deviceId);
        }
      }

      await _deviceManager.SetOutputDeviceAsync(deviceId);
      _logger.LogInformation("Output device set to {DeviceId}", deviceId);

      return Ok(new { message = "Output device set", deviceId = deviceId });
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
  /// Activates an audio output if available.
  /// </summary>
  private async Task ActivateOutputAsync(IAudioOutput? output, string name)
  {
    if (output == null)
    {
      _logger.LogDebug("{Name} output not available", name);
      return;
    }

    try
    {
      if (output.State == AudioOutputState.Created)
      {
        await output.InitializeAsync();
      }

      if (output.State == AudioOutputState.Ready || output.State == AudioOutputState.Stopped)
      {
        await output.StartAsync();
        _logger.LogInformation("{Name} output activated", name);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to activate {Name} output", name);
    }
  }

  /// <summary>
  /// Deactivates an audio output if running.
  /// </summary>
  private async Task DeactivateOutputAsync(IAudioOutput? output, string name)
  {
    if (output == null)
    {
      return;
    }

    try
    {
      if (output.State == AudioOutputState.Streaming)
      {
        await output.StopAsync();
        _logger.LogInformation("{Name} output deactivated", name);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to deactivate {Name} output", name);
    }
  }

  /// <summary>
  /// Discovers available Google Cast devices on the network.
  /// </summary>
  /// <returns>List of available Cast devices.</returns>
  [HttpGet("cast")]
  [ProducesResponseType(typeof(List<CastDeviceDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<IActionResult> DiscoverCastDevices(CancellationToken cancellationToken)
  {
    if (_castOutput == null)
    {
      return StatusCode(503, new { error = "Google Cast output not available" });
    }

    try
    {
      _logger.LogInformation("Discovering Google Cast devices...");

      // Ensure Cast output is initialized
      if (_castOutput.State == AudioOutputState.Created)
      {
        await _castOutput.InitializeAsync(cancellationToken);
      }

      // Add a timeout to prevent blocking forever if mDNS discovery hangs
      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

      var devices = await _castOutput.DiscoverDevicesAsync(timeoutCts.Token);

      var result = devices.Select(d => new CastDeviceDto
      {
        Id = d.Id,
        Name = d.FriendlyName,
        IpAddress = d.IpAddress,
        Port = d.Port,
        Model = d.Model
      }).ToList();

      _logger.LogInformation("Found {Count} Google Cast devices", result.Count);
      return Ok(result);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      // Timeout occurred during discovery
      _logger.LogWarning("Cast device discovery timed out after 15 seconds");
      return Ok(new List<CastDeviceDto>()); // Return empty list on timeout
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error discovering Cast devices");
      return StatusCode(500, new { error = "Failed to discover Cast devices", details = ex.Message });
    }
  }

  /// <summary>
  /// Connects to a specific Google Cast device.
  /// </summary>
  /// <param name="request">The Cast device to connect to.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>Success or error response.</returns>
  [HttpPost("cast/connect")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<IActionResult> ConnectToCastDevice(
    [FromBody] ConnectCastDeviceRequest request,
    CancellationToken cancellationToken)
  {
    if (_castOutput == null)
    {
      return StatusCode(503, new { error = "Google Cast output not available" });
    }

    if (string.IsNullOrWhiteSpace(request.DeviceId) ||
        string.IsNullOrWhiteSpace(request.IpAddress))
    {
      return BadRequest(new { error = "DeviceId and IpAddress are required" });
    }

    try
    {
      _logger.LogInformation("Connecting to Cast device: {Name} at {IP}",
        request.Name, request.IpAddress);

      // Ensure Cast output is initialized
      if (_castOutput.State == AudioOutputState.Created)
      {
        await _castOutput.InitializeAsync(cancellationToken);
      }

      // Create device info for connection
      var deviceInfo = new ChromecastDeviceInfo
      {
        Id = request.DeviceId,
        FriendlyName = request.Name ?? "Cast Device",
        IpAddress = request.IpAddress,
        Port = request.Port ?? 8009,
        Model = request.Model ?? "Unknown"
      };

      await _castOutput.ConnectAsync(deviceInfo, cancellationToken);

      _logger.LogInformation("Connected to Cast device: {Name}", request.Name);
      return Ok(new { message = "Connected to Cast device", device = request.Name });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error connecting to Cast device: {Name}", request.Name);
      return StatusCode(500, new { error = "Failed to connect to Cast device", details = ex.Message });
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
