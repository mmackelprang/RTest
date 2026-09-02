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
/// <b>Index mapping: 0 = Volume, 1 = Source, 2 = Visualization, 3 = Tuning.</b> The cabinet reads
/// VOLUME / SOURCE / PRESETS / TUNING, so three of the four now match the engraving. Index 2 does
/// not: it holds the visualiser as a seat-warmer until ENC-7 puts PRESETS there. Leaving the old
/// source cycler on index 2 instead would have given two adjacent knobs two divergent copies of the
/// source selection, which is worse than a knob that does something harmless and unlabelled.
/// </para>
///
/// <para>
/// The HUD's geometry keys off the encoder index the event arrived on, not off this table, so a
/// card always appears beside the knob that was turned.
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
  private readonly SourceSelectorService _sourceSelector;
  private readonly EncoderLongPressGesture _gesture;
  private bool _disposed;

  private readonly RotaryEncoderMapping[] _mapping;
  // (index, delta) rather than ENC-8's (delta): ENC-5 threads the encoder index the event actually
  // arrived on into every handler, so a HUD card cannot be published against a hard-coded index and
  // land beside the wrong knob after a remap.
  private readonly Action<int, int>[] _turnHandlers;
  private readonly Action<int>[] _pressHandlers;

  /// <summary>
  /// What each knob currently does. <b>This is the table the router dispatches through</b>, not a
  /// description kept alongside it, so the Settings page cannot disagree with the code.
  ///
  /// <para>
  /// ⚠ <b>Index 2 is the one entry that does not match the cabinet engraving</b>
  /// (VOLUME / SOURCE / PRESETS / TUNING). ENC-5 remapped indices 1 and 3 onto SOURCE and TUNING and
  /// parked the visualiser on 2; ENC-7 replaces it with PRESETS. Editing this array is how that
  /// remap is made — there is no second place to change.
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
    SourceSelectorService sourceSelector,
    ISleepService? sleepService = null,
    TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _encoderService = encoderService;
    _audioManagerFactory = audioManagerFactory;
    _vizModeService = vizModeService;
    _options = options;
    _hud = hud;
    _sourceSelector = sourceSelector;
    _sleepService = sleepService;

    // Index-ordered and index-addressed: entry n dispatches encoder n. Kept as three parallel arrays
    // rather than delegates on the record so the record stays a plain data type the API can project.
    _mapping =
    [
      new RotaryEncoderMapping(0, "Volume up / down", "Mute on / off"),
      new RotaryEncoderMapping(1, "Preview a source or radio band", "Switch to the highlighted entry"),
      new RotaryEncoderMapping(2, "Cycle visualization mode", "Visualization on / off"),
      new RotaryEncoderMapping(3, "Tune up / down (radio sources)", "Start / stop station scan"),
    ];
    _turnHandlers = [HandleVolumeTurn, HandleSourceTurn, HandleVizTurn, HandleTuningTurn];
    _pressHandlers = [HandleVolumePress, HandleSourcePress, HandleVizPress, HandleTuningPress];

    // Four channels, matching the 0-3 index range EncoderTurnedEventArgs and
    // EncoderButtonEventArgs document.
    _gesture = new EncoderLongPressGesture(4, logger, timeProvider);
    _gesture.ShortPress += OnShortPress;
    _gesture.LongPress += OnLongPress;
    _gesture.HoldStarted += i => PublishHold(i, EncoderHudPhase.HoldStart);
    _gesture.HoldCancelled += i => PublishHold(i, EncoderHudPhase.HoldCancel);

    _encoderService.EncoderTurned += OnEncoderTurned;
    _encoderService.ButtonPressed += OnButtonPressed;
    _encoderService.ConnectionChanged += OnConnectionChanged;
  }

  /// <summary>
  /// Tears down an open selector overlay when the encoder disappears mid-session.
  ///
  /// <para>
  /// ENC-0's notification policy: an overlay left on screen after the knob that drives it has gone
  /// is a list nobody can navigate or commit. Dismissing it commits nothing.
  /// </para>
  /// </summary>
  private void OnConnectionChanged(object? sender, EncoderConnectionEventArgs e)
  {
    if (e.IsConnected)
    {
      return;
    }

    _sourceSelector.Dismiss();
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
      //
      // The index the event arrived on is passed to the handler rather than baked into it, so a card
      // cannot end up beside a knob other than the one that was turned the next time this table is
      // reassigned.
      if (e.EncoderIndex >= 0 && e.EncoderIndex < _turnHandlers.Length)
      {
        _turnHandlers[e.EncoderIndex](e.EncoderIndex, e.Delta);
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
        _pressHandlers[index](index);
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
    // first is wired here. PRESETS is ENC-7's action, and encoder 2 currently drives the visualiser
    // - registering a save on it now would put a preset write behind a knob that still cycles
    // visualisation modes.
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

  private void HandleVolumeTurn(int index, int delta)
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

    PublishHud(index, "VOLUME", b =>
    {
      b.VolumePercent = (int)Math.Round(newVolume * 100f);
      b.IsMuted = mgr.IsMuted;
    });
  }

  private void HandleVolumePress(int index)
  {
    var mgr = _audioManagerFactory();
    mgr.IsMuted = !mgr.IsMuted;
    _logger.LogInformation("Mute toggled: {IsMuted}", mgr.IsMuted);
  }

  // --- Encoder 3: TUNING ---

  private void HandleTuningTurn(int index, int delta)
  {
    var mgr = _audioManagerFactory();
    if (mgr.ActiveSource is IRadioControl radio)
    {
      // ENC-3 clamp. StepRadioFrequencyAsync awaits ONE hardware call per step, so an unclamped
      // delta is not merely a big jump — it is that many sequential tuner calls from a single
      // detent. At a factory x50 tier that is fifty, on a box where incidental load correlates with
      // audible distortion.
      int clamped = Clamp(delta, RotaryEncoderConfigDefaults.TuningClamp);
      _ = StepRadioFrequencyAsync(index, radio, clamped);
    }
    else
    {
      // The knob takes no action on a non-radio source - track skip is not wired to it. It still
      // publishes a card, because a knob that moves and shows nothing reads as broken hardware.
      PublishHud(index, "TRACK", b =>
      {
        b.PrimaryText = mgr.ActiveSource?.Name;
        b.SecondaryText = "no track control on this source";
      });
    }
  }

  private async Task StepRadioFrequencyAsync(int index, IRadioControl radio, int delta)
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
      PublishHud(index, "TUNING", b =>
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

  private void HandleTuningPress(int index)
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

  // --- Encoder 1: SOURCE ---

  private void HandleSourceTurn(int index, int delta)
  {
    // ENC-3 clamp: one detent, one entry, always. Acceleration is disabled on this encoder in the
    // device configuration too, so this bounds the window before a configuration push is verified
    // rather than a value the device would normally send.
    _sourceSelector.EncoderIndex = index;
    _sourceSelector.Turn(Clamp(delta, RotaryEncoderConfigDefaults.SelectorClamp));
  }

  private void HandleSourcePress(int index)
  {
    // Set here as well as on turn: a press is the other way the overlay opens, and if only the turn
    // path set it, a press-first interaction would render the overlay against whatever index was
    // last turned.
    _sourceSelector.EncoderIndex = index;
    _sourceSelector.Press();
  }

  // --- Encoder 2: PRESETS on the cabinet, the visualiser until ENC-7 puts PRESETS here ---

  private void HandleVizTurn(int index, int delta)
  {
    // ENC-3 clamp: the visualiser list is a selector like any other.
    _vizModeService.CycleMode(Clamp(delta, RotaryEncoderConfigDefaults.SelectorClamp));

    PublishHud(index, "VISUALIZER", b => b.PrimaryText = _vizModeService.CurrentMode.ToUpperInvariant());
  }

  private void HandleVizPress(int index)
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
    _encoderService.ConnectionChanged -= OnConnectionChanged;
    GC.SuppressFinalize(this);
  }
}
