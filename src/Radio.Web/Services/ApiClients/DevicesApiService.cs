using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for audio device management endpoints (7 endpoints)
/// </summary>
public class DevicesApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<DevicesApiService> _logger;

  public DevicesApiService(HttpClient httpClient, ILogger<DevicesApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<List<AudioDeviceDto>?> GetOutputDevicesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<AudioDeviceDto>>("/api/devices/output", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get output devices");
      return null;
    }
  }

  public async Task<List<AudioDeviceDto>?> GetInputDevicesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<AudioDeviceDto>>("/api/devices/input", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get input devices");
      return null;
    }
  }

  public async Task<AudioDeviceDto?> GetCurrentOutputDeviceAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<AudioDeviceDto>("/api/devices/output/current", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get current output device");
      return null;
    }
  }

  public async Task<bool> SetOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync($"/api/devices/output/{deviceId}", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set output device");
      return false;
    }
  }

  public async Task<bool> RefreshDevicesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/devices/refresh", null, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to refresh devices");
      return false;
    }
  }

  public async Task<List<UsbPortDto>?> GetUsbPortsAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<UsbPortDto>>("/api/devices/usb", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get USB ports");
      return null;
    }
  }

  public async Task<UsbPortDto?> GetUsbPortAsync(string portId, CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<UsbPortDto>($"/api/devices/usb/{portId}", cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get USB port");
      return null;
    }
  }
}
