using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Platform.Bluetooth;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Components;
using SoundFlow.Enums;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// Bluetooth audio source (A2DP sink) using a platform-provided capture device.
/// Leverages USBAudioSourceBase pipeline for capture integration and fingerprinting hooks.
/// </summary>
public class BluetoothAudioSource : USBAudioSourceBase
{
  private readonly IBluetoothService _bluetoothService;
  private readonly BackgroundIdentificationService? _identificationService;
  private readonly IServiceScopeFactory? _serviceScopeFactory;
  private readonly AlbumArtCacheService? _albumArtCache;
  private readonly IOptionsMonitor<BluetoothOptions> _options;
  private readonly SoundFlowPlaybackService? _playbackService;
  private string? _playbackId;
  private string? _lastCoverArtLookupKey;
  private AudioCaptureDevice? _captureDevice;
  private BufferedSoundGenerator<float>? _captureGenerator;
  private TimeSpan _btPosition;
  private TimeSpan? _btDuration;
  private bool _hasMediaPlayer;

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
    SoundFlowPlaybackService? playbackService = null,
    IServiceScopeFactory? serviceScopeFactory = null,
    AlbumArtCacheService? albumArtCache = null)
    : base(logger, deviceManager, identificationService, metricsCollector)
  {
    _bluetoothService = bluetoothService;
    _identificationService = identificationService;
    _serviceScopeFactory = serviceScopeFactory;
    _albumArtCache = albumArtCache;
    _options = options;
    _playbackService = playbackService;
    SetDefaultMetadata("Bluetooth", "Bluetooth", "Bluetooth Device");

    _bluetoothService.MetadataChanged += OnMetadataChanged;
    _bluetoothService.PlaybackStatusChanged += OnPlaybackStatusChanged;
    _bluetoothService.PositionChanged += OnPositionChanged;
    _bluetoothService.DeviceConnected += OnDeviceConnected;
    _bluetoothService.DeviceDisconnected += OnDeviceDisconnected;

    if (_identificationService != null)
      _identificationService.TrackIdentified += OnTrackIdentified;
  }

  public override string Name => "Bluetooth Audio";
  public override AudioSourceType Type => AudioSourceType.Bluetooth;

  public override bool SupportsNext => _hasMediaPlayer;
  public override bool SupportsPrevious => _hasMediaPlayer;
  public override bool SupportsShuffle => false;
  public override bool SupportsRepeat => false;

  /// <summary>Bluetooth is a live stream; not seekable.</summary>
  public override bool IsSeekable => false;

  public override TimeSpan? Duration => _btDuration;
  public override TimeSpan Position => _btPosition;

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
      _captureDevice = audioCapture;
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
      // No device connected yet — go to Ready state and wait for OnDeviceConnected
      // to acquire the capture device. This is the normal flow when the user activates
      // the Bluetooth source before pairing their phone.
      Logger.LogInformation("BluetoothAudioSource: waiting for Bluetooth device connection");
      State = AudioSourceState.Ready;
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

    if (_captureDevice == null && SoundComponent == null)
    {
      await InitializeAsync(cancellationToken);
    }

    if (_captureDevice != null && _playbackService != null)
    {
      // Linux capture path: bridge AudioCaptureDevice → BufferedSoundGenerator → mixer
      var engine = _playbackService.GetUnderlyingEngine();
      var format = _playbackService.GetAudioFormat();

      if (engine == null)
      {
        Logger.LogError("BluetoothAudioSource: SoundFlow engine not available — cannot create capture bridge");
        _captureDevice.Start();
        return;
      }

      _captureGenerator = new BufferedSoundGenerator<float>(engine, format, Logger, metricsCollector: MetricsCollector);
      _captureDevice.OnAudioProcessed += OnCaptureAudioProcessed;
      _captureDevice.Start();

      _playbackId = $"bt-capture-{Guid.NewGuid():N}";
      var success = await _playbackService.PlayComponentAsync(
        _playbackId, _captureGenerator, Volume, cancellationToken);

      if (success)
      {
        Logger.LogInformation("BluetoothAudioSource: capture bridge active — AudioCaptureDevice → BufferedSoundGenerator → mixer (PlaybackId={PlaybackId})", _playbackId);
      }
      else
      {
        Logger.LogError("BluetoothAudioSource: failed to register capture generator with mixer");
      }
    }
    else if (_captureDevice != null)
    {
      // Fallback: no playback service, just start capture (audio won't reach speakers)
      _captureDevice.Start();
      Logger.LogWarning("BluetoothAudioSource: no playback service — capture started but audio won't reach mixer");
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
    _captureDevice?.Stop();
    return Task.CompletedTask;
  }

  protected override Task ResumeCoreAsync(CancellationToken cancellationToken)
  {
    _captureDevice?.Start();
    return Task.CompletedTask;
  }

  protected override async Task StopCoreAsync(CancellationToken cancellationToken)
  {
    if (_captureDevice != null)
    {
      _captureDevice.OnAudioProcessed -= OnCaptureAudioProcessed;
      _captureDevice.Stop();
    }

    if (_playbackId != null && _playbackService != null)
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

  public override async Task NextAsync(CancellationToken cancellationToken = default)
  {
    await _bluetoothService.NextTrackAsync(cancellationToken);
  }

  public override async Task PreviousAsync(CancellationToken cancellationToken = default)
  {
    await _bluetoothService.PreviousTrackAsync(cancellationToken);
  }

  protected override async ValueTask DisposeAsyncCore()
  {
    if (_captureDevice != null)
    {
      _captureDevice.OnAudioProcessed -= OnCaptureAudioProcessed;
      _captureDevice.Dispose();
      _captureDevice = null;
    }

    if (_playbackId != null && _playbackService != null)
    {
      if (_bluetoothService is WindowsBluetoothService winBt)
      {
        winBt.StopLoopbackCapture();
      }
      await _playbackService.StopAsync(_playbackId);
      _playbackId = null;
    }

    _captureGenerator = null;

    _bluetoothService.MetadataChanged -= OnMetadataChanged;
    _bluetoothService.PlaybackStatusChanged -= OnPlaybackStatusChanged;
    _bluetoothService.PositionChanged -= OnPositionChanged;
    _bluetoothService.DeviceConnected -= OnDeviceConnected;
    _bluetoothService.DeviceDisconnected -= OnDeviceDisconnected;

    if (_identificationService != null)
      _identificationService.TrackIdentified -= OnTrackIdentified;

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

      if (_captureDevice != null || SoundComponent != null)
      {
        Logger.LogDebug("BluetoothAudioSource: capture device already acquired, skipping");
        return;
      }

      var capture = await _bluetoothService.GetAudioCaptureDeviceAsync();
      if (capture is AudioCaptureDevice audioCapture)
      {
        _captureDevice = audioCapture;
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
        Logger.LogWarning("BluetoothAudioSource: no audio capture device available after retries");
      }
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "BluetoothAudioSource: failed to acquire audio capture device on connect");
    }
  }

  private void OnDeviceDisconnected(object? sender, BluetoothDeviceDisconnectedEventArgs e)
  {
    Logger.LogDebug("BluetoothAudioSource: device disconnected event for {DeviceName}", e.Device.Name);
    NeedsFingerprintingLookup = false;
    _hasMediaPlayer = false;
    _btPosition = TimeSpan.Zero;
    _btDuration = null;

    if (State == AudioSourceState.Playing || State == AudioSourceState.Paused)
    {
      State = AudioSourceState.Stopped;
      SetDefaultMetadata("Bluetooth", "Bluetooth", "Bluetooth Device");
    }
  }

  private void OnPositionChanged(object? sender, TimeSpan position)
  {
    _btPosition = position;
  }

  private void OnMetadataChanged(object? sender, BluetoothPlaybackMetadata e)
  {
    if (e == null) return;

    // AVRCP metadata arriving means a media player is attached — enable next/prev
    _hasMediaPlayer = true;
    _btPosition = TimeSpan.Zero;

    MetricsCollector?.Increment("bluetooth.metadata_updates");
    MetadataInternal[StandardMetadataKeys.Title] = e.Title;
    MetadataInternal[StandardMetadataKeys.Artist] = e.Artist;
    MetadataInternal[StandardMetadataKeys.Album] = e.Album;

    if (e.Duration > TimeSpan.Zero)
    {
      _btDuration = e.Duration;
      MetadataInternal[StandardMetadataKeys.Duration] = e.Duration.ToString();
    }
    else
    {
      _btDuration = null;
    }

    // Propagate album art URL from AVRCP if available
    if (!string.IsNullOrEmpty(e.AlbumArtUrl))
    {
      MetadataInternal[StandardMetadataKeys.AlbumArtUrl] = e.AlbumArtUrl;
    }

    // If metadata is incomplete (no title or artist), request fingerprinting
    NeedsFingerprintingLookup = string.IsNullOrEmpty(e.Title) || string.IsNullOrEmpty(e.Artist);

    if (NeedsFingerprintingLookup)
    {
      _identificationService?.RequestImmediateIdentification();
    }
    else if (string.IsNullOrEmpty(e.AlbumArtUrl) && _serviceScopeFactory != null)
    {
      // AVRCP rarely provides album art — look it up via MusicBrainz text search
      var lookupKey = $"{e.Title}|{e.Artist}";
      if (lookupKey != _lastCoverArtLookupKey)
      {
        _lastCoverArtLookupKey = lookupKey;
        _ = LookupCoverArtAsync(e.Title, e.Artist, e.Album);
      }
    }
  }

  private void OnTrackIdentified(object? sender, TrackIdentifiedEventArgs e)
  {
    // After fingerprinting identifies a track, stop re-fingerprinting
    // until new AVRCP metadata arrives (OnMetadataChanged resets the flag)
    if (NeedsFingerprintingLookup)
    {
      NeedsFingerprintingLookup = false;
      Logger.LogDebug(
        "Track identified via fingerprinting: '{Title}' by '{Artist}' — skipping further fingerprinting",
        e.Track.Title, e.Track.Artist);
    }
  }

  private async Task LookupCoverArtAsync(string title, string artist, string? album)
  {
    try
    {
      Logger.LogInformation("Looking up cover art for '{Title}' by '{Artist}' (Album: '{Album}')", title, artist, album);

      // Resolve IMetadataLookupService from a scope (it's registered as scoped,
      // but BluetoothAudioSource is a long-lived singleton-created instance)
      using var scope = _serviceScopeFactory!.CreateScope();
      var lookupService = scope.ServiceProvider.GetService<IMetadataLookupService>();
      if (lookupService == null)
      {
        Logger.LogWarning("IMetadataLookupService not available — cannot look up cover art");
        return;
      }

      var coverArtUrl = await lookupService.SearchCoverArtByTextAsync(title, artist, album);
      if (!string.IsNullOrEmpty(coverArtUrl))
      {
        // Cache the external URL locally so the Web UI can serve it via /api/albumart/
        if (_albumArtCache != null)
        {
          var localUrl = await _albumArtCache.SaveFromUrlAsync(coverArtUrl);
          if (!string.IsNullOrEmpty(localUrl))
          {
            coverArtUrl = localUrl;
          }
        }

        MetadataInternal[StandardMetadataKeys.AlbumArtUrl] = coverArtUrl;
        Logger.LogInformation("Cover art found for '{Title}' by '{Artist}': {Url}", title, artist, coverArtUrl);
      }
      else
      {
        Logger.LogInformation("No cover art found for '{Title}' by '{Artist}'", title, artist);
      }
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "Cover art lookup failed for '{Title}' by '{Artist}'", title, artist);
    }
  }

  /// <summary>
  /// Forwards captured audio samples from AudioCaptureDevice to the BufferedSoundGenerator
  /// so they flow through the SoundFlow playback mixer.
  /// </summary>
  private void OnCaptureAudioProcessed(Span<float> samples, Capability capability)
  {
    _captureGenerator?.AddSamples(samples);
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
