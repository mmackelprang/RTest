using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for audio control endpoints (12 endpoints)
/// </summary>
public class AudioApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<AudioApiService> _logger;

  public AudioApiService(HttpClient httpClient, ILogger<AudioApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<PlaybackStateDto?> GetPlaybackStateAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<PlaybackStateDto>("/api/audio", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get playback state");
      return null;
    }
  }

  public async Task<PlaybackStateDto?> UpdatePlaybackStateAsync(UpdatePlaybackRequest request, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/audio", request, cancellationToken);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<PlaybackStateDto>(cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to update playback state");
      return null;
    }
  }

  public async Task<PlaybackStateDto?> StartAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/audio/start", null, cancellationToken);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<PlaybackStateDto>(cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start playback");
      return null;
    }
  }

  public async Task<PlaybackStateDto?> StopAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/audio/stop", null, cancellationToken);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<PlaybackStateDto>(cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to stop playback");
      return null;
    }
  }

  public async Task<VolumeDto?> GetVolumeAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<VolumeDto>("/api/audio/volume", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get volume");
      return null;
    }
  }

  public async Task<PlaybackStateDto?> SetVolumeAsync(float volume, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync($"/api/audio/volume/{volume}", null, cancellationToken);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<PlaybackStateDto>(cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set volume");
      return null;
    }
  }

  public async Task<PlaybackStateDto?> ToggleMuteAsync(bool muted, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/audio/mute", null, cancellationToken);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<PlaybackStateDto>(cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to toggle mute");
      return null;
    }
  }

  public async Task<PlaybackStateDto?> NextAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/audio/next", null, cancellationToken);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<PlaybackStateDto>(cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to go to next track");
      return null;
    }
  }

  public async Task<PlaybackStateDto?> PreviousAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/audio/previous", null, cancellationToken);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<PlaybackStateDto>(cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to go to previous track");
      return null;
    }
  }

  public async Task<PlaybackStateDto?> SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
  {
    try
    {
      _logger.LogDebug("Setting shuffle to {Enabled}", enabled);
      var request = new { Enabled = enabled };
      var response = await _httpClient.PostAsJsonAsync("/api/audio/shuffle", request, cancellationToken);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<PlaybackStateDto>(cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set shuffle to {Enabled}", enabled);
      return null;
    }
  }

  public async Task<PlaybackStateDto?> SetRepeatAsync(string mode, CancellationToken cancellationToken = default)
  {
    try
    {
      _logger.LogDebug("Setting repeat mode to {Mode}", mode);
      var request = new { Mode = mode };
      var response = await _httpClient.PostAsJsonAsync("/api/audio/repeat", request, cancellationToken);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<PlaybackStateDto>(cancellationToken: cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set repeat mode to {Mode}", mode);
      return null;
    }
  }

  public async Task<NowPlayingDto?> GetNowPlayingAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      _logger.LogDebug("Fetching now playing info from /api/audio/nowplaying");
      var result = await _httpClient.GetFromJsonAsync<NowPlayingDto>("/api/audio/nowplaying", cancellationToken);
      
      if (result != null)
      {
        _logger.LogDebug("Now playing received: Title={Title}, Artist={Artist}, Album={Album}, Source={Source}", 
          result.Title, result.Artist, result.Album, result.SourceName);
      }
      else
      {
        _logger.LogWarning("Now playing returned null");
      }
      
      return result;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get now playing info");
      return null;
    }
  }
}
