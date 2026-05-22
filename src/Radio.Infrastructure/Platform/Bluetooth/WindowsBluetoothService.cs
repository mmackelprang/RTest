using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Metrics;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Platform.Bluetooth;

internal sealed class WindowsBluetoothService : IBluetoothService
{
    private readonly ILogger _logger;
    private readonly BluetoothOptions _options;
    private readonly Radio.Infrastructure.Audio.SoundFlow.SoundFlowDeviceManager? _deviceManager;
    private readonly IMetricsCollector? _metricsCollector;
    private readonly SoundFlowPlaybackService? _playbackService;
    private readonly AlbumArtCacheService? _albumArtCache;
    private BluetoothClient _client;
    private BluetoothRadio? _radio;
    private readonly Timer _stateTimer;
    private MiniAudioEngine? _captureEngine;
    private DateTime? _connectionStartTime;
    private IntPtr _authCallbackHandle;
    private PfnAuthenticationCallbackEx? _authCallbackDelegate; // prevent GC collection

#if WINDOWS_TARGET
    private Windows.WindowsA2dpSinkManager? _a2dpSinkManager;
    private Windows.WindowsMediaSessionWatcher? _mediaSessionWatcher;
    private Windows.WasapiLoopbackCaptureSource? _loopbackCapture;
    private BufferedSoundGenerator<float>? _loopbackGenerator;
#endif

    // Connection stability: require device to be connected for 2 consecutive polls
    // before firing DeviceConnected. Prevents spurious events from transient connections.
    private string? _pendingConnectionAddress;
    private int _pendingConnectionPolls;
    private const int RequiredStablePolls = 2;

    // Reconnect cooldown: after a disconnect, suppress reconnect events for the same
    // device for a cooldown period to prevent rapid connect/disconnect cycling.
    private string? _lastDisconnectedAddress;
    private DateTime _lastDisconnectTime;
    private static readonly TimeSpan ReconnectCooldown = TimeSpan.FromSeconds(10);

    // InTheHand.Net doesn't have robust event-driven discovery in the same way,
    // often relies on polling or blocking calls. We'll simulate async discovery.
    private CancellationTokenSource? _discoveryCts;

    // ========== Native Bluetooth Authentication P/Invoke ==========
    // Windows Bluetooth API for intercepting pairing requests. Without this,
    // Windows shows a system toast notification that users may miss, causing
    // "incorrect PIN or passkey" errors on the phone side.

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BLUETOOTH_DEVICE_INFO
    {
      public int dwSize;
      public ulong Address;
      public uint ulClassofDevice;
      [MarshalAs(UnmanagedType.Bool)]
      public bool fConnected;
      [MarshalAs(UnmanagedType.Bool)]
      public bool fRemembered;
      [MarshalAs(UnmanagedType.Bool)]
      public bool fAuthenticated;
      public SYSTEMTIME stLastSeen;
      public SYSTEMTIME stLastUsed;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
      public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
      public ushort wYear, wMonth, wDayOfWeek, wDay;
      public ushort wHour, wMinute, wSecond, wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_AUTHENTICATION_CALLBACK_PARAMS
    {
      public BLUETOOTH_DEVICE_INFO deviceInfo;
      public int authenticationMethod;
      public int ioCapability;
      public int authenticationRequirements;
      public uint Numeric_Value_Passkey;
    }

    private delegate bool PfnAuthenticationCallbackEx(
      IntPtr pvParam, ref BLUETOOTH_AUTHENTICATION_CALLBACK_PARAMS pAuthCallbackParams);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    private static extern uint BluetoothRegisterForAuthenticationEx(
      ref BLUETOOTH_DEVICE_INFO pbtdi,
      out IntPtr phRegHandle,
      PfnAuthenticationCallbackEx pfnCallbackEx,
      IntPtr pvParam);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothUnregisterAuthentication(IntPtr hRegHandle);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    private static extern uint BluetoothSendAuthenticationResponseEx(
      IntPtr hRadio,
      ref BLUETOOTH_AUTHENTICATE_RESPONSE pAuthResponse);

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_AUTHENTICATE_RESPONSE
    {
      public ulong bthAddressRemote;
      public int authMethod;

      // Union: we use Numeric_Value_Passkey for SSP confirmation
      public uint Numeric_Value_Passkey;
      [MarshalAs(UnmanagedType.Bool)]
      public bool bNegativeResponse;
    }

    private const int BLUETOOTH_AUTHENTICATION_METHOD_LEGACY = 0x1;
    private const int BLUETOOTH_AUTHENTICATION_METHOD_OOB = 0x2;
    private const int BLUETOOTH_AUTHENTICATION_METHOD_NUMERIC_COMPARISON = 0x3;
    private const int BLUETOOTH_AUTHENTICATION_METHOD_PASSKEY_NOTIFICATION = 0x4;
    private const int BLUETOOTH_AUTHENTICATION_METHOD_PASSKEY = 0x5;

    public WindowsBluetoothService(
        ILogger logger,
        IOptions<BluetoothOptions> options,
        Radio.Infrastructure.Audio.SoundFlow.SoundFlowDeviceManager? deviceManager = null,
        IMetricsCollector? metricsCollector = null,
        SoundFlowPlaybackService? playbackService = null,
        AlbumArtCacheService? albumArtCache = null)
    {
        _logger = logger;
        _options = options.Value;
        _deviceManager = deviceManager;
        _metricsCollector = metricsCollector;
        _playbackService = playbackService;
        _albumArtCache = albumArtCache;
        _client = new BluetoothClient();

        // Try to get the primary radio at construction time
        try
        {
            _radio = BluetoothRadio.Default;
            if (_radio != null)
            {
                _logger.LogInformation("Bluetooth radio found: {Name}, Mode: {Mode}",
                    _radio.Name, _radio.Mode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get primary Bluetooth radio");
        }

        // Poll for state changes
        _stateTimer = new Timer(CheckState, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    public bool IsAvailable => _radio != null;

    public BluetoothAdapterState State { get; private set; } = BluetoothAdapterState.Unknown;

    public IReadOnlyList<Radio.Core.Interfaces.Audio.BluetoothDeviceInfo> PairedDevices
    {
        get
        {
            try
            {
                    return _client.PairedDevices.Select(MapDevice).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get paired devices");
                return Array.Empty<Radio.Core.Interfaces.Audio.BluetoothDeviceInfo>();
            }
        }
    }

    private readonly List<Radio.Core.Interfaces.Audio.BluetoothDeviceInfo> _discoveredDevices = new();
    public IReadOnlyList<Radio.Core.Interfaces.Audio.BluetoothDeviceInfo> DiscoveredDevices
    {
        get
        {
            lock (_discoveredDevices)
            {
                return _discoveredDevices.ToList();
            }
        }
    }

    public bool IsDiscovering { get; private set; }

    public Radio.Core.Interfaces.Audio.BluetoothDeviceInfo? ConnectedDevice { get; private set; }

#if WINDOWS_TARGET
    // Platform manages audio ONLY when A2DP sink is active but loopback capture is disabled.
    // When loopback capture is enabled, audio goes through SoundFlow (we capture it via WASAPI).
    public bool IsAudioManagedByPlatform =>
      _a2dpSinkManager != null && !_options.EnableLoopbackCapture;
#else
    public bool IsAudioManagedByPlatform => false;
#endif

    public event EventHandler<BluetoothAdapterStateChangedEventArgs>? StateChanged;
    public event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected;
    public event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected;
    public event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered;

#if WINDOWS_TARGET
    // Under WINDOWS_TARGET, these events are wired from WindowsMediaSessionWatcher.
    public event EventHandler<BluetoothPlaybackMetadata>? MetadataChanged;
    public event EventHandler<BluetoothPlaybackStatus>? PlaybackStatusChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
#pragma warning disable CS0067 // VolumeChanged will be wired when AVRCP volume monitoring is implemented
    public event EventHandler<BluetoothVolumeChangedEventArgs>? VolumeChanged;
#pragma warning restore CS0067
    public float? DeviceVolume { get; private set; }
#else
    // Under net8.0 (no WinRT), suppress CS0067 — events are never raised on this TFM.
    public event EventHandler<BluetoothPlaybackMetadata>? MetadataChanged { add { } remove { } }
    public event EventHandler<BluetoothPlaybackStatus>? PlaybackStatusChanged { add { } remove { } }
    public event EventHandler<TimeSpan>? PositionChanged { add { } remove { } }
    public event EventHandler<BluetoothVolumeChangedEventArgs>? VolumeChanged { add { } remove { } }
    public float? DeviceVolume => null;
#endif

    public event EventHandler? CaptureStreamRecovered { add { } remove { } }
    public event EventHandler<CaptureStreamStalledEventArgs>? CaptureStreamStalled { add { } remove { } }

    public Task SetDeviceVolumeAsync(float volume)
    {
      // AVRCP absolute volume set is not yet implemented for Windows.
      // Windows maps AVRCP volume to the audio endpoint volume, which is
      // managed by the OS. Setting it programmatically requires identifying
      // the specific Bluetooth audio endpoint via NAudio/CoreAudioApi.
      _logger.LogDebug("SetDeviceVolumeAsync not yet implemented for Windows BT");
      return Task.CompletedTask;
    }

    public Task NextTrackAsync(CancellationToken cancellationToken = default)
    {
      _logger.LogDebug("NextTrackAsync not yet implemented for Windows BT");
      return Task.CompletedTask;
    }

    public Task PreviousTrackAsync(CancellationToken cancellationToken = default)
    {
      _logger.LogDebug("PreviousTrackAsync not yet implemented for Windows BT");
      return Task.CompletedTask;
    }

    public bool IsReconnecting => false;
    public void CancelReconnection() { }
    public BluetoothDisconnectReason? LastDisconnectReason => null;
    public BluetoothPipelineStatus PipelineStatus => BluetoothPipelineStatus.Inactive;

    private void CheckState(object? state)
    {
        try
        {
            if (_radio != null)
            {
                var mode = _radio.Mode;
                var adapterState = mode switch
                {
                    RadioMode.PowerOff => BluetoothAdapterState.Off,
                    _ => BluetoothAdapterState.On
                };
                UpdateState(adapterState);

                // Check for device connection changes by polling paired devices
                CheckForConnectionChanges();
            }
            else
            {
                // Try to re-acquire radio (may have been plugged in)
                _radio = BluetoothRadio.Default;
                if (_radio != null)
                {
                    _logger.LogInformation("Bluetooth radio became available: {Name}", _radio.Name);
                    UpdateState(BluetoothAdapterState.On);
                }
                else
                {
                    UpdateState(BluetoothAdapterState.Off);
                }
            }
        }
        catch
        {
            UpdateState(BluetoothAdapterState.Off);
        }
    }

    private void CheckForConnectionChanges()
    {
        try
        {
            var paired = _client.PairedDevices;
            var connectedNow = paired.FirstOrDefault(d => d.Connected);

            if (connectedNow != null && ConnectedDevice == null)
            {
                var address = connectedNow.DeviceAddress.ToString();

                // Reconnect cooldown: suppress rapid reconnect cycles for the same device.
                if (_lastDisconnectedAddress == address
                    && (DateTime.UtcNow - _lastDisconnectTime) < ReconnectCooldown)
                {
                    return; // Still in cooldown — ignore this connection
                }

                // Connection stability: require the device to appear connected for
                // multiple consecutive polls before firing the event. This filters
                // out transient/bouncing connections.
                if (_pendingConnectionAddress == address)
                {
                    _pendingConnectionPolls++;
                }
                else
                {
                    _pendingConnectionAddress = address;
                    _pendingConnectionPolls = 1;

#if WINDOWS_TARGET
                    // On FIRST detection (before stability confirmed), immediately try
                    // A2DP connection using DIRECT address resolution. The phone may
                    // disconnect within 2-4 seconds if A2DP isn't established quickly.
                    // Direct address bypasses DeviceWatcher/FindAllAsync which may
                    // return 0 AudioPlaybackConnection devices for some phones.
                    if (_a2dpSinkManager != null && !_a2dpSinkManager.IsConnected)
                    {
                        var macAddress = ulong.Parse(connectedNow.DeviceAddress.ToString(), System.Globalization.NumberStyles.HexNumber);
                        _logger.LogInformation(
                          "BT device detected on first poll — triggering direct A2DP connection for {Address} (MAC: {Mac:X12})",
                          address, macAddress);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _a2dpSinkManager.TryConnectByAddressAsync(macAddress);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Direct A2DP connection failed for {Address}", address);
                            }
                        });
                    }
#endif
                }

                if (_pendingConnectionPolls < RequiredStablePolls)
                {
                    return; // Not stable yet — wait for more polls
                }

                // Connection is stable — fire the event
                var device = MapDevice(connectedNow);
                ConnectedDevice = device;
                _pendingConnectionAddress = null;
                _pendingConnectionPolls = 0;
                _connectionStartTime = DateTime.UtcNow;
                _logger.LogInformation("Bluetooth device connected: {DeviceName} ({Address})",
                    device.Name, device.Address);
                _metricsCollector?.Increment("bluetooth.devices_connected_total");
                _metricsCollector?.Gauge("bluetooth.active_connections", 1);
                DeviceConnected?.Invoke(this, new BluetoothDeviceConnectedEventArgs { Device = device });
#if WINDOWS_TARGET
                // After stability confirmed, try A2DP again if first-poll attempt didn't succeed.
                // Uses direct address resolution (bypasses DeviceWatcher which may return 0 devices).
                if (_a2dpSinkManager != null && !_a2dpSinkManager.IsConnected)
                {
                    var macAddress = ulong.Parse(connectedNow.DeviceAddress.ToString(), System.Globalization.NumberStyles.HexNumber);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _a2dpSinkManager.TryConnectByAddressAsync(macAddress);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "A2DP connection by address failed after stability confirmed");
                        }
                    });
                }
#endif
            }
            else if (connectedNow == null && ConnectedDevice != null)
            {
                // Disconnection detected
                var device = ConnectedDevice;
                ConnectedDevice = null;
                _pendingConnectionAddress = null;
                _pendingConnectionPolls = 0;
                _lastDisconnectedAddress = device.Address;
                _lastDisconnectTime = DateTime.UtcNow;
                RecordDisconnectionMetrics();
                _logger.LogInformation("Bluetooth device disconnected: {DeviceName} ({Address})",
                    device.Name, device.Address);
                DeviceDisconnected?.Invoke(this, new BluetoothDeviceDisconnectedEventArgs { Device = device, UserInitiated = false });
            }
            else if (connectedNow == null)
            {
                // No device connected and no tracked device — reset pending state
                _pendingConnectionAddress = null;
                _pendingConnectionPolls = 0;
            }
            else if (connectedNow != null && ConnectedDevice != null
                     && connectedNow.DeviceAddress.ToString() != ConnectedDevice.Address)
            {
                // Different device connected — fire disconnect for old, connect for new
                var oldDevice = ConnectedDevice;
                var newDevice = MapDevice(connectedNow);

                ConnectedDevice = newDevice;
                RecordDisconnectionMetrics();
                _connectionStartTime = DateTime.UtcNow;
                _metricsCollector?.Increment("bluetooth.devices_connected_total");
                _logger.LogInformation("Bluetooth device switched from {OldDevice} to {NewDevice}",
                    oldDevice.Name, newDevice.Name);
                DeviceDisconnected?.Invoke(this, new BluetoothDeviceDisconnectedEventArgs { Device = oldDevice, UserInitiated = false });
                DeviceConnected?.Invoke(this, new BluetoothDeviceConnectedEventArgs { Device = newDevice });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking for Bluetooth connection changes");
        }
    }

    private void UpdateState(BluetoothAdapterState newState)
    {
        if (State != newState)
        {
            var oldState = State;
            State = newState;
            StateChanged?.Invoke(this, new BluetoothAdapterStateChangedEventArgs
            {
                PreviousState = oldState,
                NewState = newState
            });
        }
    }

    public async Task<bool> StartAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        if (_radio == null)
        {
            _radio = BluetoothRadio.Default;
        }

        if (_radio == null)
        {
            _logger.LogWarning("No Bluetooth radio available");
            return false;
        }

        // Set discoverable mode — non-fatal if it fails. On Windows under the
        // Windows TFM, InTheHand's WindowsBluetoothRadio.set_Mode can throw NRE
        // if the underlying WinRT radio handle is not fully initialized. The phone
        // discovers the PC through Windows Bluetooth Settings regardless.
        try
        {
            _radio.Mode = RadioMode.Discoverable;
            _logger.LogInformation(
                "Bluetooth discoverable as '{ComputerName}' (Windows computer name). " +
                "Requested name '{RequestedName}' can only be set on Linux/Raspberry Pi",
                _radio.Name, deviceName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set Bluetooth radio to Discoverable mode — " +
                "continuing with A2DP/SMTC setup (phone can still connect via Windows Settings)");
        }

        // Register native authentication callback to auto-accept incoming pairing requests.
        // Without this, Windows shows a system toast notification that users may miss,
        // causing "incorrect PIN or passkey" errors on the phone side.
        if (_options.AutoAcceptConnections && _authCallbackHandle == IntPtr.Zero)
        {
            RegisterAuthenticationCallback();
        }

#if WINDOWS_TARGET
        // Initialize A2DP sink manager (AudioPlaybackConnection)
        if (_options.EnableA2dpSink)
        {
            try
            {
                _a2dpSinkManager = new Windows.WindowsA2dpSinkManager(_logger);
                _a2dpSinkManager.A2dpConnected += (_, id) =>
                    _logger.LogInformation("A2DP sink connected: {DeviceId}", id);
                _a2dpSinkManager.A2dpDisconnected += (_, id) =>
                    _logger.LogInformation("A2DP sink disconnected: {DeviceId}", id);
                _a2dpSinkManager.Start();
                _logger.LogInformation("A2DP sink manager initialized (AudioPlaybackConnection)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize A2DP sink manager — phone audio will not route to speakers");
            }
        }

        // Initialize SMTC media session watcher for track metadata
        if (_options.EnableMediaSessionMonitoring)
        {
            try
            {
                _mediaSessionWatcher = new Windows.WindowsMediaSessionWatcher(_logger, _albumArtCache);
                _mediaSessionWatcher.MetadataChanged += (s, e) => MetadataChanged?.Invoke(this, e);
                _mediaSessionWatcher.PlaybackStatusChanged += (s, e) => PlaybackStatusChanged?.Invoke(this, e);
                _mediaSessionWatcher.PositionChanged += (s, e) => PositionChanged?.Invoke(this, e);
                await _mediaSessionWatcher.StartAsync();
                _logger.LogInformation("SMTC media session watcher initialized");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize SMTC media session watcher — metadata will use fingerprinting fallback");
            }
        }
#endif

        return true;
    }

    private void RegisterAuthenticationCallback()
    {
        try
        {
            // Register for ALL devices (zeroed BLUETOOTH_DEVICE_INFO = wildcard)
            var deviceInfo = new BLUETOOTH_DEVICE_INFO
            {
                dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>()
            };

            // Keep a reference to the delegate to prevent garbage collection
            _authCallbackDelegate = OnAuthenticationCallback;

            var result = BluetoothRegisterForAuthenticationEx(
                ref deviceInfo, out _authCallbackHandle, _authCallbackDelegate, IntPtr.Zero);

            if (result == 0) // ERROR_SUCCESS
            {
                _logger.LogInformation("Bluetooth authentication callback registered (auto-accept enabled)");
            }
            else
            {
                _logger.LogWarning("Failed to register Bluetooth authentication callback (error code: {ErrorCode})", result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register Bluetooth authentication callback");
        }
    }

    private void UnregisterAuthenticationCallback()
    {
        if (_authCallbackHandle != IntPtr.Zero)
        {
            try
            {
                BluetoothUnregisterAuthentication(_authCallbackHandle);
                _authCallbackHandle = IntPtr.Zero;
                _authCallbackDelegate = null;
                _logger.LogDebug("Bluetooth authentication callback unregistered");
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error unregistering Bluetooth authentication callback");
            }
        }
    }

    private bool OnAuthenticationCallback(IntPtr pvParam, ref BLUETOOTH_AUTHENTICATION_CALLBACK_PARAMS authParams)
    {
        var deviceName = authParams.deviceInfo.szName ?? "Unknown";
        var authMethod = authParams.authenticationMethod;

        _logger.LogInformation(
            "Bluetooth pairing request from '{DeviceName}' (method: {Method}, passkey: {Passkey})",
            deviceName, authMethod, authParams.Numeric_Value_Passkey);

        if (!_options.AutoAcceptConnections)
        {
            _logger.LogInformation("Rejecting Bluetooth pairing from '{DeviceName}' (auto-accept disabled)", deviceName);
            return false;
        }

        try
        {
            var response = new BLUETOOTH_AUTHENTICATE_RESPONSE
            {
                bthAddressRemote = authParams.deviceInfo.Address,
                authMethod = authMethod,
                bNegativeResponse = false
            };

            // For numeric comparison / SSP, echo back the passkey to confirm
            if (authMethod == BLUETOOTH_AUTHENTICATION_METHOD_NUMERIC_COMPARISON
                || authMethod == BLUETOOTH_AUTHENTICATION_METHOD_PASSKEY)
            {
                response.Numeric_Value_Passkey = authParams.Numeric_Value_Passkey;
            }

            var result = BluetoothSendAuthenticationResponseEx(IntPtr.Zero, ref response);
            if (result == 0)
            {
                _logger.LogInformation("Auto-accepted Bluetooth pairing from '{DeviceName}'", deviceName);
                return true;
            }
            else
            {
                _logger.LogWarning("BluetoothSendAuthenticationResponseEx failed (error: {Error}) for '{DeviceName}'",
                    result, deviceName);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error sending Bluetooth authentication response for '{DeviceName}'", deviceName);
            return false;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_radio != null)
            {
                _radio.Mode = RadioMode.Connectable;
                _logger.LogInformation("Bluetooth radio set to Connectable mode (no longer discoverable)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set Bluetooth radio to Connectable mode");
        }

        return Task.CompletedTask;
    }

    public void StopAudioCapture()
    {
        // Windows uses WASAPI loopback — nothing to stop here (handled by StopLoopbackCapture)
    }

    public async Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        if (IsDiscovering)
        {
            return;
        }
        IsDiscovering = true;
        _metricsCollector?.Increment("bluetooth.discovery_sessions");
        _discoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_discoveryCts.Token.IsCancellationRequested)
                {
                    var devices = _client.DiscoverDevices();
                    foreach (var d in devices)
                    {
                        var info = MapDevice(d);
                        lock (_discoveredDevices)
                        {
                            if (!_discoveredDevices.Any(x => x.Address == info.Address))
                            {
                                _discoveredDevices.Add(info);
                                DeviceDiscovered?.Invoke(this, new BluetoothDeviceDiscoveredEventArgs { Device = info });
                            }
                        }
                    }
                    await Task.Delay(5000, _discoveryCts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during discovery");
            }
            finally
            {
                IsDiscovering = false;
            }
        }, _discoveryCts.Token);
    }

    public Task StopDiscoveryAsync()
    {
        _discoveryCts?.Cancel();
        IsDiscovering = false;
        return Task.CompletedTask;
    }

    public Task<bool> PairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
    {
        if (BluetoothAddress.TryParse(deviceAddress, out var address))
        {
            var result = BluetoothSecurity.PairRequest(address, null);
            var resultTag = new Dictionary<string, string> { { "result", result ? "success" : "failure" } };
            _metricsCollector?.Increment("bluetooth.pair_attempts", 1.0, resultTag);
            return Task.FromResult(result);
        }
        _metricsCollector?.Increment("bluetooth.pair_attempts", 1.0,
            new Dictionary<string, string> { { "result", "failure" } });
        return Task.FromResult(false);
    }

    public Task<bool> UnpairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
    {
        if (BluetoothAddress.TryParse(deviceAddress, out var address))
        {
            return Task.FromResult(BluetoothSecurity.RemoveDevice(address));
        }
        return Task.FromResult(false);
    }

    public Task<bool> AcceptConnectionAsync(string deviceAddress, CancellationToken cancellationToken = default)
    {
        // Windows handles A2DP sink connections at the OS level usually.
        // We would rely on the OS to accept it.
        return Task.FromResult(true);
    }

    public Task<bool> ConnectAsync(string deviceAddress, CancellationToken cancellationToken = default)
    {
        // Windows BT service uses InTheHand polling — outbound connect not implemented
        return Task.FromResult(false);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var device = ConnectedDevice;
        if (device != null)
        {
            ConnectedDevice = null;
            RecordDisconnectionMetrics();
            _logger.LogInformation("Disconnected Bluetooth device: {DeviceName} ({Address})",
                device.Name, device.Address);
            DeviceDisconnected?.Invoke(this, new BluetoothDeviceDisconnectedEventArgs { Device = device, UserInitiated = true });

            // Dispose and recreate client to drop active connections
            try
            {
                _client?.Dispose();
                _client = new BluetoothClient();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error recreating Bluetooth client after disconnect");
                _client = new BluetoothClient();
            }
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(string deviceAddress, CancellationToken cancellationToken = default)
    {
        // Windows: delegate to the parameterless version (single-device model)
        return DisconnectAsync(cancellationToken);
    }

    public Task<object?> GetAudioCaptureDeviceAsync(CancellationToken cancellationToken = default)
    {
#if WINDOWS_TARGET
        // When A2DP sink + loopback capture are both enabled, return a BufferedSoundGenerator
        // that will receive audio from WASAPI loopback capture
        if (_a2dpSinkManager != null && _options.EnableLoopbackCapture && _playbackService != null)
        {
            try
            {
                var engine = _playbackService.GetUnderlyingEngine();
                if (engine == null)
                {
                    _logger.LogWarning("SoundFlow engine not available for loopback capture");
                    return Task.FromResult<object?>(null);
                }

                var format = _playbackService.GetAudioFormat();
                _loopbackCapture = new Windows.WasapiLoopbackCaptureSource(_logger);
                _loopbackGenerator = _loopbackCapture.CreateGenerator(engine, format, _logger, _metricsCollector);

                _logger.LogInformation(
                  "WASAPI loopback capture device created — BT audio will route through SoundFlow pipeline");
                return Task.FromResult<object?>(_loopbackGenerator);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create WASAPI loopback capture — falling back to platform-managed audio");
                _loopbackCapture?.Dispose();
                _loopbackCapture = null;
                _loopbackGenerator = null;
            }
        }

        // When A2DP sink is active but loopback disabled — platform manages audio
        if (_a2dpSinkManager != null && !_options.EnableLoopbackCapture)
        {
            _logger.LogInformation("A2DP sink manager active (loopback disabled) — audio managed by platform");
            return Task.FromResult<object?>(null);
        }
#endif

        // Cleanup previous capture engine if any
        _captureEngine?.Dispose();
        _captureEngine = null;

        if (ConnectedDevice == null)
        {
            _logger.LogWarning("No connected Bluetooth device — cannot create audio capture");
            return Task.FromResult<object?>(null);
        }

        try
        {
            _captureEngine = new MiniAudioEngine();
            var captureDevices = _captureEngine.CaptureDevices;
            var deviceName = ConnectedDevice.Name;

            // Log all available capture devices for diagnostics
            _logger.LogInformation(
                "Searching for Bluetooth audio capture device matching '{DeviceName}' among {Count} capture devices:",
                deviceName, captureDevices.Length);
            foreach (var device in captureDevices)
            {
                _logger.LogInformation("  Capture device: '{CaptureDeviceName}'", device.Name);
            }

            if (captureDevices.Length == 0)
            {
                _logger.LogWarning("No audio capture devices available on this system");
                _captureEngine.Dispose();
                _captureEngine = null;
                return Task.FromResult<object?>(null);
            }

            // Strategy 1: Match by connected device name
            DeviceInfo? targetDevice = null;
            foreach (var device in captureDevices)
            {
                if (device.Name != null && device.Name.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    targetDevice = device;
                    break;
                }
            }

            // Strategy 2: Search for "bluetooth" keyword in device name
            if (targetDevice == null)
            {
                foreach (var device in captureDevices)
                {
                    if (device.Name != null && device.Name.Contains("bluetooth", StringComparison.OrdinalIgnoreCase))
                    {
                        targetDevice = device;
                        break;
                    }
                }
            }

            if (targetDevice != null)
            {
                var format = _options.AudioQuality == BluetoothAudioQuality.High
                    ? new AudioFormat { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 }
                    : AudioFormat.Cd;

                var captureDevice = _captureEngine.InitializeCaptureDevice(targetDevice, format);
                _logger.LogInformation(
                    "Created Bluetooth audio capture device: {DeviceName} ({Quality})",
                    targetDevice.Value.Name, _options.AudioQuality);
                return Task.FromResult<object?>(captureDevice);
            }

            // No Bluetooth-specific capture device found.
            _logger.LogWarning(
                "No Bluetooth audio capture device found. " +
                "None of the {Count} capture device(s) match '{DeviceName}' or contain 'bluetooth'. " +
                "This works on Linux/Raspberry Pi with BlueZ",
                captureDevices.Length, deviceName);
            _captureEngine.Dispose();
            _captureEngine = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create Bluetooth audio capture device");
            _metricsCollector?.Increment("bluetooth.audio_capture_errors");
            _captureEngine?.Dispose();
            _captureEngine = null;
        }

        return Task.FromResult<object?>(null);
    }

    /// <summary>
    /// Starts WASAPI loopback capture. Call after registering the generator with the mixer.
    /// </summary>
    internal void StartLoopbackCapture()
    {
#if WINDOWS_TARGET
        if (_loopbackCapture == null || _loopbackGenerator == null)
        {
            _logger.LogDebug("StartLoopbackCapture: no loopback capture/generator available");
            return;
        }

        // Double-play detection: check if SoundFlow output is the same as Windows default
        var soundFlowOutputId = _deviceManager?.GetSelectedOutputDeviceId();
        var isSameDevice = _loopbackCapture.IsSameDeviceAsDefault(soundFlowOutputId);
        var shouldMute = !isSameDevice;

        if (isSameDevice)
        {
            _logger.LogInformation(
              "WASAPI loopback: SoundFlow output is same as Windows default — " +
              "NOT muting endpoint (AudioPlaybackConnection handles local output, SoundFlow handles Cast/viz)");
        }
        else
        {
            _logger.LogInformation(
              "WASAPI loopback: SoundFlow output differs from Windows default — " +
              "muting default endpoint, SoundFlow handles all output");
        }

        _loopbackCapture.StartCapture(_loopbackGenerator, shouldMute);
#endif
    }

    /// <summary>
    /// Stops WASAPI loopback capture and restores endpoint state.
    /// </summary>
    internal void StopLoopbackCapture()
    {
#if WINDOWS_TARGET
        _loopbackCapture?.StopCapture();
#endif
    }

    private void RecordDisconnectionMetrics()
    {
        _metricsCollector?.Increment("bluetooth.devices_disconnected_total");
        _metricsCollector?.Gauge("bluetooth.active_connections", 0);
        if (_connectionStartTime.HasValue)
        {
            var duration = (DateTime.UtcNow - _connectionStartTime.Value).TotalSeconds;
            _metricsCollector?.Gauge("bluetooth.connection_duration_seconds", duration);
            _connectionStartTime = null;
        }
    }

    private Radio.Core.Interfaces.Audio.BluetoothDeviceInfo MapDevice(InTheHand.Net.Sockets.BluetoothDeviceInfo device)
    {
        return new Radio.Core.Interfaces.Audio.BluetoothDeviceInfo
        {
            Address = device.DeviceAddress.ToString(),
            Name = device.DeviceName,
            IsPaired = device.Authenticated,
            IsConnected = device.Connected,
            LastConnected = DateTime.UtcNow
        };
    }

    public async ValueTask DisposeAsync()
    {
        _stateTimer?.Dispose();
        _discoveryCts?.Cancel();

        // Unregister native authentication callback
        UnregisterAuthenticationCallback();

#if WINDOWS_TARGET
        // Dispose loopback capture (restores endpoint mute state)
        _loopbackCapture?.Dispose();
        _loopbackCapture = null;
        _loopbackGenerator = null;

        // Dispose A2DP sink manager and media session watcher
        _a2dpSinkManager?.Dispose();
        _a2dpSinkManager = null;
        _mediaSessionWatcher?.Dispose();
        _mediaSessionWatcher = null;
#endif

        // Dispose audio capture engine
        _captureEngine?.Dispose();
        _captureEngine = null;

        // Restore connectable mode on shutdown
        try
        {
            if (_radio != null && _radio.Mode == RadioMode.Discoverable)
            {
                _radio.Mode = RadioMode.Connectable;
            }
        }
        catch
        {
            // Best-effort cleanup
        }

        try
        {
            _client?.Dispose();
        }
        catch
        {
            // InTheHand.Net can throw NullReferenceException during dispose if not fully initialized
        }
    }
}
