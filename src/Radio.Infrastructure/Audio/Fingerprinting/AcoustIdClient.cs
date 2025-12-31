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
  private const string LookupEndpoint = "/lookup";

  private readonly HttpClient _httpClient;
  private readonly ILogger<AcoustIdClient> _logger;
  private readonly FingerprintingOptions _options;
  private readonly bool _ownsHttpClient;

  /// <summary>
  /// Initializes a new instance of the <see cref="AcoustIdClient"/> class.
  /// </summary>
  /// <param name="httpClient">The HTTP client.</param>
  /// <param name="logger">The logger instance.</param>
  /// <param name="options">The fingerprinting options.</param>
  /// <param name="ownsHttpClient">Whether this instance owns (and should dispose) the HttpClient.</param>
  public AcoustIdClient(
    HttpClient httpClient,
    ILogger<AcoustIdClient> logger,
    IOptions<FingerprintingOptions> options,
    bool ownsHttpClient = true)
  {
    _httpClient = httpClient;
    _logger = logger;
    _options = options.Value;
    _ownsHttpClient = ownsHttpClient;
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
    var apiKey = _options.AcoustId.ApiKey;
    if (string.IsNullOrEmpty(apiKey))
    {
      _logger.LogWarning("AcoustID API key not configured");
      return null;
    }

    try
    {
      // Use POST for fingerprint data because fingerprints can be very long
      // (potentially thousands of characters) and may exceed URL length limits
      var queryParams = new Dictionary<string, string>
      {
        ["client"] = apiKey,
        ["meta"] = "recordings+releasegroups+compress",
        ["duration"] = duration.ToString(),
        ["fingerprint"] = fingerprint
      };

      var baseUrl = _options.AcoustId.BaseUrl.TrimEnd('/');
      using var content = new FormUrlEncodedContent(queryParams);
      using var httpResponse = await _httpClient.PostAsync($"{baseUrl}{LookupEndpoint}", content, ct);
      httpResponse.EnsureSuccessStatusCode();

      var response = await httpResponse.Content.ReadFromJsonAsync<AcoustIdResponse>(ct);

      if (response?.Status != "ok" || response.Results == null || response.Results.Count == 0)
      {
        _logger.LogDebug("AcoustID lookup returned no results");
        return null;
      }

      // Return the best match (highest score)
      var bestResult = response.Results
        .Where(r => r.Score >= _options.MinimumConfidenceThreshold)
        .OrderByDescending(r => r.Score)
        .FirstOrDefault();

      if (bestResult == null)
      {
        _logger.LogDebug("No AcoustID results with sufficient confidence");
        return null;
      }

      _logger.LogInformation(
        "AcoustID match found: {Id} (score: {Score:P0})",
        bestResult.Id, bestResult.Score);

      return new AcoustIdLookupResult
      {
        Id = bestResult.Id ?? string.Empty,
        Score = bestResult.Score,
        Recordings = bestResult.Recordings?.Select(r => new AcoustIdRecording
        {
          Id = r.Id ?? string.Empty,
          Title = r.Title,
          Artists = r.Artists?.Select(a => a.Name ?? string.Empty).ToList() ?? new List<string>(),
          ReleaseGroups = r.ReleaseGroups?.Select(rg => new AcoustIdReleaseGroup
          {
            Id = rg.Id ?? string.Empty,
            Title = rg.Title,
            Type = rg.Type
          }).ToList() ?? new List<AcoustIdReleaseGroup>()
        }).ToList() ?? new List<AcoustIdRecording>()
      };
    }
    catch (HttpRequestException ex)
    {
      _logger.LogError(ex, "AcoustID API request failed");
      return null;
    }
    catch (TaskCanceledException ex) when (ex.CancellationToken != ct)
    {
      _logger.LogWarning("AcoustID API request timed out");
      return null;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error processing AcoustID response");
      return null;
    }
  }

  /// <inheritdoc/>
  public void Dispose()
  {
    // Only dispose the HttpClient if this instance owns it.
    // If HttpClient was provided via IHttpClientFactory, it should not be disposed here.
    if (_ownsHttpClient)
    {
      _httpClient.Dispose();
    }
  }
}

#region Response DTOs

/// <summary>
/// AcoustID API response.
/// </summary>
public class AcoustIdResponse
{
  /// <summary>Response status ("ok" or "error").</summary>
  [JsonPropertyName("status")]
  public string? Status { get; set; }

  /// <summary>Error message if status is "error".</summary>
  [JsonPropertyName("error")]
  public AcoustIdError? Error { get; set; }

  /// <summary>List of matching results.</summary>
  [JsonPropertyName("results")]
  public List<AcoustIdResult>? Results { get; set; }
}

/// <summary>
/// AcoustID API error.
/// </summary>
public class AcoustIdError
{
  /// <summary>Error message.</summary>
  [JsonPropertyName("message")]
  public string? Message { get; set; }

  /// <summary>Error code.</summary>
  [JsonPropertyName("code")]
  public int Code { get; set; }
}

/// <summary>
/// Individual result from AcoustID lookup.
/// </summary>
public class AcoustIdResult
{
  /// <summary>AcoustID track ID.</summary>
  [JsonPropertyName("id")]
  public string? Id { get; set; }

  /// <summary>Match confidence score (0.0 to 1.0).</summary>
  [JsonPropertyName("score")]
  public double Score { get; set; }

  /// <summary>Associated MusicBrainz recordings.</summary>
  [JsonPropertyName("recordings")]
  public List<AcoustIdRecordingDto>? Recordings { get; set; }
}

/// <summary>
/// MusicBrainz recording information from AcoustID.
/// </summary>
public class AcoustIdRecordingDto
{
  /// <summary>MusicBrainz recording ID.</summary>
  [JsonPropertyName("id")]
  public string? Id { get; set; }

  /// <summary>Recording title.</summary>
  [JsonPropertyName("title")]
  public string? Title { get; set; }

  /// <summary>Duration in seconds.</summary>
  [JsonPropertyName("duration")]
  public int? Duration { get; set; }

  /// <summary>List of artists.</summary>
  [JsonPropertyName("artists")]
  public List<AcoustIdArtistDto>? Artists { get; set; }

  /// <summary>List of release groups (albums).</summary>
  [JsonPropertyName("releasegroups")]
  public List<AcoustIdReleaseGroupDto>? ReleaseGroups { get; set; }
}

/// <summary>
/// Artist information from AcoustID.
/// </summary>
public class AcoustIdArtistDto
{
  /// <summary>MusicBrainz artist ID.</summary>
  [JsonPropertyName("id")]
  public string? Id { get; set; }

  /// <summary>Artist name.</summary>
  [JsonPropertyName("name")]
  public string? Name { get; set; }
}

/// <summary>
/// Release group (album) information from AcoustID.
/// </summary>
public class AcoustIdReleaseGroupDto
{
  /// <summary>MusicBrainz release group ID.</summary>
  [JsonPropertyName("id")]
  public string? Id { get; set; }

  /// <summary>Release group title (album name).</summary>
  [JsonPropertyName("title")]
  public string? Title { get; set; }

  /// <summary>Release group type (Album, Single, EP, etc.).</summary>
  [JsonPropertyName("type")]
  public string? Type { get; set; }
}

#endregion

#region Result Models

/// <summary>
/// Processed result from AcoustID lookup.
/// </summary>
public class AcoustIdLookupResult
{
  /// <summary>AcoustID track ID.</summary>
  public required string Id { get; init; }

  /// <summary>Match confidence score (0.0 to 1.0).</summary>
  public double Score { get; init; }

  /// <summary>Associated MusicBrainz recordings.</summary>
  public List<AcoustIdRecording> Recordings { get; init; } = new();
}

/// <summary>
/// Processed MusicBrainz recording from AcoustID lookup.
/// </summary>
public class AcoustIdRecording
{
  /// <summary>MusicBrainz recording ID.</summary>
  public required string Id { get; init; }

  /// <summary>Recording title.</summary>
  public string? Title { get; init; }

  /// <summary>List of artist names.</summary>
  public List<string> Artists { get; init; } = new();

  /// <summary>List of release groups (albums).</summary>
  public List<AcoustIdReleaseGroup> ReleaseGroups { get; init; } = new();
}

/// <summary>
/// Processed release group from AcoustID lookup.
/// </summary>
public class AcoustIdReleaseGroup
{
  /// <summary>MusicBrainz release group ID.</summary>
  public required string Id { get; init; }

  /// <summary>Release group title (album name).</summary>
  public string? Title { get; init; }

  /// <summary>Release group type (Album, Single, EP, etc.).</summary>
  public string? Type { get; init; }
}

#endregion
