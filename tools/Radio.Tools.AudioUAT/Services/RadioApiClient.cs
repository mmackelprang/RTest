using System.Net.Http.Json;
using System.Text.Json;

namespace Radio.Tools.AudioUAT.Services;

/// <summary>
/// HTTP client for interacting with the Radio.API.
/// </summary>
public class RadioApiClient : IDisposable
{
  private readonly HttpClient _httpClient;
  private readonly JsonSerializerOptions _jsonOptions;

  /// <summary>
  /// Gets the base URL of the API.
  /// </summary>
  public string BaseUrl { get; }

  /// <summary>
  /// Initializes a new instance of the RadioApiClient.
  /// </summary>
  /// <param name="baseUrl">The base URL of the Radio.API.</param>
  public RadioApiClient(string baseUrl = "http://localhost:5000")
  {
    BaseUrl = baseUrl.TrimEnd('/');
    _httpClient = new HttpClient
    {
      BaseAddress = new Uri(BaseUrl),
      Timeout = TimeSpan.FromSeconds(30)
    };
    _jsonOptions = new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
  }

  #region Health Check

  /// <summary>
  /// Checks if the API is reachable.
  /// </summary>
  public async Task<bool> IsApiAvailableAsync(CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.GetAsync("/api/sources", ct);
      return response.IsSuccessStatusCode;
    }
    catch
    {
      return false;
    }
  }

  #endregion

  #region Sources API

  /// <summary>
  /// Gets available audio sources.
  /// </summary>
  public async Task<AvailableSourcesResponse?> GetSourcesAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/sources", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<AvailableSourcesResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Gets the current primary source.
  /// </summary>
  public async Task<AudioSourceResponse?> GetPrimarySourceAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/sources/primary", ct);
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      return null;
    }

    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<AudioSourceResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Switches to a different audio source.
  /// </summary>
  public async Task<AudioSourceResponse?> SwitchSourceAsync(string sourceType, CancellationToken ct = default)
  {
    var request = new { SourceType = sourceType };
    var response = await _httpClient.PostAsJsonAsync("/api/sources", request, _jsonOptions, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<AudioSourceResponse>(_jsonOptions, ct);
  }

  #endregion

  #region Audio/Playback API

  /// <summary>
  /// Gets the current playback state.
  /// </summary>
  public async Task<PlaybackStateResponse?> GetPlaybackStateAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/audio", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<PlaybackStateResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Updates playback state (play, pause, stop, volume, etc.).
  /// </summary>
  public async Task<PlaybackStateResponse?> UpdatePlaybackAsync(UpdatePlaybackRequest request, CancellationToken ct = default)
  {
    var response = await _httpClient.PostAsJsonAsync("/api/audio", request, _jsonOptions, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<PlaybackStateResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Starts playback.
  /// </summary>
  public Task<PlaybackStateResponse?> PlayAsync(CancellationToken ct = default)
    => UpdatePlaybackAsync(new UpdatePlaybackRequest { Action = PlaybackAction.Play }, ct);

  /// <summary>
  /// Pauses playback.
  /// </summary>
  public Task<PlaybackStateResponse?> PauseAsync(CancellationToken ct = default)
    => UpdatePlaybackAsync(new UpdatePlaybackRequest { Action = PlaybackAction.Pause }, ct);

  /// <summary>
  /// Stops playback.
  /// </summary>
  public Task<PlaybackStateResponse?> StopAsync(CancellationToken ct = default)
    => UpdatePlaybackAsync(new UpdatePlaybackRequest { Action = PlaybackAction.Stop }, ct);

  /// <summary>
  /// Sets volume level (0.0 to 1.0).
  /// </summary>
  public async Task<VolumeResponse?> SetVolumeAsync(float volume, CancellationToken ct = default)
  {
    var response = await _httpClient.PostAsync($"/api/audio/volume/{volume}", null, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<VolumeResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Gets current volume level.
  /// </summary>
  public async Task<VolumeResponse?> GetVolumeAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/audio/volume", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<VolumeResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Skips to next track.
  /// </summary>
  public async Task<PlaybackStateResponse?> NextTrackAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.PostAsync("/api/audio/next", null, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<PlaybackStateResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Goes to previous track.
  /// </summary>
  public async Task<PlaybackStateResponse?> PreviousTrackAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.PostAsync("/api/audio/previous", null, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<PlaybackStateResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Gets now playing information.
  /// </summary>
  public async Task<NowPlayingResponse?> GetNowPlayingAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/audio/nowplaying", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<NowPlayingResponse>(_jsonOptions, ct);
  }

  #endregion

  #region Queue API

  /// <summary>
  /// Gets the current queue.
  /// </summary>
  public async Task<List<QueueItemResponse>?> GetQueueAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/queue", ct);
    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
        response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      return new List<QueueItemResponse>();
    }

    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<List<QueueItemResponse>>(_jsonOptions, ct);
  }

  /// <summary>
  /// Adds a track to the queue.
  /// </summary>
  public async Task<List<QueueItemResponse>?> AddToQueueAsync(string trackIdentifier, int? position = null, CancellationToken ct = default)
  {
    var request = new { TrackIdentifier = trackIdentifier, Position = position };
    var response = await _httpClient.PostAsJsonAsync("/api/queue/add", request, _jsonOptions, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<List<QueueItemResponse>>(_jsonOptions, ct);
  }

  /// <summary>
  /// Clears the queue.
  /// </summary>
  public async Task<bool> ClearQueueAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.DeleteAsync("/api/queue", ct);
    // Don't throw if queue source not active
    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
        response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      return false;
    }
    return response.IsSuccessStatusCode;
  }

  /// <summary>
  /// Removes a track from the queue by index.
  /// </summary>
  public async Task<bool> RemoveFromQueueAsync(int index, CancellationToken ct = default)
  {
    var response = await _httpClient.DeleteAsync($"/api/queue/{index}", ct);
    // Don't throw if queue source not active
    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
        response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      return false;
    }
    return response.IsSuccessStatusCode;
  }

  /// <summary>
  /// Moves a track in the queue from one position to another.
  /// </summary>
  public async Task<List<QueueItemResponse>?> MoveQueueItemAsync(int fromIndex, int toIndex, CancellationToken ct = default)
  {
    var request = new { FromIndex = fromIndex, ToIndex = toIndex };
    var response = await _httpClient.PostAsJsonAsync("/api/queue/move", request, _jsonOptions, ct);
    // Don't throw if queue source not active
    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
        response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      return null;
    }
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<List<QueueItemResponse>>(_jsonOptions, ct);
  }

  /// <summary>
  /// Jumps to a specific index in the queue.
  /// </summary>
  public async Task<PlaybackStateResponse?> JumpToQueueIndexAsync(int index, CancellationToken ct = default)
  {
    var response = await _httpClient.PostAsync($"/api/queue/jump/{index}", null, ct);
    // Don't throw if queue source not active
    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
        response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      return null;
    }
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<PlaybackStateResponse>(_jsonOptions, ct);
  }

  #endregion

  #region Devices API

  /// <summary>
  /// Gets available output devices.
  /// </summary>
  public async Task<List<AudioDeviceResponse>?> GetOutputDevicesAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/devices/output", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<List<AudioDeviceResponse>>(_jsonOptions, ct);
  }

  /// <summary>
  /// Gets available input devices.
  /// </summary>
  public async Task<List<AudioDeviceResponse>?> GetInputDevicesAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/devices/input", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<List<AudioDeviceResponse>>(_jsonOptions, ct);
  }

  /// <summary>
  /// Gets the default output device.
  /// </summary>
  public async Task<AudioDeviceResponse?> GetDefaultOutputDeviceAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/devices/output/default", ct);
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
      return null;
    }

    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<AudioDeviceResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Sets the output device.
  /// </summary>
  public async Task<bool> SetOutputDeviceAsync(string deviceId, CancellationToken ct = default)
  {
    var request = new { DeviceId = deviceId };
    var response = await _httpClient.PostAsJsonAsync("/api/devices/output", request, _jsonOptions, ct);
    response.EnsureSuccessStatusCode();
    return response.IsSuccessStatusCode;
  }

  /// <summary>
  /// Gets USB devices.
  /// </summary>
  public async Task<List<AudioDeviceResponse>?> GetUsbDevicesAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/devices/usb", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<List<AudioDeviceResponse>>(_jsonOptions, ct);
  }

  /// <summary>
  /// Refreshes the device list.
  /// </summary>
  public async Task<bool> RefreshDevicesAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.PostAsync("/api/devices/refresh", null, ct);
    response.EnsureSuccessStatusCode();
    return response.IsSuccessStatusCode;
  }

  #endregion

  #region Balance API

  /// <summary>
  /// Sets the audio balance (-100 to 100).
  /// </summary>
  public async Task<VolumeResponse?> SetBalanceAsync(int balance, CancellationToken ct = default)
  {
    var normalizedBalance = balance / 100.0f; // Convert to -1.0 to 1.0
    var request = new UpdatePlaybackRequest { Balance = normalizedBalance };
    var response = await _httpClient.PostAsJsonAsync("/api/audio", request, _jsonOptions, ct);
    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadFromJsonAsync<PlaybackStateResponse>(_jsonOptions, ct);
    return new VolumeResponse { Volume = result?.Volume ?? 0, Balance = result?.Balance ?? 0 };
  }

  /// <summary>
  /// Toggles the mute state.
  /// </summary>
  public async Task<MuteResponse?> ToggleMuteAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.PostAsync("/api/audio/mute", null, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<MuteResponse>(_jsonOptions, ct);
  }

  #endregion

  #region Radio API

  /// <summary>
  /// Gets the current radio state.
  /// </summary>
  public async Task<RadioStateResponse?> GetRadioStateAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/radio/state", ct);
    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
    {
      return null;
    }

    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<RadioStateResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Sets the radio frequency.
  /// </summary>
  public async Task<RadioStateResponse?> SetFrequencyAsync(double frequencyHz, CancellationToken ct = default)
  {
    var request = new { Frequency = frequencyHz };
    var response = await _httpClient.PostAsJsonAsync("/api/radio/frequency", request, _jsonOptions, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<RadioStateResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Steps frequency up.
  /// </summary>
  public async Task<RadioStateResponse?> StepFrequencyUpAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.PostAsync("/api/radio/frequency/up", null, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<RadioStateResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Steps frequency down.
  /// </summary>
  public async Task<RadioStateResponse?> StepFrequencyDownAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.PostAsync("/api/radio/frequency/down", null, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<RadioStateResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Tunes frequency up (alias for StepFrequencyUp).
  /// </summary>
  public Task<RadioStateResponse?> TuneUpAsync(CancellationToken ct = default)
    => StepFrequencyUpAsync(ct);

  /// <summary>
  /// Tunes frequency down (alias for StepFrequencyDown).
  /// </summary>
  public Task<RadioStateResponse?> TuneDownAsync(CancellationToken ct = default)
    => StepFrequencyDownAsync(ct);

  /// <summary>
  /// Scans up for the next station.
  /// </summary>
  public Task<RadioStateResponse?> ScanUpAsync(CancellationToken ct = default)
    => StartScanAsync("Up", ct);

  /// <summary>
  /// Scans down for the previous station.
  /// </summary>
  public Task<RadioStateResponse?> ScanDownAsync(CancellationToken ct = default)
    => StartScanAsync("Down", ct);

  /// <summary>
  /// Sets the radio band (FM, AM, Shortwave).
  /// </summary>
  public async Task<RadioStateResponse?> SetBandAsync(string band, CancellationToken ct = default)
  {
    var request = new { Band = band };
    var response = await _httpClient.PostAsJsonAsync("/api/radio/band", request, _jsonOptions, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<RadioStateResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Starts scanning for stations.
  /// </summary>
  public async Task<RadioStateResponse?> StartScanAsync(string direction, CancellationToken ct = default)
  {
    var request = new { Direction = direction };
    var response = await _httpClient.PostAsJsonAsync("/api/radio/scan/start", request, _jsonOptions, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<RadioStateResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Stops scanning.
  /// </summary>
  public async Task<RadioStateResponse?> StopScanAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.PostAsync("/api/radio/scan/stop", null, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<RadioStateResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Gets available radio devices.
  /// </summary>
  public async Task<RadioDeviceListResponse?> GetRadioDevicesAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/radio/devices", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<RadioDeviceListResponse>(_jsonOptions, ct);
  }

  #endregion

  #region Spotify API

  /// <summary>
  /// Searches Spotify.
  /// </summary>
  public async Task<SpotifySearchResponse?> SearchSpotifyAsync(string query, string? types = null, CancellationToken ct = default)
  {
    var url = $"/api/spotify/search?query={Uri.EscapeDataString(query)}";
    if (!string.IsNullOrWhiteSpace(types))
    {
      url += $"&types={Uri.EscapeDataString(types)}";
    }

    var response = await _httpClient.GetAsync(url, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<SpotifySearchResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Plays a Spotify URI.
  /// </summary>
  public async Task PlaySpotifyUriAsync(string uri, CancellationToken ct = default)
  {
    var request = new { Uri = uri };
    var response = await _httpClient.PostAsJsonAsync("/api/spotify/play", request, _jsonOptions, ct);
    response.EnsureSuccessStatusCode();
  }

  /// <summary>
  /// Gets Spotify authentication status.
  /// </summary>
  public async Task<SpotifyAuthStatusResponse?> GetSpotifyAuthStatusAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/spotify/auth/status", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<SpotifyAuthStatusResponse>(_jsonOptions, ct);
  }

  #endregion

  #region Files API

  /// <summary>
  /// Gets files in a directory.
  /// </summary>
  public async Task<FileBrowserResponse?> GetFilesAsync(string? path = null, CancellationToken ct = default)
  {
    var url = "/api/files";
    if (!string.IsNullOrWhiteSpace(path))
    {
      url += $"?path={Uri.EscapeDataString(path)}";
    }

    var response = await _httpClient.GetAsync(url, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<FileBrowserResponse>(_jsonOptions, ct);
  }

  #endregion

  #region System API

  /// <summary>
  /// Gets system statistics (CPU, RAM, etc.).
  /// </summary>
  public async Task<SystemStatsResponse?> GetSystemStatsAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/system/stats", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<SystemStatsResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Gets system logs.
  /// </summary>
  public async Task<SystemLogsResponse?> GetSystemLogsAsync(string? level = null, int? limit = null, CancellationToken ct = default)
  {
    var query = new List<string>();
    if (!string.IsNullOrEmpty(level))
    {
      query.Add($"level={level}");
    }

    if (limit.HasValue)
    {
      query.Add($"limit={limit.Value}");
    }

    var url = "/api/system/logs";
    if (query.Count > 0)
    {
      url += "?" + string.Join("&", query);
    }

    var response = await _httpClient.GetAsync(url, ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<SystemLogsResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Initiates a graceful shutdown of the application.
  /// </summary>
  public async Task<bool> ShutdownAsync(CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/system/shutdown", null, ct);
      response.EnsureSuccessStatusCode();
      return true;
    }
    catch
    {
      return false;
    }
  }

  #endregion

  #region Configuration API

  /// <summary>
  /// Gets all configuration entries.
  /// </summary>
  public async Task<ConfigurationResponse?> GetAllConfigurationAsync(CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync("/api/configuration", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<ConfigurationResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Gets configuration for a specific section.
  /// </summary>
  public async Task<ConfigurationSectionResponse?> GetConfigurationSectionAsync(string section, CancellationToken ct = default)
  {
    var response = await _httpClient.GetAsync($"/api/configuration/{section}", ct);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<ConfigurationSectionResponse>(_jsonOptions, ct);
  }

  /// <summary>
  /// Updates configuration for a section.
  /// </summary>
  public async Task<bool> UpdateConfigurationSectionAsync(string section, Dictionary<string, object> settings, CancellationToken ct = default)
  {
    var response = await _httpClient.PostAsJsonAsync($"/api/configuration/{section}", settings, _jsonOptions, ct);
    response.EnsureSuccessStatusCode();
    return response.IsSuccessStatusCode;
  }

  #endregion

  public void Dispose()
  {
    _httpClient.Dispose();
  }
}

#region Response DTOs

public class AvailableSourcesResponse
{
  public List<string> PrimarySources { get; set; } = new();
  public string? ActiveSourceType { get; set; }
  public List<AudioSourceResponse> ActiveSources { get; set; } = new();

  /// <summary>
  /// All available sources (alias for ActiveSources for compatibility).
  /// </summary>
  public List<AudioSourceResponse> Sources => ActiveSources;
}

public class AudioSourceResponse
{
  public string Id { get; set; } = "";
  public string Name { get; set; } = "";
  public string Type { get; set; } = "";
  public string Category { get; set; } = "";
  public string State { get; set; } = "";
  public float Volume { get; set; }
  public bool IsSeekable { get; set; }
  public Dictionary<string, object>? Metadata { get; set; }
  public bool IsRadio { get; set; }
  public bool IsStreaming { get; set; }
  public bool HasQueue { get; set; }
}

public class PlaybackStateResponse
{
  public bool IsPlaying { get; set; }
  public bool IsPaused { get; set; }
  public float Volume { get; set; }
  public bool IsMuted { get; set; }
  public float Balance { get; set; }
  public TimeSpan? Position { get; set; }
  public TimeSpan? Duration { get; set; }
  public AudioSourceResponse? ActiveSource { get; set; }
  public bool CanNext { get; set; }
  public bool CanPrevious { get; set; }
  public bool CanShuffle { get; set; }
  public bool CanRepeat { get; set; }
  public bool CanQueue { get; set; }
  public bool CanSeek { get; set; }
  public bool IsShuffleEnabled { get; set; }
  public string? RepeatMode { get; set; }

  /// <summary>
  /// Gets the computed state as a string for test comparisons.
  /// </summary>
  public string State => IsPlaying ? "Playing" : IsPaused ? "Paused" : "Stopped";
}

public class VolumeResponse
{
  public float Volume { get; set; }
  public bool IsMuted { get; set; }
  public float Balance { get; set; }
}

public class MuteResponse
{
  public bool IsMuted { get; set; }
}

public class NowPlayingResponse
{
  public string SourceType { get; set; } = "";
  public string SourceName { get; set; } = "";
  public bool IsPlaying { get; set; }
  public bool IsPaused { get; set; }
  public TimeSpan Position { get; set; }
  public TimeSpan Duration { get; set; }
  public double? ProgressPercentage { get; set; }
  public string? Title { get; set; }
  public string? Artist { get; set; }
  public string? Album { get; set; }
  public string? AlbumArtUrl { get; set; }
  public string? FilePath { get; set; }
  public string? State { get; set; }
  public Dictionary<string, object>? ExtendedMetadata { get; set; }
}

public class QueueItemResponse
{
  public string Id { get; set; } = "";
  public string? Title { get; set; }
  public string? Artist { get; set; }
  public string? Album { get; set; }
  public TimeSpan? Duration { get; set; }
  public string? AlbumArtUrl { get; set; }
  public int Index { get; set; }
  public bool IsCurrent { get; set; }

  /// <summary>
  /// Gets the file path (uses Id which contains the full path).
  /// </summary>
  public string FilePath => Id;
}

public class AudioDeviceResponse
{
  public string Id { get; set; } = "";
  public string Name { get; set; } = "";
  public string Type { get; set; } = "";
  public bool IsDefault { get; set; }
  public bool IsUSBDevice { get; set; }
  public string? USBPort { get; set; }
  public int MaxChannels { get; set; }
  public int[]? SupportedSampleRates { get; set; }
}

public class RadioStateResponse
{
  public double Frequency { get; set; }
  public string Band { get; set; } = "";
  public double FrequencyStep { get; set; }
  public int SignalStrength { get; set; }
  public bool IsStereo { get; set; }
  public string EqualizerMode { get; set; } = "";
  public int DeviceVolume { get; set; }
  public bool IsScanning { get; set; }
  public string? ScanDirection { get; set; }
  public bool AutoGainEnabled { get; set; }
  public double Gain { get; set; }
  public bool IsRunning { get; set; }
}

public class RadioDeviceListResponse
{
  public List<RadioDeviceInfo> Devices { get; set; } = new();
  public int Count { get; set; }
}

public class RadioDeviceInfo
{
  public string DeviceType { get; set; } = "";
  public bool IsAvailable { get; set; }
  public bool IsActive { get; set; }
}

public class SpotifySearchResponse
{
  public List<SpotifyTrackDto> Tracks { get; set; } = new();
  public List<SpotifyAlbumDto> Albums { get; set; } = new();
  public List<SpotifyPlaylistDto> Playlists { get; set; } = new();
  public List<SpotifyArtistDto> Artists { get; set; } = new();
}

public class SpotifyTrackDto
{
  public string Id { get; set; } = "";
  public string Name { get; set; } = "";
  public string Artist { get; set; } = "";
  public string Album { get; set; } = "";
  public TimeSpan Duration { get; set; }
  public string Uri { get; set; } = "";
  public string? AlbumArtUrl { get; set; }
  public List<SpotifyArtistDto>? Artists { get; set; }
}

public class SpotifyAlbumDto
{
  public string Id { get; set; } = "";
  public string Name { get; set; } = "";
  public string Artist { get; set; } = "";
  public string? ImageUrl { get; set; }
  public string Uri { get; set; } = "";
}

public class SpotifyPlaylistDto
{
  public string Id { get; set; } = "";
  public string Name { get; set; } = "";
  public string Owner { get; set; } = "";
  public string? ImageUrl { get; set; }
  public int TrackCount { get; set; }
  public string Uri { get; set; } = "";
}

public class SpotifyArtistDto
{
  public string Id { get; set; } = "";
  public string Name { get; set; } = "";
  public string? ImageUrl { get; set; }
  public string Uri { get; set; } = "";
}

public class SpotifyAuthStatusResponse
{
  public bool IsAuthenticated { get; set; }
  public string? Username { get; set; }
  public string? DisplayName { get; set; }
  public DateTime? ExpiresAt { get; set; }
  public string? UserId { get; set; }
}

public class FileBrowserResponse
{
  public string CurrentPath { get; set; } = "";
  public List<FileEntry> Files { get; set; } = new();
  public List<DirectoryEntry> Directories { get; set; } = new();
}

public class FileEntry
{
  public string Name { get; set; } = "";
  public string Path { get; set; } = "";
  public long Size { get; set; }
}

public class DirectoryEntry
{
  public string Name { get; set; } = "";
  public string Path { get; set; } = "";
}

/// <summary>
/// Playback actions for the audio API.
/// </summary>
public enum PlaybackAction
{
  None = 0,
  Play = 1,
  Pause = 2,
  Stop = 3,
  Seek = 4
}

public class UpdatePlaybackRequest
{
  public PlaybackAction Action { get; set; }
  public float? Volume { get; set; }
  public float? Balance { get; set; }
  public bool? IsMuted { get; set; }
  public TimeSpan? SeekPosition { get; set; }
}

public class SystemStatsResponse
{
  public double CpuUsagePercent { get; set; }
  public long MemoryUsedBytes { get; set; }
  public long MemoryTotalBytes { get; set; }
  public int ThreadCount { get; set; }
  public TimeSpan Uptime { get; set; }
  public double? CpuTemperature { get; set; }
}

public class SystemLogsResponse
{
  public List<LogEntry> Logs { get; set; } = new();
  public int TotalCount { get; set; }
}

public class LogEntry
{
  public DateTime Timestamp { get; set; }
  public string Level { get; set; } = "";
  public string Message { get; set; } = "";
  public string? Source { get; set; }
}

public class ConfigurationResponse
{
  public Dictionary<string, object> Configuration { get; set; } = new();
}

public class ConfigurationSectionResponse
{
  public string Section { get; set; } = "";
  public Dictionary<string, object> Settings { get; set; } = new();
}

#endregion
