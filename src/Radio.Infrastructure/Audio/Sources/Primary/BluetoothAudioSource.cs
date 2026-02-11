using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Platform.Bluetooth;
using SoundFlow.Abstracts;
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
  private readonly SoundFlowPlaybackService? _playbackService;
  private string? _playbackId;

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
    Radio.Core.Interfaces.IMetricsCollector? metricsCollector = null,
    SoundFlowPlaybackService? playbackService = null)
    : base(logger, deviceManager, identificationService, metricsCollector)
  {
    _bluetoothService = bluetoothService;
    _identificationService = identificationService;
    _options = options;
    _playbackService = playbackService;
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

    // When the platform manages audio routing directly (e.g., Windows AudioPlaybackConnection),
    // no SoundFlow capture device is needed — audio goes to system speakers.
    // Metadata still flows via SMTC events on Windows.
    if (_bluetoothService.IsAudioManagedByPlatform)
    {
      Logger.LogInformation("BluetoothAudioSource: platform manages audio routing, skipping capture device");
      var connected = _bluetoothService.ConnectedDevice;
      var connectedName = connected?.Name ?? "Bluetooth Device";
      MetadataInternal[StandardMetadataKeys.Title] = connectedName;
      MetadataInternal["Device"] = connectedName;
      if (!string.IsNullOrWhiteSpace(connected?.Address))
      {
        MetadataInternal["DeviceAddress"] = connected!.Address;
      }
      NeedsFingerprintingLookup = true;
      State = AudioSourceState.Ready;
      return;
    }

    // Obtain platform audio capture device (may be AudioCaptureDevice or BufferedSoundGenerator)
    var capture = await _bluetoothService.GetAudioCaptureDeviceAsync(cancellationToken);
    if (capture is AudioCaptureDevice audioCapture)
    {
      SoundComponent = audioCapture;
      SetConnectedDeviceMetadata();
      NeedsFingerprintingLookup = true;
      State = AudioSourceState.Ready;
    }
    else if (capture is SoundComponent soundComponent)
    {
      // WASAPI loopback capture returns a BufferedSoundGenerator<float>
      SoundComponent = soundComponent;
      SetConnectedDeviceMetadata();
      NeedsFingerprintingLookup = true;
      State = AudioSourceState.Ready;
      Logger.LogInformation("BluetoothAudioSource: using WASAPI loopback capture via SoundFlow pipeline");
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
    // Platform manages audio routing — nothing to start locally
    if (_bluetoothService.IsAudioManagedByPlatform)
    {
      Logger.LogDebug("BluetoothAudioSource: PlayCore — platform manages audio, no capture device to start");
      return;
    }

    if (SoundComponent == null)
    {
      await InitializeAsync(cancellationToken);
    }

    if (SoundComponent is AudioCaptureDevice captureDevice)
    {
      captureDevice.Start();
    }
    else if (SoundComponent is BufferedSoundGenerator<float> generator && _playbackService != null)
    {
      // WASAPI loopback capture path: register generator with SoundFlow mixer
      _playbackId = $"bt-loopback-{Guid.NewGuid():N}";
      var success = await _playbackService.PlayComponentAsync(
        _playbackId, generator, Volume, cancellationToken);

      if (success)
      {
        Logger.LogInformation("BluetoothAudioSource: WASAPI loopback generator added to mixer (PlaybackId={PlaybackId})", _playbackId);

        // Start the loopback capture (with endpoint muting logic)
        if (_bluetoothService is WindowsBluetoothService winBt)
        {
          winBt.StartLoopbackCapture();
        }
      }
      else
      {
        Logger.LogError("BluetoothAudioSource: Failed to add loopback generator to SoundFlow mixer");
      }
    }
  }

  protected override Task PauseCoreAsync(CancellationToken cancellationToken)
  {
    if (SoundComponent is AudioCaptureDevice captureDevice)
    {
      captureDevice.Stop(); // Local pause; does not send AVRCP
    }
    // For loopback capture, we don't stop the capture on pause —
    // the phone's audio keeps flowing, we just don't change mixer state.
    // Pausing the source's state is enough for the UI.
    return Task.CompletedTask;
  }

  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    if (SoundComponent is AudioCaptureDevice captureDevice)
    {
      captureDevice.Start();
    }
    // Loopback capture continues running — resume is a no-op for mixer
    return Task.CompletedTask;
  }

  protected override async Task StopCoreAsync(CancellationToken cancellationToken)
  {
    if (SoundComponent is AudioCaptureDevice captureDevice)
    {
      captureDevice.Stop();
    }
    else if (_playbackId != null && _playbackService != null)
    {
      // Stop loopback capture first, then remove from mixer
      if (_bluetoothService is WindowsBluetoothService winBt)
      {
        winBt.StopLoopbackCapture();
      }

      await _playbackService.StopAsync(_playbackId, cancellationToken);
      _playbackId = null;
      Logger.LogInformation("BluetoothAudioSource: loopback capture stopped and generator removed from mixer");
    }

    await _bluetoothService.StopAsync(cancellationToken);
  }

  protected override async ValueTask DisposeAsyncCore()
  {
    if (SoundComponent is AudioCaptureDevice captureDevice)
    {
      captureDevice.Dispose();
    }
    else if (_playbackId != null && _playbackService != null)
    {
      if (_bluetoothService is WindowsBluetoothService winBt)
      {
        winBt.StopLoopbackCapture();
      }
      await _playbackService.StopAsync(_playbackId);
      _playbackId = null;
    }

    _bluetoothService.MetadataChanged -= OnMetadataChanged;
    _bluetoothService.PlaybackStatusChanged -= OnPlaybackStatusChanged;
    _bluetoothService.DeviceConnected -= OnDeviceConnected;
    _bluetoothService.DeviceDisconnected -= OnDeviceDisconnected;

    await _bluetoothService.StopAsync();
    await _bluetoothService.DisposeAsync();

    await base.DisposeAsyncCore();
  }

  private void SetConnectedDeviceMetadata()
  {
    var connected = _bluetoothService.ConnectedDevice;
    var connectedName = connected?.Name ?? "Bluetooth Device";
    MetadataInternal[StandardMetadataKeys.Title] = connectedName;
    MetadataInternal["Device"] = connectedName;
    if (!string.IsNullOrWhiteSpace(connected?.Address))
    {
      MetadataInternal["DeviceAddress"] = connected!.Address;
    }
  }

  private void OnDeviceConnected(object? sender, BluetoothDeviceConnectedEventArgs e)
  {
    Logger.LogDebug("BluetoothAudioSource: device connected event for {DeviceName}", e.Device.Name);
    MetadataInternal[StandardMetadataKeys.Title] = e.Device.Name;
    MetadataInternal["Device"] = e.Device.Name;
    MetadataInternal["DeviceAddress"] = e.Device.Address;
    NeedsFingerprintingLookup = true;

    // Attempt to acquire audio capture device now that a device is connected
    _ = TryAcquireAudioCaptureAsync();
  }

  private async Task TryAcquireAudioCaptureAsync()
  {
    try
    {
      // Platform manages audio directly — no capture device needed
      if (_bluetoothService.IsAudioManagedByPlatform)
      {
        State = AudioSourceState.Ready;
        Logger.LogDebug("BluetoothAudioSource: platform manages audio, source set to Ready");
        return;
      }

      if (SoundComponent != null)
      {
        Logger.LogDebug("BluetoothAudioSource: capture device already acquired, skipping");
        return;
      }

      var capture = await _bluetoothService.GetAudioCaptureDeviceAsync();
      if (capture is AudioCaptureDevice audioCapture)
      {
        SoundComponent = audioCapture;
        State = AudioSourceState.Ready;
        Logger.LogInformation("BluetoothAudioSource: audio capture device acquired after device connected");
      }
      else if (capture is SoundComponent soundComponent)
      {
        SoundComponent = soundComponent;
        State = AudioSourceState.Ready;
        Logger.LogInformation("BluetoothAudioSource: WASAPI loopback generator acquired after device connected");
      }
      else
      {
        Logger.LogDebug("BluetoothAudioSource: no audio capture device available for connected device");
      }
    }
    catch (Exception ex)
    {
      Logger.LogDebug(ex, "BluetoothAudioSource: failed to acquire audio capture device on connect");
    }
  }

  private void OnDeviceDisconnected(object? sender, BluetoothDeviceDisconnectedEventArgs e)
  {
    Logger.LogDebug("BluetoothAudioSource: device disconnected event for {DeviceName}", e.Device.Name);
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

    // Mirror the phone's playback state so the UI accurately reflects
    // whether the phone is playing or paused (drives play history + state updates).
    switch (e)
    {
      case BluetoothPlaybackStatus.Playing when State == AudioSourceState.Ready || State == AudioSourceState.Paused:
        State = AudioSourceState.Playing;
        break;
      case BluetoothPlaybackStatus.Paused when State == AudioSourceState.Playing:
        State = AudioSourceState.Paused;
        break;
      case BluetoothPlaybackStatus.Stopped when State == AudioSourceState.Playing || State == AudioSourceState.Paused:
        State = AudioSourceState.Stopped;
        break;
    }
  }
}
