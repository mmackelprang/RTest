using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Configuration.Models;
using IAppConfigurationManager = Radio.Configuration.Abstractions.IConfigurationManager;

namespace Radio.API.Services;

/// <summary>
/// Background service that initializes and starts the audio engine on application startup.
/// Also handles graceful shutdown of the audio engine and automatic source/output selection.
/// </summary>
public class AudioEngineInitializationService : IHostedService
{
  private readonly ILogger<AudioEngineInitializationService> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly IAudioManager? _audioManager;
  private readonly IOptionsMonitor<AudioPreferences> _audioPreferences;
  private readonly IMasterMixer _masterMixer;
  private readonly IAppConfigurationManager? _configManager;
  private readonly IOptions<BluetoothOptions> _bluetoothOptions;
  private readonly IOptions<AudioOutputOptions> _audioOutputOptions;
  private readonly IBluetoothService? _bluetoothService;
  private readonly BluetoothAutoSwitchService? _bluetoothAutoSwitch;
  private readonly GoogleCastOutput? _castOutput;
  private readonly HttpStreamOutput? _httpOutput;
  private readonly IServiceProvider _serviceProvider;

  /// <summary>
  /// Initializes a new instance of the AudioEngineInitializationService.
  /// </summary>
  public AudioEngineInitializationService(
    ILogger<AudioEngineInitializationService> logger,
    IAudioEngine audioEngine,
    IAudioDeviceManager deviceManager,
    IOptionsMonitor<AudioPreferences> audioPreferences,
    IMasterMixer masterMixer,
    IOptions<BluetoothOptions> bluetoothOptions,
    IOptions<AudioOutputOptions> audioOutputOptions,
    IServiceProvider serviceProvider)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _deviceManager = deviceManager;
    _audioPreferences = audioPreferences;
    _masterMixer = masterMixer;
    _bluetoothOptions = bluetoothOptions;
    _audioOutputOptions = audioOutputOptions;
    _serviceProvider = serviceProvider;

    // Try to get IAudioManager (optional)
    _audioManager = serviceProvider.GetService<IAudioManager>();
    _configManager = serviceProvider.GetService<IAppConfigurationManager>();
    _bluetoothService = serviceProvider.GetService<IBluetoothService>();
    _bluetoothAutoSwitch = serviceProvider.GetService<BluetoothAutoSwitchService>();
    _castOutput = serviceProvider.GetService<GoogleCastOutput>();
    _httpOutput = serviceProvider.GetService<HttpStreamOutput>();
  }

  /// <summary>
  /// Starts the service and initializes the audio engine.
  /// </summary>
  public async Task StartAsync(CancellationToken cancellationToken)
  {
    try
    {
      // Clean up orphaned play history entries from unclean shutdown.
      // Runs before audio engine init to prevent fingerprinting from creating duplicates.
      await CloseOrphanedPlayHistoryEntriesAsync(cancellationToken);

      _logger.LogInformation("Initializing audio engine...");

      // Initialize the audio engine
      await _audioEngine.InitializeAsync(cancellationToken);
      
      // Start the audio engine
      await _audioEngine.StartAsync(cancellationToken);
      
      _logger.LogInformation("Audio engine initialized and started successfully");

      // Load persisted device display settings from config store (hidden/visible/friendly names).
      // IOptionsMonitor only loads from appsettings.json; user changes are saved to SQLite.
      if (_deviceManager is SoundFlowDeviceManager sfDeviceManager)
      {
        await sfDeviceManager.LoadDisplaySettingsFromStoreAsync(cancellationToken);
      }

      // Enumerate devices
      var outputDevices = await _deviceManager.GetOutputDevicesAsync(cancellationToken);
      var inputDevices = await _deviceManager.GetInputDevicesAsync(cancellationToken);
      
      _logger.LogInformation("Found {OutputCount} output devices and {InputCount} input devices",
        outputDevices.Count, inputDevices.Count);
      
      // Log device details
      foreach (var device in outputDevices)
      {
        _logger.LogInformation("Output device: {DeviceName} (ID: {DeviceId}, Default: {IsDefault})",
          device.Name, device.Id, device.IsDefault);
      }
      
      foreach (var device in inputDevices)
      {
        _logger.LogInformation("Input device: {DeviceName} (ID: {DeviceId})",
          device.Name, device.Id);
      }
      
      // Apply startup audio preferences (output device, source)
      await ApplyStartupPreferencesAsync(outputDevices, cancellationToken);

      // Initialize AudioManager (restores volume/mute/balance from config store)
      if (_audioManager != null)
      {
        await _audioManager.InitializeAsync(cancellationToken);
      }

      // Activate persisted audio source (after volume is restored so audio starts at correct level)
      await ActivatePersistedSourceAsync(cancellationToken);

      // Pre-warm Bluetooth source if configured (creates source without switching to it)
      if (_bluetoothAutoSwitch != null)
      {
        await _bluetoothAutoSwitch.PreWarmBluetoothAsync(cancellationToken);
      }

      // Enable Bluetooth discoverability on startup if configured
      await EnableBluetoothOnStartupAsync(cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize audio engine");
      // Don't throw - allow the application to start even if audio fails
    }
  }

  /// <summary>
  /// Applies user preferences for audio source and output on startup.
  /// If no preferences exist, defaults to Radio source and default output device.
  /// </summary>
  private async Task ApplyStartupPreferencesAsync(
    IReadOnlyList<AudioDeviceInfo> outputDevices,
    CancellationToken cancellationToken)
  {
    try
    {
      var prefs = _audioPreferences.CurrentValue;

      // Try reading persisted output device from the config store first,
      // since IOptionsMonitor reads from appsettings.json which doesn't
      // get updated when the user changes the output device at runtime.
      string? persistedOutput = null;
      if (_configManager != null)
      {
        try
        {
          var storeId = _configManager.CurrentStoreType == ConfigurationStoreType.Sqlite ? "sqlite" : "config";
          persistedOutput = await _configManager.GetValueAsync<string>(storeId, "AudioPreferences:CurrentOutput", ct: cancellationToken);
          if (!string.IsNullOrEmpty(persistedOutput))
          {
            _logger.LogInformation("Found persisted output device preference: {DeviceId}", persistedOutput);
          }
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Could not read persisted output device preference from config store");
        }
      }

      // Prefer persisted value from config store, fall back to IOptionsMonitor
      var preferredOutputId = !string.IsNullOrEmpty(persistedOutput) ? persistedOutput : prefs.CurrentOutput;

      // Handle virtual outputs (google-cast, http-stream)
      if (preferredOutputId == "google-cast")
      {
        _logger.LogInformation("Restoring Google Cast output from startup preferences");
        await ActivateVirtualOutputsForCastAsync(cancellationToken);
      }
      else if (preferredOutputId == "http-stream")
      {
        _logger.LogInformation("Restoring HTTP Stream output from startup preferences");
        await ActivateOutputAsync(_httpOutput, "HTTP Stream");
      }
      else
      {
        // Physical output device
        string? outputToUse = null;
        string? preferredDeviceName = null;
        if (!string.IsNullOrEmpty(preferredOutputId))
        {
          var preferredOutput = outputDevices.FirstOrDefault(d => d.Id == preferredOutputId);
          if (preferredOutput != null)
          {
            outputToUse = preferredOutput.Id;
            preferredDeviceName = preferredOutput.Name;
            _logger.LogInformation("Using preferred output device: {DeviceName}", preferredOutput.Name);
          }
          else
          {
            _logger.LogWarning("Preferred output device {OutputId} not found, using default", preferredOutputId);
          }
        }

        if (outputToUse == null)
        {
          var defaultOutput = outputDevices.FirstOrDefault(d => d.IsDefault);
          if (defaultOutput != null)
          {
            outputToUse = defaultOutput.Id;
            preferredDeviceName = defaultOutput.Name;
            _logger.LogInformation("Using default output device: {DeviceName}", defaultOutput.Name);
          }
        }

        if (outputToUse != null)
        {
          try
          {
            await _deviceManager.SetOutputDeviceAsync(outputToUse, cancellationToken);
            _logger.LogInformation("Output device set successfully");
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Failed to set output device");
          }

          // Verify the output device actually connected to the correct PipeWire node.
          // After PipeWire restarts, MiniAudio device indices may shift, causing the
          // wrong device to be selected even though the ID matches.
          try
          {
            var currentDevices = await _deviceManager.GetOutputDevicesAsync(cancellationToken);
            var selectedId = _deviceManager.GetSelectedOutputDeviceId();
            var activeDevice = selectedId != null
              ? currentDevices.FirstOrDefault(d => d.Id == selectedId)
              : null;
            if (activeDevice != null && preferredDeviceName != null &&
                !activeDevice.Name.Contains(preferredDeviceName, StringComparison.OrdinalIgnoreCase))
            {
              _logger.LogWarning(
                "Output device mismatch: expected \"{Expected}\" but connected to \"{Actual}\" — searching by name",
                preferredDeviceName, activeDevice.Name);

              var correctDevice = currentDevices.FirstOrDefault(d =>
                d.Name.Contains(preferredDeviceName, StringComparison.OrdinalIgnoreCase));
              if (correctDevice != null)
              {
                _logger.LogInformation("Found correct device \"{Name}\" at ID {Id} — switching",
                  correctDevice.Name, correctDevice.Id);
                await _deviceManager.SetOutputDeviceAsync(correctDevice.Id, cancellationToken);
              }
              else
              {
                _logger.LogWarning("Could not find device matching \"{Name}\" — using current device",
                  preferredDeviceName);
              }
            }
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Output device verification failed — continuing with current device");
          }
        }
      }
      
      _logger.LogInformation("Audio startup output configuration applied");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to apply startup preferences");
    }
  }

  /// <summary>
  /// Activates the last-used audio source on startup. Falls back to Radio if no preference exists.
  /// Must be called AFTER AudioManager.InitializeAsync() so volume is restored before audio starts.
  /// </summary>
  private async Task ActivatePersistedSourceAsync(CancellationToken cancellationToken)
  {
    if (_audioManager == null)
    {
      _logger.LogDebug("AudioManager not available, skipping source activation on startup");
      return;
    }

    try
    {
      var prefs = _audioPreferences.CurrentValue;
      var sourceToActivate = !string.IsNullOrEmpty(prefs.CurrentSource)
        ? prefs.CurrentSource
        : "Radio";

      if (!Enum.TryParse<AudioSourceType>(sourceToActivate, true, out var sourceType))
      {
        _logger.LogWarning("Invalid persisted source type '{Source}', falling back to Radio", sourceToActivate);
        sourceType = AudioSourceType.Radio;
      }

      _logger.LogInformation("Activating persisted source: {SourceType} (from {Origin})",
        sourceType,
        !string.IsNullOrEmpty(prefs.CurrentSource) ? "preferences" : "default");

      await _audioManager.GetOrCreateSourceAsync(sourceType, switchToSource: true, cancellationToken);
      _logger.LogInformation("Source {SourceType} activated on startup", sourceType);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to activate persisted source, UI will handle default selection");
    }
  }

  /// <summary>
  /// Enables Bluetooth discoverability on startup if configured.
  /// On Windows, the adapter will start but A2DP sink (acting as a speaker)
  /// is not natively supported — phones can see the device but cannot stream audio.
  /// This works on the target Linux/Raspberry Pi platform where BlueZ supports A2DP sink.
  /// </summary>
  private async Task EnableBluetoothOnStartupAsync(CancellationToken cancellationToken)
  {
    try
    {
      var opts = _bluetoothOptions.Value;
      if (!opts.Enabled || !opts.EnableOnStartup)
      {
        _logger.LogDebug("Bluetooth auto-start disabled (Enabled={Enabled}, EnableOnStartup={EnableOnStartup})",
          opts.Enabled, opts.EnableOnStartup);
        return;
      }

      if (_bluetoothService == null)
      {
        _logger.LogDebug("Bluetooth service not available, skipping auto-start");
        return;
      }

      var deviceName = opts.DeviceName;
      _logger.LogInformation("Enabling Bluetooth on startup as '{DeviceName}'...", deviceName);
      var success = await _bluetoothService.StartAsync(deviceName, cancellationToken);

      if (success)
      {
        _logger.LogInformation("Bluetooth started successfully, device is discoverable as '{DeviceName}'", deviceName);
      }
      else
      {
        _logger.LogWarning("Bluetooth StartAsync returned false — adapter may not be available");
      }
    }
    catch (Exception ex)
    {
      // Bluetooth failure must not block application startup
      _logger.LogWarning(ex, "Failed to enable Bluetooth on startup — continuing without Bluetooth");
    }
  }

  /// <summary>
  /// Activates Cast and HTTP Stream outputs, then auto-connects to the default Cast device.
  /// </summary>
  private async Task ActivateVirtualOutputsForCastAsync(CancellationToken cancellationToken)
  {
    var castOptions = _audioOutputOptions.Value.GoogleCast;
    var isDirectChannel = string.Equals(castOptions.StreamingMode, "DirectChannel", StringComparison.OrdinalIgnoreCase);

    // DirectChannel mode bypasses HTTP — audio is sent directly over the Cast protocol.
    // Only start the HTTP stream for the standard HttpMp3 mode.
    if (!isDirectChannel)
    {
      await ActivateOutputAsync(_httpOutput, "HTTP Stream");
    }
    else
    {
      _logger.LogInformation("DirectChannel mode: skipping HTTP stream activation");
    }

    await ActivateOutputAsync(_castOutput, "Google Cast");

    // In DirectChannel mode, wire the audio engine so GoogleCastOutput can
    // create a stream reader for sending PCM data over the Cast message bus.
    if (isDirectChannel && _castOutput != null)
    {
      _castOutput.SetAudioEngine(_audioEngine);
      _logger.LogInformation("DirectChannel mode: audio engine wired to Cast output");
    }

    // Auto-connect to saved default Cast device
    var prefs = _audioPreferences.CurrentValue;
    if (string.IsNullOrEmpty(prefs.DefaultCastDeviceId) || _castOutput == null)
    {
      _logger.LogInformation("No default Cast device configured, Cast output activated but not connected");
      return;
    }

    _logger.LogInformation("Auto-connecting to default Cast device on startup: {Name} ({Id})",
      prefs.DefaultCastDeviceName, prefs.DefaultCastDeviceId);

    // Run auto-connect in background so it doesn't block startup
    _ = Task.Run(async () =>
    {
      try
      {
        // Give the Cast discovery a moment to populate cache
        await Task.Delay(3000, cancellationToken);

        var cached = await _castOutput.GetCachedDevicesAsync(cancellationToken);
        var device = cached.FirstOrDefault(d => d.Id == prefs.DefaultCastDeviceId);
        if (device == null)
        {
          _logger.LogWarning("Default Cast device {Id} not found in cache after startup, skipping auto-connect",
            prefs.DefaultCastDeviceId);
          return;
        }

        if (_castOutput.State == AudioOutputState.Created)
        {
          await _castOutput.InitializeAsync(cancellationToken);
        }

        await _castOutput.ConnectAsync(device, cancellationToken);

        // Wire the HTTP audio stream (HttpMp3 mode only)
        if (!isDirectChannel && _httpOutput?.State == AudioOutputState.Streaming)
        {
          var streamUrl = GetRoutableStreamUrl(_httpOutput.Mp3StreamUrl, _httpOutput.Port, device.IpAddress);
          _castOutput.SetStreamUrl(streamUrl);
        }

        await _castOutput.StartAsync(cancellationToken);
        _logger.LogInformation("Startup: Auto-connected to Cast device: {Name} (mode: {Mode})",
          device.FriendlyName, castOptions.StreamingMode);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to auto-connect to Cast device on startup");
      }
    }, cancellationToken);
  }

  /// <summary>
  /// Activates an audio output (initialize + start) if available.
  /// </summary>
  private async Task ActivateOutputAsync(IAudioOutput? output, string name)
  {
    if (output == null)
    {
      _logger.LogDebug("{Name} output not available", name);
      return;
    }

    try
    {
      if (output.State == AudioOutputState.Error)
      {
        await output.InitializeAsync();
      }

      if (output.State == AudioOutputState.Created)
      {
        await output.InitializeAsync();
      }

      if (output.State == AudioOutputState.Ready || output.State == AudioOutputState.Stopped)
      {
        await output.StartAsync();
        _logger.LogInformation("{Name} output activated on startup", name);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to activate {Name} output on startup", name);
    }
  }

  /// <summary>
  /// Resolves the stream URL to use the local LAN IP (Cast devices need a routable address).
  /// </summary>
  private string GetRoutableStreamUrl(string streamUrl, int port, string? targetDeviceIp)
  {
    try
    {
      var localIp = GetLocalIPAddress(targetDeviceIp);
      if (localIp != null)
      {
        var uri = new Uri(streamUrl);
        return $"http://{localIp}:{port}{uri.PathAndQuery}";
      }
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Could not resolve routable stream URL");
    }
    return streamUrl;
  }

  /// <summary>
  /// Gets the local LAN IP address, preferring one on the same subnet as the target.
  /// </summary>
  private static string? GetLocalIPAddress(string? targetDeviceIp)
  {
    IPAddress? targetIp = null;
    if (!string.IsNullOrEmpty(targetDeviceIp))
    {
      IPAddress.TryParse(targetDeviceIp, out targetIp);
    }

    string? fallbackIp = null;
    foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
    {
      if (ni.OperationalStatus != OperationalStatus.Up)
      {
        continue;
      }

      if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
      {
        continue;
      }

      var desc = ni.Description.ToLowerInvariant();
      var name = ni.Name.ToLowerInvariant();
      if (desc.Contains("hyper-v") || desc.Contains("virtual") ||
          name.Contains("vethernet") || name.Contains("wsl") ||
          name.Contains("docker") || name.Contains("br-"))
      {
        continue;
      }

      foreach (var addr in ni.GetIPProperties().UnicastAddresses)
      {
        if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
        {
          continue;
        }

        if (IPAddress.IsLoopback(addr.Address))
        {
          continue;
        }

        if (targetIp != null && addr.IPv4Mask != null)
        {
          var localBytes = addr.Address.GetAddressBytes();
          var maskBytes = addr.IPv4Mask.GetAddressBytes();
          var targetBytes = targetIp.GetAddressBytes();
          bool sameSubnet = true;
          for (int i = 0; i < 4; i++)
          {
            if ((localBytes[i] & maskBytes[i]) != (targetBytes[i] & maskBytes[i]))
            { sameSubnet = false; break; }
          }
          if (sameSubnet)
          {
            return addr.Address.ToString();
          }
        }

        fallbackIp ??= addr.Address.ToString();
      }
    }
    return fallbackIp;
  }

  /// <summary>
  /// Closes orphaned play history entries left by unclean shutdown.
  /// Must run before audio engine initialization so fingerprinting doesn't create duplicates.
  /// </summary>
  private async Task CloseOrphanedPlayHistoryEntriesAsync(CancellationToken cancellationToken)
  {
    try
    {
      using var scope = _serviceProvider.CreateScope();
      var repo = scope.ServiceProvider.GetService<IPlayHistoryRepository>();
      if (repo != null)
      {
        var closed = await repo.CloseOrphanedEntriesAsync(TimeSpan.FromMinutes(2), cancellationToken);
        if (closed > 0)
        {
          _logger.LogInformation("Cleaned up {Count} orphaned play history entries from previous shutdown", closed);
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to clean up orphaned play history entries");
    }
  }

  /// <summary>
  /// Stops the service and gracefully shuts down the audio engine.
  /// </summary>
  public async Task StopAsync(CancellationToken cancellationToken)
  {
    try
    {
      _logger.LogInformation("Stopping audio engine...");

      if (_audioEngine.State == Radio.Core.Interfaces.Audio.AudioEngineState.Running)
      {
        await _audioEngine.StopAsync(cancellationToken);
      }

      _logger.LogInformation("Audio engine stopped successfully");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stopping audio engine");
    }
  }
}
