using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.SoundFlow;
using RTLSDRCore;
using RTLSDRCore.Enums;

namespace Radio.Infrastructure.Audio.Sources.Primary;

/// <summary>
/// RTL-SDR software-defined radio audio source.
/// Wraps RTLSDRCore.RadioReceiver and provides async IRadioControl interface.
/// Translates RTLSDRCore events to Radio.Core events for unified API surface.
/// </summary>
public class SDRRadioAudioSource : PrimaryAudioSourceBase, Radio.Core.Interfaces.Audio.IRadioControl
{
  private readonly RadioReceiver _radioReceiver;
  private readonly IOptionsMonitor<RadioOptions> _radioOptions;
  private readonly BackgroundIdentificationService? _identificationService;
  private readonly SoundFlowPlaybackService? _playbackService;
  private readonly Dictionary<string, object> _metadata = new();
  private Frequency _frequencyStep;
  private int _deviceVolume;
  private bool _isScanning;
  private ScanDirection? _scanDirection;
  private Task? _scanTask;
  private CancellationTokenSource? _scanCts;
  private BufferedSoundGenerator<float>? _soundGenerator;
  private string? _playbackId;

  /// <summary>
  /// Initializes a new instance of the <see cref="SDRRadioAudioSource"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="radioReceiver">The RTLSDRCore radio receiver.</param>
  /// <param name="radioOptions">Radio configuration options.</param>
  /// <param name="metricsCollector">Optional metrics collector for tracking radio operations.</param>
  /// <param name="identificationService">Optional fingerprinting service for track identification.</param>
  /// <param name="playbackService">Optional SoundFlow playback service for audio output.</param>
  public SDRRadioAudioSource(
    ILogger<SDRRadioAudioSource> logger,
    RadioReceiver radioReceiver,
    IOptionsMonitor<RadioOptions> radioOptions,
    IMetricsCollector? metricsCollector = null,
    BackgroundIdentificationService? identificationService = null,
    SoundFlowPlaybackService? playbackService = null)
    : base(logger, metricsCollector)
  {
    _radioReceiver = radioReceiver ?? throw new ArgumentNullException(nameof(radioReceiver));
    _radioOptions = radioOptions ?? throw new ArgumentNullException(nameof(radioOptions));
    _identificationService = identificationService;
    _playbackService = playbackService;

    // Initialize from configuration
    var options = _radioOptions.CurrentValue;
    _frequencyStep = Frequency.FromMegahertz(options.DefaultFMStepMHz);
    _deviceVolume = options.DefaultDeviceVolume;

    // Subscribe to RTLSDRCore events and translate to Radio.Core events
    _radioReceiver.FrequencyChanged += OnRTLSDRFrequencyChanged;
    _radioReceiver.SignalStrengthUpdated += OnRTLSDRSignalStrengthUpdated;
    _radioReceiver.StateChanged += OnRTLSDRStateChanged;

    // Subscribe to track identification events if service is available
    if (_identificationService != null)
    {
      _identificationService.TrackIdentified += OnTrackIdentified;
    }

    // Initialize metadata
    SetDefaultMetadata();
  }

  // Track if we've logged the first audio data received (to avoid spamming logs)
  private bool _hasLoggedFirstAudioData;
  private long _totalSamplesReceived;

  /// <summary>
  /// Handles audio data events from the RTL-SDR receiver.
  /// Delegates samples to the buffered sound generator.
  /// </summary>
  private void OnAudioDataAvailable(object? sender, AudioDataEventArgs e)
  {
    if (_soundGenerator == null) return;

    _soundGenerator.AddSamples(e.Samples);
    _totalSamplesReceived += e.Samples.Length;

    // Log first audio data received
    if (!_hasLoggedFirstAudioData)
    {
      _hasLoggedFirstAudioData = true;
      Logger.LogInformation(
        "📻 SDR RADIO AUDIO FLOW STARTED: First audio data received from RTL-SDR " +
        "(Frequency: {Frequency}, Band: {Band}, {SampleCount} samples)",
        CurrentFrequency.ToDisplayString(), CurrentBand, e.Samples.Length);
    }
  }

  #region IAudioSource Properties

  /// <inheritdoc/>
  public override string Name => "SDR Radio (RTL-SDR)";

  /// <inheritdoc/>
  public override AudioSourceType Type => AudioSourceType.Radio;

  /// <inheritdoc/>
  public override TimeSpan? Duration => null; // Live stream has no duration

  /// <inheritdoc/>
  public override TimeSpan Position => TimeSpan.Zero; // Live stream has no position

  /// <inheritdoc/>
  public override bool IsSeekable => false; // Live radio cannot be seeked

  /// <inheritdoc/>
  public override IReadOnlyDictionary<string, object> Metadata => _metadata;

  /// <inheritdoc/>
  public override object GetSoundComponent()
  {
    // Return the SDR sound generator for SoundFlow integration
    // This directly generates audio samples from RTLSDRCore's push-based audio
    // Note: The generator is created in PlayCoreAsync when we have access to the audio engine
    return _soundGenerator ?? throw new InvalidOperationException(
      "SDR sound generator not initialized. Call PlayAsync first.");
  }

  #endregion

  #region IRadioControl Lifecycle

  /// <inheritdoc/>
  public bool IsRunning => _radioReceiver.IsRunning;

  /// <inheritdoc/>
  public async Task<bool> StartupAsync(CancellationToken cancellationToken = default)
  {
    return await Task.Run(() => _radioReceiver.Startup(), cancellationToken);
  }

  /// <inheritdoc/>
  public async Task ShutdownAsync(CancellationToken cancellationToken = default)
  {
    // Stop any ongoing scan
    if (_isScanning)
    {
      await StopScanAsync(cancellationToken);
    }

    await Task.Run(() => _radioReceiver.Shutdown(), cancellationToken);
  }

  #endregion

  #region IRadioControl Frequency Control

  /// <inheritdoc/>
  public Frequency CurrentFrequency => new(_radioReceiver.CurrentFrequency);

  /// <inheritdoc/>
  public async Task SetFrequencyAsync(Frequency frequency, CancellationToken cancellationToken = default)
  {
    var success = await Task.Run(() => _radioReceiver.SetFrequency(frequency.Hertz), cancellationToken);
    if (!success)
    {
      throw new ArgumentOutOfRangeException(nameof(frequency), 
        $"Failed to set frequency to {frequency.ToDisplayString()}");
    }
  }

  /// <inheritdoc/>
  public async Task StepFrequencyUpAsync(CancellationToken cancellationToken = default)
  {
    await Task.Run(() => _radioReceiver.TuneFrequencyUp(_frequencyStep.Hertz), cancellationToken);
  }

  /// <inheritdoc/>
  public async Task StepFrequencyDownAsync(CancellationToken cancellationToken = default)
  {
    await Task.Run(() => _radioReceiver.TuneFrequencyDown(_frequencyStep.Hertz), cancellationToken);
  }

  /// <inheritdoc/>
  public Frequency FrequencyStep => _frequencyStep;

  /// <inheritdoc/>
  public Task SetFrequencyStepAsync(Frequency step, CancellationToken cancellationToken = default)
  {
    if (step.Hertz <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(step), "Frequency step must be greater than zero");
    }

    _frequencyStep = step;
    return Task.CompletedTask;
  }

  #endregion

  #region IRadioControl Scanning

  /// <inheritdoc/>
  public bool IsScanning => _isScanning;

  /// <inheritdoc/>
  public ScanDirection? ScanDirection => _scanDirection;

  /// <inheritdoc/>
  public Task StartScanAsync(ScanDirection direction, CancellationToken cancellationToken = default)
  {
    if (_isScanning)
    {
      throw new InvalidOperationException("Scan already in progress");
    }

    _isScanning = true;
    _scanDirection = direction;
    _scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    // Track scan started metric
    MetricsCollector?.Increment("radio.scan_started", 1.0, new Dictionary<string, string>
    {
      ["direction"] = direction.ToString().ToLowerInvariant()
    });

    // Start scan in background task
    _scanTask = Task.Run(() =>
    {
      try
      {
        if (direction == Radio.Core.Models.Audio.ScanDirection.Up)
        {
          _radioReceiver.ScanFrequencyUp(_frequencyStep.Hertz);
        }
        else
        {
          _radioReceiver.ScanFrequencyDown(_frequencyStep.Hertz);
        }
      }
      finally
      {
        _isScanning = false;
        _scanDirection = null;
      }
    }, _scanCts.Token);

    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  public async Task StopScanAsync(CancellationToken cancellationToken = default)
  {
    if (!_isScanning)
    {
      return;
    }

    _radioReceiver.CancelScan();
    _scanCts?.Cancel();

    // Track scan stopped metric
    MetricsCollector?.Increment("radio.scan_stopped");

    if (_scanTask != null)
    {
      try
      {
        await _scanTask;
      }
      catch (OperationCanceledException)
      {
        // Expected when canceling scan
      }
    }

    _isScanning = false;
    _scanDirection = null;
    _scanCts?.Dispose();
    _scanCts = null;
    _scanTask = null;
  }

  #endregion

  #region IRadioControl Band and Modulation

  /// <inheritdoc/>
  public RadioBand CurrentBand => MapBandFromRTLSDR(_radioReceiver.CurrentBand);

  /// <inheritdoc/>
  public async Task SetBandAsync(RadioBand band, CancellationToken cancellationToken = default)
  {
    var rtlBandType = MapBandTypeToRTLSDR(band);
    var success = await Task.Run(() => _radioReceiver.SetBand(rtlBandType), cancellationToken);
    if (!success)
    {
      throw new ArgumentException($"Failed to set band to {band}", nameof(band));
    }

    _frequencyStep = new Frequency(_radioReceiver.CurrentBand.DefaultStepHz); // Update step size to band default

    // Track band change metric
    MetricsCollector?.Increment("radio.band_changes", 1.0, new Dictionary<string, string>
    {
      ["band"] = band.ToString().ToLowerInvariant()
    });
  }

  #endregion

  #region IRadioControl Audio Control

  /// <inheritdoc/>
  /// <remarks>
  /// Uses 'new' keyword because both PrimaryAudioSourceBase and IRadioControl define Volume.
  /// IRadioControl requires synchronization with DeviceVolume property.
  /// </remarks>
  public new float Volume
  {
    get => _radioReceiver.Volume;
    set
    {
      var clampedValue = Math.Clamp(value, 0.0f, 1.0f);
      _radioReceiver.Volume = clampedValue;
      base.Volume = clampedValue; // Maintain consistency with base class
      _deviceVolume = (int)Math.Round(clampedValue * 100);
    }
  }

  /// <inheritdoc/>
  public int DeviceVolume
  {
    get => _deviceVolume;
    set
    {
      _deviceVolume = Math.Clamp(value, 0, 100);
      _radioReceiver.Volume = _deviceVolume / 100.0f;
    }
  }

  /// <inheritdoc/>
  public bool IsMuted
  {
    get => _radioReceiver.IsMuted;
    set => _radioReceiver.IsMuted = value;
  }

  /// <inheritdoc/>
  public float SquelchThreshold
  {
    get => _radioReceiver.SquelchThreshold;
    set => _radioReceiver.SquelchThreshold = Math.Clamp(value, 0.0f, 1.0f);
  }

  #endregion

  #region IRadioControl Equalizer

  /// <inheritdoc/>
  public RadioEqualizerMode EqualizerMode => RadioEqualizerMode.Off; // RTL-SDR doesn't have EQ

  /// <inheritdoc/>
  public Task SetEqualizerModeAsync(RadioEqualizerMode mode, CancellationToken cancellationToken = default)
  {
    // RTL-SDR doesn't support equalizer modes
    // Silently ignore to maintain compatibility
    return Task.CompletedTask;
  }

  #endregion

  #region IRadioControl Gain Control

  /// <inheritdoc/>
  public bool AutoGainEnabled
  {
    get => _radioReceiver.AutoGainEnabled;
    set => _radioReceiver.AutoGainEnabled = value;
  }

  /// <inheritdoc/>
  public float Gain
  {
    get => _radioReceiver.Gain;
    set => _radioReceiver.Gain = value;
  }

  #endregion

  #region IRadioControl State and Signal

  /// <inheritdoc/>
  public int SignalStrength => (int)(_radioReceiver.SignalStrength * 100);

  /// <inheritdoc/>
  public bool IsStereo => _radioReceiver.CurrentModulation == ModulationType.WFM; // WFM can be stereo

  /// <inheritdoc/>
  public Task<bool> GetPowerStateAsync(CancellationToken cancellationToken = default)
  {
    return Task.FromResult(_radioReceiver.IsRunning);
  }

  /// <inheritdoc/>
  public async Task TogglePowerStateAsync(CancellationToken cancellationToken = default)
  {
    if (_radioReceiver.IsRunning)
    {
      await ShutdownAsync(cancellationToken);
    }
    else
    {
      await StartupAsync(cancellationToken);
    }
  }

  #endregion

  #region IRadioControl Events

  /// <inheritdoc/>
  /// <remarks>
  /// Uses 'new' keyword because both PrimaryAudioSourceBase and IRadioControl define StateChanged.
  /// Base class uses AudioSourceStateChangedEventArgs while IRadioControl uses RadioStateChangedEventArgs.
  /// Both events serve different purposes and are intentionally separate.
  /// </remarks>
  public new event EventHandler<RadioStateChangedEventArgs>? StateChanged;

  /// <inheritdoc/>
  public event EventHandler<RadioControlFrequencyChangedEventArgs>? FrequencyChanged;

  /// <inheritdoc/>
  public event EventHandler<RadioControlSignalStrengthEventArgs>? SignalStrengthUpdated;

  #endregion

  #region Event Translation (RTLSDRCore → Radio.Core)

  /// <summary>
  /// Translates RTLSDRCore FrequencyChanged event to Radio.Core event.
  /// </summary>
  private void OnRTLSDRFrequencyChanged(object? sender, RTLSDRCore.FrequencyChangedEventArgs e)
  {
    var oldFreq = new Frequency(e.OldFrequency);
    var newFreq = new Frequency(e.NewFrequency);
    
    // Track frequency change metric
    MetricsCollector?.Increment("radio.frequency_changes");
    
    FrequencyChanged?.Invoke(this, new RadioControlFrequencyChangedEventArgs(oldFreq, newFreq));
  }

  /// <summary>
  /// Translates RTLSDRCore SignalStrengthUpdated event to Radio.Core event.
  /// </summary>
  private void OnRTLSDRSignalStrengthUpdated(object? sender, RTLSDRCore.SignalStrengthEventArgs e)
  {
    // Track signal strength as gauge metric with frequency and band tags
    MetricsCollector?.Gauge("radio.signal_strength", e.Strength * 100, new Dictionary<string, string>
    {
      ["frequency_mhz"] = (CurrentFrequency.Hertz / 1_000_000.0).ToString("F2"),
      ["band"] = CurrentBand.ToString()
    });
    
    SignalStrengthUpdated?.Invoke(this, new RadioControlSignalStrengthEventArgs(e.Strength));
  }

  /// <summary>
  /// Translates RTLSDRCore ReceiverStateChanged event to Radio.Core event.
  /// </summary>
  private void OnRTLSDRStateChanged(object? sender, RTLSDRCore.ReceiverStateChangedEventArgs e)
  {
    // Update metadata when state changes
    UpdateMetadataFromState();
    
    // Raise Radio.Core state changed event using the property-based event args
    StateChanged?.Invoke(this, new RadioStateChangedEventArgs(
      "State",
      e.NewState.ToString(),
      e.OldState.ToString()));
  }

  #endregion

  #region Fingerprinting

  /// <summary>
  /// Handles the TrackIdentified event from the fingerprinting service.
  /// Updates metadata with identified track information for radio streams.
  /// </summary>
  private void OnTrackIdentified(object? sender, Radio.Core.Events.TrackIdentifiedEventArgs e)
  {
    // Only update metadata if this is the active source
    if (State != AudioSourceState.Playing && State != AudioSourceState.Paused)
    {
      return;
    }

    var track = e.Track;
    Logger.LogInformation(
      "Updating SDR Radio metadata from fingerprinting: {Title} by {Artist} (confidence: {Confidence:P0})",
      track.Title, track.Artist, e.Confidence);

    UpdateMetadataFromFingerprint(track, e.Confidence, e.IdentifiedAt);
  }

  /// <summary>
  /// Updates metadata from fingerprinting results.
  /// Follows the same pattern as USBAudioSourceBase for consistency.
  /// </summary>
  protected virtual void UpdateMetadataFromFingerprint(TrackMetadata track, double confidence, DateTime identifiedAt)
  {
    // Store current source/device info to restore later
    var sourceInfo = _metadata.TryGetValue("Source", out var source) ? source : null;
    var deviceInfo = _metadata.TryGetValue("Device", out var device) ? device : null;

    // Update standard metadata fields
    _metadata[StandardMetadataKeys.Title] = track.Title;
    _metadata[StandardMetadataKeys.Artist] = track.Artist;
    _metadata[StandardMetadataKeys.Album] = track.Album ?? StandardMetadataKeys.DefaultAlbum;
    
    // Use CoverArtUrl from fingerprinting if available, otherwise use default
    _metadata[StandardMetadataKeys.AlbumArtUrl] = !string.IsNullOrEmpty(track.CoverArtUrl)
      ? track.CoverArtUrl
      : StandardMetadataKeys.DefaultAlbumArtUrl;

    // Add optional metadata if available
    if (track.Genre != null)
    {
      _metadata[StandardMetadataKeys.Genre] = track.Genre;
    }

    if (track.ReleaseYear.HasValue)
    {
      _metadata[StandardMetadataKeys.Year] = track.ReleaseYear.Value;
    }

    if (track.TrackNumber.HasValue)
    {
      _metadata[StandardMetadataKeys.TrackNumber] = track.TrackNumber.Value;
    }

    // Restore source/device information
    if (sourceInfo != null)
    {
      _metadata["Source"] = sourceInfo;
    }
    if (deviceInfo != null)
    {
      _metadata["Device"] = deviceInfo;
    }

    // Add fingerprinting metadata with standard keys for consistency
    _metadata["IdentificationConfidence"] = confidence;
    _metadata["IdentifiedAt"] = identifiedAt;
    _metadata["MetadataSource"] = "Fingerprinting";
  }

  #endregion

  #region Helper Methods

  /// <summary>
  /// Sets default metadata for the SDR Radio source.
  /// </summary>
  private void SetDefaultMetadata()
  {
    _metadata[StandardMetadataKeys.Title] = "SDR Radio";
    _metadata[StandardMetadataKeys.Artist] = "RTL-SDR";
    _metadata[StandardMetadataKeys.Album] = CurrentBand.ToString();
    UpdateMetadataFromState();
  }

  /// <summary>
  /// Updates metadata based on current radio state.
  /// </summary>
  private void UpdateMetadataFromState()
  {
    _metadata[StandardMetadataKeys.Album] = CurrentBand.ToString();
    _metadata["Frequency"] = CurrentFrequency.ToDisplayString();
    _metadata["SignalStrength"] = $"{SignalStrength}%";
    _metadata["Stereo"] = IsStereo ? "Yes" : "No";
  }

  /// <summary>
  /// Maps RTLSDRCore RadioBand to Radio.Core RadioBand.
  /// </summary>
  private static RadioBand MapBandFromRTLSDR(RTLSDRCore.Models.RadioBand rtlBand)
  {
    return rtlBand.Type switch
    {
      RTLSDRCore.Enums.BandType.FM => RadioBand.FM,
      RTLSDRCore.Enums.BandType.AM => RadioBand.AM,
      RTLSDRCore.Enums.BandType.Weather => RadioBand.WB,
      RTLSDRCore.Enums.BandType.Aircraft => RadioBand.AIR,
      RTLSDRCore.Enums.BandType.Shortwave => RadioBand.SW,
      RTLSDRCore.Enums.BandType.VHF => RadioBand.VHF,
      _ => RadioBand.FM
    };
  }

  /// <summary>
  /// Maps Radio.Core RadioBand to RTLSDRCore BandType.
  /// </summary>
  private static RTLSDRCore.Enums.BandType MapBandTypeToRTLSDR(RadioBand band)
  {
    return band switch
    {
      RadioBand.FM => RTLSDRCore.Enums.BandType.FM,
      RadioBand.AM => RTLSDRCore.Enums.BandType.AM,
      RadioBand.WB => RTLSDRCore.Enums.BandType.Weather,
      RadioBand.AIR => RTLSDRCore.Enums.BandType.Aircraft,
      RadioBand.VHF => RTLSDRCore.Enums.BandType.VHF,
      RadioBand.SW => RTLSDRCore.Enums.BandType.Shortwave,
      _ => throw new ArgumentException($"Unsupported band: {band}", nameof(band))
    };
  }

  #endregion

  #region PrimaryAudioSourceBase Overrides

  /// <inheritdoc/>
  public override Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    // Initialization is handled by StartupAsync
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override async Task PlayCoreAsync(CancellationToken cancellationToken = default)
  {
    Logger.LogInformation("📻 SDR RADIO: Starting playback (Frequency: {Frequency}, Band: {Band})",
      CurrentFrequency.ToDisplayString(), CurrentBand);

    // Reset audio flow tracking
    _hasLoggedFirstAudioData = false;
    _totalSamplesReceived = 0;

    // Start the RTL-SDR receiver first
    await StartupAsync(cancellationToken);
    Logger.LogDebug("📻 SDR RADIO: RTL-SDR receiver started, IsRunning={IsRunning}", IsRunning);

    // Generate a playback ID for this session
    _playbackId = $"sdr-radio-{Guid.NewGuid():N}";

    // Use SoundFlow playback service to route audio to output
    if (_playbackService != null)
    {
      var engine = _playbackService.GetUnderlyingEngine();
      if (engine == null)
      {
        Logger.LogError("📻 SDR RADIO: SoundFlow audio engine not available - SDR audio output will not work");
        return;
      }

      var format = _playbackService.GetAudioFormat();
      Logger.LogDebug("📻 SDR RADIO: SoundFlow format - SampleRate={SampleRate}, Channels={Channels}",
        format.SampleRate, format.Channels);

      // Create the sound generator with proper engine and format context
      if (_soundGenerator == null)
      {
        _soundGenerator = new BufferedSoundGenerator<float>(engine, format, Logger);
        _radioReceiver.AudioDataAvailable += OnAudioDataAvailable;
        Logger.LogDebug("📻 SDR RADIO: Created BufferedSoundGenerator and subscribed to AudioDataAvailable");
      }

      // Use PlayComponentAsync for raw PCM audio (not PlayStreamAsync which expects encoded audio)
      var success = await _playbackService.PlayComponentAsync(
        _playbackId,
        _soundGenerator,
        Volume,
        cancellationToken);

      if (!success)
      {
        Logger.LogError("📻 SDR RADIO: Failed to add sound component to SoundFlow mixer");
      }
      else
      {
        Logger.LogInformation(
          "📻 SDR RADIO: Audio pipeline ready - waiting for audio data from RTL-SDR " +
          "(PlaybackId={PlaybackId}, Volume={Volume:P0})",
          _playbackId, Volume);
      }
    }
    else
    {
      Logger.LogWarning("📻 SDR RADIO: SoundFlowPlaybackService not available - SDR audio output may not work");
    }
  }

  /// <inheritdoc/>
  protected override Task PauseCoreAsync(CancellationToken cancellationToken = default)
  {
    // Radio doesn't support pause - just mute instead
    IsMuted = true;
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override Task ResumeCoreAsync(CancellationToken cancellationToken = default)
  {
    // Unmute to "resume"
    IsMuted = false;
    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  protected override async Task StopCoreAsync(CancellationToken cancellationToken = default)
  {
    Logger.LogInformation(
      "📻 SDR RADIO AUDIO FLOW STOPPED: Stopping playback " +
      "(Frequency: {Frequency}, Band: {Band}, TotalSamplesProcessed: {Samples})",
      CurrentFrequency.ToDisplayString(), CurrentBand, _totalSamplesReceived);

    // Unsubscribe from audio events before stopping
    _radioReceiver.AudioDataAvailable -= OnAudioDataAvailable;

    // Stop SoundFlow playback first (this disposes the sound generator)
    if (_playbackService != null && _playbackId != null)
    {
      Logger.LogDebug("📻 SDR RADIO: Removing from SoundFlow mixer (PlaybackId={PlaybackId})", _playbackId);
      await _playbackService.StopAsync(_playbackId, cancellationToken);
      _playbackId = null;
      _soundGenerator = null; // Clear reference - generator was disposed by StopAsync
    }

    // Then shutdown the radio receiver
    await ShutdownAsync(cancellationToken);
  }

  /// <inheritdoc/>
  protected override async ValueTask DisposeAsyncCore()
  {
    // Unsubscribe from RTLSDRCore events
    _radioReceiver.FrequencyChanged -= OnRTLSDRFrequencyChanged;
    _radioReceiver.SignalStrengthUpdated -= OnRTLSDRSignalStrengthUpdated;
    _radioReceiver.StateChanged -= OnRTLSDRStateChanged;
    _radioReceiver.AudioDataAvailable -= OnAudioDataAvailable;

    // Unsubscribe from fingerprinting events
    if (_identificationService != null)
    {
      _identificationService.TrackIdentified -= OnTrackIdentified;
    }

    // Stop SoundFlow playback (this will also dispose the component)
    if (_playbackService != null && _playbackId != null)
    {
      await _playbackService.StopAsync(_playbackId);
      _playbackId = null;
      _soundGenerator = null; // Disposed by StopAsync
    }
    else
    {
      // Manually dispose if not managed by playback service
      _soundGenerator?.Dispose();
      _soundGenerator = null;
    }

    // Clean up scan resources
    _scanCts?.Dispose();
  }

  #endregion
}
