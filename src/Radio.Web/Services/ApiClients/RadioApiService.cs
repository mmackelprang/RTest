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

  public async Task<RadioStateDto?> GetStateAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<RadioStateDto>("/api/radio/state", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get radio state");
      return null;
    }
  }

  public async Task<bool> SetFrequencyAsync(double frequency, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/radio/frequency", new { frequency }, cancellationToken);
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

  public async Task<List<RadioPresetDto>?> GetPresetsAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<RadioPresetDto>>("/api/radio/presets", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get presets");
      return null;
    }
  }

  public async Task<bool> SavePresetAsync(int slot, string name, double frequency, string band, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/radio/presets", new { slot, name, frequency, band }, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to save preset");
      return false;
    }
  }

  public async Task<bool> LoadPresetAsync(int slot, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync($"/api/radio/presets/{slot}", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to load preset");
      return false;
    }
  }

  public async Task<bool> DeletePresetAsync(int slot, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync($"/api/radio/presets/{slot}", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to delete preset");
      return false;
    }
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
