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
/// </summary>
public sealed class BackgroundIdentificationService : BackgroundService
{
  private readonly ILogger<BackgroundIdentificationService> _logger;
  private readonly IServiceProvider _serviceProvider;
  private readonly FingerprintingOptions _options;

  // Track recent identifications for duplicate suppression
  private readonly ConcurrentDictionary<string, DateTime> _recentIdentifications = new();

  /// <summary>
  /// Event raised when a track is identified.
  /// </summary>
  public event EventHandler<TrackIdentifiedEventArgs>? TrackIdentified;

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
      }

      try
      {
        await Task.Delay(
          TimeSpan.FromSeconds(_options.IdentificationIntervalSeconds),
          stoppingToken);
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
    // Resolve services from scope
    using var scope = _serviceProvider.CreateScope();
    var audioTap = scope.ServiceProvider.GetService<IAudioSampleProvider>();
    var fingerprintService = scope.ServiceProvider.GetService<IFingerprintService>();
    var lookupService = scope.ServiceProvider.GetService<IMetadataLookupService>();
    var historyRepo = scope.ServiceProvider.GetService<IPlayHistoryRepository>();

    if (audioTap == null || fingerprintService == null || lookupService == null || historyRepo == null)
    {
      _logger.LogWarning("Required services not available for fingerprinting");
      return;
    }

    // Check if source is active
    if (!audioTap.IsActive)
    {
      _logger.LogDebug("Audio source not active, skipping identification");
      return;
    }

    // Capture audio samples
    var sampleDuration = TimeSpan.FromSeconds(_options.SampleDurationSeconds);
    var samples = await audioTap.CaptureAsync(sampleDuration, ct);
    if (samples == null)
    {
      _logger.LogDebug("No audio samples captured");
      return;
    }

    // Generate fingerprint
    var fingerprint = await fingerprintService.GenerateFingerprintAsync(samples, ct);
    _logger.LogDebug("Generated fingerprint {Id} for {Duration}s of audio",
      fingerprint.Id, fingerprint.DurationSeconds);

    // Lookup metadata
    var result = await lookupService.LookupAsync(fingerprint, ct);

    // Check duplicate suppression
    if (result?.IsMatch == true && result.Metadata != null)
    {
      var trackKey = $"{result.Metadata.Title}|{result.Metadata.Artist}";
      if (IsDuplicateIdentification(trackKey))
      {
        _logger.LogDebug("Suppressing duplicate identification: {Title} by {Artist}",
          result.Metadata.Title, result.Metadata.Artist);
        return;
      }

      MarkAsRecentlyIdentified(trackKey);
    }

    // Record to play history
    var historyEntry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      TrackMetadataId = result?.Metadata?.Id,
      FingerprintId = fingerprint.Id,
      PlayedAt = DateTime.UtcNow,
      Source = audioTap.SourceType,
      SourceDetails = audioTap.SourceName,
      DurationSeconds = fingerprint.DurationSeconds,
      IdentificationConfidence = result?.Confidence,
      WasIdentified = result?.IsMatch ?? false
    };

    await historyRepo.RecordPlayAsync(historyEntry, ct);

    // Raise event for UI updates
    if (result?.IsMatch == true && result.Metadata != null)
    {
      _logger.LogInformation("Identified track: {Title} by {Artist} (confidence: {Confidence:P0})",
        result.Metadata.Title, result.Metadata.Artist, result.Confidence);

      TrackIdentified?.Invoke(this, new TrackIdentifiedEventArgs(result.Metadata, result.Confidence));
    }
    else
    {
      _logger.LogDebug("Track not identified (fingerprint stored for manual tagging)");
    }
  }

  private bool IsDuplicateIdentification(string trackKey)
  {
    if (_recentIdentifications.TryGetValue(trackKey, out var lastIdentified))
    {
      var elapsed = DateTime.UtcNow - lastIdentified;
      return elapsed.TotalMinutes < _options.DuplicateSuppressionMinutes;
    }

    return false;
  }

  private void MarkAsRecentlyIdentified(string trackKey)
  {
    _recentIdentifications[trackKey] = DateTime.UtcNow;
  }

  private void CleanupRecentIdentifications()
  {
    var cutoff = DateTime.UtcNow.AddMinutes(-_options.DuplicateSuppressionMinutes * 2);
    var keysToRemove = _recentIdentifications
      .Where(kvp => kvp.Value < cutoff)
      .Select(kvp => kvp.Key)
      .ToList();

    foreach (var key in keysToRemove)
    {
      _recentIdentifications.TryRemove(key, out _);
    }
  }
}
