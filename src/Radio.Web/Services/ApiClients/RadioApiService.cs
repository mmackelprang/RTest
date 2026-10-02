using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for radio control endpoints (23 endpoints)
/// </summary>
public class RadioApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<RadioApiService> _logger;

  public RadioApiService(HttpClient httpClient, ILogger<RadioApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public Task<IEnumerable<Radio.Core.Models.RadioBandModel>> GetBandsAsync(CancellationToken cancellationToken = default) =>
    GetBandsCoreAsync(LogLevel.Error, cancellationToken);

  /// <summary>
  /// <see cref="GetBandsAsync"/> for a poll: a failure logs at Debug, because the visualizer's band map view
  /// (UI-31) retries it on its 30 s refresh until one read succeeds, and <c>radio-web</c>'s Information and
  /// above reach journald — the same reasoning as <see cref="GetPresetsForPollAsync"/>.
  /// </summary>
  public Task<IEnumerable<Radio.Core.Models.RadioBandModel>> GetBandsForPollAsync(CancellationToken cancellationToken = default) =>
    GetBandsCoreAsync(LogLevel.Debug, cancellationToken);

  private async Task<IEnumerable<Radio.Core.Models.RadioBandModel>> GetBandsCoreAsync(LogLevel failureLevel, CancellationToken cancellationToken)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<IEnumerable<Radio.Core.Models.RadioBandModel>>("/api/RadioBands", cancellationToken) ?? Enumerable.Empty<Radio.Core.Models.RadioBandModel>();
    }
    catch (Exception ex)
    {
      _logger.Log(failureLevel, ex, "Failed to get radio bands");
      return Enumerable.Empty<Radio.Core.Models.RadioBandModel>();
    }
  }

  public async Task<RadioStateDto?> GetStateAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.GetAsync("/api/radio/state", cancellationToken);

      // 400 is expected when radio is not the active source - don't log as error
      if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
      {
        _logger.LogDebug("Radio is not the active source");
        return null;
      }

      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<RadioStateDto>(cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get radio state");
      return null;
    }
  }

  /// <summary>
  /// Reads <c>GET /api/radio/state</c>, keeping "the radio is not the active source" (400) apart from
  /// "the state could not be read" (anything else). Used by the visualizer's BAND view (AUD-76), which
  /// switches source only on the first and must not on the second.
  /// </summary>
  /// <remarks>
  /// Failures log at Debug, not Error: BAND polls this once a second while a sweep runs, and on
  /// <c>radio-web</c> every level at Information and above reaches journald.
  /// </remarks>
  public async Task<RadioStateRead> ReadStateAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      using HttpResponseMessage response = await _httpClient.GetAsync("/api/radio/state", cancellationToken);
      if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
      {
        return new RadioStateRead(RadioStateReadStatus.NotActive, null);
      }

      if (!response.IsSuccessStatusCode)
      {
        _logger.LogDebug("Radio state read answered {StatusCode}", (int)response.StatusCode);
        return new RadioStateRead(RadioStateReadStatus.Unavailable, null);
      }

      RadioStateDto? state = await response.Content.ReadFromJsonAsync<RadioStateDto>(cancellationToken);
      return state == null
        ? new RadioStateRead(RadioStateReadStatus.Unavailable, null)
        : new RadioStateRead(RadioStateReadStatus.Active, state);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Could not read the radio state");
      return new RadioStateRead(RadioStateReadStatus.Unavailable, null);
    }
  }

  /// <summary>
  /// Tunes the radio (<c>POST /api/radio/frequency</c>). With <paramref name="band"/>, the API tunes inside
  /// that band in one retune (AUD-91); without it the body is <c>{"frequency":…}</c> alone, as before, and
  /// the API infers the band from the frequency.
  /// </summary>
  public async Task<bool> SetFrequencyAsync(double frequency, string? band = null, CancellationToken cancellationToken = default)
  {
    try
    {
      // Two shapes rather than one with a null band: the band-less body stays byte-for-byte what it was.
      HttpResponseMessage response = band == null
        ? await _httpClient.PostAsJsonAsync("/api/radio/frequency", new { frequency }, cancellationToken)
        : await _httpClient.PostAsJsonAsync("/api/radio/frequency", new { frequency, band }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set frequency");
      return false;
    }
  }

  public async Task<bool> FrequencyUpAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/radio/frequency/up", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to increase frequency");
      return false;
    }
  }

  public async Task<bool> FrequencyDownAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/radio/frequency/down", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to decrease frequency");
      return false;
    }
  }

  public async Task<bool> SetBandAsync(string band, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/radio/band", new { band }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set band");
      return false;
    }
  }

  public async Task<bool> SetStepAsync(double step, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/radio/step", new { step }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set step");
      return false;
    }
  }

  public async Task<bool> StartScanAsync(string direction, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/radio/scan/start", new { direction }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start scan");
      return false;
    }
  }

  public async Task<bool> StopScanAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/radio/scan/stop", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to stop scan");
      return false;
    }
  }

  public async Task<bool> SetGainAsync(int gain, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/radio/gain", new { gain }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set gain");
      return false;
    }
  }

  public async Task<bool> SetAutoGainAsync(bool enabled, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/radio/gain/auto", new { enabled }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set auto gain");
      return false;
    }
  }

  public async Task<bool> SetEqualizerAsync(string preset, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/radio/eq", new { preset }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set equalizer");
      return false;
    }
  }

  public async Task<bool> SetDeviceVolumeAsync(int volume, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/radio/volume", new { volume }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set device volume");
      return false;
    }
  }

  public async Task<RadioPowerStateDto?> GetPowerStateAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<RadioPowerStateDto>("/api/radio/power", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get power state");
      return null;
    }
  }

  public async Task<bool> TogglePowerAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/radio/power/toggle", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to toggle power");
      return false;
    }
  }

  public async Task<bool> StartupAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/radio/startup", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to startup radio");
      return false;
    }
  }

  public async Task<bool> ShutdownAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/radio/shutdown", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to shutdown radio");
      return false;
    }
  }

  public Task<List<RadioPresetDto>?> GetPresetsAsync(CancellationToken cancellationToken = default) =>
    GetPresetsCoreAsync(LogLevel.Error, cancellationToken);

  /// <summary>
  /// <see cref="GetPresetsAsync"/> for a poll: a failure logs at Debug, because the visualizer's BAND
  /// view (AUD-76) re-reads the presets every 30 s and <c>radio-web</c>'s Information and above reach
  /// journald.
  /// </summary>
  public Task<List<RadioPresetDto>?> GetPresetsForPollAsync(CancellationToken cancellationToken = default) =>
    GetPresetsCoreAsync(LogLevel.Debug, cancellationToken);

  private async Task<List<RadioPresetDto>?> GetPresetsCoreAsync(LogLevel failureLevel, CancellationToken cancellationToken)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<RadioPresetDto>>("/api/radio/presets", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.Log(failureLevel, ex, "Failed to get presets");
      return null;
    }
  }

  public async Task<bool> SavePresetAsync(string name, double frequency, string band, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/radio/presets", new { name, frequency, band }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to save preset");
      return false;
    }
  }

  public async Task<bool> LoadPresetAsync(string id, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync($"/api/radio/presets/{id}/load", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to load preset");
      return false;
    }
  }

  public async Task<bool> DeletePresetAsync(string id, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync($"/api/radio/presets/{id}", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to delete preset");
      return false;
    }
  }

  /// <summary>
  /// Renames an existing preset. Added by the LED-font + preset-affordances
  /// hot-fix off PR #371 so the kebab-menu Rename action can call PUT
  /// /api/radio/presets/{id} with the new name. Returns the new name on
  /// success so the caller can echo it into a toast without re-fetching.
  /// </summary>
  public async Task<bool> RenamePresetAsync(string id, string newName, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(newName))
    {
      return false;
    }

    try
    {
      var response = await _httpClient.PutAsJsonAsync($"/api/radio/presets/{id}", new { name = newName.Trim() }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to rename preset {Id}", id);
      return false;
    }
  }

  /// <summary>
  /// Reads a band's stored map, its axis and the sweep status (<c>GET /api/radio/bandmap</c>, AUD-76;
  /// per band since AUD-91).
  /// </summary>
  /// <param name="band">Band code; null for the radio's current band, which the API resolves.</param>
  /// <param name="cancellationToken">Cancels the read.</param>
  /// <returns>The map, or null when the API could not be read.</returns>
  /// <remarks>
  /// A failure logs at Debug, not Error: the visualizer's BAND view polls this once a second while a
  /// sweep runs, and on <c>radio-web</c> every level at Information and above reaches journald.
  /// </remarks>
  public async Task<BandMapResponseDto?> GetBandMapAsync(string? band = null, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<BandMapResponseDto>(WithBand("/api/radio/bandmap", band), cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Could not read the band map");
      return null;
    }
  }

  /// <summary>
  /// Requests a band sweep (<c>POST /api/radio/bandmap/scan</c>, AUD-76). The API answers 202 when a
  /// sweep started or was already running, 409 when the band cannot be scanned on this tuner or the SDR
  /// device is busy, 503 when sweeps are disabled or there is no SDR device, and 400 for an unknown band;
  /// the failures carry an <c>error</c> message, returned here.
  /// </summary>
  /// <param name="band">Band code to sweep; null for the radio's current band, which the API resolves.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  public async Task<BandScanRequestResult> RequestBandScanAsync(string? band = null, CancellationToken cancellationToken = default)
  {
    try
    {
      using HttpResponseMessage response =
        await _httpClient.PostAsync(WithBand("/api/radio/bandmap/scan", band), null, cancellationToken);
      if (response.IsSuccessStatusCode)
      {
        return new BandScanRequestResult(true, (int)response.StatusCode, null);
      }

      return new BandScanRequestResult(false, (int)response.StatusCode, await ReadErrorAsync(response, cancellationToken));
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Band scan request failed");
      return new BandScanRequestResult(false, 0, null);
    }
  }

  /// <summary>Appends <c>?band=</c> when a band is given; the path alone otherwise.</summary>
  private static string WithBand(string path, string? band) =>
    band == null ? path : $"{path}?band={Uri.EscapeDataString(band)}";

  /// <summary>Reads <c>{"error": "..."}</c> from a failed response; null when there is none.</summary>
  private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
  {
    try
    {
      System.Text.Json.JsonElement body =
        await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken);
      if (body.ValueKind == System.Text.Json.JsonValueKind.Object
        && body.TryGetProperty("error", out System.Text.Json.JsonElement error)
        && error.ValueKind == System.Text.Json.JsonValueKind.String)
      {
        return error.GetString();
      }
    }
    catch (Exception)
    {
      // No body, or not JSON: the status code alone is the answer.
    }

    return null;
  }

  public async Task<List<RadioDeviceDto>?> GetAvailableDevicesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<RadioDeviceDto>>("/api/radio/devices", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get available radio devices");
      return null;
    }
  }

  public async Task<RadioDeviceDto?> GetDefaultDeviceAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<RadioDeviceDto>("/api/radio/devices/default", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get default radio device");
      return null;
    }
  }

  public async Task<bool> SelectDeviceAsync(string deviceType, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/radio/devices/select", new { deviceType }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to select radio device");
      return false;
    }
  }
}
