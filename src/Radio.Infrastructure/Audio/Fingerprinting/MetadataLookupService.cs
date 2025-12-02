using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting;

/// <summary>
/// Service for looking up metadata from fingerprints.
/// Checks local cache first, then can query external services.
/// </summary>
public sealed class MetadataLookupService : IMetadataLookupService
{
  private readonly ILogger<MetadataLookupService> _logger;
  private readonly IFingerprintCacheRepository _cache;
  private readonly ITrackMetadataRepository _metadataRepo;
  private readonly FingerprintingOptions _options;

  /// <summary>
  /// Initializes a new instance of the <see cref="MetadataLookupService"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="cache">The fingerprint cache repository.</param>
  /// <param name="metadataRepo">The track metadata repository.</param>
  /// <param name="options">The fingerprinting options.</param>
  public MetadataLookupService(
    ILogger<MetadataLookupService> logger,
    IFingerprintCacheRepository cache,
    ITrackMetadataRepository metadataRepo,
    IOptions<FingerprintingOptions> options)
  {
    _logger = logger;
    _cache = cache;
    _metadataRepo = metadataRepo;
    _options = options.Value;
  }

  /// <inheritdoc/>
  public async Task<MetadataLookupResult?> LookupAsync(
    FingerprintData fingerprint,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(fingerprint);

    _logger.LogDebug("Looking up metadata for fingerprint {Id}", fingerprint.Id);

    // Step 1: Check local SQLite cache
    var cached = await _cache.FindByHashAsync(fingerprint.ChromaprintHash, ct);
    if (cached?.Metadata != null)
    {
      await _cache.UpdateLastMatchedAsync(cached.Id, ct);
      _logger.LogDebug("Found cached metadata for fingerprint: {Title} by {Artist}",
        cached.Metadata.Title, cached.Metadata.Artist);

      return new MetadataLookupResult
      {
        IsMatch = true,
        Confidence = 1.0,
        Metadata = cached.Metadata,
        Source = LookupSource.Cache
      };
    }

    // Step 2: If API key is configured, query AcoustID
    // Note: In a real implementation, this would call the AcoustID API
    // For now, we just cache the fingerprint for manual tagging later
    if (string.IsNullOrEmpty(_options.AcoustId.ApiKey))
    {
      _logger.LogDebug("No AcoustID API key configured, storing fingerprint for manual tagging");
      await _cache.StoreAsync(fingerprint, null, ct);
      return null;
    }

    // Placeholder for AcoustID lookup
    // In production, would call AcoustIdClient.LookupAsync()
    _logger.LogDebug("AcoustID lookup not implemented, storing fingerprint for manual tagging");
    await _cache.StoreAsync(fingerprint, null, ct);

    return null;
  }

  /// <inheritdoc/>
  public async Task<TrackMetadata?> GetMusicBrainzMetadataAsync(
    string recordingId,
    CancellationToken ct = default)
  {
    ArgumentException.ThrowIfNullOrEmpty(recordingId);

    // Placeholder for MusicBrainz lookup
    // In production, would call MusicBrainzClient.GetRecordingAsync()
    _logger.LogDebug("MusicBrainz lookup not implemented for recording {RecordingId}", recordingId);

    return await Task.FromResult<TrackMetadata?>(null);
  }
}
