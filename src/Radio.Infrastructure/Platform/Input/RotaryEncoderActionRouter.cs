using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// Maps rotary encoder events to audio actions.
/// Encoder 0 = Volume, Encoder 1 = Tuning, Encoder 2 = Source, Encoder 3 = Visualization.
/// Uses Func&lt;IAudioManager&gt; for deferred resolution to break circular DI.
/// </summary>
public class RotaryEncoderActionRouter : IDisposable
{
  private readonly ILogger<RotaryEncoderActionRouter> _logger;
  private readonly IRotaryEncoderService _encoderService;
  private readonly Func<IAudioManager> _audioManagerFactory;
  private readonly VisualizationModeService _vizModeService;
  private readonly IOptionsMonitor<RotaryEncoderOptions> _options;
  private bool _disposed;

  // Primary source types for cycling (Encoder 2)
  private static readonly AudioSourceType[] PrimarySourceTypes =
  [
    AudioSourceType.Radio,
    AudioSourceType.FilePlayer,
    AudioSourceType.Bluetooth,
    AudioSourceType.Vinyl,
    AudioSourceType.GenericUSB
  ];
  private int _currentSourceIndex;

  public RotaryEncoderActionRouter(
    ILogger<RotaryEncoderActionRouter> logger,
    IRotaryEncoderService encoderService,
    Func<IAudioManager> audioManagerFactory,
    VisualizationModeService vizModeService,
    IOptionsMonitor<RotaryEncoderOptions> options)
  {
    _logger = logger;
    _encoderService = encoderService;
    _audioManagerFactory = audioManagerFactory;
    _vizModeService = vizModeService;
    _options = options;

    _encoderService.EncoderTurned += OnEncoderTurned;
    _encoderService.ButtonPressed += OnButtonPressed;
  }

  private void OnEncoderTurned(object? sender, EncoderTurnedEventArgs e)
  {
    try
    {
      switch (e.EncoderIndex)
      {
        case 0: HandleVolumeTurn(e.Delta); break;
        case 1: HandleTuningTurn(e.Delta); break;
        case 2: HandleSourceTurn(e.Delta); break;
        case 3: HandleVizTurn(e.Delta); break;
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling encoder {Index} turn", e.EncoderIndex);
    }
  }

  private void OnButtonPressed(object? sender, EncoderButtonEventArgs e)
  {
    // Only act on press (not release)
    if (!e.IsPressed) return;

    try
    {
      switch (e.EncoderIndex)
      {
        case 0: HandleVolumePress(); break;
        case 1: HandleTuningPress(); break;
        case 2: HandleSourcePress(); break;
        case 3: HandleVizPress(); break;
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling encoder {Index} button press", e.EncoderIndex);
    }
  }

  // --- Encoder 0: Volume ---

  private void HandleVolumeTurn(int delta)
  {
    var mgr = _audioManagerFactory();
    var opts = _options.CurrentValue;
    var step = opts.VolumeStepPercent / 100f;
    var newVolume = Math.Clamp(mgr.MasterVolume + delta * step, 0f, 1f);
    mgr.MasterVolume = newVolume;
    _logger.LogDebug("Volume: {Volume:P0}", newVolume);
  }

  private void HandleVolumePress()
  {
    var mgr = _audioManagerFactory();
    mgr.IsMuted = !mgr.IsMuted;
    _logger.LogInformation("Mute toggled: {IsMuted}", mgr.IsMuted);
  }

  // --- Encoder 1: Tuning ---

  private void HandleTuningTurn(int delta)
  {
    var mgr = _audioManagerFactory();
    if (mgr.ActiveSource is IRadioControl radio)
    {
      // Step frequency based on delta direction (may be multiple steps for fast turning)
      _ = StepRadioFrequencyAsync(radio, delta);
    }
  }

  private async Task StepRadioFrequencyAsync(IRadioControl radio, int delta)
  {
    try
    {
      var steps = Math.Abs(delta);
      for (int i = 0; i < steps; i++)
      {
        if (delta > 0)
          await radio.StepFrequencyUpAsync();
        else
          await radio.StepFrequencyDownAsync();
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error stepping radio frequency");
    }
  }

  private void HandleTuningPress()
  {
    var mgr = _audioManagerFactory();
    if (mgr.ActiveSource is IRadioControl radio)
    {
      if (radio.IsScanning)
      {
        _ = radio.StopScanAsync();
        _logger.LogInformation("Radio scan stopped");
      }
      else
      {
        _ = radio.StartScanAsync(ScanDirection.Up);
        _logger.LogInformation("Radio scan started (up)");
      }
    }
  }

  // --- Encoder 2: Source ---

  private void HandleSourceTurn(int delta)
  {
    _currentSourceIndex = ((_currentSourceIndex + delta) % PrimarySourceTypes.Length
      + PrimarySourceTypes.Length) % PrimarySourceTypes.Length;

    var sourceType = PrimarySourceTypes[_currentSourceIndex];
    _logger.LogDebug("Source selection: {Source}", sourceType);
  }

  private void HandleSourcePress()
  {
    var sourceType = PrimarySourceTypes[_currentSourceIndex];
    _logger.LogInformation("Switching to source: {Source}", sourceType);
    _ = SwitchSourceAsync(sourceType);
  }

  private async Task SwitchSourceAsync(AudioSourceType sourceType)
  {
    try
    {
      var mgr = _audioManagerFactory();
      await mgr.GetOrCreateSourceAsync(sourceType, switchToSource: true);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error switching to source {Source}", sourceType);
    }
  }

  // --- Encoder 3: Visualization ---

  private void HandleVizTurn(int delta)
  {
    _vizModeService.CycleMode(delta);
  }

  private void HandleVizPress()
  {
    _vizModeService.ToggleEnabled();
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    _encoderService.EncoderTurned -= OnEncoderTurned;
    _encoderService.ButtonPressed -= OnButtonPressed;
    GC.SuppressFinalize(this);
  }
}
