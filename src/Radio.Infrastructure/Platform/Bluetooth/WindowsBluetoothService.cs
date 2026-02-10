using System;
using System.Collections.Generic;
using System.Linq;
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
    private BluetoothClient _client;
    private BluetoothRadio? _radio;
    private readonly Timer _stateTimer;
    private MiniAudioEngine? _captureEngine;
    private DateTime? _connectionStartTime;

    // InTheHand.Net doesn't have robust event-driven discovery in the same way,
    // often relies on polling or blocking calls. We'll simulate async discovery.
    private CancellationTokenSource? _discoveryCts;

    public WindowsBluetoothService(
        ILogger logger,
        IOptions<BluetoothOptions> options,
        Radio.Infrastructure.Audio.SoundFlow.SoundFlowDeviceManager? deviceManager = null,
        IMetricsCollector? metricsCollector = null)
    {
        _logger = logger;
        _options = options.Value;
        _deviceManager = deviceManager;
        _metricsCollector = metricsCollector;
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

    public event EventHandler<BluetoothAdapterStateChangedEventArgs>? StateChanged;
    public event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected;
    public event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected;
    public event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered;
    // TODO: Windows AVRCP metadata via Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager
    // Requires net8.0-windows10.0.17763.0 TFM or conditional compilation.
    // Fingerprinting pipeline serves as fallback for track identification.
#pragma warning disable CS0067
    public event EventHandler<BluetoothPlaybackMetadata>? MetadataChanged;
    public event EventHandler<BluetoothPlaybackStatus>? PlaybackStatusChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
#pragma warning restore CS0067

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
                // New connection detected
                var device = MapDevice(connectedNow);
                ConnectedDevice = device;
                _connectionStartTime = DateTime.UtcNow;
                _logger.LogInformation("Bluetooth device connected: {DeviceName} ({Address})",
                    device.Name, device.Address);
                _metricsCollector?.Increment("bluetooth.devices_connected_total");
                _metricsCollector?.Gauge("bluetooth.active_connections", 1);
                DeviceConnected?.Invoke(this, new BluetoothDeviceConnectedEventArgs { Device = device });
            }
            else if (connectedNow == null && ConnectedDevice != null)
            {
                // Disconnection detected
                var device = ConnectedDevice;
                ConnectedDevice = null;
                RecordDisconnectionMetrics();
                _logger.LogInformation("Bluetooth device disconnected: {DeviceName} ({Address})",
                    device.Name, device.Address);
                DeviceDisconnected?.Invoke(this, new BluetoothDeviceDisconnectedEventArgs { Device = device });
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
                DeviceDisconnected?.Invoke(this, new BluetoothDeviceDisconnectedEventArgs { Device = oldDevice });
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

    public Task<bool> StartAsync(string deviceName, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_radio == null)
            {
                _radio = BluetoothRadio.Default;
            }

            if (_radio == null)
            {
                _logger.LogWarning("No Bluetooth radio available to set discoverable");
                return Task.FromResult(false);
            }

            _radio.Mode = RadioMode.Discoverable;
            _logger.LogInformation("Bluetooth radio set to Discoverable mode (device name: {Name})", deviceName);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set Bluetooth radio to Discoverable mode");
            return Task.FromResult(false);
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

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var device = ConnectedDevice;
        if (device != null)
        {
            ConnectedDevice = null;
            RecordDisconnectionMetrics();
            _logger.LogInformation("Disconnected Bluetooth device: {DeviceName} ({Address})",
                device.Name, device.Address);
            DeviceDisconnected?.Invoke(this, new BluetoothDeviceDisconnectedEventArgs { Device = device });

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

    public Task<object?> GetAudioCaptureDeviceAsync(CancellationToken cancellationToken = default)
    {
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

            _logger.LogInformation(
                "Searching for Bluetooth audio capture device matching '{DeviceName}' among {Count} capture devices",
                deviceName, captureDevices.Length);

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

            // Strategy 3: Use first available capture device
            if (targetDevice == null && captureDevices.Length > 0)
            {
                _logger.LogWarning(
                    "No Bluetooth-specific capture device found, using first available: {DeviceName}",
                    captureDevices[0].Name);
                targetDevice = captureDevices[0];
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

            _logger.LogWarning("No audio capture devices available");
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
