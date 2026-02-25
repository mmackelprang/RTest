using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Events;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting;

/// <summary>
/// Background service that periodically identifies audio from active sources.
/// Exposes real-time status via <see cref="GetStatus"/> and <see cref="StatusChanged"/>.
/// </summary>
public sealed class BackgroundIdentificationService : BackgroundService
{
  private readonly ILogger<BackgroundIdentificationService> _logger;
  private readonly IServiceProvider _serviceProvider;
  private readonly FingerprintingOptions _options;

  // Track recent identifications for duplicate suppression (key → (timestamp, confidence))
  private readonly ConcurrentDictionary<string, (DateTime Timestamp, double Confidence)> _recentIdentifications = new();

  // Song change detection: track last identified track to detect transitions
  private (string TrackKey, TrackMetadata Track, DateTime IdentifiedAt)? _lastIdentification;
  private DateTime _lastSongChangeAt = DateTime.MinValue;

  // On-demand identification trigger — cancels the current delay to identify immediately
  private CancellationTokenSource? _delayCts;

  // --- Fingerprint status tracking ---
  private readonly object _statusLock = new();
  private FingerprintPhase _currentPhase = FingerprintPhase.Idle;
  private string? _lastError;
  private string? _currentSourceName;

  // Event log — circular buffer of recent events, capped at MaxRecentEvents
  private const int MaxRecentEvents = 20;
  private readonly List<FingerprintEventRecord> _recentEvents = new(MaxRecentEvents + 1);
  private FingerprintEventRecord? _currentEvent;

  // Rate tracking — timestamps within the rolling window used to compute per-minute rates
  private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(5);
  private readonly List<DateTime> _fingerprintTimestamps = new();
  private readonly List<DateTime> _metadataCallTimestamps = new();

  /// <summary>
  /// Event raised when a track is identified.
  /// </summary>
  public event EventHandler<TrackIdentifiedEventArgs>? TrackIdentified;

  /// <summary>
  /// Event raised when a song change is detected (different track identified than previous).
  /// </summary>
  public event EventHandler<SongChangedEventArgs>? SongChanged;

  /// <summary>
  /// Event raised when the fingerprint status changes (phase, event log, rates).
  /// </summary>
  public event EventHandler<FingerprintStatusSnapshot>? StatusChanged;

  /// <summary>
  /// Initializes a new instance of the <see cref="BackgroundIdentificationService"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="serviceProvider">The service provider for resolving scoped services.</param>
  /// <param name="options">The fingerprinting options.</param>
  public BackgroundIdentificationService(
    ILogger<BackgroundIdentificationService> logger,
    IServiceProvider serviceProvider,
    IOptions<FingerprintingOptions> options)
  {
    _logger = logger;
    _serviceProvider = serviceProvider;
    _options = options.Value;
  }

  /// <summary>
  /// Returns the current fingerprint identification status snapshot.
  /// </summary>
  public FingerprintStatusSnapshot GetStatus()
  {
    lock (_statusLock)
    {
      PruneRateTimestamps();
      return new FingerprintStatusSnapshot
      {
        Phase = _currentPhase,
        IsEnabled = _options.Enabled,
        FingerprintsPerMinute = ComputeRate(_fingerprintTimestamps),
        MetadataCallsPerMinute = ComputeRate(_metadataCallTimestamps),
        RecentEvents = _recentEvents.ToList().AsReadOnly(),
        LastError = _lastError
      };
    }
  }

  /// <summary>
  /// Requests an immediate identification cycle, bypassing the normal interval wait.
  /// Called by sources when a track changes or new incomplete metadata is received.
  /// </summary>
  public void RequestImmediateIdentification()
  {
    _logger.LogDebug("Immediate identification requested");
    try
    {
      _delayCts?.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // Timer already disposed, ignore
    }
  }

  /// <inheritdoc/>
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!_options.Enabled)
    {
      _logger.LogInformation("Audio fingerprinting is disabled");
      return;
    }

    _logger.LogInformation(
      "Background identification service started (interval: {Interval}s, sample duration: {Duration}s)",
      _options.IdentificationIntervalSeconds,
      _options.SampleDurationSeconds);

    // Initial delay to let the audio engine initialize
    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await IdentifyCurrentAudioAsync(stoppingToken);

        // Clean up old entries from duplicate suppression cache
        CleanupRecentIdentifications();
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error during audio identification");
        UpdatePhase(FingerprintPhase.Error, ex.Message);
      }

      try
      {
        UpdatePhase(FingerprintPhase.Idle);
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _delayCts = delayCts;
        await Task.Delay(
          TimeSpan.FromSeconds(_options.IdentificationIntervalSeconds),
          delayCts.Token);
        _delayCts = null;
      }
      catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
      {
        _logger.LogDebug("Identification interval interrupted by immediate request");
        _delayCts = null;
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }

    _logger.LogInformation("Background identification service stopped");
  }

  private async Task IdentifyCurrentAudioAsync(CancellationToken ct)
  {
    _logger.LogDebug("Starting identification cycle");
    var cycleStartTime = DateTime.UtcNow;

    // Resolve services from scope
    using var scope = _serviceProvider.CreateScope();
    var audioTap = scope.ServiceProvider.GetService<IAudioSampleProvider>();
    var fingerprintService = scope.ServiceProvider.GetService<IFingerprintService>();
    var lookupService = scope.ServiceProvider.GetService<IMetadataLookupService>();

    if (audioTap == null || fingerprintService == null || lookupService == null)
    {
      _logger.LogWarning("Required services not available for fingerprinting. AudioTap={HasAudioTap}, FingerprintService={HasFingerprint}, LookupService={HasLookup}",
        audioTap != null, fingerprintService != null, lookupService != null);
      return;
    }

    // Check if source is active
    if (!audioTap.IsActive)
    {
      _logger.LogDebug("Audio source not active, skipping identification");
      return;
    }

    // Check if the active source needs fingerprinting (complete metadata → skip)
    if (!audioTap.NeedsFingerprintingLookup)
    {
      _logger.LogDebug("Active source has complete metadata, skipping identification cycle");
      return;
    }

    _logger.LogDebug("Audio source active: {SourceType} - {SourceName}", audioTap.SourceType, audioTap.SourceName);

    // Start or continue an event record for this audio segment
    var sourceName = audioTap.SourceName ?? audioTap.SourceType.ToString();
    EnsureCurrentEvent(sourceName);
    UpdatePhase(FingerprintPhase.Capturing);

    FingerprintData fingerprint;

    // For file-based sources, fingerprint the actual file to get correct track duration.
    // AcoustID requires duration within ~3s of the real track length to match.
    var sourceFilePath = audioTap.SourceFilePath;
    if (!string.IsNullOrEmpty(sourceFilePath))
    {
      _logger.LogDebug("File source detected, fingerprinting file directly: {FilePath}", sourceFilePath);
      UpdatePhase(FingerprintPhase.Fingerprinting);
      var fingerprintStartTime = DateTime.UtcNow;
      fingerprint = await fingerprintService.GenerateFingerprintFromFileAsync(sourceFilePath, ct);
      var fingerprintElapsed = (DateTime.UtcNow - fingerprintStartTime).TotalMilliseconds;

      if (string.IsNullOrEmpty(fingerprint.ChromaprintHash))
      {
        _logger.LogWarning("Failed to generate fingerprint from file: {FilePath}", sourceFilePath);
        UpdatePhase(FingerprintPhase.Error, "Failed to generate fingerprint from file");
        return;
      }

      RecordFingerprintGenerated();
      _logger.LogDebug("Generated fingerprint {Id} from file (duration={Duration}s) in {Elapsed}ms",
        fingerprint.Id, fingerprint.DurationSeconds, fingerprintElapsed);
    }
    else
    {
      // For non-file sources (radio, vinyl, Bluetooth), capture from the output tap.
      // Note: AcoustID matching may be limited since we can't determine the full track duration.
      var captureStartTime = DateTime.UtcNow;
      var sampleDuration = TimeSpan.FromSeconds(_options.SampleDurationSeconds);
      _logger.LogDebug("Attempting to capture {Duration}s of audio", sampleDuration.TotalSeconds);

      var samples = await audioTap.CaptureAsync(sampleDuration, ct);
      var captureElapsed = (DateTime.UtcNow - captureStartTime).TotalMilliseconds;

      if (samples == null)
      {
        _logger.LogWarning("No audio samples captured after {Elapsed}ms", captureElapsed);
        UpdatePhase(FingerprintPhase.Error, "No audio samples captured");
        return;
      }

      _logger.LogDebug("Captured {SampleCount} audio samples in {Elapsed}ms", samples.Samples.Length, captureElapsed);

      UpdatePhase(FingerprintPhase.Fingerprinting);
      var fingerprintStartTime = DateTime.UtcNow;
      fingerprint = await fingerprintService.GenerateFingerprintAsync(samples, ct);
      var fingerprintElapsed = (DateTime.UtcNow - fingerprintStartTime).TotalMilliseconds;

      RecordFingerprintGenerated();
      _logger.LogDebug("Generated fingerprint {Id} for {Duration}s of audio in {Elapsed}ms",
        fingerprint.Id, fingerprint.DurationSeconds, fingerprintElapsed);

      _logger.LogInformation(
        "Live source fingerprint: duration={Duration}s, hash length={HashLength} chars",
        fingerprint.DurationSeconds, fingerprint.ChromaprintHash.Length);
    }

    // Lookup metadata — for live sources, try multiple duration values if the initial
    // lookup fails. AcoustID uses duration to narrow the search space, and for live
    // captures (vinyl, radio) the capture duration (e.g., 30s) doesn't match the actual
    // track length in the database (e.g., 220s). Try common song durations as fallbacks.
    UpdatePhase(FingerprintPhase.Querying);
    var lookupStartTime = DateTime.UtcNow;
    _logger.LogDebug("Looking up fingerprint via {LookupService}", lookupService.GetType().Name);

    RecordMetadataCall();
    var result = await lookupService.LookupAsync(fingerprint, ct);
    var lookupElapsed = (DateTime.UtcNow - lookupStartTime).TotalMilliseconds;

    _logger.LogDebug("Lookup completed in {Elapsed}ms. Match={IsMatch}, Confidence={Confidence}",
      lookupElapsed, result?.IsMatch ?? false, result?.Confidence);

    // For live sources: if no match with actual capture duration, try common song durations
    if (result?.IsMatch != true && string.IsNullOrEmpty(sourceFilePath))
    {
      int[] fallbackDurations = [180, 210, 240, 270, 300];
      foreach (var tryDuration in fallbackDurations)
      {
        if (tryDuration == fingerprint.DurationSeconds) continue;

        var fallbackFingerprint = new FingerprintData
        {
          Id = fingerprint.Id,
          ChromaprintHash = fingerprint.ChromaprintHash,
          DurationSeconds = tryDuration,
          GeneratedAt = fingerprint.GeneratedAt,
          SourcePath = fingerprint.SourcePath
        };

        _logger.LogInformation("Retrying AcoustID with duration={Duration}s", tryDuration);
        RecordMetadataCall();
        var fallbackResult = await lookupService.LookupAsync(fallbackFingerprint, ct);

        if (fallbackResult?.IsMatch == true)
        {
          _logger.LogInformation(
            "AcoustID matched with fallback duration={Duration}s (original={Original}s)",
            tryDuration, fingerprint.DurationSeconds);
          result = fallbackResult;
          break;
        }
      }

      lookupElapsed = (DateTime.UtcNow - lookupStartTime).TotalMilliseconds;
    }

    // Update event record with result
    if (result?.IsMatch == true && result.Metadata != null)
    {
      var trackKey = $"{result.Metadata.Title}|{result.Metadata.Artist}";
      UpdateCurrentEventMatch(result.Metadata, result.Confidence);
      UpdatePhase(FingerprintPhase.Matched);

      // Check duplicate suppression
      if (IsDuplicateIdentification(trackKey))
      {
        _logger.LogDebug("Suppressing duplicate identification: {Title} by {Artist}",
          result.Metadata.Title, result.Metadata.Artist);
        return;
      }

      MarkAsRecentlyIdentified(trackKey, result.Confidence);

      // Song change detection: compare with last identification
      DetectSongChange(trackKey, result.Metadata, result.Confidence);
    }
    else
    {
      UpdateCurrentEventNoMatch();
      UpdatePhase(FingerprintPhase.NoMatch);
    }

    // Raise event for UI updates and play history updates
    if (result?.IsMatch == true && result.Metadata != null)
    {
      _logger.LogInformation(
        "Identified track: '{Title}' by '{Artist}' (confidence: {Confidence:P0}, source: {Source}, coverArt: {CoverArtUrl})",
        result.Metadata.Title, result.Metadata.Artist, result.Confidence,
        result.Source, result.Metadata.CoverArtUrl ?? "(none)");

      TrackIdentified?.Invoke(this, new TrackIdentifiedEventArgs(result.Metadata, result.Confidence));
    }
    else
    {
      _logger.LogDebug("Track not identified via fingerprinting");
    }

    var totalElapsed = (DateTime.UtcNow - cycleStartTime).TotalMilliseconds;
    _logger.LogDebug("Identification cycle completed in {TotalElapsed}ms (lookup: {Lookup}ms)",
      totalElapsed, lookupElapsed);
  }

  /// <summary>
  /// Resets the song change detection state.
  /// Call when the audio source changes to prevent false song-change events.
  /// </summary>
  public void ResetSongChangeState()
  {
    _lastIdentification = null;
    _lastSongChangeAt = DateTime.MinValue;
    _logger.LogDebug("Song change detection state reset");
  }

  // --- Status tracking helpers ---

  private void UpdatePhase(FingerprintPhase phase, string? error = null)
  {
    lock (_statusLock)
    {
      _currentPhase = phase;
      if (error != null)
        _lastError = error;
      if (_currentEvent != null)
      {
        _currentEvent.Phase = phase;
        _currentEvent.Timestamp = DateTime.UtcNow;
      }
    }
    FireStatusChanged();
  }

  /// <summary>
  /// Ensures a current event record exists for the given source.
  /// Only starts a new record when the source changes or after an error;
  /// same-source match/no-match results aggregate into the existing record.
  /// </summary>
  internal void EnsureCurrentEvent(string sourceName)
  {
    lock (_statusLock)
    {
      // Start a new record only if: no current event, source changed, or error (terminal)
      if (_currentEvent == null || _currentSourceName != sourceName ||
          _currentEvent.Phase == FingerprintPhase.Error)
      {
        _currentEvent = new FingerprintEventRecord { AudioSource = sourceName };
        _currentSourceName = sourceName;
        _recentEvents.Add(_currentEvent);
        if (_recentEvents.Count > MaxRecentEvents)
          _recentEvents.RemoveAt(0);
      }
    }
  }

  /// <summary>
  /// Updates the current event record with a successful match.
  /// If the same song matches again, aggregates (increments MatchCount, updates confidence).
  /// If a different song matches, starts a new event record.
  /// </summary>
  internal void UpdateCurrentEventMatch(TrackMetadata metadata, double confidence)
  {
    lock (_statusLock)
    {
      if (_currentEvent == null) return;

      // Different song identified → start a new record
      if (_currentEvent.Title != null && _currentEvent.Title != metadata.Title)
      {
        _currentEvent = new FingerprintEventRecord { AudioSource = _currentSourceName ?? "Unknown" };
        _recentEvents.Add(_currentEvent);
        if (_recentEvents.Count > MaxRecentEvents)
          _recentEvents.RemoveAt(0);
      }

      _currentEvent.MatchCount++;
      _currentEvent.LastConfidence = confidence;
      _currentEvent.Title = metadata.Title;
      _currentEvent.Artist = metadata.Artist;
      _currentEvent.Album = metadata.Album;
      _currentEvent.FirstMatchAt ??= DateTime.UtcNow;
      _currentEvent.Timestamp = DateTime.UtcNow;
    }
  }

  /// <summary>
  /// Updates the current event record with a no-match result.
  /// Aggregates into the existing record (increments NoMatchCount).
  /// </summary>
  internal void UpdateCurrentEventNoMatch()
  {
    lock (_statusLock)
    {
      if (_currentEvent == null) return;
      _currentEvent.NoMatchCount++;
      _currentEvent.Timestamp = DateTime.UtcNow;
    }
  }

  private void RecordFingerprintGenerated()
  {
    lock (_statusLock)
    {
      _fingerprintTimestamps.Add(DateTime.UtcNow);
    }
  }

  private void RecordMetadataCall()
  {
    lock (_statusLock)
    {
      _metadataCallTimestamps.Add(DateTime.UtcNow);
    }
  }

  private void PruneRateTimestamps()
  {
    var cutoff = DateTime.UtcNow - RateWindow;
    _fingerprintTimestamps.RemoveAll(t => t < cutoff);
    _metadataCallTimestamps.RemoveAll(t => t < cutoff);
  }

  private static double ComputeRate(List<DateTime> timestamps)
  {
    if (timestamps.Count == 0) return 0;
    var oldest = timestamps[0];
    var elapsed = (DateTime.UtcNow - oldest).TotalMinutes;
    return elapsed > 0 ? timestamps.Count / elapsed : timestamps.Count;
  }

  private void FireStatusChanged()
  {
    try
    {
      StatusChanged?.Invoke(this, GetStatus());
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error firing StatusChanged event");
    }
  }

  // --- Existing helpers ---

  private void DetectSongChange(string trackKey, TrackMetadata newTrack, double confidence)
  {
    var now = DateTime.UtcNow;

    if (_lastIdentification == null)
    {
      // First identification — record it, no song change event
      _lastIdentification = (trackKey, newTrack, now);
      _logger.LogDebug("First identification recorded: '{Title}' by '{Artist}'",
        newTrack.Title, newTrack.Artist);
      return;
    }

    // Same track — update timestamp, no song change
    if (trackKey == _lastIdentification.Value.TrackKey)
    {
      _lastIdentification = (_lastIdentification.Value.TrackKey, _lastIdentification.Value.Track, now);
      return;
    }

    // Different track detected — check minimum interval to prevent rapid-fire events
    var secondsSinceLastChange = (now - _lastSongChangeAt).TotalSeconds;
    if (secondsSinceLastChange < _options.MinimumSecondsBetweenSongChanges)
    {
      _logger.LogDebug(
        "Song change suppressed (only {Seconds:F0}s since last change, minimum is {Min}s): '{OldTitle}' → '{NewTitle}'",
        secondsSinceLastChange, _options.MinimumSecondsBetweenSongChanges,
        _lastIdentification.Value.Track.Title, newTrack.Title);
      return;
    }

    // Song change confirmed!
    var previousTrack = _lastIdentification.Value.Track;
    _lastIdentification = (trackKey, newTrack, now);
    _lastSongChangeAt = now;

    _logger.LogInformation(
      "Song change detected: '{OldTitle}' by '{OldArtist}' -> '{NewTitle}' by '{NewArtist}' (confidence: {Confidence:P0})",
      previousTrack.Title, previousTrack.Artist,
      newTrack.Title, newTrack.Artist, confidence);

    SongChanged?.Invoke(this, new SongChangedEventArgs(previousTrack, newTrack, confidence));
  }

  private bool IsDuplicateIdentification(string trackKey)
  {
    if (_recentIdentifications.TryGetValue(trackKey, out var entry))
    {
      var elapsed = DateTime.UtcNow - entry.Timestamp;
      // High-confidence matches get a longer suppression window
      var suppressionMinutes = entry.Confidence > 0.9
        ? _options.HighConfidenceDuplicateSuppressionMinutes
        : _options.DuplicateSuppressionMinutes;
      return elapsed.TotalMinutes < suppressionMinutes;
    }

    return false;
  }

  private void MarkAsRecentlyIdentified(string trackKey, double confidence)
  {
    _recentIdentifications[trackKey] = (DateTime.UtcNow, confidence);
  }

  private void CleanupRecentIdentifications()
  {
    var maxSuppression = Math.Max(
      _options.DuplicateSuppressionMinutes,
      _options.HighConfidenceDuplicateSuppressionMinutes);
    var cutoff = DateTime.UtcNow.AddMinutes(-maxSuppression * 2);
    var keysToRemove = _recentIdentifications
      .Where(kvp => kvp.Value.Timestamp < cutoff)
      .Select(kvp => kvp.Key)
      .ToList();

    foreach (var key in keysToRemove)
    {
      _recentIdentifications.TryRemove(key, out _);
    }
  }
}
