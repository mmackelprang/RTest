using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Exceptions;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// SoundFlow implementation of audio device management.
/// Handles device enumeration and USB port reservations.
/// </summary>
public class SoundFlowDeviceManager : IAudioDeviceManager
{
  private readonly ILogger<SoundFlowDeviceManager> _logger;
  private readonly IConfigurationManager _configurationManager;
  private readonly IOptionsMonitor<AudioPreferences> _audioPreferences;
  private readonly IOptionsMonitor<AudioOutputOptions> _audioOutputOptionsMonitor;
  private DeviceDisplayOptions _displayOptions;
  private List<Regex> _hiddenPatterns;
  private readonly Dictionary<string, string> _usbPortReservations = new();
  private readonly object _reservationLock = new();
  private readonly object _devicesLock = new();

  private List<AudioDeviceInfo> _cachedOutputDevices = [];
  private List<AudioDeviceInfo> _cachedInputDevices = [];
  private string? _selectedOutputDeviceId;
  private string? _selectedInputDeviceId;

  /// <inheritdoc/>
  public event EventHandler<AudioDeviceChangedEventArgs>? DevicesChanged;

  /// <summary>
  /// Initializes a new instance of the <see cref="SoundFlowDeviceManager"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="configurationManager">The configuration manager.</param>
  /// <param name="audioPreferences">The audio preferences.</param>
  /// <param name="audioOutputOptions">The audio output options monitor (includes device display config).</param>
  public SoundFlowDeviceManager(
    ILogger<SoundFlowDeviceManager> logger,
    IConfigurationManager configurationManager,
    IOptionsMonitor<AudioPreferences> audioPreferences,
    IOptionsMonitor<AudioOutputOptions> audioOutputOptions)
  {
    _logger = logger;
    _configurationManager = configurationManager;
    _audioPreferences = audioPreferences;
    _audioOutputOptionsMonitor = audioOutputOptions;
    _displayOptions = audioOutputOptions.CurrentValue.DeviceDisplay;
    _hiddenPatterns = CompileHiddenPatterns(_displayOptions);

    // Subscribe to config changes for runtime refresh
    _audioOutputOptionsMonitor.OnChange(opts =>
    {
      _logger.LogInformation("Audio output options changed, reloading display settings");
      ReloadDisplaySettingsInternal(opts.DeviceDisplay);
    });

    // Initialize device cache immediately
    var (outputDevices, inputDevices) = EnumerateDevices();
    _cachedOutputDevices = outputDevices;
    _cachedInputDevices = inputDevices;

    // Restore selected output device from preferences
    var savedOutput = _audioPreferences.CurrentValue.CurrentOutput;
    if (!string.IsNullOrEmpty(savedOutput))
    {
      var device = _cachedOutputDevices.Find(d => d.Id == savedOutput);
      if (device != null)
      {
        _selectedOutputDeviceId = savedOutput;
        _logger.LogInformation("Restored saved output device: {DeviceId}", savedOutput);
      }
      else
      {
        _logger.LogWarning("Saved output device {DeviceId} not found", savedOutput);
      }
    }

    // Restore selected input device from preferences
    var savedInput = _audioPreferences.CurrentValue.CurrentInput;
    if (!string.IsNullOrEmpty(savedInput))
    {
      var inputDevice = _cachedInputDevices.Find(d => d.Id == savedInput);
      if (inputDevice != null)
      {
        _selectedInputDeviceId = savedInput;
        _logger.LogInformation("Restored saved input device: {DeviceId}", savedInput);
      }
      else
      {
        _logger.LogWarning("Saved input device {DeviceId} not found", savedInput);
      }
    }

    _logger.LogInformation(
      "SoundFlowDeviceManager initialized with {OutputCount} output and {InputCount} input devices",
      outputDevices.Count, inputDevices.Count);
  }

  /// <inheritdoc/>
  public Task<IReadOnlyList<AudioDeviceInfo>> GetOutputDevicesAsync(
    CancellationToken cancellationToken = default)
  {
    lock (_devicesLock)
    {
      return Task.FromResult<IReadOnlyList<AudioDeviceInfo>>(
        _cachedOutputDevices.ToList().AsReadOnly());
    }
  }

  /// <inheritdoc/>
  public Task<IReadOnlyList<AudioDeviceInfo>> GetInputDevicesAsync(
    CancellationToken cancellationToken = default)
  {
    lock (_devicesLock)
    {
      return Task.FromResult<IReadOnlyList<AudioDeviceInfo>>(
        _cachedInputDevices.ToList().AsReadOnly());
    }
  }

  /// <inheritdoc/>
  public Task<AudioDeviceInfo?> GetDefaultOutputDeviceAsync(
    CancellationToken cancellationToken = default)
  {
    lock (_devicesLock)
    {
      var defaultDevice = _cachedOutputDevices.Find(d => d.IsDefault);
      return Task.FromResult(defaultDevice);
    }
  }

  /// <inheritdoc/>
  public async Task SetOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrEmpty(deviceId);

    lock (_devicesLock)
    {
      var device = _cachedOutputDevices.Find(d => d.Id == deviceId);
      if (device == null)
      {
        throw new InvalidOperationException($"Output device '{deviceId}' not found");
      }

      _selectedOutputDeviceId = deviceId;
      _logger.LogInformation("Selected output device: {DeviceId} ({DeviceName})",
        device.Id, device.Name);
    }

    // Persist the selection
    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      await _configurationManager.SetValueAsync(
        storeId,
        "AudioPreferences:CurrentOutput",
        deviceId,
        cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to persist output device selection: {DeviceId}", deviceId);
    }
  }

  /// <inheritdoc/>
  public bool IsUSBPortInUse(string usbPort)
  {
    ArgumentException.ThrowIfNullOrEmpty(usbPort);

    lock (_reservationLock)
    {
      return _usbPortReservations.ContainsKey(usbPort);
    }
  }

  /// <inheritdoc/>
  public void ReserveUSBPort(string usbPort, string sourceId)
  {
    ArgumentException.ThrowIfNullOrEmpty(usbPort);
    ArgumentException.ThrowIfNullOrEmpty(sourceId);

    lock (_reservationLock)
    {
      if (_usbPortReservations.TryGetValue(usbPort, out var existingSourceId))
      {
        throw new AudioDeviceConflictException(
          $"USB port '{usbPort}' is already in use by source '{existingSourceId}'",
          usbPort,
          existingSourceId);
      }

      _usbPortReservations[usbPort] = sourceId;
      _logger.LogInformation(
        "Reserved USB port {USBPort} for source {SourceId}",
        usbPort, sourceId);
    }
  }

  /// <inheritdoc/>
  public void ReleaseUSBPort(string usbPort)
  {
    ArgumentException.ThrowIfNullOrEmpty(usbPort);

    lock (_reservationLock)
    {
      if (_usbPortReservations.Remove(usbPort))
      {
        _logger.LogInformation("Released USB port {USBPort}", usbPort);
      }
    }
  }

  /// <inheritdoc/>
  public Task RefreshDevicesAsync(CancellationToken cancellationToken = default)
  {
    _logger.LogDebug("Refreshing audio devices");

    try
    {
      var previousOutputDevices = new List<AudioDeviceInfo>();
      var previousInputDevices = new List<AudioDeviceInfo>();

      lock (_devicesLock)
      {
        previousOutputDevices.AddRange(_cachedOutputDevices);
        previousInputDevices.AddRange(_cachedInputDevices);
      }

      // Enumerate devices using SoundFlow
      var (outputDevices, inputDevices) = EnumerateDevices();

      lock (_devicesLock)
      {
        _cachedOutputDevices = outputDevices;
        _cachedInputDevices = inputDevices;
      }

      // Detect changes and raise events
      RaiseDeviceChangeEvents(previousOutputDevices, outputDevices);
      RaiseDeviceChangeEvents(previousInputDevices, inputDevices);

      _logger.LogDebug(
        "Device refresh complete. Found {OutputCount} output and {InputCount} input devices",
        outputDevices.Count, inputDevices.Count);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to refresh audio devices");
      throw;
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc/>
  public string? GetSelectedOutputDeviceId() => _selectedOutputDeviceId;

  /// <inheritdoc/>
  public string? GetSelectedInputDeviceId() => _selectedInputDeviceId;

  /// <summary>
  /// Sets the preferred input device and persists the selection.
  /// </summary>
  public async Task SetInputDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrEmpty(deviceId);

    lock (_devicesLock)
    {
      var device = _cachedInputDevices.Find(d => d.Id == deviceId);
      if (device == null)
        throw new InvalidOperationException($"Input device '{deviceId}' not found");

      _selectedInputDeviceId = deviceId;
      _logger.LogInformation("Selected input device: {DeviceId} ({DeviceName})", device.Id, device.Name);
    }

    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
      await _configurationManager.SetValueAsync(
        storeId,
        "AudioPreferences:CurrentInput",
        deviceId,
        cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to persist input device selection: {DeviceId}", deviceId);
    }
  }

  /// <summary>
  /// Gets all USB port reservations.
  /// </summary>
  /// <returns>A dictionary of USB port to source ID mappings.</returns>
  public IReadOnlyDictionary<string, string> GetUSBPortReservations()
  {
    lock (_reservationLock)
    {
      return new Dictionary<string, string>(_usbPortReservations);
    }
  }

  /// <summary>
  /// Updates the device cache from SoundFlow enumeration.
  /// </summary>
  /// <param name="outputDevices">The list of output devices.</param>
  /// <param name="inputDevices">The list of input devices.</param>
  internal void UpdateDeviceCache(
    IEnumerable<AudioDeviceInfo> outputDevices,
    IEnumerable<AudioDeviceInfo> inputDevices)
  {
    lock (_devicesLock)
    {
      _cachedOutputDevices = outputDevices.ToList();
      _cachedInputDevices = inputDevices.ToList();
    }
  }

  /// <summary>
  /// Gets the display name for the Google Cast virtual output device.
  /// Shows the default device name if one is configured.
  /// </summary>
  private string GetCastDisplayName()
  {
    var prefs = _audioPreferences.CurrentValue;
    return string.IsNullOrEmpty(prefs.DefaultCastDeviceName)
      ? "Google Cast"
      : $"Cast ({prefs.DefaultCastDeviceName})";
  }

  private bool IsDeviceHidden(string deviceName)
  {
    // Per-device name overrides take precedence
    if (_displayOptions.HiddenDeviceNames.Contains(deviceName, StringComparer.OrdinalIgnoreCase))
      return true;
    if (_displayOptions.VisibleDeviceNames.Contains(deviceName, StringComparer.OrdinalIgnoreCase))
      return false;

    // Fall back to regex patterns
    return _hiddenPatterns.Any(p => p.IsMatch(deviceName));
  }

  private string ApplyFriendlyName(string rawName)
  {
    // Per-device name overrides take precedence
    if (_displayOptions.DeviceFriendlyNames.TryGetValue(rawName, out var overrideName) &&
        !string.IsNullOrEmpty(overrideName))
      return overrideName;

    // Fall back to substring match
    foreach (var mapping in _displayOptions.FriendlyNames)
    {
      if (!string.IsNullOrEmpty(mapping.Pattern) &&
          rawName.Contains(mapping.Pattern, StringComparison.OrdinalIgnoreCase))
        return mapping.FriendlyName;
    }
    return rawName;
  }

  private (List<AudioDeviceInfo> output, List<AudioDeviceInfo> input) EnumerateDevices()
  {
    var outputDevices = new List<AudioDeviceInfo>();
    var inputDevices = new List<AudioDeviceInfo>();
    var castName = GetCastDisplayName();

    try
    {
      // Create a temporary MiniAudioEngine to enumerate devices
      // This is disposed after enumeration
      using var tempEngine = new MiniAudioEngine();

      _logger.LogDebug("Enumerating audio devices using MiniAudio backend...");

      // Enumerate playback (output) devices
      var playbackDevices = tempEngine.PlaybackDevices;
      _logger.LogDebug("Found {Count} playback devices from MiniAudio", playbackDevices.Length);

      for (int i = 0; i < playbackDevices.Length; i++)
      {
        var device = playbackDevices[i];
        var rawName = string.IsNullOrWhiteSpace(device.Name)
          ? "Default Audio Output"
          : device.Name;

        if (IsDeviceHidden(rawName))
        {
          _logger.LogDebug("  Output device {Index}: {Name} — hidden by filter", i, rawName);
          continue;
        }

        var isDefault = outputDevices.Count == 0; // First non-hidden device is default
        var displayName = ApplyFriendlyName(rawName);
        var isUsb = rawName.Contains("USB", StringComparison.OrdinalIgnoreCase);

        outputDevices.Add(new AudioDeviceInfo
        {
          Id = $"playback-{i}",
          Name = displayName,
          RawName = rawName,
          Type = AudioDeviceType.Output,
          IsDefault = isDefault,
          MaxChannels = 2,
          SupportedSampleRates = [44100, 48000, 96000],
          IsUSBDevice = isUsb,
          USBPort = isUsb ? ExtractUSBPort(rawName) : null
        });

        _logger.LogDebug("  Output device {Index}: {RawName} → {DisplayName} (Default: {IsDefault}, USB: {IsUSB})",
          i, rawName, displayName, isDefault, isUsb);
      }

      // Enumerate capture (input) devices
      var captureDevices = tempEngine.CaptureDevices;
      _logger.LogDebug("Found {Count} capture devices from MiniAudio", captureDevices.Length);

      for (int i = 0; i < captureDevices.Length; i++)
      {
        var device = captureDevices[i];
        var rawName = string.IsNullOrWhiteSpace(device.Name)
          ? "Default Audio Input"
          : device.Name;

        if (IsDeviceHidden(rawName))
        {
          _logger.LogDebug("  Input device {Index}: {Name} — hidden by filter", i, rawName);
          continue;
        }

        var isDefault = inputDevices.Count == 0; // First non-hidden device is default
        var displayName = ApplyFriendlyName(rawName);
        var isUsb = rawName.Contains("USB", StringComparison.OrdinalIgnoreCase);

        inputDevices.Add(new AudioDeviceInfo
        {
          Id = $"capture-{i}",
          Name = displayName,
          RawName = rawName,
          Type = AudioDeviceType.Input,
          IsDefault = isDefault,
          MaxChannels = 2,
          SupportedSampleRates = [44100, 48000],
          IsUSBDevice = isUsb,
          USBPort = isUsb ? ExtractUSBPort(rawName) : null
        });

        _logger.LogDebug("  Input device {Index}: {RawName} → {DisplayName} (Default: {IsDefault}, USB: {IsUSB})",
          i, rawName, displayName, isDefault, isUsb);
      }

      // Always add virtual outputs for Google Cast and HTTP Stream
      // These are software outputs managed by the application
      outputDevices.Add(new AudioDeviceInfo
      {
        Id = "http-stream",
        Name = "HTTP Audio Stream",
        Type = AudioDeviceType.Output,
        IsDefault = false,
        MaxChannels = 2,
        SupportedSampleRates = [48000],
        IsUSBDevice = false
      });

      outputDevices.Add(new AudioDeviceInfo
      {
        Id = "google-cast",
        Name = castName,
        Type = AudioDeviceType.Output,
        IsDefault = false,
        MaxChannels = 2,
        SupportedSampleRates = [48000],
        IsUSBDevice = false
      });

      _logger.LogDebug("Device enumeration complete: {OutputCount} output, {InputCount} input devices",
        outputDevices.Count, inputDevices.Count);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to enumerate audio devices using MiniAudio, using fallback defaults");

      // Ensure we always have at least a default device
      outputDevices.Add(new AudioDeviceInfo
      {
        Id = "default",
        Name = "Default Audio Output",
        Type = AudioDeviceType.Output,
        IsDefault = true,
        MaxChannels = 2,
        SupportedSampleRates = [44100, 48000],
        IsUSBDevice = false
      });

      outputDevices.Add(new AudioDeviceInfo
      {
        Id = "http-stream",
        Name = "HTTP Audio Stream",
        Type = AudioDeviceType.Output,
        IsDefault = false,
        MaxChannels = 2,
        SupportedSampleRates = [48000],
        IsUSBDevice = false
      });

      outputDevices.Add(new AudioDeviceInfo
      {
        Id = "google-cast",
        Name = castName,
        Type = AudioDeviceType.Output,
        IsDefault = false,
        MaxChannels = 2,
        SupportedSampleRates = [48000],
        IsUSBDevice = false
      });
    }

    return (outputDevices, inputDevices);
  }

  /// <summary>
  /// Gets all output devices (including hidden) with display metadata.
  /// Used by the Device Display Settings UI.
  /// </summary>
  public Task<IReadOnlyList<DeviceDisplayInfo>> GetAllDevicesWithDisplayInfoAsync(
    CancellationToken cancellationToken = default)
  {
    var result = new List<DeviceDisplayInfo>();
    var castName = GetCastDisplayName();

    try
    {
      using var tempEngine = new MiniAudioEngine();

      var playbackDevices = tempEngine.PlaybackDevices;
      for (int i = 0; i < playbackDevices.Length; i++)
      {
        var device = playbackDevices[i];
        var rawName = string.IsNullOrWhiteSpace(device.Name)
          ? "Default Audio Output"
          : device.Name;
        var isHidden = IsDeviceHidden(rawName);
        var displayName = ApplyFriendlyName(rawName);
        var isUsb = rawName.Contains("USB", StringComparison.OrdinalIgnoreCase);
        var friendlyOverride = _displayOptions.DeviceFriendlyNames.TryGetValue(rawName, out var fn) ? fn : null;

        result.Add(new DeviceDisplayInfo
        {
          DeviceId = $"playback-{i}",
          RawName = rawName,
          DisplayName = displayName,
          IsHidden = isHidden,
          FriendlyNameOverride = friendlyOverride,
          Type = AudioDeviceType.Output,
          IsDefault = result.Count(r => r.Type == AudioDeviceType.Output && !r.IsHidden) == 0 && !isHidden,
          IsUSBDevice = isUsb
        });
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to enumerate devices for display info");
      result.Add(new DeviceDisplayInfo
      {
        DeviceId = "default",
        RawName = "Default Audio Output",
        DisplayName = "Default Audio Output",
        IsHidden = false,
        Type = AudioDeviceType.Output,
        IsDefault = true,
        IsUSBDevice = false
      });
    }

    return Task.FromResult<IReadOnlyList<DeviceDisplayInfo>>(result.AsReadOnly());
  }

  /// <summary>
  /// Reloads display settings from the current options and re-enumerates devices.
  /// Call this after persisting config changes that may not trigger IOptionsMonitor.OnChange
  /// (e.g., SQLite config store updates).
  /// </summary>
  public void ReloadDisplaySettings()
  {
    ReloadDisplaySettingsInternal(_audioOutputOptionsMonitor.CurrentValue.DeviceDisplay);
  }

  /// <summary>
  /// Reloads display settings using explicitly provided display options.
  /// Use this when the caller has freshly-persisted data that IOptionsMonitor
  /// may not yet reflect (e.g., after writing to SQLite config store).
  /// </summary>
  public void ReloadDisplaySettings(DeviceDisplayOptions options)
  {
    ReloadDisplaySettingsInternal(options);
  }

  private void ReloadDisplaySettingsInternal(DeviceDisplayOptions newOptions)
  {
    _displayOptions = newOptions;
    _hiddenPatterns = CompileHiddenPatterns(_displayOptions);
    _logger.LogInformation(
      "Display settings reloaded: {HiddenPatterns} patterns, {HiddenNames} hidden names, {VisibleNames} visible overrides, {FriendlyNames} friendly names",
      _hiddenPatterns.Count, _displayOptions.HiddenDeviceNames.Count,
      _displayOptions.VisibleDeviceNames.Count, _displayOptions.DeviceFriendlyNames.Count);

    // Re-enumerate devices to apply new display settings
    try
    {
      var (outputDevices, inputDevices) = EnumerateDevices();
      lock (_devicesLock)
      {
        _cachedOutputDevices = outputDevices;
        _cachedInputDevices = inputDevices;
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to re-enumerate devices after display settings reload");
    }
  }

  private List<Regex> CompileHiddenPatterns(DeviceDisplayOptions options)
  {
    var patterns = options.HiddenDevicePatterns
      .Select(p =>
      {
        try { return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
        catch (Exception ex)
        {
          _logger.LogWarning("Invalid hidden device pattern '{Pattern}': {Error}", p, ex.Message);
          return null;
        }
      })
      .Where(r => r != null)
      .Cast<Regex>()
      .ToList();

    if (patterns.Count > 0)
      _logger.LogInformation("Device filtering: {Count} hidden pattern(s) active", patterns.Count);
    if (options.FriendlyNames.Count > 0)
      _logger.LogInformation("Device friendly names: {Count} mapping(s) configured", options.FriendlyNames.Count);

    return patterns;
  }

  /// <summary>
  /// Finds a capture device by name fuzzy matching.
  /// Returns the MiniAudio DeviceInfo struct if found, or null.
  /// </summary>
  public DeviceInfo? FindCaptureDeviceByName(string namePart)
  {
    if (string.IsNullOrWhiteSpace(namePart))
    {
      return null;
    }

    try
    {
      using var engine = new MiniAudioEngine();
      var devices = engine.CaptureDevices;
      var match = devices.FirstOrDefault(d =>
        d.Name != null && d.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase));

      if (match.Name != null)
      {
        _logger.LogDebug("Found capture device matching '{NamePart}': {DeviceName}", namePart, match.Name);
        return match;
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error searching for capture device {NamePart}", namePart);
    }
    return null;
  }

  /// <summary>
  /// Attempts to extract a USB port identifier from a device name.
  /// </summary>
  private static string? ExtractUSBPort(string deviceName)
  {
    // Common patterns: "USB Audio Device", "hw:1,0", "plughw:1,0"
    // Try to extract card number as USB port identifier
    var match = Regex.Match(deviceName, @"hw:(\d+)|card\s*(\d+)|USB-(\d+)");
    if (match.Success)
    {
      var cardNum = match.Groups[1].Success ? match.Groups[1].Value :
                    match.Groups[2].Success ? match.Groups[2].Value :
                    match.Groups[3].Value;
      return $"USB-{cardNum}";
    }
    return null;
  }

  private void RaiseDeviceChangeEvents(
    List<AudioDeviceInfo> previousDevices,
    List<AudioDeviceInfo> currentDevices)
  {
    var previousIds = previousDevices.Select(d => d.Id).ToHashSet();
    var currentIds = currentDevices.Select(d => d.Id).ToHashSet();

    // Find added devices
    foreach (var deviceId in currentIds.Except(previousIds))
    {
      var device = currentDevices.Find(d => d.Id == deviceId);
      DevicesChanged?.Invoke(this, new AudioDeviceChangedEventArgs
      {
        ChangeType = DeviceChangeType.Added,
        Device = device
      });
      _logger.LogInformation("Audio device added: {DeviceId} ({DeviceName})",
        device?.Id, device?.Name);
    }

    // Find removed devices
    foreach (var deviceId in previousIds.Except(currentIds))
    {
      var device = previousDevices.Find(d => d.Id == deviceId);
      DevicesChanged?.Invoke(this, new AudioDeviceChangedEventArgs
      {
        ChangeType = DeviceChangeType.Removed,
        Device = device
      });
      _logger.LogInformation("Audio device removed: {DeviceId} ({DeviceName})",
        device?.Id, device?.Name);
    }
  }

  /// <summary>
  /// Raises the DevicesChanged event.
  /// </summary>
  /// <param name="e">The event arguments.</param>
  protected virtual void OnDevicesChanged(AudioDeviceChangedEventArgs e)
  {
    DevicesChanged?.Invoke(this, e);
  }
}

/// <summary>
/// Display metadata for a device, including hidden/visible state and friendly name overrides.
/// Used by the Device Display Settings UI.
/// </summary>
public record DeviceDisplayInfo
{
  public required string DeviceId { get; init; }
  public required string RawName { get; init; }
  public required string DisplayName { get; init; }
  public bool IsHidden { get; init; }
  public string? FriendlyNameOverride { get; init; }
  public required AudioDeviceType Type { get; init; }
  public bool IsDefault { get; init; }
  public bool IsUSBDevice { get; init; }
}
