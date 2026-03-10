using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Metrics;

namespace Radio.Fingerprinting.Services;

/// <summary>
/// Service for looking up cover art via MusicBrainz and the Cover Art Archive.
/// Used by Bluetooth source when AVRCP provides metadata but not album art.
/// </summary>
public sealed class MetadataLookupService : IMetadataLookupService
{
  private readonly ILogger<MetadataLookupService> _logger;
  private readonly FingerprintingOptions _options;
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
  /// <param name="options">The fingerprinting options.</param>
  /// <param name="httpClient">HTTP client for MusicBrainz and Cover Art Archive API calls.</param>
  /// <param name="metricsCollector">Optional metrics collector for cover art metrics.</param>
  public MetadataLookupService(
    ILogger<MetadataLookupService> logger,
    IOptions<FingerprintingOptions> options,
    HttpClient httpClient,
    IMetricsCollector? metricsCollector = null)
  {
    _logger = logger;
    _options = options.Value;
    _httpClient = httpClient;
    _metricsCollector = metricsCollector;
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
    {
      return null;
    }

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
      {
        query += $" AND release:\"{Uri.EscapeDataString(album)}\"";
      }

      var mb = _options.MusicBrainz;
      var url = $"{mb.BaseUrl}/recording?query={query}&fmt=json&limit=5";

      _logger.LogDebug("Searching MusicBrainz for cover art: {Title} by {Artist}", title, artist);

      var json = await SendMusicBrainzRequestAsync(url, ct);
      if (json == null)
      {
        _metricsCollector?.Increment("fingerprint.cover_art_searches", 1,
          new Dictionary<string, string> { ["result"] = "error" });
        return null;
      }

      var searchResult = JsonSerializer.Deserialize<MusicBrainzSearchResult>(json);
      var recordings = searchResult?.Recordings;
      if (recordings == null || recordings.Count == 0)
      {
        _logger.LogDebug("No MusicBrainz recordings found for '{Title}' by '{Artist}'", title, artist);
        _metricsCollector?.Increment("fingerprint.cover_art_searches", 1,
          new Dictionary<string, string> { ["result"] = "not_found" });
        return null;
      }

      // Try multiple recordings/releases — the first may not have cover art
      foreach (var recording in recordings)
      {
        if (recording.Releases == null)
        {
          continue;
        }
        foreach (var release in recording.Releases)
        {
          if (string.IsNullOrEmpty(release.Id))
          {
            continue;
          }
          var coverArtUrl = await GetCoverArtUrlAsync(release.Id, ct);
          if (!string.IsNullOrEmpty(coverArtUrl))
          {
            _logger.LogInformation("Found cover art for '{Title}' by '{Artist}': {Url}", title, artist, coverArtUrl);
            _metricsCollector?.Increment("fingerprint.cover_art_searches", 1,
              new Dictionary<string, string> { ["result"] = "found" });
            return coverArtUrl;
          }
        }
      }

      _logger.LogDebug("No cover art found across {Count} recordings for '{Title}' by '{Artist}'",
        recordings.Count, title, artist);
      _metricsCollector?.Increment("fingerprint.cover_art_searches", 1,
        new Dictionary<string, string> { ["result"] = "not_found" });
      return null;
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Cover art text search failed for '{Title}' by '{Artist}'", title, artist);
      _metricsCollector?.Increment("fingerprint.cover_art_searches", 1,
        new Dictionary<string, string> { ["result"] = "error" });
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
    {
      return null;
    }

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
        _metricsCollector?.Increment("fingerprint.cover_art_fetches", 1,
          new Dictionary<string, string> { ["result"] = "not_found" });
        return null;
      }

      var json = await response.Content.ReadAsStringAsync(ct);
      var coverArt = JsonSerializer.Deserialize<CoverArtArchiveResponse>(json);

      var frontImage = coverArt?.Images?.FirstOrDefault(i => i.Front == true);
      if (frontImage != null)
      {
        _metricsCollector?.Increment("fingerprint.cover_art_fetches", 1,
          new Dictionary<string, string> { ["result"] = "found" });
        // Prefer small thumbnail, fall back to full image
        return frontImage.Thumbnails?.Small
          ?? frontImage.Thumbnails?.Large
          ?? frontImage.Image;
      }

      // Fall back to first image if no front cover
      var firstImage = coverArt?.Images?.FirstOrDefault();
      var resultUrl = firstImage?.Thumbnails?.Small
        ?? firstImage?.Thumbnails?.Large
        ?? firstImage?.Image;

      _metricsCollector?.Increment("fingerprint.cover_art_fetches", 1,
        new Dictionary<string, string> { ["result"] = resultUrl != null ? "found" : "not_found" });

      return resultUrl;
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Error querying Cover Art Archive for release {ReleaseId}", releaseId);
      _metricsCollector?.Increment("fingerprint.cover_art_fetches", 1,
        new Dictionary<string, string> { ["result"] = "error" });
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
