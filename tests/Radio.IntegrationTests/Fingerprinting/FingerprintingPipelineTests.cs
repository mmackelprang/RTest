using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;
using Radio.Infrastructure.Audio.Fingerprinting.Data;
using Radio.IntegrationTests.TestSupport;
using Xunit.Abstractions;

namespace Radio.IntegrationTests.Fingerprinting;

/// <summary>
/// Integration tests that validate the fingerprinting pipeline end-to-end.
/// Uses fpcalc (native Chromaprint) to generate fingerprints from MP3 files,
/// then validates identification via live AcoustID and MusicBrainz APIs.
/// </summary>
[Trait("Category", "Integration")]
public class FingerprintingPipelineTests : IAsyncLifetime
{
  private readonly ITestOutputHelper _output;

  // Test MP3 file paths (relative to solution root)
  private static readonly string SolutionRoot = FindSolutionRoot();
  private static readonly string WereReadyMp3 = Path.Combine(SolutionRoot,
    "src", "Radio.API", "media", "audio", "music", "02 We're Ready.mp3");
  private static readonly string BabyItsColdOutsideMp3 = Path.Combine(SolutionRoot,
    "src", "Radio.API", "media", "audio", "music", "05-Baby, It's Cold Outside.mp3");

  private readonly string _tempDirectory;
  private FingerprintDbContext _dbContext = null!;
  private SqliteFingerprintCacheRepository _cacheRepository = null!;
  private SqliteTrackMetadataRepository _metadataRepository = null!;
  private ChromaprintFingerprintService _fingerprintService = null!;
  private AcoustIdClient _acoustIdClient = null!;
  private MetadataLookupService _lookupService = null!;

  public FingerprintingPipelineTests(ITestOutputHelper output)
  {
    _output = output;
    _tempDirectory = Path.Combine(Path.GetTempPath(), $"FingerprintPipelineTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDirectory);
  }

  public async Task InitializeAsync()
  {
    // Set up SQLite database for fingerprint cache
    var databaseOptions = Options.Create(new DatabaseOptions
    {
      RootPath = _tempDirectory,
      FingerprintingSubdirectory = "",
      FingerprintingFileName = "fingerprints.db"
    });
    var pathResolver = new DatabasePathResolver(databaseOptions);
    _dbContext = new FingerprintDbContext(
      NullLogger<FingerprintDbContext>.Instance, pathResolver);
    await _dbContext.InitializeAsync();

    _cacheRepository = new SqliteFingerprintCacheRepository(
      NullLogger<SqliteFingerprintCacheRepository>.Instance, _dbContext);
    _metadataRepository = new SqliteTrackMetadataRepository(
      NullLogger<SqliteTrackMetadataRepository>.Instance, _dbContext);

    // Configure with real AcoustID API key and MusicBrainz settings
    var fpcalcPath = Path.Combine(SolutionRoot, "tools", "fpcalc",
      OperatingSystem.IsWindows() ? "fpcalc.exe" : "fpcalc");
    Assert.True(File.Exists(fpcalcPath), $"fpcalc not found at: {fpcalcPath}");
    _output.WriteLine($"Using fpcalc at: {fpcalcPath}");

    var options = new FingerprintingOptions
    {
      Enabled = true,
      SampleDurationSeconds = 15,
      MinimumConfidenceThreshold = 0.5,
      FpcalcPath = fpcalcPath,
      AcoustId = new AcoustIdOptions
      {
        ApiKey = "Z0mwq00cEz",
        BaseUrl = "https://api.acoustid.org/v2",
        TimeoutSeconds = 15
      },
      MusicBrainz = new MusicBrainzOptions
      {
        BaseUrl = "https://musicbrainz.org/ws/2",
        ApplicationName = "RadioConsole",
        ApplicationVersion = "1.0.0",
        ContactEmail = "mark@mackelprang.com",
        TimeoutSeconds = 15
      }
    };
    var optionsWrapper = Options.Create(options);

    // Create real services
    _fingerprintService = new ChromaprintFingerprintService(
      NullLogger<ChromaprintFingerprintService>.Instance,
      optionsWrapper);

    var acoustIdHttpClient = new HttpClient
    {
      Timeout = TimeSpan.FromSeconds(15)
    };
    acoustIdHttpClient.DefaultRequestHeaders.Add("User-Agent", "RadioConsole/1.0");
    _acoustIdClient = new AcoustIdClient(
      acoustIdHttpClient,
      NullLogger<AcoustIdClient>.Instance,
      optionsWrapper);

    var musicBrainzHttpClient = new HttpClient
    {
      Timeout = TimeSpan.FromSeconds(15)
    };
    _lookupService = new MetadataLookupService(
      NullLogger<MetadataLookupService>.Instance,
      _cacheRepository,
      _metadataRepository,
      optionsWrapper,
      musicBrainzHttpClient,
      _acoustIdClient);
  }

  public async Task DisposeAsync()
  {
    _acoustIdClient?.Dispose();
    await _dbContext.DisposeAsync();
    SqliteConnection.ClearAllPools();

    try
    {
      if (Directory.Exists(_tempDirectory))
        Directory.Delete(_tempDirectory, recursive: true);
    }
    catch (IOException) { }
  }

  [Fact]
  public async Task GenerateFingerprint_WereReady_ProducesValidFingerprint()
  {
    Assert.True(File.Exists(WereReadyMp3), $"MP3 file not found: {WereReadyMp3}");

    // Generate fingerprint using fpcalc (same service as production pipeline)
    var fingerprint = await _fingerprintService.GenerateFingerprintFromFileAsync(WereReadyMp3);

    _output.WriteLine($"Fingerprint: hash length={fingerprint.ChromaprintHash?.Length ?? 0}, " +
      $"duration={fingerprint.DurationSeconds}s");

    Assert.NotNull(fingerprint);
    Assert.False(string.IsNullOrEmpty(fingerprint.ChromaprintHash),
      "Chromaprint hash should not be empty");
    Assert.True(fingerprint.DurationSeconds > 10,
      $"Duration should be > 10s, got {fingerprint.DurationSeconds}s");
  }

  [Fact]
  public async Task LookupMetadata_WereReady_IdentifiesCorrectly()
  {
    Assert.True(File.Exists(WereReadyMp3), $"MP3 file not found: {WereReadyMp3}");

    // Step 1: Generate fingerprint from MP3 file (fpcalc handles decoding)
    var fingerprint = await _fingerprintService.GenerateFingerprintFromFileAsync(WereReadyMp3);
    _output.WriteLine($"Fingerprint: hash length={fingerprint.ChromaprintHash?.Length ?? 0}, " +
      $"duration={fingerprint.DurationSeconds}s");
    Assert.False(string.IsNullOrEmpty(fingerprint.ChromaprintHash),
      "Fingerprint hash should not be empty");

    // Step 2: Full pipeline lookup (AcoustID -> MusicBrainz -> Cover Art)
    var result = await _lookupService.LookupAsync(fingerprint);

    // Validate identification
    Assert.NotNull(result);
    _output.WriteLine($"Lookup result: IsMatch={result.IsMatch}, Confidence={result.Confidence:P0}, " +
      $"Source={result.Source}");
    if (result.Metadata != null)
      _output.WriteLine($"Metadata: Title=\"{result.Metadata.Title}\", " +
        $"Artist=\"{result.Metadata.Artist}\", Album=\"{result.Metadata.Album}\"");
    if (!string.IsNullOrEmpty(result.MusicBrainzRecordingId))
      _output.WriteLine($"MusicBrainz Recording ID: {result.MusicBrainzRecordingId}");

    Assert.True(result.IsMatch, "Should identify the track");
    Assert.True(result.Confidence >= 0.5, $"Confidence {result.Confidence} should be >= 0.5");

    // Validate metadata
    Assert.NotNull(result.Metadata);
    Assert.Contains("Ready", result.Metadata.Title, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("Boston", result.Metadata.Artist, StringComparison.OrdinalIgnoreCase);

    // MusicBrainz enrichment should provide album info
    Assert.False(string.IsNullOrEmpty(result.Metadata.Album),
      "Album should be populated from MusicBrainz");
    Assert.Contains("Third Stage", result.Metadata.Album, StringComparison.OrdinalIgnoreCase);

    // Cover art - log result but don't fail test since Cover Art Archive/archive.org
    // may return 401 or be temporarily unavailable
    if (!string.IsNullOrEmpty(result.Metadata.CoverArtUrl))
      _output.WriteLine($"Cover art URL: {result.Metadata.CoverArtUrl}");
    else
      _output.WriteLine("Cover art URL not available (Cover Art Archive may be unavailable)");
  }

  [Fact]
  public async Task LookupMetadata_BabyItsColdOutside_IdentifiesCorrectly()
  {
    Assert.True(File.Exists(BabyItsColdOutsideMp3), $"MP3 file not found: {BabyItsColdOutsideMp3}");

    // Step 1: Generate fingerprint from MP3 file (fpcalc handles decoding)
    var fingerprint = await _fingerprintService.GenerateFingerprintFromFileAsync(BabyItsColdOutsideMp3);
    _output.WriteLine($"Fingerprint: hash length={fingerprint.ChromaprintHash?.Length ?? 0}, " +
      $"duration={fingerprint.DurationSeconds}s");
    if (!string.IsNullOrEmpty(fingerprint.ChromaprintHash))
      _output.WriteLine($"Hash preview: {fingerprint.ChromaprintHash[..Math.Min(80, fingerprint.ChromaprintHash.Length)]}...");
    Assert.False(string.IsNullOrEmpty(fingerprint.ChromaprintHash),
      "Fingerprint hash should not be empty");

    // Step 2: Full pipeline lookup
    var result = await _lookupService.LookupAsync(fingerprint);

    // Validate identification
    Assert.NotNull(result);
    _output.WriteLine($"Lookup result: IsMatch={result.IsMatch}, Confidence={result.Confidence}, " +
      $"Source={result.Source}");
    if (result.Metadata != null)
      _output.WriteLine($"Metadata: Title=\"{result.Metadata.Title}\", " +
        $"Artist=\"{result.Metadata.Artist}\", Album=\"{result.Metadata.Album}\"");

    Assert.True(result.IsMatch, "Should identify the track");
    Assert.True(result.Confidence >= 0.5, $"Confidence {result.Confidence} should be >= 0.5");

    // Validate metadata
    Assert.NotNull(result.Metadata);
    Assert.Contains("Dean Martin", result.Metadata.Artist, StringComparison.OrdinalIgnoreCase);
  }

  private static string FindSolutionRoot()
  {
    var dir = AppDomain.CurrentDomain.BaseDirectory;
    while (dir != null)
    {
      if (File.Exists(Path.Combine(dir, "RadioConsole.sln")))
        return dir;
      dir = Path.GetDirectoryName(dir);
    }
    throw new InvalidOperationException("Could not find solution root (RadioConsole.sln)");
  }
}
