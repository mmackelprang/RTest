using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;

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
/// <b>Index mapping: 0 = Volume, 1 = Source, 2 = Presets, 3 = Tuning.</b> This matches the cabinet's
/// engraving (VOLUME / SOURCE / PRESETS / TUNING) and the per-encoder configuration ENC-11 pushes to
/// the device. The transitional mismatch that ENC-4 and ENC-5 documented is closed.
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
  private readonly IOptionsMonitor<RotaryEncoderOptions> _options;
  private readonly IEncoderFeedbackSink _hud;
  private readonly SourceSelectorService _sourceSelector;
  private readonly PresetSelectorService _presetSelector;
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
  /// All four entries now match the cabinet engraving (VOLUME / SOURCE / PRESETS / TUNING). Editing
  /// this array is how a remap is made — there is no second place to change.
  /// </para>
  ///
  /// <para>
  /// ⚠ <b>The Settings page reads the descriptions, not the indices.</b>
  /// <c>SystemConfigPage.DescribesItsCabinetRole</c> decides whether a knob agrees with its
  /// engraving by keyword-matching <see cref="RotaryEncoderMapping.TurnDescription"/> — "Volume",
  /// "source", "preset", "Tune" — because there is no handler identity on the wire. A reword that
  /// drops the keyword relights the "does not match the cabinet" banner on a knob that is correct.
  /// </para>
  /// </summary>
  public IReadOnlyList<RotaryEncoderMapping> Mapping => _mapping;

  public RotaryEncoderActionRouter(
    ILogger<RotaryEncoderActionRouter> logger,
    IRotaryEncoderService encoderService,
    Func<IAudioManager> audioManagerFactory,
    IOptionsMonitor<RotaryEncoderOptions> options,
    IEncoderFeedbackSink hud,
    SourceSelectorService sourceSelector,
    PresetSelectorService presetSelector,
    ISleepService? sleepService = null,
    TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _encoderService = encoderService;
    _audioManagerFactory = audioManagerFactory;
    _options = options;
    _hud = hud;
    _sourceSelector = sourceSelector;
    _presetSelector = presetSelector;
    _sleepService = sleepService;

    // Index-ordered and index-addressed: entry n dispatches encoder n. Kept as three parallel arrays
    // rather than delegates on the record so the record stays a plain data type the API can project.
    _mapping =
    [
      new RotaryEncoderMapping(0, "Volume up / down", "Mute on / off"),
      new RotaryEncoderMapping(1, "Preview a source or radio band", "Switch to the highlighted entry"),
      new RotaryEncoderMapping(2, "Preview a saved preset", "Recall the highlighted preset"),
      new RotaryEncoderMapping(3, "Tune up / down (radio sources)", "Start / stop station scan"),
    ];
    _turnHandlers = [HandleVolumeTurn, HandleSourceTurn, HandlePresetsTurn, HandleTuningTurn];
    _pressHandlers = [HandleVolumePress, HandleSourcePress, HandlePresetsPress, HandleTuningPress];

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
  ///
  /// <para>
  /// Both selectors are torn down, because half a teardown is worse than none: only one of the two
  /// knobs would recover, and the other would keep an unnavigable list.
  /// </para>
  /// </summary>
  private void OnConnectionChanged(object? sender, EncoderConnectionEventArgs e)
  {
    if (e.IsConnected)
    {
      return;
    }

    _sourceSelector.Dismiss();
    _presetSelector.Dismiss();
  }

  private void OnEncoderTurned(object? sender, EncoderTurnedEventArgs e)
  {
    try
    {
      SleepGateOutcome gate = GateInput(e.EncoderIndex, isTurn: true);
      if (gate != SleepGateOutcome.Dispatch)
      {
        PublishCurrentValue(e.EncoderIndex);
        if (gate == SleepGateOutcome.ConsumeAndWake && _sleepService is not null)
        {
          _ = _sleepService.WakeAsync("encoder-turn");
          _logger.LogInformation("Woke via encoder-turn on encoder {Index}", e.EncoderIndex);
        }
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
      // Both edges matter. The short action fires on release and the long action fires at the
      // threshold while still held, so this handler routes the edge and leaves the choice of action
      // to the gesture.
      //
      // The sleep gate is applied to the PRESS edge only: waking is what the input is spent on, and
      // letting the release through would fire a short action into a UI that has just changed
      // underneath the user. The release that follows a consumed press reaches the gesture and is
      // dropped by its orphan-release guard, which exists for exactly this path.
      if (e.IsPressed)
      {
        SleepGateOutcome gate = GateInput(e.EncoderIndex, isTurn: false);
        if (gate != SleepGateOutcome.Dispatch)
        {
          PublishCurrentValue(e.EncoderIndex);
          if (gate == SleepGateOutcome.ConsumeAndWake && _sleepService is not null)
          {
            _ = _sleepService.WakeAsync("encoder-button");
            _logger.LogInformation("Woke via encoder-button on encoder {Index}", e.EncoderIndex);
          }
          return;
        }
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
    // The spec defines exactly two long-press consumers and deliberately no third: VOLUME enters
    // standby and PRESETS saves what is playing. SOURCE and TUNING have no long action at all.
    switch (index)
    {
      case 0:
        if (_sleepService is null)
        {
          _logger.LogDebug("Volume long-press ignored: no sleep service is registered");
          return;
        }

        _ = EnterStandbyAsync();
        break;

      case 2:
        // No PublishHold here. The save's own notice is a selector phase, and EncoderHudService
        // clears IsHolding for it, so the ring collapses without a hold-phase card being sent -
        // which is what keeps a label-only HoldCommit card from replacing the overlay.
        _presetSelector.LongPress();
        break;
    }
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
    // Only VOLUME and PRESETS have a long action, so only they publish hold phases. A ring drawing
    // on SOURCE or TUNING would promise something neither performs.
    if (index == 2)
    {
      // PRESETS publishes the START edge only, and only while the selector list is not already up.
      //
      // Nothing else is published because a save always ends in a SelectorNotice, and
      // EncoderHudService clears IsHolding for any selector phase, so the ring collapses there.
      // Publishing HoldCancel or HoldCommit here instead would send a label-only card with no rows,
      // and since EncoderLongPressGesture raises ShortPress BEFORE HoldCancelled, a sub-threshold
      // press would open the overlay and then have it wiped by that card.
      //
      // The IsOpen check is the same hazard on the START edge. EncoderLongPressGesture raises
      // HoldStarted on the PRESS-DOWN edge - not when the ring reaches its 300 ms draw threshold -
      // and a hold phase is not a selector phase, so EncoderHudService.Publish would swap the open
      // list for a label-only card on EVERY press - from the press-down edge until the press
      // resolves, on release or at the threshold. That is this row's primary interaction (turn to
      // preview, press to recall) broken on every press.
      //
      // The trade, stated plainly: a hold begun with the overlay ALREADY OPEN draws no ring. The
      // save still happens and its notice still lands at the 600 ms threshold; only the ring is
      // missing. The documented save gesture - tune a station, hold PRESETS with no overlay up -
      // keeps its ring.
      if (phase != EncoderHudPhase.HoldStart || _presetSelector.IsOpen)
      {
        return;
      }

      _hud.Publish(new EncoderHudEventArgs
      {
        EncoderIndex = 2,
        Label = "HOLD TO SAVE",
        Phase = EncoderHudPhase.HoldStart,
      });
      return;
    }

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
  /// What the sleep model does with one encoder input, decided before any handler runs.
  /// </summary>
  private enum SleepGateOutcome
  {
    /// <summary>Run the handler. The console is awake, or this is VOLUME on the lit Ambient clock.</summary>
    Dispatch,

    /// <summary>Spend this input waking: publish this knob's current value, start the wake, run no handler.</summary>
    ConsumeAndWake,

    /// <summary>Spend this input: publish this knob's current value, run no handler, and do not wake.</summary>
    Consume,
  }

  /// <summary>
  /// Applies handoff §8.3's two surviving columns to one input.
  ///
  /// <para>
  /// Rule 2 on a lit panel: VOLUME acts in place and everything else is spent waking. Standby adds
  /// D22 on top of it — a <b>turn</b> never resumes audio, only a press or a screen tap does — so a
  /// turn there is consumed without a wake. <b>The two dark states are withdrawn by
  /// <c>ENC-15</c></b>, so Rule 1 has no reachable state and appears nowhere below.
  /// </para>
  ///
  /// <para>
  /// ⚠ <paramref name="index"/> is compared against
  /// <see cref="RotaryEncoderConfigDefaults.VolumeEncoderIndex"/> and nothing else, which is why
  /// this survives the ENC-5 / ENC-7 remap: index 0 is VOLUME under both the current handler table
  /// and the remapped one, and every other index reaches the same branch.
  /// </para>
  /// </summary>
  private SleepGateOutcome GateInput(int index, bool isTurn)
  {
    if (_sleepService is null)
    {
      return SleepGateOutcome.Dispatch;
    }

    switch (_sleepService.WakeState)
    {
      case ConsoleWakeState.Ambient when index == RotaryEncoderConfigDefaults.VolumeEncoderIndex:
        // The handler runs and publishes as usual; the card lands on Sleep.razor's own HUD host,
        // which is why this needs no code of its own.
        return SleepGateOutcome.Dispatch;

      case ConsoleWakeState.Standby when isTurn:
        return SleepGateOutcome.Consume;

      case ConsoleWakeState.Ambient:
      case ConsoleWakeState.Standby:
        // A lost claim means an earlier input in this same burst already started the wake, so
        // WakeState now reads Awake and dispatching is the correct answer rather than a fallback:
        // a fast spin must lose one detent, not twelve.
        return _sleepService.TryClaimWake()
          ? SleepGateOutcome.ConsumeAndWake
          : SleepGateOutcome.Dispatch;

      default:
        return SleepGateOutcome.Dispatch;
    }
  }

  // Task 6 replaces this body with the real readout. It is a no-op for exactly one commit so the
  // gate can be reviewed on its own; the tests that force it to publish are in Task 6.
  private void PublishCurrentValue(int index)
  {
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

  // --- Encoder 2: PRESETS ---

  private void HandlePresetsTurn(int index, int delta)
  {
    // ENC-3 clamp: the same one-entry-per-detent bound SOURCE uses, which is what keeps the two
    // adjacent selector knobs interchangeable in the hand.
    //
    // Not "always", though, and the difference is the first detent of a session: PRESETS opens on
    // an empty list and fills it from the background bank read that opening starts, so that detent
    // moves nothing. SOURCE recomposes synchronously before it moves, so its first detent already
    // moves an entry. From the second detent on the two knobs behave identically.
    _presetSelector.EncoderIndex = index;
    _presetSelector.Turn(Clamp(delta, RotaryEncoderConfigDefaults.SelectorClamp));
  }

  private void HandlePresetsPress(int index)
  {
    // Set here as well as on turn, for the same reason HandleSourcePress does it: a press is the
    // other way the overlay opens, and if only the turn path set it, a press-first interaction
    // would render the overlay against whatever index was last turned.
    _presetSelector.EncoderIndex = index;
    _presetSelector.Press();
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
