using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Platform.Bluetooth;

public sealed class MockBluetoothService : IBluetoothService
{
        private readonly ILogger _logger;
        
        public MockBluetoothService(ILogger<MockBluetoothService> logger)
        {
            _logger = logger;
        }

        public bool IsAvailable => true;
        public BluetoothAdapterState State { get; private set; } = BluetoothAdapterState.On;
        public IReadOnlyList<BluetoothDeviceInfo> PairedDevices => new List<BluetoothDeviceInfo>
        {
            new BluetoothDeviceInfo { Name = "Mock Speaker", Address = "00:11:22:33:44:55", IsPaired = true, IsConnected = false },
            new BluetoothDeviceInfo { Name = "Mock Phone", Address = "AA:BB:CC:DD:EE:FF", IsPaired = true, IsConnected = true }
        };
        
        public IReadOnlyList<BluetoothDeviceInfo> DiscoveredDevices => new List<BluetoothDeviceInfo>();
        public bool IsDiscovering { get; private set; }
        public BluetoothDeviceInfo? ConnectedDevice { get; set; }

        public bool IsAudioManagedByPlatform => false;

        // Suppress "event never used" warning for mocks
#pragma warning disable 67
        public event EventHandler<BluetoothAdapterStateChangedEventArgs>? StateChanged;
        public event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected;
        public event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected;
        public event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered;
#pragma warning restore 67
        public event EventHandler<BluetoothPlaybackMetadata>? MetadataChanged;
        public event EventHandler<BluetoothPlaybackStatus>? PlaybackStatusChanged;
        public event EventHandler<TimeSpan>? PositionChanged { add { } remove { } }
        public event EventHandler<BluetoothVolumeChangedEventArgs>? VolumeChanged { add { } remove { } }
        public event EventHandler? CaptureStreamRecovered;
        public event EventHandler<CaptureStreamStalledEventArgs>? CaptureStreamStalled { add { } remove { } }
        // Mock never raises CaptureNodeAvailable — the platform manages capture-node visibility.
        public event EventHandler<CaptureNodeAvailableEventArgs>? CaptureNodeAvailable { add { } remove { } }
        public float? DeviceVolume => null;
        public Task SetDeviceVolumeAsync(float volume) => Task.CompletedTask;
        public Task NextTrackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PreviousTrackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsReconnecting => false;
        public void CancelReconnection() { }
        public BluetoothDisconnectReason? LastDisconnectReason => null;
        public BluetoothPipelineStatus PipelineStatus => BluetoothPipelineStatus.Inactive;

        // A2DP codec observability — Mock implementation is a no-op (no real BT transport).
#pragma warning disable 67 // event never raised in mock
        public event EventHandler<A2dpCodecChangedEventArgs>? A2dpCodecChanged;
#pragma warning restore 67
        public Task<A2dpCodecInfo?> GetA2dpCodecInfoAsync(string deviceAddress, CancellationToken ct = default)
          => Task.FromResult<A2dpCodecInfo?>(null);

        public Task<bool> StartAsync(string deviceName, CancellationToken cancellationToken = default)
        {
            State = BluetoothAdapterState.On;
            return Task.FromResult(true);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            State = BluetoothAdapterState.Off;
            return Task.CompletedTask;
        }

        public Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
        {
            IsDiscovering = true;
            return Task.CompletedTask;
        }

        public Task StopDiscoveryAsync()
        {
            IsDiscovering = false;
            return Task.CompletedTask;
        }

        public Task<bool> PairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> UnpairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> AcceptConnectionAsync(string deviceAddress, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> ConnectAsync(string deviceAddress, CancellationToken cancellationToken = default)
        {
            var device = new BluetoothDeviceInfo
            {
                Name = "Mock Device",
                Address = deviceAddress,
                IsPaired = true,
                IsConnected = true
            };
            ConnectedDevice = device;
            DeviceConnected?.Invoke(this, new BluetoothDeviceConnectedEventArgs { Device = device });
            return Task.FromResult(true);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectedDevice = null;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(string deviceAddress, CancellationToken cancellationToken = default)
            => DisconnectAsync(cancellationToken);

        public Task<object?> GetAudioCaptureDeviceAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<object?>("mock-capture-endpoint");
        }

        public void StopAudioCapture() { }

        // Mock always reports the capture node as available — platform-equivalent stub.
        public Task<bool> IsCaptureNodeAvailableAsync(string deviceAddress, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
        
        // Mock helper to simulate metadata
        public void SimulateMetadataChange(string title, string artist, string? albumArtUrl = null)
        {
            MetadataChanged?.Invoke(this, new BluetoothPlaybackMetadata
            {
               Title = title,
               Artist = artist,
               Album = "Mock Album",
               AlbumArtUrl = albumArtUrl
            });
        }

        public void SimulatePlaybackStatusChange(BluetoothPlaybackStatus status)
        {
            PlaybackStatusChanged?.Invoke(this, status);
        }

        public void SimulateConnection(BluetoothDeviceInfo device)
        {
            ConnectedDevice = device;
            DeviceConnected?.Invoke(this, new BluetoothDeviceConnectedEventArgs { Device = device });
        }

        public void SimulateDisconnection(BluetoothDeviceInfo device, bool userInitiated = false)
        {
            ConnectedDevice = null;
            DeviceDisconnected?.Invoke(this, new BluetoothDeviceDisconnectedEventArgs { Device = device, UserInitiated = userInitiated });
        }

        // Mock helper to simulate the BT capture pipeline monitor raising recovery after a stall.
        public void SimulateCaptureStreamRecovered()
        {
            CaptureStreamRecovered?.Invoke(this, EventArgs.Empty);
        }
}
