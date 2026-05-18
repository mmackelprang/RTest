using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using Radio.Metrics;
using Radio.Fingerprinting.Services;
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
  private readonly IConfigurationManager? _configurationManager;
  private readonly Dictionary<string, object> _metadata = new();
  private Frequency _frequencyStep;
  private int _deviceVolume;
  private bool _isScanning;
  private ScanDirection? _scanDirection;
  private Task? _scanTask;
  private CancellationTokenSource? _scanCts;
  private BufferedSoundGenerator<float>? _soundGenerator;
  private string? _playbackId;
  private bool _hasRestoredPreferences;

  // PR D #40 — tracks the last-stable RDS Program-Service (PS) value so the
  // save-preset dialog and history records don't seed with mid-roll fragments.
  // Observe() is called inside the RdsStationNameStable getter — each poll
  // of the property pushes the current live PS into the tracker, and after
  // WindowSize identical samples the consensus value is emitted.
  private readonly RdsStationNameStabilityTracker _rdsStationNameStabilityTracker
    = new RdsStationNameStabilityTracker(windowSize: 3);

  /// <summary>
  /// Initializes a new instance of the <see cref="SDRRadioAudioSource"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="radioReceiver">The RTLSDRCore radio receiver.</param>
  /// <param name="radioOptions">Radio configuration options.</param>
  /// <param name="metricsCollector">Optional metrics collector for tracking radio operations.</param>
  /// <param name="identificationService">Optional fingerprinting service for track identification.</param>
  /// <param name="playbackService">Optional SoundFlow playback service for audio output.</param>
  /// <param name="configurationManager">Optional configuration manager for restoring persisted preferences.</param>
  public SDRRadioAudioSource(
    ILogger<SDRRadioAudioSource> logger,
    RadioReceiver radioReceiver,
    IOptionsMonitor<RadioOptions> radioOptions,
    IMetricsCollector? metricsCollector = null,
    BackgroundIdentificationService? identificationService = null,
    SoundFlowPlaybackService? playbackService = null,
    IConfigurationManager? configurationManager = null)
    : base(logger, metricsCollector)
  {
    _radioReceiver = radioReceiver ?? throw new ArgumentNullException(nameof(radioReceiver));
    _radioOptions = radioOptions ?? throw new ArgumentNullException(nameof(radioOptions));
    _identificationService = identificationService;
    _playbackService = playbackService;
    _configurationManager = configurationManager;

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
  private long _silentCallbackCount;
  private Timer? _diagnosticTimer;

  // Pre-allocated stereo buffer for mono→stereo conversion (avoids per-callback allocation)
  private float[] _stereoBuffer = Array.Empty<float>();

  /// <summary>
  /// Handles audio data events from the RTL-SDR receiver.
  /// Delegates samples to the buffered sound generator.
  /// </summary>
  private void OnAudioDataAvailable(object? sender, AudioDataEventArgs e)
  {
    if (_soundGenerator == null)
    {
      return;
    }

    var sampleCount = e.SampleCount;

    if (e.Format.Channels >= 2)
    {
      // Native stereo from receiver (WFM stereo decode) — already interleaved L,R.
      // SampleCount is the interleaved count (L,R,L,R,...), pass through directly.
      _soundGenerator.AddSamples(e.Samples.AsSpan(0, sampleCount));
      _totalSamplesReceived += sampleCount / 2; // track mono-equivalent count
    }
    else
    {
      // Mono audio — duplicate each sample to both L and R channels.
      var stereoCount = sampleCount * 2;

      // Grow the pre-allocated stereo buffer if needed (rarely happens after first call)
      if (_stereoBuffer.Length < stereoCount)
      {
        _stereoBuffer = new float[stereoCount];
      }

      for (int i = 0; i < sampleCount; i++)
      {
        _stereoBuffer[i * 2] = e.Samples[i];     // Left
        _stereoBuffer[i * 2 + 1] = e.Samples[i]; // Right
      }

      _soundGenerator.AddSamples(_stereoBuffer.AsSpan(0, stereoCount));
      _totalSamplesReceived += sampleCount;
    }

    // Silence detection: warn if all samples are zero
    bool allSilent = true;
    for (int i = 0; i < sampleCount; i++)
    {
      if (Math.Abs(e.Samples[i]) > float.Epsilon)
      {
        allSilent = false;
        break;
      }
    }

    if (allSilent && sampleCount > 0)
    {
      _silentCallbackCount++;
      if (_silentCallbackCount % 100 == 1)
      {
        Logger.LogDebug(
          "SDR RADIO: Silent audio data received ({SampleCount} zero samples, silent callbacks: {SilentCount})",
          sampleCount, _silentCallbackCount);
      }
    }
    else
    {
      _silentCallbackCount = 0;
    }

    // Log first audio data received
    if (!_hasLoggedFirstAudioData)
    {
      _hasLoggedFirstAudioData = true;
      Logger.LogInformation(
        "SDR RADIO AUDIO FLOW STARTED: First audio data received from RTL-SDR " +
        "(Frequency: {Frequency}, Band: {Band}, {SampleCount} samples, {Channels}ch)",
        CurrentFrequency.ToDisplayString(), CurrentBand, sampleCount, e.Format.Channels);
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
  public int ScanStopThreshold => _radioOptions.CurrentValue.ScanStopThreshold;

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
        // Convert ScanStopThreshold (0-100 UI scale) to 0.0-1.0 for RadioReceiver
        var threshold = _radioOptions.CurrentValue.ScanStopThreshold / 100.0f;
        if (direction == Radio.Core.Models.Audio.ScanDirection.Up)
        {
          _radioReceiver.ScanFrequencyUp(_frequencyStep.Hertz, threshold);
        }
        else
        {
          _radioReceiver.ScanFrequencyDown(_frequencyStep.Hertz, threshold);
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
  public bool IsStereo => _radioReceiver.StereoDetected; // true when 19 kHz pilot detected

  /// <inheritdoc/>
  public string? RdsStationName => _radioReceiver.RdsStationName;

  /// <inheritdoc/>
  /// <remarks>
  /// Each read pushes the current live PS into the stability tracker so the
  /// broadcast loop (AudioStateUpdateService at ~1 Hz) naturally drives the
  /// 3-consecutive-identical-samples window. The property returns the
  /// consensus value, or null until consensus is reached.
  /// </remarks>
  public string? RdsStationNameStable
  {
    get
    {
      _rdsStationNameStabilityTracker.Observe(_radioReceiver.RdsStationName);
      return _rdsStationNameStabilityTracker.Stable;
    }
  }

  /// <inheritdoc/>
  public string? RdsProgramType => _radioReceiver.RdsProgramTypeName;

  /// <inheritdoc/>
  public string? RdsRadioText => _radioReceiver.RdsRadioText;

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

    // PR D #40 — the previous station's stable PS is no longer meaningful
    // after a re-tune. Reset so the next station has to earn its own
    // consensus from scratch.
    _rdsStationNameStabilityTracker.Reset();

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
    if (!string.IsNullOrEmpty(track.CoverArtUrl))
    {
      _metadata[StandardMetadataKeys.AlbumArtUrl] = track.CoverArtUrl;
      Logger.LogInformation("Album art URL set for '{Title}': {Url}", track.Title, track.CoverArtUrl);
    }
    else
    {
      _metadata[StandardMetadataKeys.AlbumArtUrl] = StandardMetadataKeys.DefaultAlbumArtUrl;
    }

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

  #region Diagnostics

  /// <summary>
  /// Periodic callback that logs SDR pipeline diagnostic information.
  /// </summary>
  private void LogPipelineDiagnostics(object? state)
  {
    var generatorDiag = _soundGenerator?.GetDiagnostics();
    Logger.LogDebug(
      "📻 SDR RADIO DIAGNOSTICS: TotalSamplesReceived={Received}, " +
      "GeneratorPresent={GeneratorPresent}, ReceiverRunning={ReceiverRunning}, " +
      "BufferReceived={BufReceived}, BufferOutput={BufOutput}, " +
      "BufferDropped={BufDropped}, BufferCount={BufCount}/{BufCapacity}",
      _totalSamplesReceived,
      _soundGenerator != null,
      _radioReceiver.IsRunning,
      generatorDiag?.TotalReceived ?? 0,
      generatorDiag?.TotalOutput ?? 0,
      generatorDiag?.TotalDropped ?? 0,
      generatorDiag?.BufferCount ?? 0,
      generatorDiag?.BufferCapacity ?? 0);
  }

  #endregion

  #region Preference Restoration

  /// <summary>
  /// Restores persisted radio preferences (band, frequency, step) from the config store.
  /// Falls back to RadioOptions defaults if no persisted values exist.
  /// Mirrors the pattern in AudioPreferencePersistence.RestoreVolumePreferences().
  /// </summary>
  private async Task RestorePersistedRadioPreferencesAsync(CancellationToken cancellationToken)
  {
    if (_configurationManager == null)
    {
      Logger.LogDebug("📻 SDR RADIO: No configuration manager available, using default preferences");
      return;
    }

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      var store = await _configurationManager.GetStoreAsync(storeId, cancellationToken);

      // Read persisted values — stored as Hz by PreferencesPersistenceService
      var bandEntry = await store.GetEntryAsync("Radio:LastBand", ct: cancellationToken);
      var freqEntry = await store.GetEntryAsync("Radio:LastFrequency", ct: cancellationToken);
      var stepEntry = await store.GetEntryAsync("Radio:LastFrequencyStep", ct: cancellationToken);

      // If no persisted frequency exists, this is a fresh install — use defaults
      if (freqEntry == null || string.IsNullOrEmpty(freqEntry.Value))
      {
        Logger.LogDebug("📻 SDR RADIO: No persisted radio preferences found, using defaults");
        return;
      }

      // Restore band first (frequency ranges depend on band)
      if (bandEntry != null && !string.IsNullOrEmpty(bandEntry.Value)
          && Enum.TryParse<RadioBand>(bandEntry.Value, out var savedBand))
      {
        Logger.LogInformation("📻 SDR RADIO: Restoring persisted band: {Band}", savedBand);
        await SetBandAsync(savedBand, cancellationToken);
      }

      // Restore frequency (stored as Hz)
      if (double.TryParse(freqEntry.Value, out var savedFreqHz) && savedFreqHz > 0)
      {
        var savedFreq = new Frequency((long)savedFreqHz);
        Logger.LogInformation("📻 SDR RADIO: Restoring persisted frequency: {Frequency}", savedFreq.ToDisplayString());
        await SetFrequencyAsync(savedFreq, cancellationToken);
      }

      // Restore frequency step (stored as Hz)
      if (stepEntry != null && !string.IsNullOrEmpty(stepEntry.Value)
          && double.TryParse(stepEntry.Value, out var savedStepHz) && savedStepHz > 0)
      {
        _frequencyStep = new Frequency((long)savedStepHz);
        Logger.LogDebug("📻 SDR RADIO: Restoring persisted frequency step: {Step}", _frequencyStep.ToDisplayString());
      }

      Logger.LogInformation(
        "📻 SDR RADIO: Restored persisted preferences - Band={Band}, Frequency={Frequency}, Step={Step}",
        CurrentBand, CurrentFrequency.ToDisplayString(), _frequencyStep.ToDisplayString());
    }
    catch (Exception ex)
    {
      Logger.LogWarning(ex, "📻 SDR RADIO: Failed to restore persisted preferences, using defaults");
    }
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

    // Generate a playback ID for this session
    _playbackId = $"sdr-radio-{Guid.NewGuid():N}";

    // On first play, restore persisted band/frequency/step from the config store.
    // This runs once per source lifetime so subsequent play/resume cycles don't re-read.
    if (!_hasRestoredPreferences)
    {
      _hasRestoredPreferences = true;
      await RestorePersistedRadioPreferencesAsync(cancellationToken);
    }

    // Set up the audio output pipeline BEFORE starting the receiver.
    // This ensures the BufferedSoundGenerator is ready to accept samples
    // from the very first USB callback, preventing data loss during startup.
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

      // Create the sound generator and subscribe to audio events BEFORE
      // starting the receiver, so no USB callbacks are missed.
      if (_soundGenerator == null)
      {
        _soundGenerator = new BufferedSoundGenerator<float>(engine, format, Logger, metricsCollector: MetricsCollector);
        _radioReceiver.AudioDataAvailable += OnAudioDataAvailable;
        Logger.LogDebug("📻 SDR RADIO: Created BufferedSoundGenerator and subscribed to AudioDataAvailable");
      }

      // Pre-fill with 1.5s of silence. The RTL-SDR USB device takes ~1s to
      // start delivering data after Startup(). This cushion keeps the mixer
      // fed during that gap and absorbs ongoing USB transfer jitter,
      // clock drift between the RTL-SDR crystal and the system audio clock,
      // and CPU spikes from Blazor rendering / SongRec identification.
      _soundGenerator.PreFillSilence(1.5f);

      // Start the RTL-SDR receiver — USB callbacks begin arriving and
      // accumulate in the buffer on top of the silence cushion.
      await StartupAsync(cancellationToken);
      Logger.LogDebug("📻 SDR RADIO: RTL-SDR receiver started, IsRunning={IsRunning}", IsRunning);

      // Now add to mixer — consumption begins with a 1s head start.
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

        // Start periodic diagnostic logging
        _diagnosticTimer = new Timer(LogPipelineDiagnostics, null,
          TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
      }
    }
    else
    {
      Logger.LogWarning("📻 SDR RADIO: SoundFlowPlaybackService not available - SDR audio output may not work");
      await StartupAsync(cancellationToken);
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

    // Stop diagnostic timer
    _diagnosticTimer?.Dispose();
    _diagnosticTimer = null;

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
    // Stop diagnostic timer
    _diagnosticTimer?.Dispose();
    _diagnosticTimer = null;

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
