using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;

namespace Radio.Infrastructure.Configuration;

/// <summary>
/// Resolves <see cref="DeviceOptions"/> by reading from the config store first,
/// falling back to <see cref="IOptionsMonitor{DeviceOptions}"/> (appsettings.json).
/// This ensures that values saved via the System Config UI (SQLite store) are
/// picked up at runtime without requiring an appsettings.Production.json override.
/// </summary>
public class DeviceOptionsResolver
{
  private readonly ILogger<DeviceOptionsResolver> _logger;
  private readonly IOptionsMonitor<DeviceOptions> _optionsMonitor;
  private readonly IConfigurationManager? _configManager;

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNameCaseInsensitive = true
  };

  public DeviceOptionsResolver(
    ILogger<DeviceOptionsResolver> logger,
    IOptionsMonitor<DeviceOptions> optionsMonitor,
    IConfigurationManager? configManager = null)
  {
    _logger = logger;
    _optionsMonitor = optionsMonitor;
    _configManager = configManager;
  }

  /// <summary>
  /// Gets the resolved <see cref="DeviceOptions"/>, reading from the config store first.
  /// Falls back to IOptionsMonitor if the config store is unavailable or has no values.
  /// </summary>
  public async Task<DeviceOptions> GetDeviceOptionsAsync(CancellationToken ct = default)
  {
    var fallback = _optionsMonitor.CurrentValue;

    if (_configManager == null)
    {
      return fallback;
    }

    try
    {
      var storeId = _configManager.CurrentStoreType == ConfigurationStoreType.Sqlite
        ? "sqlite" : "config";

      var result = new DeviceOptions
      {
        Radio = await ResolveNestedAsync<RadioDeviceOptions>(storeId, "devices:Radio", ct)
                ?? fallback.Radio ?? new RadioDeviceOptions(),
        Vinyl = await ResolveNestedAsync<VinylDeviceOptions>(storeId, "devices:Vinyl", ct)
                ?? fallback.Vinyl ?? new VinylDeviceOptions(),
      };

      return result;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to read DeviceOptions from config store, using appsettings fallback");
      return fallback;
    }
  }

  /// <summary>
  /// Gets just the Radio USB port, reading from config store first.
  /// </summary>
  public async Task<string> GetRadioUSBPortAsync(CancellationToken ct = default)
  {
    var options = await GetDeviceOptionsAsync(ct);
    return options.Radio?.USBPort ?? "";
  }

  /// <summary>
  /// Gets just the Vinyl USB port, reading from config store first.
  /// </summary>
  public async Task<string> GetVinylUSBPortAsync(CancellationToken ct = default)
  {
    var options = await GetDeviceOptionsAsync(ct);
    return options.Vinyl?.USBPort ?? "";
  }

  /// <summary>
  /// Gets the Generic USB port, reading from config store first.
  /// GenericSourcePreferences is a flat object, so the key is "genericsourcepreferences:USBPort".
  /// </summary>
  public async Task<string> GetGenericUSBPortAsync(CancellationToken ct = default)
  {
    if (_configManager == null)
    {
      return "";
    }

    try
    {
      var storeId = _configManager.CurrentStoreType == ConfigurationStoreType.Sqlite
        ? "sqlite" : "config";
      var value = await _configManager.GetValueAsync<string>(storeId, "genericsourcepreferences:USBPort", ct: ct);
      if (!string.IsNullOrWhiteSpace(value))
      {
        return value;
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to read GenericUSB port from config store");
    }

    return "";
  }

  /// <summary>
  /// Reads a nested object from the config store, where the value is stored as
  /// serialized JSON (e.g., key "devices:Radio" → value '{"usbPort":"AB13X"}').
  /// Tries the exact key first, then a lowercase variant, since the config controller
  /// preserves the casing from the JSON payload.
  /// </summary>
  private async Task<T?> ResolveNestedAsync<T>(string storeId, string key, CancellationToken ct)
    where T : class
  {
    // Try exact key first (e.g., "devices:Radio"), then lowercase (e.g., "devices:radio")
    var raw = await _configManager!.GetValueAsync<string>(storeId, key, ct: ct);
    if (string.IsNullOrWhiteSpace(raw))
    {
      var lowerKey = key.ToLowerInvariant();
      if (lowerKey != key)
      {
        raw = await _configManager.GetValueAsync<string>(storeId, lowerKey, ct: ct);
      }
    }

    if (string.IsNullOrWhiteSpace(raw))
    {
      return null;
    }

    try
    {
      return JsonSerializer.Deserialize<T>(raw, JsonOptions);
    }
    catch (JsonException ex)
    {
      _logger.LogWarning(ex, "Failed to deserialize config store value for key '{Key}': {Value}", key, raw);
      return null;
    }
  }
}
