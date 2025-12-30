using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for audio source management endpoints (5 endpoints)
/// </summary>
public class SourcesApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<SourcesApiService> _logger;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public SourcesApiService(HttpClient httpClient, ILogger<SourcesApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<List<AudioSourceDto>?> GetSourcesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      // Fetch available sources from /api/sources
      _logger.LogDebug("Fetching available sources from /api/sources");
      var available = await _httpClient.GetFromJsonAsync<AvailableSourcesDto>("/api/sources", JsonOptions, cancellationToken);

      if (available == null)
      {
        _logger.LogWarning("API returned null for available sources");
        return null;
      }

      _logger.LogDebug("API returned {Count} primary sources: {Sources}",
        available.PrimarySources?.Count ?? 0,
        available.PrimarySources != null ? string.Join(", ", available.PrimarySources) : "null");

      if (available.PrimarySources == null || available.PrimarySources.Count == 0)
      {
        _logger.LogWarning("PrimarySources is null or empty");
        return [];
      }

      // Convert primary source types to AudioSourceDto objects for the dropdown
      var sources = available.PrimarySources.Select(sourceType => new AudioSourceDto
      {
        Id = sourceType.ToLowerInvariant(),
        Name = GetSourceDisplayName(sourceType),
        Type = sourceType,
        Category = "Primary",
        State = available.ActiveSourceType == sourceType ? "Active" : "Available"
      }).ToList();

      _logger.LogInformation("Returning {Count} sources: {Names}",
        sources.Count, string.Join(", ", sources.Select(s => s.Name)));

      return sources;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get sources");
      return null;
    }
  }

  private static string GetSourceDisplayName(string sourceType) => sourceType switch
  {
    "Spotify" => "Spotify",
    "Radio" => "FM/AM Radio",
    "Vinyl" => "Vinyl (Phono)",
    "FilePlayer" => "File Player",
    "GenericUSB" => "USB Audio",
    _ => sourceType
  };

  public async Task<List<AudioSourceDto>?> GetActiveSourcesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<AudioSourceDto>>("/api/sources/active", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get active sources");
      return null;
    }
  }

  public async Task<AudioSourceDto?> GetPrimarySourceAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<AudioSourceDto>("/api/sources/primary", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get primary source");
      return null;
    }
  }

  public async Task<bool> SwitchSourceAsync(string sourceType, CancellationToken cancellationToken = default)
  {
    try
    {
      var request = new { sourceType = sourceType };
      var response = await _httpClient.PostAsJsonAsync("/api/sources", request, cancellationToken);

      if (!response.IsSuccessStatusCode)
      {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Source switch failed with status {Status}: {Content}",
          response.StatusCode, content);
      }

      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to switch source to {SourceType}", sourceType);
      return false;
    }
  }

  public async Task<AudioSourceDto?> GetSourceByIdAsync(string sourceId, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<AudioSourceDto>($"/api/sources/{sourceId}", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get source by ID");
      return null;
    }
  }

  public async Task<List<AudioSourceDto>?> GetEventSourcesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<AudioSourceDto>>("/api/sources/events", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get event sources");
      return null;
    }
  }
}
