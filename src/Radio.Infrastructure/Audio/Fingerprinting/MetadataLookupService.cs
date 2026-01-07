using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Audio.Fingerprinting;

/// <summary>
/// Service for looking up metadata from fingerprints.
/// Checks local cache first, then queries AcoustID and MusicBrainz.
/// </summary>
public sealed class MetadataLookupService : IMetadataLookupService
{
  private readonly ILogger<MetadataLookupService> _logger;
  private readonly IFingerprintCacheRepository _cache;
  private readonly ITrackMetadataRepository _metadataRepo;
  private readonly FingerprintingOptions _options;
  private readonly AcoustIdClient? _acoustIdClient;

  /// <summary>
  /// Initializes a new instance of the <see cref="MetadataLookupService"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="cache">The fingerprint cache repository.</param>
  /// <param name="metadataRepo">The track metadata repository.</param>
  /// <param name="options">The fingerprinting options.</param>
  /// <param name="acoustIdClient">Optional AcoustID client for API lookups.</param>
  public MetadataLookupService(
    ILogger<MetadataLookupService> logger,
    IFingerprintCacheRepository cache,
    ITrackMetadataRepository metadataRepo,
    IOptions<FingerprintingOptions> options,
    AcoustIdClient? acoustIdClient = null)
  {
    _logger = logger;
    _cache = cache;
    _metadataRepo = metadataRepo;
    _options = options.Value;
    _acoustIdClient = acoustIdClient;
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
        FingerprintId = cached.Id,
        Metadata = cached.Metadata,
        Source = LookupSource.Cache
      };
    }

    // Step 2: Check if AcoustID API key is configured
    if (string.IsNullOrEmpty(_options.AcoustId.ApiKey))
    {
      _logger.LogDebug("No AcoustID API key configured, storing fingerprint for manual tagging");
      var stored = await _cache.StoreAsync(fingerprint, null, ct);
      return new MetadataLookupResult
      {
        IsMatch = false,
        Confidence = 0.0,
        FingerprintId = stored.Id,
        Source = LookupSource.Manual
      };
    }

    // Step 3: Query AcoustID API
    if (_acoustIdClient == null)
    {
      _logger.LogWarning("AcoustID client not available, storing fingerprint for manual tagging");
      var stored = await _cache.StoreAsync(fingerprint, null, ct);
      return new MetadataLookupResult
      {
        IsMatch = false,
        Confidence = 0.0,
        FingerprintId = stored.Id,
        Source = LookupSource.Manual
      };
    }

    _logger.LogInformation("Querying AcoustID for fingerprint {FingerprintId} (hash: {Hash})", 
      fingerprint.Id, fingerprint.ChromaprintHash?.Substring(0, Math.Min(50, fingerprint.ChromaprintHash?.Length ?? 0)));
    
    var acoustIdResult = await _acoustIdClient.LookupAsync(
      fingerprint.ChromaprintHash,
      fingerprint.DurationSeconds,
      ct);

    if (acoustIdResult == null || acoustIdResult.Recordings.Count == 0)
    {
      _logger.LogInformation("No AcoustID match found for fingerprint {FingerprintId}, storing for manual tagging", fingerprint.Id);
      var stored = await _cache.StoreAsync(fingerprint, null, ct);
      return new MetadataLookupResult
      {
        IsMatch = false,
        Confidence = 0.0,
        FingerprintId = stored.Id,
        Source = LookupSource.Manual
      };
    }

    // Get the best recording match
    var bestRecording = acoustIdResult.Recordings.FirstOrDefault();
    if (bestRecording == null)
    {
      _logger.LogInformation("No recordings in AcoustID result for fingerprint {FingerprintId}", fingerprint.Id);
      var stored = await _cache.StoreAsync(fingerprint, null, ct);
      return new MetadataLookupResult
      {
        IsMatch = false,
        Confidence = 0.0,
        FingerprintId = stored.Id,
        Source = LookupSource.Manual
      };
    }

    _logger.LogInformation(
      "Processing AcoustID recording: MusicBrainzRecordingId={RecordingId}, Title={Title}, Artists={Artists}, ReleaseGroups={ReleaseGroups}",
      bestRecording.Id, 
      bestRecording.Title, 
      string.Join(", ", bestRecording.Artists),
      bestRecording.ReleaseGroups.Count);

    // Create track metadata from AcoustID result
    var trackMetadata = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = bestRecording.Title ?? "Unknown Title",
      Artist = bestRecording.Artists.FirstOrDefault() ?? "Unknown Artist",
      Album = bestRecording.ReleaseGroups.FirstOrDefault()?.Title,
      MusicBrainzRecordingId = bestRecording.Id,
      Source = MetadataSource.AcoustID,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };

    _logger.LogInformation(
      "Created track metadata from AcoustID: Title=\"{Title}\", Artist=\"{Artist}\", Album=\"{Album}\", MusicBrainzRecordingId={MusicBrainzId}, Confidence={Confidence:P0}",
      trackMetadata.Title, trackMetadata.Artist, trackMetadata.Album ?? "(none)", 
      trackMetadata.MusicBrainzRecordingId, acoustIdResult.Score);

    // Store the fingerprint with metadata
    var storedWithMeta = await _cache.StoreAsync(fingerprint, trackMetadata, ct);

    // Also store the track metadata in the repository
    await _metadataRepo.StoreAsync(trackMetadata, ct);

    return new MetadataLookupResult
    {
      IsMatch = true,
      Confidence = acoustIdResult.Score,
      FingerprintId = storedWithMeta.Id,
      Metadata = trackMetadata,
      Source = LookupSource.AcoustID,
      AcoustId = acoustIdResult.Id,
      MusicBrainzRecordingId = bestRecording.Id
    };
  }

  /// <inheritdoc/>
  public async Task<TrackMetadata?> GetMusicBrainzMetadataAsync(
    string recordingId,
    CancellationToken ct = default)
  {
    ArgumentException.ThrowIfNullOrEmpty(recordingId);

    // First check if we already have this recording in our cache
    var existingMetadata = await _metadataRepo.FindByMusicBrainzIdAsync(recordingId, ct);
    if (existingMetadata != null)
    {
      _logger.LogDebug("Found cached MusicBrainz metadata for recording {RecordingId}", recordingId);
      return existingMetadata;
    }

    // MusicBrainz API lookup would go here
    // For now, return null as the AcoustID response already includes basic metadata
    _logger.LogDebug(
      "MusicBrainz API lookup not implemented for recording {RecordingId}. " +
      "Basic metadata from AcoustID is used instead.",
      recordingId);

    return null;
  }
}
