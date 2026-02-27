using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Audio.Validation;
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
        private readonly SoundFlowDeviceManager? _deviceManager;
        private readonly SoundFlowPlaybackService? _playbackService;
        private readonly IMetricsCollector? _metricsCollector;
        private readonly IAudioValidator? _audioValidator;
        private Connection? _connection;
        private Linux.IObjectManager? _objectManager;
        private Linux.IAdapter1? _adapter;
        private IDisposable? _discoveryWatcher;
        private MiniAudioEngine? _captureEngine;
        private Process? _captureProcess;
        private CancellationTokenSource? _captureCts;
        private object? _activeGenerator;
        private string? _activeNodeName;
        private DateTime? _connectionStartTime;

        // Player tracking
        private Linux.IMediaPlayer1? _mediaPlayer;
        private ObjectPath? _mediaPlayerPath;
        private IDisposable? _playerPropertiesWatcher;

        // Media transport (AVRCP volume)
        private Linux.IMediaTransport1? _mediaTransport;
        private ObjectPath? _mediaTransportPath;
        private IDisposable? _transportPropertiesWatcher;

        // Maps object path to device info
        private readonly Dictionary<ObjectPath, BluetoothDeviceInfo> _deviceCache = new();
        private readonly HashSet<ObjectPath> _watchedDevicePaths = new();
        private readonly SemaphoreSlim _captureDeviceLock = new(1, 1);
        private readonly object _mediaPlayerLock = new();
        private bool _started;
        private Linux.BluezAgent? _agent;

        public LinuxBluetoothService(
            ILogger logger,
            IOptions<BluetoothOptions> options,
            SoundFlowDeviceManager? deviceManager = null,
            IMetricsCollector? metricsCollector = null,
            SoundFlowPlaybackService? playbackService = null,
            IAudioValidator? audioValidator = null)
        {
            _logger = logger;
            _options = options.Value;
            _deviceManager = deviceManager;
            _metricsCollector = metricsCollector;
            _playbackService = playbackService;
            _audioValidator = audioValidator;
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
        public event EventHandler<BluetoothVolumeChangedEventArgs>? VolumeChanged;
        public float? DeviceVolume { get; private set; }

        public async Task SetDeviceVolumeAsync(float volume)
        {
            if (_mediaTransport == null)
            {
                _logger.LogDebug("No media transport attached, cannot set BT volume");
                return;
            }

            try
            {
                var bluezVolume = (ushort)Math.Clamp((int)(volume * 127f), 0, 127);
                await _mediaTransport.SetAsync("Volume", bluezVolume);
                DeviceVolume = volume;
                _logger.LogDebug("Set BT AVRCP volume to {Volume:P0} (BlueZ: {Raw}/127)", volume, bluezVolume);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set BT AVRCP volume via MediaTransport1");
            }
        }

        public async Task NextTrackAsync(CancellationToken cancellationToken = default)
        {
            if (_mediaPlayer == null)
            {
                _logger.LogWarning("No MPRIS media player attached, cannot skip to next track. " +
                    "MediaPlayer D-Bus interface not found — phone may not expose AVRCP controller");
                return;
            }
            try
            {
                await _mediaPlayer.NextAsync();
                _logger.LogInformation("AVRCP: Sent Next command via D-Bus ({Path})", _mediaPlayerPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send AVRCP Next command via D-Bus ({Path})", _mediaPlayerPath);
            }
        }

        public async Task PreviousTrackAsync(CancellationToken cancellationToken = default)
        {
            if (_mediaPlayer == null)
            {
                _logger.LogWarning("No MPRIS media player attached, cannot go to previous track. " +
                    "MediaPlayer D-Bus interface not found — phone may not expose AVRCP controller");
                return;
            }
            try
            {
                await _mediaPlayer.PreviousAsync();
                _logger.LogInformation("AVRCP: Sent Previous command via D-Bus ({Path})", _mediaPlayerPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send AVRCP Previous command via D-Bus ({Path})", _mediaPlayerPath);
            }
        }

        public async Task<bool> StartAsync(string deviceName, CancellationToken cancellationToken = default)
        {
            if (_started)
            {
                _logger.LogDebug("Bluetooth service already started, skipping duplicate StartAsync call");
                return IsAvailable;
            }
            _started = true;

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
            StopCaptureSubprocess();
            _captureEngine?.Dispose();
            _captureEngine = null;

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

            _started = false;
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

            if (change.interfaces.ContainsKey(Linux.BluezConstants.MediaTransportInterface))
            {
                _ = AttachMediaTransportAsync(change.objectPath);
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
                                StopCaptureSubprocess();
                                // Clean up media transport on disconnect
                                _transportPropertiesWatcher?.Dispose();
                                _transportPropertiesWatcher = null;
                                _mediaTransport = null;
                                _mediaTransportPath = null;
                                DeviceVolume = null;

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

            // If capture subprocess is already running, return cached generator
            if (_captureProcess != null && !_captureProcess.HasExited && _activeGenerator != null)
            {
                _logger.LogDebug("Returning existing capture generator (PID {Pid})", _captureProcess.Id);
                return _activeGenerator;
            }

            // Wait for any concurrent search to complete (with timeout instead of zero-wait).
            // Multiple callers race here: TryAcquireAudioCaptureAsync (from DeviceConnected event)
            // and InitializeAsync (from auto-switch). Second caller should wait and get cached result.
            if (!await _captureDeviceLock.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
            {
                _logger.LogWarning("Timeout waiting for capture device lock");
                return null;
            }

            try
            {
                // Re-check after acquiring lock — another caller may have completed
                if (_captureProcess != null && !_captureProcess.HasExited && _activeGenerator != null)
                {
                    _logger.LogDebug("Capture already active after lock wait (PID {Pid})", _captureProcess.Id);
                    return _activeGenerator;
                }

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
            // Cleanup previous capture
            StopCaptureSubprocess();
            _captureEngine?.Dispose();
            _captureEngine = null;

            if (_playbackService == null)
            {
                _logger.LogError("SoundFlowPlaybackService not available — cannot create BT capture bridge");
                return null;
            }

            var engine = _playbackService.GetUnderlyingEngine();
            var format = _playbackService.GetAudioFormat();
            if (engine == null)
            {
                _logger.LogError("SoundFlow engine not available — cannot create BT capture bridge");
                return null;
            }

            // Use pw-record to capture directly from the PipeWire bluez input node.
            // PipeWire creates a node named "bluez_input.<ADDRESS>.<N>" when a BT A2DP
            // source connects. pw-record --target captures from it directly — no ALSA
            // bridge, .asoundrc, or null sink configuration needed.
            const int maxRetries = 20;
            const int retryDelayMs = 1000;

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ConnectedDevice == null)
                {
                    _logger.LogWarning("Bluetooth device disconnected during capture device polling");
                    return null;
                }

                try
                {
                    // Find the PipeWire node name for this BT device
                    var (nodeName, nodeId, nodeSerial) = await FindPipeWireBluetoothNodeAsync(connected.Address, cancellationToken);
                    if (nodeName == null)
                    {
                        _logger.LogDebug("PipeWire BT node not found for {Address} (attempt {Attempt}/{Max})",
                            connected.Address, attempt, maxRetries);
                        if (attempt < maxRetries)
                            await Task.Delay(retryDelayMs, cancellationToken);
                        continue;
                    }

                    _logger.LogInformation("Found PipeWire BT node: {Node} (id={NodeId}, serial={Serial}, attempt {Attempt})",
                        nodeName, nodeId, nodeSerial, attempt);

                    var generator = new BufferedSoundGenerator<float>(
                        engine, format, _logger, maxBufferSeconds: 2.0f,
                        metricsCollector: _metricsCollector,
                        audioValidator: _audioValidator);

                    StartCaptureSubprocess(generator, format, nodeName, nodeSerial);

                    // Give pw-record a moment to connect and verify it's running
                    await Task.Delay(500, cancellationToken);

                    if (_captureProcess != null && !_captureProcess.HasExited)
                    {
                        _logger.LogInformation(
                            "pw-record capture running (attempt {Attempt}/{Max}, PID {Pid}, target {Node})",
                            attempt, maxRetries, _captureProcess.Id, nodeName);
                        _activeGenerator = generator;
                        _activeNodeName = nodeName;

                        // Set BT source node master volume to 1.0 AFTER pw-record is running
                        // and AVRCP volume sync has settled. BlueZ sets the PipeWire node volume
                        // to the phone's AVRCP transport volume (~0.11) during connection setup.
                        // If we set volume too early, AVRCP resets it. A 1s delay lets AVRCP
                        // finish, then we override with full volume for capture.
                        // Also disconnect PipeWire's auto-link from BT input to the default
                        // output sink — WirePlumber creates this automatically when a BT device
                        // connects, causing a quiet echo (duplicate audio path to speakers).
                        // Disconnect auto-link immediately (WirePlumber may have already created it)
                        // CRITICAL: WirePlumber overrides pw-record's --target flag and links
                        // it to the default audio source (e.g. USB mic) instead of the BT node.
                        // We must: (1) disconnect BT auto-links to speakers, (2) manually link
                        // pw-record inputs to BT node outputs. Repeat after delays because
                        // WirePlumber re-creates links during AVRCP/A2DP setup.
                        DisconnectPipeWireBtAutoLinks(nodeName);
                        LinkPipeWireRecordToBtNode(nodeName);

                        var capturedNodeName = nodeName;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                // Delayed re-link: WirePlumber re-creates links during AVRCP setup
                                await Task.Delay(1000, cancellationToken);
                                await SetPipeWireNodeVolumeAsync(nodeId);
                                DisconnectPipeWireBtAutoLinks(capturedNodeName);
                                LinkPipeWireRecordToBtNode(capturedNodeName);

                                // Third attempt: WirePlumber can be slow on some BT codecs
                                await Task.Delay(2000, cancellationToken);
                                DisconnectPipeWireBtAutoLinks(capturedNodeName);
                                LinkPipeWireRecordToBtNode(capturedNodeName);

                                // Fourth attempt: final stabilization
                                await Task.Delay(3000, cancellationToken);
                                DisconnectPipeWireBtAutoLinks(capturedNodeName);
                                LinkPipeWireRecordToBtNode(capturedNodeName);

                                // Continuous link monitor: BT transport resets destroy and
                                // recreate PipeWire nodes with new serials, causing all links
                                // to be lost. WirePlumber re-creates its default links (BT→speakers)
                                // but our pw-record links are not restored. Poll every 10s and
                                // re-link if pw-record is no longer connected to the BT node.
                                while (!cancellationToken.IsCancellationRequested)
                                {
                                    await Task.Delay(10_000, cancellationToken);
                                    if (!IsPwRecordLinkedToBtNode(capturedNodeName))
                                    {
                                        _logger.LogWarning("pw-record lost link to BT node {BtNode}, re-linking", capturedNodeName);
                                        DisconnectAllLinksToPort("pw-record:input_FL");
                                        DisconnectAllLinksToPort("pw-record:input_FR");
                                        LinkPipeWireRecordToBtNode(capturedNodeName);
                                    }
                                    DisconnectPipeWireBtAutoLinks(capturedNodeName);
                                }
                            }
                            catch (OperationCanceledException) { }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Deferred volume/link setup failed for node {NodeId}", nodeId);
                            }
                        }, cancellationToken);

                        return generator;
                    }

                    // pw-record exited immediately — read stderr for diagnostics
                    var pwStderr = "";
                    try { pwStderr = _captureProcess?.StandardError.ReadToEnd() ?? ""; }
                    catch { /* ignore */ }
                    _logger.LogDebug("pw-record exited early (attempt {Attempt}/{Max}): {Stderr}",
                        attempt, maxRetries, pwStderr.Length > 200 ? pwStderr[..200] : pwStderr.TrimEnd());
                    StopCaptureSubprocess();
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "BT capture attempt {Attempt} failed", attempt);
                }

                if (attempt < maxRetries)
                {
                    await Task.Delay(retryDelayMs, cancellationToken);
                }
            }

            _logger.LogWarning("BT capture device not accessible after {MaxRetries} attempts", maxRetries);
            _metricsCollector?.Increment("bluetooth.audio_capture_errors");
            return null;
        }

        /// <summary>
        /// Queries PipeWire for a bluez_input node matching the given BT address.
        /// Returns (nodeName, pipeWireId) or (null, 0) if not found.
        /// </summary>
        private async Task<(string? NodeName, int PipeWireId, int PipeWireSerial)> FindPipeWireBluetoothNodeAsync(
            string btAddress, CancellationToken cancellationToken)
        {
            // PipeWire names use underscores: "D4:3A:2C:64:87:9E" → "D4_3A_2C_64_87_9E"
            var addressUnderscored = btAddress.Replace(':', '_');
            var prefix = $"bluez_input.{addressUnderscored}";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pw-cli",
                    Arguments = "list-objects",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    _logger.LogWarning("Failed to start pw-cli process");
                    return (null, 0, 0);
                }

                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0 || output.Length == 0)
                {
                    _logger.LogDebug("pw-cli exit={ExitCode}, stdout={StdoutLen}b, stderr={Stderr}",
                        process.ExitCode, output.Length,
                        stderr.Length > 200 ? stderr[..200] : stderr.TrimEnd());
                    return (null, 0, 0);
                }

                // Parse pw-cli output for node.name matching our prefix.
                // Format: "id 68, type PipeWire:Interface:Node/3" followed by properties.
                // We need: node.name, object.id, and object.serial.
                // pw-record --target accepts the serial (not the object id).
                var lines = output.Split('\n');
                var lastNodeId = 0;
                string? matchedNodeName = null;
                var matchedNodeId = 0;
                var inMatchedNode = false;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();

                    // Track the current object ID: "id 68, type PipeWire:Interface:Node/3"
                    if (trimmed.StartsWith("id ") && trimmed.Contains(", type PipeWire:Interface:Node"))
                    {
                        var commaIdx = trimmed.IndexOf(',');
                        if (commaIdx > 3 && int.TryParse(trimmed[3..commaIdx], out var id))
                            lastNodeId = id;
                        inMatchedNode = false;
                    }

                    if (trimmed.StartsWith("node.name = ") && trimmed.Contains(prefix))
                    {
                        // Extract: node.name = "bluez_input.D4_3A_2C_64_87_9E.2"
                        var start = trimmed.IndexOf('"') + 1;
                        var end = trimmed.LastIndexOf('"');
                        if (start > 0 && end > start)
                        {
                            matchedNodeName = trimmed[start..end];
                            matchedNodeId = lastNodeId;
                            inMatchedNode = true;
                        }
                    }

                    // Extract object.serial from the matched node's properties
                    if (inMatchedNode && trimmed.StartsWith("object.serial = "))
                    {
                        var start = trimmed.IndexOf('"') + 1;
                        var end = trimmed.LastIndexOf('"');
                        if (start > 0 && end > start && int.TryParse(trimmed[start..end], out var serial))
                            return (matchedNodeName, matchedNodeId, serial);
                    }
                }

                // If we found the node but not the serial, return with serial=0
                if (matchedNodeName != null)
                    return (matchedNodeName, matchedNodeId, 0);

                _logger.LogDebug("pw-cli returned {Lines} lines but no node matching {Prefix}",
                    lines.Length, prefix);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query PipeWire for BT node");
            }

            return (null, 0, 0);
        }

        /// <summary>
        /// Sets the PipeWire node's master volume to 1.0 so pw-record captures at full level.
        /// BT A2DP source nodes inherit the AVRCP transport volume from the phone (often ~0.11),
        /// which causes very faint capture audio. We override the node-level "volume" property
        /// via pw-cli set-param (NOT wpctl set-volume, which only sets channelVolumes).
        /// </summary>
        private async Task SetPipeWireNodeVolumeAsync(int pipeWireNodeId)
        {
            if (pipeWireNodeId <= 0) return;

            try
            {
                // pw-cli set-param sets the node's master volume property directly.
                // wpctl set-volume only changes channelVolumes, not the master volume
                // that BlueZ/AVRCP sets — so we must use pw-cli here.
                var psi = new ProcessStartInfo
                {
                    FileName = "pw-cli",
                    Arguments = $"set-param {pipeWireNodeId} Props '{{\"volume\": 1.0}}'",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0)
                    {
                        _logger.LogInformation(
                            "Set PipeWire BT node {NodeId} master volume to 1.0 for full capture level", pipeWireNodeId);
                    }
                    else
                    {
                        var stderr = await process.StandardError.ReadToEndAsync();
                        _logger.LogDebug("pw-cli set-param {NodeId} failed: exit={ExitCode}, {Stderr}",
                            pipeWireNodeId, process.ExitCode, stderr.TrimEnd());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to set PipeWire node volume for {NodeId}", pipeWireNodeId);
            }
        }

        private void StartCaptureSubprocess(
            BufferedSoundGenerator<float> generator, AudioFormat format, string targetNode,
            int targetSerial = 0)
        {
            StopCaptureSubprocess();

            _captureCts = new CancellationTokenSource();
            var ct = _captureCts.Token;

            // Pre-fill the buffer with silence before pw-record starts delivering data.
            // This provides a cushion so the mixer has audio to consume while the capture
            // subprocess spins up and during periodic PipeWire graph processing stalls.
            // Same pattern used by SDR radio source for USB transfer jitter absorption.
            generator.PreFillSilence(0.5f);

            // pw-record captures directly from a PipeWire node — no ALSA config needed.
            // Output format: raw S16_LE stereo at 48kHz to stdout.
            // Wrap with stdbuf -o0 to disable glibc stdout buffering — without this,
            // pw-record uses full buffering when piped (64KB blocks), causing data to
            // arrive in bursts which starves the ring buffer and produces periodic
            // ~1s audio artifacts every 15-30s.
            //
            // CRITICAL: Use --target with the node serial (not name) to pin pw-record
            // to the specific BT node. Without this, WirePlumber's autoconnect policy
            // can relink pw-record to the default audio source (e.g. USB mic) instead
            // of the BT node, causing silence or wrong audio capture.
            var targetArg = targetSerial > 0 ? targetSerial.ToString() : targetNode;
            _captureProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "stdbuf",
                Arguments = $"-o0 pw-record --target {targetArg} --rate 48000 --channels 2 --format s16 -",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (_captureProcess == null)
            {
                _logger.LogError("Failed to start pw-record subprocess");
                return;
            }

            _logger.LogInformation("Started pw-record capture (PID {Pid}, target {Node})",
                _captureProcess.Id, targetNode);

            // Background task to read S16_LE data and feed float samples to generator
            _ = Task.Run(async () =>
            {
                // ~100ms of S16 stereo at 48kHz = 19,200 bytes
                const int readBufferSize = 48000 * 2 * 2 / 10;
                var buffer = new byte[readBufferSize];
                var stream = _captureProcess.StandardOutput.BaseStream;
                var pendingByte = -1; // Tracks a leftover byte from odd-length reads

                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        // If we had a leftover byte from a previous odd-length read,
                        // place it at the start of the buffer so it pairs with the next byte
                        var offset = 0;
                        if (pendingByte >= 0)
                        {
                            buffer[0] = (byte)pendingByte;
                            offset = 1;
                            pendingByte = -1;
                        }

                        var bytesRead = await stream.ReadAsync(buffer, offset, buffer.Length - offset, ct);
                        if (bytesRead == 0) break; // EOF — process exited

                        var totalBytes = offset + bytesRead;

                        // S16_LE = 2 bytes per sample. If we have an odd total, save
                        // the trailing byte for the next iteration to maintain alignment.
                        // Without this, MemoryMarshal.Cast drops the byte, shifting all
                        // subsequent samples by 1 byte → corrupted audio.
                        if (totalBytes % 2 != 0)
                        {
                            pendingByte = buffer[totalBytes - 1];
                            totalBytes--;
                        }

                        if (totalBytes < 2) continue;

                        // Convert S16_LE samples to float [-1.0, 1.0]
                        var sampleCount = totalBytes / 2;
                        var floatSamples = ArrayPool<float>.Shared.Rent(sampleCount);
                        try
                        {
                            var shorts = MemoryMarshal.Cast<byte, short>(buffer.AsSpan(0, totalBytes));
                            for (var i = 0; i < shorts.Length; i++)
                            {
                                floatSamples[i] = shorts[i] / 32768f;
                            }

                            generator.AddSamples(floatSamples.AsSpan(0, sampleCount));

                            // V1-BTCapture: submit decoded BT audio for validation
                            _audioValidator?.Submit(floatSamples.AsSpan(0, sampleCount), "V1-BTCapture");
                        }
                        finally
                        {
                            ArrayPool<float>.Shared.Return(floatSamples);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "pw-record capture feed loop ended");
                }

                _logger.LogInformation("pw-record capture feed loop stopped");
            }, ct);
        }

        public void StopAudioCapture()
        {
            StopCaptureSubprocess();
        }

        private void StopCaptureSubprocess()
        {
            _captureCts?.Cancel();
            _captureCts?.Dispose();
            _captureCts = null;
            _activeGenerator = null;

            if (_captureProcess != null)
            {
                try
                {
                    if (!_captureProcess.HasExited)
                    {
                        _captureProcess.Kill();
                        _captureProcess.WaitForExit(2000);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error stopping capture subprocess");
                }
                _captureProcess.Dispose();
                _captureProcess = null;
            }

            // Disconnect PipeWire's auto-link from the BT input node to the default
            // output sink. PipeWire/WirePlumber automatically links bluez_input to the
            // default audio output, which bypasses our application entirely. When we
            // switch away from BT, we must sever this link so only the active source
            // (routed through our SoundFlow mixer) reaches the speakers.
            if (_activeNodeName != null)
            {
                DisconnectPipeWireBtAutoLinks(_activeNodeName);
                _activeNodeName = null;
            }
        }

        /// <summary>
        /// Manually links pw-record's input ports to the BT node's output ports.
        /// WirePlumber overrides pw-record's --target flag and links it to the default
        /// audio source instead. This method: (1) disconnects any existing links to
        /// pw-record inputs, (2) creates explicit links from the BT node outputs.
        /// </summary>
        private void LinkPipeWireRecordToBtNode(string btNodeName)
        {
            try
            {
                // Disconnect whatever WirePlumber linked to pw-record
                RunPipeWireLinkCommand($"-d alsa_input.usb-Generic_USB_Microphone_IM20000001-00.analog-stereo:capture_FL pw-record:input_FL");
                RunPipeWireLinkCommand($"-d alsa_input.usb-Generic_USB_Microphone_IM20000001-00.analog-stereo:capture_FR pw-record:input_FR");

                // Also try disconnecting from any other source that WirePlumber might link
                // (Built-in Audio, other USB devices, etc.) by querying current links
                DisconnectAllLinksToPort("pw-record:input_FL");
                DisconnectAllLinksToPort("pw-record:input_FR");

                // Create explicit links from BT node to pw-record
                RunPipeWireLinkCommand($"{btNodeName}:output_FL pw-record:input_FL");
                RunPipeWireLinkCommand($"{btNodeName}:output_FR pw-record:input_FR");

                _logger.LogInformation("Linked pw-record to BT node {BtNode}", btNodeName);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to link pw-record to BT node {BtNode}", btNodeName);
            }
        }

        /// <summary>
        /// Disconnects all links connected to the given input port.
        /// </summary>
        private void DisconnectAllLinksToPort(string inputPort)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pw-link",
                    Arguments = "-l",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return;
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);

                // Parse: "portname\n  |<- sourceport" format
                var lines = output.Split('\n');
                var inTargetPort = false;

                foreach (var rawLine in lines)
                {
                    var line = rawLine.TrimEnd();

                    if (line.TrimStart() == inputPort || line.Trim() == inputPort)
                    {
                        inTargetPort = true;
                        continue;
                    }

                    if (!line.StartsWith("  ") && line.Length > 0)
                    {
                        inTargetPort = false;
                        continue;
                    }

                    if (inTargetPort && line.Contains("|<-"))
                    {
                        var sourcePort = line[(line.IndexOf("|<-") + 4)..].Trim();
                        RunPipeWireLinkCommand($"-d {sourcePort} {inputPort}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to disconnect links to {Port}", inputPort);
            }
        }

        /// <summary>
        /// Runs a pw-link command with the given arguments.
        /// </summary>
        private void RunPipeWireLinkCommand(string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "pw-link",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                process?.WaitForExit(3000);
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Checks whether pw-record is currently linked to the specified BT node.
        /// Returns false if pw-record inputs are linked to a different source or not linked at all.
        /// </summary>
        private bool IsPwRecordLinkedToBtNode(string btNodeName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pw-link",
                    Arguments = "-l",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return true; // assume ok if we can't check
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);

                // Look for "pw-record:input_FL" section with "|<- btNodeName:output_FL"
                var lines = output.Split('\n');
                var inPwRecordFL = false;

                foreach (var rawLine in lines)
                {
                    var line = rawLine.TrimEnd();
                    if (line.Trim() == "pw-record:input_FL")
                    {
                        inPwRecordFL = true;
                        continue;
                    }
                    if (inPwRecordFL)
                    {
                        if (line.Contains("|<-") && line.Contains(btNodeName))
                            return true;
                        if (!line.StartsWith("  ") && line.Length > 0)
                            break; // moved to next port, didn't find our link
                    }
                }

                return false;
            }
            catch
            {
                return true; // assume ok if we can't check
            }
        }

        /// <summary>
        /// Disconnects PipeWire's automatic links from a bluez_input node to the
        /// default audio output sink, preventing BT audio from bypassing our mixer.
        /// </summary>
        private void DisconnectPipeWireBtAutoLinks(string btNodeName)
        {
            try
            {
                // Query current links and find ones from this BT node to any output sink
                var psi = new ProcessStartInfo
                {
                    FileName = "pw-link",
                    Arguments = "-l",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);

                // Parse links: find output ports of the BT node that connect to a sink
                // Format: "  |-> alsa_output.xxx:playback_FL"
                // We look for lines under the BT node's output ports
                var lines = output.Split('\n');
                var inBtNode = false;
                string? currentBtPort = null;

                foreach (var rawLine in lines)
                {
                    var line = rawLine.TrimEnd();

                    // Detect BT node output port headers (no leading whitespace beyond node name)
                    if (line.StartsWith(btNodeName + ":"))
                    {
                        inBtNode = true;
                        currentBtPort = line.TrimEnd();
                        continue;
                    }

                    // Lines under a different node reset the flag
                    if (!line.StartsWith(" ") && line.Length > 0)
                    {
                        inBtNode = false;
                        currentBtPort = null;
                        continue;
                    }

                    // Under BT node, find links to output sinks
                    if (inBtNode && currentBtPort != null && line.Contains("|->"))
                    {
                        var targetPort = line.Substring(line.IndexOf("|->") + 4).Trim();
                        if (targetPort.StartsWith("alsa_output."))
                        {
                            // Disconnect this link
                            DisconnectPipeWireLink(currentBtPort, targetPort);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to disconnect PipeWire BT auto-links for {NodeName}", btNodeName);
            }
        }

        /// <summary>
        /// Disconnects a single PipeWire link between two ports.
        /// </summary>
        private void DisconnectPipeWireLink(string outputPort, string inputPort)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pw-link",
                    Arguments = $"-d \"{outputPort}\" \"{inputPort}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return;

                process.WaitForExit(3000);

                _logger.LogInformation(
                    "Disconnected PipeWire auto-link: {Output} -> {Input}",
                    outputPort, inputPort);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to disconnect PipeWire link {Output} -> {Input}",
                    outputPort, inputPort);
            }
        }

        private static DeviceInfo? FindBluetoothCaptureDevice(DeviceInfo[] devices, string connectedName)
        {
            // Strategy 1: Match the bt_capture null sink monitor.
            // PipeWire config creates a virtual null sink called "bt_capture" and
            // WirePlumber routes bluez_input streams to it. The monitor source
            // (bt_capture.monitor) appears as a capture device in PulseAudio/MiniAudio.
            foreach (var device in devices)
            {
                if (device.Name != null && device.Name.Contains("bt_capture", StringComparison.OrdinalIgnoreCase))
                    return device;
            }

            // Strategy 2: Match by "bluez" prefix (direct PipeWire Bluetooth source)
            foreach (var device in devices)
            {
                if (device.Name != null && device.Name.Contains("bluez", StringComparison.OrdinalIgnoreCase))
                    return device;
            }

            // Strategy 3: Match by connected device name
            foreach (var device in devices)
            {
                if (device.Name != null && device.Name.Contains(connectedName, StringComparison.OrdinalIgnoreCase))
                    return device;
            }

            // Strategy 4: Match by "bluetooth" keyword
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
            _transportPropertiesWatcher?.Dispose();
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
                    if (obj.Value.ContainsKey(Linux.BluezConstants.MediaPlayerInterface) && _mediaPlayer == null)
                    {
                        await AttachMediaPlayerAsync(obj.Key);
                    }

                    if (obj.Value.ContainsKey(Linux.BluezConstants.MediaTransportInterface) && _mediaTransport == null)
                    {
                        await AttachMediaTransportAsync(obj.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check for existing media players/transports");
            }
        }

        private async Task AttachMediaPlayerAsync(ObjectPath objectPath)
        {
            try
            {
                if (_connection == null) return;

                // Thread-safe dedup — multiple concurrent InterfacesAdded callbacks
                // can race into this method for the same player path
                lock (_mediaPlayerLock)
                {
                    if (_mediaPlayerPath == objectPath && _mediaPlayer != null)
                    {
                        _logger.LogDebug("Already attached to media player at {Path}, skipping", objectPath);
                        return;
                    }

                    _playerPropertiesWatcher?.Dispose();
                    _mediaPlayerPath = objectPath;
                }
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

        private async Task AttachMediaTransportAsync(ObjectPath objectPath)
        {
            try
            {
                if (_connection == null) return;

                if (_mediaTransportPath == objectPath && _mediaTransport != null)
                {
                    _logger.LogDebug("Already attached to media transport at {Path}", objectPath);
                    return;
                }

                _transportPropertiesWatcher?.Dispose();
                _mediaTransportPath = objectPath;
                _mediaTransport = _connection.CreateProxy<Linux.IMediaTransport1>(
                    Linux.BluezConstants.ServiceName, objectPath);

                _transportPropertiesWatcher = await _mediaTransport.WatchPropertiesAsync(OnTransportPropertiesChanged);

                // Read initial volume
                try
                {
                    var volume = await _mediaTransport.GetAsync<ushort>("Volume");
                    var normalized = volume / 127f;
                    DeviceVolume = normalized;
                    _logger.LogDebug("Initial BT transport volume: {Raw}/127 ({Normalized:P0})", volume, normalized);
                }
                catch
                {
                    // Volume property might not be available
                }

                _logger.LogInformation("Attached to MediaTransport1 at {Path}", objectPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to attach media transport at {Path}", objectPath);
            }
        }

        private void OnTransportPropertiesChanged(PropertyChanges changes)
        {
            foreach (var prop in changes.Changed)
            {
                if (prop.Key == "Volume" && prop.Value is ushort volume)
                {
                    var normalized = volume / 127f;
                    DeviceVolume = normalized;
                    VolumeChanged?.Invoke(this, new BluetoothVolumeChangedEventArgs { Volume = normalized });
                    _logger.LogDebug("BT AVRCP volume changed: {Raw}/127 ({Normalized:P0})", volume, normalized);
                }
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
