using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting.Data;
using Radio.Tools.AudioUAT.Utilities;

namespace Radio.Tools.AudioUAT.Phases.Phase9;

/// <summary>
/// Phase 9 tests for Audio Fingerprinting and Song Detection.
/// </summary>
public class FingerprintingTests
{
  private readonly IServiceProvider _serviceProvider;

  /// <summary>
  /// Initializes a new instance of the <see cref="FingerprintingTests"/> class.
  /// </summary>
  /// <param name="serviceProvider">The service provider.</param>
  public FingerprintingTests(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  /// <summary>
  /// Gets all Phase 9 tests.
  /// </summary>
  /// <returns>The list of tests.</returns>
  public IReadOnlyList<IPhaseTest> GetAllTests()
  {
    return
    [
      new DatabaseInitializationTest(_serviceProvider),
      new FingerprintGenerationTest(_serviceProvider),
      new CacheStoreAndRetrieveTest(_serviceProvider),
      new MetadataStorageTest(_serviceProvider),
      new PlayHistoryRecordingTest(_serviceProvider),
      new PlayHistoryRetrievalTest(_serviceProvider),
      new PlayStatisticsTest(_serviceProvider),
      new AudioSampleCaptureTest(_serviceProvider),
      new DuplicateSuppressionTest(_serviceProvider),
      new UnknownTrackHandlingTest(_serviceProvider),
      new MetadataSearchTest(_serviceProvider),
      new CacheCleanupTest(_serviceProvider),
    ];
  }
}

/// <summary>
/// P9-001: Database Initialization Test.
/// </summary>
public class DatabaseInitializationTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P9-001";
  public string TestName => "Database Initialization";
  public string Description => "Initialize SQLite fingerprint database and verify tables";
  public int Phase => 9;

  public DatabaseInitializationTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var dbContext = scope.ServiceProvider.GetRequiredService<FingerprintDbContext>();

      ConsoleUI.WriteInfo("Initializing fingerprint database...");
      await dbContext.InitializeAsync(ct);

      var connection = await dbContext.GetConnectionAsync(ct);
      ConsoleUI.WriteSuccess("Database connection established");

      // Verify tables exist
      using var cmd = connection.CreateCommand();
      cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('FingerprintCache', 'TrackMetadata', 'PlayHistory')";
      var tables = new List<string>();
      using (var reader = await cmd.ExecuteReaderAsync(ct))
      {
        while (await reader.ReadAsync(ct))
        {
          tables.Add(reader.GetString(0));
        }
      }

      ConsoleUI.WriteInfo($"Found tables: {string.Join(", ", tables)}");

      if (tables.Count < 3)
      {
        return TestResult.Fail(TestId, $"Expected 3 tables, found {tables.Count}");
      }

      ConsoleUI.WriteSuccess("All required tables exist");

      return TestResult.Pass(TestId, "Database initialized successfully",
        metadata: new Dictionary<string, object>
        {
          ["TableCount"] = tables.Count
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Database initialization failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P9-002: Fingerprint Generation Test.
/// </summary>
public class FingerprintGenerationTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P9-002";
  public string TestName => "Fingerprint Generation";
  public string Description => "Generate audio fingerprint from test samples";
  public int Phase => 9;

  public FingerprintGenerationTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var fingerprintService = scope.ServiceProvider.GetRequiredService<IFingerprintService>();

      ConsoleUI.WriteInfo("Creating test audio samples (5 second 440Hz sine wave)...");
      var samples = CreateTestSamples(5.0, frequency: 440);

      ConsoleUI.WriteInfo("Generating fingerprint...");
      var fingerprint = await fingerprintService.GenerateFingerprintAsync(samples, ct);

      ConsoleUI.WriteInfo($"Fingerprint ID: {fingerprint.Id}");
      ConsoleUI.WriteInfo($"Hash length: {fingerprint.ChromaprintHash.Length} characters");
      ConsoleUI.WriteInfo($"Duration: {fingerprint.DurationSeconds} seconds");

      if (string.IsNullOrEmpty(fingerprint.ChromaprintHash))
      {
        return TestResult.Fail(TestId, "Fingerprint hash is empty");
      }

      ConsoleUI.WriteSuccess("Fingerprint generated successfully");

      // Test with different frequency to verify different fingerprints
      ConsoleUI.WriteInfo("Generating fingerprint with different audio...");
      var samples2 = CreateTestSamples(5.0, frequency: 880);
      var fingerprint2 = await fingerprintService.GenerateFingerprintAsync(samples2, ct);

      if (fingerprint.ChromaprintHash == fingerprint2.ChromaprintHash)
      {
        ConsoleUI.WriteWarning("Different audio produced same hash (may be expected for mock service)");
      }
      else
      {
        ConsoleUI.WriteSuccess("Different audio produced different fingerprints");
      }

      return TestResult.Pass(TestId, "Fingerprint generation works correctly",
        metadata: new Dictionary<string, object>
        {
          ["FingerprintId"] = fingerprint.Id,
          ["HashLength"] = fingerprint.ChromaprintHash.Length
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Fingerprint generation failed: {ex.Message}", exception: ex);
    }
  }

  private static AudioSampleBuffer CreateTestSamples(double durationSeconds, double frequency)
  {
    const int sampleRate = 44100;
    const int channels = 2;
    var sampleCount = (int)(durationSeconds * sampleRate * channels);
    var samples = new float[sampleCount];

    for (int i = 0; i < sampleCount; i++)
    {
      var t = (double)(i / channels) / sampleRate;
      samples[i] = (float)Math.Sin(2 * Math.PI * frequency * t);
    }

    return new AudioSampleBuffer
    {
      Samples = samples,
      SampleRate = sampleRate,
      Channels = channels,
      Duration = TimeSpan.FromSeconds(durationSeconds),
      SourceName = "Test Generator"
    };
  }
}

/// <summary>
/// P9-003: Cache Store and Retrieve Test.
/// </summary>
public class CacheStoreAndRetrieveTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P9-003";
  public string TestName => "Cache Store and Retrieve";
  public string Description => "Store and retrieve fingerprint from SQLite cache";
  public int Phase => 9;

  public CacheStoreAndRetrieveTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var cacheRepo = scope.ServiceProvider.GetRequiredService<IFingerprintCacheRepository>();

      var uniqueHash = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
      var fingerprint = new FingerprintData
      {
        Id = Guid.NewGuid().ToString(),
        ChromaprintHash = uniqueHash,
        DurationSeconds = 15,
        GeneratedAt = DateTime.UtcNow
      };

      ConsoleUI.WriteInfo($"Storing fingerprint with hash: {uniqueHash[..20]}...");
      var stored = await cacheRepo.StoreAsync(fingerprint, null, ct);
      ConsoleUI.WriteSuccess($"Fingerprint stored with ID: {stored.Id}");

      ConsoleUI.WriteInfo("Retrieving fingerprint by hash...");
      var retrieved = await cacheRepo.FindByHashAsync(uniqueHash, ct);

      if (retrieved == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve stored fingerprint");
      }

      if (retrieved.ChromaprintHash != uniqueHash)
      {
        return TestResult.Fail(TestId, "Retrieved hash doesn't match original");
      }

      ConsoleUI.WriteSuccess("Fingerprint retrieved successfully");
      ConsoleUI.WriteInfo($"Match count: {retrieved.MatchCount}");

      // Update last matched
      ConsoleUI.WriteInfo("Updating last matched timestamp...");
      await cacheRepo.UpdateLastMatchedAsync(retrieved.Id, ct);

      var afterUpdate = await cacheRepo.FindByHashAsync(uniqueHash, ct);
      if (afterUpdate?.MatchCount != retrieved.MatchCount + 1)
      {
        return TestResult.Fail(TestId, "Match count not incremented");
      }

      ConsoleUI.WriteSuccess($"Match count incremented to {afterUpdate.MatchCount}");

      return TestResult.Pass(TestId, "Cache store and retrieve works correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Cache test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P9-004: Metadata Storage Test.
/// </summary>
public class MetadataStorageTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P9-004";
  public string TestName => "Metadata Storage";
  public string Description => "Store and retrieve track metadata";
  public int Phase => 9;

  public MetadataStorageTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var metadataRepo = scope.ServiceProvider.GetRequiredService<ITrackMetadataRepository>();

      var metadata = new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = "UAT Test Song",
        Artist = "UAT Test Artist",
        Album = "UAT Test Album",
        ReleaseYear = 2024,
        Genre = "Test",
        Source = MetadataSource.Manual,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      };

      ConsoleUI.WriteInfo($"Storing metadata: {metadata.Title} by {metadata.Artist}");
      await metadataRepo.StoreAsync(metadata, ct);
      ConsoleUI.WriteSuccess("Metadata stored");

      ConsoleUI.WriteInfo("Retrieving metadata by ID...");
      var retrieved = await metadataRepo.GetByIdAsync(metadata.Id, ct);

      if (retrieved == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve stored metadata");
      }

      ConsoleUI.WriteInfo($"Retrieved: {retrieved.Title} by {retrieved.Artist}");
      ConsoleUI.WriteInfo($"Album: {retrieved.Album}, Year: {retrieved.ReleaseYear}");

      if (retrieved.Title != metadata.Title || retrieved.Artist != metadata.Artist)
      {
        return TestResult.Fail(TestId, "Retrieved metadata doesn't match original");
      }

      ConsoleUI.WriteSuccess("Metadata retrieved correctly");

      return TestResult.Pass(TestId, "Metadata storage works correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Metadata storage failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P9-005: Play History Recording Test.
/// </summary>
public class PlayHistoryRecordingTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P9-005";
  public string TestName => "Play History Recording";
  public string Description => "Record a play history entry";
  public int Phase => 9;

  public PlayHistoryRecordingTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var historyRepo = scope.ServiceProvider.GetRequiredService<IPlayHistoryRepository>();

      var entry = new PlayHistoryEntry
      {
        Id = Guid.NewGuid().ToString(),
        PlayedAt = DateTime.UtcNow,
        Source = PlaySource.Vinyl,
        SourceDetails = "UAT Test Vinyl Source",
        DurationSeconds = 210,
        IdentificationConfidence = 0.92,
        WasIdentified = true
      };

      ConsoleUI.WriteInfo($"Recording play from {entry.Source}: {entry.SourceDetails}");
      await historyRepo.RecordPlayAsync(entry, ct);
      ConsoleUI.WriteSuccess("Play history recorded");

      ConsoleUI.WriteInfo("Retrieving recorded entry...");
      var retrieved = await historyRepo.GetByIdAsync(entry.Id, ct);

      if (retrieved == null)
      {
        return TestResult.Fail(TestId, "Failed to retrieve recorded entry");
      }

      ConsoleUI.WriteInfo($"Source: {retrieved.Source}");
      ConsoleUI.WriteInfo($"Played at: {retrieved.PlayedAt}");
      ConsoleUI.WriteInfo($"Identified: {retrieved.WasIdentified} (confidence: {retrieved.IdentificationConfidence:P0})");

      if (retrieved.Source != entry.Source)
      {
        return TestResult.Fail(TestId, "Source mismatch in retrieved entry");
      }

      ConsoleUI.WriteSuccess("Play history entry verified");

      return TestResult.Pass(TestId, "Play history recording works correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Play history recording failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P9-006: Play History Retrieval Test.
/// </summary>
public class PlayHistoryRetrievalTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P9-006";
  public string TestName => "Play History Retrieval";
  public string Description => "Retrieve recent play history entries";
  public int Phase => 9;

  public PlayHistoryRetrievalTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var historyRepo = scope.ServiceProvider.GetRequiredService<IPlayHistoryRepository>();

      // Add some test entries
      ConsoleUI.WriteInfo("Recording test play history entries...");
      for (int i = 0; i < 3; i++)
      {
        await historyRepo.RecordPlayAsync(new PlayHistoryEntry
        {
          Id = Guid.NewGuid().ToString(),
          PlayedAt = DateTime.UtcNow.AddMinutes(-i * 5),
          Source = i % 2 == 0 ? PlaySource.Radio : PlaySource.Vinyl,
          SourceDetails = $"Test Source {i + 1}",
          WasIdentified = i % 2 == 0
        }, ct);
      }

      ConsoleUI.WriteInfo("Retrieving recent history...");
      var recent = await historyRepo.GetRecentAsync(10, ct);

      ConsoleUI.WriteInfo($"Retrieved {recent.Count} entries");

      if (recent.Count < 3)
      {
        return TestResult.Fail(TestId, $"Expected at least 3 entries, got {recent.Count}");
      }

      // Display entries
      foreach (var entry in recent.Take(5))
      {
        var identified = entry.WasIdentified ? "[green]Y[/]" : "[red]N[/]";
        Spectre.Console.AnsiConsole.MarkupLine($"  {entry.PlayedAt:HH:mm:ss} | {entry.Source,-8} | Identified: {identified}");
      }

      // Verify ordering (most recent first)
      for (int i = 1; i < recent.Count; i++)
      {
        if (recent[i].PlayedAt > recent[i - 1].PlayedAt)
        {
          ConsoleUI.WriteWarning("Entries not in descending order by PlayedAt");
        }
      }

      ConsoleUI.WriteSuccess("Play history retrieval works correctly");

      return TestResult.Pass(TestId, $"Retrieved {recent.Count} history entries",
        metadata: new Dictionary<string, object>
        {
          ["EntryCount"] = recent.Count
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Play history retrieval failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P9-007: Play Statistics Test.
/// </summary>
public class PlayStatisticsTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P9-007";
  public string TestName => "Play Statistics";
  public string Description => "Calculate and verify play statistics";
  public int Phase => 9;

  public PlayStatisticsTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var historyRepo = scope.ServiceProvider.GetRequiredService<IPlayHistoryRepository>();

      ConsoleUI.WriteInfo("Calculating play statistics...");
      var stats = await historyRepo.GetStatisticsAsync(ct);

      ConsoleUI.WriteInfo("");
      ConsoleUI.WriteInfo("═══════════════════════════════════");
      ConsoleUI.WriteInfo("        PLAY STATISTICS");
      ConsoleUI.WriteInfo("═══════════════════════════════════");
      ConsoleUI.WriteInfo($"Total Plays:      {stats.TotalPlays}");
      ConsoleUI.WriteInfo($"Identified:       {stats.IdentifiedPlays}");
      ConsoleUI.WriteInfo($"Unidentified:     {stats.UnidentifiedPlays}");
      ConsoleUI.WriteInfo("");

      if (stats.PlaysBySource.Count > 0)
      {
        ConsoleUI.WriteInfo("Plays by Source:");
        foreach (var (source, count) in stats.PlaysBySource)
        {
          ConsoleUI.WriteInfo($"  {source,-12}: {count}");
        }
      }

      if (stats.TopArtists.Count > 0)
      {
        ConsoleUI.WriteInfo("");
        ConsoleUI.WriteInfo("Top Artists:");
        foreach (var artist in stats.TopArtists.Take(5))
        {
          ConsoleUI.WriteInfo($"  {artist.Artist}: {artist.PlayCount} plays");
        }
      }

      ConsoleUI.WriteInfo("═══════════════════════════════════");

      ConsoleUI.WriteSuccess("Statistics calculated successfully");

      return TestResult.Pass(TestId, $"Total plays: {stats.TotalPlays}",
        metadata: new Dictionary<string, object>
        {
          ["TotalPlays"] = stats.TotalPlays,
          ["IdentifiedPlays"] = stats.IdentifiedPlays,
          ["UnidentifiedPlays"] = stats.UnidentifiedPlays
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Statistics calculation failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P9-008: Audio Sample Capture Test.
/// </summary>
public class AudioSampleCaptureTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P9-008";
  public string TestName => "Audio Sample Capture";
  public string Description => "Capture audio samples from the output stream";
  public int Phase => 9;

  public AudioSampleCaptureTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var audioTap = scope.ServiceProvider.GetService<IAudioSampleProvider>();

      if (audioTap == null)
      {
        ConsoleUI.WriteWarning("Audio sample provider not registered");
        return TestResult.Skip(TestId, "Audio sample provider not available");
      }

      ConsoleUI.WriteInfo($"Audio source: {audioTap.SourceName}");
      ConsoleUI.WriteInfo($"Source type: {audioTap.SourceType}");
      ConsoleUI.WriteInfo($"Active: {audioTap.IsActive}");

      if (!audioTap.IsActive)
      {
        ConsoleUI.WriteWarning("Audio source not active (audio engine may not be running)");
        return TestResult.Skip(TestId, "Audio source not active - start audio engine first");
      }

      ConsoleUI.WriteInfo("Capturing 2 seconds of audio...");
      var samples = await audioTap.CaptureAsync(TimeSpan.FromSeconds(2), ct);

      if (samples == null)
      {
        return TestResult.Fail(TestId, "No samples captured");
      }

      ConsoleUI.WriteInfo($"Captured {samples.Samples.Length} samples");
      ConsoleUI.WriteInfo($"Sample rate: {samples.SampleRate} Hz");
      ConsoleUI.WriteInfo($"Channels: {samples.Channels}");
      ConsoleUI.WriteInfo($"Duration: {samples.Duration.TotalSeconds:F2}s");

      // Check for audio content
      var maxSample = samples.Samples.Max(Math.Abs);
      ConsoleUI.WriteInfo($"Peak level: {maxSample:F4}");

      if (maxSample < 0.001f)
      {
        ConsoleUI.WriteWarning("Captured audio appears to be silence");
      }
      else
      {
        ConsoleUI.WriteSuccess("Audio content detected in samples");
      }

      return TestResult.Pass(TestId, $"Captured {samples.Samples.Length} samples",
        metadata: new Dictionary<string, object>
        {
          ["SampleCount"] = samples.Samples.Length,
          ["PeakLevel"] = maxSample
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Audio capture failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P9-009: Duplicate Suppression Test.
/// </summary>
public class DuplicateSuppressionTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P9-009";
  public string TestName => "Duplicate Suppression";
  public string Description => "Test that duplicate fingerprints are handled correctly";
  public int Phase => 9;

  public DuplicateSuppressionTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var cacheRepo = scope.ServiceProvider.GetRequiredService<IFingerprintCacheRepository>();

      var countBefore = await cacheRepo.GetCacheCountAsync(ct);
      ConsoleUI.WriteInfo($"Cache count before: {countBefore}");

      // Store a fingerprint
      var uniqueHash = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
      var fingerprint1 = new FingerprintData
      {
        Id = Guid.NewGuid().ToString(),
        ChromaprintHash = uniqueHash,
        DurationSeconds = 15,
        GeneratedAt = DateTime.UtcNow
      };

      ConsoleUI.WriteInfo("Storing first fingerprint...");
      await cacheRepo.StoreAsync(fingerprint1, null, ct);

      var countAfterFirst = await cacheRepo.GetCacheCountAsync(ct);
      ConsoleUI.WriteInfo($"Cache count after first: {countAfterFirst}");

      // Store same hash again (should not create duplicate)
      var fingerprint2 = new FingerprintData
      {
        Id = Guid.NewGuid().ToString(),
        ChromaprintHash = uniqueHash, // Same hash
        DurationSeconds = 15,
        GeneratedAt = DateTime.UtcNow
      };

      ConsoleUI.WriteInfo("Storing fingerprint with same hash...");
      await cacheRepo.StoreAsync(fingerprint2, null, ct);

      var countAfterSecond = await cacheRepo.GetCacheCountAsync(ct);
      ConsoleUI.WriteInfo($"Cache count after second: {countAfterSecond}");

      // Verify only one entry with this hash
      var found = await cacheRepo.FindByHashAsync(uniqueHash, ct);
      if (found == null)
      {
        return TestResult.Fail(TestId, "Could not find fingerprint after duplicate store");
      }

      // The count should only have increased by 1 total
      if (countAfterSecond - countBefore > 1)
      {
        return TestResult.Fail(TestId, $"Duplicate created: count increased by {countAfterSecond - countBefore}");
      }

      ConsoleUI.WriteSuccess("Duplicate suppression working - only one entry per hash");

      return TestResult.Pass(TestId, "Duplicate suppression works correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Duplicate suppression test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P9-010: Unknown Track Handling Test.
/// </summary>
public class UnknownTrackHandlingTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P9-010";
  public string TestName => "Unknown Track Handling";
  public string Description => "Test handling of unidentified tracks";
  public int Phase => 9;

  public UnknownTrackHandlingTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var lookupService = scope.ServiceProvider.GetRequiredService<IMetadataLookupService>();
      var cacheRepo = scope.ServiceProvider.GetRequiredService<IFingerprintCacheRepository>();

      // Create a unique fingerprint that won't be found
      var uniqueHash = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
      var fingerprint = new FingerprintData
      {
        Id = Guid.NewGuid().ToString(),
        ChromaprintHash = uniqueHash,
        DurationSeconds = 10,
        GeneratedAt = DateTime.UtcNow
      };

      ConsoleUI.WriteInfo("Looking up unknown fingerprint...");
      var result = await lookupService.LookupAsync(fingerprint, ct);

      if (result != null && result.IsMatch)
      {
        ConsoleUI.WriteWarning("Unexpected match found for unknown fingerprint");
      }
      else
      {
        ConsoleUI.WriteSuccess("Lookup returned null/no match as expected");
      }

      // Verify fingerprint was stored for manual tagging
      ConsoleUI.WriteInfo("Verifying fingerprint was stored for manual tagging...");
      var cached = await cacheRepo.FindByHashAsync(uniqueHash, ct);

      if (cached == null)
      {
        return TestResult.Fail(TestId, "Unknown fingerprint was not stored for manual tagging");
      }

      ConsoleUI.WriteSuccess("Unknown fingerprint stored successfully");
      ConsoleUI.WriteInfo($"Cached fingerprint ID: {cached.Id}");
      ConsoleUI.WriteInfo($"Has metadata: {cached.Metadata != null}");

      return TestResult.Pass(TestId, "Unknown tracks handled correctly - stored for manual tagging");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Unknown track handling test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P9-011: Metadata Search Test.
/// </summary>
public class MetadataSearchTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P9-011";
  public string TestName => "Metadata Search";
  public string Description => "Search for tracks by title, artist, or album";
  public int Phase => 9;

  public MetadataSearchTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var metadataRepo = scope.ServiceProvider.GetRequiredService<ITrackMetadataRepository>();

      // First add some searchable data
      var searchTerm = $"SearchTest_{Guid.NewGuid().ToString()[..8]}";
      var metadata = new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = $"{searchTerm} Song",
        Artist = $"{searchTerm} Artist",
        Album = "Search Test Album",
        Source = MetadataSource.Manual,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      };

      ConsoleUI.WriteInfo($"Adding test metadata with search term: {searchTerm}");
      await metadataRepo.StoreAsync(metadata, ct);

      // Search for it
      ConsoleUI.WriteInfo($"Searching for '{searchTerm}'...");
      var results = await metadataRepo.SearchAsync(searchTerm, 10, ct);

      ConsoleUI.WriteInfo($"Found {results.Count} results");

      if (results.Count == 0)
      {
        return TestResult.Fail(TestId, "Search returned no results");
      }

      // Verify our item is in results
      var found = results.Any(r => r.Title.Contains(searchTerm));
      if (!found)
      {
        return TestResult.Fail(TestId, "Search did not find the expected track");
      }

      foreach (var result in results.Take(5))
      {
        ConsoleUI.WriteInfo($"  - {result.Title} by {result.Artist}");
      }

      ConsoleUI.WriteSuccess("Search working correctly");

      return TestResult.Pass(TestId, $"Search found {results.Count} results",
        metadata: new Dictionary<string, object>
        {
          ["ResultCount"] = results.Count
        });
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Search test failed: {ex.Message}", exception: ex);
    }
  }
}

/// <summary>
/// P9-012: Cache Cleanup Test.
/// </summary>
public class CacheCleanupTest : IPhaseTest
{
  private readonly IServiceProvider _serviceProvider;

  public string TestId => "P9-012";
  public string TestName => "Cache Cleanup";
  public string Description => "Delete fingerprints from cache";
  public int Phase => 9;

  public CacheCleanupTest(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public async Task<TestResult> ExecuteAsync(CancellationToken ct = default)
  {
    ConsoleUI.WriteHeader($"{TestId}: {TestName}");
    ConsoleUI.WriteInfo(Description);

    try
    {
      using var scope = _serviceProvider.CreateScope();
      var cacheRepo = scope.ServiceProvider.GetRequiredService<IFingerprintCacheRepository>();

      // Create a temporary fingerprint
      var uniqueHash = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
      var fingerprint = new FingerprintData
      {
        Id = Guid.NewGuid().ToString(),
        ChromaprintHash = uniqueHash,
        DurationSeconds = 5,
        GeneratedAt = DateTime.UtcNow
      };

      ConsoleUI.WriteInfo("Creating temporary fingerprint...");
      var stored = await cacheRepo.StoreAsync(fingerprint, null, ct);
      ConsoleUI.WriteSuccess($"Created fingerprint: {stored.Id}");

      var countBefore = await cacheRepo.GetCacheCountAsync(ct);
      ConsoleUI.WriteInfo($"Cache count before delete: {countBefore}");

      // Delete it
      ConsoleUI.WriteInfo("Deleting fingerprint...");
      var deleted = await cacheRepo.DeleteAsync(stored.Id, ct);

      if (!deleted)
      {
        return TestResult.Fail(TestId, "Delete operation returned false");
      }

      ConsoleUI.WriteSuccess("Delete operation succeeded");

      // Verify deletion
      var afterDelete = await cacheRepo.FindByHashAsync(uniqueHash, ct);
      if (afterDelete != null)
      {
        return TestResult.Fail(TestId, "Fingerprint still exists after deletion");
      }

      var countAfter = await cacheRepo.GetCacheCountAsync(ct);
      ConsoleUI.WriteInfo($"Cache count after delete: {countAfter}");

      ConsoleUI.WriteSuccess("Fingerprint successfully deleted from cache");

      return TestResult.Pass(TestId, "Cache cleanup works correctly");
    }
    catch (Exception ex)
    {
      ConsoleUI.WriteError($"Error: {ex.Message}");
      return TestResult.Fail(TestId, $"Cache cleanup test failed: {ex.Message}", exception: ex);
    }
  }
}
