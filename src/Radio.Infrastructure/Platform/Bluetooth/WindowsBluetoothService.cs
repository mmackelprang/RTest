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
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Platform.Bluetooth;

internal sealed class WindowsBluetoothService : IBluetoothService
{
    private readonly ILogger _logger;
    private readonly BluetoothOptions _options;
    private readonly Radio.Infrastructure.Audio.SoundFlow.SoundFlowDeviceManager? _deviceManager;
    private BluetoothClient _client;
    // Suppressing unused warning for _radio as it might be needed for specific Win32 interactions later
    #pragma warning disable CS0169
    private BluetoothRadio? _radio;
    #pragma warning restore CS0169
    private readonly Timer _stateTimer;

    // InTheHand.Net doesn't have robust event-driven discovery in the same way, 
    // often relies on polling or blocking calls. We'll simulate async discovery.
    private CancellationTokenSource? _discoveryCts;

    public WindowsBluetoothService(
        ILogger logger, 
        IOptions<BluetoothOptions> options,
        Radio.Infrastructure.Audio.SoundFlow.SoundFlowDeviceManager? deviceManager = null)
    {
        _logger = logger;
        _options = options.Value;
        _deviceManager = deviceManager;
        _client = new BluetoothClient();
        
        // Poll for state changes
        _stateTimer = new Timer(CheckState, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
    }

    public bool IsAvailable => true; // Assume true on Windows; IsSupported/PrimaryRadio might not be static in 4.x

    public BluetoothAdapterState State { get; private set; } = BluetoothAdapterState.Unknown;

    public IReadOnlyList<Radio.Core.Interfaces.Audio.BluetoothDeviceInfo> PairedDevices
    {
        get
        {
            try
            {
                // In 4.x check if client exposes paired devices directly or query via discover
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

    public Radio.Core.Interfaces.Audio.BluetoothDeviceInfo? ConnectedDevice { get; private set; } // Tracking manually for now

    public event EventHandler<BluetoothAdapterStateChangedEventArgs>? StateChanged;
    public event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected { add { } remove { } }
    public event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected { add { } remove { } }
    public event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered;
    public event EventHandler<BluetoothPlaybackMetadata>? MetadataChanged { add { } remove { } }
    public event EventHandler<BluetoothPlaybackStatus>? PlaybackStatusChanged { add { } remove { } }
    public event EventHandler<TimeSpan>? PositionChanged { add { } remove { } }

    private void CheckState(object? state)
    {
        // In 4.x getting PrimaryRadio requires context usually, but there is a static method in some versions.
        // Simplified check:
            try 
            {
            // Just assume ON if no exception for now as 32feet API varies by version
            UpdateState(BluetoothAdapterState.On);
            } 
            catch 
            {
            UpdateState(BluetoothAdapterState.Off);
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
        // Windows manages radio state
        return Task.FromResult(true);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Cannot programmatically turn off Bluetooth radio on Windows easily/reliably via this library
        return Task.CompletedTask;
    }

    public async Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
    {
        if (IsDiscovering)
        {
            return;
        }
        IsDiscovering = true;
        _discoveryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = Task.Run(async () => 
        {
            try
            {
                // 4.x DiscoverDevicesAsync method check
                while (!_discoveryCts.Token.IsCancellationRequested)
                {
                    var devices = _client.DiscoverDevices(); // Sync block or minimal params
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
            return Task.FromResult(BluetoothSecurity.PairRequest(address, null));
        }
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
        ConnectedDevice = null;
        return Task.CompletedTask;
    }

    public Task<object?> GetAudioCaptureDeviceAsync(CancellationToken cancellationToken = default)
    {
            if (ConnectedDevice == null || _deviceManager == null)
        {
            return Task.FromResult<object?>(null);
        }

        // Attempt to find capture device matching connected bluetooth device name
        // Often "Bluetooth Something" or just the device name
        var deviceName = ConnectedDevice.Name;
        var captureId = _deviceManager.FindCaptureDeviceByName(deviceName);
        
        if (captureId != null)
        {
            // Return just the ID string for now (matches SoundFlowDeviceManager.FindCaptureDeviceByName return type)
            return Task.FromResult<object?>(captureId);
        }
        
        return Task.FromResult<object?>(null);
    }

    private Radio.Core.Interfaces.Audio.BluetoothDeviceInfo MapDevice(InTheHand.Net.Sockets.BluetoothDeviceInfo device)
    {
        return new Radio.Core.Interfaces.Audio.BluetoothDeviceInfo
        {
            Address = device.DeviceAddress.ToString(),
            Name = device.DeviceName,
            IsPaired = device.Authenticated,
            IsConnected = device.Connected,
            // LastSeen property has availability issues across 32feet versions
            LastConnected = DateTime.UtcNow
        };
    }

    public async ValueTask DisposeAsync()
    {
        _stateTimer?.Dispose();
        _discoveryCts?.Cancel();
        try
        {
            _client?.Dispose();
        }
        catch
        {
            // InTheHand.Net can throw NullReferenceException during dispose if not fully initialized (common in test environments)
        }
    }
}
