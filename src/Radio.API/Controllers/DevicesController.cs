using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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

        // Validate the device exists before responding
        if (_audioEngine != null)
        {
          var deviceIndex = _audioEngine.GetDeviceIndexById(deviceId);
          if (deviceIndex < 0)
          {
            _logger.LogDebug("Device {DeviceId} is not a local playback device, skipping engine switch", deviceId);
          }
          else
          {
            // Persist preference and respond BEFORE the native switch.
            // SwitchPlaybackDevice calls native MiniAudio Stop/Dispose/Init/Start
            // which can tear down the HTTP socket before the response is sent.
            await _deviceManager.SetOutputDeviceAsync(deviceId);

            if (_localOutput != null)
            {
              _localOutput.UpdateDeviceId(deviceId);
            }

            _logger.LogInformation("Output device preference saved to {DeviceId}, starting native switch...", deviceId);

            // Fire-and-forget the native device switch on the thread pool
            var capturedIndex = deviceIndex;
            var capturedDeviceId = deviceId;
            _ = Task.Run(async () =>
            {
              try
              {
                var success = _audioEngine.SwitchPlaybackDevice(capturedIndex);
                if (!success)
                {
                  _logger.LogWarning("Failed to switch SoundFlow playback device to {DeviceId}", capturedDeviceId);
                }
                else
                {
                  _logger.LogInformation("Native playback device switch to {DeviceId} completed", capturedDeviceId);
                }
              }
              catch (Exception ex)
              {
                _logger.LogError(ex, "Error during native playback device switch to {DeviceId}", capturedDeviceId);
              }
            });

            return Ok(new { message = "Output device set", deviceId = deviceId });
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
      if (output.State == AudioOutputState.Error)
      {
        _logger.LogInformation("Recovering {Name} output from Error state", name);
        await output.InitializeAsync();
      }

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
      if (output.State == AudioOutputState.Streaming || output.State == AudioOutputState.Error)
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
  /// Gets diagnostic information about the Cast audio pipeline.
  /// </summary>
  [HttpGet("cast/diagnostics")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public IActionResult GetCastDiagnostics()
  {
    var tapDiag = _audioEngine?.GetOutputTapDiagnostics();
    var pipelineDiag = _audioEngine?.GetPipelineDiagnostics();

    return Ok(new
    {
      fingerprintTap = new
      {
        totalSamplesProcessed = pipelineDiag?.FingerprintTapTotalSamples ?? 0,
        lastProcessedTime = pipelineDiag?.FingerprintTapLastProcessedTime,
      },
      outputTap = tapDiag.HasValue ? new
      {
        totalBytesWritten = tapDiag.Value.TotalBytesWritten,
        totalWriteCalls = tapDiag.Value.TotalWriteCalls,
        lastWriteTime = tapDiag.Value.LastWriteTime,
        activeReaderCount = tapDiag.Value.ActiveReaderCount,
        bufferSize = tapDiag.Value.BufferSize,
      } : null,
      httpStream = new
      {
        state = _httpOutput?.State.ToString(),
        streamUrl = _httpOutput?.StreamUrl,
        connectedClients = _httpOutput?.ConnectedClientCount ?? 0,
      },
      cast = new
      {
        state = _castOutput?.State.ToString(),
        connectedDevice = _castOutput?.ConnectedDevice?.FriendlyName,
      },
      engine = new
      {
        state = pipelineDiag?.EngineState,
        playbackDeviceActive = pipelineDiag?.PlaybackDeviceActive ?? false,
        modifierCount = pipelineDiag?.ModifierCount ?? 0,
      }
    });
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

      // Wire the HTTP audio stream so the Chromecast has something to play
      if (_httpOutput != null)
      {
        // Recover HTTP output from Error state
        if (_httpOutput.State == AudioOutputState.Error)
        {
          _logger.LogInformation("HTTP stream output in Error state, reinitializing");
          await _httpOutput.InitializeAsync(cancellationToken);
        }

        // Ensure HTTP stream output is initialized and started
        if (_httpOutput.State == AudioOutputState.Created)
        {
          await _httpOutput.InitializeAsync(cancellationToken);
        }
        if (_httpOutput.State == AudioOutputState.Ready || _httpOutput.State == AudioOutputState.Stopped)
        {
          await _httpOutput.StartAsync(cancellationToken);
        }

        if (_httpOutput.State != AudioOutputState.Streaming)
        {
          _logger.LogWarning("HTTP stream output is not streaming (state: {State}), Cast will have no audio",
            _httpOutput.State);
        }

        // Resolve the actual LAN IP (Chromecast needs a routable IP, not a hostname)
        var streamUrl = GetRoutableStreamUrl(_httpOutput.StreamUrl, _httpOutput.Port);
        _castOutput.SetStreamUrl(streamUrl);
        _logger.LogInformation("Set Cast stream URL to {StreamUrl}", streamUrl);
      }
      else
      {
        _logger.LogWarning("HTTP stream output not available — Cast device will have no audio source");
      }

      // Start audio playback on the Cast device
      await _castOutput.StartAsync(cancellationToken);

      _logger.LogInformation("Connected to Cast device: {Name}, audio streaming started", request.Name);
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

  /// <summary>
  /// Replaces the hostname in a stream URL with the local LAN IP address.
  /// Chromecast devices need a routable IP, not a hostname.
  /// </summary>
  private string GetRoutableStreamUrl(string streamUrl, int port)
  {
    var localIp = GetLocalIPAddress();
    if (localIp != null)
    {
      // Build URL with routable IP
      var uri = new Uri(streamUrl);
      return $"http://{localIp}:{port}{uri.PathAndQuery}";
    }

    // Fall back to the original URL
    _logger.LogWarning("Could not resolve local LAN IP, using original stream URL");
    return streamUrl;
  }

  /// <summary>
  /// Gets the local LAN IP address by scanning network interfaces.
  /// </summary>
  private static string? GetLocalIPAddress()
  {
    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
    {
      if (ni.OperationalStatus != OperationalStatus.Up)
      {
        continue;
      }

      if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
      {
        continue;
      }

      var props = ni.GetIPProperties();
      foreach (var addr in props.UnicastAddresses)
      {
        if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
            !IPAddress.IsLoopback(addr.Address))
        {
          return addr.Address.ToString();
        }
      }
    }

    return null;
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
