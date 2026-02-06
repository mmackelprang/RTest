using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting.Data;
using Radio.IntegrationTests.TestSupport;

namespace Radio.IntegrationTests.Fingerprinting;

/// <summary>
/// End-to-end integration tests for the fingerprinting system.
/// Tests the complete flow: audio capture → fingerprinting → metadata lookup → play history update.
/// </summary>
public class EndToEndFingerprintingIntegrationTests : IAsyncLifetime
{
  private readonly string _tempDirectory;
  private readonly Mock<ILogger<FingerprintDbContext>> _dbLoggerMock;
  private readonly Mock<ILogger<SqliteFingerprintCacheRepository>> _cacheLoggerMock;
  private readonly Mock<ILogger<SqliteTrackMetadataRepository>> _metadataLoggerMock;
  private readonly Mock<ILogger<SqlitePlayHistoryRepository>> _historyLoggerMock;
  private FingerprintDbContext _dbContext = null!;
  private SqliteFingerprintCacheRepository _cacheRepository = null!;
  private SqliteTrackMetadataRepository _metadataRepository = null!;
  private SqlitePlayHistoryRepository _historyRepository = null!;

  public EndToEndFingerprintingIntegrationTests()
  {
    _tempDirectory = Path.Combine(Path.GetTempPath(), $"E2EFingerprintTests_{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDirectory);

    _dbLoggerMock = new Mock<ILogger<FingerprintDbContext>>();
    _cacheLoggerMock = new Mock<ILogger<SqliteFingerprintCacheRepository>>();
    _metadataLoggerMock = new Mock<ILogger<SqliteTrackMetadataRepository>>();
    _historyLoggerMock = new Mock<ILogger<SqlitePlayHistoryRepository>>();
  }

  public async Task InitializeAsync()
  {
    var databaseOptions = Options.Create(new DatabaseOptions
    {
      RootPath = _tempDirectory,
      FingerprintingSubdirectory = "",
      FingerprintingFileName = "fingerprints.db"
    });
    var pathResolver = new DatabasePathResolver(databaseOptions);

    _dbContext = new FingerprintDbContext(_dbLoggerMock.Object, pathResolver);
    await _dbContext.InitializeAsync();

    _cacheRepository = new SqliteFingerprintCacheRepository(_cacheLoggerMock.Object, _dbContext);
    _metadataRepository = new SqliteTrackMetadataRepository(_metadataLoggerMock.Object, _dbContext);
    _historyRepository = new SqlitePlayHistoryRepository(_historyLoggerMock.Object, _dbContext);
  }

  public async Task DisposeAsync()
  {
    await _dbContext.DisposeAsync();
    SqliteConnection.ClearAllPools();

    try
    {
      if (Directory.Exists(_tempDirectory))
      {
        Directory.Delete(_tempDirectory, recursive: true);
      }
    }
    catch (IOException)
    {
      // Ignore cleanup errors
    }
  }

  [Fact]
  public async Task EndToEnd_SimulateFilePlayback_RecordsHistoryEntry()
  {
    // Arrange - Simulate playing a file
    var playEntry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.File,
      SourceDetails = "/music/test-song.mp3",
      WasIdentified = false
    };

    // Act - Record unidentified play
    await _historyRepository.RecordPlayAsync(playEntry);

    // Assert - Play was recorded
    var recorded = await _historyRepository.GetByIdAsync(playEntry.Id);
    Assert.NotNull(recorded);
    Assert.False(recorded.WasIdentified);
    Assert.Equal(PlaySource.File, recorded.Source);
  }

  [Fact]
  public async Task EndToEnd_MockFingerprinting_IdentifiesAndUpdatesHistory()
  {
    // Arrange - 1) Record unidentified play
    var playEntry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.Radio,
      SourceDetails = "FM 101.5 MHz",
      WasIdentified = false
    };
    await _historyRepository.RecordPlayAsync(playEntry);

    // 2) Simulate audio capture
    var audioProvider = new MockAudioSampleProvider();
    audioProvider.SetActive(true, "FM 101.5 MHz", PlaySource.Radio);
    var samples = await audioProvider.CaptureAsync(TimeSpan.FromSeconds(10));

    // 3) Simulate fingerprint generation
    var fingerprintData = new FingerprintData
    {
      Id = Guid.NewGuid().ToString(),
      ChromaprintHash = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
      DurationSeconds = 10,
      GeneratedAt = DateTime.UtcNow
    };

    // 4) Simulate metadata lookup (using mock service)
    var lookupService = new MockMetadataLookupService();
    lookupService.SetDefaultMetadata(new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Test Track",
      Artist = "Test Artist",
      Album = "Test Album",
      Source = MetadataSource.AcoustID,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    });
    var lookupResult = await lookupService.LookupAsync(fingerprintData);
    Assert.NotNull(lookupResult);

    // Verify we got metadata back and store it
    var metadata = lookupResult!.Metadata ?? throw new InvalidOperationException("Metadata should not be null");

    // Act - 5) Store fingerprint with metadata
    var cachedFingerprint = await _cacheRepository.StoreAsync(fingerprintData, metadata);

    // 6) Update play history with identification result
    var updatedEntry = playEntry with
    {
      TrackMetadataId = metadata.Id,
      MetadataSource = MetadataSource.AcoustID,
      WasIdentified = true,
      IdentificationConfidence = lookupResult.Confidence
    };
    await _historyRepository.UpdateAsync(updatedEntry);

    // Assert - History was updated with identification
    var finalEntry = await _historyRepository.GetByIdAsync(playEntry.Id);
    Assert.NotNull(finalEntry);
    Assert.True(finalEntry.WasIdentified);
    Assert.Equal(MetadataSource.AcoustID, finalEntry.MetadataSource);
    Assert.Equal(metadata.Id, finalEntry.TrackMetadataId);

    // Assert - Fingerprint was cached
    var cached = await _cacheRepository.FindByHashAsync(fingerprintData.ChromaprintHash);
    Assert.NotNull(cached);
    Assert.Equal(fingerprintData.Id, cached.Id);
  }

  [Fact]
  public async Task EndToEnd_DuplicateFingerprint_ReusesFromCache()
  {
    // Arrange - Store a fingerprint first
    var chromaprintHash = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    var originalFingerprint = new FingerprintData
    {
      Id = Guid.NewGuid().ToString(),
      ChromaprintHash = chromaprintHash,
      DurationSeconds = 30,
      GeneratedAt = DateTime.UtcNow.AddHours(-1)
    };
    var originalMetadata = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Original Track",
      Artist = "Original Artist",
      Source = MetadataSource.Fingerprinting,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    await _cacheRepository.StoreAsync(originalFingerprint, originalMetadata);

    // Act - "Generate" same fingerprint again (different ID but same hash)
    var newFingerprint = new FingerprintData
    {
      Id = Guid.NewGuid().ToString(),
      ChromaprintHash = chromaprintHash, // Same hash!
      DurationSeconds = 30,
      GeneratedAt = DateTime.UtcNow
    };

    // Look up in cache first
    var cached = await _cacheRepository.FindByHashAsync(newFingerprint.ChromaprintHash);

    // Assert - Found in cache with metadata
    Assert.NotNull(cached);
    Assert.Equal(chromaprintHash, cached.ChromaprintHash);
    Assert.NotNull(cached.Metadata);
    Assert.Equal("Original Track", cached.Metadata.Title);
    Assert.Equal("Original Artist", cached.Metadata.Artist);
  }

  [Fact]
  public async Task EndToEnd_MultiplePlaySources_TracksSeparately()
  {
    // Arrange - Create entries from different sources
    var fileEntry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow.AddMinutes(-30),
      Source = PlaySource.File,
      SourceDetails = "/music/track.mp3",
      WasIdentified = true,
      MetadataSource = MetadataSource.FileTag
    };
    var radioEntry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow.AddMinutes(-20),
      Source = PlaySource.Radio,
      SourceDetails = "FM 103.5",
      WasIdentified = true,
      MetadataSource = MetadataSource.Fingerprinting
    };
    var vinylEntry = new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow.AddMinutes(-10),
      Source = PlaySource.Vinyl,
      SourceDetails = "Vinyl: Test",
      WasIdentified = true,
      MetadataSource = MetadataSource.Manual
    };

    // Act
    await _historyRepository.RecordPlayAsync(fileEntry);
    await _historyRepository.RecordPlayAsync(radioEntry);
    await _historyRepository.RecordPlayAsync(vinylEntry);

    // Assert - Each source is tracked separately
    var fileResults = await _historyRepository.GetBySourceAsync(PlaySource.File, 10);
    var radioResults = await _historyRepository.GetBySourceAsync(PlaySource.Radio, 10);
    var vinylResults = await _historyRepository.GetBySourceAsync(PlaySource.Vinyl, 10);

    Assert.Single(fileResults);
    Assert.Equal(MetadataSource.FileTag, fileResults[0].MetadataSource);

    Assert.Single(radioResults);
    Assert.Equal(MetadataSource.Fingerprinting, radioResults[0].MetadataSource);

    Assert.Single(vinylResults);
    Assert.Equal(MetadataSource.Manual, vinylResults[0].MetadataSource);
  }

  [Fact]
  public async Task EndToEnd_AudioCapture_GeneratesValidSamples()
  {
    // Arrange
    var provider = new MockAudioSampleProvider();
    provider.SetActive(true, "Test Source", PlaySource.File);

    // Generate a sine wave for more realistic test
    provider.SetSampleGenerator(duration =>
      MockAudioSampleProvider.GenerateSineWave(duration, frequency: 440.0));

    // Act - Capture audio as would happen in real fingerprinting
    var captureResult = await provider.CaptureAsync(TimeSpan.FromSeconds(5));

    // Assert - Samples are valid for fingerprinting
    Assert.NotNull(captureResult);
    Assert.Equal(48000, captureResult.SampleRate);
    Assert.Equal(2, captureResult.Channels);
    Assert.True(captureResult.Samples.Length > 0);

    // Verify samples are normalized (between -1 and 1)
    Assert.All(captureResult.Samples, s =>
      Assert.True(s >= -1.0f && s <= 1.0f));
  }

  [Fact]
  public async Task EndToEnd_TrackIdentification_StoresMetadataWithFingerprint()
  {
    // Arrange
    var fingerprintData = new FingerprintData
    {
      Id = Guid.NewGuid().ToString(),
      ChromaprintHash = Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
      DurationSeconds = 30,
      GeneratedAt = DateTime.UtcNow
    };

    var metadata = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Bohemian Rhapsody",
      Artist = "Queen",
      Album = "A Night at the Opera",
      ReleaseYear = 1975,
      Genre = "Rock",
      Source = MetadataSource.AcoustID,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };

    // Act - Store fingerprint with full metadata
    var cached = await _cacheRepository.StoreAsync(fingerprintData, metadata);

    // Assert - Fingerprint and metadata are linked
    Assert.NotNull(cached);
    Assert.NotNull(cached.Metadata);
    Assert.Equal("Bohemian Rhapsody", cached.Metadata.Title);
    Assert.Equal("Queen", cached.Metadata.Artist);
    Assert.Equal(1975, cached.Metadata.ReleaseYear);

    // Verify lookup also returns metadata
    var found = await _cacheRepository.FindByHashAsync(fingerprintData.ChromaprintHash);
    Assert.NotNull(found?.Metadata);
    Assert.Equal("Bohemian Rhapsody", found.Metadata.Title);
  }

  [Fact]
  public async Task EndToEnd_RecentPlays_SearchWorks()
  {
    // Arrange - Store tracks with metadata
    var metadata1 = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Yesterday",
      Artist = "The Beatles",
      Album = "Help!",
      Source = MetadataSource.Fingerprinting,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    await _metadataRepository.StoreAsync(metadata1);

    var metadata2 = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = "Let It Be",
      Artist = "The Beatles",
      Album = "Let It Be",
      Source = MetadataSource.Fingerprinting,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };
    await _metadataRepository.StoreAsync(metadata2);

    // Record play history
    await _historyRepository.RecordPlayAsync(new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      TrackMetadataId = metadata1.Id,
      PlayedAt = DateTime.UtcNow.AddMinutes(-30),
      Source = PlaySource.Radio,
      WasIdentified = true
    });
    await _historyRepository.RecordPlayAsync(new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      TrackMetadataId = metadata2.Id,
      PlayedAt = DateTime.UtcNow.AddMinutes(-20),
      Source = PlaySource.Radio,
      WasIdentified = true
    });

    // Act - Search for "Beatles"
    var (results, total) = await _historyRepository.SearchAsync("Beatles");

    // Assert - Both tracks found
    Assert.Equal(2, total);
    Assert.Equal(2, results.Count);
  }

  [Fact]
  public async Task EndToEnd_Statistics_ReflectAllActivity()
  {
    // Arrange - Create various plays
    await _historyRepository.RecordPlayAsync(new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.File,
      WasIdentified = true
    });
    await _historyRepository.RecordPlayAsync(new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.Radio,
      WasIdentified = false
    });
    await _historyRepository.RecordPlayAsync(new PlayHistoryEntry
    {
      Id = Guid.NewGuid().ToString(),
      PlayedAt = DateTime.UtcNow,
      Source = PlaySource.Radio,
      WasIdentified = true
    });

    // Act
    var stats = await _historyRepository.GetStatisticsAsync();

    // Assert
    Assert.Equal(3, stats.TotalPlays);
    Assert.Equal(2, stats.IdentifiedPlays);
    Assert.Equal(1, stats.UnidentifiedPlays);
    Assert.Equal(1, stats.PlaysBySource[PlaySource.File]);
    Assert.Equal(2, stats.PlaysBySource[PlaySource.Radio]);
  }
}
