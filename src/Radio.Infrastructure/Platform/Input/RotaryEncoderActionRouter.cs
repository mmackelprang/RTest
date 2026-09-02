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
/// One knob's behaviour, as the router actually dispatches it.
///
/// <para>
/// ⚠ <b>Descriptions are for display and must describe what the delegates do.</b> This type exists
/// because a hand-typed table on the Settings page drifted from the code (§2.2 defect 2 of the
/// encoder handoff, corrected by hand in PR #489 and structurally here). Changing a handler without
/// changing its description recreates the defect this replaced.
/// </para>
/// </summary>
/// <param name="EncoderIndex">Encoder index this entry dispatches.</param>
/// <param name="TurnDescription">What a detent does, in the owner's language.</param>
/// <param name="PressDescription">What a short press does, in the owner's language.</param>
public sealed record RotaryEncoderMapping(int EncoderIndex, string TurnDescription, string PressDescription);

/// <summary>
/// Maps rotary encoder events to audio actions.
///
/// <para>
/// <b>Index mapping: 0 = Volume, 1 = Tuning, 2 = Source, 3 = Visualization.</b> The cabinet's
/// physical order is VOLUME / SOURCE / PRESETS / TUNING, so indices 1-3 do not yet match the
/// engraving. That is deliberate and tracked: the remap lands with ENC-5 (the SOURCE overlay) and
/// ENC-7 (PRESETS), because those rows introduce the handlers the remap would point at. Index 0 is
/// VOLUME under both orders, so the knob with a safety hazard on it is already correct.
/// </para>
///
/// <para>
/// The HUD's geometry keys off the encoder index, not off this table, so a card already appears
/// above the knob that was turned. Remapping later changes what the card says, not where it is.
/// </para>
///
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
  private readonly IEncoderFeedbackSink _hud;
  private readonly EncoderLongPressGesture _gesture;
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

  private readonly RotaryEncoderMapping[] _mapping;
  private readonly Action<int>[] _turnHandlers;
  private readonly Action[] _pressHandlers;

  /// <summary>
  /// What each knob currently does. <b>This is the table the router dispatches through</b>, not a
  /// description kept alongside it, so the Settings page cannot disagree with the code.
  ///
  /// <para>
  /// ⚠ Indices 1-3 do not match the cabinet engraving (VOLUME / SOURCE / PRESETS / TUNING) yet. That
  /// is deliberate and tracked: ENC-5 and ENC-7 own the remap because they introduce the handlers it
  /// would point at. Editing this array is how that remap is made — there is no second place to
  /// change.
  /// </para>
  /// </summary>
  public IReadOnlyList<RotaryEncoderMapping> Mapping => _mapping;

  public RotaryEncoderActionRouter(
    ILogger<RotaryEncoderActionRouter> logger,
    IRotaryEncoderService encoderService,
    Func<IAudioManager> audioManagerFactory,
    VisualizationModeService vizModeService,
    IOptionsMonitor<RotaryEncoderOptions> options,
    IEncoderFeedbackSink hud,
    ISleepService? sleepService = null,
    TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _encoderService = encoderService;
    _audioManagerFactory = audioManagerFactory;
    _vizModeService = vizModeService;
    _options = options;
    _hud = hud;
    _sleepService = sleepService;

    // Index-ordered and index-addressed: entry n dispatches encoder n. Kept as three parallel arrays
    // rather than delegates on the record so the record stays a plain data type the API can project.
    _mapping =
    [
      new RotaryEncoderMapping(0, "Volume up / down", "Mute on / off"),
      new RotaryEncoderMapping(1, "Tune up / down (radio sources)", "Start / stop station scan"),
      new RotaryEncoderMapping(2, "Preview the next / previous source", "Switch to the previewed source"),
      new RotaryEncoderMapping(3, "Cycle visualization mode", "Visualization on / off"),
    ];
    _turnHandlers = [HandleVolumeTurn, HandleTuningTurn, HandleSourceTurn, HandleVizTurn];
    _pressHandlers = [HandleVolumePress, HandleTuningPress, HandleSourcePress, HandleVizPress];

    // Four channels, matching the 0-3 index range EncoderTurnedEventArgs and
    // EncoderButtonEventArgs document.
    _gesture = new EncoderLongPressGesture(4, logger, timeProvider);
    _gesture.ShortPress += OnShortPress;
    _gesture.LongPress += OnLongPress;
    _gesture.HoldStarted += i => PublishHold(i, EncoderHudPhase.HoldStart);
    _gesture.HoldCancelled += i => PublishHold(i, EncoderHudPhase.HoldCancel);

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

      // Dispatch through the same table the Settings page renders (ENC-8 §2.5). A parallel switch
      // beside it would be a second source of truth wearing one name, and it would drift on the
      // first remap.
      if (e.EncoderIndex >= 0 && e.EncoderIndex < _turnHandlers.Length)
      {
        _turnHandlers[e.EncoderIndex](e.Delta);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling encoder {Index} turn", e.EncoderIndex);
    }
  }

  private void OnButtonPressed(object? sender, EncoderButtonEventArgs e)
  {
    try
    {
      // Both edges matter now. The short action fires on release and the long action fires at the
      // threshold while still held, so this handler routes the edge and leaves the choice of action
      // to the gesture.
      //
      // The sleep-wake consumption stays on the PRESS edge: waking is what the input is spent on,
      // and letting the release through would fire a short action into a UI that has just changed
      // underneath the user.
      if (e.IsPressed && TryWakeFromSleep("encoder-button"))
      {
        return;
      }

      _gesture.OnButtonEdge(e.EncoderIndex, e.IsPressed);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling encoder {Index} button edge", e.EncoderIndex);
    }
  }

  private void OnShortPress(int index)
  {
    try
    {
      // Same table, same reason as OnEncoderTurned.
      if (index >= 0 && index < _pressHandlers.Length)
      {
        _pressHandlers[index]();
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling encoder {Index} short press", index);
    }
  }

  private void OnLongPress(int index)
  {
    // Two long-press consumers exist in the spec: VOLUME -> Standby and PRESETS -> Save. Only the
    // first is wired here. PRESETS is ENC-7's action, and encoder 2 still drives the source handler
    // under the pre-ENC-5 index mapping - registering a save on it now would put a preset write
    // behind a knob the cabinet does not label PRESETS yet.
    if (index != 0)
    {
      return;
    }

    if (_sleepService is null)
    {
      _logger.LogDebug("Volume long-press ignored: no sleep service is registered");
      return;
    }

    _ = EnterStandbyAsync();
  }

  private async Task EnterStandbyAsync()
  {
    try
    {
      await _sleepService!.EnterSleepAsync();
      PublishHold(0, EncoderHudPhase.HoldCommit);
      _logger.LogInformation("Standby entered by a volume knob long-press");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error entering standby from the volume knob");
    }
  }

  /// <summary>
  /// Publishes one hold-phase card, so the client can draw, collapse or complete the progress ring.
  /// </summary>
  private void PublishHold(int index, EncoderHudPhase phase)
  {
    // Encoder 0 is the only index OnLongPress acts on, so it is the only index that publishes hold
    // phases. A ring drawing on the other three would promise an action nothing performs.
    if (index != 0)
    {
      return;
    }

    var mgr = _audioManagerFactory();
    _hud.Publish(new EncoderHudEventArgs
    {
      EncoderIndex = index,
      // The label is what the card reads while the ring draws. Completing the hold enters standby,
      // so that is what the ring is labelled with.
      Label = phase == EncoderHudPhase.HoldStart ? "HOLD FOR STANDBY" : "VOLUME",
      Phase = phase,
      VolumePercent = (int)Math.Round(mgr.MasterVolume * 100f),
      IsMuted = mgr.IsMuted,
    });
  }

  /// <summary>
  /// Publishes what this handler just did, so the HUD can put it above the knob that produced it.
  /// </summary>
  private void PublishHud(int index, string label, Action<HudBuilder> configure)
  {
    var b = new HudBuilder();
    configure(b);
    _hud.Publish(new EncoderHudEventArgs
    {
      EncoderIndex = index,
      Label = label,
      Phase = EncoderHudPhase.Value,
      VolumePercent = b.VolumePercent,
      IsMuted = b.IsMuted,
      PrimaryText = b.PrimaryText,
      SecondaryText = b.SecondaryText,
      PrimaryIsFrequency = b.PrimaryIsFrequency,
    });
  }

  private sealed class HudBuilder
  {
    public int? VolumePercent;
    public bool IsMuted;
    public string? PrimaryText;
    public string? SecondaryText;
    public bool PrimaryIsFrequency;
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
    // step_size x tier_multiplier per detent and multiplies it by 2% again. On factory defaults —
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

    // ENC-4b. The first detent clears mute and applies the delta in the same frame.
    //
    // Without this the knob moves a value nobody can hear, and the user's response to that silence
    // is to turn it further - which is the input pattern the host clamp above exists to survive.
    // Unmuting first also means the delta lands on an audible volume rather than on a number that
    // will be revealed at some later, surprising moment.
    if (mgr.IsMuted)
    {
      mgr.IsMuted = false;
      _logger.LogInformation("Unmuted by a volume knob turn");
    }

    var newVolume = Math.Clamp(mgr.MasterVolume + clamped * step, 0f, 1f);
    mgr.MasterVolume = newVolume;
    _logger.LogDebug("Volume: {Volume:P0}", newVolume);

    PublishHud(0, "VOLUME", b =>
    {
      b.VolumePercent = (int)Math.Round(newVolume * 100f);
      b.IsMuted = mgr.IsMuted;
    });
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
    else
    {
      // The knob takes no action on a non-radio source - track skip is not wired to it. It still
      // publishes a card, because a knob that moves and shows nothing reads as broken hardware.
      PublishHud(1, "TRACK", b =>
      {
        b.PrimaryText = mgr.ActiveSource?.Name;
        b.SecondaryText = "no track control on this source";
      });
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

      // Published after the stepping finishes, so the card reads where the tuner actually landed
      // rather than where the detent aimed it.
      PublishHud(1, "TUNING", b =>
      {
        b.PrimaryText = radio.CurrentFrequency.ToDisplayString();
        b.SecondaryText = radio.CurrentBand.ToString().ToUpperInvariant();
        b.PrimaryIsFrequency = true;
      });
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

    PublishHud(2, "SOURCE", b =>
    {
      b.PrimaryText = sourceType.ToString().ToUpperInvariant();
      b.SecondaryText = "press to switch";
    });
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

    PublishHud(3, "VISUALIZER", b => b.PrimaryText = _vizModeService.CurrentMode.ToUpperInvariant());
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

    _gesture.Dispose();
    _encoderService.EncoderTurned -= OnEncoderTurned;
    _encoderService.ButtonPressed -= OnButtonPressed;
    GC.SuppressFinalize(this);
  }
}
