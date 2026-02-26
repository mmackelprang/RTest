using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;

namespace Radio.IntegrationTests.TestSupport;

/// <summary>
/// Mock metadata lookup service for integration tests.
/// Returns configurable track metadata without requiring external API calls.
/// </summary>
public class MockMetadataLookupService : IMetadataLookupService
{
  private MetadataLookupResult? _nextResult;
  private TrackMetadata? _defaultMetadata;
  private readonly Dictionary<string, MetadataLookupResult> _resultsByFingerprintId = new();

  /// <summary>
  /// Gets or sets whether the mock should return matches. Default is true.
  /// </summary>
  public bool ShouldMatch { get; set; } = true;

  /// <summary>
  /// Gets or sets the default confidence level. Default is 0.95.
  /// </summary>
  public double DefaultConfidence { get; set; } = 0.95;

  /// <summary>
  /// Gets a list of all lookups performed.
  /// </summary>
  public List<FingerprintData> LookupHistory { get; } = new();

  /// <summary>
  /// Sets the next result to return from lookup.
  /// </summary>
  public void SetNextResult(MetadataLookupResult result)
  {
    _nextResult = result;
  }

  /// <summary>
  /// Sets the default metadata to return for matches.
  /// </summary>
  public void SetDefaultMetadata(TrackMetadata metadata)
  {
    _defaultMetadata = metadata;
  }

  /// <summary>
  /// Configures a specific result for a fingerprint ID.
  /// </summary>
  public void ConfigureResult(string fingerprintId, MetadataLookupResult result)
  {
    _resultsByFingerprintId[fingerprintId] = result;
  }

  /// <summary>
  /// Creates default test metadata.
  /// </summary>
  public static TrackMetadata CreateTestMetadata(
    string title = "Test Track",
    string artist = "Test Artist",
    string? album = "Test Album")
  {
    return new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = title,
      Artist = artist,
      Album = album,
      Source = MetadataSource.AcoustID,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
  }

  public Task<MetadataLookupResult?> LookupAsync(FingerprintData fingerprint, CancellationToken ct = default)
  {
    LookupHistory.Add(fingerprint);

    // Check for specific result configured for this fingerprint ID
    if (_resultsByFingerprintId.TryGetValue(fingerprint.Id, out var specificResult))
    {
      return Task.FromResult<MetadataLookupResult?>(specificResult);
    }

    // Return pre-configured next result if set
    if (_nextResult != null)
    {
      var result = _nextResult;
      _nextResult = null;
      return Task.FromResult<MetadataLookupResult?>(result);
    }

    // Return default match or no-match based on ShouldMatch setting
    if (!ShouldMatch)
    {
      return Task.FromResult<MetadataLookupResult?>(new MetadataLookupResult
      {
        IsMatch = false,
        Confidence = 0,
        FingerprintId = fingerprint.Id,
        Source = LookupSource.AcoustID
      });
    }

    var metadata = _defaultMetadata ?? CreateTestMetadata();

    return Task.FromResult<MetadataLookupResult?>(new MetadataLookupResult
    {
      IsMatch = true,
      Confidence = DefaultConfidence,
      FingerprintId = fingerprint.Id,
      Metadata = metadata,
      AcoustId = "test-acoustid-123",
      MusicBrainzRecordingId = "test-mbid-456",
      Source = LookupSource.AcoustID
    });
  }

  public Task<TrackMetadata?> GetMusicBrainzMetadataAsync(string recordingId, CancellationToken ct = default)
  {
    // Return default or configured metadata
    return Task.FromResult<TrackMetadata?>(_defaultMetadata ?? CreateTestMetadata());
  }

  public Task<string?> SearchCoverArtByTextAsync(
    string title, string artist, string? album = null, CancellationToken ct = default)
  {
    return Task.FromResult<string?>(null);
  }

  public Task<string?> GetCoverArtByReleaseIdAsync(string releaseId, CancellationToken ct = default)
  {
    return Task.FromResult<string?>(null);
  }
}
