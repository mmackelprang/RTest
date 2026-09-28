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

  private static bool IsRadioSource(string source) =>
    source == "Radio" || source.StartsWith("Radio.", StringComparison.Ordinal);

  private static bool IsDebugOrMoreVerbose(string level) =>
    string.Equals(level, "Debug", StringComparison.OrdinalIgnoreCase)
    || string.Equals(level, "Verbose", StringComparison.OrdinalIgnoreCase);
}
