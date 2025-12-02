# Audio Fingerprinting & Metadata Recognition Design

## Overview

This document outlines the integration of audio fingerprinting technology into the Radio Console project to enable automatic song identification for:
- **Vinyl Record Player** - Identify songs playing from the turntable
- **Radio Streams** - Detect songs playing on radio stations  
- **Audio Files** - Identify files with missing or no embedded metadata

Identified tracks automatically feed into the **Play History** feature.

---

## Architecture Overview

### Identification Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           AUDIO IDENTIFICATION FLOW                         │
└─────────────────────────────────────────────────────────────────────────────┘

     Audio Source (Vinyl/Radio/File)
                    │
                    ▼
     ┌──────────────────────────────┐
     │  Capture Audio Samples       │
     │  (Resample to required rate) │
     └──────────────────────────────┘
                    │
                    ▼
     ┌──────────────────────────────┐
     │  Generate Chromaprint        │
     │  Fingerprint                 │
     └──────────────────────────────┘
                    │
                    ▼
     ┌──────────────────────────────┐
     │  1. Query LOCAL SQLite Cache │◄──── Match Found? ───► Return Cached Metadata
     └──────────────────────────────┘              │
                    │ NO                           │
                    ▼                              │
     ┌──────────────────────────────┐              │
     │  2. Query AcoustID API       │◄──── Match Found? ───► Get MusicBrainz IDs
     └──────────────────────────────┘              │
                    │ NO                           │
                    ▼                              ▼
     ┌──────────────────────────────┐   ┌──────────────────────────────┐
     │  Mark as "Unknown"           │   │  3. Fetch MusicBrainz        │
     │  (Store fingerprint for      │   │     Metadata                 │
     │   manual tagging later)      │   └──────────────────────────────┘
     └──────────────────────────────┘              │
                                                   ▼
                                       ┌──────────────────────────────┐
                                       │  4. Cache in SQLite          │
                                       │     (Fingerprint + Metadata) │
                                       └──────────────────────────────┘
                                                   │
                                                   ▼
                                       ┌──────────────────────────────┐
                                       │  5. Record to Play History   │
                                       └──────────────────────────────┘
```

### Technology Stack

| Component | Technology | Purpose |
|-----------|------------|---------|
| Fingerprint Generation | Chromaprint | Generate audio fingerprints compatible with AcoustID |
| External Lookup | AcoustID API | Match fingerprints to MusicBrainz recording IDs |
| Metadata Enrichment | MusicBrainz API | Fetch detailed track/artist/album metadata |
| Local Cache | SQLite | Store fingerprints and metadata for offline/fast lookup |
| Audio Capture | SoundFlow | Capture audio samples from playback pipeline |

---

## Database Schema

### SQLite Tables

```sql
-- Fingerprint cache table
CREATE TABLE IF NOT EXISTS FingerprintCache (
    Id TEXT PRIMARY KEY,                    -- GUID
    ChromaprintHash TEXT NOT NULL UNIQUE,   -- Base64-encoded fingerprint
    Duration INTEGER NOT NULL,              -- Audio duration in seconds
    AcoustId TEXT,                          -- AcoustID recording ID (if matched)
    MusicBrainzRecordingId TEXT,           -- MusicBrainz recording MBID
    CreatedAt TEXT NOT NULL,               -- ISO 8601 timestamp
    LastMatchedAt TEXT,                    -- Last time this was matched
    MatchCount INTEGER DEFAULT 0           -- Number of times matched
);

-- Track metadata table (enriched from MusicBrainz)
CREATE TABLE IF NOT EXISTS TrackMetadata (
    Id TEXT PRIMARY KEY,                    -- GUID
    FingerprintId TEXT NOT NULL,           -- FK to FingerprintCache
    Title TEXT NOT NULL,
    Artist TEXT NOT NULL,
    Album TEXT,
    AlbumArtist TEXT,
    TrackNumber INTEGER,
    DiscNumber INTEGER,
    ReleaseYear INTEGER,
    Genre TEXT,
    MusicBrainzArtistId TEXT,
    MusicBrainzReleaseId TEXT,
    MusicBrainzRecordingId TEXT,
    CoverArtUrl TEXT,
    Source TEXT NOT NULL,                  -- 'AcoustID', 'Manual', 'FileTag'
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (FingerprintId) REFERENCES FingerprintCache(Id)
);

-- Play history table
CREATE TABLE IF NOT EXISTS PlayHistory (
    Id TEXT PRIMARY KEY,                    -- GUID
    TrackMetadataId TEXT,                  -- FK to TrackMetadata (nullable for unknown)
    FingerprintId TEXT,                    -- FK to FingerprintCache
    PlayedAt TEXT NOT NULL,                -- ISO 8601 timestamp
    Source TEXT NOT NULL,                  -- 'Vinyl', 'Radio', 'File', 'Spotify'
    SourceDetails TEXT,                    -- e.g., radio station name, file path
    Duration INTEGER,                      -- How long it played (seconds)
    IdentificationConfidence REAL,         -- 0.0 to 1.0
    WasIdentified INTEGER NOT NULL,        -- 1 if identified, 0 if unknown
    FOREIGN KEY (TrackMetadataId) REFERENCES TrackMetadata(Id),
    FOREIGN KEY (FingerprintId) REFERENCES FingerprintCache(Id)
);

-- Indexes for performance
CREATE INDEX IF NOT EXISTS IX_FingerprintCache_ChromaprintHash ON FingerprintCache(ChromaprintHash);
CREATE INDEX IF NOT EXISTS IX_FingerprintCache_AcoustId ON FingerprintCache(AcoustId);
CREATE INDEX IF NOT EXISTS IX_TrackMetadata_FingerprintId ON TrackMetadata(FingerprintId);
CREATE INDEX IF NOT EXISTS IX_TrackMetadata_Artist ON TrackMetadata(Artist);
CREATE INDEX IF NOT EXISTS IX_TrackMetadata_Title ON TrackMetadata(Title);
CREATE INDEX IF NOT EXISTS IX_PlayHistory_PlayedAt ON PlayHistory(PlayedAt);
CREATE INDEX IF NOT EXISTS IX_PlayHistory_TrackMetadataId ON PlayHistory(TrackMetadataId);
```

---

## Prerequisites

### NuGet Packages Required

```xml
<!-- Add to Radio.Infrastructure.csproj -->

<!-- Chromaprint for fingerprint generation -->
<PackageReference Include="Chromaprint.NET" Version="1.*" />

<!-- HTTP client for API calls -->
<PackageReference Include="Microsoft.Extensions.Http" Version="8.*" />

<!-- JSON handling (likely already present) -->
<PackageReference Include="System.Text.Json" Version="8.*" />
```

### External API Requirements

#### AcoustID API
- **Registration**: https://acoustid.org/new-application
- **API Key**: Free, required for lookups
- **Rate Limit**: 3 requests per second
- **Documentation**: https://acoustid.org/webservice

#### MusicBrainz API
- **Registration**: Not required, but recommended for higher rate limits
- **Rate Limit**: 1 request per second (anonymous), higher with auth
- **User-Agent**: Required, must identify your application
- **Documentation**: https://musicbrainz.org/doc/MusicBrainz_API

---

## Phase 1: Core Infrastructure

### Step 1.1: Create Core Interfaces

**Prompt for Copilot:**
```
Create the following interfaces in Radio.Core/Interfaces/Audio/:

1. IFingerprintService.cs - Service for generating audio fingerprints:
   - Task<FingerprintData> GenerateFingerprintAsync(AudioSampleBuffer samples, CancellationToken ct)
   - Task<FingerprintData> GenerateFingerprintFromFileAsync(string filePath, CancellationToken ct)

2. IMetadataLookupService.cs - Service for looking up track metadata:
   - Task<MetadataLookupResult?> LookupAsync(FingerprintData fingerprint, CancellationToken ct)
   - Task<TrackMetadata?> GetMusicBrainzMetadataAsync(string recordingId, CancellationToken ct)

3. IFingerprintCacheRepository.cs - Repository for fingerprint cache (SQLite):
   - Task<CachedFingerprint?> FindByHashAsync(string chromaprintHash, CancellationToken ct)
   - Task<CachedFingerprint> StoreAsync(FingerprintData fingerprint, TrackMetadata? metadata, CancellationToken ct)
   - Task UpdateLastMatchedAsync(string id, CancellationToken ct)
   - Task<int> GetCacheCountAsync(CancellationToken ct)

4. ITrackMetadataRepository.cs - Repository for track metadata:
   - Task<TrackMetadata?> GetByIdAsync(string id, CancellationToken ct)
   - Task<TrackMetadata?> GetByFingerprintIdAsync(string fingerprintId, CancellationToken ct)
   - Task<TrackMetadata> StoreAsync(TrackMetadata metadata, CancellationToken ct)
   - Task<IReadOnlyList<TrackMetadata>> SearchAsync(string query, int limit, CancellationToken ct)

5. IPlayHistoryRepository.cs - Repository for play history:
   - Task RecordPlayAsync(PlayHistoryEntry entry, CancellationToken ct)
   - Task<IReadOnlyList<PlayHistoryEntry>> GetRecentAsync(int count, CancellationToken ct)
   - Task<IReadOnlyList<PlayHistoryEntry>> GetByDateRangeAsync(DateTime start, DateTime end, CancellationToken ct)
   - Task<PlayStatistics> GetStatisticsAsync(CancellationToken ct)

Reference the existing interface patterns in Radio.Core/Interfaces/ for consistency.
Follow the repository pattern used in the configuration system.
```

### Step 1.2: Create Core Models

**Prompt for Copilot:**
```
Create the following models in Radio.Core/Models/Audio/:

1. FingerprintData.cs:
   - string Id (GUID)
   - string ChromaprintHash (base64 encoded fingerprint)
   - int DurationSeconds
   - DateTime GeneratedAt
   - string? SourcePath

2. MetadataLookupResult.cs:
   - bool IsMatch
   - double Confidence (0.0 to 1.0)
   - string? AcoustId
   - string? MusicBrainzRecordingId
   - TrackMetadata? Metadata
   - LookupSource Source (enum: Cache, AcoustID, Manual)

3. TrackMetadata.cs:
   - string Id (GUID)
   - string? FingerprintId
   - string Title
   - string Artist
   - string? Album
   - string? AlbumArtist
   - int? TrackNumber
   - int? DiscNumber
   - int? ReleaseYear
   - string? Genre
   - string? MusicBrainzArtistId
   - string? MusicBrainzReleaseId
   - string? MusicBrainzRecordingId
   - string? CoverArtUrl
   - MetadataSource Source (enum: AcoustID, Manual, FileTag)
   - DateTime CreatedAt
   - DateTime UpdatedAt

4. CachedFingerprint.cs:
   - string Id
   - string ChromaprintHash
   - int DurationSeconds
   - string? AcoustId
   - string? MusicBrainzRecordingId
   - DateTime CreatedAt
   - DateTime? LastMatchedAt
   - int MatchCount
   - TrackMetadata? Metadata

5. PlayHistoryEntry.cs:
   - string Id (GUID)
   - string? TrackMetadataId
   - string? FingerprintId
   - TrackMetadata? Track (navigation property, not stored)
   - DateTime PlayedAt
   - PlaySource Source (enum: Vinyl, Radio, File, Spotify)
   - string? SourceDetails
   - int? DurationSeconds
   - double? IdentificationConfidence
   - bool WasIdentified

6. PlayStatistics.cs:
   - int TotalPlays
   - int IdentifiedPlays
   - int UnidentifiedPlays
   - Dictionary<string, int> PlaysBySource
   - IReadOnlyList<ArtistPlayCount> TopArtists
   - IReadOnlyList<TrackPlayCount> TopTracks

7. AudioSampleBuffer.cs:
   - float[] Samples
   - int SampleRate
   - int Channels
   - TimeSpan Duration
   - string? SourceName

Use record types where appropriate for immutability.
Include XML documentation comments.
```

### Step 1.3: Create Configuration Options

**Prompt for Copilot:**
```
Create Radio.Core/Configuration/FingerprintingOptions.cs:

/// <summary>
/// Configuration options for the audio fingerprinting system.
/// </summary>
public sealed class FingerprintingOptions
{
    /// <summary>Configuration section name for binding.</summary>
    public const string SectionName = "Fingerprinting";

    /// <summary>Enable or disable automatic fingerprinting.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Duration of audio to capture for fingerprinting (seconds).</summary>
    public int SampleDurationSeconds { get; set; } = 15;

    /// <summary>Interval between identification attempts (seconds).</summary>
    public int IdentificationIntervalSeconds { get; set; } = 30;

    /// <summary>Minimum confidence threshold for accepting a match (0.0 to 1.0).</summary>
    public double MinimumConfidenceThreshold { get; set; } = 0.5;

    /// <summary>Minutes to suppress duplicate identifications of the same track.</summary>
    public int DuplicateSuppressionMinutes { get; set; } = 5;

    /// <summary>AcoustID API configuration.</summary>
    public AcoustIdOptions AcoustId { get; set; } = new();

    /// <summary>MusicBrainz API configuration.</summary>
    public MusicBrainzOptions MusicBrainz { get; set; } = new();

    /// <summary>SQLite database path for fingerprint cache.</summary>
    public string DatabasePath { get; set; } = "./data/fingerprints.db";
}

public sealed class AcoustIdOptions
{
    /// <summary>AcoustID API key (register at https://acoustid.org/new-application).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>AcoustID API base URL.</summary>
    public string BaseUrl { get; set; } = "https://api.acoustid.org/v2";

    /// <summary>Maximum requests per second (AcoustID limit is 3).</summary>
    public int MaxRequestsPerSecond { get; set; } = 3;

    /// <summary>Request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}

public sealed class MusicBrainzOptions
{
    /// <summary>MusicBrainz API base URL.</summary>
    public string BaseUrl { get; set; } = "https://musicbrainz.org/ws/2";

    /// <summary>Application name for User-Agent header.</summary>
    public string ApplicationName { get; set; } = "RadioConsole";

    /// <summary>Application version for User-Agent header.</summary>
    public string ApplicationVersion { get; set; } = "1.0.0";

    /// <summary>Contact email for User-Agent header.</summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>Maximum requests per second (MusicBrainz limit is 1 for anonymous).</summary>
    public int MaxRequestsPerSecond { get; set; } = 1;

    /// <summary>Request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;
}

Follow the existing options pattern in Radio.Core/Configuration/.
```

---

## Phase 2: SQLite Repository Implementation

### Step 2.1: Create Database Context

**Prompt for Copilot:**
```
Create Radio.Infrastructure/Audio/Fingerprinting/Data/FingerprintDbContext.cs:

This class should:
1. Manage the SQLite connection for fingerprinting data
2. Follow the patterns established in SqliteConfigurationStore.cs
3. Handle database initialization and table creation
4. Use connection pooling appropriately
5. Implement IAsyncDisposable

Include methods:
- Task InitializeAsync(CancellationToken ct)
- SqliteConnection GetConnection()
- Task<bool> TableExistsAsync(string tableName, CancellationToken ct)

Create all three tables (FingerprintCache, TrackMetadata, PlayHistory) with proper indexes.

Reference the existing SqliteConfigurationStore for patterns:
- Connection string handling
- Table creation with IF NOT EXISTS
- Parameterized queries
- Async/await patterns
- Logging
```

### Step 2.2: Implement Fingerprint Cache Repository

**Prompt for Copilot:**
```
Create Radio.Infrastructure/Audio/Fingerprinting/Data/SqliteFingerprintCacheRepository.cs:

Implement IFingerprintCacheRepository with:
1. Dependency on FingerprintDbContext
2. All CRUD operations using parameterized queries
3. Proper null handling
4. Logging via ILogger<SqliteFingerprintCacheRepository>
5. CancellationToken support throughout

Key methods:
- FindByHashAsync: Query by ChromaprintHash, include related TrackMetadata
- StoreAsync: Insert fingerprint and optionally linked metadata
- UpdateLastMatchedAsync: Update LastMatchedAt and increment MatchCount

Follow the query patterns in SqliteConfigurationStore.cs.
Use DateTimeOffset.UtcNow.ToString("O") for timestamps.
```

### Step 2.3: Implement Track Metadata Repository

**Prompt for Copilot:**
```
Create Radio.Infrastructure/Audio/Fingerprinting/Data/SqliteTrackMetadataRepository.cs:

Implement ITrackMetadataRepository with:
1. Dependency on FingerprintDbContext
2. Full-text search capability for SearchAsync
3. LIKE queries for artist/title search
4. Proper JOIN with FingerprintCache when needed

SearchAsync should search across Title, Artist, and Album fields.
```

### Step 2.4: Implement Play History Repository

**Prompt for Copilot:**
```
Create Radio.Infrastructure/Audio/Fingerprinting/Data/SqlitePlayHistoryRepository.cs:

Implement IPlayHistoryRepository with:
1. Dependency on FingerprintDbContext
2. Efficient date range queries with proper indexing
3. Statistics aggregation queries

GetStatisticsAsync should return:
- Total play count
- Identified vs unidentified counts
- Group by Source for PlaysBySource
- Top 10 artists and tracks by play count

Use SQL aggregation functions (COUNT, GROUP BY) for efficiency.
```

---

## Phase 3: External API Integration

### Step 3.1: Implement Chromaprint Fingerprint Service

**Prompt for Copilot:**
```
Create Radio.Infrastructure/Audio/Fingerprinting/ChromaprintFingerprintService.cs:

Implement IFingerprintService using Chromaprint.NET:
1. Accept audio samples at any sample rate
2. Resample to Chromaprint's required rate if needed
3. Generate fingerprint hash
4. Return FingerprintData with duration and hash

Key implementation:
```csharp
public async Task<FingerprintData> GenerateFingerprintAsync(
    AudioSampleBuffer samples, 
    CancellationToken ct)
{
    // Chromaprint requires specific sample rate (usually 11025 Hz)
    // Resample if necessary
    
    using var chromaprint = new ChromaprintContext();
    chromaprint.Start(sampleRate, channels);
    chromaprint.Feed(samples.Samples);
    chromaprint.Finish();
    
    var fingerprint = chromaprint.GetFingerprint();
    
    return new FingerprintData
    {
        Id = Guid.NewGuid().ToString(),
        ChromaprintHash = Convert.ToBase64String(fingerprint),
        DurationSeconds = (int)samples.Duration.TotalSeconds,
        GeneratedAt = DateTime.UtcNow
    };
}
```

Handle errors gracefully with meaningful exceptions.
Log fingerprint generation timing for performance monitoring.
```

### Step 3.2: Implement AcoustID Client

**Prompt for Copilot:**
```
Create Radio.Infrastructure/Audio/Fingerprinting/AcoustIdClient.cs:

This HTTP client wrapper should:
1. Implement rate limiting (3 requests/second max)
2. Use IHttpClientFactory for proper connection management
3. Handle API responses and errors
4. Parse JSON responses

API endpoint: POST https://api.acoustid.org/v2/lookup

Request parameters:
- client: Your API key
- fingerprint: The Chromaprint fingerprint
- duration: Audio duration in seconds
- meta: "recordings" (to get MusicBrainz recording IDs)

Response parsing:
```csharp
public record AcoustIdResponse(
    string Status,
    IReadOnlyList<AcoustIdResult> Results);

public record AcoustIdResult(
    string Id,
    double Score,
    IReadOnlyList<AcoustIdRecording>? Recordings);

public record AcoustIdRecording(
    string Id,  // MusicBrainz recording ID
    string? Title,
    IReadOnlyList<AcoustIdArtist>? Artists);
```

Include retry logic with exponential backoff for transient failures.
Use SemaphoreSlim for rate limiting.
```

### Step 3.3: Implement MusicBrainz Client

**Prompt for Copilot:**
```
Create Radio.Infrastructure/Audio/Fingerprinting/MusicBrainzClient.cs:

This HTTP client wrapper should:
1. Implement rate limiting (1 request/second for anonymous)
2. Set proper User-Agent header (required by MusicBrainz)
3. Fetch detailed recording metadata
4. Optionally fetch cover art from Cover Art Archive

User-Agent format: "ApplicationName/Version (contact@email.com)"

API endpoint: GET https://musicbrainz.org/ws/2/recording/{id}?fmt=json&inc=artists+releases

Parse response to TrackMetadata:
- Title from recording.title
- Artist from recording.artist-credit
- Album from recording.releases[0].title
- Release year from recording.releases[0].date

For cover art:
GET https://coverartarchive.org/release/{release-id}/front-250

Include caching headers support (If-None-Match / ETag).
```

### Step 3.4: Implement Metadata Lookup Service (Orchestrator)

**Prompt for Copilot:**
```
Create Radio.Infrastructure/Audio/Fingerprinting/MetadataLookupService.cs:

Implement IMetadataLookupService as the main orchestrator:

```csharp
public class MetadataLookupService : IMetadataLookupService
{
    private readonly IFingerprintCacheRepository _cache;
    private readonly AcoustIdClient _acoustId;
    private readonly MusicBrainzClient _musicBrainz;
    private readonly ITrackMetadataRepository _metadataRepo;
    private readonly ILogger<MetadataLookupService> _logger;
    private readonly FingerprintingOptions _options;

    public async Task<MetadataLookupResult?> LookupAsync(
        FingerprintData fingerprint, 
        CancellationToken ct)
    {
        // Step 1: Check local SQLite cache
        var cached = await _cache.FindByHashAsync(fingerprint.ChromaprintHash, ct);
        if (cached?.Metadata != null)
        {
            await _cache.UpdateLastMatchedAsync(cached.Id, ct);
            return new MetadataLookupResult
            {
                IsMatch = true,
                Confidence = 1.0,
                Metadata = cached.Metadata,
                Source = LookupSource.Cache
            };
        }

        // Step 2: Query AcoustID
        var acoustIdResult = await _acoustId.LookupAsync(
            fingerprint.ChromaprintHash, 
            fingerprint.DurationSeconds, 
            ct);
        
        if (acoustIdResult == null || !acoustIdResult.Results.Any())
        {
            // Store fingerprint for future manual matching
            await _cache.StoreAsync(fingerprint, null, ct);
            return null;
        }

        var bestMatch = acoustIdResult.Results
            .OrderByDescending(r => r.Score)
            .First();

        if (bestMatch.Score < _options.MinimumConfidenceThreshold)
        {
            return null;
        }

        // Step 3: Get MusicBrainz metadata
        var recordingId = bestMatch.Recordings?.FirstOrDefault()?.Id;
        if (recordingId == null) return null;

        var metadata = await _musicBrainz.GetRecordingAsync(recordingId, ct);
        if (metadata == null) return null;

        // Step 4: Cache the result
        metadata = metadata with { FingerprintId = fingerprint.Id };
        await _cache.StoreAsync(fingerprint, metadata, ct);
        await _metadataRepo.StoreAsync(metadata, ct);

        return new MetadataLookupResult
        {
            IsMatch = true,
            Confidence = bestMatch.Score,
            AcoustId = bestMatch.Id,
            MusicBrainzRecordingId = recordingId,
            Metadata = metadata,
            Source = LookupSource.AcoustID
        };
    }
}
```

Include comprehensive logging at each step.
Handle and log all API failures gracefully.
```

---

## Phase 4: Audio Capture Integration

### Step 4.1: Create Audio Sample Provider Interface

**Prompt for Copilot:**
```
Create Radio.Core/Interfaces/Audio/IAudioSampleProvider.cs:

/// <summary>
/// Provides audio samples from various sources for fingerprinting.
/// </summary>
public interface IAudioSampleProvider
{
    /// <summary>
    /// Captures audio samples from the current source.
    /// </summary>
    Task<AudioSampleBuffer?> CaptureAsync(TimeSpan duration, CancellationToken ct);

    /// <summary>
    /// Gets whether the source is currently active and producing audio.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Gets the name of the audio source.
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// Gets the source type for play history recording.
    /// </summary>
    PlaySource SourceType { get; }
}
```

### Step 4.2: Implement SoundFlow Audio Tap

**Prompt for Copilot:**
```
Create Radio.Infrastructure/Audio/Fingerprinting/SoundFlowAudioTap.cs:

This class should:
1. Integrate with the existing SoundFlow audio engine
2. Tap into the audio pipeline without interrupting playback
3. Collect samples into a buffer
4. Resample to the rate required by Chromaprint

Use the existing TappedOutputStream pattern from SoundFlow integration.

Implementation:
- Subscribe to audio data events from the master mixer
- Accumulate samples until requested duration is reached
- Convert stereo to mono if needed
- Resample from playback rate (e.g., 44100 Hz) to Chromaprint rate (11025 Hz)

Reference existing SoundFlow code in:
- src/Radio.Infrastructure/Audio/SoundFlow/TappedOutputStream.cs
- src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowMasterMixer.cs
```

### Step 4.3: Implement Source-Specific Providers

**Prompt for Copilot:**
```
Create the following audio sample providers that implement IAudioSampleProvider:

1. Radio.Infrastructure/Audio/Sources/VinylAudioProvider.cs
   - Captures from vinyl input source
   - Detects silence (needle lifted) to avoid false fingerprints
   - SourceType = PlaySource.Vinyl

2. Radio.Infrastructure/Audio/Sources/RadioStreamAudioProvider.cs
   - Captures from active radio stream
   - Includes station name in SourceDetails
   - SourceType = PlaySource.Radio

3. Radio.Infrastructure/Audio/Sources/FileAudioProvider.cs
   - Reads directly from audio files
   - Uses file path as SourceDetails
   - SourceType = PlaySource.File

Each provider should:
- Use SoundFlowAudioTap for capture
- Handle source-specific edge cases
- Include logging
- Return null if source is inactive
```

---

## Phase 5: Background Identification Service

### Step 5.1: Create Background Service

**Prompt for Copilot:**
```
Create Radio.Infrastructure/Audio/Fingerprinting/BackgroundIdentificationService.cs:

This hosted service should:
1. Implement IHostedService
2. Periodically capture and identify audio
3. Respect duplicate suppression settings
4. Raise events when tracks are identified
5. Record all plays to history (identified or not)

```csharp
public class BackgroundIdentificationService : BackgroundService
{
    private readonly IEnumerable<IAudioSampleProvider> _audioProviders;
    private readonly IFingerprintService _fingerprintService;
    private readonly IMetadataLookupService _lookupService;
    private readonly IPlayHistoryRepository _historyRepo;
    private readonly IOptions<FingerprintingOptions> _options;
    private readonly ILogger<BackgroundIdentificationService> _logger;

    // Track recent identifications for duplicate suppression
    private readonly ConcurrentDictionary<string, DateTime> _recentIdentifications = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Audio fingerprinting is disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await IdentifyCurrentAudioAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during audio identification");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.Value.IdentificationIntervalSeconds), 
                stoppingToken);
        }
    }

    private async Task IdentifyCurrentAudioAsync(CancellationToken ct)
    {
        // Find active audio provider
        var activeProvider = _audioProviders.FirstOrDefault(p => p.IsActive);
        if (activeProvider == null) return;

        // Capture audio
        var samples = await activeProvider.CaptureAsync(
            TimeSpan.FromSeconds(_options.Value.SampleDurationSeconds), 
            ct);
        if (samples == null) return;

        // Generate fingerprint
        var fingerprint = await _fingerprintService.GenerateFingerprintAsync(samples, ct);

        // Lookup metadata
        var result = await _lookupService.LookupAsync(fingerprint, ct);

        // Check duplicate suppression
        if (result?.IsMatch == true && IsDuplicateIdentification(result.Metadata!))
        {
            _logger.LogDebug("Suppressing duplicate identification: {Title}", result.Metadata.Title);
            return;
        }

        // Record to play history
        var historyEntry = new PlayHistoryEntry
        {
            Id = Guid.NewGuid().ToString(),
            TrackMetadataId = result?.Metadata?.Id,
            FingerprintId = fingerprint.Id,
            PlayedAt = DateTime.UtcNow,
            Source = activeProvider.SourceType,
            SourceDetails = activeProvider.SourceName,
            IdentificationConfidence = result?.Confidence,
            WasIdentified = result?.IsMatch ?? false
        };

        await _historyRepo.RecordPlayAsync(historyEntry, ct);

        // Raise event for UI updates
        if (result?.IsMatch == true)
        {
            TrackIdentified?.Invoke(this, new TrackIdentifiedEventArgs(result.Metadata!, result.Confidence));
            MarkAsRecentlyIdentified(result.Metadata!);
        }
    }

    public event EventHandler<TrackIdentifiedEventArgs>? TrackIdentified;
}
```

Include graceful shutdown handling.
Log all identification attempts with timing information.
```

### Step 5.2: Create Event Types

**Prompt for Copilot:**
```
Create Radio.Core/Events/TrackIdentifiedEventArgs.cs:

public class TrackIdentifiedEventArgs : EventArgs
{
    public TrackMetadata Track { get; }
    public double Confidence { get; }
    public DateTime IdentifiedAt { get; }

    public TrackIdentifiedEventArgs(TrackMetadata track, double confidence)
    {
        Track = track;
        Confidence = confidence;
        IdentifiedAt = DateTime.UtcNow;
    }
}

Follow the existing event patterns in Radio.Core/Events/.
```

---

## Phase 6: API Endpoints

### Step 6.1: Create Fingerprinting Controller

**Prompt for Copilot:**
```
Create Radio.API/Controllers/FingerprintController.cs:

[ApiController]
[Route("api/[controller]")]
public class FingerprintController : ControllerBase
{
    // GET /api/fingerprint/status
    // Returns: { enabled, cacheCount, lastIdentification }

    // POST /api/fingerprint/identify
    // Manual identification from uploaded audio file
    // Returns: MetadataLookupResult

    // GET /api/fingerprint/cache
    // List cached fingerprints with pagination
    // Query params: page, pageSize, hasMetadata

    // DELETE /api/fingerprint/cache/{id}
    // Remove a cached fingerprint

    // POST /api/fingerprint/cache/{id}/metadata
    // Manually add/update metadata for an unidentified fingerprint
    // Body: TrackMetadata
}

Follow existing controller patterns in Radio.API/Controllers/.
Include proper error handling and status codes.
Add XML documentation for Swagger.
```

### Step 6.2: Create Play History Controller

**Prompt for Copilot:**
```
Create Radio.API/Controllers/PlayHistoryController.cs:

[ApiController]
[Route("api/[controller]")]
public class PlayHistoryController : ControllerBase
{
    // GET /api/playhistory
    // List recent play history with pagination
    // Query params: page, pageSize, source, startDate, endDate

    // GET /api/playhistory/stats
    // Get play statistics (top artists, tracks, etc.)
    // Query params: startDate, endDate

    // GET /api/playhistory/{id}
    // Get specific history entry

    // DELETE /api/playhistory/{id}
    // Remove a history entry

    // GET /api/playhistory/now-playing
    // Get the currently playing track (if any)
}

Include SignalR hub for real-time "now playing" updates.
```

### Step 6.3: Create SignalR Hub for Real-Time Updates

**Prompt for Copilot:**
```
Create Radio.API/Hubs/NowPlayingHub.cs:

public class NowPlayingHub : Hub
{
    // Clients subscribe to receive real-time track identification updates
    // When BackgroundIdentificationService identifies a track,
    // broadcast to all connected clients

    public async Task SubscribeToUpdates()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "NowPlaying");
    }
}

// Hub extension methods for broadcasting:
public static class NowPlayingHubExtensions
{
    public static async Task BroadcastTrackIdentified(
        this IHubContext<NowPlayingHub> hub,
        TrackMetadata track,
        double confidence)
    {
        await hub.Clients.Group("NowPlaying").SendAsync("TrackIdentified", new
        {
            track.Title,
            track.Artist,
            track.Album,
            track.CoverArtUrl,
            Confidence = confidence,
            IdentifiedAt = DateTime.UtcNow
        });
    }
}

Register the hub in Program.cs.
```

---

## Phase 7: Dependency Injection Setup

### Step 7.1: Create Service Extensions

**Prompt for Copilot:**
```
Create Radio.Infrastructure/DependencyInjection/FingerprintingServiceExtensions.cs:

public static class FingerprintingServiceExtensions
{
    public static IServiceCollection AddFingerprinting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration
        services.Configure<FingerprintingOptions>(
            configuration.GetSection(FingerprintingOptions.SectionName));

        // Register database context
        services.AddSingleton<FingerprintDbContext>();

        // Register repositories
        services.AddScoped<IFingerprintCacheRepository, SqliteFingerprintCacheRepository>();
        services.AddScoped<ITrackMetadataRepository, SqliteTrackMetadataRepository>();
        services.AddScoped<IPlayHistoryRepository, SqlitePlayHistoryRepository>();

        // Register services
        services.AddSingleton<IFingerprintService, ChromaprintFingerprintService>();
        services.AddScoped<IMetadataLookupService, MetadataLookupService>();

        // Register HTTP clients with rate limiting
        services.AddHttpClient<AcoustIdClient>(client =>
        {
            var options = configuration
                .GetSection(FingerprintingOptions.SectionName)
                .Get<FingerprintingOptions>()!;
            client.BaseAddress = new Uri(options.AcoustId.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.AcoustId.TimeoutSeconds);
        });

        services.AddHttpClient<MusicBrainzClient>(client =>
        {
            var options = configuration
                .GetSection(FingerprintingOptions.SectionName)
                .Get<FingerprintingOptions>()!;
            client.BaseAddress = new Uri(options.MusicBrainz.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.MusicBrainz.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"{options.MusicBrainz.ApplicationName}/{options.MusicBrainz.ApplicationVersion} " +
                $"({options.MusicBrainz.ContactEmail})");
        });

        // Register audio providers
        services.AddScoped<IAudioSampleProvider, VinylAudioProvider>();
        services.AddScoped<IAudioSampleProvider, RadioStreamAudioProvider>();
        services.AddScoped<IAudioSampleProvider, FileAudioProvider>();
        services.AddSingleton<SoundFlowAudioTap>();

        // Register background service
        services.AddHostedService<BackgroundIdentificationService>();

        return services;
    }
}

Add to Program.cs:
builder.Services.AddFingerprinting(builder.Configuration);
```

### Step 7.2: Add Configuration to appsettings.json

**Prompt for Copilot:**
```
Add the following configuration section to appsettings.json:

{
  "Fingerprinting": {
    "Enabled": true,
    "SampleDurationSeconds": 15,
    "IdentificationIntervalSeconds": 30,
    "MinimumConfidenceThreshold": 0.5,
    "DuplicateSuppressionMinutes": 5,
    "DatabasePath": "./data/fingerprints.db",
    "AcoustId": {
      "ApiKey": "",
      "BaseUrl": "https://api.acoustid.org/v2",
      "MaxRequestsPerSecond": 3,
      "TimeoutSeconds": 10
    },
    "MusicBrainz": {
      "BaseUrl": "https://musicbrainz.org/ws/2",
      "ApplicationName": "RadioConsole",
      "ApplicationVersion": "1.0.0",
      "ContactEmail": "",
      "MaxRequestsPerSecond": 1,
      "TimeoutSeconds": 10
    }
  }
}

Store the AcoustID API key as a secret using the existing secrets management system.
Add to secrets.json:
{
  "Fingerprinting:AcoustId:ApiKey": "your-api-key-here"
}
```

---

## Phase 8: Testing

### Step 8.1: Unit Tests

**Prompt for Copilot:**
```
Create tests in tests/Radio.Infrastructure.Tests/Audio/Fingerprinting/:

1. MetadataLookupServiceTests.cs
   - Test_LookupAsync_WithCachedFingerprint_ReturnsCachedResult
   - Test_LookupAsync_WithNewFingerprint_QueriesAcoustId
   - Test_LookupAsync_WithLowConfidence_ReturnsNull
   - Test_LookupAsync_CachesResultAfterAcoustIdLookup
   - Test_LookupAsync_WithAcoustIdFailure_ReturnsNull

2. SqliteFingerprintCacheRepositoryTests.cs
   - Test_FindByHashAsync_WithExistingHash_ReturnsFingerprint
   - Test_FindByHashAsync_WithUnknownHash_ReturnsNull
   - Test_StoreAsync_CreatesNewEntry
   - Test_UpdateLastMatchedAsync_UpdatesTimestampAndCount

3. BackgroundIdentificationServiceTests.cs
   - Test_ExecuteAsync_IdentifiesActiveSource
   - Test_ExecuteAsync_SuppressesDuplicates
   - Test_ExecuteAsync_RecordsToHistory

4. AcoustIdClientTests.cs
   - Test_LookupAsync_ParsesResponseCorrectly
   - Test_LookupAsync_RespectsRateLimit
   - Test_LookupAsync_HandlesApiErrors

Use xUnit, Moq, and follow existing test patterns.
Include test fixtures for SQLite in-memory databases.
```

### Step 8.2: Integration Tests

**Prompt for Copilot:**
```
Create tests/Radio.Infrastructure.Tests/Integration/FingerprintingIntegrationTests.cs:

[Collection("Integration")]
public class FingerprintingIntegrationTests : IClassFixture<FingerprintingTestFixture>
{
    // Test full workflow with actual SQLite database
    // Use mock HTTP responses for AcoustID/MusicBrainz
    
    [Fact]
    public async Task FullWorkflow_NewTrack_IdentifiesAndCaches()
    {
        // Arrange: Create test audio samples
        // Act: Generate fingerprint, lookup, store
        // Assert: Verify cache contains entry with metadata
    }

    [Fact]
    public async Task FullWorkflow_CachedTrack_ReturnsCachedResult()
    {
        // Arrange: Pre-populate cache
        // Act: Lookup same fingerprint
        // Assert: No external API calls, returns cached metadata
    }
}

Include test audio files in tests/TestAssets/.
```

---

## File Structure Summary

```
src/
├── Radio.Core/
│   ├── Interfaces/
│   │   └── Audio/
│   │       ├── IFingerprintService.cs
│   │       ├── IMetadataLookupService.cs
│   │       ├── IFingerprintCacheRepository.cs
│   │       ├── ITrackMetadataRepository.cs
│   │       ├── IPlayHistoryRepository.cs
│   │       └── IAudioSampleProvider.cs
│   ├── Models/
│   │   └── Audio/
│   │       ├── FingerprintData.cs
│   │       ├── MetadataLookupResult.cs
│   │       ├── TrackMetadata.cs
│   │       ├── CachedFingerprint.cs
│   │       ├── PlayHistoryEntry.cs
│   │       ├── PlayStatistics.cs
│   │       └── AudioSampleBuffer.cs
│   ├── Configuration/
│   │   └── FingerprintingOptions.cs
│   └── Events/
│       └── TrackIdentifiedEventArgs.cs
├── Radio.Infrastructure/
│   ├── Audio/
│   │   ├── Fingerprinting/
│   │   │   ├── Data/
│   │   │   │   ├── FingerprintDbContext.cs
│   │   │   │   ├── SqliteFingerprintCacheRepository.cs
│   │   │   │   ├── SqliteTrackMetadataRepository.cs
│   │   │   │   └── SqlitePlayHistoryRepository.cs
│   │   │   ├── ChromaprintFingerprintService.cs
│   │   │   ├── MetadataLookupService.cs
│   │   │   ├── AcoustIdClient.cs
│   │   │   ├── MusicBrainzClient.cs
│   │   │   ├── BackgroundIdentificationService.cs
│   │   │   └── SoundFlowAudioTap.cs
│   │   └── Sources/
│   │       ├── VinylAudioProvider.cs
│   │       ├── RadioStreamAudioProvider.cs
│   │       └── FileAudioProvider.cs
│   └── DependencyInjection/
│       └── FingerprintingServiceExtensions.cs
└── Radio.API/
    ├── Controllers/
    │   ├── FingerprintController.cs
    │   └── PlayHistoryController.cs
    └── Hubs/
        └── NowPlayingHub.cs

tests/
├── Radio.Infrastructure.Tests/
│   ├── Audio/
│   │   └── Fingerprinting/
│   │       ├── MetadataLookupServiceTests.cs
│   │       ├── SqliteFingerprintCacheRepositoryTests.cs
│   │       ├── BackgroundIdentificationServiceTests.cs
│   │       └── AcoustIdClientTests.cs
│   └── Integration/
│       └── FingerprintingIntegrationTests.cs
└── TestAssets/
    └── Audio/
        └── test-sample.wav
```

---

## Implementation Order

| Week | Phase | Description |
|------|-------|-------------|
| 1 | Phase 1 | Core interfaces, models, and configuration |
| 2 | Phase 2 | SQLite repositories for caching |
| 3 | Phase 3 | Chromaprint, AcoustID, and MusicBrainz integration |
| 4 | Phase 4 | SoundFlow audio capture integration |
| 5 | Phase 5 | Background identification service |
| 6 | Phase 6 & 7 | API endpoints and dependency injection |
| 7 | Phase 8 | Testing and refinement |

---

## Configuration Checklist

Before deployment:

- [ ] Register for AcoustID API key at https://acoustid.org/new-application
- [ ] Configure MusicBrainz contact email (required for User-Agent)
- [ ] Store AcoustID API key in secrets management
- [ ] Set appropriate rate limits for your usage
- [ ] Configure database path for fingerprint cache
- [ ] Adjust confidence threshold based on testing
- [ ] Set duplicate suppression interval

---

## Setup and Maintenance Guide

### Initial Setup

1. **Add NuGet Packages** (if using real Chromaprint):
   ```bash
   dotnet add package Chromaprint.NET
   ```

2. **Configure Fingerprinting Options** in `appsettings.json`:
   ```json
   {
     "Fingerprinting": {
       "Enabled": true,
       "DatabasePath": "./data/fingerprints.db"
     }
   }
   ```

3. **Register Fingerprinting Services** in `Program.cs`:
   ```csharp
   builder.Services.AddFingerprinting(builder.Configuration);
   ```

4. **Initialize Database**: The database is automatically initialized on first use.

### Database Maintenance

The SQLite database (`fingerprints.db`) contains three tables:

- **FingerprintCache**: Stores audio fingerprints and their hashes
- **TrackMetadata**: Stores track information (title, artist, album, etc.)
- **PlayHistory**: Records each play event with identification results

**Backup**: Regularly backup the database file. The file can be safely copied while the application is running due to SQLite's WAL mode.

**Cleanup**: Unidentified fingerprints accumulate over time. Consider periodic cleanup of old unmatched fingerprints:

```sql
DELETE FROM FingerprintCache 
WHERE Metadata IS NULL 
AND CreatedAt < datetime('now', '-30 days');
```

### API Key Management

**AcoustID API Key**:
- Register at https://acoustid.org/new-application
- Free tier allows 3 requests/second
- Store in user secrets or environment variables:
  ```bash
  dotnet user-secrets set "Fingerprinting:AcoustId:ApiKey" "your-key-here"
  ```

**MusicBrainz**:
- No API key required for anonymous access
- Rate limit: 1 request/second
- Contact email required in User-Agent header

### Monitoring

The BackgroundIdentificationService logs:
- Each identification attempt
- Cache hits and misses
- API call results
- Duplicate suppressions

Monitor log entries with prefix `BackgroundIdentificationService` for:
- `Identified track: {Title} by {Artist}` - successful identification
- `Track not identified` - unknown track stored for manual tagging
- `Suppressing duplicate identification` - duplicate suppression active

### Manual Tagging

For unidentified tracks:
1. Query unmatched fingerprints via API
2. Use the FingerprintController API to add manual metadata
3. Future plays will match against the cached fingerprint

---

## References

- [AcoustID API Documentation](https://acoustid.org/webservice)
- [MusicBrainz API Documentation](https://musicbrainz.org/doc/MusicBrainz_API)
- [Chromaprint](https://acoustid.org/chromaprint)
- [Cover Art Archive](https://coverartarchive.org/)
- [Existing SQLite patterns in RTest](src/Radio.Infrastructure/Configuration/Stores/SqliteConfigurationStore.cs)
