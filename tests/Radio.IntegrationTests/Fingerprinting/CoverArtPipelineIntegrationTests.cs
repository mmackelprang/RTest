using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.IntegrationTests.TestSupport;
using Xunit.Abstractions;

namespace Radio.IntegrationTests.Fingerprinting;

/// <summary>
/// Integration tests for the Cover Art Archive pipeline.
/// Verifies that MusicBrainz enrichment + Cover Art Archive returns valid album art URLs.
/// These tests require network access and use real MusicBrainz/Cover Art Archive APIs.
/// </summary>
[Trait("Category", "Integration")]
public class CoverArtPipelineIntegrationTests : IDisposable
{
  private readonly ITestOutputHelper _output;
  private readonly HttpClient _musicBrainzClient;
  private readonly HttpClient _coverArtClient;

  // Known MusicBrainz recording: "Bixby Canyon Bridge" by Death Cab for Cutie
  // From user logs: MusicBrainzRecordingId=6e4a0a2d-87ff-4b3b-91cc-be94b90ad5fb
  private const string KnownRecordingId = "6e4a0a2d-87ff-4b3b-91cc-be94b90ad5fb";
  private const string KnownTitle = "Bixby Canyon Bridge";
  private const string KnownArtist = "Death Cab for Cutie";
  private const string KnownAlbum = "Narrow Stairs";

  // Known MusicBrainz recording: "I'm Not in Love" by 10cc
  private const string KnownRecordingId2 = "cc0152c2-3487-4789-a78a-4b7c36048ece";

  public CoverArtPipelineIntegrationTests(ITestOutputHelper output)
  {
    _output = output;

    _musicBrainzClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    _musicBrainzClient.DefaultRequestHeaders.UserAgent.ParseAdd(
      "RadioConsole/1.0 (mark@mackelprang.com)");

    _coverArtClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
    {
      Timeout = TimeSpan.FromSeconds(15)
    };
    _coverArtClient.DefaultRequestHeaders.UserAgent.ParseAdd("RadioConsole/1.0");
  }

  public void Dispose()
  {
    _musicBrainzClient.Dispose();
    _coverArtClient.Dispose();
  }

  /// <summary>
  /// Verifies that MusicBrainz returns release info for a known recording,
  /// and that Cover Art Archive returns a valid front cover URL.
  /// This is the exact same path MetadataLookupService.GetMusicBrainzMetadataAsync() takes.
  /// </summary>
  [SkippableFact]
  public async Task CoverArtArchive_ReturnsValidUrl_ForKnownRecording()
  {
    NetworkAvailabilityHelper.RequireExternalNetwork();
    // Step 1: Query MusicBrainz for recording → extract release ID
    _output.WriteLine($"=== Step 1: MusicBrainz lookup for '{KnownTitle}' ===");
    var url = $"https://musicbrainz.org/ws/2/recording/{KnownRecordingId}?inc=artists+releases+release-groups&fmt=json";

    using var mbResponse = await _musicBrainzClient.GetAsync(url);
    Assert.True(mbResponse.IsSuccessStatusCode,
      $"MusicBrainz returned {mbResponse.StatusCode} for recording {KnownRecordingId}");

    var mbJson = await mbResponse.Content.ReadAsStringAsync();
    var recording = JsonSerializer.Deserialize<MbRecording>(mbJson);
    Assert.NotNull(recording);
    _output.WriteLine($"  Title: {recording.Title}");
    _output.WriteLine($"  Releases: {recording.Releases?.Count ?? 0}");

    // Find best non-compilation album release (same logic as MetadataLookupService)
    var bestRelease = recording.Releases?
      .OrderByDescending(r =>
      {
        var rg = r.ReleaseGroup;
        if (rg == null) return 0;
        bool isAlbum = string.Equals(rg.PrimaryType, "Album", StringComparison.OrdinalIgnoreCase);
        bool isCompilation = rg.SecondaryTypes?.Any(st =>
          string.Equals(st, "Compilation", StringComparison.OrdinalIgnoreCase)) == true;
        return isAlbum && !isCompilation ? 2 : isAlbum ? 1 : 0;
      })
      .FirstOrDefault();

    Assert.NotNull(bestRelease);
    Assert.False(string.IsNullOrEmpty(bestRelease.Id), "Release ID should not be empty");
    _output.WriteLine($"  Best release: '{bestRelease.Title}' (ID: {bestRelease.Id})");
    _output.WriteLine($"  Release group: '{bestRelease.ReleaseGroup?.Title}' (type: {bestRelease.ReleaseGroup?.PrimaryType})");

    // Step 2: Query Cover Art Archive for the release
    _output.WriteLine($"\n=== Step 2: Cover Art Archive lookup for release {bestRelease.Id} ===");
    var coverArtUrl = $"https://coverartarchive.org/release/{bestRelease.Id}";

    using var caResponse = await _coverArtClient.GetAsync(coverArtUrl);
    _output.WriteLine($"  Response: {caResponse.StatusCode}");

    if (!caResponse.IsSuccessStatusCode)
    {
      _output.WriteLine($"  *** Cover Art Archive returned {caResponse.StatusCode} — no cover art available ***");
      _output.WriteLine($"  This means the release {bestRelease.Id} has no cover art uploaded.");
      Assert.Fail($"Cover Art Archive returned {caResponse.StatusCode} for release {bestRelease.Id}");
    }

    var caJson = await caResponse.Content.ReadAsStringAsync();
    var coverArt = JsonSerializer.Deserialize<CaResponse>(caJson);
    Assert.NotNull(coverArt?.Images);
    Assert.NotEmpty(coverArt.Images);
    _output.WriteLine($"  Images: {coverArt.Images.Count}");

    // Find front cover
    var frontImage = coverArt.Images.FirstOrDefault(i => i.Front == true);
    if (frontImage == null)
    {
      _output.WriteLine("  No front cover found, using first image");
      frontImage = coverArt.Images.First();
    }

    _output.WriteLine($"  Front: {frontImage.Front}");
    _output.WriteLine($"  Image URL: {frontImage.Image}");
    _output.WriteLine($"  Thumbnail (small): {frontImage.Thumbnails?.Small}");
    _output.WriteLine($"  Thumbnail (large): {frontImage.Thumbnails?.Large}");
    _output.WriteLine($"  Thumbnail (250): {frontImage.Thumbnails?.Size250}");
    _output.WriteLine($"  Thumbnail (500): {frontImage.Thumbnails?.Size500}");

    // Get the URL that MetadataLookupService would use
    var selectedUrl = frontImage.Thumbnails?.Small
      ?? frontImage.Thumbnails?.Large
      ?? frontImage.Image;

    Assert.False(string.IsNullOrEmpty(selectedUrl), "Cover art URL should not be empty");
    _output.WriteLine($"\n  Selected URL (what MetadataLookupService returns): {selectedUrl}");

    // Step 3: Verify the URL is accessible
    _output.WriteLine($"\n=== Step 3: Verify URL is accessible ===");
    using var imgResponse = await _coverArtClient.GetAsync(selectedUrl);
    _output.WriteLine($"  Response: {imgResponse.StatusCode}");
    _output.WriteLine($"  Content-Type: {imgResponse.Content.Headers.ContentType}");
    _output.WriteLine($"  Content-Length: {imgResponse.Content.Headers.ContentLength}");

    Assert.True(imgResponse.IsSuccessStatusCode,
      $"Cover art URL returned {imgResponse.StatusCode}: {selectedUrl}");

    _output.WriteLine("\n=== PASS: Cover Art Archive pipeline works end-to-end ===");
  }

  /// <summary>
  /// Tests the full MetadataLookupService pipeline with a real MusicBrainz recording ID.
  /// Uses the actual service code path to verify CoverArtUrl is populated.
  /// </summary>
  [SkippableFact]
  public async Task MetadataLookupService_GetMusicBrainzMetadata_PopulatesCoverArtUrl()
  {
    NetworkAvailabilityHelper.RequireExternalNetwork();
    // Create MetadataLookupService with real HTTP client
    var options = Options.Create(new FingerprintingOptions
    {
      Enabled = true,
      MusicBrainz = new MusicBrainzOptions
      {
        BaseUrl = "https://musicbrainz.org/ws/2",
        ApplicationName = "RadioConsole",
        ApplicationVersion = "1.0.0",
        ContactEmail = "mark@mackelprang.com",
        TimeoutSeconds = 15
      }
    });

    // We need to mock the cache and metadata repos since they require SQLite
    var mockCache = new InMemoryFingerprintCache();
    var mockMetadataRepo = new InMemoryTrackMetadataRepo();

    var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
      "RadioConsole/1.0.0 (mark@mackelprang.com)");
    var service = new MetadataLookupService(
      NullLogger<MetadataLookupService>.Instance,
      mockCache,
      mockMetadataRepo,
      options,
      httpClient);

    // Call GetMusicBrainzMetadataAsync directly
    _output.WriteLine($"Querying MusicBrainz for recording {KnownRecordingId} ('{KnownTitle}')");
    var metadata = await service.GetMusicBrainzMetadataAsync(KnownRecordingId);

    Assert.NotNull(metadata);
    _output.WriteLine($"  Title: {metadata.Title}");
    _output.WriteLine($"  Artist: {metadata.Artist}");
    _output.WriteLine($"  Album: {metadata.Album}");
    _output.WriteLine($"  ReleaseYear: {metadata.ReleaseYear}");
    _output.WriteLine($"  MusicBrainzReleaseId: {metadata.MusicBrainzReleaseId}");
    _output.WriteLine($"  CoverArtUrl: {metadata.CoverArtUrl ?? "(null)"}");

    Assert.False(string.IsNullOrEmpty(metadata.CoverArtUrl),
      $"CoverArtUrl should be populated for '{KnownTitle}' by '{KnownArtist}'. " +
      $"MusicBrainzReleaseId={metadata.MusicBrainzReleaseId}");

    Assert.StartsWith("http", metadata.CoverArtUrl);
    _output.WriteLine($"\n=== PASS: MetadataLookupService returns CoverArtUrl for '{KnownTitle}' ===");

    httpClient.Dispose();
  }

  #region DTOs for direct API testing

  private sealed class MbRecording
  {
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("releases")] public List<MbRelease>? Releases { get; set; }
  }

  private sealed class MbRelease
  {
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("date")] public string? Date { get; set; }
    [JsonPropertyName("release-group")] public MbReleaseGroup? ReleaseGroup { get; set; }
  }

  private sealed class MbReleaseGroup
  {
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("primary-type")] public string? PrimaryType { get; set; }
    [JsonPropertyName("secondary-types")] public List<string>? SecondaryTypes { get; set; }
  }

  private sealed class CaResponse
  {
    [JsonPropertyName("images")] public List<CaImage>? Images { get; set; }
  }

  private sealed class CaImage
  {
    [JsonPropertyName("front")] public bool? Front { get; set; }
    [JsonPropertyName("image")] public string? Image { get; set; }
    [JsonPropertyName("thumbnails")] public CaThumbnails? Thumbnails { get; set; }
  }

  private sealed class CaThumbnails
  {
    [JsonPropertyName("small")] public string? Small { get; set; }
    [JsonPropertyName("large")] public string? Large { get; set; }
    [JsonPropertyName("250")] public string? Size250 { get; set; }
    [JsonPropertyName("500")] public string? Size500 { get; set; }
  }

  #endregion

  #region Minimal in-memory mocks for MetadataLookupService

  private sealed class InMemoryFingerprintCache : Radio.Core.Interfaces.Audio.IFingerprintCacheRepository
  {
    public Task<CachedFingerprint?> FindByHashAsync(string chromaprintHash, CancellationToken ct = default)
      => Task.FromResult<CachedFingerprint?>(null);

    public Task<CachedFingerprint> StoreAsync(FingerprintData fingerprint, TrackMetadata? metadata, CancellationToken ct = default)
      => Task.FromResult(new CachedFingerprint
      {
        Id = fingerprint.Id,
        ChromaprintHash = fingerprint.ChromaprintHash,
        DurationSeconds = fingerprint.DurationSeconds,
        CreatedAt = DateTime.UtcNow,
        Metadata = metadata
      });

    public Task UpdateLastMatchedAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    public Task<int> GetCacheCountAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<IReadOnlyList<CachedFingerprint>> GetAllAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<CachedFingerprint>>(Array.Empty<CachedFingerprint>());
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(false);
  }

  private sealed class InMemoryTrackMetadataRepo : Radio.Core.Interfaces.Audio.ITrackMetadataRepository
  {
    public Task<TrackMetadata?> GetByIdAsync(string id, CancellationToken ct = default)
      => Task.FromResult<TrackMetadata?>(null);
    public Task<TrackMetadata?> GetByFingerprintIdAsync(string fingerprintId, CancellationToken ct = default)
      => Task.FromResult<TrackMetadata?>(null);
    public Task<TrackMetadata?> FindByMusicBrainzIdAsync(string recordingId, CancellationToken ct = default)
      => Task.FromResult<TrackMetadata?>(null);
    public Task<TrackMetadata> StoreAsync(TrackMetadata metadata, CancellationToken ct = default)
      => Task.FromResult(metadata);
    public Task<IReadOnlyList<TrackMetadata>> SearchAsync(string query, int limit = 20, CancellationToken ct = default)
      => Task.FromResult<IReadOnlyList<TrackMetadata>>(Array.Empty<TrackMetadata>());
    public Task UpdateCoverArtUrlAsync(string id, string coverArtUrl, CancellationToken ct = default)
      => Task.CompletedTask;
  }

  #endregion
}
