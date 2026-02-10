using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radio.Web.Models;

namespace Radio.Web.Services.ApiClients;

/// <summary>
/// API client service for Bluetooth management endpoints.
/// </summary>
public class BluetoothApiService
{
  private readonly HttpClient _httpClient;
  private readonly ILogger<BluetoothApiService> _logger;
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public BluetoothApiService(HttpClient httpClient, ILogger<BluetoothApiService> logger)
  {
    _httpClient = httpClient;
    _logger = logger;
  }

  public async Task<BluetoothStatusDto?> GetStatusAsync(CancellationToken ct = default)
  {
    try
    {
      return await _httpClient.GetFromJsonAsync<BluetoothStatusDto>("/api/bluetooth/status", JsonOptions, ct);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to get Bluetooth status");
      return null;
    }
  }

  public async Task<bool> StartAsync(string? deviceName = null, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/bluetooth/start",
        new { DeviceName = deviceName }, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start Bluetooth adapter");
      return false;
    }
  }

  public async Task<bool> StopAsync(CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/bluetooth/stop", null, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to stop Bluetooth adapter");
      return false;
    }
  }

  public async Task<bool> StartDiscoveryAsync(CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/bluetooth/discovery/start", null, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to start Bluetooth discovery");
      return false;
    }
  }

  public async Task<bool> StopDiscoveryAsync(CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/bluetooth/discovery/stop", null, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to stop Bluetooth discovery");
      return false;
    }
  }

  public async Task<bool> PairAsync(string deviceAddress, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/bluetooth/pair",
        new { DeviceAddress = deviceAddress }, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to pair Bluetooth device {Address}", deviceAddress);
      return false;
    }
  }

  public async Task<bool> UnpairAsync(string deviceAddress, CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsJsonAsync("/api/bluetooth/unpair",
        new { DeviceAddress = deviceAddress }, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to unpair Bluetooth device {Address}", deviceAddress);
      return false;
    }
  }

  public async Task<bool> DisconnectAsync(CancellationToken ct = default)
  {
    try
    {
      var response = await _httpClient.PostAsync("/api/bluetooth/disconnect", null, ct);
      return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to disconnect Bluetooth device");
      return false;
    }
  }
}
