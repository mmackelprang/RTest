using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;
using SoundFlow.Structs;
using Tmds.DBus;

namespace Radio.Infrastructure.Platform.Bluetooth
{
    internal sealed class LinuxBluetoothService : IBluetoothService
    {
        private readonly ILogger _logger;
        private readonly BluetoothOptions _options;
        private readonly Radio.Infrastructure.Audio.SoundFlow.SoundFlowDeviceManager? _deviceManager;
        private readonly IMetricsCollector? _metricsCollector;
        private Connection? _connection;
        private Linux.IObjectManager? _objectManager;
        private Linux.IAdapter1? _adapter;
        private IDisposable? _discoveryWatcher;
        private MiniAudioEngine? _captureEngine;
        private DateTime? _connectionStartTime;

        // Player tracking
        private Linux.IMediaPlayer1? _mediaPlayer;
        private ObjectPath? _mediaPlayerPath;
        private IDisposable? _playerPropertiesWatcher;

        // Maps object path to device info
        private readonly Dictionary<ObjectPath, BluetoothDeviceInfo> _deviceCache = new();
        private readonly SemaphoreSlim _captureDeviceLock = new(1, 1);
        private Linux.BluezAgent? _agent;

        public LinuxBluetoothService(
            ILogger logger,
            IOptions<BluetoothOptions> options,
            Radio.Infrastructure.Audio.SoundFlow.SoundFlowDeviceManager? deviceManager = null,
            IMetricsCollector? metricsCollector = null)
        {
            _logger = logger;
            _options = options.Value;
            _deviceManager = deviceManager;
            _metricsCollector = metricsCollector;
        }

        public bool IsAvailable => _connection != null && _adapter != null;
        public BluetoothAdapterState State { get; private set; } = BluetoothAdapterState.Unknown;

        public IReadOnlyList<BluetoothDeviceInfo> PairedDevices
        {
            get
            {
                lock (_deviceCache)
                {
                    return _deviceCache.Values.Where(d => d.IsPaired).ToList();
                }
            }
        }

        public IReadOnlyList<BluetoothDeviceInfo> DiscoveredDevices
        {
            get
            {
                lock (_deviceCache)
                {
                    return _deviceCache.Values.ToList();
                }
            }
        }

        public bool IsDiscovering { get; private set; }

        public bool IsAudioManagedByPlatform => false;

        public BluetoothDeviceInfo? ConnectedDevice
        {
            get
            {
                lock (_deviceCache)
                {
                    return _deviceCache.Values.FirstOrDefault(d => d.IsConnected);
                }
            }
        }

        public event EventHandler<BluetoothAdapterStateChangedEventArgs>? StateChanged;
        public event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected;
        public event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected;
        public event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<BluetoothPlaybackMetadata>? MetadataChanged;
        public event EventHandler<BluetoothPlaybackStatus>? PlaybackStatusChanged;
        public event EventHandler<TimeSpan>? PositionChanged;
#pragma warning disable CS0067 // VolumeChanged will be wired when BlueZ MediaTransport1.Volume monitoring is implemented
        public event EventHandler<BluetoothVolumeChangedEventArgs>? VolumeChanged;
#pragma warning restore CS0067
        public float? DeviceVolume { get; private set; }

        public Task SetDeviceVolumeAsync(float volume)
        {
            // BlueZ MediaTransport1.Volume (0-127) could be set via D-Bus here.
            // Requires finding the MediaTransport1 object for the connected device.
            _logger.LogDebug("SetDeviceVolumeAsync not yet implemented for Linux BT");
            return Task.CompletedTask;
        }

        public async Task<bool> StartAsync(string deviceName, CancellationToken cancellationToken = default)
        {
            try
            {
                _connection = new Connection(Address.System);
                await _connection.ConnectAsync();
                
                _objectManager = _connection.CreateProxy<Linux.IObjectManager>(Linux.BluezConstants.ServiceName, "/");
                var objects = await _objectManager.GetManagedObjectsAsync();

                // Find first adapter
                foreach (var obj in objects)
                {
                    if (obj.Value.ContainsKey(Linux.BluezConstants.AdapterInterface))
                    {
                        _adapter = _connection.CreateProxy<Linux.IAdapter1>(Linux.BluezConstants.ServiceName, obj.Key);
                        break;
                    }
                }

                if (_adapter == null)
                {
                    _logger.LogError("No Bluetooth adapter found.");
                    State = BluetoothAdapterState.Error;
                    return false;
                }

                // Set powered
                await _adapter.SetAsync("Powered", true);
                if (!string.IsNullOrEmpty(deviceName))
                {
                    await _adapter.SetAsync("Alias", deviceName);
                }
                
                await _adapter.SetAsync("Discoverable", true);
                await _adapter.SetAsync("Pairable", true);

                // Register a BlueZ Agent to handle pairing requests automatically.
                // Without an agent, pairing fails with "incorrect PIN or passkey" because
                // BlueZ has no one to delegate the pairing decision to.
                await RegisterAgentAsync();

                // Watch for new interfaces (device connects/disconnects)
                _discoveryWatcher = await _objectManager.WatchInterfacesAddedAsync(OnInterfaceAdded);

                // Set up property watchers on all existing devices so we detect
                // reconnections from already-paired phones.
                await WatchExistingDevicesAsync();

                State = BluetoothAdapterState.On;
                StateChanged?.Invoke(this, new BluetoothAdapterStateChangedEventArgs { NewState = State });
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Bluetooth service");
                State = BluetoothAdapterState.Error;
                return false;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_adapter != null)
            {
                try
                {
                    await _adapter.SetAsync("Powered", false);
                    State = BluetoothAdapterState.Off;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping Bluetooth adapter");
                }
            }
        }

        public async Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
        {
            if (_adapter == null || _objectManager == null) return;
            try
            {
                await _adapter.StartDiscoveryAsync();
                IsDiscovering = true;
                _metricsCollector?.Increment("bluetooth.discovery_sessions");

                // InterfacesAdded watcher is already set up in StartAsync — no need to
                // create a duplicate here (the old one would leak, and both would fire
                // events causing duplicate DeviceConnected/AttachMediaPlayer calls).

                // Scan for existing players that appeared before discovery started
                await CheckForMediaPlayersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start discovery");
            }
        }

        public async Task StopDiscoveryAsync()
        {
            if (_adapter == null) return;
            try
            {
                await _adapter.StopDiscoveryAsync();
                IsDiscovering = false;
                _discoveryWatcher?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop discovery");
            }
        }
        
        /// <summary>
        /// Scans BlueZ for existing Device1 objects and sets up property watchers
        /// so that reconnections from already-paired phones are detected even
        /// without an explicit StartDiscoveryAsync call.
        /// </summary>
        private async Task WatchExistingDevicesAsync()
        {
            if (_objectManager == null) return;

            try
            {
                var objects = await _objectManager.GetManagedObjectsAsync();
                foreach (var obj in objects)
                {
                    if (!obj.Value.ContainsKey(Linux.BluezConstants.DeviceInterface))
                        continue;

                    var props = obj.Value[Linux.BluezConstants.DeviceInterface];
                    var device = ParseDevice(obj.Key, props);

                    lock (_deviceCache)
                    {
                        _deviceCache[obj.Key] = device;
                    }

                    // Watch property changes (Connected, etc.) on this device
                    _ = WatchDevicePropertiesAsync(obj.Key);

                    if (device.IsConnected)
                    {
                        _connectionStartTime = DateTime.UtcNow;
                        _metricsCollector?.Increment("bluetooth.devices_connected_total");
                        _metricsCollector?.Gauge("bluetooth.active_connections", 1);
                        _logger.LogInformation("Bluetooth device already connected: {DeviceName} ({Address})",
                            device.Name, device.Address);
                        DeviceConnected?.Invoke(this, new BluetoothDeviceConnectedEventArgs { Device = device });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to watch existing Bluetooth devices");
            }
        }

        private void OnInterfaceAdded((ObjectPath objectPath, IDictionary<string, IDictionary<string, object>> interfaces) change)
        {
            if (change.interfaces.ContainsKey(Linux.BluezConstants.DeviceInterface))
            {
                var props = change.interfaces[Linux.BluezConstants.DeviceInterface];
                var device = ParseDevice(change.objectPath, props);

                lock (_deviceCache)
                {
                    _deviceCache[change.objectPath] = device;
                }

                DeviceDiscovered?.Invoke(this, new BluetoothDeviceDiscoveredEventArgs { Device = device });

                if (device.IsConnected)
                {
                    _connectionStartTime = DateTime.UtcNow;
                    _metricsCollector?.Increment("bluetooth.devices_connected_total");
                    _metricsCollector?.Gauge("bluetooth.active_connections", 1);
                    _logger.LogInformation("Bluetooth device connected: {DeviceName} ({Address})",
                        device.Name, device.Address);
                    DeviceConnected?.Invoke(this, new BluetoothDeviceConnectedEventArgs { Device = device });
                }

                // Watch property changes on this device for connect/disconnect tracking
                _ = WatchDevicePropertiesAsync(change.objectPath);
            }

            if (change.interfaces.ContainsKey(Linux.BluezConstants.MediaPlayerInterface))
            {
                _ = AttachMediaPlayerAsync(change.objectPath);
            }
        }

        private async Task WatchDevicePropertiesAsync(ObjectPath devicePath)
        {
            if (_connection == null) return;

            // Prevent duplicate watchers — each fires DeviceConnected independently
            lock (_watchedDevicePaths)
            {
                if (!_watchedDevicePaths.Add(devicePath))
                {
                    _logger.LogDebug("Already watching device properties at {Path}, skipping", devicePath);
                    return;
                }
            }

            try
            {
                var device = _connection.CreateProxy<Linux.IDevice1>(
                    Linux.BluezConstants.ServiceName, devicePath);

                await device.WatchPropertiesAsync(changes =>
                {
                    foreach (var prop in changes.Changed)
                    {
                        if (prop.Key == "Connected" && prop.Value is bool connected)
                        {
                            BluetoothDeviceInfo? deviceInfo;
                            lock (_deviceCache)
                            {
                                _deviceCache.TryGetValue(devicePath, out deviceInfo);
                            }

                            if (deviceInfo == null) return;

                            // Update cache with new connection state
                            var updatedDevice = new BluetoothDeviceInfo
                            {
                                Address = deviceInfo.Address,
                                Name = deviceInfo.Name,
                                IsPaired = deviceInfo.IsPaired,
                                IsConnected = connected
                            };

                            lock (_deviceCache)
                            {
                                _deviceCache[devicePath] = updatedDevice;
                            }

                            if (connected)
                            {
                                _connectionStartTime = DateTime.UtcNow;
                                _metricsCollector?.Increment("bluetooth.devices_connected_total");
                                _metricsCollector?.Gauge("bluetooth.active_connections", 1);
                                _logger.LogInformation("Bluetooth device connected: {DeviceName} ({Address})",
                                    updatedDevice.Name, updatedDevice.Address);
                                DeviceConnected?.Invoke(this, new BluetoothDeviceConnectedEventArgs { Device = updatedDevice });
                            }
                            else
                            {
                                RecordDisconnectionMetrics();
                                _logger.LogInformation("Bluetooth device disconnected: {DeviceName} ({Address})",
                                    updatedDevice.Name, updatedDevice.Address);
                                DeviceDisconnected?.Invoke(this, new BluetoothDeviceDisconnectedEventArgs { Device = updatedDevice });
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to watch device properties at {Path}", devicePath);
            }
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

        private BluetoothDeviceInfo ParseDevice(ObjectPath path, IDictionary<string, object> props)
        {
            return new BluetoothDeviceInfo
            {
                Address = props.ContainsKey("Address") ? (string)props["Address"] : "Unknown",
                Name = props.ContainsKey("Name") ? (string)props["Name"] : (props.ContainsKey("Alias") ? (string)props["Alias"] : "Unknown Device"),
                IsPaired = props.ContainsKey("Paired") && (bool)props["Paired"],
                IsConnected = props.ContainsKey("Connected") && (bool)props["Connected"]
            };
        }

        public async Task<bool> PairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
        {
            if (_connection == null || _objectManager == null)
            {
                _logger.LogWarning("Cannot pair: Bluetooth service not started");
                return false;
            }

            try
            {
                var devicePath = FindDevicePath(deviceAddress);
                if (devicePath == null)
                {
                    _logger.LogWarning("Device {Address} not found for pairing", deviceAddress);
                    return false;
                }

                var device = _connection.CreateProxy<Linux.IDevice1>(
                    Linux.BluezConstants.ServiceName, devicePath.Value);
                await device.PairAsync();
                _metricsCollector?.Increment("bluetooth.pair_attempts", 1.0,
                    new Dictionary<string, string> { { "result", "success" } });
                _logger.LogInformation("Paired with device {Address}", deviceAddress);
                return true;
            }
            catch (Exception ex)
            {
                _metricsCollector?.Increment("bluetooth.pair_attempts", 1.0,
                    new Dictionary<string, string> { { "result", "failure" } });
                _logger.LogError(ex, "Failed to pair with device {Address}", deviceAddress);
                return false;
            }
        }

        public async Task<bool> UnpairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
        {
            if (_connection == null || _adapter == null)
            {
                _logger.LogWarning("Cannot unpair: Bluetooth service not started");
                return false;
            }

            try
            {
                var devicePath = FindDevicePath(deviceAddress);
                if (devicePath == null)
                {
                    _logger.LogWarning("Device {Address} not found for unpairing", deviceAddress);
                    return false;
                }

                await _adapter.RemoveDeviceAsync(devicePath.Value);
                lock (_deviceCache)
                {
                    _deviceCache.Remove(devicePath.Value);
                }
                _logger.LogInformation("Unpaired device {Address}", deviceAddress);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unpair device {Address}", deviceAddress);
                return false;
            }
        }

        public Task<bool> AcceptConnectionAsync(string deviceAddress, CancellationToken cancellationToken = default)
        {
            // BlueZ handles incoming connections automatically if Pairable is true
            return Task.FromResult(true);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            var connected = ConnectedDevice;
            if (connected == null || _connection == null)
            {
                return;
            }

            try
            {
                var devicePath = FindDevicePath(connected.Address);
                if (devicePath != null)
                {
                    var device = _connection.CreateProxy<Linux.IDevice1>(
                        Linux.BluezConstants.ServiceName, devicePath.Value);
                    await device.DisconnectAsync();
                    _logger.LogInformation("Disconnected device {DeviceName} ({Address})",
                        connected.Name, connected.Address);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to disconnect device {Address}", connected.Address);
            }
        }

        private ObjectPath? FindDevicePath(string deviceAddress)
        {
            var normalizedAddress = deviceAddress.Replace(":", "_").ToUpperInvariant();
            lock (_deviceCache)
            {
                foreach (var kvp in _deviceCache)
                {
                    if (kvp.Value.Address.Replace(":", "_").Equals(normalizedAddress, StringComparison.OrdinalIgnoreCase)
                        || kvp.Value.Address.Equals(deviceAddress, StringComparison.OrdinalIgnoreCase))
                    {
                        return kvp.Key;
                    }
                }
            }
            return null;
        }

        public async Task<object?> GetAudioCaptureDeviceAsync(CancellationToken cancellationToken = default)
        {
            var connected = ConnectedDevice;
            if (connected == null)
            {
                _logger.LogWarning("No connected Bluetooth device — cannot create audio capture");
                return null;
            }

            // Prevent concurrent calls — multiple DeviceConnected events fire simultaneously
            // and MiniAudioEngine is not safe to instantiate concurrently.
            if (!await _captureDeviceLock.WaitAsync(0, cancellationToken))
            {
                _logger.LogDebug("Capture device search already in progress, skipping duplicate call");
                return null;
            }

            try
            {
                return await SearchForCaptureDeviceAsync(connected, cancellationToken);
            }
            finally
            {
                _captureDeviceLock.Release();
            }
        }

        private async Task<object?> SearchForCaptureDeviceAsync(
            BluetoothDeviceInfo connected, CancellationToken cancellationToken)
        {
            // Cleanup previous capture engine
            _captureEngine?.Dispose();
            _captureEngine = null;

            // PulseAudio/PipeWire takes time to create the Bluetooth capture device
            // after A2DP is authorized. Poll with retries.
            const int maxRetries = 20;
            const int retryDelayMs = 1000;

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Re-check connection — phone may have disconnected during polling
                if (ConnectedDevice == null)
                {
                    _logger.LogWarning("Bluetooth device disconnected during capture device polling");
                    return null;
                }

                try
                {
                    // Create a fresh engine each attempt to re-enumerate devices.
                    // PipeWire may add the bluez source between enumerations.
                    _captureEngine?.Dispose();
                    _captureEngine = new MiniAudioEngine();
                    var captureDevices = _captureEngine.CaptureDevices;

                    _logger.LogDebug(
                        "Capture device search attempt {Attempt}/{Max}: {Count} devices",
                        attempt, maxRetries, captureDevices.Length);

                    if (attempt == 1)
                    {
                        for (var i = 0; i < captureDevices.Length; i++)
                        {
                            _logger.LogInformation("  Capture device [{Index}]: {Name}",
                                i, captureDevices[i].Name ?? "(null)");
                        }
                    }

                    // On Linux with PulseAudio/PipeWire, Bluetooth audio devices appear as:
                    //   "bluez_output.<mac_address>.monitor" or "bluez_sink.<mac_address>.monitor"
                    //   or by the connected device name
                    var targetDevice = FindBluetoothCaptureDevice(captureDevices, connected.Name);

                    if (targetDevice != null)
                    {
                        var format = _options.AudioQuality == BluetoothAudioQuality.High
                            ? new AudioFormat { SampleRate = 48000, Channels = 2, Format = SampleFormat.F32 }
                            : AudioFormat.Cd;

                        var captureDevice = _captureEngine.InitializeCaptureDevice(targetDevice, format);
                        _logger.LogInformation(
                            "Created Bluetooth audio capture device: {DeviceName} (attempt {Attempt}/{Max})",
                            targetDevice.Value.Name, attempt, maxRetries);
                        return captureDevice;
                    }

                    // Log new devices that appear on subsequent attempts
                    if (attempt > 1 && captureDevices.Length > 1)
                    {
                        for (var i = 0; i < captureDevices.Length; i++)
                        {
                            _logger.LogDebug("  Capture device [{Index}]: {Name}",
                                i, captureDevices[i].Name ?? "(null)");
                        }
                    }

                    _captureEngine.Dispose();
                    _captureEngine = null;

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Capture device search attempt {Attempt} failed", attempt);
                    _captureEngine?.Dispose();
                    _captureEngine = null;

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs, cancellationToken);
                    }
                }
            }

            _logger.LogWarning("No Bluetooth audio capture device found after {MaxRetries}s", maxRetries);
            _metricsCollector?.Increment("bluetooth.audio_capture_errors");
            return null;
        }

        private static DeviceInfo? FindBluetoothCaptureDevice(DeviceInfo[] devices, string connectedName)
        {
            // Strategy 1: Match by "bluez" prefix (PipeWire Bluetooth source)
            foreach (var device in devices)
            {
                if (device.Name != null && device.Name.Contains("bluez", StringComparison.OrdinalIgnoreCase))
                    return device;
            }

            // Strategy 2: Match by connected device name
            foreach (var device in devices)
            {
                if (device.Name != null && device.Name.Contains(connectedName, StringComparison.OrdinalIgnoreCase))
                    return device;
            }

            // Strategy 3: Match by "bluetooth" keyword
            foreach (var device in devices)
            {
                if (device.Name != null && device.Name.Contains("bluetooth", StringComparison.OrdinalIgnoreCase))
                    return device;
            }

            return null;
        }

        public async ValueTask DisposeAsync()
        {
            _playerPropertiesWatcher?.Dispose();
            _discoveryWatcher?.Dispose();
            _captureEngine?.Dispose();
            _captureEngine = null;
            await UnregisterAgentAsync();
            await StopAsync();
            _connection?.Dispose();
        }

        private async Task RegisterAgentAsync()
        {
            if (_connection == null) return;

            try
            {
                _agent = new Linux.BluezAgent(_logger, _options.AutoAcceptConnections);

                // Export the agent object on D-Bus so BlueZ can call its methods
                await _connection.RegisterObjectAsync(_agent);

                var agentManager = _connection.CreateProxy<Linux.IAgentManager1>(
                    Linux.BluezConstants.ServiceName, "/org/bluez");

                // "NoInputNoOutput" capability enables Just Works pairing (no PIN prompt)
                await agentManager.RegisterAgentAsync(_agent.ObjectPath, "NoInputNoOutput");
                await agentManager.RequestDefaultAgentAsync(_agent.ObjectPath);

                _logger.LogInformation("Bluetooth pairing agent registered (auto-accept: {AutoAccept})",
                    _options.AutoAcceptConnections);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register Bluetooth pairing agent — pairing may require manual acceptance");
            }
        }

        private async Task UnregisterAgentAsync()
        {
            if (_connection == null || _agent == null) return;

            try
            {
                var agentManager = _connection.CreateProxy<Linux.IAgentManager1>(
                    Linux.BluezConstants.ServiceName, "/org/bluez");
                await agentManager.UnregisterAgentAsync(_agent.ObjectPath);
                _connection.UnregisterObject(_agent);
                _agent = null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to unregister Bluetooth agent (may already be unregistered)");
            }
        }

        private async Task CheckForMediaPlayersAsync()
        {
            try
            {
                if (_objectManager == null) return;
                var objects = await _objectManager.GetManagedObjectsAsync();
                foreach (var obj in objects)
                {
                    if (obj.Value.ContainsKey(Linux.BluezConstants.MediaPlayerInterface))
                    {
                        await AttachMediaPlayerAsync(obj.Key);
                        // For now we just attach the first one we find
                        break; 
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check for existing media players");
            }
        }

        private async Task AttachMediaPlayerAsync(ObjectPath objectPath)
        {
            try
            {
                if (_connection == null) return;

                // Skip if already attached to this exact player
                if (_mediaPlayerPath == objectPath && _mediaPlayer != null)
                {
                    _logger.LogDebug("Already attached to media player at {Path}, skipping", objectPath);
                    return;
                }

                _playerPropertiesWatcher?.Dispose();
                _mediaPlayerPath = objectPath;
                _mediaPlayer = _connection.CreateProxy<Linux.IMediaPlayer1>(Linux.BluezConstants.ServiceName, objectPath);
                
                _playerPropertiesWatcher = await _mediaPlayer.WatchPropertiesAsync(OnPlayerPropertiesChanged);
                
                // Get initial state
                try
                {
                    var status = await _mediaPlayer.GetAsync<string>("Status");
                    UpdatePlaybackStatus(status);
                    
                    var track = await _mediaPlayer.GetAsync<IDictionary<string, object>>("Track");
                    UpdateMetadata(track);
                }
                catch (Exception ex)
                {
                    // Properties might not be available yet
                    _logger.LogDebug($"Failed to get initial player state: {ex.Message}");
                }

                _logger.LogInformation($"Attached to Media Player at {objectPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to attach media player at {objectPath}");
            }
        }

        private void OnPlayerPropertiesChanged(PropertyChanges changes)
        {
            foreach (var prop in changes.Changed)
            {
                switch (prop.Key)
                {
                    case "Status":
                        UpdatePlaybackStatus(prop.Value as string);
                        break;
                    case "Track":
                         if (prop.Value is IDictionary<string, object> track)
                         {
                             UpdateMetadata(track);
                         }
                        break;
                    case "Position":
                         if (prop.Value is uint pos)
                         {
                             PositionChanged?.Invoke(this, TimeSpan.FromMilliseconds(pos));
                         }
                        break;
                }
            }
        }

        private void UpdatePlaybackStatus(string? statusStr)
        {
            if (string.IsNullOrEmpty(statusStr)) return;
            
            var status = statusStr.ToLower() switch
            {
                "playing" => BluetoothPlaybackStatus.Playing,
                "paused" => BluetoothPlaybackStatus.Paused,
                "stopped" => BluetoothPlaybackStatus.Stopped,
                "forward-seek" => BluetoothPlaybackStatus.ForwardSeek,
                "reverse-seek" => BluetoothPlaybackStatus.ReverseSeek,
                "error" => BluetoothPlaybackStatus.Error,
                _ => BluetoothPlaybackStatus.Stopped
            };
            
            PlaybackStatusChanged?.Invoke(this, status);
        }

        private void UpdateMetadata(IDictionary<string, object> track)
        {
            if (track == null) return;

            // MPRIS/BlueZ exposes album art via "ArtUrl" or "mpris:artUrl"
            var artUrl = GetString(track, "ArtUrl");
            if (string.IsNullOrEmpty(artUrl))
            {
                artUrl = GetString(track, "mpris:artUrl");
            }

            var meta = new BluetoothPlaybackMetadata
            {
                Title = GetString(track, "Title"),
                Artist = GetString(track, "Artist"),
                Album = GetString(track, "Album"),
                Duration = track.ContainsKey("Duration")
                    ? TimeSpan.FromMilliseconds(Convert.ToUInt32(track["Duration"]))
                    : TimeSpan.Zero,
                AlbumArtUrl = string.IsNullOrEmpty(artUrl) ? null : artUrl
            };

            _logger.LogDebug("AVRCP metadata: {Title} by {Artist} (album: {Album}, art: {HasArt})",
                meta.Title, meta.Artist, meta.Album, !string.IsNullOrEmpty(meta.AlbumArtUrl));

            MetadataChanged?.Invoke(this, meta);
        }

        private string GetString(IDictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var val) && val is string s)
            {
                return s;
            }
            return string.Empty;
        }
    }
}
