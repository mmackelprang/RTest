using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace Radio.Infrastructure.Platform.Bluetooth.Windows;

/// <summary>
/// Manages Windows AudioPlaybackConnection to enable A2DP sink (receive audio from phone).
/// Requires Windows 10 2004+ (build 19041) and MSIX sparse package identity.
/// Audio routes to system default output — no SoundFlow involvement on Windows.
/// </summary>
/// <remarks>
/// Three connection strategies (tried in order of speed):
/// 1. Direct address (fastest): when InTheHand detects a phone MAC address, resolves it via
///    BluetoothDevice.FromBluetoothAddressAsync → AudioPlaybackConnection.TryCreateFromId.
///    Bypasses DeviceWatcher/FindAllAsync which may return 0 devices for some phones.
/// 2. DeviceWatcher (reactive): watches for AudioPlaybackConnection devices appearing in WinRT.
/// 3. Proactive enumeration: enumerates AudioPlaybackConnection devices and opens the first match.
/// </remarks>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WindowsA2dpSinkManager : IDisposable
{
  private readonly ILogger _logger;
  private DeviceWatcher? _deviceWatcher;
  private AudioPlaybackConnection? _activeConnection;
  private string? _activeDeviceId;
  private string? _deviceSelector;
  private bool _disposed;
  private bool _isConnecting;

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
  /// Also proactively enumerates any already-paired devices.
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

    // Check MSIX sparse package identity — required for AudioPlaybackConnection
    CheckPackageIdentity();

    try
    {
      _deviceSelector = AudioPlaybackConnection.GetDeviceSelector();
      _logger.LogInformation("A2DP device selector: {Selector}", _deviceSelector);

      _deviceWatcher = DeviceInformation.CreateWatcher(_deviceSelector);

      _deviceWatcher.Added += OnDeviceAdded;
      _deviceWatcher.Updated += OnDeviceUpdated;
      _deviceWatcher.Removed += OnDeviceRemoved;
      _deviceWatcher.EnumerationCompleted += (_, _) =>
        _logger.LogDebug("A2DP device enumeration completed");
      _deviceWatcher.Stopped += (_, _) =>
        _logger.LogDebug("A2DP device watcher stopped");

      _deviceWatcher.Start();
      _logger.LogInformation("A2DP sink device watcher started");

      // Proactively enumerate already-paired devices in the background
      _ = EnumerateAndConnectExistingDevicesAsync();
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to start A2DP sink device watcher");
    }
  }

  /// <summary>
  /// Proactively enumerate AudioPlaybackConnection devices and connect to the first available.
  /// Called at startup and when InTheHand detects a new Bluetooth connection.
  /// </summary>
  public async Task EnumerateAndConnectExistingDevicesAsync()
  {
    if (_activeConnection != null)
    {
      _logger.LogDebug("A2DP connection already active, skipping enumeration");
      return;
    }

    if (_deviceSelector == null)
    {
      _logger.LogDebug("A2DP device selector not available, skipping enumeration");
      return;
    }

    try
    {
      _logger.LogInformation("Enumerating AudioPlaybackConnection devices...");
      var devices = await DeviceInformation.FindAllAsync(_deviceSelector);
      _logger.LogInformation("Found {Count} AudioPlaybackConnection device(s)", devices.Count);

      foreach (var device in devices)
      {
        _logger.LogInformation("  AudioPlaybackConnection device: {Name} ({Id})", device.Name, device.Id);

        if (_activeConnection == null)
        {
          await OpenConnectionAsync(device.Id);
        }
      }

      if (devices.Count == 0)
      {
        _logger.LogInformation("No AudioPlaybackConnection devices found — " +
          "this is expected if the process lacks MSIX package identity. " +
          "Run Radio.API.exe directly (not via 'dotnet run').");
        await LogBluetoothDeviceDiagnosticsAsync();
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to enumerate AudioPlaybackConnection devices");
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

  /// <summary>
  /// Attempt to establish an A2DP sink connection by resolving a Bluetooth MAC address
  /// directly to a WinRT BluetoothDevice and creating an AudioPlaybackConnection from it.
  /// This bypasses DeviceWatcher/FindAllAsync which may return 0 devices for some phones.
  /// </summary>
  /// <param name="bluetoothAddress">The 48-bit MAC address as a ulong (e.g. from InTheHand BluetoothAddress).</param>
  public async Task TryConnectByAddressAsync(ulong bluetoothAddress)
  {
    if (_activeConnection != null)
    {
      _logger.LogDebug("A2DP connection already active, skipping direct connect for {Address:X12}", bluetoothAddress);
      return;
    }

    if (_isConnecting)
    {
      _logger.LogDebug("A2DP connection attempt already in progress, skipping direct connect for {Address:X12}", bluetoothAddress);
      return;
    }

    try
    {
      _logger.LogInformation(
        "Resolving Bluetooth address {Address:X12} to WinRT BluetoothDevice for direct A2DP connection...",
        bluetoothAddress);

      var btDevice = await BluetoothDevice.FromBluetoothAddressAsync(bluetoothAddress);
      if (btDevice == null)
      {
        _logger.LogWarning(
          "BluetoothDevice.FromBluetoothAddressAsync returned null for {Address:X12} — " +
          "device may not be paired or in range", bluetoothAddress);
        return;
      }

      _logger.LogInformation(
        "Resolved BT address {Address:X12} → WinRT device: {Name} (DeviceId: {DeviceId})",
        bluetoothAddress, btDevice.Name, btDevice.DeviceId);

      // Log the device's services for diagnostics
      var rfcommServices = await btDevice.GetRfcommServicesAsync();
      _logger.LogInformation(
        "Device {Name} has {Count} RFCOMM service(s)",
        btDevice.Name, rfcommServices.Services.Count);

      // Try to create AudioPlaybackConnection from the WinRT device ID
      await OpenConnectionAsync(btDevice.DeviceId);

      // If direct DeviceId didn't work, try constructing alternate ID formats.
      // AudioPlaybackConnection may expect a DeviceInformation.Id from the
      // AudioPlaybackConnection device selector, not the BluetoothDevice.DeviceId.
      if (_activeConnection == null)
      {
        _logger.LogInformation(
          "Direct DeviceId didn't work for A2DP — trying DeviceInformation lookup for {Name}...",
          btDevice.Name);

        // Get the DeviceInformation for this Bluetooth device
        var devInfo = await DeviceInformation.CreateFromIdAsync(btDevice.DeviceId);
        if (devInfo != null)
        {
          _logger.LogInformation(
            "DeviceInformation for {Name}: Id={Id}, Kind={Kind}, IsEnabled={IsEnabled}",
            devInfo.Name, devInfo.Id, devInfo.Kind, devInfo.IsEnabled);
        }

        // Also try looking up the device in the AudioPlaybackConnection selector
        // by checking if any matching device can be found by name/address
        if (_deviceSelector != null)
        {
          var apcDevices = await DeviceInformation.FindAllAsync(_deviceSelector);
          _logger.LogInformation(
            "Re-checking AudioPlaybackConnection devices after direct resolve: {Count} found",
            apcDevices.Count);
          foreach (var apcDev in apcDevices)
          {
            _logger.LogInformation("  APC device: {Name} ({Id})", apcDev.Name, apcDev.Id);
            if (_activeConnection == null)
            {
              await OpenConnectionAsync(apcDev.Id);
            }
          }
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex,
        "Failed to connect A2DP by direct address {Address:X12}", bluetoothAddress);
    }
  }

  private async void OnDeviceAdded(DeviceWatcher sender, DeviceInformation device)
  {
    _logger.LogInformation("A2DP device added (watcher): {Name} ({Id})", device.Name, device.Id);
    await OpenConnectionAsync(device.Id);
  }

  private async void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
  {
    _logger.LogDebug("A2DP device updated (watcher): {Id}", update.Id);
    // If we don't have an active connection, try to open one
    if (_activeConnection == null)
    {
      await OpenConnectionAsync(update.Id);
    }
  }

  private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
  {
    _logger.LogInformation("A2DP device removed (watcher): {Id}", update.Id);
    if (_activeDeviceId == update.Id)
    {
      CloseConnection();
    }
  }

  private async Task OpenConnectionAsync(string deviceId)
  {
    // Prevent concurrent connection attempts
    if (_isConnecting)
    {
      _logger.LogDebug("A2DP connection attempt already in progress, skipping {DeviceId}", deviceId);
      return;
    }

    if (_activeConnection != null)
    {
      _logger.LogDebug("A2DP connection already active, skipping {DeviceId}", deviceId);
      return;
    }

    _isConnecting = true;
    try
    {
      _logger.LogInformation("Opening A2DP connection for {DeviceId}...", deviceId);

      var connection = AudioPlaybackConnection.TryCreateFromId(deviceId);
      if (connection == null)
      {
        _logger.LogWarning("AudioPlaybackConnection.TryCreateFromId returned null for {DeviceId} — " +
          "this usually means the MSIX sparse package identity is not registered " +
          "or the device doesn't support AudioPlaybackConnection", deviceId);
        return;
      }

      connection.StateChanged += OnConnectionStateChanged;

      // StartAsync activates the A2DP sink profile on this endpoint
      _logger.LogDebug("Calling AudioPlaybackConnection.StartAsync for {DeviceId}...", deviceId);
      await connection.StartAsync();
      _logger.LogInformation("A2DP profile activated (StartAsync) for {DeviceId}", deviceId);

      // OpenAsync opens the audio channel — audio will route to default speakers
      _logger.LogDebug("Calling AudioPlaybackConnection.OpenAsync for {DeviceId}...", deviceId);
      var result = await connection.OpenAsync();
      _logger.LogInformation("A2DP connection opened for {DeviceId}, status: {Status}", deviceId, result.Status);

      if (result.Status == AudioPlaybackConnectionOpenResultStatus.Success)
      {
        // Keep the connection alive — GC collection would terminate it
        _activeConnection = connection;
        _activeDeviceId = deviceId;
        A2dpConnected?.Invoke(this, deviceId);
        _logger.LogInformation("A2DP audio channel established — phone audio will play through default speakers");
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
    finally
    {
      _isConnecting = false;
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

  /// <summary>
  /// Checks whether the process has MSIX sparse package identity, which is required
  /// for AudioPlaybackConnection to function. Without identity, the API returns
  /// 0 devices and A2DP sink connections cannot be established.
  /// </summary>
  private void CheckPackageIdentity()
  {
    try
    {
      var package = global::Windows.ApplicationModel.Package.Current;
      _logger.LogInformation(
        "MSIX package identity active: {Name} v{Major}.{Minor}.{Build}.{Revision}",
        package.Id.Name,
        package.Id.Version.Major,
        package.Id.Version.Minor,
        package.Id.Version.Build,
        package.Id.Version.Revision);
    }
    catch (InvalidOperationException)
    {
      _logger.LogWarning(
        "NO MSIX package identity — AudioPlaybackConnection will NOT work. " +
        "A2DP sink requires package identity. Either:\n" +
        "  1. Run Radio.API.exe directly from the registered ExternalLocation " +
        "(e.g., src/Radio.API/bin/Release/net8.0-windows10.0.19041.0/Radio.API.exe), OR\n" +
        "  2. Re-register the sparse package: run packaging/register-identity.ps1 as Administrator\n" +
        "Note: 'dotnet run' uses dotnet.exe which does NOT inherit the sparse package identity.");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to check MSIX package identity");
    }
  }

  /// <summary>
  /// Enumerates all paired Bluetooth devices (not just AudioPlaybackConnection ones)
  /// to help diagnose why a phone might not appear as an A2DP source.
  /// </summary>
  private async Task LogBluetoothDeviceDiagnosticsAsync()
  {
    try
    {
      // Enumerate ALL paired Bluetooth devices for comparison
      var btSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
      var allBtDevices = await DeviceInformation.FindAllAsync(btSelector);
      _logger.LogInformation("Paired Bluetooth devices (all types): {Count}", allBtDevices.Count);
      foreach (var dev in allBtDevices)
      {
        _logger.LogInformation("  BT device: {Name} ({Id})", dev.Name, dev.Id);
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Failed to enumerate Bluetooth devices for diagnostics");
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
