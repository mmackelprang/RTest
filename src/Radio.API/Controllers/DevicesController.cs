using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Radio.API.Models;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Configuration.Models;
using IRadioConfigurationManager = Radio.Configuration.Abstractions.IConfigurationManager;

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
  private readonly IAudioManager? _audioManager;
  private readonly SoundFlowAudioEngine? _audioEngine;
  private readonly LocalAudioOutput? _localOutput;
  private readonly GoogleCastOutput? _castOutput;
  private readonly HttpStreamOutput? _httpOutput;
  private readonly IRadioConfigurationManager _configurationManager;
  private readonly IOptionsMonitor<AudioPreferences> _audioPreferences;
  private readonly IOptions<AudioOutputOptions> _audioOutputOptions;

  /// <summary>
  /// Initializes a new instance of the DevicesController.
  /// </summary>
  public DevicesController(
    ILogger<DevicesController> logger,
    IAudioDeviceManager deviceManager,
    IRadioConfigurationManager configurationManager,
    IOptionsMonitor<AudioPreferences> audioPreferences,
    IOptions<AudioOutputOptions> audioOutputOptions,
    IAudioManager? audioManager = null,
    SoundFlowAudioEngine? audioEngine = null,
    LocalAudioOutput? localOutput = null,
    GoogleCastOutput? castOutput = null,
    HttpStreamOutput? httpOutput = null)
  {
    _logger = logger;
    _deviceManager = deviceManager;
    _configurationManager = configurationManager;
    _audioPreferences = audioPreferences;
    _audioOutputOptions = audioOutputOptions;
    _audioManager = audioManager;
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
      var activeId = _deviceManager.GetSelectedOutputDeviceId();
      var hasUserSelection = !string.IsNullOrEmpty(activeId);
      return Ok(devices.Select(d =>
      {
        var isActive = d.Id == activeId;
        // If user has selected a device, that one is "default"; otherwise use system default
        var isDefault = hasUserSelection ? isActive : (bool?)null;
        return MapToDeviceDto(d, isActive, isDefault);
      }).ToList());
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
      var activeInputId = _deviceManager.GetSelectedInputDeviceId();
      var hasUserSelection = !string.IsNullOrEmpty(activeInputId);
      return Ok(devices.Select(d =>
      {
        var isActive = d.Id == activeInputId;
        var isDefault = hasUserSelection ? isActive : (bool?)null;
        return MapToDeviceDto(d, isActive, isDefault);
      }).ToList());
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting input devices");
      return StatusCode(500, new { error = "Failed to get input devices" });
    }
  }

  /// <summary>
  /// Sets the preferred input device.
  /// </summary>
  /// <param name="request">The device selection request.</param>
  /// <returns>Success or error response.</returns>
  [HttpPost("input")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> SetInputDevice([FromBody] SetOutputDeviceRequest request)
  {
    try
    {
      if (string.IsNullOrWhiteSpace(request.DeviceId))
      {
        return BadRequest(new { error = "DeviceId is required" });
      }

      await _deviceManager.SetInputDeviceAsync(request.DeviceId);
      return Ok(new { message = $"Input device set to {request.DeviceId}" });
    }
    catch (InvalidOperationException ex)
    {
      return NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting input device");
      return StatusCode(500, new { error = "Failed to set input device" });
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
      var activeId = _deviceManager.GetSelectedOutputDeviceId();
      return Ok(MapToDeviceDto(device, device.Id == activeId, isUserDefault: true));
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

        // Unmute local speakers when switching away from Cast
        _audioEngine?.SetLocalOutputMuted(false);
      }
      else if (deviceId == "google-cast")
      {
        // Activate Cast output AND HTTP stream — Cast streams audio via HTTP
        await ActivateOutputAsync(_castOutput, "Google Cast");
        await ActivateOutputAsync(_httpOutput, "HTTP Stream");

        // Mute local speakers — audio pipeline continues for HTTP/Cast streaming
        _audioEngine?.SetLocalOutputMuted(true);

        // Auto-connect to default Cast device if one is saved
        await TryAutoConnectDefaultCastDeviceAsync();
      }
      else
      {
        // Local audio device - switch the local output device
        await DeactivateOutputAsync(_castOutput, "Google Cast");
        await DeactivateOutputAsync(_httpOutput, "HTTP Stream");

        // Unmute local speakers when switching back from Cast
        _audioEngine?.SetLocalOutputMuted(false);

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
        directChannel = _castOutput?.DirectStreaming != null ? new
        {
          isStreaming = _castOutput.DirectStreaming.IsStreaming,
          totalChunksSent = _castOutput.DirectStreaming.TotalChunksSent,
          totalBytesSent = _castOutput.DirectStreaming.TotalBytesSent,
          sendErrors = _castOutput.DirectStreaming.SendErrors,
          lastChunkTime = _castOutput.DirectStreaming.LastChunkTime,
          lastRttMs = _castOutput.DirectStreaming.LastRttMs,
          lastPongJson = _castOutput.DirectStreaming.LastPongJson,
        } : null,
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
  /// Sends a ping to the DirectChannel Cast receiver and returns the pong response
  /// with latency metrics (transit delay, buffer-ahead, chunks dropped, etc.).
  /// </summary>
  [HttpPost("cast/ping")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> PingCastReceiver()
  {
    if (_castOutput?.DirectStreaming == null)
    {
      return BadRequest("DirectChannel streaming not active");
    }

    // Capture current state before sending ping
    var previousPong = _castOutput.DirectStreaming.LastPongJson;

    _logger.LogInformation("Cast ping: sending ping (previous pong: {HasPrev})",
      previousPong != null);

    var sent = await _castOutput.DirectStreaming.SendPingAsync();
    if (!sent)
    {
      return BadRequest("Failed to send ping — no active transport");
    }

    _logger.LogInformation("Cast ping: ping sent, waiting for pong...");

    // Wait up to 2s for a NEW pong response
    for (int i = 0; i < 20; i++)
    {
      await Task.Delay(100);
      var currentPong = _castOutput.DirectStreaming.LastPongJson;
      if (currentPong != null && !ReferenceEquals(currentPong, previousPong))
      {
        _logger.LogInformation("Cast ping: pong received after {Ms}ms", (i + 1) * 100);
        break;
      }
    }

    var rtt = _castOutput.DirectStreaming.LastRttMs;
    var pong = _castOutput.DirectStreaming.LastPongJson;
    _logger.LogInformation("Cast ping: result — RTT={Rtt}ms, hasPong={Has}",
      rtt, pong != null);

    return Ok(new
    {
      rttMs = rtt,
      pong
    });
  }

  /// <summary>
  /// Tests Cast playback with a known public audio URL.
  /// If this works but our stream doesn't, the issue is with our HTTP stream format.
  /// If this also fails, the issue is with the Cast device or connection.
  /// </summary>
  [HttpPost("cast/test-audio")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> TestCastAudio([FromQuery] string? url = null)
  {
    if (_castOutput == null)
    {
      return BadRequest("Cast output not available");
    }

    // Use a known-working public MP3 sample if no URL provided
    var testUrl = url ?? "https://www.soundhelix.com/examples/mp3/SoundHelix-Song-1.mp3";
    var contentType = testUrl.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav"
      : testUrl.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ? "audio/flac"
      : "audio/mpeg";

    _logger.LogInformation("Cast test: Loading public audio URL {Url} (type: {ContentType})", testUrl, contentType);

    try
    {
      var result = await _castOutput.TestPlayUrlAsync(testUrl, contentType);
      return Ok(result);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Cast test failed");
      return Ok(new { success = false, error = ex.Message });
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

      // Wire audio source based on streaming mode
      var castOptions = _audioOutputOptions.Value.GoogleCast;
      var isDirectChannel = string.Equals(castOptions.StreamingMode, "DirectChannel", StringComparison.OrdinalIgnoreCase);

      if (isDirectChannel)
      {
        // DirectChannel mode: audio engine sends PCM chunks directly over Cast protocol.
        // No HTTP stream needed — wire the audio engine to the Cast output instead.
        if (_audioEngine != null)
        {
          _castOutput.SetAudioEngine(_audioEngine);
          _logger.LogInformation("Cast: DirectChannel mode — audio engine wired, bypassing HTTP stream");
        }
        else
        {
          _logger.LogWarning("Cast: DirectChannel mode but audio engine not available — Cast will have no audio");
        }
      }
      else if (_httpOutput != null)
      {
        // HttpMp3 mode: wire the HTTP audio stream BEFORE connecting so auto-restart uses the correct URL
        _logger.LogInformation("HTTP stream output state before wiring: {State}", _httpOutput.State);

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
        else
        {
          _logger.LogInformation("HTTP stream output confirmed streaming on port {Port}", _httpOutput.Port);
        }

        // Use the MP3 endpoint — Cast requires MP3 for progressive HTTP streaming
        // (Chrome's audio element in the Default Media Receiver can't handle chunked WAV)
        var streamUrl = GetRoutableStreamUrl(_httpOutput.Mp3StreamUrl, _httpOutput.Port, request.IpAddress);
        _castOutput.SetStreamUrl(streamUrl);
        _logger.LogInformation("Set Cast stream URL to {StreamUrl} (MP3, 192kbps)", streamUrl);

        // Log output tap diagnostics to verify audio data is flowing
        var tapDiag = _audioEngine?.GetOutputTapDiagnostics();
        if (tapDiag.HasValue)
        {
          _logger.LogInformation(
            "Output tap: {WriteCalls} writes, {Bytes} bytes written, last write {LastWrite}",
            tapDiag.Value.TotalWriteCalls, tapDiag.Value.TotalBytesWritten, tapDiag.Value.LastWriteTime);
        }
      }
      else
      {
        _logger.LogWarning("HTTP stream output not available — Cast device will have no audio source");
      }

      // Push current now-playing metadata before connecting so it's included in the initial media load
      PushCurrentMetadataToCast();

      await _castOutput.ConnectAsync(deviceInfo, cancellationToken);

      // ConnectAsync auto-restarts if it was previously streaming.
      // Only start manually if not already streaming.
      if (_castOutput.State != AudioOutputState.Streaming)
      {
        await _castOutput.StartAsync(cancellationToken);
      }

      // Mute local speakers — audio pipeline continues for Cast streaming
      _audioEngine?.SetLocalOutputMuted(true);

      // Save as default Cast device for auto-connect
      await SaveDefaultCastDeviceAsync(request.DeviceId, request.Name ?? "Cast Device");

      _logger.LogInformation("Connected to Cast device: {Name}, audio streaming started (local output muted)", request.Name);
      return Ok(new { message = "Connected to Cast device", device = request.Name });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error connecting to Cast device: {Name}", request.Name);
      return StatusCode(500, new { error = "Failed to connect to Cast device", details = ex.Message });
    }
  }

  /// <summary>
  /// Disconnects from the currently connected Google Cast device.
  /// Unmutes local speakers so audio resumes from the local output.
  /// </summary>
  /// <returns>Success or error response.</returns>
  [HttpPost("cast/disconnect")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<IActionResult> DisconnectFromCastDevice(CancellationToken cancellationToken)
  {
    if (_castOutput == null)
    {
      return StatusCode(503, new { error = "Google Cast output not available" });
    }

    try
    {
      var deviceName = _castOutput.ConnectedDevice?.FriendlyName ?? "Cast Device";
      _logger.LogInformation("Disconnecting from Cast device: {Name}", deviceName);

      await _castOutput.StopAsync(cancellationToken);
      await _castOutput.DisconnectAsync(cancellationToken);

      // Stop HTTP stream if it was active (used by HttpMp3 mode)
      if (_httpOutput?.State == AudioOutputState.Streaming)
      {
        await _httpOutput.StopAsync(cancellationToken);
      }

      // Unmute local speakers so audio resumes locally
      _audioEngine?.SetLocalOutputMuted(false);

      _logger.LogInformation("Disconnected from Cast device: {Name}, local output unmuted", deviceName);
      return Ok(new { message = "Disconnected from Cast device", device = deviceName });
    }
    catch (Exception ex)
    {
      // Even if disconnect fails, unmute local so user isn't stuck with no audio
      _audioEngine?.SetLocalOutputMuted(false);
      _logger.LogError(ex, "Error disconnecting from Cast device");
      return StatusCode(500, new { error = "Failed to disconnect from Cast device", details = ex.Message });
    }
  }

  /// <summary>
  /// Gets cached Google Cast devices without running mDNS discovery.
  /// Returns instantly from the SQLite cache.
  /// </summary>
  [HttpGet("cast/cached")]
  [ProducesResponseType(typeof(List<CastDeviceDto>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<IActionResult> GetCachedCastDevices(CancellationToken cancellationToken)
  {
    if (_castOutput == null)
    {
      return StatusCode(503, new { error = "Google Cast output not available" });
    }

    try
    {
      var devices = await _castOutput.GetCachedDevicesAsync(cancellationToken);
      var result = devices.Select(d => new CastDeviceDto
      {
        Id = d.Id,
        Name = d.FriendlyName,
        IpAddress = d.IpAddress,
        Port = d.Port,
        Model = d.Model
      }).ToList();

      _logger.LogDebug("Returning {Count} cached Cast devices", result.Count);
      return Ok(result);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting cached Cast devices");
      return StatusCode(500, new { error = "Failed to get cached Cast devices" });
    }
  }

  /// <summary>
  /// Gets the default Cast device preference.
  /// </summary>
  [HttpGet("cast/default")]
  [ProducesResponseType(typeof(CastDeviceDto), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> GetDefaultCastDevice(CancellationToken cancellationToken)
  {
    var prefs = _audioPreferences.CurrentValue;
    if (string.IsNullOrEmpty(prefs.DefaultCastDeviceId))
    {
      return NotFound(new { error = "No default Cast device set" });
    }

    // Try to find the device in cache for full details
    if (_castOutput != null)
    {
      try
      {
        var cached = await _castOutput.GetCachedDevicesAsync(cancellationToken);
        var device = cached.FirstOrDefault(d => d.Id == prefs.DefaultCastDeviceId);
        if (device != null)
        {
          return Ok(new CastDeviceDto
          {
            Id = device.Id,
            Name = device.FriendlyName,
            IpAddress = device.IpAddress,
            Port = device.Port,
            Model = device.Model
          });
        }
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to look up default Cast device in cache");
      }
    }

    // Return minimal info from preferences
    return Ok(new CastDeviceDto
    {
      Id = prefs.DefaultCastDeviceId,
      Name = prefs.DefaultCastDeviceName,
      IpAddress = "",
      Port = 8009,
      Model = ""
    });
  }

  /// <summary>
  /// Sets the default Cast device preference.
  /// </summary>
  [HttpPost("cast/default")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> SetDefaultCastDevice(
    [FromBody] CastDeviceDto device,
    CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(device.Id))
    {
      return BadRequest(new { error = "Device ID is required" });
    }

    try
    {
      await SaveDefaultCastDeviceAsync(device.Id, device.Name);

      // Refresh device list so the name updates
      await _deviceManager.RefreshDevicesAsync(cancellationToken);

      return Ok(new { message = "Default Cast device set", deviceName = device.Name });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting default Cast device");
      return StatusCode(500, new { error = "Failed to set default Cast device" });
    }
  }

  /// <summary>
  /// Clears the default Cast device preference.
  /// </summary>
  [HttpDelete("cast/default")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public async Task<IActionResult> ClearDefaultCastDevice(CancellationToken cancellationToken)
  {
    try
    {
      await SaveDefaultCastDeviceAsync("", "");

      // Refresh device list so the name reverts to "Google Cast"
      await _deviceManager.RefreshDevicesAsync(cancellationToken);

      return Ok(new { message = "Default Cast device cleared" });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error clearing default Cast device");
      return StatusCode(500, new { error = "Failed to clear default Cast device" });
    }
  }

  /// <summary>
  /// Gets all output devices (including hidden) with display settings.
  /// Used by the Device Display Settings UI.
  /// </summary>
  [HttpGet("display")]
  [ProducesResponseType(typeof(List<DeviceDisplayInfoDto>), StatusCodes.Status200OK)]
  public async Task<IActionResult> GetDeviceDisplaySettings(CancellationToken cancellationToken)
  {
    try
    {
      if (_deviceManager is not SoundFlowDeviceManager sfDeviceManager)
      {
        return StatusCode(500, new { error = "Device manager does not support display settings" });
      }

      var devices = await sfDeviceManager.GetAllDevicesWithDisplayInfoAsync(cancellationToken);
      var result = devices.Select(d => new DeviceDisplayInfoDto
      {
        Id = d.DeviceId,
        RawName = d.RawName,
        DisplayName = d.DisplayName,
        IsHidden = d.IsHidden,
        FriendlyNameOverride = d.FriendlyNameOverride,
        Type = d.Type.ToString(),
        IsDefault = d.IsDefault,
        IsUSBDevice = d.IsUSBDevice
      }).ToList();

      return Ok(result);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error getting device display settings");
      return StatusCode(500, new { error = "Failed to get device display settings" });
    }
  }

  /// <summary>
  /// Sets visibility for a specific device by raw name.
  /// </summary>
  [HttpPut("display/visibility")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> SetDeviceVisibility(
    [FromBody] SetDeviceVisibilityRequest request,
    CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(request.RawName))
    {
      return BadRequest(new { error = "RawName is required" });
    }

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";

      // Read current lists
      var hiddenNames = await _configurationManager.GetValueAsync<List<string>>(
        storeId, "AudioOutput:DeviceDisplay:HiddenDeviceNames", ct: cancellationToken) ?? [];
      var visibleNames = await _configurationManager.GetValueAsync<List<string>>(
        storeId, "AudioOutput:DeviceDisplay:VisibleDeviceNames", ct: cancellationToken) ?? [];

      if (request.Visible)
      {
        // Remove from hidden, add to visible
        hiddenNames.Remove(request.RawName);
        if (!visibleNames.Contains(request.RawName, StringComparer.OrdinalIgnoreCase))
        {
          visibleNames.Add(request.RawName);
        }
      }
      else
      {
        // Add to hidden, remove from visible
        visibleNames.Remove(request.RawName);
        if (!hiddenNames.Contains(request.RawName, StringComparer.OrdinalIgnoreCase))
        {
          hiddenNames.Add(request.RawName);
        }
      }

      await _configurationManager.SetValueAsync(storeId, "AudioOutput:DeviceDisplay:HiddenDeviceNames", hiddenNames, cancellationToken);
      await _configurationManager.SetValueAsync(storeId, "AudioOutput:DeviceDisplay:VisibleDeviceNames", visibleNames, cancellationToken);

      // Reload display settings with freshly-persisted data (IOptionsMonitor may be stale)
      await ReloadDisplaySettingsFromStoreAsync(storeId, cancellationToken);

      _logger.LogInformation("Device visibility set: {RawName} = {Visible}", request.RawName, request.Visible);
      return Ok(new { message = "Device visibility updated", rawName = request.RawName, visible = request.Visible });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting device visibility");
      return StatusCode(500, new { error = "Failed to set device visibility" });
    }
  }

  /// <summary>
  /// Sets a friendly name for a specific device by raw name.
  /// </summary>
  [HttpPut("display/name")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> SetDeviceFriendlyName(
    [FromBody] SetDeviceFriendlyNameRequest request,
    CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(request.RawName))
    {
      return BadRequest(new { error = "RawName is required" });
    }

    if (string.IsNullOrWhiteSpace(request.FriendlyName))
    {
      return BadRequest(new { error = "FriendlyName is required" });
    }

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";

      var friendlyNames = await _configurationManager.GetValueAsync<Dictionary<string, string>>(
        storeId, "AudioOutput:DeviceDisplay:DeviceFriendlyNames", ct: cancellationToken)
        ?? new Dictionary<string, string>();

      friendlyNames[request.RawName] = request.FriendlyName;

      await _configurationManager.SetValueAsync(storeId, "AudioOutput:DeviceDisplay:DeviceFriendlyNames", friendlyNames, cancellationToken);

      await ReloadDisplaySettingsFromStoreAsync(storeId, cancellationToken);

      _logger.LogInformation("Device friendly name set: {RawName} = {FriendlyName}", request.RawName, request.FriendlyName);
      return Ok(new { message = "Device friendly name updated", rawName = request.RawName, friendlyName = request.FriendlyName });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error setting device friendly name");
      return StatusCode(500, new { error = "Failed to set device friendly name" });
    }
  }

  /// <summary>
  /// Clears a friendly name override for a specific device.
  /// </summary>
  [HttpDelete("display/name")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<IActionResult> ClearDeviceFriendlyName(
    [FromQuery] string rawName,
    CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(rawName))
    {
      return BadRequest(new { error = "rawName query parameter is required" });
    }

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";

      var friendlyNames = await _configurationManager.GetValueAsync<Dictionary<string, string>>(
        storeId, "AudioOutput:DeviceDisplay:DeviceFriendlyNames", ct: cancellationToken)
        ?? new Dictionary<string, string>();

      friendlyNames.Remove(rawName);

      await _configurationManager.SetValueAsync(storeId, "AudioOutput:DeviceDisplay:DeviceFriendlyNames", friendlyNames, cancellationToken);

      await ReloadDisplaySettingsFromStoreAsync(storeId, cancellationToken);

      _logger.LogInformation("Device friendly name cleared: {RawName}", rawName);
      return Ok(new { message = "Device friendly name cleared", rawName });
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error clearing device friendly name");
      return StatusCode(500, new { error = "Failed to clear device friendly name" });
    }
  }

  /// <summary>
  /// Reads display settings directly from the config store and passes them to SoundFlowDeviceManager.
  /// Bypasses IOptionsMonitor which may not yet reflect the just-persisted changes.
  /// </summary>
  private async Task ReloadDisplaySettingsFromStoreAsync(string storeId, CancellationToken cancellationToken)
  {
    if (_deviceManager is not SoundFlowDeviceManager sfDeviceManager)
    {
      return;
    }

    var hiddenNames = await _configurationManager.GetValueAsync<List<string>>(
      storeId, "AudioOutput:DeviceDisplay:HiddenDeviceNames", ct: cancellationToken) ?? [];
    var visibleNames = await _configurationManager.GetValueAsync<List<string>>(
      storeId, "AudioOutput:DeviceDisplay:VisibleDeviceNames", ct: cancellationToken) ?? [];
    var friendlyNames = await _configurationManager.GetValueAsync<Dictionary<string, string>>(
      storeId, "AudioOutput:DeviceDisplay:DeviceFriendlyNames", ct: cancellationToken) ?? new();
    var hiddenPatterns = await _configurationManager.GetValueAsync<List<string>>(
      storeId, "AudioOutput:DeviceDisplay:HiddenDevicePatterns", ct: cancellationToken);

    var options = new DeviceDisplayOptions
    {
      HiddenDeviceNames = hiddenNames,
      VisibleDeviceNames = visibleNames,
      DeviceFriendlyNames = friendlyNames,
    };

    // Preserve hidden patterns from existing config if not overridden in store
    if (hiddenPatterns != null)
    {
      options.HiddenDevicePatterns = hiddenPatterns;
    }

    sfDeviceManager.ReloadDisplaySettings(options);
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
  /// Persists the default Cast device ID and name to the configuration store.
  /// </summary>
  private async Task SaveDefaultCastDeviceAsync(string deviceId, string deviceName)
  {
    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      await _configurationManager.SetValueAsync(storeId, "AudioPreferences:DefaultCastDeviceId", deviceId);
      await _configurationManager.SetValueAsync(storeId, "AudioPreferences:DefaultCastDeviceName", deviceName);
      _logger.LogInformation("Saved default Cast device: {Name} ({Id})", deviceName, deviceId);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to persist default Cast device preference");
    }
  }

  /// <summary>
  /// Attempts to auto-connect to the saved default Cast device from cache.
  /// Runs as fire-and-forget so it doesn't block the SetOutputDevice response.
  /// </summary>
  private Task TryAutoConnectDefaultCastDeviceAsync()
  {
    var prefs = _audioPreferences.CurrentValue;
    if (string.IsNullOrEmpty(prefs.DefaultCastDeviceId) || _castOutput == null)
    {
      return Task.CompletedTask;
    }

    _logger.LogInformation("Auto-connecting to default Cast device: {Name} ({Id})",
      prefs.DefaultCastDeviceName, prefs.DefaultCastDeviceId);

    // Fire-and-forget — don't block the output switch response
    _ = Task.Run(async () =>
    {
      try
      {
        var cached = await _castOutput.GetCachedDevicesAsync();
        var device = cached.FirstOrDefault(d => d.Id == prefs.DefaultCastDeviceId);
        if (device == null)
        {
          _logger.LogWarning("Default Cast device {Id} not found in cache, skipping auto-connect",
            prefs.DefaultCastDeviceId);
          return;
        }

        // Ensure Cast output is initialized
        if (_castOutput.State == AudioOutputState.Created)
        {
          await _castOutput.InitializeAsync();
        }

        // Push current now-playing metadata before connecting
        PushCurrentMetadataToCast();

        await _castOutput.ConnectAsync(device);

        // Wire audio source based on streaming mode
        var autoConnectOptions = _audioOutputOptions.Value.GoogleCast;
        var autoConnectDirectChannel = string.Equals(
          autoConnectOptions.StreamingMode, "DirectChannel", StringComparison.OrdinalIgnoreCase);

        if (autoConnectDirectChannel)
        {
          // DirectChannel mode — wire audio engine instead of HTTP stream
          if (_audioEngine != null)
          {
            _castOutput.SetAudioEngine(_audioEngine);
          }
        }
        else if (_httpOutput != null)
        {
          // HttpMp3 mode — wire the HTTP audio stream
          if (_httpOutput.State == AudioOutputState.Error)
          {
            await _httpOutput.InitializeAsync();
          }

          if (_httpOutput.State == AudioOutputState.Created)
          {
            await _httpOutput.InitializeAsync();
          }

          if (_httpOutput.State == AudioOutputState.Ready || _httpOutput.State == AudioOutputState.Stopped)
          {
            await _httpOutput.StartAsync();
          }

          if (_httpOutput.State == AudioOutputState.Streaming)
          {
            var streamUrl = GetRoutableStreamUrl(_httpOutput.Mp3StreamUrl, _httpOutput.Port, device.IpAddress);
            _castOutput.SetStreamUrl(streamUrl);
          }
        }

        await _castOutput.StartAsync();

        // Mute local speakers — audio pipeline continues for Cast streaming
        _audioEngine?.SetLocalOutputMuted(true);

        _logger.LogInformation("Auto-connected to default Cast device: {Name} (mode: {Mode}, local output muted)",
          device.FriendlyName, autoConnectOptions.StreamingMode);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to auto-connect to default Cast device");
      }
    });

    return Task.CompletedTask;
  }

  /// <summary>
  /// Replaces the hostname in a stream URL with the local LAN IP address.
  /// Chromecast devices need a routable IP, not a hostname.
  /// </summary>
  private string GetRoutableStreamUrl(string streamUrl, int port, string? targetDeviceIp = null)
  {
    var localIp = GetLocalIPAddress(targetDeviceIp);
    if (localIp != null)
    {
      var uri = new Uri(streamUrl);
      var routableUrl = $"http://{localIp}:{port}{uri.PathAndQuery}";
      _logger.LogInformation("Resolved routable stream URL: {Url} (target device: {Target})", routableUrl, targetDeviceIp);
      return routableUrl;
    }

    _logger.LogWarning("Could not resolve local LAN IP, using original stream URL");
    return streamUrl;
  }

  /// <summary>
  /// Gets the local LAN IP address that can reach the target device.
  /// Prefers an IP on the same subnet as the target. Filters out virtual adapters (Hyper-V, WSL, Docker).
  /// </summary>
  private string? GetLocalIPAddress(string? targetDeviceIp = null)
  {
    IPAddress? targetIp = null;
    if (!string.IsNullOrEmpty(targetDeviceIp))
    {
      IPAddress.TryParse(targetDeviceIp, out targetIp);
    }

    string? fallbackIp = null;

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

      // Skip virtual adapters (Hyper-V, WSL, Docker, VPN tunnels)
      var desc = ni.Description.ToLowerInvariant();
      var name = ni.Name.ToLowerInvariant();
      if (desc.Contains("hyper-v") || desc.Contains("virtual") ||
          name.Contains("vethernet") || name.Contains("wsl") ||
          desc.Contains("docker") || desc.Contains("vmware") ||
          desc.Contains("virtualbox"))
      {
        continue;
      }

      var props = ni.GetIPProperties();
      foreach (var addr in props.UnicastAddresses)
      {
        if (addr.Address.AddressFamily != AddressFamily.InterNetwork ||
            IPAddress.IsLoopback(addr.Address))
        {
          continue;
        }

        // If we have a target IP, check if this interface is on the same subnet
        if (targetIp != null && addr.IPv4Mask != null)
        {
          var localBytes = addr.Address.GetAddressBytes();
          var maskBytes = addr.IPv4Mask.GetAddressBytes();
          var targetBytes = targetIp.GetAddressBytes();

          bool sameSubnet = true;
          for (int i = 0; i < 4; i++)
          {
            if ((localBytes[i] & maskBytes[i]) != (targetBytes[i] & maskBytes[i]))
            {
              sameSubnet = false;
              break;
            }
          }

          if (sameSubnet)
          {
            _logger.LogDebug("Found same-subnet IP {Ip} on {Interface} for target {Target}",
              addr.Address, ni.Name, targetDeviceIp);
            return addr.Address.ToString();
          }
        }

        // Track first non-virtual IP as fallback
        fallbackIp ??= addr.Address.ToString();
      }
    }

    if (fallbackIp != null)
    {
      _logger.LogDebug("No same-subnet match found, using fallback IP {Ip}", fallbackIp);
    }

    return fallbackIp;
  }

  /// <summary>
  /// Reads current now-playing metadata from the active audio source and
  /// pushes it to the Cast output so it's available for media loading.
  /// </summary>
  private void PushCurrentMetadataToCast()
  {
    if (_castOutput == null || _audioManager?.ActiveSource is not IPrimaryAudioSource primary)
    {
      return;
    }

    var metadata = primary.Metadata;
    if (metadata == null || metadata.Count == 0)
    {
      return;
    }

    var title = metadata.TryGetValue(StandardMetadataKeys.Title, out var t) ? t as string : null;
    var artist = metadata.TryGetValue(StandardMetadataKeys.Artist, out var a) ? a as string : null;
    var album = metadata.TryGetValue(StandardMetadataKeys.Album, out var al) ? al as string : null;
    var albumArtUrl = metadata.TryGetValue(StandardMetadataKeys.AlbumArtUrl, out var art) ? art as string : null;

    _castOutput.SetNowPlayingMetadata(title, artist, album, albumArtUrl);
  }

  private static AudioDeviceDto MapToDeviceDto(
    AudioDeviceInfo device, bool isActive = false, bool? isUserDefault = null)
  {
    return new AudioDeviceDto
    {
      Id = device.Id,
      Name = device.Name,
      Type = device.Type.ToString(),
      IsDefault = isUserDefault ?? device.IsDefault,
      IsActive = isActive,
      IsUSBDevice = device.IsUSBDevice,
      USBPort = device.USBPort,
      MaxChannels = device.MaxChannels,
      SupportedSampleRates = device.SupportedSampleRates
    };
  }
}
