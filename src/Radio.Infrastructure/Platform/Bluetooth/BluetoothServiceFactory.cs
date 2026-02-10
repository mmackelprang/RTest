using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Platform.Bluetooth;

public static class BluetoothServiceFactory
{
  public static IBluetoothService Create(
      IServiceProvider serviceProvider,
      IOptions<BluetoothOptions> options,
      ILoggerFactory loggerFactory,
      Radio.Infrastructure.Audio.SoundFlow.SoundFlowDeviceManager? deviceManager,
      IMetricsCollector? metricsCollector = null)
  {
      if (!options.Value.Enabled)
      {
          return new NullBluetoothService();
      }

      if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
      {
          return new LinuxBluetoothService(loggerFactory.CreateLogger<LinuxBluetoothService>(), options, deviceManager, metricsCollector);
      }
      else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      {
          return new WindowsBluetoothService(loggerFactory.CreateLogger<WindowsBluetoothService>(), options, deviceManager, metricsCollector);
      }
      else
      {
          // Fallback or platform specific implementation
          return new NullBluetoothService();
      }
  }
}

internal sealed class NullBluetoothService : IBluetoothService
{
  public bool IsAvailable => false;

  public BluetoothAdapterState State => BluetoothAdapterState.Unknown;

  public IReadOnlyList<BluetoothDeviceInfo> PairedDevices => Array.Empty<BluetoothDeviceInfo>();

  public IReadOnlyList<BluetoothDeviceInfo> DiscoveredDevices => Array.Empty<BluetoothDeviceInfo>();

  public bool IsDiscovering => false;

  public BluetoothDeviceInfo? ConnectedDevice => null;

  public event EventHandler<BluetoothAdapterStateChangedEventArgs>? StateChanged { add { } remove { } }
  public event EventHandler<BluetoothDeviceConnectedEventArgs>? DeviceConnected { add { } remove { } }
  public event EventHandler<BluetoothDeviceDisconnectedEventArgs>? DeviceDisconnected { add { } remove { } }
  public event EventHandler<BluetoothDeviceDiscoveredEventArgs>? DeviceDiscovered { add { } remove { } }
  public event EventHandler<BluetoothPlaybackMetadata>? MetadataChanged { add { } remove { } }
  public event EventHandler<BluetoothPlaybackStatus>? PlaybackStatusChanged { add { } remove { } }
  public event EventHandler<TimeSpan>? PositionChanged { add { } remove { } }

  public Task<bool> StartAsync(string deviceName, CancellationToken cancellationToken = default)
  {
      return Task.FromResult(false);
  }

  public Task StopAsync(CancellationToken cancellationToken = default)
  {
      return Task.CompletedTask;
  }

  public Task StartDiscoveryAsync(CancellationToken cancellationToken = default)
  {
      return Task.CompletedTask;
  }

  public Task StopDiscoveryAsync()
  {
      return Task.CompletedTask;
  }

  public Task<bool> PairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
  {
      return Task.FromResult(false);
  }

  public Task<bool> UnpairDeviceAsync(string deviceAddress, CancellationToken cancellationToken = default)
  {
      return Task.FromResult(false);
  }

  public Task<bool> AcceptConnectionAsync(string deviceAddress, CancellationToken cancellationToken = default)
  {
      return Task.FromResult(false);
  }

  public Task DisconnectAsync(CancellationToken cancellationToken = default)
  {
      return Task.CompletedTask;
  }

  public Task<object?> GetAudioCaptureDeviceAsync(CancellationToken cancellationToken = default)
  {
      return Task.FromResult<object?>(null);
  }

  public ValueTask DisposeAsync()
  {
      return ValueTask.CompletedTask;
  }
}
