using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
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
  private readonly HttpClient _httpClient;
  private readonly IMetricsCollector? _metricsCollector;

  // Rate limiter: MusicBrainz enforces 1 request/second for anonymous clients
  private static readonly SemaphoreSlim _musicBrainzThrottle = new(1, 1);
  private static DateTime _lastMusicBrainzRequest = DateTime.MinValue;
  private const int MaxRetries = 2;
  private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(2);

  /// <summary>
  /// Initializes a new instance of the <see cref="MetadataLookupService"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="cache">The fingerprint cache repository.</param>
  /// <param name="metadataRepo">The track metadata repository.</param>
  /// <param name="options">The fingerprinting options.</param>
  /// <param name="httpClient">HTTP client for MusicBrainz and Cover Art Archive API calls.</param>
  /// <param name="acoustIdClient">Optional AcoustID client for API lookups.</param>
  /// <param name="metricsCollector">Optional metrics collector for fingerprint lookup metrics.</param>
  public MetadataLookupService(
    ILogger<MetadataLookupService> logger,
    IFingerprintCacheRepository cache,
    ITrackMetadataRepository metadataRepo,
    IOptions<FingerprintingOptions> options,
    HttpClient httpClient,
    AcoustIdClient? acoustIdClient = null,
    IMetricsCollector? metricsCollector = null)
  {
    _logger = logger;
    _cache = cache;
    _metadataRepo = metadataRepo;
    _options = options.Value;
    _httpClient = httpClient;
    _acoustIdClient = acoustIdClient;
    _metricsCollector = metricsCollector;
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
      _logger.LogDebug("Found cached metadata for fingerprint: {Title} by {Artist} (CoverArtUrl={CoverArtUrl})",
        cached.Metadata.Title, cached.Metadata.Artist, cached.Metadata.CoverArtUrl ?? "(none)");

      // Re-enrich cover art if missing from cache but we have a MusicBrainz release ID
      var metadata = cached.Metadata;
      if (string.IsNullOrEmpty(metadata.CoverArtUrl) && !string.IsNullOrEmpty(metadata.MusicBrainzReleaseId))
      {
        _logger.LogInformation("Cached metadata for '{Title}' missing cover art, fetching from Cover Art Archive (release {ReleaseId})",
          metadata.Title, metadata.MusicBrainzReleaseId);
        try
        {
          var coverArtUrl = await GetCoverArtUrlAsync(metadata.MusicBrainzReleaseId, ct);
          if (!string.IsNullOrEmpty(coverArtUrl))
          {
            metadata = metadata with { CoverArtUrl = coverArtUrl, UpdatedAt = DateTime.UtcNow };
            await _metadataRepo.UpdateCoverArtUrlAsync(metadata.Id, coverArtUrl, ct);
            _logger.LogInformation("Cover art URL updated for '{Title}': {Url}", metadata.Title, coverArtUrl);
          }
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Failed to re-enrich cover art for cached metadata");
        }
      }

      _metricsCollector?.Increment("audio.fingerprint.lookups", 1,
        new Dictionary<string, string> { ["result"] = "cache_hit" });

      return new MetadataLookupResult
      {
        IsMatch = true,
        Confidence = 1.0,
        FingerprintId = cached.Id,
        Metadata = metadata,
        Source = LookupSource.Cache
      };
    }

    // Step 2: Check if AcoustID API key is configured
    if (string.IsNullOrEmpty(_options.AcoustId.ApiKey))
    {
      _logger.LogDebug("No AcoustID API key configured, storing fingerprint for manual tagging");
      _metricsCollector?.Increment("audio.fingerprint.lookups", 1,
        new Dictionary<string, string> { ["result"] = "no_api_key" });
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
      _metricsCollector?.Increment("audio.fingerprint.lookups", 1,
        new Dictionary<string, string> { ["result"] = "no_client" });
      var stored = await _cache.StoreAsync(fingerprint, null, ct);
      return new MetadataLookupResult
      {
        IsMatch = false,
        Confidence = 0.0,
        FingerprintId = stored.Id,
        Source = LookupSource.Manual
      };
    }

    // Ensure we have a valid chromaprint hash before querying AcoustID
    if (string.IsNullOrWhiteSpace(fingerprint.ChromaprintHash))
    {
      _logger.LogWarning(
        "Fingerprint {FingerprintId} has no chromaprint hash, storing for manual tagging",
        fingerprint.Id);
      _metricsCollector?.Increment("audio.fingerprint.lookups", 1,
        new Dictionary<string, string> { ["result"] = "no_hash" });

      var stored = await _cache.StoreAsync(fingerprint, null, ct);
      return new MetadataLookupResult
      {
        IsMatch = false,
        Confidence = 0.0,
        FingerprintId = stored.Id,
        Source = LookupSource.Manual
      };
    }

    _logger.LogInformation("Querying AcoustID for fingerprint {FingerprintId}", fingerprint.Id);
    if (_logger.IsEnabled(LogLevel.Debug))
    {
      var hashPreview = fingerprint.ChromaprintHash.Length <= 50
        ? fingerprint.ChromaprintHash
        : fingerprint.ChromaprintHash.Substring(0, 50);

      _logger.LogDebug("Fingerprint hash preview: {HashPreview}", hashPreview);
    }
    
    var acoustIdResult = await _acoustIdClient.LookupAsync(
      fingerprint.ChromaprintHash,
      fingerprint.DurationSeconds,
      ct);

    if (acoustIdResult == null || acoustIdResult.Recordings.Count == 0)
    {
      _logger.LogInformation("No AcoustID match found for fingerprint {FingerprintId}, storing for manual tagging", fingerprint.Id);
      _metricsCollector?.Increment("audio.fingerprint.lookups", 1,
        new Dictionary<string, string> { ["result"] = "no_match" });
      var stored = await _cache.StoreAsync(fingerprint, null, ct);
      return new MetadataLookupResult
      {
        IsMatch = false,
        Confidence = 0.0,
        FingerprintId = stored.Id,
        Source = LookupSource.Manual
      };
    }

    // Get the best recording match, preferring recordings with non-compilation album release groups
    var bestRecording = SelectBestRecording(acoustIdResult.Recordings);
    if (bestRecording == null)
    {
      _logger.LogInformation("No recordings in AcoustID result for fingerprint {FingerprintId}", fingerprint.Id);
      _metricsCollector?.Increment("audio.fingerprint.lookups", 1,
        new Dictionary<string, string> { ["result"] = "no_match" });
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
    // Prefer non-compilation album release groups for the album name
    var bestReleaseGroup = bestRecording.ReleaseGroups
      .OrderByDescending(rg =>
        string.Equals(rg.Type, "Album", StringComparison.OrdinalIgnoreCase)
        && !rg.SecondaryTypes.Any(st => string.Equals(st, "Compilation", StringComparison.OrdinalIgnoreCase))
          ? 2
          : string.Equals(rg.Type, "Album", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
      .FirstOrDefault();

    var trackMetadata = new TrackMetadata
    {
      Id = Guid.NewGuid().ToString(),
      Title = bestRecording.Title ?? "Unknown Title",
      Artist = bestRecording.Artists.FirstOrDefault() ?? "Unknown Artist",
      Album = bestReleaseGroup?.Title,
      MusicBrainzRecordingId = bestRecording.Id,
      Source = MetadataSource.AcoustID,
      CreatedAt = DateTime.UtcNow,
      UpdatedAt = DateTime.UtcNow
    };

    _logger.LogInformation(
      "Created track metadata from AcoustID: Title=\"{Title}\", Artist=\"{Artist}\", Album=\"{Album}\", MusicBrainzRecordingId={MusicBrainzId}, Confidence={Confidence:P0}",
      trackMetadata.Title, trackMetadata.Artist, trackMetadata.Album ?? "(none)",
      trackMetadata.MusicBrainzRecordingId, acoustIdResult.Score);

    // Enrich with MusicBrainz data (album, year, cover art, additional IDs)
    if (!string.IsNullOrEmpty(bestRecording.Id))
    {
      try
      {
        var enriched = await GetMusicBrainzMetadataAsync(bestRecording.Id, ct);
        if (enriched != null)
        {
          trackMetadata = trackMetadata with
          {
            Album = enriched.Album ?? trackMetadata.Album,
            AlbumArtist = enriched.AlbumArtist,
            ReleaseYear = enriched.ReleaseYear ?? trackMetadata.ReleaseYear,
            CoverArtUrl = enriched.CoverArtUrl ?? trackMetadata.CoverArtUrl,
            MusicBrainzArtistId = enriched.MusicBrainzArtistId ?? trackMetadata.MusicBrainzArtistId,
            MusicBrainzReleaseId = enriched.MusicBrainzReleaseId ?? trackMetadata.MusicBrainzReleaseId,
            UpdatedAt = DateTime.UtcNow
          };

          _metricsCollector?.Increment("audio.fingerprint.musicbrainz_enrichments", 1,
            new Dictionary<string, string> { ["result"] = "success" });

          _logger.LogInformation(
            "Enriched metadata from MusicBrainz: Album=\"{Album}\", Year={Year}, CoverArt={HasCoverArt}",
            trackMetadata.Album, trackMetadata.ReleaseYear, trackMetadata.CoverArtUrl != null);
        }
        else
        {
          _metricsCollector?.Increment("audio.fingerprint.musicbrainz_enrichments", 1,
            new Dictionary<string, string> { ["result"] = "no_data" });
        }
      }
      catch (Exception ex)
      {
        _metricsCollector?.Increment("audio.fingerprint.musicbrainz_enrichments", 1,
          new Dictionary<string, string> { ["result"] = "error" });
        _logger.LogWarning(ex, "MusicBrainz enrichment failed for recording {RecordingId}, using AcoustID data only",
          bestRecording.Id);
      }
    }

    // Store the fingerprint with metadata
    var storedWithMeta = await _cache.StoreAsync(fingerprint, trackMetadata, ct);

    // Also store the track metadata in the repository
    await _metadataRepo.StoreAsync(trackMetadata, ct);

    _metricsCollector?.Increment("audio.fingerprint.lookups", 1,
      new Dictionary<string, string> { ["result"] = "match" });
    _metricsCollector?.Gauge("audio.fingerprint.confidence", acoustIdResult.Score);

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

  /// <summary>
  /// Selects the best recording from AcoustID results, preferring recordings
  /// that have non-compilation "Album" release groups.
  /// </summary>
  private AcoustIdRecording? SelectBestRecording(List<AcoustIdRecording> recordings)
  {
    if (recordings.Count == 0) return null;
    if (recordings.Count == 1) return recordings[0];

    // Score each recording: prefer those with Album-type, non-compilation release groups
    var scored = recordings.Select(r =>
    {
      int score = 0;
      foreach (var rg in r.ReleaseGroups)
      {
        bool isAlbum = string.Equals(rg.Type, "Album", StringComparison.OrdinalIgnoreCase);
        bool isCompilation = rg.SecondaryTypes.Any(st =>
          string.Equals(st, "Compilation", StringComparison.OrdinalIgnoreCase));

        if (isAlbum && !isCompilation) score += 10;   // Original album — strongly preferred
        else if (isAlbum) score += 3;                  // Compilation album — better than nothing
        else if (!isCompilation) score += 5;           // Non-compilation non-album (EP, Single)
        // Compilation non-album gets 0
      }
      // Tie-breaker: recordings with more release groups have more metadata
      score += Math.Min(r.ReleaseGroups.Count, 3);
      return (Recording: r, Score: score);
    })
    .OrderByDescending(x => x.Score)
    .ToList();

    var selected = scored.First().Recording;
    _logger.LogDebug(
      "Selected recording {RecordingId} (score={Score}) from {Total} candidates. " +
      "Top scores: {Scores}",
      selected.Id, scored.First().Score, recordings.Count,
      string.Join(", ", scored.Take(3).Select(s => $"{s.Recording.Id}={s.Score}")));

    return selected;
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

    var mb = _options.MusicBrainz;
    var baseUrl = mb.BaseUrl.TrimEnd('/');
    var url = $"{baseUrl}/recording/{recordingId}?inc=artists+releases+release-groups&fmt=json";

    _logger.LogInformation("Querying MusicBrainz for recording {RecordingId}", recordingId);

    try
    {
      var json = await SendMusicBrainzRequestAsync(url, ct);
      if (json == null) return null;
      var recording = JsonSerializer.Deserialize<MusicBrainzRecording>(json);
      if (recording == null)
      {
        _logger.LogWarning("Failed to parse MusicBrainz response for recording {RecordingId}", recordingId);
        return null;
      }

      // Extract artist info
      var artistCredit = recording.ArtistCredit?.FirstOrDefault();
      var artistName = artistCredit?.Artist?.Name ?? artistCredit?.Name ?? "Unknown Artist";
      var artistId = artistCredit?.Artist?.Id;

      // Extract release/album info, preferring non-compilation album releases
      string? album = null;
      string? releaseId = null;
      string? releaseGroupId = null;
      int? releaseYear = null;

      // Sort releases: non-compilation albums first, then albums, then everything else
      var sortedReleases = (recording.Releases ?? [])
        .OrderByDescending(r =>
        {
          var rg = r.ReleaseGroup;
          if (rg == null) return 0;
          bool isAlbum = string.Equals(rg.PrimaryType, "Album", StringComparison.OrdinalIgnoreCase);
          bool isCompilation = rg.SecondaryTypes?.Any(st =>
            string.Equals(st, "Compilation", StringComparison.OrdinalIgnoreCase)) == true;
          if (isAlbum && !isCompilation) return 2;
          if (isAlbum) return 1;
          return 0;
        });

      foreach (var release in sortedReleases)
      {
        album = release.ReleaseGroup?.Title ?? release.Title;
        releaseId = release.Id;
        releaseGroupId = release.ReleaseGroup?.Id;
        if (!string.IsNullOrEmpty(release.Date) && int.TryParse(release.Date.Split('-')[0], out var year))
          releaseYear = year;
        if (album != null) break;
      }

      // Try to get cover art
      string? coverArtUrl = null;
      if (!string.IsNullOrEmpty(releaseId))
        coverArtUrl = await GetCoverArtUrlAsync(releaseId, ct);

      var metadata = new TrackMetadata
      {
        Id = Guid.NewGuid().ToString(),
        Title = recording.Title ?? "Unknown Title",
        Artist = artistName,
        Album = album,
        MusicBrainzRecordingId = recordingId,
        MusicBrainzArtistId = artistId,
        MusicBrainzReleaseId = releaseId,
        ReleaseYear = releaseYear,
        CoverArtUrl = coverArtUrl,
        Source = MetadataSource.AcoustID,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
      };

      _logger.LogInformation(
        "MusicBrainz enrichment: Title=\"{Title}\", Artist=\"{Artist}\", Album=\"{Album}\", Year={Year}, CoverArt={HasCoverArt}",
        metadata.Title, metadata.Artist, metadata.Album, metadata.ReleaseYear, coverArtUrl != null);

      return metadata;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error querying MusicBrainz for recording {RecordingId}", recordingId);
      return null;
    }
  }

  /// <summary>
  /// Sends a rate-limited, retryable request to MusicBrainz.
  /// Enforces 1 request/second and retries on transient failures (SSL resets, timeouts).
  /// </summary>
  private async Task<string?> SendMusicBrainzRequestAsync(string url, CancellationToken ct)
  {
    for (int attempt = 0; attempt <= MaxRetries; attempt++)
    {
      await _musicBrainzThrottle.WaitAsync(ct);
      try
      {
        // Enforce minimum 1.1s between requests (MusicBrainz rate limit)
        var elapsed = DateTime.UtcNow - _lastMusicBrainzRequest;
        var minInterval = TimeSpan.FromMilliseconds(1100);
        if (elapsed < minInterval)
        {
          await Task.Delay(minInterval - elapsed, ct);
        }

        using var response = await _httpClient.GetAsync(url, ct);
        _lastMusicBrainzRequest = DateTime.UtcNow;

        if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
            response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
          _logger.LogWarning("MusicBrainz rate limited ({StatusCode}), retrying after delay", response.StatusCode);
          await Task.Delay(RetryBaseDelay * (attempt + 1), ct);
          continue;
        }

        if (!response.IsSuccessStatusCode)
        {
          _logger.LogWarning("MusicBrainz API returned {StatusCode} for {Url}", response.StatusCode, url);
          return null;
        }

        return await response.Content.ReadAsStringAsync(ct);
      }
      catch (HttpRequestException ex) when (attempt < MaxRetries)
      {
        _logger.LogWarning("MusicBrainz request failed (attempt {Attempt}/{MaxRetries}): {Message}",
          attempt + 1, MaxRetries + 1, ex.Message);
        _lastMusicBrainzRequest = DateTime.UtcNow;
        await Task.Delay(RetryBaseDelay * (attempt + 1), ct);
      }
      finally
      {
        _musicBrainzThrottle.Release();
      }
    }

    _logger.LogWarning("MusicBrainz request failed after {MaxRetries} retries: {Url}", MaxRetries + 1, url);
    return null;
  }

  /// <summary>
  /// Searches MusicBrainz by title/artist text and returns a cover art URL if found.
  /// Used by Bluetooth source when AVRCP provides metadata but not album art.
  /// </summary>
  public async Task<string?> SearchCoverArtByTextAsync(
    string title, string artist, string? album = null, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
      return null;

    try
    {
      // Strip streaming service suffixes like "- 2010 Remaster", "(Deluxe Edition)",
      // "(feat. X)" that Spotify/Tidal append but MusicBrainz doesn't index
      var cleanTitle = CleanStreamingTitle(title);

      // Build MusicBrainz Lucene search query — escape special chars but not spaces
      var escapedTitle = Uri.EscapeDataString(cleanTitle);
      var escapedArtist = Uri.EscapeDataString(artist);
      var query = $"recording:\"{escapedTitle}\" AND artist:\"{escapedArtist}\"";
      if (!string.IsNullOrWhiteSpace(album))
        query += $" AND release:\"{Uri.EscapeDataString(album)}\"";

      var mb = _options.MusicBrainz;
      var url = $"{mb.BaseUrl}/recording?query={query}&fmt=json&limit=5";

      _logger.LogDebug("Searching MusicBrainz for cover art: {Title} by {Artist}", title, artist);

      var json = await SendMusicBrainzRequestAsync(url, ct);
      if (json == null) return null;

      var searchResult = JsonSerializer.Deserialize<MusicBrainzSearchResult>(json);
      var recordings = searchResult?.Recordings;
      if (recordings == null || recordings.Count == 0)
      {
        _logger.LogDebug("No MusicBrainz recordings found for '{Title}' by '{Artist}'", title, artist);
        return null;
      }

      // Try multiple recordings/releases — the first may not have cover art
      foreach (var recording in recordings)
      {
        if (recording.Releases == null) continue;
        foreach (var release in recording.Releases)
        {
          if (string.IsNullOrEmpty(release.Id)) continue;
          var coverArtUrl = await GetCoverArtUrlAsync(release.Id, ct);
          if (!string.IsNullOrEmpty(coverArtUrl))
          {
            _logger.LogInformation("Found cover art for '{Title}' by '{Artist}': {Url}", title, artist, coverArtUrl);
            return coverArtUrl;
          }
        }
      }

      _logger.LogDebug("No cover art found across {Count} recordings for '{Title}' by '{Artist}'",
        recordings.Count, title, artist);
      return null;
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Cover art text search failed for '{Title}' by '{Artist}'", title, artist);
      return null;
    }
  }

  /// <summary>
  /// Strips streaming service suffixes from track titles for better MusicBrainz matching.
  /// Spotify, Tidal, etc. append remaster/edition/feature info that MusicBrainz doesn't index.
  /// </summary>
  private static string CleanStreamingTitle(string title)
  {
    // Strip " - Remaster", " - 2010 Remaster", " - Deluxe Edition", etc.
    var dashIdx = title.IndexOf(" - ", StringComparison.Ordinal);
    if (dashIdx > 0)
    {
      var suffix = title[(dashIdx + 3)..];
      if (suffix.Contains("Remaster", StringComparison.OrdinalIgnoreCase) ||
          suffix.Contains("Edition", StringComparison.OrdinalIgnoreCase) ||
          suffix.Contains("Version", StringComparison.OrdinalIgnoreCase) ||
          suffix.Contains("Mix", StringComparison.OrdinalIgnoreCase) ||
          suffix.Contains("Bonus", StringComparison.OrdinalIgnoreCase) ||
          suffix.Contains("Anniversary", StringComparison.OrdinalIgnoreCase) ||
          suffix.Contains("Mono", StringComparison.OrdinalIgnoreCase) ||
          suffix.Contains("Stereo", StringComparison.OrdinalIgnoreCase) ||
          suffix.Contains("Live", StringComparison.OrdinalIgnoreCase))
      {
        title = title[..dashIdx];
      }
    }

    // Strip parenthesized suffixes: "(feat. X)", "(Remastered)", "(Deluxe)", etc.
    var parenIdx = title.IndexOf(" (", StringComparison.Ordinal);
    if (parenIdx > 0 && title.EndsWith(')'))
    {
      var inner = title[(parenIdx + 2)..^1];
      if (inner.StartsWith("feat", StringComparison.OrdinalIgnoreCase) ||
          inner.Contains("Remaster", StringComparison.OrdinalIgnoreCase) ||
          inner.Contains("Edition", StringComparison.OrdinalIgnoreCase) ||
          inner.Contains("Version", StringComparison.OrdinalIgnoreCase) ||
          inner.Contains("Deluxe", StringComparison.OrdinalIgnoreCase) ||
          inner.Contains("Bonus", StringComparison.OrdinalIgnoreCase))
      {
        title = title[..parenIdx];
      }
    }

    return title.Trim();
  }

  /// <inheritdoc/>
  public async Task<string?> GetCoverArtByReleaseIdAsync(string releaseId, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(releaseId))
      return null;

    return await GetCoverArtUrlAsync(releaseId, ct);
  }

  /// <summary>
  /// Queries the Cover Art Archive for a release's front cover image URL.
  /// Uses a separate HttpClient to avoid sending the MusicBrainz User-Agent to archive.org.
  /// </summary>
  private async Task<string?> GetCoverArtUrlAsync(string releaseId, CancellationToken ct)
  {
    try
    {
      var url = $"https://coverartarchive.org/release/{releaseId}";
      _logger.LogDebug("Querying Cover Art Archive for release {ReleaseId}", releaseId);

      // Use a dedicated HttpClient for Cover Art Archive requests since the redirect
      // goes to archive.org which may not accept the MusicBrainz User-Agent
      using var coverArtClient = new HttpClient(new HttpClientHandler
      {
        AllowAutoRedirect = true
      });
      coverArtClient.DefaultRequestHeaders.UserAgent.ParseAdd("RadioConsole/1.0");
      coverArtClient.Timeout = TimeSpan.FromSeconds(10);

      using var response = await coverArtClient.GetAsync(url, ct);
      if (!response.IsSuccessStatusCode)
      {
        _logger.LogDebug("Cover Art Archive returned {StatusCode} for release {ReleaseId}",
          response.StatusCode, releaseId);
        return null;
      }

      var json = await response.Content.ReadAsStringAsync(ct);
      var coverArt = JsonSerializer.Deserialize<CoverArtArchiveResponse>(json);

      var frontImage = coverArt?.Images?.FirstOrDefault(i => i.Front == true);
      if (frontImage != null)
      {
        // Prefer small thumbnail, fall back to full image
        return frontImage.Thumbnails?.Small
          ?? frontImage.Thumbnails?.Large
          ?? frontImage.Image;
      }

      // Fall back to first image if no front cover
      var firstImage = coverArt?.Images?.FirstOrDefault();
      return firstImage?.Thumbnails?.Small
        ?? firstImage?.Thumbnails?.Large
        ?? firstImage?.Image;
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error querying Cover Art Archive for release {ReleaseId}", releaseId);
      return null;
    }
  }
}

#region MusicBrainz Response DTOs

internal sealed class MusicBrainzSearchResult
{
  [JsonPropertyName("recordings")]
  public List<MusicBrainzRecording>? Recordings { get; set; }
}

internal sealed class MusicBrainzRecording
{
  [JsonPropertyName("id")]
  public string? Id { get; set; }

  [JsonPropertyName("title")]
  public string? Title { get; set; }

  [JsonPropertyName("artist-credit")]
  public List<MusicBrainzArtistCredit>? ArtistCredit { get; set; }

  [JsonPropertyName("releases")]
  public List<MusicBrainzRelease>? Releases { get; set; }
}

internal sealed class MusicBrainzArtistCredit
{
  [JsonPropertyName("name")]
  public string? Name { get; set; }

  [JsonPropertyName("artist")]
  public MusicBrainzArtist? Artist { get; set; }
}

internal sealed class MusicBrainzArtist
{
  [JsonPropertyName("id")]
  public string? Id { get; set; }

  [JsonPropertyName("name")]
  public string? Name { get; set; }
}

internal sealed class MusicBrainzRelease
{
  [JsonPropertyName("id")]
  public string? Id { get; set; }

  [JsonPropertyName("title")]
  public string? Title { get; set; }

  [JsonPropertyName("date")]
  public string? Date { get; set; }

  [JsonPropertyName("release-group")]
  public MusicBrainzReleaseGroup? ReleaseGroup { get; set; }
}

internal sealed class MusicBrainzReleaseGroup
{
  [JsonPropertyName("id")]
  public string? Id { get; set; }

  [JsonPropertyName("title")]
  public string? Title { get; set; }

  [JsonPropertyName("primary-type")]
  public string? PrimaryType { get; set; }

  [JsonPropertyName("secondary-types")]
  public List<string>? SecondaryTypes { get; set; }
}

#endregion

#region Cover Art Archive Response DTOs

internal sealed class CoverArtArchiveResponse
{
  [JsonPropertyName("images")]
  public List<CoverArtImage>? Images { get; set; }
}

internal sealed class CoverArtImage
{
  [JsonPropertyName("front")]
  public bool? Front { get; set; }

  [JsonPropertyName("image")]
  public string? Image { get; set; }

  [JsonPropertyName("thumbnails")]
  public CoverArtThumbnails? Thumbnails { get; set; }
}

internal sealed class CoverArtThumbnails
{
  [JsonPropertyName("small")]
  public string? Small { get; set; }

  [JsonPropertyName("large")]
  public string? Large { get; set; }

  [JsonPropertyName("250")]
  public string? Size250 { get; set; }

  [JsonPropertyName("500")]
  public string? Size500 { get; set; }
}

#endregion
