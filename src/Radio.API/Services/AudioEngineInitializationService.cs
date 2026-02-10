using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Configuration.Models;
using IAppConfigurationManager = Radio.Infrastructure.Configuration.Abstractions.IConfigurationManager;

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
  private readonly IBluetoothService? _bluetoothService;

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
    IServiceProvider serviceProvider)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _deviceManager = deviceManager;
    _audioPreferences = audioPreferences;
    _masterMixer = masterMixer;
    _bluetoothOptions = bluetoothOptions;

    // Try to get IAudioManager (optional)
    _audioManager = serviceProvider.GetService<IAudioManager>();
    _configManager = serviceProvider.GetService<IAppConfigurationManager>();
    _bluetoothService = serviceProvider.GetService<IBluetoothService>();
  }

  /// <summary>
  /// Starts the service and initializes the audio engine.
  /// </summary>
  public async Task StartAsync(CancellationToken cancellationToken)
  {
    try
    {
      _logger.LogInformation("Initializing audio engine...");
      
      // Initialize the audio engine
      await _audioEngine.InitializeAsync(cancellationToken);
      
      // Start the audio engine
      await _audioEngine.StartAsync(cancellationToken);
      
      _logger.LogInformation("Audio engine initialized and started successfully");
      
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
      
      // Apply startup audio preferences
      await ApplyStartupPreferencesAsync(outputDevices, cancellationToken);

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

      // Set output device
      string? outputToUse = null;
      if (!string.IsNullOrEmpty(preferredOutputId))
      {
        // Try to use the preferred output
        var preferredOutput = outputDevices.FirstOrDefault(d => d.Id == preferredOutputId);
        if (preferredOutput != null)
        {
          outputToUse = preferredOutput.Id;
          _logger.LogInformation("Using preferred output device: {DeviceName}", preferredOutput.Name);
        }
        else
        {
          _logger.LogWarning("Preferred output device {OutputId} not found, using default", preferredOutputId);
        }
      }
      
      // If no preferred output or it wasn't found, use the default
      if (outputToUse == null)
      {
        var defaultOutput = outputDevices.FirstOrDefault(d => d.IsDefault);
        if (defaultOutput != null)
        {
          outputToUse = defaultOutput.Id;
          _logger.LogInformation("Using default output device: {DeviceName}", defaultOutput.Name);
        }
      }
      
      // Apply the output device
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
      }
      
      // Determine which source to activate
      var sourceToActivate = !string.IsNullOrEmpty(prefs.CurrentSource) 
        ? prefs.CurrentSource 
        : "Radio"; // Default to Radio if no preference
      
      _logger.LogInformation("Startup audio source: {SourceType} (from {Origin})",
        sourceToActivate,
        !string.IsNullOrEmpty(prefs.CurrentSource) ? "preferences" : "default");
      
      // Note: Actual source activation would require IAudioManager or source factory
      // For now, we log the intent. The MainLayout will handle initial source selection.
      _logger.LogInformation("Audio startup configuration applied. Source activation deferred to UI.");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to apply startup preferences");
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
