# Code Cleanup and Production Readiness Plan

This document outlines phases and prompts for completing all TODO and placeholder implementations in the Radio Console codebase.

---

## Table of Contents

1. [Overview](#overview)
2. [Phase Summary](#phase-summary)
3. [Phase 1: FileBrowser Database Integration](#phase-1-filebrowser-database-integration)
4. [Phase 2: AudioFileEventSource SoundFlow Integration](#phase-2-audiofileeventsource-soundflow-integration)
5. [Phase 3: FilesController Source Switching](#phase-3-filescontroller-source-switching)
6. [Phase 4: AcoustID API Integration](#phase-4-acoustid-api-integration)
7. [Phase 5: PlayHistoryController Search Optimization](#phase-5-playhistorycontroller-search-optimization)
8. [Phase 6: RadioFactory Device Enumeration](#phase-6-radiofactory-device-enumeration)
9. [Phase 7: TTSFactory Azure Voice API](#phase-7-ttsfactory-azure-voice-api)
10. [Phase 8: FilePlayerAudioSource SoundFlow Playback](#phase-8-fileplayeraudiosource-soundflow-playback)

---

## Overview

The Radio Console project has several areas marked with `TODO` or `In a real implementation` comments that require production-ready implementations. This document provides detailed coding agent prompts for each area.

### Reference Documentation
- `/README.md` - Project overview and status
- `/PLAN.md` - Development plan and phase details
- `/design/AUDIO.md` - Audio system architecture
- `/design/CONFIGURATION.md` - Configuration infrastructure

---

## Phase Summary

| Phase | Area | File | Priority | Complexity | Status |
|-------|------|------|----------|------------|--------|
| 1 | FileBrowser Database | `src/Radio.Infrastructure/Audio/Services/FileBrowser.cs` | Medium | Medium | ✅ Complete |
| 2 | AudioFileEventSource SoundFlow | `src/Radio.Infrastructure/Audio/Sources/Events/AudioFileEventSource.cs` | High | High | 🔲 Pending |
| 3 | FilesController Source Switching | `src/Radio.API/Controllers/FilesController.cs` | Medium | Low | 🔲 Pending |
| 4 | AcoustID API | `src/Radio.Infrastructure/Audio/Fingerprinting/MetadataLookupService.cs` | Low | High | 🔲 Pending |
| 5 | PlayHistory Search | `src/Radio.API/Controllers/PlayHistoryController.cs` | Medium | Medium | 🔲 Pending |
| 6 | RadioFactory Device Enum | `src/Radio.Infrastructure/Audio/Factories/RadioFactory.cs` | Low | Medium | 🔲 Pending |
| 7 | TTSFactory Azure API | `src/Radio.Infrastructure/Audio/Services/TTSFactory.cs` | Low | Medium | 🔲 Pending |
| 8 | FilePlayerAudioSource Playback | `src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs` | High | High | 🔲 Pending |

---

## Phase 1: FileBrowser Database Integration

**File:** `src/Radio.Infrastructure/Audio/Services/FileBrowser.cs`  
**Location:** Near line 70  
**Priority:** Medium  
**Complexity:** Medium

### Current State

```csharp
var audioFiles = new List<AudioFileInfo>();
var previousCount = 0; // TODO: In a real implementation, this would track existing files from a database
```

The FileBrowser currently scans files without tracking which files are new vs. existing.

### Required Implementation

Create a repository for tracking scanned audio files, enabling:
- Detection of new files added since last scan
- Tracking of removed files
- Efficient delta updates instead of full rescans
- Metadata caching for faster subsequent scans

### Coding Agent Prompt

```markdown
## Task: Implement FileBrowser Database Integration

Implement database tracking for the FileBrowser service to detect new, modified, and removed audio files.

### Context
- File: `src/Radio.Infrastructure/Audio/Services/FileBrowser.cs`
- The FileBrowser scans directories for audio files
- Currently has no persistent tracking of scanned files
- Need to track file state for incremental updates

### Requirements

#### 1. Create IAudioFileRepository Interface

Create `src/Radio.Core/Interfaces/Audio/IAudioFileRepository.cs`:

```csharp
namespace Radio.Core.Interfaces.Audio;

/// <summary>
/// Repository for persisting audio file metadata and scan state.
/// </summary>
public interface IAudioFileRepository
{
  /// <summary>
  /// Gets all tracked audio files.
  /// </summary>
  Task<IReadOnlyList<AudioFileInfo>> GetAllAsync(CancellationToken ct = default);
  
  /// <summary>
  /// Gets tracked files in a specific directory.
  /// </summary>
  Task<IReadOnlyList<AudioFileInfo>> GetByDirectoryAsync(
    string directoryPath, 
    bool recursive = false,
    CancellationToken ct = default);
  
  /// <summary>
  /// Gets a single file by path.
  /// </summary>
  Task<AudioFileInfo?> GetByPathAsync(string path, CancellationToken ct = default);
  
  /// <summary>
  /// Adds or updates a file record.
  /// </summary>
  Task UpsertAsync(AudioFileInfo file, CancellationToken ct = default);
  
  /// <summary>
  /// Bulk adds or updates file records.
  /// </summary>
  Task UpsertBatchAsync(IEnumerable<AudioFileInfo> files, CancellationToken ct = default);
  
  /// <summary>
  /// Removes a file record by path.
  /// </summary>
  Task<bool> RemoveAsync(string path, CancellationToken ct = default);
  
  /// <summary>
  /// Removes all files in a directory that are no longer present in the provided list.
  /// </summary>
  Task<int> RemoveStaleAsync(
    string directoryPath, 
    IEnumerable<string> currentPaths,
    CancellationToken ct = default);
  
  /// <summary>
  /// Gets the count of tracked files.
  /// </summary>
  Task<int> GetCountAsync(CancellationToken ct = default);
  
  /// <summary>
  /// Gets files that need metadata update (last modified changed).
  /// </summary>
  Task<IReadOnlyList<AudioFileInfo>> GetStaleMetadataAsync(
    IEnumerable<(string Path, DateTime LastModified)> currentFiles,
    CancellationToken ct = default);
}
```

#### 2. Create SQLite Implementation

Create `src/Radio.Infrastructure/Audio/Repositories/SqliteAudioFileRepository.cs`:

- Use the existing database path configuration from `DatabasePathConfiguration`
- Create a new database file `audiofiles.db` in the configured directory
- Implement WAL mode for concurrent access
- Include indexes on Path and Directory columns

Table schema:
```sql
CREATE TABLE IF NOT EXISTS AudioFiles (
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Path TEXT NOT NULL UNIQUE,
  FileName TEXT NOT NULL,
  Extension TEXT NOT NULL,
  SizeBytes INTEGER NOT NULL,
  CreatedAt TEXT NOT NULL,
  LastModifiedAt TEXT NOT NULL,
  Title TEXT,
  Artist TEXT,
  Album TEXT,
  Duration INTEGER,
  TrackNumber INTEGER,
  Genre TEXT,
  Year INTEGER,
  ScannedAt TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_AudioFiles_Path ON AudioFiles(Path);
CREATE INDEX IF NOT EXISTS IX_AudioFiles_Directory ON AudioFiles(Path);
```

#### 3. Update FileBrowser

Modify `src/Radio.Infrastructure/Audio/Services/FileBrowser.cs`:

1. Inject `IAudioFileRepository` via constructor
2. In `ListFilesAsync`:
   - Get previous count from repository
   - After scanning, compare with existing records
   - Track new files (increment `library.new_tracks_added`)
   - Track removed files (new metric `library.tracks_removed`)
   - Update repository with current state
3. Add new methods:
   - `ScanForChangesAsync()` - returns only new/modified/removed files
   - `GetFileCountAsync()` - returns cached count from database

#### 4. Register Services

Update `src/Radio.Infrastructure/DependencyInjection/AudioServiceExtensions.cs`:
- Register `IAudioFileRepository` with `SqliteAudioFileRepository`

#### 5. Add Unit Tests

Create `tests/Radio.Infrastructure.Tests/Audio/Repositories/SqliteAudioFileRepositoryTests.cs`:
- Test CRUD operations
- Test batch operations
- Test stale file detection

### Success Criteria
- [ ] Repository interface and implementation created
- [ ] FileBrowser tracks new vs existing files
- [ ] Metrics correctly report new/removed files
- [ ] Unit tests pass
- [ ] No breaking changes to existing API
```

---

## Phase 2: AudioFileEventSource SoundFlow Integration

**File:** `src/Radio.Infrastructure/Audio/Sources/Events/AudioFileEventSource.cs`  
**Location:** Lines 72-77 and 98-99  
**Priority:** High  
**Complexity:** High

### Current State

```csharp
// In a full implementation, this would return the SoundFlow node
// For now, return the audio stream or a placeholder object
return _audioStream ?? (object)_filePath;

// In a full implementation, we would create a SoundFlow audio node here
State = AudioSourceState.Ready;
```

The AudioFileEventSource simulates playback with `Task.Delay` instead of actual audio playback.

### Required Implementation

Integrate with SoundFlow to create actual audio playback nodes for event sounds.

### Coding Agent Prompt

```markdown
## Task: Implement AudioFileEventSource SoundFlow Integration

Integrate AudioFileEventSource with SoundFlow for real audio playback.

### Context
- File: `src/Radio.Infrastructure/Audio/Sources/Events/AudioFileEventSource.cs`
- Currently simulates playback with Task.Delay
- Need to use SoundFlow's audio pipeline for actual playback
- Reference: `src/Radio.Infrastructure/Audio/SoundFlow/SoundFlowPlaybackService.cs`

### Requirements

#### 1. Understand SoundFlow Audio Pipeline

Reference existing implementations:
- `SoundFlowPlaybackService.cs` - Shows how to create sound players
- `SoundFlowMasterMixer.cs` - Shows mixer node management
- SoundFlow documentation: https://lsxprime.github.io/soundflow-docs/

Key classes:
- `SoundPlayer` - Plays audio from a data provider
- `ChunkedDataProvider` - Reads audio from file streams
- `Mixer` - Combines multiple audio sources

#### 2. Update AudioFileEventSource

Modify `src/Radio.Infrastructure/Audio/Sources/Events/AudioFileEventSource.cs`:

```csharp
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;

public class AudioFileEventSource : EventAudioSourceBase
{
  private readonly string _filePath;
  private readonly TimeSpan _duration;
  private readonly string _name;
  private Stream? _audioStream;
  private FileStream? _fileStream;
  private ISoundDataProvider? _dataProvider;
  private SoundPlayer? _soundPlayer;
  private CancellationTokenSource? _playbackCts;
  
  // ... existing code ...
  
  /// <inheritdoc/>
  public override object GetSoundComponent()
  {
    // Return the SoundFlow sound player for mixer integration
    return _soundPlayer ?? throw new InvalidOperationException(
      "Audio source not initialized. Call InitializeAsync first.");
  }
  
  /// <inheritdoc/>
  protected override async Task InitializeAsync(CancellationToken cancellationToken = default)
  {
    await base.InitializeAsync(cancellationToken);
    
    try
    {
      // Load the file if not already loaded as a stream
      if (_audioStream == null && !string.IsNullOrEmpty(_filePath))
      {
        if (!File.Exists(_filePath))
        {
          throw new FileNotFoundException($"Audio file not found: {_filePath}");
        }
        
        // Open file stream for SoundFlow
        _fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _audioStream = _fileStream;
      }
      
      // Create SoundFlow data provider
      if (_audioStream != null)
      {
        _dataProvider = new ChunkedDataProvider(_audioStream);
        
        // Create the sound player
        _soundPlayer = new SoundPlayer(_dataProvider);
        _soundPlayer.PlaybackEnded += OnSoundPlayerEnded;
        
        Logger.LogDebug("Created SoundFlow player for: {FilePath}", _filePath);
      }
      
      State = AudioSourceState.Ready;
      Logger.LogInformation("Audio file event source initialized: {Name}", _name);
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to initialize audio file event source");
      State = AudioSourceState.Error;
      throw;
    }
  }
  
  /// <inheritdoc/>
  protected override Task PlayCoreAsync(CancellationToken cancellationToken)
  {
    _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    
    Logger.LogDebug("Playing audio file event: {Name}", _name);
    
    try
    {
      if (_soundPlayer == null)
      {
        throw new InvalidOperationException("Sound player not initialized");
      }
      
      // Reset to beginning if needed
      if (_dataProvider is ChunkedDataProvider chunked && chunked.Position > 0)
      {
        _audioStream?.Seek(0, SeekOrigin.Begin);
      }
      
      // Start SoundFlow playback
      _soundPlayer.Play();
      State = AudioSourceState.Playing;
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error during audio file event playback");
      State = AudioSourceState.Error;
      OnPlaybackCompleted(PlaybackCompletionReason.Error, ex);
    }
    
    return Task.CompletedTask;
  }
  
  private void OnSoundPlayerEnded(object? sender, EventArgs e)
  {
    State = AudioSourceState.Stopped;
    OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);
  }
  
  /// <inheritdoc/>
  protected override Task StopCoreAsync(CancellationToken cancellationToken)
  {
    Logger.LogDebug("Stopping audio file event playback");
    
    _soundPlayer?.Stop();
    _playbackCts?.Cancel();
    
    OnPlaybackCompleted(PlaybackCompletionReason.UserStopped);
    return Task.CompletedTask;
  }
  
  /// <inheritdoc/>
  protected override ValueTask DisposeAsyncCore()
  {
    Logger.LogDebug("Disposing audio file event source");
    
    _playbackCts?.Cancel();
    _playbackCts?.Dispose();
    
    if (_soundPlayer != null)
    {
      _soundPlayer.PlaybackEnded -= OnSoundPlayerEnded;
      _soundPlayer.Dispose();
    }
    
    _dataProvider?.Dispose();
    _audioStream?.Dispose();
    _fileStream?.Dispose();
    
    return ValueTask.CompletedTask;
  }
  
  /// <inheritdoc/>
  protected override void OnVolumeChanged(float volume)
  {
    if (_soundPlayer != null)
    {
      _soundPlayer.Volume = volume;
      Logger.LogDebug("Audio file event volume changed to {Volume}", volume);
    }
  }
}
```

#### 3. Verify Integration with Mixer

Ensure the SoundPlayer can be added to the master mixer:
- `SoundFlowMasterMixer.AddSource()` should handle the sound player component
- Volume/mute controls should apply correctly

#### 4. Add Unit Tests

Create or update `tests/Radio.Infrastructure.Tests/Audio/Sources/Events/AudioFileEventSourceTests.cs`:
- Test initialization creates SoundPlayer
- Test play/stop lifecycle
- Test volume changes apply to SoundPlayer
- Test disposal cleans up resources

### Success Criteria
- [ ] SoundPlayer created during initialization
- [ ] GetSoundComponent returns valid SoundPlayer
- [ ] Playback uses SoundFlow (not Task.Delay simulation)
- [ ] PlaybackEnded event fires correctly
- [ ] Volume control works
- [ ] Proper resource cleanup on dispose
- [ ] Unit tests pass
```

---

## Phase 3: FilesController Source Switching

**File:** `src/Radio.API/Controllers/FilesController.cs`  
**Location:** Lines 229-251  
**Priority:** Medium  
**Complexity:** Low

### Current State

```csharp
/// <remarks>
/// TODO: Implement automatic source switching via IAudioManager.SwitchSourceAsync
/// when the full AudioManager implementation is available (Phase 3).
/// Currently returns null if File Player is not the active source.
/// </remarks>
private async Task<IPrimaryAudioSource?> GetOrActivateFilePlayerSourceAsync(
  CancellationToken cancellationToken)
{
  // ...
  // TODO: When IAudioManager.SwitchSourceAsync is available, activate File Player here
  _logger.LogWarning("File Player source is not currently active...");
  return null;
}
```

### Required Implementation

Implement source switching to automatically activate the File Player when file operations are requested.

### Coding Agent Prompt

```markdown
## Task: Implement FilesController Source Switching

Complete the source switching implementation in FilesController.

### Context
- File: `src/Radio.API/Controllers/FilesController.cs`
- IAudioManager is now available (implemented in Phase 3 of main plan)
- Need to activate File Player source when file operations are requested
- Reference: `src/Radio.API/Controllers/SourcesController.cs` for similar patterns

### Requirements

#### 1. Update FilesController Constructor

Inject IAudioManager (note: using optional injection pattern consistent with existing codebase):

```csharp
private readonly IAudioManager? _audioManager;

/// <summary>
/// Initializes a new instance of the FilesController.
/// </summary>
/// <param name="logger">The logger instance.</param>
/// <param name="audioEngine">The audio engine.</param>
/// <param name="fileBrowser">The file browser service.</param>
/// <param name="queueService">The queue service.</param>
/// <param name="audioManager">
/// Optional audio manager. When null, source switching is not available.
/// This is expected during phased rollout before IAudioManager is fully implemented.
/// </param>
public FilesController(
  ILogger<FilesController> logger,
  IAudioEngine audioEngine,
  IFileBrowser fileBrowser,
  IFilePlayerQueueService queueService,
  IAudioManager? audioManager = null)
{
  _logger = logger;
  _audioEngine = audioEngine;
  _fileBrowser = fileBrowser;
  _queueService = queueService;
  _audioManager = audioManager;
}
```

#### 2. Implement GetOrActivateFilePlayerSourceAsync

```csharp
/// <summary>
/// Gets or activates the File Player audio source.
/// </summary>
/// <remarks>
/// Automatically switches to File Player source if not currently active.
/// </remarks>
private async Task<IPrimaryAudioSource?> GetOrActivateFilePlayerSourceAsync(
  CancellationToken cancellationToken)
{
  // Get the current primary source
  var primarySource = _audioEngine.GetActivePrimarySource();
  
  // If File Player is already active, return it
  if (primarySource?.Type == AudioSourceType.FilePlayer)
  {
    return primarySource;
  }
  
  // Check if AudioManager is available for source switching
  if (_audioManager == null)
  {
    _logger.LogWarning(
      "File Player source is not active and AudioManager is not available for source switching");
    return null;
  }
  
  try
  {
    _logger.LogInformation("Activating File Player source for file operations");
    
    // Get or create the File Player source via AudioManager
    if (_audioManager is Radio.Infrastructure.Audio.Services.AudioManager audioManager)
    {
      var filePlayerSource = await audioManager.GetOrCreateSourceAsync(
        AudioSourceType.FilePlayer, 
        cancellationToken);
      
      // Switch to the File Player source
      await audioManager.SwitchSourceAsync(filePlayerSource, cancellationToken);
      
      _logger.LogInformation("Successfully activated File Player source");
      return filePlayerSource;
    }
    
    _logger.LogWarning("AudioManager implementation does not support source creation");
    return null;
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Failed to activate File Player source");
    return null;
  }
}
```

#### 3. Update Endpoint Documentation

Remove the TODO from the XML documentation:

```csharp
/// <summary>
/// Gets or activates the File Player audio source.
/// </summary>
/// <remarks>
/// Automatically switches to File Player source if not currently active.
/// If AudioManager is not available, returns null when File Player is not the active source.
/// </remarks>
```

#### 4. Add Integration Tests

Create or update `tests/Radio.API.Tests/Controllers/FilesControllerTests.cs`:
- Test that file operations activate File Player when needed
- Test fallback behavior when AudioManager is not available
- Test that already-active File Player is returned directly

### Success Criteria
- [ ] AudioManager injected into FilesController
- [ ] Source switching implemented
- [ ] TODO comments removed
- [ ] Fallback behavior preserved when AudioManager unavailable
- [ ] Integration tests pass
```

---

## Phase 4: AcoustID API Integration

**File:** `src/Radio.Infrastructure/Audio/Fingerprinting/MetadataLookupService.cs`  
**Location:** Lines 65-80  
**Priority:** Low  
**Complexity:** High

### Current State

```csharp
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
```

### Required Implementation

Implement actual AcoustID API integration for audio fingerprint lookups.

### Coding Agent Prompt

```markdown
## Task: Implement AcoustID API Integration

Implement the AcoustID web service integration for audio fingerprint lookups.

### Context
- File: `src/Radio.Infrastructure/Audio/Fingerprinting/MetadataLookupService.cs`
- AcoustID API: https://acoustid.org/webservice
- Requires API key (stored in secrets)
- Returns MusicBrainz recording IDs

### Requirements

#### 1. Create AcoustID Client

Create `src/Radio.Infrastructure/Audio/Fingerprinting/AcoustIdClient.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.Audio.Fingerprinting;

/// <summary>
/// Client for the AcoustID fingerprint lookup service.
/// </summary>
public sealed class AcoustIdClient : IDisposable
{
  private const string BaseUrl = "https://api.acoustid.org/v2/lookup";
  
  private readonly HttpClient _httpClient;
  private readonly ILogger<AcoustIdClient> _logger;
  private readonly string _apiKey;
  
  public AcoustIdClient(
    HttpClient httpClient,
    ILogger<AcoustIdClient> logger,
    IOptions<FingerprintingOptions> options)
  {
    _httpClient = httpClient;
    _logger = logger;
    _apiKey = options.Value.AcoustId.ApiKey;
  }
  
  /// <summary>
  /// Looks up a fingerprint in the AcoustID database.
  /// </summary>
  /// <param name="fingerprint">The chromaprint fingerprint string.</param>
  /// <param name="duration">The audio duration in seconds.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>The lookup result, or null if no match found.</returns>
  public async Task<AcoustIdLookupResult?> LookupAsync(
    string fingerprint, 
    int duration,
    CancellationToken ct = default)
  {
    if (string.IsNullOrEmpty(_apiKey))
    {
      _logger.LogWarning("AcoustID API key not configured");
      return null;
    }
    
    try
    {
      // Use POST for fingerprint data because fingerprints can be very long
      // (potentially thousands of characters) and may exceed URL length limits
      // FormUrlEncodedContent handles the encoding for all parameters
      var queryParams = new Dictionary<string, string>
      {
        ["client"] = _apiKey,
        ["meta"] = "recordings+releasegroups+compress",
        ["duration"] = duration.ToString(),
        ["fingerprint"] = fingerprint
      };
      
      var content = new FormUrlEncodedContent(queryParams);
      var httpResponse = await _httpClient.PostAsync(BaseUrl, content, ct);
      httpResponse.EnsureSuccessStatusCode();
      
      var response = await httpResponse.Content.ReadFromJsonAsync<AcoustIdResponse>(ct);
      
      if (response?.Status != "ok" || response.Results == null)
      {
        _logger.LogDebug("AcoustID lookup returned no results");
        return null;
      }
      
      // Return the best match (highest score)
      var bestResult = response.Results
        .Where(r => r.Score >= 0.5) // Minimum 50% confidence
        .OrderByDescending(r => r.Score)
        .FirstOrDefault();
      
      if (bestResult == null)
      {
        _logger.LogDebug("No AcoustID results with sufficient confidence");
        return null;
      }
      
      return new AcoustIdLookupResult
      {
        Id = bestResult.Id,
        Score = bestResult.Score,
        Recordings = bestResult.Recordings?.Select(r => new AcoustIdRecording
        {
          Id = r.Id,
          Title = r.Title,
          Artists = r.Artists?.Select(a => a.Name).ToList(),
          ReleaseGroups = r.ReleaseGroups?.Select(rg => new AcoustIdReleaseGroup
          {
            Id = rg.Id,
            Title = rg.Title,
            Type = rg.Type
          }).ToList()
        }).ToList()
      };
    }
    catch (HttpRequestException ex)
    {
      _logger.LogError(ex, "AcoustID API request failed");
      return null;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing AcoustID response");
      return null;
    }
  }
  
  public void Dispose()
  {
    _httpClient.Dispose();
  }
}

// Response DTOs
public class AcoustIdResponse
{
  [JsonPropertyName("status")]
  public string? Status { get; set; }
  
  [JsonPropertyName("results")]
  public List<AcoustIdResult>? Results { get; set; }
}

public class AcoustIdResult
{
  [JsonPropertyName("id")]
  public string? Id { get; set; }
  
  [JsonPropertyName("score")]
  public double Score { get; set; }
  
  [JsonPropertyName("recordings")]
  public List<AcoustIdApiRecording>? Recordings { get; set; }
}

public class AcoustIdApiRecording
{
  [JsonPropertyName("id")]
  public string? Id { get; set; }
  
  [JsonPropertyName("title")]
  public string? Title { get; set; }
  
  [JsonPropertyName("artists")]
  public List<AcoustIdArtist>? Artists { get; set; }
  
  [JsonPropertyName("releasegroups")]
  public List<AcoustIdApiReleaseGroup>? ReleaseGroups { get; set; }
}

public class AcoustIdArtist
{
  [JsonPropertyName("id")]
  public string? Id { get; set; }
  
  [JsonPropertyName("name")]
  public string? Name { get; set; }
}

public class AcoustIdApiReleaseGroup
{
  [JsonPropertyName("id")]
  public string? Id { get; set; }
  
  [JsonPropertyName("title")]
  public string? Title { get; set; }
  
  [JsonPropertyName("type")]
  public string? Type { get; set; }
}

// Result models
public class AcoustIdLookupResult
{
  public string? Id { get; set; }
  public double Score { get; set; }
  public List<AcoustIdRecording>? Recordings { get; set; }
}

public class AcoustIdRecording
{
  public string? Id { get; set; }
  public string? Title { get; set; }
  public List<string>? Artists { get; set; }
  public List<AcoustIdReleaseGroup>? ReleaseGroups { get; set; }
}

public class AcoustIdReleaseGroup
{
  public string? Id { get; set; }
  public string? Title { get; set; }
  public string? Type { get; set; }
}
```

#### 2. Update MetadataLookupService

Modify `src/Radio.Infrastructure/Audio/Fingerprinting/MetadataLookupService.cs`:

```csharp
private readonly AcoustIdClient _acoustIdClient;

public MetadataLookupService(
  ILogger<MetadataLookupService> logger,
  IFingerprintCacheRepository cache,
  ITrackMetadataRepository metadataRepo,
  IOptions<FingerprintingOptions> options,
  AcoustIdClient acoustIdClient)
{
  _logger = logger;
  _cache = cache;
  _metadataRepo = metadataRepo;
  _options = options.Value;
  _acoustIdClient = acoustIdClient;
}

public async Task<MetadataLookupResult?> LookupAsync(
  FingerprintData fingerprint,
  CancellationToken ct = default)
{
  ArgumentNullException.ThrowIfNull(fingerprint);
  
  _logger.LogDebug("Looking up metadata for fingerprint {Id}", fingerprint.Id);
  
  // Step 1: Check local cache
  var cached = await _cache.FindByHashAsync(fingerprint.ChromaprintHash, ct);
  if (cached?.Metadata != null)
  {
    await _cache.UpdateLastMatchedAsync(cached.Id, ct);
    _logger.LogDebug("Found cached metadata: {Title} by {Artist}",
      cached.Metadata.Title, cached.Metadata.Artist);
    
    return new MetadataLookupResult
    {
      IsMatch = true,
      Confidence = 1.0,
      Metadata = cached.Metadata,
      Source = LookupSource.Cache
    };
  }
  
  // Step 2: Query AcoustID if API key is configured
  if (string.IsNullOrEmpty(_options.AcoustId.ApiKey))
  {
    _logger.LogDebug("No AcoustID API key configured, storing for manual tagging");
    await _cache.StoreAsync(fingerprint, null, ct);
    return null;
  }
  
  var acoustIdResult = await _acoustIdClient.LookupAsync(
    fingerprint.ChromaprintHash,
    (int)fingerprint.Duration.TotalSeconds,
    ct);
  
  if (acoustIdResult?.Recordings?.Count > 0)
  {
    var recording = acoustIdResult.Recordings[0];
    var metadata = new TrackMetadata
    {
      Title = recording.Title ?? "Unknown",
      Artist = recording.Artists?.FirstOrDefault() ?? "Unknown Artist",
      Album = recording.ReleaseGroups?.FirstOrDefault()?.Title,
      MusicBrainzRecordingId = recording.Id
    };
    
    // Cache the result
    await _cache.StoreAsync(fingerprint, metadata, ct);
    
    _logger.LogInformation(
      "AcoustID match found: {Title} by {Artist} (confidence: {Score:P0})",
      metadata.Title, metadata.Artist, acoustIdResult.Score);
    
    return new MetadataLookupResult
    {
      IsMatch = true,
      Confidence = acoustIdResult.Score,
      Metadata = metadata,
      Source = LookupSource.AcoustId
    };
  }
  
  // No match found, store fingerprint for manual tagging
  _logger.LogDebug("No AcoustID match, storing fingerprint for manual tagging");
  await _cache.StoreAsync(fingerprint, null, ct);
  return null;
}
```

#### 3. Register Services

Update DI registration:

```csharp
services.AddHttpClient<AcoustIdClient>();
services.AddTransient<AcoustIdClient>();
```

#### 4. Add Unit Tests

Create `tests/Radio.Infrastructure.Tests/Audio/Fingerprinting/AcoustIdClientTests.cs`:
- Test successful lookup
- Test no match scenario
- Test API error handling
- Test missing API key behavior

### Success Criteria
- [ ] AcoustIdClient implemented
- [ ] MetadataLookupService uses AcoustID
- [ ] Proper error handling for API failures
- [ ] Results cached to avoid repeated lookups
- [ ] Unit tests pass
```

---

## Phase 5: PlayHistoryController Search Optimization

**File:** `src/Radio.API/Controllers/PlayHistoryController.cs`  
**Location:** Lines 252-262  
**Priority:** Medium  
**Complexity:** Medium

### Current State

```csharp
// Since repository doesn't have a specific search method exposed in interface yet,
// we'll fetch recent history and filter in memory
// Note: In a real implementation this should be pushed to the repository
var allEntries = await _playHistoryRepository.GetRecentAsync(1000);

var query = allEntries.Where(e => 
  (e.Track?.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
  (e.Track?.Artist?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
  (e.Track?.Album?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
```

### Required Implementation

Add proper search methods to the repository interface and push search logic to the database layer.

### Coding Agent Prompt

```markdown
## Task: Implement PlayHistoryController Search Optimization

Move search logic from in-memory filtering to the repository layer.

### Context
- File: `src/Radio.API/Controllers/PlayHistoryController.cs`
- Current implementation fetches 1000 records and filters in memory
- Need to add search method to IPlayHistoryRepository
- Need efficient SQL-based search with pagination

### Requirements

#### 1. Update IPlayHistoryRepository Interface

Add to `src/Radio.Core/Interfaces/Audio/IPlayHistoryRepository.cs`:

```csharp
/// <summary>
/// Searches play history entries by title, artist, or album.
/// </summary>
/// <param name="searchTerm">The search term to match against title, artist, or album.</param>
/// <param name="limit">Maximum number of results to return.</param>
/// <param name="offset">Number of results to skip for pagination.</param>
/// <param name="ct">Cancellation token.</param>
/// <returns>Paginated search results.</returns>
Task<(IReadOnlyList<PlayHistoryEntry> Items, int TotalCount)> SearchAsync(
  string searchTerm,
  int? limit = null,
  int? offset = null,
  CancellationToken ct = default);
```

#### 2. Implement in SqlitePlayHistoryRepository

Update `src/Radio.Infrastructure/Audio/Repositories/SqlitePlayHistoryRepository.cs`:

```csharp
public async Task<(IReadOnlyList<PlayHistoryEntry> Items, int TotalCount)> SearchAsync(
  string searchTerm,
  int? limit = null,
  int? offset = null,
  CancellationToken ct = default)
{
  ArgumentException.ThrowIfNullOrWhiteSpace(searchTerm);
  
  await using var connection = new SqliteConnection(_connectionString);
  await connection.OpenAsync(ct);
  
  // Use LIKE for case-insensitive search with wildcards
  var searchPattern = $"%{searchTerm}%";
  
  // Get total count
  var countQuery = @"
    SELECT COUNT(*) FROM PlayHistory ph
    LEFT JOIN TrackMetadata tm ON ph.TrackMetadataId = tm.Id
    WHERE tm.Title LIKE @search COLLATE NOCASE
       OR tm.Artist LIKE @search COLLATE NOCASE
       OR tm.Album LIKE @search COLLATE NOCASE";
  
  await using var countCommand = new SqliteCommand(countQuery, connection);
  countCommand.Parameters.AddWithValue("@search", searchPattern);
  var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(ct));
  
  // Get paginated results
  // Note: Parameterized LIMIT/OFFSET requires SQLite 3.8.0+ (2014)
  // If using older SQLite, validate inputs are non-negative integers
  // and use string formatting instead
  var query = @"
    SELECT ph.*, tm.*
    FROM PlayHistory ph
    LEFT JOIN TrackMetadata tm ON ph.TrackMetadataId = tm.Id
    WHERE tm.Title LIKE @search COLLATE NOCASE
       OR tm.Artist LIKE @search COLLATE NOCASE
       OR tm.Album LIKE @search COLLATE NOCASE
    ORDER BY ph.PlayedAt DESC
    LIMIT @limit OFFSET @offset";
  
  var entries = new List<PlayHistoryEntry>();
  
  await using var command = new SqliteCommand(query, connection);
  command.Parameters.AddWithValue("@search", searchPattern);
  // For SQLite 3.8.0+, LIMIT -1 means no limit
  // For older versions, use a large number like 999999999
  command.Parameters.AddWithValue("@limit", limit ?? -1);
  command.Parameters.AddWithValue("@offset", offset ?? 0);
  
  await using var reader = await command.ExecuteReaderAsync(ct);
  while (await reader.ReadAsync(ct))
  {
    entries.Add(MapFromReader(reader));
  }
  
  return (entries, totalCount);
}
```

#### 3. Update PlayHistoryController

Update `src/Radio.API/Controllers/PlayHistoryController.cs`:

```csharp
[HttpGet("search")]
[ProducesResponseType(typeof(PlayHistoryListDto), StatusCodes.Status200OK)]
public async Task<ActionResult<PlayHistoryListDto>> Search(
  [FromQuery] string q,
  [FromQuery] int? limit = null,
  [FromQuery] int? offset = null)
{
  try
  {
    if (string.IsNullOrWhiteSpace(q))
    {
      return BadRequest(new { error = "Search query 'q' is required" });
    }
    
    var (entries, totalCount) = await _playHistoryRepository.SearchAsync(
      q, limit, offset);
    
    return Ok(new PlayHistoryListDto
    {
      Items = entries.Select(MapToDto).ToList(),
      TotalCount = totalCount
    });
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Error searching play history");
    return StatusCode(500, new { error = "Failed to search history" });
  }
}
```

#### 4. Add Full-Text Search Index (Optional Enhancement)

For better performance with large datasets, add FTS5:

```sql
-- Create FTS table
CREATE VIRTUAL TABLE IF NOT EXISTS PlayHistoryFts USING fts5(
  title, artist, album, content='TrackMetadata', content_rowid='Id'
);

-- Populate from existing data
INSERT INTO PlayHistoryFts(rowid, title, artist, album)
  SELECT Id, Title, Artist, Album FROM TrackMetadata;

-- Create triggers to keep FTS in sync
CREATE TRIGGER IF NOT EXISTS TrackMetadata_ai AFTER INSERT ON TrackMetadata BEGIN
  INSERT INTO PlayHistoryFts(rowid, title, artist, album) 
    VALUES (new.Id, new.Title, new.Artist, new.Album);
END;
```

#### 5. Add Unit Tests

Create `tests/Radio.Infrastructure.Tests/Audio/Repositories/PlayHistoryRepositorySearchTests.cs`:
- Test search by title
- Test search by artist
- Test search by album
- Test pagination
- Test empty results
- Test case insensitivity

### Success Criteria
- [ ] Search method added to repository interface
- [ ] SQL-based search implementation
- [ ] Controller uses repository search method
- [ ] Pagination works correctly
- [ ] Performance acceptable for large datasets
- [ ] Unit tests pass
```

---

## Phase 6: RadioFactory Device Enumeration

**File:** `src/Radio.Infrastructure/Audio/Factories/RadioFactory.cs`  
**Location:** Lines 206-224  
**Priority:** Low  
**Complexity:** Medium

### Current State

```csharp
/// <summary>
/// Checks if RTL-SDR devices are available.
/// </summary>
private bool IsRTLSDRAvailable()
{
  try
  {
    // Try to enumerate RTL-SDR devices without creating a receiver
    // For now, we'll just check if we can create one - in a real implementation
    // we'd use a device enumeration API
    var receiver = RadioReceiver.CreateWithFirstAvailableDevice();
    if (receiver != null)
    {
      receiver.Dispose();
      return true;
    }
    return false;
  }
```

### Required Implementation

Use proper RTL-SDR device enumeration API instead of creating/disposing receivers.

### Coding Agent Prompt

```markdown
## Task: Implement RTL-SDR Device Enumeration

Use the RTL-SDR device enumeration API for proper device detection.

### Context
- File: `src/Radio.Infrastructure/Audio/Factories/RadioFactory.cs`
- Using RTLSDRCore library
- Current implementation creates a receiver just to check availability
- Need efficient device enumeration without creating receivers

### Requirements

#### 1. Research RTLSDRCore API

The RTLSDRCore library should provide device enumeration. Check for:
- `RadioReceiver.GetDeviceCount()` or similar
- `RadioReceiver.EnumerateDevices()` or similar
- Device info retrieval without opening the device

#### 2. Update IsRTLSDRAvailable

If RTLSDRCore provides enumeration API:

```csharp
/// <summary>
/// Checks if RTL-SDR devices are available.
/// </summary>
private bool IsRTLSDRAvailable()
{
  try
  {
    // Use RTL-SDR device enumeration API
    var deviceCount = RadioReceiver.GetDeviceCount();
    
    if (deviceCount > 0)
    {
      _logger.LogDebug("Found {Count} RTL-SDR device(s)", deviceCount);
      return true;
    }
    
    _logger.LogDebug("No RTL-SDR devices found");
    return false;
  }
  catch (Exception ex)
  {
    _logger.LogDebug(ex, "Error checking RTL-SDR availability");
    return false;
  }
}
```

If enumeration API is not available, implement caching:

```csharp
private DateTime _lastRtlSdrCheck = DateTime.MinValue;
private bool _rtlSdrAvailable = false;
private readonly TimeSpan _deviceCheckInterval = TimeSpan.FromSeconds(30);
private readonly object _rtlSdrCheckLock = new();

/// <summary>
/// Checks if RTL-SDR devices are available.
/// Uses caching to avoid repeatedly creating/disposing receivers.
/// </summary>
private bool IsRTLSDRAvailable()
{
  lock (_rtlSdrCheckLock)
  {
    // Use cached result if recent
    if (DateTime.UtcNow - _lastRtlSdrCheck < _deviceCheckInterval)
    {
      return _rtlSdrAvailable;
    }
    
    try
    {
      // Create receiver to check availability
      using var receiver = RadioReceiver.CreateWithFirstAvailableDevice();
      _rtlSdrAvailable = receiver != null;
      _lastRtlSdrCheck = DateTime.UtcNow;
      
      _logger.LogDebug("RTL-SDR availability check: {Available}", _rtlSdrAvailable);
      return _rtlSdrAvailable;
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error checking RTL-SDR availability");
      _rtlSdrAvailable = false;
      _lastRtlSdrCheck = DateTime.UtcNow;
      return false;
    }
  }
}
```

#### 3. Add Device Info Method

Add method to get detailed device information:

```csharp
/// <summary>
/// Gets detailed information about available RTL-SDR devices.
/// </summary>
/// <returns>List of device information.</returns>
public IReadOnlyList<RadioDeviceInfo> GetRTLSDRDevices()
{
  var devices = new List<RadioDeviceInfo>();
  
  try
  {
    var count = RadioReceiver.GetDeviceCount();
    
    for (var i = 0; i < count; i++)
    {
      var name = RadioReceiver.GetDeviceName(i);
      var serial = RadioReceiver.GetDeviceSerial(i);
      
      devices.Add(new RadioDeviceInfo
      {
        Index = i,
        Name = name,
        Serial = serial,
        DeviceType = DeviceTypes.RTLSDRCore
      });
    }
  }
  catch (Exception ex)
  {
    _logger.LogWarning(ex, "Error enumerating RTL-SDR devices");
  }
  
  return devices;
}

public record RadioDeviceInfo
{
  public int Index { get; init; }
  public string? Name { get; init; }
  public string? Serial { get; init; }
  public string DeviceType { get; init; } = "";
}
```

#### 4. Add Unit Tests

Create `tests/Radio.Infrastructure.Tests/Audio/Factories/RadioFactoryTests.cs`:
- Test device enumeration
- Test caching behavior (if implemented)
- Test GetRTLSDRDevices method

### Success Criteria
- [ ] Device enumeration uses proper API (if available)
- [ ] Caching prevents repeated device creation
- [ ] Device info accessible without opening device
- [ ] Unit tests pass
```

---

## Phase 7: TTSFactory Azure Voice API

**File:** `src/Radio.Infrastructure/Audio/Services/TTSFactory.cs`  
**Location:** Lines 520-534  
**Priority:** Low  
**Complexity:** Medium

### Current State

```csharp
private Task<IReadOnlyList<TTSVoiceInfo>> GetAzureVoicesAsync(CancellationToken cancellationToken)
{
  // In a full implementation, this would call the Azure Speech API
  // For now, return some common Azure TTS voice identifiers
  var voices = new List<TTSVoiceInfo>
  {
    new() { Id = "en-US-JennyNeural", Name = "Jenny (US)", ... },
    // ...
  };
  
  return Task.FromResult<IReadOnlyList<TTSVoiceInfo>>(voices.AsReadOnly());
}
```

### Required Implementation

Query the Azure Speech API to get the actual list of available voices.

### Coding Agent Prompt

```markdown
## Task: Implement Azure Voice API Integration

Query Azure Speech Service for available TTS voices.

### Context
- File: `src/Radio.Infrastructure/Audio/Services/TTSFactory.cs`
- Azure Speech API: https://docs.microsoft.com/azure/cognitive-services/speech-service/
- Requires subscription key and region from secrets
- Current implementation returns hardcoded voice list

### Requirements

#### 1. Create Azure Speech Client

The Azure Speech SDK provides voice listing functionality:

```csharp
using Microsoft.CognitiveServices.Speech;

private async Task<IReadOnlyList<TTSVoiceInfo>> GetAzureVoicesAsync(
  CancellationToken cancellationToken)
{
  var apiKey = await _secretsProvider.GetSecretAsync("azure:speech:key", cancellationToken);
  var region = await _secretsProvider.GetSecretAsync("azure:speech:region", cancellationToken);
  
  if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(region))
  {
    _logger.LogDebug("Azure Speech credentials not configured, returning default voices");
    return GetDefaultAzureVoices();
  }
  
  try
  {
    var config = SpeechConfig.FromSubscription(apiKey, region);
    using var synthesizer = new SpeechSynthesizer(config);
    
    var result = await synthesizer.GetVoicesAsync();
    
    if (result.Reason == ResultReason.VoicesListRetrieved)
    {
      var voices = result.Voices
        .Where(v => v.Locale.StartsWith("en-")) // Filter to English voices
        .Select(v => new TTSVoiceInfo
        {
          Id = v.ShortName,
          Name = v.LocalName,
          Language = v.Locale,
          Gender = v.Gender == SynthesisVoiceGender.Male 
            ? TTSVoiceGender.Male 
            : TTSVoiceGender.Female
        })
        .ToList();
      
      _logger.LogInformation("Retrieved {Count} Azure voices", voices.Count);
      return voices.AsReadOnly();
    }
    
    _logger.LogWarning("Failed to retrieve Azure voices: {Reason}", result.Reason);
    return GetDefaultAzureVoices();
  }
  catch (Exception ex)
  {
    _logger.LogError(ex, "Error retrieving Azure voices");
    return GetDefaultAzureVoices();
  }
}

private static IReadOnlyList<TTSVoiceInfo> GetDefaultAzureVoices()
{
  return new List<TTSVoiceInfo>
  {
    new() { Id = "en-US-JennyNeural", Name = "Jenny (US)", Language = "en-US", Gender = TTSVoiceGender.Female },
    new() { Id = "en-US-GuyNeural", Name = "Guy (US)", Language = "en-US", Gender = TTSVoiceGender.Male },
    new() { Id = "en-US-AriaNeural", Name = "Aria (US)", Language = "en-US", Gender = TTSVoiceGender.Female },
    new() { Id = "en-GB-SoniaNeural", Name = "Sonia (UK)", Language = "en-GB", Gender = TTSVoiceGender.Female },
    new() { Id = "en-GB-RyanNeural", Name = "Ryan (UK)", Language = "en-GB", Gender = TTSVoiceGender.Male }
  }.AsReadOnly();
}
```

#### 2. Add Caching

Cache the voice list to avoid repeated API calls:

```csharp
private IReadOnlyList<TTSVoiceInfo>? _cachedAzureVoices;
private DateTime _azureVoicesCacheExpiry = DateTime.MinValue;
private readonly TimeSpan _voiceCacheDuration = TimeSpan.FromHours(24);

private async Task<IReadOnlyList<TTSVoiceInfo>> GetAzureVoicesAsync(
  CancellationToken cancellationToken)
{
  // Return cached voices if still valid
  if (_cachedAzureVoices != null && DateTime.UtcNow < _azureVoicesCacheExpiry)
  {
    return _cachedAzureVoices;
  }
  
  // ... fetch from API ...
  
  _cachedAzureVoices = voices;
  _azureVoicesCacheExpiry = DateTime.UtcNow.Add(_voiceCacheDuration);
  
  return voices;
}
```

#### 3. Add Azure Speech SDK Package

If not already present, add to `src/Radio.Infrastructure/Radio.Infrastructure.csproj`:

```xml
<PackageReference Include="Microsoft.CognitiveServices.Speech" Version="1.*" />
```

#### 4. Add Unit Tests

Create or update `tests/Radio.Infrastructure.Tests/Audio/Services/TTSFactoryAzureTests.cs`:
- Test voice retrieval with valid credentials
- Test fallback to defaults without credentials
- Test caching behavior
- Test error handling

### Success Criteria
- [ ] Azure Speech SDK integrated
- [ ] Real voice list retrieved from API
- [ ] Graceful fallback when credentials unavailable
- [ ] Voice list cached appropriately
- [ ] Unit tests pass
```

---

## Phase 8: FilePlayerAudioSource SoundFlow Playback

**File:** `src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs`  
**Location:** Lines 532-544  
**Priority:** High  
**Complexity:** High

### Current State

```csharp
protected override Task PlayCoreAsync(CancellationToken cancellationToken)
{
  if (_currentFile == null)
  {
    throw new InvalidOperationException("No file loaded");
  }

  // In a real implementation, this would start SoundFlow playback
  // When playback completes naturally (not skipped), call: OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent)
  // This will automatically track the audio.songs_played_total metric
  Logger.LogInformation("Playing file: {File}", _currentFile);

  return Task.CompletedTask;
}
```

### Required Implementation

Integrate with SoundFlow to actually play audio files through the audio pipeline.

### Coding Agent Prompt

```markdown
## Task: Implement FilePlayerAudioSource SoundFlow Playback

Complete the SoundFlow integration for actual audio playback in FilePlayerAudioSource.

### Context
- File: `src/Radio.Infrastructure/Audio/Sources/Primary/FilePlayerAudioSource.cs`
- Has ChunkedDataProvider (_dataProvider) already created during file loading
- Need to integrate with SoundFlowPlaybackService or SoundFlowMasterMixer
- Reference implementations:
  - `SoundFlowPlaybackService.cs` - Shows file playback pattern
  - `SoundFlowMasterMixer.cs` - Shows mixer integration

### Requirements

#### 1. Inject SoundFlow Playback Service

Update constructor to accept playback service:

```csharp
private readonly SoundFlowPlaybackService? _playbackService;

public FilePlayerAudioSource(
  ILogger<FilePlayerAudioSource> logger,
  IOptionsMonitor<FilePlayerOptions> options,
  IOptionsMonitor<FilePlayerPreferences> preferences,
  string rootDir = "",
  BackgroundIdentificationService? identificationService = null,
  IMetricsCollector? metricsCollector = null,
  SoundFlowPlaybackService? playbackService = null)
  : base(logger, metricsCollector)
{
  // ... existing initialization ...
  _playbackService = playbackService;
}
```

#### 2. Implement PlayCoreAsync

```csharp
private string? _playbackId;
private bool _isPlaybackActive = false;

/// <inheritdoc/>
protected override async Task PlayCoreAsync(CancellationToken cancellationToken)
{
  if (_currentFile == null)
  {
    throw new InvalidOperationException("No file loaded");
  }
  
  Logger.LogInformation("Starting playback: {File}", _currentFile);
  
  if (_playbackService != null)
  {
    // Generate unique ID for this playback session
    _playbackId = $"fileplayer-{Guid.NewGuid():N}";
    
    // Subscribe to playback events
    _playbackService.PlaybackEnded += OnPlaybackServiceEnded;
    _playbackService.PositionChanged += OnPlaybackServicePosition;
    
    // Start actual audio playback
    var success = await _playbackService.PlayFileAsync(
      _playbackId,
      _currentFile,
      Volume,
      cancellationToken);
    
    if (!success)
    {
      Logger.LogError("Failed to start SoundFlow playback for {File}", _currentFile);
      throw new InvalidOperationException("Failed to start audio playback");
    }
    
    // Seek to saved position if resuming
    if (_position > TimeSpan.Zero)
    {
      await _playbackService.SeekAsync(_playbackId, _position, cancellationToken);
    }
    
    _isPlaybackActive = true;
    Logger.LogDebug("SoundFlow playback started for {File}", _currentFile);
  }
  else
  {
    // Fallback: No playback service available
    Logger.LogWarning("No playback service available, playback simulation only");
  }
}

private void OnPlaybackServiceEnded(object? sender, PlaybackEndedEventArgs e)
{
  if (e.PlaybackId != _playbackId)
    return;
  
  _isPlaybackActive = false;
  State = AudioSourceState.Stopped;
  
  if (e.CompletedNaturally)
  {
    // Track completed song metric
    OnPlaybackCompleted(PlaybackCompletionReason.EndOfContent);
    
    // Auto-advance to next track
    _ = NextAsync();
  }
}

private void OnPlaybackServicePosition(object? sender, PositionChangedEventArgs e)
{
  if (e.PlaybackId != _playbackId)
    return;
  
  _position = e.Position;
}
```

#### 3. Implement PauseCoreAsync

```csharp
protected override async Task PauseCoreAsync(CancellationToken cancellationToken)
{
  Logger.LogDebug("Pausing file playback at {Position}", _position);
  
  if (_playbackService != null && _playbackId != null && _isPlaybackActive)
  {
    await _playbackService.PauseAsync(_playbackId, cancellationToken);
  }
}
```

#### 4. Implement ResumeCoreAsync

```csharp
protected override async Task ResumeCoreAsync(CancellationToken cancellationToken)
{
  Logger.LogDebug("Resuming file playback from {Position}", _position);
  
  if (_playbackService != null && _playbackId != null)
  {
    await _playbackService.ResumeAsync(_playbackId, cancellationToken);
    _isPlaybackActive = true;
  }
}
```

#### 5. Implement StopCoreAsync

```csharp
protected override async Task StopCoreAsync(CancellationToken cancellationToken)
{
  Logger.LogDebug("Stopping file playback");
  
  // Save current position for next session
  if (_currentFile != null)
  {
    _preferences.CurrentValue.LastSongPlayed = _currentFile;
    _preferences.CurrentValue.SongPositionMs = (long)_position.TotalMilliseconds;
  }
  
  if (_playbackService != null && _playbackId != null && _isPlaybackActive)
  {
    await _playbackService.StopAsync(_playbackId, cancellationToken);
    _isPlaybackActive = false;
  }
  
  _position = TimeSpan.Zero;
}
```

#### 6. Implement SeekCoreAsync

```csharp
protected override async Task SeekCoreAsync(TimeSpan position, CancellationToken cancellationToken)
{
  if (position < TimeSpan.Zero || (_duration > TimeSpan.Zero && position > _duration))
  {
    throw new ArgumentOutOfRangeException(nameof(position), "Seek position out of range");
  }
  
  _position = position;
  
  if (_playbackService != null && _playbackId != null && _isPlaybackActive)
  {
    await _playbackService.SeekAsync(_playbackId, position, cancellationToken);
  }
  
  Logger.LogDebug("Seeked to {Position}", position);
}
```

#### 7. Update Disposal

```csharp
protected override async ValueTask DisposeAsyncCore()
{
  // Unsubscribe from playback service events
  if (_playbackService != null)
  {
    _playbackService.PlaybackEnded -= OnPlaybackServiceEnded;
    _playbackService.PositionChanged -= OnPlaybackServicePosition;
    
    if (_playbackId != null && _isPlaybackActive)
    {
      await _playbackService.StopAsync(_playbackId);
    }
  }
  
  // ... rest of existing disposal logic ...
}
```

#### 8. Update Volume Control

```csharp
protected override void OnVolumeChanged(float volume)
{
  if (_playbackService != null && _playbackId != null)
  {
    _playbackService.SetVolume(_playbackId, volume);
  }
}
```

#### 9. Update DI Registration

Ensure SoundFlowPlaybackService is injected when creating FilePlayerAudioSource.

#### 10. Add Unit Tests

Create or update `tests/Radio.Infrastructure.Tests/Audio/Sources/Primary/FilePlayerAudioSourcePlaybackTests.cs`:
- Test play/pause/resume/stop lifecycle
- Test seek functionality
- Test playback completion triggers next track
- Test volume changes apply to playback service
- Test position tracking

### Success Criteria
- [ ] Actual audio plays through SoundFlow
- [ ] Play/pause/resume/stop work correctly
- [ ] Seek updates playback position
- [ ] Auto-advance to next track on completion
- [ ] Volume control works
- [ ] Position tracking accurate
- [ ] Proper cleanup on disposal
- [ ] Unit tests pass
```

---

## Appendix: Priority Order for Implementation

### Critical Path (Required for Core Functionality)
1. **Phase 8**: FilePlayerAudioSource SoundFlow Playback
2. **Phase 2**: AudioFileEventSource SoundFlow Integration

### Important Enhancements
3. **Phase 3**: FilesController Source Switching
4. **Phase 5**: PlayHistoryController Search Optimization
5. **Phase 1**: FileBrowser Database Integration

### Nice-to-Have Features
6. **Phase 4**: AcoustID API Integration
7. **Phase 7**: TTSFactory Azure Voice API
8. **Phase 6**: RadioFactory Device Enumeration

---

*Last Updated: 2025-12-31*
