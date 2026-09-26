using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Events;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Metrics;

namespace Radio.Fingerprinting.Services;

/// <summary>
/// Background service that periodically identifies audio from active sources using SongRec.
/// All source types (radio, file, vinyl, USB) use the same SongRec capture-and-recognize path.
/// Exposes real-time status via <see cref="GetStatus"/> and <see cref="StatusChanged"/>.
/// </summary>
public class BackgroundIdentificationService : BackgroundService
{
  private readonly ILogger<BackgroundIdentificationService> _logger;
  private readonly IServiceProvider _serviceProvider;
  private readonly IOptionsMonitor<FingerprintingOptions> _optionsMonitor;
  private readonly IMetricsCollector? _metricsCollector;

  /// <summary>Current fingerprinting options (live from IOptionsMonitor).</summary>
  private FingerprintingOptions _options => _optionsMonitor.CurrentValue;

  // Track recent identifications for duplicate suppression (key → (timestamp, confidence))
  private readonly ConcurrentDictionary<string, (DateTime Timestamp, double Confidence)> _recentIdentifications = new();

  // Song change detection: track last identified track to detect transitions
  private (string TrackKey, TrackMetadata Track, DateTime IdentifiedAt)? _lastIdentification;
  private DateTime _lastSongChangeAt = DateTime.MinValue;

  // On-demand identification trigger — cancels the current delay to identify immediately
  private CancellationTokenSource? _delayCts;

  // SongRec exponential backoff — increases delay after consecutive failures
  private int _consecutiveSongRecFailures;
  private static readonly int[] BackoffSeconds = [15, 30, 60, 120];

  // --- Fingerprint status tracking ---
  private readonly object _statusLock = new();
  private FingerprintPhase _currentPhase = FingerprintPhase.Idle;
  private string? _lastError;
  private string? _currentSourceName;

  // Cached status snapshot — only rebuilt when _eventsVersion changes.
  // GetStatus() is called every ~3s by the API; caching avoids rebuilding
  // 40 record copies + List + ReadOnlyCollection on every call.
  private long _eventsVersion;
  private FingerprintStatusSnapshot? _cachedSnapshot;
  private long _cachedSnapshotVersion = -1;

  // Event log — circular buffer of recent events, capped at MaxRecentEvents
  private const int MaxRecentEvents = 40;
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
  /// <param name="metricsCollector">Optional metrics collector.</param>
  public BackgroundIdentificationService(
    ILogger<BackgroundIdentificationService> logger,
    IServiceProvider serviceProvider,
    IOptionsMonitor<FingerprintingOptions> options,
    IMetricsCollector? metricsCollector = null)
  {
    _logger = logger;
    _serviceProvider = serviceProvider;
    _optionsMonitor = options;
    _metricsCollector = metricsCollector;
  }

  /// <summary>
  /// Returns the current fingerprint identification status snapshot.
  /// Uses a cached snapshot that is only rebuilt when events change.
  /// </summary>
  public FingerprintStatusSnapshot GetStatus()
  {
    lock (_statusLock)
    {
      if (_cachedSnapshot != null && _cachedSnapshotVersion == _eventsVersion)
      {
        // Snapshot is still valid — only refresh rate counters (cheap)
        PruneRateTimestamps();
        return _cachedSnapshot with
        {
          Phase = _currentPhase,
          FingerprintsPerMinute = ComputeRate(_fingerprintTimestamps),
          MetadataCallsPerMinute = ComputeRate(_metadataCallTimestamps),
          LastError = _lastError
        };
      }

      PruneRateTimestamps();
      _cachedSnapshot = new FingerprintStatusSnapshot
      {
        Phase = _currentPhase,
        IsEnabled = _options.Enabled,
        FingerprintsPerMinute = ComputeRate(_fingerprintTimestamps),
        MetadataCallsPerMinute = ComputeRate(_metadataCallTimestamps),
        RecentEvents = _recentEvents.Select(e => e with { }).ToList().AsReadOnly(),
        LastError = _lastError
      };
      _cachedSnapshotVersion = _eventsVersion;
      return _cachedSnapshot;
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

  /// <summary>
  /// Internal test hook: raises the <see cref="TrackIdentified"/> event without
  /// running a real SongRec identification cycle. Used by Infrastructure.Tests
  /// to verify downstream subscribers (e.g., BluetoothAudioSource's
  /// OnTrackIdentified handler) without spinning up the full audio pipeline.
  /// </summary>
  internal void RaiseTrackIdentifiedForTesting(TrackIdentifiedEventArgs e)
  {
    TrackIdentified?.Invoke(this, e);
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
      var captured = false;
      try
      {
        captured = await IdentifyCurrentAudioAsync(stoppingToken);

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

        TimeSpan delay;
        if (_consecutiveSongRecFailures > 0)
        {
          var delaySeconds = BackoffSeconds[Math.Min(_consecutiveSongRecFailures - 1, BackoffSeconds.Length - 1)];

          _logger.LogDebug("SongRec backoff: {BackoffSeconds}s after {Failures} consecutive failures",
            delaySeconds, _consecutiveSongRecFailures);

          _metricsCollector?.Gauge("fingerprint.consecutive_failures", _consecutiveSongRecFailures);
          delay = TimeSpan.FromSeconds(delaySeconds);
        }
        else if (!captured)
        {
          // AUD-35: a cycle that captured nothing returned without awaiting anything, so going straight
          // round again was a synchronous tight loop — one core at 99.9 % on the appliance, indefinitely.
          // RequestImmediateIdentification cancels this wait, so a track change is not delayed by it.
          delay = TimeSpan.FromMilliseconds(Math.Max(1, _options.IdlePollIntervalMs));
        }
        else
        {
          // The capture itself (SampleDurationSeconds) throttles a cycle that did work.
          continue;
        }

        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _delayCts = delayCts;
        await Task.Delay(delay, delayCts.Token);
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

  /// <returns>
  /// True when the cycle captured audio — which is what throttles the loop. False when it returned without
  /// capturing (no tap, inactive source, no lookup needed, no samples): the caller must then wait (AUD-35).
  /// </returns>
  private async Task<bool> IdentifyCurrentAudioAsync(CancellationToken ct)
  {
    _logger.LogDebug("Starting identification cycle");
    var cycleStartTime = DateTime.UtcNow;

    // Resolve services from scope
    using var scope = _serviceProvider.CreateScope();
    var audioTap = scope.ServiceProvider.GetService<IAudioSampleProvider>();

    if (audioTap == null)
    {
      _logger.LogWarning("Audio sample provider not available for fingerprinting");
      return false;
    }

    // Check if source is active and needs fingerprinting
    if (!audioTap.IsActive)
    {
      _logger.LogDebug("Audio source not active, skipping identification");
      return false;
    }

    if (!audioTap.NeedsFingerprintingLookup)
    {
      _logger.LogDebug("Source {SourceType} does not need fingerprinting, skipping", audioTap.SourceType);
      return false;
    }

    _logger.LogDebug("Audio source active: {SourceType} - {SourceName}", audioTap.SourceType, audioTap.SourceName);

    // Start or continue an event record for this audio segment
    var sourceName = audioTap.SourceName ?? audioTap.SourceType.ToString();
    var sourceType = audioTap.SourceType.ToString();
    EnsureCurrentEvent(sourceName, sourceType);
    UpdatePhase(FingerprintPhase.Capturing);

    var sourceTag = audioTap.SourceType.ToString().ToLowerInvariant();
    _metricsCollector?.Increment("fingerprint.identification_attempts", 1,
      new Dictionary<string, string> { ["source"] = sourceTag });

    // Unified path: Capture audio → SongRec recognition
    var captureStartTime = DateTime.UtcNow;
    var sampleDuration = TimeSpan.FromSeconds(_options.SampleDurationSeconds);
    _logger.LogDebug("Capturing {Duration}s of audio for SongRec", sampleDuration.TotalSeconds);

    var samples = await audioTap.CaptureAsync(sampleDuration, ct);
    var captureElapsed = (DateTime.UtcNow - captureStartTime).TotalMilliseconds;

    _metricsCollector?.Gauge("fingerprint.capture_latency_ms", captureElapsed);
    RecordFingerprintGenerated();

    if (samples == null)
    {
      _logger.LogWarning("No audio samples captured after {Elapsed}ms", captureElapsed);
      UpdatePhase(FingerprintPhase.Error, "No audio samples captured");
      _metricsCollector?.Increment("fingerprint.identification_failures", 1,
        new Dictionary<string, string> { ["source"] = sourceTag, ["reason"] = "error" });
      return false;
    }

    _logger.LogDebug("Captured {SampleCount} audio samples in {Elapsed}ms", samples.Samples.Length, captureElapsed);

    // SongRec recognition
    var songRec = scope.ServiceProvider.GetService<ISongRecRecognitionService>();
    if (songRec is not { IsAvailable: true })
    {
      _logger.LogWarning("SongRec not available for identification");
      UpdatePhase(FingerprintPhase.NoMatch);
      _metricsCollector?.Increment("fingerprint.identification_failures", 1,
        new Dictionary<string, string> { ["source"] = sourceTag, ["reason"] = "error" });
      return true;
    }

    UpdatePhase(FingerprintPhase.Querying);
    var lookupStartTime = DateTime.UtcNow;
    double lookupElapsed = 0;

    MetadataLookupResult? result = null;

    try
    {
      RecordMetadataCall();
      var songRecMetadata = await songRec.RecognizeAsync(samples, ct);
      lookupElapsed = (DateTime.UtcNow - lookupStartTime).TotalMilliseconds;

      _metricsCollector?.Gauge("fingerprint.songrec_latency_ms", lookupElapsed);

      if (songRecMetadata != null)
      {
        // Cache album art from Shazam CDN to serve locally
        if (!string.IsNullOrEmpty(songRecMetadata.CoverArtUrl))
        {
          try
          {
            var albumArtCache = scope.ServiceProvider.GetService<IAlbumArtCacheService>();
            if (albumArtCache != null)
            {
              var localPath = await albumArtCache.SaveFromUrlAsync(songRecMetadata.CoverArtUrl);
              if (localPath != null)
              {
                songRecMetadata = songRecMetadata with { CoverArtUrl = localPath };
              }
            }
          }
          catch (Exception ex)
          {
            _logger.LogDebug(ex, "Failed to cache album art from {Url}, keeping external URL",
              songRecMetadata.CoverArtUrl);
          }
        }

        result = new MetadataLookupResult
        {
          IsMatch = true,
          Confidence = 0.8, // SongRec doesn't provide a numeric confidence score
          FingerprintId = Guid.NewGuid().ToString(),
          Metadata = songRecMetadata,
          Source = LookupSource.SongRec
        };

        _consecutiveSongRecFailures = 0;
        _metricsCollector?.Gauge("fingerprint.consecutive_failures", 0);
        _metricsCollector?.Increment("fingerprint.identification_successes", 1,
          new Dictionary<string, string> { ["source"] = sourceTag });
      }
      else
      {
        _logger.LogInformation("SongRec returned no match");
        _metricsCollector?.Increment("fingerprint.identification_failures", 1,
          new Dictionary<string, string> { ["source"] = sourceTag, ["reason"] = "no_match" });
      }
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (TimeoutException)
    {
      lookupElapsed = (DateTime.UtcNow - lookupStartTime).TotalMilliseconds;
      _logger.LogWarning("SongRec recognition timed out");
      _consecutiveSongRecFailures++;
      _metricsCollector?.Increment("fingerprint.identification_failures", 1,
        new Dictionary<string, string> { ["source"] = sourceTag, ["reason"] = "timeout" });
    }
    catch (Exception ex)
    {
      lookupElapsed = (DateTime.UtcNow - lookupStartTime).TotalMilliseconds;
      _logger.LogWarning(ex, "SongRec recognition failed");
      _consecutiveSongRecFailures++;
      _metricsCollector?.Increment("fingerprint.identification_failures", 1,
        new Dictionary<string, string> { ["source"] = sourceTag, ["reason"] = "error" });
    }

    // Update event record with result
    if (result?.IsMatch == true && result.Metadata != null)
    {
      var trackKey = TrackKey(result.Metadata);
      UpdateCurrentEventMatch(result.Metadata, result.Confidence);
      UpdatePhase(FingerprintPhase.Matched);

      // Check duplicate suppression
      if (IsDuplicateIdentification(trackKey))
      {
        _logger.LogDebug("Suppressing duplicate identification: {Title} by {Artist}",
          result.Metadata.Title, result.Metadata.Artist);
        _metricsCollector?.Increment("fingerprint.duplicate_suppressions");
        return true;
      }

      MarkAsRecentlyIdentified(trackKey, result.Confidence);

      // Song change detection: compare with last identification
      DetectSongChange(trackKey, result.Metadata, result.Confidence, sourceTag);
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

      // AUD-33: carry the capture start so a source can drop a result sampled from a track the
      // listener has since skipped away from.
      TrackIdentified?.Invoke(
        this, new TrackIdentifiedEventArgs(result.Metadata, result.Confidence, captureStartTime));
    }
    else
    {
      _logger.LogDebug("Track not identified via fingerprinting");
    }

    var totalElapsed = (DateTime.UtcNow - cycleStartTime).TotalMilliseconds;
    _logger.LogDebug("Identification cycle completed in {TotalElapsed}ms (lookup: {Lookup}ms)",
      totalElapsed, lookupElapsed);
    return true;
  }

  /// <summary>
  /// Resets the song change detection state.
  /// Call when the audio source changes to prevent false song-change events.
  /// </summary>
  public void ResetSongChangeState()
  {
    _lastIdentification = null;
    _lastSongChangeAt = DateTime.MinValue;
    _consecutiveSongRecFailures = 0;
    _logger.LogDebug("Song change detection state reset");
  }

  // --- Status tracking helpers ---

  private void UpdatePhase(FingerprintPhase phase, string? error = null)
  {
    lock (_statusLock)
    {
      _currentPhase = phase;
      if (error != null)
      {
        _lastError = error;
      }
      if (_currentEvent != null)
      {
        _currentEvent.Phase = phase;
        _currentEvent.Timestamp = DateTime.UtcNow;
      }
      _eventsVersion++;
    }
    FireStatusChanged();
  }

  /// <summary>
  /// Ensures a current event record exists for the given source.
  /// Only starts a new record when the source changes or after an error;
  /// same-source match/no-match results aggregate into the existing record.
  /// </summary>
  internal void EnsureCurrentEvent(string sourceName, string sourceType = "")
  {
    lock (_statusLock)
    {
      // Start a new record only if: no current event, source changed, or error (terminal)
      if (_currentEvent == null || _currentSourceName != sourceName ||
          _currentEvent.Phase == FingerprintPhase.Error)
      {
        _currentEvent = new FingerprintEventRecord { AudioSource = sourceName, SourceType = sourceType };
        _currentSourceName = sourceName;
        _recentEvents.Add(_currentEvent);
        if (_recentEvents.Count > MaxRecentEvents)
        {
          _recentEvents.RemoveAt(0);
        }
        _eventsVersion++;
      }
    }
  }

  /// <summary>
  /// Updates the current event record with a successful match.
  /// Same source + same title → Count++; different title or was no-match → new row.
  /// </summary>
  internal void UpdateCurrentEventMatch(TrackMetadata metadata, double confidence)
  {
    lock (_statusLock)
    {
      if (_currentEvent == null)
      {
        return;
      }

      _eventsVersion++;

      // Aggregate if same source, same title, and already a match row
      if (_currentEvent.IsMatch && _currentEvent.Title == metadata.Title)
      {
        _currentEvent.Count++;
        _currentEvent.LastConfidence = confidence;
        _currentEvent.HasAlbumArt = !string.IsNullOrEmpty(metadata.CoverArtUrl);
        _currentEvent.Timestamp = DateTime.UtcNow;
        return;
      }

      // Fresh event from EnsureCurrentEvent (Count=0) → convert in place
      if (_currentEvent.Count == 0)
      {
        _currentEvent.IsMatch = true;
        _currentEvent.Count = 1;
        _currentEvent.LastConfidence = confidence;
        _currentEvent.Title = metadata.Title;
        _currentEvent.Artist = metadata.Artist;
        _currentEvent.Album = metadata.Album;
        _currentEvent.HasAlbumArt = !string.IsNullOrEmpty(metadata.CoverArtUrl);
        _currentEvent.Timestamp = DateTime.UtcNow;
        return;
      }

      // Different title or was a no-match row with data → new record
      _currentEvent = new FingerprintEventRecord
      {
        AudioSource = _currentSourceName ?? "Unknown",
        SourceType = _currentEvent.SourceType,
        IsMatch = true,
        Count = 1,
        LastConfidence = confidence,
        Title = metadata.Title,
        Artist = metadata.Artist,
        Album = metadata.Album,
        HasAlbumArt = !string.IsNullOrEmpty(metadata.CoverArtUrl),
        Timestamp = DateTime.UtcNow
      };
      _recentEvents.Add(_currentEvent);
      if (_recentEvents.Count > MaxRecentEvents)
      {
        _recentEvents.RemoveAt(0);
      }
    }
  }

  /// <summary>
  /// Updates the current event record with a no-match result.
  /// Current is not a match row → Count++; otherwise → new row.
  /// </summary>
  internal void UpdateCurrentEventNoMatch()
  {
    lock (_statusLock)
    {
      if (_currentEvent == null)
      {
        return;
      }

      _eventsVersion++;

      // Aggregate into existing no-match row (or fresh empty row from EnsureCurrentEvent)
      if (!_currentEvent.IsMatch)
      {
        _currentEvent.Count++;
        _currentEvent.Timestamp = DateTime.UtcNow;
        return;
      }

      // Was a match row → start new no-match record
      _currentEvent = new FingerprintEventRecord
      {
        AudioSource = _currentSourceName ?? "Unknown",
        SourceType = _currentEvent.SourceType,
        IsMatch = false,
        Count = 1,
        Timestamp = DateTime.UtcNow
      };
      _recentEvents.Add(_currentEvent);
      if (_recentEvents.Count > MaxRecentEvents)
      {
        _recentEvents.RemoveAt(0);
      }
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
    if (timestamps.Count == 0)
    {
      return 0;
    }
    var oldest = timestamps[0];
    var elapsed = (DateTime.UtcNow - oldest).TotalMinutes;
    return elapsed > 0 ? timestamps.Count / elapsed : 0;
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

  private void DetectSongChange(string trackKey, TrackMetadata newTrack, double confidence, string sourceTag)
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

    _metricsCollector?.Increment("fingerprint.song_changes", 1,
      new Dictionary<string, string> { ["source"] = sourceTag });

    SongChanged?.Invoke(this, new SongChangedEventArgs(previousTrack, newTrack, confidence));
  }

  /// <summary>
  /// Removes <paramref name="track"/> from duplicate suppression, so the next identification of it is
  /// raised rather than suppressed.
  /// </summary>
  /// <remarks>
  /// AUD-33: a result is marked as recently identified BEFORE <see cref="TrackIdentified"/> is raised.
  /// A source that then drops it as stale (sampled before its current track started) calls this, or the
  /// song it dropped would be suppressed for the whole window — even when that song is the one now
  /// playing (a capture straddling the skip, or Bluetooth AVRCP metadata arriving after the audio).
  /// </remarks>
  public void ForgetRecentIdentification(TrackMetadata track)
  {
    _recentIdentifications.TryRemove(TrackKey(track), out _);
  }

  /// <summary>Test seam (kind B — injection): writes the suppression entry a real identification cycle
  /// writes. A real cycle CAN be driven (<c>BackgroundIdentificationServiceCaptureTimeTests</c> does, with a
  /// mock tap and SongRec), but not paused between the mark and the raise, and each run pays the service's
  /// fixed 5 s start-up delay — so source tests that need "marked, then raised" state write it here.</summary>
  internal void MarkAsRecentlyIdentifiedForTesting(TrackMetadata track, double confidence) =>
    MarkAsRecentlyIdentified(TrackKey(track), confidence);

  /// <summary>Test seam (kind A — visibility): whether duplicate suppression would block <paramref name="track"/>.</summary>
  internal bool IsSuppressedAsDuplicateForTesting(TrackMetadata track) =>
    IsDuplicateIdentification(TrackKey(track));

  private static string TrackKey(TrackMetadata track) => $"{track.Title}|{track.Artist}";

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
