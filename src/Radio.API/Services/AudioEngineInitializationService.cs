using Radio.Core.Interfaces.Audio;

namespace Radio.API.Services;

/// <summary>
/// Background service that initializes and starts the audio engine on application startup.
/// Also handles graceful shutdown of the audio engine.
/// </summary>
public class AudioEngineInitializationService : IHostedService
{
  private readonly ILogger<AudioEngineInitializationService> _logger;
  private readonly IAudioEngine _audioEngine;
  private readonly IAudioDeviceManager _deviceManager;
  private readonly IAudioManager? _audioManager;

  /// <summary>
  /// Initializes a new instance of the AudioEngineInitializationService.
  /// </summary>
  public AudioEngineInitializationService(
    ILogger<AudioEngineInitializationService> logger,
    IAudioEngine audioEngine,
    IAudioDeviceManager deviceManager,
    IServiceProvider serviceProvider)
  {
    _logger = logger;
    _audioEngine = audioEngine;
    _deviceManager = deviceManager;
    
    // Try to get IAudioManager (optional)
    _audioManager = serviceProvider.GetService<IAudioManager>();
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
      
      // Initialize audio manager if available
      if (_audioManager != null)
      {
        _logger.LogInformation("Initializing audio manager with default source...");
        
        // TODO: Load last audio source from preferences
        // For now, we'll try to initialize Radio as the default source
        try
        {
          // The audio manager will handle source initialization
          // This will be implemented when we add automatic startup behavior
          _logger.LogInformation("Audio manager initialized (manual source selection required)");
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Failed to initialize default audio source");
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to initialize audio engine");
      // Don't throw - allow the application to start even if audio fails
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
