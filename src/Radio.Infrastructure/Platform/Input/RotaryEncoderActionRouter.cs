using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
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
  private readonly ISleepService? _sleepService;
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
    IOptionsMonitor<RotaryEncoderOptions> options,
    ISleepService? sleepService = null)
  {
    _logger = logger;
    _encoderService = encoderService;
    _audioManagerFactory = audioManagerFactory;
    _vizModeService = vizModeService;
    _options = options;
    _sleepService = sleepService;

    _encoderService.EncoderTurned += OnEncoderTurned;
    _encoderService.ButtonPressed += OnButtonPressed;
  }

  private void OnEncoderTurned(object? sender, EncoderTurnedEventArgs e)
  {
    try
    {
      // If sleeping, wake on any encoder input and consume the event
      if (TryWakeFromSleep("encoder-turn"))
      {
        return;
      }

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
    if (!e.IsPressed)
    {
      return;
    }

    try
    {
      // If sleeping, wake on any encoder input and consume the event
      if (TryWakeFromSleep("encoder-button"))
      {
        return;
      }

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

  /// <summary>
  /// Checks if the system is sleeping and wakes it if so.
  /// </summary>
  private bool TryWakeFromSleep(string wakeSource)
  {
    if (_sleepService == null || !_sleepService.IsSleeping)
    {
      return false;
    }

    _ = _sleepService.WakeAsync(wakeSource);
    _logger.LogInformation("Woke from sleep via {WakeSource}", wakeSource);
    return true;
  }

  /// <summary>
  /// Bounds one event's movement, regardless of what arrived on the wire.
  ///
  /// <para>
  /// ENC-3. This is applied <b>unconditionally</b>, not as a fallback, and that is the point: there
  /// is a real window on every boot and after every reconnect during which the device runs whatever
  /// is in its flash — on a fresh or reset Pico, factory defaults including volume acceleration at
  /// x50 — and the knobs are live throughout it. The clamp is what makes that window sluggish
  /// rather than dangerous.
  /// </para>
  /// </summary>
  private static int Clamp(int delta, int limit) => Math.Clamp(delta, -limit, limit);

  // --- Encoder 0: Volume ---

  private void HandleVolumeTurn(int delta)
  {
    var mgr = _audioManagerFactory();
    var opts = _options.CurrentValue;
    var step = opts.VolumeStepPercent / 100f;

    // ENC-11 host clamp, and it is not defensive boilerplate.
    //
    // The device's movement already includes its own acceleration, so the host is handed
    // `step_size x tier_multiplier` per detent and multiplies it by 2% again. On factory defaults —
    // measured on this hardware as step_size 1 with a x50 tier — that is 50 x 2% = 100 points, a
    // single click from silence to full, in a living room, from a knob a guest may be touching for
    // the first time.
    //
    // There is a real window on every boot and after every reconnect during which the device runs
    // whatever is in its flash, and this clamp is what makes that window safe. It tightens further
    // until a configuration push has been verified, because until then "the device is on factory
    // tiers" is a live possibility rather than a hypothetical.
    int clamp = RotaryEncoderConfigVerifier.VolumeClampFor(_encoderService.ConfigStatus);
    int clamped = Clamp(delta, clamp);

    if (clamped != delta)
    {
      _logger.LogDebug(
        "Volume movement {Delta} clamped to {Clamped} (config status {Status})",
        delta, clamped, _encoderService.ConfigStatus);
    }

    var newVolume = Math.Clamp(mgr.MasterVolume + clamped * step, 0f, 1f);
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
      // ENC-3 clamp. StepRadioFrequencyAsync awaits ONE hardware call per step, so an unclamped
      // delta is not merely a big jump — it is that many sequential tuner calls from a single
      // detent. At a factory x50 tier that is fifty, on a box where incidental load correlates with
      // audible distortion.
      int clamped = Clamp(delta, RotaryEncoderConfigDefaults.TuningClamp);
      _ = StepRadioFrequencyAsync(radio, clamped);
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
        {
          await radio.StepFrequencyUpAsync();
        }
        else
        {
          await radio.StepFrequencyDownAsync();
        }
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
    // ENC-3 clamp: one detent, one entry, always. Without it a fast spin walks the list by the
    // acceleration multiplier and lands somewhere nobody aimed.
    int clamped = Clamp(delta, RotaryEncoderConfigDefaults.SelectorClamp);

    _currentSourceIndex = ((_currentSourceIndex + clamped) % PrimarySourceTypes.Length
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
    // ENC-3 clamp: the visualiser list is a selector like any other.
    _vizModeService.CycleMode(Clamp(delta, RotaryEncoderConfigDefaults.SelectorClamp));
  }

  private void HandleVizPress()
  {
    _vizModeService.ToggleEnabled();
  }

  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    _disposed = true;

    _encoderService.EncoderTurned -= OnEncoderTurned;
    _encoderService.ButtonPressed -= OnButtonPressed;
    GC.SuppressFinalize(this);
  }
}
