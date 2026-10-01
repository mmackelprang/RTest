using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for system management endpoints (2 endpoints)
/// </summary>
public class SystemApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<SystemApiService> _logger;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public SystemApiService(HttpClient httpClient, ILogger<SystemApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<SystemStatsDto?> GetSystemStatsAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<SystemStatsDto>("/api/system/stats", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get system stats");
      return null;
    }
  }

  public async Task<SystemLogsResponse?> GetSystemLogsAsync(string? level = "warning", int? limit = 100, int? maxAgeMinutes = null, CancellationToken cancellationToken = default)
  {
    try
    {
      var queryParams = new List<string>();
      if (!string.IsNullOrEmpty(level))
      {
        queryParams.Add($"level={level}");
      }

      if (limit.HasValue)
      {
        queryParams.Add($"limit={limit.Value}");
      }

      if (maxAgeMinutes.HasValue)
      {
        queryParams.Add($"maxAgeMinutes={maxAgeMinutes.Value}");
      }

      var query = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
      return await _httpClient.GetFromJsonAsync<SystemLogsResponse>($"/api/system/logs{query}", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get system logs");
      return null;
    }
  }

  public async Task<bool> SetSleepAsync(bool sleep, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/system/sleep", new { sleep }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set sleep state to {Sleep}", sleep);
      return false;
    }
  }

  /// <summary>
  /// Reports whether the <c>/sleep</c> route is on screen, and returns the resulting state.
  /// </summary>
  /// <remarks>
  /// Returns <c>null</c> on any failure, and every caller must render correctly from that: the
  /// bUnit rig fails every outbound request by design, and the kiosk can call this while the API is
  /// still starting. Failing means the caller keeps its default, which is the Ambient copy.
  /// </remarks>
  public async Task<SleepStateDto?> SetSleepScreenVisibleAsync(
    bool visible,
    CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync(
        "/api/system/sleep-screen", new { visible }, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        return null;
      }

      return await response.Content.ReadFromJsonAsync<SleepStateDto>(JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to report sleep screen visibility {Visible}", visible);
      return null;
    }
  }

  public async Task<bool> PowerOffSystemAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/system/poweroff", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to power off system");
      return false;
    }
  }

  public async Task<bool> RestartServicesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/system/restart-services", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to restart services");
      return false;
    }
  }

  // ── LOG-5: runtime log levels on radio-api ────────────────────────────────────────────────────
  // These change radio-api's emission levels only; radio-web's own logging is not switchable.

  /// <summary>Every switchable source on radio-api, or null if the API could not be reached.</summary>
  public async Task<IReadOnlyList<LogLevelStateDto>?> GetLogLevelsAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<LogLevelStateDto>>("/api/system/logging/levels", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get log levels");
      return null;
    }
  }

  /// <summary>Sets one source's level on radio-api.</summary>
  public async Task<bool> SetLogLevelAsync(string source, string level, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PutAsJsonAsync(
        $"/api/system/logging/levels/{Uri.EscapeDataString(source)}", new { level }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set log level");
      return false;
    }
  }

  /// <summary>Returns every radio-api switch to its configured level.</summary>
  public async Task<bool> ResetLogLevelsAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/system/logging/levels/reset", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to reset log levels");
      return false;
    }
  }

  /// <summary>
  /// The DevTray's one-tap "verbose logs": lowers every <c>Radio</c> / <c>Radio.*</c> switch to Debug.
  /// Switches already at Debug or Verbose are left alone, and <c>Default</c> (Microsoft and other
  /// third-party chatter) is not touched. Returns false if the levels could not be read, no Radio
  /// switch exists, or any PUT failed.
  /// </summary>
  public async Task<bool> EnableVerboseRadioLoggingAsync(CancellationToken cancellationToken = default)
  {
    var levels = await GetLogLevelsAsync(cancellationToken);
    if (levels is null)
    {
      return false;
    }

    var radio = levels.Where(l => IsRadioSource(l.Source)).ToList();
    if (radio.Count == 0)
    {
      return false;
    }

    var ok = true;
    foreach (var state in radio.Where(l => !IsDebugOrMoreVerbose(l.Level)))
    {
      ok &= await SetLogLevelAsync(state.Source, "Debug", cancellationToken);
    }
    return ok;
  }

  /// <summary>True when any radio-api switch is running at something other than its configured level.</summary>
  public static bool IsRuntimeModified(IReadOnlyList<LogLevelStateDto> levels) =>
    levels.Any(l => !string.Equals(l.Level, l.ConfiguredLevel, StringComparison.OrdinalIgnoreCase));

  // ── UI-26: the DevTray's file-shaped diagnostics ───────────────────────────────────────────────
  // These used to be window.open()ed straight at radio-api, which on the kiosk (no tabs, no address
  // bar, no back button) left the panel on a page it could not leave. They are now fetched here, on
  // the server side of the circuit, so the browser is never sent to radio-api: the tray shows the
  // outcome and, on success, the page saves the bytes as a blob download.

  /// <summary>
  /// Longest error text returned for display. A tray card's status line is clamped to three lines of
  /// about 25 monospace characters, and the card prefixes the status code (<c>"501 · "</c>), so 64 fits.
  /// </summary>
  internal const int MaxErrorLength = 64;

  /// <summary>Fetches radio-api's zipped log files for <paramref name="period"/> (<c>5m</c>/<c>1h</c>/<c>24h</c>/<c>7d</c>).</summary>
  public Task<ApiFileResult> DownloadLogsAsync(string period = "1h", CancellationToken cancellationToken = default) =>
    FetchFileAsync($"/api/system/logs/download?period={Uri.EscapeDataString(period)}", "radio-logs.zip", cancellationToken);

  /// <summary>
  /// Asks radio-api for an audio-frame dump. As of UI-26 the endpoint is a deliberate
  /// <c>501 Not Implemented</c> stub (<c>AudioDebugController.DumpAudioFrame</c>), so against today's
  /// API this returns a failure carrying the API's own explanation.
  /// </summary>
  public Task<ApiFileResult> DumpAudioFrameAsync(CancellationToken cancellationToken = default) =>
    FetchFileAsync("/api/audio/debug/dump-frame", "audio-frame.wav", cancellationToken);

  private async Task<ApiFileResult> FetchFileAsync(string path, string fallbackFileName, CancellationToken cancellationToken)
  {
    try
    {
      using var response = await _httpClient.GetAsync(path, HttpCompletionOption.ResponseContentRead, cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        var reason = await ReadErrorAsync(response, cancellationToken);
        return ApiFileResult.Failed((int)response.StatusCode, reason);
      }

      var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
      var disposition = response.Content.Headers.ContentDisposition;
      var name = (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"');
      return ApiFileResult.Ok((int)response.StatusCode, SafeFileName(name, fallbackFileName), content);
    }
    catch (OperationCanceledException)
    {
      // Either the caller's token, or HttpClient's own 30 s timeout — which surfaces as a
      // TaskCanceledException with the caller's token NOT cancelled. Both mean the API was too slow.
      return ApiFileResult.Failed(null, "timed out waiting for radio-api");
    }
    catch (HttpRequestException ex)
    {
      _logger.LogWarning(ex, "DevTray file fetch failed");
      return ApiFileResult.Failed(null, "radio-api unreachable");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "DevTray file fetch failed");
      return ApiFileResult.Failed(null, "request to radio-api failed");
    }
  }

  /// <summary>
  /// A short reason for a failed response: the body's <c>error</c> field (this API's convention), else a
  /// problem-details <c>title</c>, else the status phrase. Only those two named fields are read — other
  /// body fields (the logs 404 carries a server path) are never shown.
  /// </summary>
  private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
  {
    string? message = null;
    try
    {
      var body = await response.Content.ReadAsStringAsync(cancellationToken);
      if (!string.IsNullOrWhiteSpace(body))
      {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
          foreach (var field in new[] { "error", "title" })
          {
            if (doc.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
            {
              message = value.GetString();
              break;
            }
          }
        }
      }
    }
    catch (Exception)
    {
      // Not JSON, or the body could not be read — fall back to the status phrase.
    }

    if (string.IsNullOrWhiteSpace(message))
    {
      message = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? "request failed" : response.ReasonPhrase;
    }
    // The tray's own lines carry no full stop; drop the API's so the two read as one voice.
    message = message.Trim().TrimEnd('.');
    return message.Length <= MaxErrorLength ? message : message[..(MaxErrorLength - 1)] + "…";
  }

  // The name arrives in a response header and ends up as an <a download> attribute; keep only a
  // bare file name.
  private static string SafeFileName(string? name, string fallback)
  {
    if (string.IsNullOrWhiteSpace(name))
    {
      return fallback;
    }
    var bare = Path.GetFileName(name.Replace('\\', '/'));
    return string.IsNullOrWhiteSpace(bare) ? fallback : bare;
  }

  private static bool IsRadioSource(string source) =>
    source == "Radio" || source.StartsWith("Radio.", StringComparison.Ordinal);

  private static bool IsDebugOrMoreVerbose(string level) =>
    string.Equals(level, "Debug", StringComparison.OrdinalIgnoreCase)
    || string.Equals(level, "Verbose", StringComparison.OrdinalIgnoreCase);
}
