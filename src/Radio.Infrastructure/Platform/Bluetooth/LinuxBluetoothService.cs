using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Tmds.DBus;

namespace Radio.Infrastructure.Platform.Bluetooth
{
    internal sealed class LinuxBluetoothService : IBluetoothService
    {
        private readonly ILogger _logger;
        private readonly BluetoothOptions _options;
        private Connection? _connection;
        private Linux.IObjectManager? _objectManager;
        private Linux.IAdapter1? _adapter;
        private IDisposable? _discoveryWatcher;
        
        // Player tracking
        private Linux.IMediaPlayer1? _mediaPlayer;
        private IDisposable? _playerPropertiesWatcher;
        
        // Maps object path to device info
        private readonly Dictionary<ObjectPath, BluetoothDeviceInfo> _deviceCache = new();
        
        public LinuxBluetoothService(ILogger logger, IOptions<BluetoothOptions> options)
        {
            _logger = logger;
            _options = options.Value;
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
        public event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected { add { } remove { } }
        public event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected { add { } remove { } }
        public event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<BluetoothPlaybackMetadata>? MetadataChanged;
        public event EventHandler<BluetoothPlaybackStatus>? PlaybackStatusChanged;
        public event EventHandler<TimeSpan>? PositionChanged;

        public async Task<bool> StartAsync(string deviceName, CancellationToken cancellationToken = default)
        {
            try
            {
                _connection = Connection.System;
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
                
                // Watch for new devices
                _discoveryWatcher = await _objectManager.WatchInterfacesAddedAsync(OnInterfaceAdded);

                // Also scan for existing players if any
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
            }

            if (change.interfaces.ContainsKey(Linux.BluezConstants.MediaPlayerInterface))
            {
                _ = AttachMediaPlayerAsync(change.objectPath);
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
            return await Task.FromResult(false);
        }

        public async Task<bool> UnpairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
        {
            return await Task.FromResult(false);
        }

        public Task<bool> AcceptConnectionAsync(string deviceAddress, CancellationToken cancellationToken = default)
        {
            // BlueZ handles incoming connections automatically if Pairable is true
            return Task.FromResult(true);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
             // Disconnect currently connected device
             await Task.CompletedTask;
        }

        public Task<object?> GetAudioCaptureDeviceAsync(CancellationToken cancellationToken = default)
        {
            // In a real Linux PipeWire setup, we'd find the monitor source for the bluetooth device
            // Here we return a capture device that might be "bluez_output.*.monitor"
            // For now, we simulate finding the correct device based on connected device name if available
            // but SoundFlowDeviceManager helper isn't directly accessible here without injection.
            // Simplified: return null or a known test loopback.
            // TODO: Inject SoundFlowDeviceManager or similar factory to finding capture devices
            return Task.FromResult<object?>(null);
        }

        public async ValueTask DisposeAsync()
        {
            _playerPropertiesWatcher?.Dispose();
            _discoveryWatcher?.Dispose();
            await StopAsync();
            _connection?.Dispose();
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
                
                _playerPropertiesWatcher?.Dispose();
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
            
            var meta = new BluetoothPlaybackMetadata
            {
                Title = GetString(track, "Title"),
                Artist = GetString(track, "Artist"),
                Album = GetString(track, "Album"),
                Duration = track.ContainsKey("Duration") ? TimeSpan.FromMilliseconds(Convert.ToUInt32(track["Duration"])) : TimeSpan.Zero
            };
            
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
