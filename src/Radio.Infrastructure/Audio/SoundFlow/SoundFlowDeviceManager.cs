using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Exceptions;
using Radio.Core.Interfaces.Audio;
using Radio.Configuration.Abstractions;
using Radio.Configuration.Models;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Structs;

namespace Radio.Infrastructure.Audio.SoundFlow;

/// <summary>
/// SoundFlow implementation of audio device management.
/// Handles device enumeration and USB port reservations.
/// </summary>
public class SoundFlowDeviceManager : IAudioDeviceManager, IDisposable
{
  /// <summary>
  /// Trailing-edge debounce window for config-change-driven display-settings reloads.
  ///
  /// A config write storm (a volume-slider drag persists <c>ui.playback</c> on every tick)
  /// fires one <c>IOptionsMonitor.OnChange</c> per write. Each reload re-enumerates audio
  /// devices, so an undebounced handler turned N slider ticks into N concurrent native
  /// enumerations. 300ms is long enough to collapse a drag into a single reload and short
  /// enough that a genuine settings change still applies while the user is still looking
  /// at the screen.
  /// </summary>
  private static readonly TimeSpan DisplaySettingsReloadDebounce = TimeSpan.FromMilliseconds(300);

  private readonly ILogger<SoundFlowDeviceManager> _logger;
  private readonly IConfigurationManager _configurationManager;
  private readonly IOptionsMonitor<AudioPreferences> _audioPreferences;
  private readonly IOptionsMonitor<AudioOutputOptions> _audioOutputOptionsMonitor;
  private DeviceDisplayOptions _displayOptions;
  private List<Regex> _hiddenPatterns;
  private readonly Dictionary<string, string> _usbPortReservations = new();
  private readonly object _reservationLock = new();
  private readonly object _devicesLock = new();

  // Debounce/coalesce state for the options-change handler. The timer is created
  // disarmed and re-armed on each change; _displaySettingsReloadInFlight keeps a slow
  // store read from overlapping the next window's reload.
  private readonly System.Threading.Timer _displaySettingsReloadTimer;
  private readonly IDisposable? _optionsChangeRegistration;
  private int _displaySettingsReloadInFlight;
  private volatile bool _disposed;

  private List<AudioDeviceInfo> _cachedOutputDevices = [];
  private List<AudioDeviceInfo> _cachedInputDevices = [];
  private string? _selectedOutputDeviceId;
  private string? _selectedInputDeviceId;
  private MiniAudioEngine? _sharedEngine;

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

    // Subscribe to config changes for runtime refresh.
    //
    // IMPORTANT: must NOT use `opts.DeviceDisplay` directly here. `IOptionsMonitor`
    // reflects ONLY appsettings.json — the SQLite config store (where the actual
    // FriendlyNames / HiddenDeviceNames / VisibleDeviceNames live in production)
    // is read separately via LoadDisplaySettingsFromStoreAsync. Using opts.DeviceDisplay
    // overwrites the SQLite-loaded values with whatever's in appsettings.json (often
    // empty), silently breaking friendly-name lookups including "Built-in Audio
    // Analog Stereo" → "Soundbar". This change-event fires on every config reload
    // (secret-tag failures, audio-preference saves, etc.) — clobbering on every fire.
    //
    // Re-loading from the store on change is the correct merge: the store is the
    // source of truth in production; appsettings.json is only the boot-time default.
    //
    // The reload is debounced and coalesced rather than fired-and-forgotten. Every
    // config write raises this event, and every reload re-enumerates audio devices —
    // so the previous `_ = LoadDisplaySettingsFromStoreAsync()` turned a burst of
    // writes into a burst of concurrent thread pool threads all calling into the
    // native device layer at once. That is what aborted radio-api on 2026-08-10.
    // Serialization is still guaranteed independently by NativeAudioDeviceGate; this
    // debounce removes the fan-out at its source so the gate is not the only defence.
    _displaySettingsReloadTimer = new System.Threading.Timer(
      OnDisplaySettingsReloadDue, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    _optionsChangeRegistration = _audioOutputOptionsMonitor.OnChange(_unusedOpts =>
    {
      _logger.LogInformation("Audio output options changed, scheduling display settings reload");
      ArmDisplaySettingsReload();
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

  /// <summary>
  /// Sets the shared MiniAudioEngine reference for device enumeration.
  /// When set, device enumeration reuses this engine instead of creating
  /// temporary instances — avoiding native memory leaks in MiniAudio that
  /// cause SIGSEGV after ~300+ create/dispose cycles (~28 minutes at 5s interval).
  /// </summary>
  internal void SetSharedEngine(MiniAudioEngine? engine)
  {
    _sharedEngine = engine;
    _logger.LogDebug("Shared MiniAudioEngine reference {Action}",
      engine != null ? "set" : "cleared");

    // The device manager gates its own native calls explicitly, but the engine's owner
    // (SoundFlowAudioEngine) calls UpdateAudioDevicesInfo directly on this instance. Those
    // call sites are serialized only if the instance is a SerializedMiniAudioEngine, whose
    // override routes through NativeAudioDeviceGate. A raw MiniAudioEngine here means those
    // calls can re-enter the PulseAudio main loop concurrently and abort the process, so
    // say so loudly rather than failing as an intermittent SIGABRT weeks later.
    if (engine != null && engine is not SerializedMiniAudioEngine)
    {
      _logger.LogError(
        "Shared audio engine is {ActualType}, not SerializedMiniAudioEngine — device " +
        "enumeration performed directly on this engine is NOT serialized and can abort " +
        "the process. Construct it via SerializedMiniAudioEngine.Create().",
        engine.GetType().Name);
    }
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
      {
        throw new InvalidOperationException($"Input device '{deviceId}' not found");
      }

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
    {
        return true;
    }
    if (_displayOptions.VisibleDeviceNames.Contains(deviceName, StringComparer.OrdinalIgnoreCase))
    {
        return false;
    }

    // Fall back to regex patterns
    return _hiddenPatterns.Any(p => p.IsMatch(deviceName));
  }

  private string ApplyFriendlyName(string rawName)
  {
    // Per-device name overrides take precedence
    if (_displayOptions.DeviceFriendlyNames.TryGetValue(rawName, out var overrideName) &&
        !string.IsNullOrEmpty(overrideName))
    {
        return overrideName;
    }

    // Fall back to substring match
    foreach (var mapping in _displayOptions.FriendlyNames)
    {
      if (!string.IsNullOrEmpty(mapping.Pattern) &&
          rawName.Contains(mapping.Pattern, StringComparison.OrdinalIgnoreCase))
      {
        return mapping.FriendlyName;
      }
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
      // Reuse the shared engine when available to avoid creating/disposing temporary
      // MiniAudioEngine instances. Each native engine init probes JACK, PulseAudio, OSS,
      // ALSA backends; after ~300+ create/dispose cycles the native allocator corrupts
      // and triggers SIGSEGV. Only create a temporary engine during initial construction
      // (before the shared engine is set by SoundFlowAudioEngine.InitializeAsync).
      MiniAudioEngine? tempEngine = null;
      var shared = _sharedEngine;
      MiniAudioEngine engine;
      if (shared != null)
      {
        engine = shared;
      }
      else
      {
        _logger.LogDebug("No shared engine available, creating temporary engine for enumeration");
        tempEngine = SerializedMiniAudioEngine.Create();
        engine = tempEngine;
      }

      try
      {
        _logger.LogDebug("Enumerating audio devices using MiniAudio backend...");

        // The native enumeration and the device snapshot it publishes are taken as one
        // gated region: holding NativeAudioDeviceGate across the native call is what keeps
        // a second thread out of the PulseAudio main loop, and taking the snapshot inside
        // the same region means the arrays below belong to *this* enumeration. Everything
        // after this — filtering, friendly names, DTO construction — is managed-only work
        // and deliberately runs outside the gate to keep the hold time to the native call.
        var (playbackDevices, captureDevices) = NativeAudioDeviceGate.Run(() =>
        {
          engine.UpdateAudioDevicesInfo();
          return (engine.PlaybackDevices, engine.CaptureDevices);
        });

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

          var isDefault = device.IsDefault; // Use the system's actual default device
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

        // Enumerate capture (input) devices from the snapshot taken above
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

        var isDefault = device.IsDefault; // Use the system's actual default device
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
      finally
      {
        // Only dispose the engine if we created a temporary one
        tempEngine?.Dispose();
      }
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
      // Reuse shared engine to avoid native memory leak (see EnumerateDevices comment)
      MiniAudioEngine? tempEngine = null;
      var shared = _sharedEngine;
      MiniAudioEngine engine;
      if (shared != null)
      {
        engine = shared;
      }
      else
      {
        tempEngine = SerializedMiniAudioEngine.Create();
        engine = tempEngine;
      }

      try
      {
        // Native enumeration + snapshot under the process-wide gate; see EnumerateDevices.
        var (playbackDevices, captureDevices) = NativeAudioDeviceGate.Run(() =>
        {
          engine.UpdateAudioDevicesInfo();
          return (engine.PlaybackDevices, engine.CaptureDevices);
        });

        // Playback (output) devices
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
            IsDefault = device.IsDefault,
            IsUSBDevice = isUsb
          });
        }

        // Virtual output devices (HTTP Stream, Google Cast)
        result.Add(new DeviceDisplayInfo
        {
          DeviceId = "http-stream",
          RawName = "HTTP Audio Stream",
          DisplayName = ApplyFriendlyName("HTTP Audio Stream"),
          IsHidden = IsDeviceHidden("HTTP Audio Stream"),
          FriendlyNameOverride = _displayOptions.DeviceFriendlyNames.TryGetValue("HTTP Audio Stream", out var httpFn) ? httpFn : null,
          Type = AudioDeviceType.Output,
          IsDefault = false,
          IsUSBDevice = false
        });

        var castRawName = "Google Cast";
        result.Add(new DeviceDisplayInfo
        {
          DeviceId = "google-cast",
          RawName = castRawName,
          DisplayName = ApplyFriendlyName(castRawName),
          IsHidden = IsDeviceHidden(castRawName),
          FriendlyNameOverride = _displayOptions.DeviceFriendlyNames.TryGetValue(castRawName, out var castFn) ? castFn : null,
          Type = AudioDeviceType.Output,
          IsDefault = false,
          IsUSBDevice = false
        });

        // Capture (input) devices from the snapshot taken above
        for (int i = 0; i < captureDevices.Length; i++)
        {
          var device = captureDevices[i];
          var rawName = string.IsNullOrWhiteSpace(device.Name)
            ? "Default Audio Input"
            : device.Name;
          var isHidden = IsDeviceHidden(rawName);
          var displayName = ApplyFriendlyName(rawName);
          var isUsb = rawName.Contains("USB", StringComparison.OrdinalIgnoreCase);
          var friendlyOverride = _displayOptions.DeviceFriendlyNames.TryGetValue(rawName, out var capFn) ? capFn : null;

          result.Add(new DeviceDisplayInfo
          {
            DeviceId = $"capture-{i}",
            RawName = rawName,
            DisplayName = displayName,
            IsHidden = isHidden,
            FriendlyNameOverride = friendlyOverride,
            Type = AudioDeviceType.Input,
            IsDefault = device.IsDefault,
            IsUSBDevice = isUsb
          });
        }
      }
      finally
      {
        tempEngine?.Dispose();
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
  /// (Re-)arms the trailing-edge debounce timer for a display-settings reload.
  /// Each call pushes the deadline out, so a burst of config changes produces one reload.
  /// </summary>
  private void ArmDisplaySettingsReload()
  {
    if (_disposed)
    {
      return;
    }

    try
    {
      _displaySettingsReloadTimer.Change(DisplaySettingsReloadDebounce, Timeout.InfiniteTimeSpan);
    }
    catch (ObjectDisposedException)
    {
      // Raced with Dispose — the reload is no longer wanted.
    }
  }

  /// <summary>
  /// Debounce timer callback: performs the coalesced display-settings reload.
  /// </summary>
  /// <remarks>
  /// <c>async void</c> is the required shape for a <see cref="System.Threading.Timer"/>
  /// callback, so the body owns its own exception boundary — an escaping exception here
  /// would tear down the process. This is not the fire-and-forget pattern it replaces:
  /// at most one reload is scheduled at a time and at most one runs at a time.
  /// </remarks>
  private async void OnDisplaySettingsReloadDue(object? state)
  {
    if (_disposed)
    {
      return;
    }

    // A store read slower than the debounce window could otherwise let the next
    // window's reload start on top of this one. Re-arm instead of overlapping.
    if (Interlocked.CompareExchange(ref _displaySettingsReloadInFlight, 1, 0) != 0)
    {
      ArmDisplaySettingsReload();
      return;
    }

    try
    {
      await LoadDisplaySettingsFromStoreAsync().ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Debounced display settings reload failed");
    }
    finally
    {
      Interlocked.Exchange(ref _displaySettingsReloadInFlight, 0);
    }
  }

  /// <summary>
  /// Loads display settings from the config store (SQLite/JSON) and re-enumerates devices.
  /// Call this on startup to restore user-persisted hidden/visible device lists
  /// that are not in appsettings.json.
  /// </summary>
  public async Task LoadDisplaySettingsFromStoreAsync(CancellationToken cancellationToken = default)
  {
    try
    {
      var storeId = _configurationManager.CurrentStoreType == ConfigurationStoreType.Sqlite
        ? "sqlite" : "config";

      var hiddenNames = await _configurationManager.GetValueAsync<List<string>>(
        storeId, "AudioOutput:DeviceDisplay:HiddenDeviceNames", ct: cancellationToken);
      var visibleNames = await _configurationManager.GetValueAsync<List<string>>(
        storeId, "AudioOutput:DeviceDisplay:VisibleDeviceNames", ct: cancellationToken);
      var friendlyNames = await _configurationManager.GetValueAsync<Dictionary<string, string>>(
        storeId, "AudioOutput:DeviceDisplay:DeviceFriendlyNames", ct: cancellationToken);
      var hiddenPatterns = await _configurationManager.GetValueAsync<List<string>>(
        storeId, "AudioOutput:DeviceDisplay:HiddenDevicePatterns", ct: cancellationToken);

      // Only apply if the store had any data (avoids overwriting appsettings.json defaults
      // when the store is empty, e.g., first run)
      if (hiddenNames != null || visibleNames != null || friendlyNames != null || hiddenPatterns != null)
      {
        var options = new DeviceDisplayOptions
        {
          HiddenDeviceNames = hiddenNames ?? _displayOptions.HiddenDeviceNames,
          VisibleDeviceNames = visibleNames ?? _displayOptions.VisibleDeviceNames,
          DeviceFriendlyNames = friendlyNames ?? _displayOptions.DeviceFriendlyNames,
        };

        // Preserve hidden patterns from existing config if not overridden in store
        if (hiddenPatterns != null)
        {
          options.HiddenDevicePatterns = hiddenPatterns;
        }
        else
        {
          options.HiddenDevicePatterns = _displayOptions.HiddenDevicePatterns;
        }

        ReloadDisplaySettingsInternal(options);
        _logger.LogInformation(
          "Loaded display settings from config store: {HiddenCount} hidden, {VisibleCount} visible, {FriendlyCount} friendly names",
          options.HiddenDeviceNames.Count, options.VisibleDeviceNames.Count, options.DeviceFriendlyNames.Count);
      }
      else
      {
        _logger.LogDebug("No display settings found in config store, using appsettings.json defaults");
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to load display settings from config store, using defaults");
    }
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
    {
      _logger.LogInformation("Device filtering: {Count} hidden pattern(s) active", patterns.Count);
    }
    if (options.FriendlyNames.Count > 0)
    {
      _logger.LogInformation("Device friendly names: {Count} mapping(s) configured", options.FriendlyNames.Count);
    }

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

    // Reuse shared engine to avoid native memory leak (see EnumerateDevices comment)
    MiniAudioEngine? tempEngine = null;
    var shared = _sharedEngine;
    MiniAudioEngine engine;
    if (shared != null)
    {
      engine = shared;
    }
    else
    {
      tempEngine = SerializedMiniAudioEngine.Create();
      engine = tempEngine;
    }

    try
    {
      // Native enumeration + snapshot under the process-wide gate; see EnumerateDevices.
      var devices = NativeAudioDeviceGate.Run(() =>
      {
        engine.UpdateAudioDevicesInfo();
        return engine.CaptureDevices;
      });

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
    finally
    {
      tempEngine?.Dispose();
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

  /// <summary>
  /// Unsubscribes from options changes and stops the display-settings reload debounce.
  /// Idempotent — the DI container resolves this instance under two registrations and may
  /// dispose it twice.
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    _disposed = true;
    _optionsChangeRegistration?.Dispose();
    _displaySettingsReloadTimer.Dispose();
    GC.SuppressFinalize(this);
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
