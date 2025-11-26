using Microsoft.Extensions.Logging;
using Radio.Core.Exceptions;
using Radio.Core.Interfaces.Audio;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// SoundFlow implementation of audio device management.
/// Handles device enumeration and USB port reservations.
/// </summary>
public class SoundFlowDeviceManager : IAudioDeviceManager
{
  private readonly ILogger<SoundFlowDeviceManager> _logger;
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
  public SoundFlowDeviceManager(ILogger<SoundFlowDeviceManager> logger)
  {
    _logger = logger;
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
  public Task SetOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
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

    return Task.CompletedTask;
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

      _logger.LogInformation(
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
      // Try to get devices from SoundFlow's AudioEngine if available
      // For now, we'll create a default device as a fallback
      // The actual enumeration happens when the AudioEngine is initialized

      // Add a default output device if none exist
      if (outputDevices.Count == 0)
      {
        outputDevices.Add(new AudioDeviceInfo
        {
          Id = "default",
          Name = "Default Audio Output",
          Type = AudioDeviceType.Output,
          IsDefault = true,
          MaxChannels = 2,
          SupportedSampleRates = [44100, 48000, 96000],
          IsUSBDevice = false
        });
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to enumerate audio devices, using defaults");

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
    }

    return (outputDevices, inputDevices);
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
