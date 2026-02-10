using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using SoundFlow.Abstracts.Devices;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Bluetooth audio source (A2DP sink) using a platform-provided capture device.
/// Leverages USBAudioSourceBase pipeline for capture integration and fingerprinting hooks.
/// </summary>
public class BluetoothAudioSource : USBAudioSourceBase
{
  private readonly IBluetoothService _bluetoothService;
  private readonly BackgroundIdentificationService? _identificationService;
  private readonly IOptionsMonitor<BluetoothOptions> _options;

  /// <summary>
  /// When true, the fingerprinting pipeline will attempt to identify the current track.
  /// Set when AVRCP metadata is incomplete (no title/artist).
  /// </summary>
  public bool NeedsFingerprintingLookup { get; private set; }

  public BluetoothAudioSource(
    ILogger<BluetoothAudioSource> logger,
    IAudioDeviceManager deviceManager,
    IBluetoothService bluetoothService,
    IOptionsMonitor<BluetoothOptions> options,
    BackgroundIdentificationService? identificationService = null,
    Radio.Core.Interfaces.IMetricsCollector? metricsCollector = null)
    : base(logger, deviceManager, identificationService, metricsCollector)
  {
    _bluetoothService = bluetoothService;
    _identificationService = identificationService;
    _options = options;
    SetDefaultMetadata("Bluetooth", "Bluetooth", "Bluetooth Device");

    _bluetoothService.MetadataChanged += OnMetadataChanged;
    _bluetoothService.PlaybackStatusChanged += OnPlaybackStatusChanged;
    _bluetoothService.DeviceConnected += OnDeviceConnected;
    _bluetoothService.DeviceDisconnected += OnDeviceDisconnected;
  }

  public override string Name => "Bluetooth Audio";
  public override AudioSourceType Type => AudioSourceType.Bluetooth;

  public override bool SupportsNext => false;
  public override bool SupportsPrevious => false;
  public override bool SupportsShuffle => false;
  public override bool SupportsRepeat => false;

  /// <summary>Bluetooth is a live stream; not seekable.</summary>
  public override bool IsSeekable => false;

  // We rely on metadata events rather than polling for these
  public override TimeSpan? Duration => null;
  public override TimeSpan Position => TimeSpan.Zero;

  public override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();

    // Start Bluetooth adapter with configured device name (not the source Name)
    var deviceName = _options.CurrentValue.DeviceName;
    Logger.LogInformation("Starting Bluetooth adapter as '{DeviceName}'", deviceName);
    await _bluetoothService.StartAsync(deviceName, cancellationToken);

    // Obtain platform audio capture device
    var capture = await _bluetoothService.GetAudioCaptureDeviceAsync(cancellationToken);
    if (capture is AudioCaptureDevice audioCapture)
    {
      SoundComponent = audioCapture;

      var connected = _bluetoothService.ConnectedDevice;
      var connectedName = connected?.Name ?? "Bluetooth Device";
      MetadataInternal[StandardMetadataKeys.Title] = connectedName;
      MetadataInternal["Device"] = connectedName;
      if (!string.IsNullOrWhiteSpace(connected?.Address))
      {
        MetadataInternal["DeviceAddress"] = connected!.Address;
      }

      // No AVRCP metadata yet — request fingerprinting
      NeedsFingerprintingLookup = true;
      State = AudioSourceState.Ready;
    }
    else
    {
      Logger.LogWarning("BluetoothAudioSource: capture device not available (no connected device or no audio endpoint)");
      MetricsCollector?.Increment("bluetooth.audio_capture_errors");
      State = AudioSourceState.Error;
    }
  }

  protected override async Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    if (SoundComponent == null)
    {
      await InitializeAsync(cancellationToken);
    }

    if (SoundComponent is AudioCaptureDevice captureDevice)
    {
      captureDevice.Start();
    }
  }

  protected override Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    if (SoundComponent is AudioCaptureDevice captureDevice)
    {
      captureDevice.Stop(); // Local pause; does not send AVRCP
    }
    return Task.CompletedTask;
  }

  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    if (SoundComponent is AudioCaptureDevice captureDevice)
    {
      captureDevice.Start();
    }
    return Task.CompletedTask;
  }

  protected override async Task StopCoreAsync(CancellationToken cancellationToken)
  {
    if (SoundComponent is AudioCaptureDevice captureDevice)
    {
      captureDevice.Stop();
    }

    await _bluetoothService.StopAsync(cancellationToken);
  }

  protected override async ValueTask DisposeAsyncCore()
  {
    if (SoundComponent is AudioCaptureDevice captureDevice)
    {
      captureDevice.Dispose();
    }

    _bluetoothService.MetadataChanged -= OnMetadataChanged;
    _bluetoothService.PlaybackStatusChanged -= OnPlaybackStatusChanged;
    _bluetoothService.DeviceConnected -= OnDeviceConnected;
    _bluetoothService.DeviceDisconnected -= OnDeviceDisconnected;

    await _bluetoothService.StopAsync();
    await _bluetoothService.DisposeAsync();

    await base.DisposeAsyncCore();
  }

  private void OnDeviceConnected(object? sender, BluetoothDeviceConnectedEventArgs e)
  {
    Logger.LogInformation("Bluetooth device connected: {DeviceName} ({Address})",
      e.Device.Name, e.Device.Address);
    MetadataInternal[StandardMetadataKeys.Title] = e.Device.Name;
    MetadataInternal["Device"] = e.Device.Name;
    MetadataInternal["DeviceAddress"] = e.Device.Address;
    NeedsFingerprintingLookup = true;
  }

  private void OnDeviceDisconnected(object? sender, BluetoothDeviceDisconnectedEventArgs e)
  {
    Logger.LogInformation("Bluetooth device disconnected: {DeviceName} ({Address})",
      e.Device.Name, e.Device.Address);
    NeedsFingerprintingLookup = false;

    if (State == AudioSourceState.Playing || State == AudioSourceState.Paused)
    {
      State = AudioSourceState.Stopped;
      SetDefaultMetadata("Bluetooth", "Bluetooth", "Bluetooth Device");
    }
  }

  private void OnMetadataChanged(object? sender, BluetoothPlaybackMetadata e)
  {
    if (e == null) return;

    MetricsCollector?.Increment("bluetooth.metadata_updates");
    MetadataInternal[StandardMetadataKeys.Title] = e.Title;
    MetadataInternal[StandardMetadataKeys.Artist] = e.Artist;
    MetadataInternal[StandardMetadataKeys.Album] = e.Album;

    if (e.Duration > TimeSpan.Zero)
    {
      MetadataInternal[StandardMetadataKeys.Duration] = e.Duration.ToString();
    }

    // Propagate album art URL from AVRCP if available
    if (!string.IsNullOrEmpty(e.AlbumArtUrl))
    {
      MetadataInternal[StandardMetadataKeys.AlbumArtUrl] = e.AlbumArtUrl;
    }

    // If metadata is incomplete (no title or artist), request fingerprinting
    NeedsFingerprintingLookup = string.IsNullOrEmpty(e.Title) || string.IsNullOrEmpty(e.Artist);
  }

  private void OnPlaybackStatusChanged(object? sender, BluetoothPlaybackStatus e)
  {
    MetadataInternal["PlaybackStatus"] = e.ToString();
    Logger.LogDebug("Bluetooth playback status: {Status}", e);
  }
}
