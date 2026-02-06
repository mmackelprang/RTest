using Microsoft.Extensions.Logging;
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

  public BluetoothAudioSource(
    ILogger<BluetoothAudioSource> logger,
    IAudioDeviceManager deviceManager,
    IBluetoothService bluetoothService,
    BackgroundIdentificationService? identificationService = null,
    Radio.Core.Interfaces.IMetricsCollector? metricsCollector = null)
    : base(logger, deviceManager, identificationService, metricsCollector)
  {
    _bluetoothService = bluetoothService;
    _identificationService = identificationService;
    SetDefaultMetadata("Bluetooth", "Bluetooth", "Bluetooth Device");

    _bluetoothService.MetadataChanged += OnMetadataChanged;
    _bluetoothService.PlaybackStatusChanged += OnPlaybackStatusChanged;
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

    // Start Bluetooth adapter with configured name
    await _bluetoothService.StartAsync(Name, cancellationToken);

    // Obtain platform audio capture device
    var capture = await _bluetoothService.GetAudioCaptureDeviceAsync(cancellationToken);
    if (capture is AudioCaptureDevice audioCapture)
    {
      SoundComponent = audioCapture;

      var connected = _bluetoothService.ConnectedDevice;
      var deviceName = connected?.Name ?? "Bluetooth Device";
      MetadataInternal[StandardMetadataKeys.Title] = deviceName;
      MetadataInternal["Device"] = deviceName;
      if (!string.IsNullOrWhiteSpace(connected?.Address))
      {
        MetadataInternal["DeviceAddress"] = connected!.Address;
      }

      State = AudioSourceState.Ready;
    }
    else
    {
      Logger.LogWarning("BluetoothAudioSource: capture device not available");
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

    await _bluetoothService.StopAsync();
    await _bluetoothService.DisposeAsync();

    await base.DisposeAsyncCore();
  }

  private void OnMetadataChanged(object? sender, BluetoothPlaybackMetadata e)
  {
    if (e != null)
    {
        MetadataInternal[StandardMetadataKeys.Title] = e.Title;
        MetadataInternal[StandardMetadataKeys.Artist] = e.Artist;
        MetadataInternal[StandardMetadataKeys.Album] = e.Album;
        if (e.Duration > TimeSpan.Zero)
        {
            MetadataInternal[StandardMetadataKeys.Duration] = e.Duration.ToString();
        }
    }
  }

  private void OnPlaybackStatusChanged(object? sender, BluetoothPlaybackStatus e)
  {
    // Optionally log or track status
    MetadataInternal["Status"] = e.ToString();
  }
}
