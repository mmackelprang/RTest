using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace Radio.Infrastructure.Platform.Bluetooth.Windows;

/// <summary>
/// Manages Windows AudioPlaybackConnection to enable A2DP sink (receive audio from phone).
/// Requires Windows 10 2004+ (build 19041) and MSIX sparse package identity.
/// Audio routes to system default output — no SoundFlow involvement on Windows.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WindowsA2dpSinkManager : IDisposable
{
  private readonly ILogger _logger;
  private DeviceWatcher? _deviceWatcher;
  private AudioPlaybackConnection? _activeConnection;
  private string? _activeDeviceId;
  private bool _disposed;

  /// <summary>Fired when A2DP audio connection is established.</summary>
  public event EventHandler<string>? A2dpConnected;

  /// <summary>Fired when A2DP audio connection is lost.</summary>
  public event EventHandler<string>? A2dpDisconnected;

  /// <summary>Whether an A2DP sink connection is currently active.</summary>
  public bool IsConnected => _activeConnection != null;

  public WindowsA2dpSinkManager(ILogger logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Start watching for paired Bluetooth audio devices and auto-open A2DP connections.
  /// </summary>
  public void Start()
  {
    if (!IsWindowsBuildSupported())
    {
      _logger.LogWarning(
        "AudioPlaybackConnection requires Windows 10 build 19041+. Current build: {Build}",
        Environment.OSVersion.Version.Build);
      return;
    }

    try
    {
      var selector = AudioPlaybackConnection.GetDeviceSelector();
      _deviceWatcher = DeviceInformation.CreateWatcher(selector);

      _deviceWatcher.Added += OnDeviceAdded;
      _deviceWatcher.Updated += OnDeviceUpdated;
      _deviceWatcher.Removed += OnDeviceRemoved;
      _deviceWatcher.EnumerationCompleted += (_, _) =>
        _logger.LogDebug("A2DP device enumeration completed");

      _deviceWatcher.Start();
      _logger.LogInformation("A2DP sink device watcher started");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to start A2DP sink device watcher");
    }
  }

  /// <summary>
  /// Attempt to establish an A2DP sink connection to a specific device.
  /// Called when a stable Bluetooth connection is detected.
  /// </summary>
  public async Task TryConnectAsync(string deviceId)
  {
    if (_activeConnection != null)
    {
      _logger.LogDebug("A2DP connection already active, skipping connect for {DeviceId}", deviceId);
      return;
    }

    await OpenConnectionAsync(deviceId);
  }

  private async void OnDeviceAdded(DeviceWatcher sender, DeviceInformation device)
  {
    _logger.LogInformation("A2DP device added: {Name} ({Id})", device.Name, device.Id);
    await OpenConnectionAsync(device.Id);
  }

  private async void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
  {
    _logger.LogDebug("A2DP device updated: {Id}", update.Id);
    // If we don't have an active connection, try to open one
    if (_activeConnection == null)
    {
      await OpenConnectionAsync(update.Id);
    }
  }

  private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
  {
    _logger.LogInformation("A2DP device removed: {Id}", update.Id);
    if (_activeDeviceId == update.Id)
    {
      CloseConnection();
    }
  }

  private async Task OpenConnectionAsync(string deviceId)
  {
    try
    {
      var connection = AudioPlaybackConnection.TryCreateFromId(deviceId);
      if (connection == null)
      {
        _logger.LogDebug("AudioPlaybackConnection.TryCreateFromId returned null for {DeviceId}", deviceId);
        return;
      }

      connection.StateChanged += OnConnectionStateChanged;

      // StartAsync activates the A2DP sink profile
      await connection.StartAsync();
      _logger.LogInformation("A2DP connection started for {DeviceId}", deviceId);

      // OpenAsync opens the audio channel — audio will route to default speakers
      var result = await connection.OpenAsync();
      _logger.LogInformation("A2DP connection opened for {DeviceId}, status: {Status}", deviceId, result.Status);

      if (result.Status == AudioPlaybackConnectionOpenResultStatus.Success)
      {
        // Keep the connection alive — GC collection would terminate it
        _activeConnection = connection;
        _activeDeviceId = deviceId;
        A2dpConnected?.Invoke(this, deviceId);
      }
      else
      {
        _logger.LogWarning("A2DP connection open failed: {Status} (extended: {Extended})",
          result.Status, result.ExtendedError?.Message ?? "none");
        connection.StateChanged -= OnConnectionStateChanged;
        connection.Dispose();
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to open A2DP connection for {DeviceId}", deviceId);
    }
  }

  private void OnConnectionStateChanged(AudioPlaybackConnection sender, object args)
  {
    _logger.LogInformation("A2DP connection state changed: {State} for device {DeviceId}",
      sender.State, _activeDeviceId);

    if (sender.State == AudioPlaybackConnectionState.Closed)
    {
      CloseConnection();
    }
  }

  private void CloseConnection()
  {
    var deviceId = _activeDeviceId;
    if (_activeConnection != null)
    {
      _activeConnection.StateChanged -= OnConnectionStateChanged;
      _activeConnection.Dispose();
      _activeConnection = null;
      _activeDeviceId = null;
      _logger.LogInformation("A2DP connection closed");

      if (deviceId != null)
      {
        A2dpDisconnected?.Invoke(this, deviceId);
      }
    }
  }

  private static bool IsWindowsBuildSupported()
  {
    return Environment.OSVersion.Version.Build >= 19041;
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    if (_deviceWatcher != null)
    {
      try
      {
        _deviceWatcher.Stop();
      }
      catch
      {
        // Best-effort cleanup
      }
      _deviceWatcher = null;
    }

    CloseConnection();
  }
}
