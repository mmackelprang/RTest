using System.Net.Http.Json;
using System.Text.Json;
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
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public DevicesApiService(HttpClient httpClient, ILogger<DevicesApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<List<AudioDeviceDto>?> GetOutputDevicesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<List<AudioDeviceDto>>("/api/devices/output", JsonOptions, cancellationToken);
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
      return await _httpClient.GetFromJsonAsync<List<AudioDeviceDto>>("/api/devices/input", JsonOptions, cancellationToken);
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
      // Uses /output/default endpoint - returns the default/current output device
      return await _httpClient.GetFromJsonAsync<AudioDeviceDto>("/api/devices/output/default", JsonOptions, cancellationToken);
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
      // API expects JSON body with DeviceId property
      var request = new { DeviceId = deviceId };
      var response = await _httpClient.PostAsJsonAsync("/api/devices/output", request, cancellationToken);
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
      return await _httpClient.GetFromJsonAsync<List<UsbPortDto>>("/api/devices/usb", JsonOptions, cancellationToken);
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
      return await _httpClient.GetFromJsonAsync<UsbPortDto>($"/api/devices/usb/{portId}", JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get USB port");
      return null;
    }
  }

  /// <summary>
  /// Gets cached Google Cast devices (no mDNS discovery, instant).
  /// </summary>
  public async Task<List<CastDeviceDto>?> GetCachedCastDevicesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.GetAsync("/api/devices/cast/cached", cancellationToken);

      if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
      {
        _logger.LogWarning("Google Cast output is not available (503)");
        return new List<CastDeviceDto>();
      }

      if (!response.IsSuccessStatusCode)
      {
        _logger.LogWarning("Failed to get cached Cast devices: {StatusCode}", response.StatusCode);
        return null;
      }

      return await response.Content.ReadFromJsonAsync<List<CastDeviceDto>>(JsonOptions, cancellationToken)
        ?? new List<CastDeviceDto>();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get cached Cast devices");
      return null;
    }
  }

  /// <summary>
  /// Gets the default Cast device preference.
  /// </summary>
  public async Task<CastDeviceDto?> GetDefaultCastDeviceAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.GetAsync("/api/devices/cast/default", cancellationToken);
      if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        return null;
      if (!response.IsSuccessStatusCode)
        return null;

      return await response.Content.ReadFromJsonAsync<CastDeviceDto>(JsonOptions, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get default Cast device");
      return null;
    }
  }

  /// <summary>
  /// Sets a Cast device as the default for auto-connect.
  /// </summary>
  public async Task<bool> SetDefaultCastDeviceAsync(CastDeviceDto device, CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/devices/cast/default", device, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to set default Cast device");
      return false;
    }
  }

  /// <summary>
  /// Clears the default Cast device preference.
  /// </summary>
  public async Task<bool> ClearDefaultCastDeviceAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.DeleteAsync("/api/devices/cast/default", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to clear default Cast device");
      return false;
    }
  }

  /// <summary>
  /// Discovers available Google Cast devices on the network.
  /// </summary>
  public async Task<List<CastDeviceDto>?> DiscoverCastDevicesAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await _httpClient.GetAsync("/api/devices/cast", cancellationToken);

      if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
      {
        _logger.LogWarning("Google Cast output is not available (503)");
        return new List<CastDeviceDto>(); // Return empty list, not null
      }

      if (!response.IsSuccessStatusCode)
      {
        _logger.LogWarning("Failed to discover Cast devices: {StatusCode}", response.StatusCode);
        return null;
      }

      var devices = await response.Content.ReadFromJsonAsync<List<CastDeviceDto>>(JsonOptions, cancellationToken);
      return devices ?? new List<CastDeviceDto>();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to discover Cast devices");
      return null;
    }
  }

  /// <summary>
  /// Connects to a specific Google Cast device.
  /// </summary>
  public async Task<bool> ConnectToCastDeviceAsync(CastDeviceDto device, CancellationToken cancellationToken = default)
  {
    try
    {
      var request = new ConnectCastDeviceRequest(
        device.Id,
        device.Name,
        device.IpAddress,
        device.Port,
        device.Model);
      var response = await _httpClient.PostAsJsonAsync("/api/devices/cast/connect", request, cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to connect to Cast device: {DeviceName}", device.Name);
      return false;
    }
  }
}
