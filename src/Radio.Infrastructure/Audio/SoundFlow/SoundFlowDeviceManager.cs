using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Exceptions;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Configuration.Abstractions;
using Radio.Infrastructure.Configuration.Models;
using SoundFlow.Backends.MiniAudio;

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
  private readonly Dictionary<string, string> _usbPortReservations = new();
  private readonly object _reservationLock = new();
  private readonly object _devicesLock = new();

  private List<AudioDeviceInfo> _cachedOutputDevices = [];
  private List<AudioDeviceInfo> _cachedInputDevices = [];
  private string? _selectedOutputDeviceId;

  /// <inheritdoc/>
  public event EventHandler<AudioDeviceChangedEventArgs>? DevicesChanged;

  /// <summary>
  /// Initializes a new instance of the <see cref="SoundFlowDeviceManager"/> class.
  /// </summary>
  /// <param name="logger">The logger instance.</param>
  /// <param name="configurationManager">The configuration manager.</param>
  /// <param name="audioPreferences">The audio preferences.</param>
  public SoundFlowDeviceManager(
    ILogger<SoundFlowDeviceManager> logger,
    IConfigurationManager configurationManager,
    IOptionsMonitor<AudioPreferences> audioPreferences)
  {
    _logger = logger;
    _configurationManager = configurationManager;
    _audioPreferences = audioPreferences;

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

  /// <summary>
  /// Gets the currently selected output device ID.
  /// </summary>
  /// <returns>The selected device ID, or null if using default.</returns>
  public string? GetSelectedOutputDeviceId() => _selectedOutputDeviceId;

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

  private (List<AudioDeviceInfo> output, List<AudioDeviceInfo> input) EnumerateDevices()
  {
    var outputDevices = new List<AudioDeviceInfo>();
    var inputDevices = new List<AudioDeviceInfo>();

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
        var isDefault = i == 0; // First device is typically the default
        var isUsb = device.Name.Contains("USB", StringComparison.OrdinalIgnoreCase);

        outputDevices.Add(new AudioDeviceInfo
        {
          Id = $"playback-{i}",
          Name = device.Name,
          Type = AudioDeviceType.Output,
          IsDefault = isDefault,
          MaxChannels = 2,
          SupportedSampleRates = [44100, 48000, 96000],
          IsUSBDevice = isUsb,
          USBPort = isUsb ? ExtractUSBPort(device.Name) : null
        });

        _logger.LogDebug("  Output device {Index}: {Name} (Default: {IsDefault}, USB: {IsUSB})",
          i, device.Name, isDefault, isUsb);
      }

      // Enumerate capture (input) devices
      var captureDevices = tempEngine.CaptureDevices;
      _logger.LogDebug("Found {Count} capture devices from MiniAudio", captureDevices.Length);

      for (int i = 0; i < captureDevices.Length; i++)
      {
        var device = captureDevices[i];
        var isDefault = i == 0; // First device is typically the default
        var isUsb = device.Name.Contains("USB", StringComparison.OrdinalIgnoreCase);

        inputDevices.Add(new AudioDeviceInfo
        {
          Id = $"capture-{i}",
          Name = device.Name,
          Type = AudioDeviceType.Input,
          IsDefault = isDefault,
          MaxChannels = 2,
          SupportedSampleRates = [44100, 48000],
          IsUSBDevice = isUsb,
          USBPort = isUsb ? ExtractUSBPort(device.Name) : null
        });

        _logger.LogDebug("  Input device {Index}: {Name} (Default: {IsDefault}, USB: {IsUSB})",
          i, device.Name, isDefault, isUsb);
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
        Name = "Google Cast",
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
        Name = "Google Cast",
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
  /// Finds a capture device by name fuzzy matching.
  /// </summary>
  public object? FindCaptureDeviceByName(string namePart)
  {
    if (string.IsNullOrWhiteSpace(namePart))
    {
        return null;
    }

    try
    {
      using var engine = new MiniAudioEngine();
      var devices = engine.CaptureDevices;
      // Look for fuzzy match
      // MiniAudio DeviceInfo is a struct and default value has null/empty Name usually
      var match = devices.FirstOrDefault(d => d.Name != null && d.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase));
      
      if (match.Name != null)
      {
         return match.Name;
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
    var match = System.Text.RegularExpressions.Regex.Match(deviceName, @"hw:(\d+)|card\s*(\d+)|USB-(\d+)");
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
