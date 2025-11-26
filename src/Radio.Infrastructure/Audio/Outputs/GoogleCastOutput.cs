using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Sharpcaster;
using Sharpcaster.Interfaces;
using Sharpcaster.Models;
using Sharpcaster.Models.Media;

namespace Radio.Infrastructure.Audio.Outputs;

/// <summary>
/// Google Chromecast audio output implementation using SharpCaster.
/// Streams audio to Chromecast devices via HTTP stream endpoint.
/// </summary>
public class GoogleCastOutput : IAudioOutput
{
  private readonly ILogger<GoogleCastOutput> _logger;
  private readonly GoogleCastOutputOptions _options;
  private readonly object _stateLock = new();

  private AudioOutputState _state = AudioOutputState.Created;
  private float _volume;
  private bool _isMuted;
  private bool _isEnabled;
  private bool _disposed;
  private ChromecastClient? _client;
  private ChromecastReceiver? _connectedReceiver;
  private string? _streamUrl;

  /// <inheritdoc />
  public string Id { get; }

  /// <inheritdoc />
  public string Name { get; private set; }

  /// <inheritdoc />
  public AudioOutputType Type => AudioOutputType.GoogleCast;

  /// <inheritdoc />
  public AudioOutputState State
  {
    get
    {
      lock (_stateLock)
      {
        return _state;
      }
    }
    private set
    {
      AudioOutputState previousState;
      lock (_stateLock)
      {
        previousState = _state;
        _state = value;
      }

      if (previousState != value)
      {
        _logger.LogInformation(
          "Google Cast output state changed from {PreviousState} to {NewState}",
          previousState, value);

        StateChanged?.Invoke(this, new AudioOutputStateChangedEventArgs
        {
          PreviousState = previousState,
          NewState = value,
          OutputId = Id
        });
      }
    }
  }

  /// <inheritdoc />
  public float Volume
  {
    get => _volume;
    set
    {
      var clamped = Math.Clamp(value, 0f, 1f);
      if (Math.Abs(_volume - clamped) > 0.0001f)
      {
        _volume = clamped;
        _logger.LogDebug("Google Cast output volume set to {Volume:P0}", _volume);

        // Apply volume to connected device if available
        _ = SetCastVolumeAsync(_volume);
      }
    }
  }

  /// <inheritdoc />
  public bool IsMuted
  {
    get => _isMuted;
    set
    {
      if (_isMuted != value)
      {
        _isMuted = value;
        _logger.LogDebug("Google Cast output mute set to {IsMuted}", _isMuted);

        // Apply mute state to connected device if available
        _ = SetCastMuteAsync(_isMuted);
      }
    }
  }

  /// <inheritdoc />
  public bool IsEnabled => _isEnabled;

  /// <inheritdoc />
  public event EventHandler<AudioOutputStateChangedEventArgs>? StateChanged;

  /// <summary>
  /// Event raised when a Chromecast device is discovered.
  /// </summary>
  public event EventHandler<ChromecastDeviceDiscoveredEventArgs>? DeviceDiscovered;

  /// <summary>
  /// Event raised when connected to a Chromecast device.
  /// </summary>
  public event EventHandler<ChromecastConnectedEventArgs>? Connected;

  /// <summary>
  /// Event raised when disconnected from a Chromecast device.
  /// </summary>
  public event EventHandler<ChromecastDisconnectedEventArgs>? Disconnected;

  /// <summary>
  /// Gets the currently connected Chromecast device information.
  /// </summary>
  public ChromecastDeviceInfo? ConnectedDevice { get; private set; }

  /// <summary>
  /// Initializes a new instance of the <see cref="GoogleCastOutput"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="options">The Google Cast output options.</param>
  public GoogleCastOutput(
    ILogger<GoogleCastOutput> logger,
    IOptions<AudioOutputOptions> options)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _options = options?.Value?.GoogleCast ?? throw new ArgumentNullException(nameof(options));

    Id = $"cast-output-{Guid.NewGuid():N}";
    Name = "Google Cast Output";
    _volume = _options.DefaultVolume;
    _isEnabled = _options.Enabled;
  }

  /// <inheritdoc />
  public Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (State != AudioOutputState.Created && State != AudioOutputState.Error)
    {
      throw new InvalidOperationException(
        $"Cannot initialize output in state {State}. Output must be in Created or Error state.");
    }

    State = AudioOutputState.Initializing;

    try
    {
      _logger.LogInformation("Initializing Google Cast output");

      // Create the ChromecastClient
      _client = new ChromecastClient();

      State = AudioOutputState.Ready;
      _logger.LogInformation("Google Cast output initialized");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize Google Cast output");
      State = AudioOutputState.Error;
      throw;
    }

    return Task.CompletedTask;
  }

  /// <summary>
  /// Discovers available Chromecast devices on the network.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A list of discovered Chromecast devices.</returns>
  public async Task<IReadOnlyList<ChromecastDeviceInfo>> DiscoverDevicesAsync(
    CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    _logger.LogInformation(
      "Starting Chromecast device discovery (timeout: {Timeout}s)",
      _options.DiscoveryTimeoutSeconds);

    var devices = new List<ChromecastDeviceInfo>();

    try
    {
      IChromecastLocator locator = new MdnsChromecastLocator();
      var discoveredDevices = await locator.FindReceiversAsync(cancellationToken);

      foreach (var device in discoveredDevices)
      {
        var deviceInfo = new ChromecastDeviceInfo
        {
          Id = device.DeviceUri.ToString(),
          FriendlyName = device.Name,
          IpAddress = device.DeviceUri.Host,
          Port = device.DeviceUri.Port,
          Model = device.Model ?? "Unknown"
        };

        devices.Add(deviceInfo);
        DeviceDiscovered?.Invoke(this, new ChromecastDeviceDiscoveredEventArgs { Device = deviceInfo });

        _logger.LogDebug(
          "Discovered Chromecast: {Name} at {IP}:{Port}",
          deviceInfo.FriendlyName, deviceInfo.IpAddress, deviceInfo.Port);
      }

      _logger.LogInformation("Discovered {Count} Chromecast device(s)", devices.Count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error during Chromecast device discovery");
    }

    return devices;
  }

  /// <summary>
  /// Connects to a specific Chromecast device.
  /// </summary>
  /// <param name="device">The device to connect to.</param>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task ConnectAsync(ChromecastDeviceInfo device, CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(device);

    if (State != AudioOutputState.Ready && State != AudioOutputState.Stopped)
    {
      throw new InvalidOperationException(
        $"Cannot connect in state {State}. Output must be in Ready or Stopped state.");
    }

    State = AudioOutputState.Connecting;

    try
    {
      _logger.LogInformation(
        "Connecting to Chromecast: {Name} at {IP}:{Port}",
        device.FriendlyName, device.IpAddress, device.Port);

      // Create ChromecastReceiver from device info
      var uri = new Uri($"https://{device.IpAddress}:{device.Port}");
      _connectedReceiver = new ChromecastReceiver
      {
        DeviceUri = uri,
        Name = device.FriendlyName,
        Model = device.Model
      };

      if (_client == null)
      {
        throw new InvalidOperationException("Client not initialized. Call InitializeAsync first.");
      }

      await _client.ConnectChromecast(_connectedReceiver);

      ConnectedDevice = device;
      Name = $"Cast: {device.FriendlyName}";

      Connected?.Invoke(this, new ChromecastConnectedEventArgs { Device = device });

      _logger.LogInformation(
        "Connected to Chromecast: {Name}",
        device.FriendlyName);

      State = AudioOutputState.Ready;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to connect to Chromecast: {Name}", device.FriendlyName);
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <summary>
  /// Disconnects from the currently connected Chromecast device.
  /// </summary>
  /// <param name="cancellationToken">Cancellation token.</param>
  /// <returns>A task representing the async operation.</returns>
  public async Task DisconnectAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (_connectedReceiver == null)
    {
      _logger.LogWarning("No Chromecast device connected");
      return;
    }

    try
    {
      _logger.LogInformation("Disconnecting from Chromecast: {Name}", ConnectedDevice?.FriendlyName);

      if (_client != null)
      {
        await _client.DisconnectAsync();
      }

      var disconnectedDevice = ConnectedDevice;
      _connectedReceiver = null;
      ConnectedDevice = null;
      Name = "Google Cast Output";

      Disconnected?.Invoke(this, new ChromecastDisconnectedEventArgs
      {
        Device = disconnectedDevice,
        Reason = "User requested disconnect"
      });

      State = AudioOutputState.Ready;
      _logger.LogInformation("Disconnected from Chromecast");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error disconnecting from Chromecast");
      throw;
    }
  }

  /// <inheritdoc />
  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (State != AudioOutputState.Ready && State != AudioOutputState.Stopped)
    {
      throw new InvalidOperationException(
        $"Cannot start output in state {State}. Output must be in Ready or Stopped state.");
    }

    if (_connectedReceiver == null)
    {
      throw new InvalidOperationException("No Chromecast device connected. Connect to a device first.");
    }

    try
    {
      _logger.LogInformation("Starting Google Cast output");

      // Start streaming to the connected Chromecast
      // Launch the default media receiver application
      if (_client != null)
      {
        // Launch default media receiver
        await _client.LaunchApplicationAsync("CC1AD845");

        // Load media if we have a stream URL
        if (!string.IsNullOrEmpty(_streamUrl))
        {
          var media = new Media
          {
            ContentUrl = _streamUrl,
            ContentType = "audio/wav",
            StreamType = StreamType.Live
          };

          var mediaChannel = _client.GetChannel<IMediaChannel>();
          if (mediaChannel != null)
          {
            await mediaChannel.LoadAsync(media);
          }
        }
      }

      _isEnabled = true;
      State = AudioOutputState.Streaming;

      _logger.LogInformation("Google Cast output started streaming to {Name}", ConnectedDevice?.FriendlyName);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start Google Cast output");
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <inheritdoc />
  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    if (State != AudioOutputState.Streaming)
    {
      _logger.LogWarning("Stop requested but output is not streaming (state: {State})", State);
      return;
    }

    try
    {
      State = AudioOutputState.Stopping;
      _logger.LogInformation("Stopping Google Cast output");

      // Stop media playback on the Chromecast
      if (_client != null)
      {
        var mediaChannel = _client.GetChannel<IMediaChannel>();
        if (mediaChannel != null)
        {
          await mediaChannel.StopAsync();
        }
      }

      _isEnabled = false;
      State = AudioOutputState.Stopped;

      _logger.LogInformation("Google Cast output stopped");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to stop Google Cast output");
      State = AudioOutputState.Error;
      throw;
    }
  }

  /// <summary>
  /// Sets the stream URL that will be used when streaming to Chromecast.
  /// The Chromecast will connect to this URL to receive the audio stream.
  /// </summary>
  /// <param name="streamUrl">The HTTP stream URL.</param>
  public void SetStreamUrl(string streamUrl)
  {
    _streamUrl = streamUrl;
    _logger.LogDebug("Stream URL set to: {Url}", streamUrl);
  }

  private async Task SetCastVolumeAsync(float volume)
  {
    if (_client == null || _connectedReceiver == null || State != AudioOutputState.Streaming)
    {
      return;
    }

    try
    {
      var receiverChannel = _client.GetChannel<IReceiverChannel>();
      if (receiverChannel != null)
      {
        await receiverChannel.SetVolume(volume);
        _logger.LogDebug("Chromecast volume set to {Volume:P0}", volume);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to set Chromecast volume");
    }
  }

  private async Task SetCastMuteAsync(bool mute)
  {
    if (_client == null || _connectedReceiver == null || State != AudioOutputState.Streaming)
    {
      return;
    }

    try
    {
      var receiverChannel = _client.GetChannel<IReceiverChannel>();
      if (receiverChannel != null)
      {
        await receiverChannel.SetMute(mute);
        _logger.LogDebug("Chromecast mute set to {Mute}", mute);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to set Chromecast mute state");
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
  }

  /// <inheritdoc />
  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _isEnabled = false;

    if (_connectedReceiver != null)
    {
      try
      {
        await DisconnectAsync();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error during disconnect in dispose");
      }
    }

    if (_client != null)
    {
      await _client.DisconnectAsync();
    }

    State = AudioOutputState.Disposed;
    _logger.LogInformation("Google Cast output disposed");
  }
}

/// <summary>
/// Information about a discovered Chromecast device.
/// </summary>
public record ChromecastDeviceInfo
{
  /// <summary>
  /// Gets or sets the unique identifier for the device.
  /// </summary>
  public required string Id { get; init; }

  /// <summary>
  /// Gets or sets the friendly name of the device.
  /// </summary>
  public required string FriendlyName { get; init; }

  /// <summary>
  /// Gets or sets the IP address of the device.
  /// </summary>
  public required string IpAddress { get; init; }

  /// <summary>
  /// Gets or sets the port number.
  /// </summary>
  public required int Port { get; init; }

  /// <summary>
  /// Gets or sets the device model.
  /// </summary>
  public required string Model { get; init; }
}

/// <summary>
/// Event arguments for Chromecast device discovery.
/// </summary>
public class ChromecastDeviceDiscoveredEventArgs : EventArgs
{
  /// <summary>
  /// Gets the discovered device.
  /// </summary>
  public required ChromecastDeviceInfo Device { get; init; }
}

/// <summary>
/// Event arguments for Chromecast connection.
/// </summary>
public class ChromecastConnectedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the connected device.
  /// </summary>
  public required ChromecastDeviceInfo Device { get; init; }
}

/// <summary>
/// Event arguments for Chromecast disconnection.
/// </summary>
public class ChromecastDisconnectedEventArgs : EventArgs
{
  /// <summary>
  /// Gets the disconnected device.
  /// </summary>
  public ChromecastDeviceInfo? Device { get; init; }

  /// <summary>
  /// Gets the reason for disconnection.
  /// </summary>
  public string? Reason { get; init; }
}
