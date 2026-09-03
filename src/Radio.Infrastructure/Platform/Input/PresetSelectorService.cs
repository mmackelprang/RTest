using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// The PRESETS knob's list, preview, recall and save (ENC-7).
///
/// <para>
/// Deliberately the same grammar as <see cref="SourceSelectorService"/>, on the same
/// <see cref="EncoderSelectorState"/> and through the same overlay: the two knobs are adjacent and
/// the handoff wants them interchangeable in the hand — learn one, you have learned both. What
/// differs is the contents of the list, what a commit does, and <b>when the list is composed</b>:
/// <see cref="SourceSelectorService.Turn"/> recomposes synchronously before it opens and moves, so
/// its first detent already moves an entry, while this one opens on an empty list and fills it from
/// a background bank read, so its first detent of a session moves nothing. From the second detent
/// on the two behave identically.
/// </para>
///
/// <para>
/// <b>The list is never filtered by the active source.</b> That is what keeps the knob alive from
/// Bluetooth or Phono: turn it from anywhere and your stations are there, and pressing one switches
/// source and band to get to it.
/// </para>
///
/// <para>
/// <b>Scope factory, not an injected repository.</b> <see cref="IRadioPresetService"/> is registered
/// scoped and this service is a singleton driven by the HID read loop, which has no request scope.
/// A singleton may not capture a scoped service — the container either refuses the injection or
/// satisfies it once out of a scope that then outlives its use — so a scope is opened per operation
/// instead. <b>That buys lifetime legality and nothing else.</b> The repository reads through
/// <c>FingerprintDbContext</c>, which is registered <b>singleton</b> and hands every caller the same
/// <c>SqliteConnection</c>, so a fresh repository from a fresh scope still works over the same
/// connection the API's request-scoped consumers use. No isolation is claimed or obtained here; the
/// hazard that leaves is recorded in <c>design/FUTURE-WORK.md</c>.
/// </para>
///
/// <para>
/// <b>The bank is read when the overlay opens, after a save and after a recall — not on every
/// detent</b> (see <see cref="RefreshAsync"/>). A turn on an already-open overlay moves the
/// highlight and publishes, and performs no I/O at all.
/// </para>
///
/// <para>
/// The rows, the highlight and the dismiss timer are read and written under <c>_gate</c>. The
/// writers are the HID read loop (turns, presses and holds, through the router), the fire-and-forget
/// tasks those start, and the dismiss timer's callback. Payloads are built under that lock and
/// published outside it, so no subscriber's work runs while the gate is held.
/// <see cref="EncoderIndex"/> is outside it: it is where the card renders rather than part of the
/// preview, and the router is its only writer.
/// </para>
/// </summary>
public sealed class PresetSelectorService : IDisposable
{
  private const string PresetIdPrefix = "preset:";

  /// <summary>
  /// How close two frequencies must be, in hertz, to count as the same station. The same tolerance
  /// <c>RadioControlPanel.IsActivePreset</c> uses, so the knob's current marker and the on-screen
  /// rail's <c>.is-active</c> cue cannot disagree about the same row.
  /// </summary>
  private const double SameStationHertz = 1.0;

  private readonly ILogger<PresetSelectorService> _logger;
  private readonly IServiceScopeFactory _scopeFactory;
  private readonly Func<IAudioManager> _audioManagerFactory;
  // Func<> for the same reason SourceSelectorService uses one: it defers resolution past container
  // build, so the minimal provider in RotaryEncoderRegistrationTests still resolves the router
  // without the audio graph or the configuration store IRadioBandMemory reads through.
  private readonly Func<IRadioBandMemory> _bandMemoryFactory;
  private readonly IEncoderFeedbackSink _hud;
  private readonly TimeProvider _timeProvider;
  private readonly EncoderSelectorState _state = new();
  private readonly object _gate = new();

  private ITimer? _dismissTimer;
  private bool _disposed;

  public PresetSelectorService(
    ILogger<PresetSelectorService> logger,
    IServiceScopeFactory scopeFactory,
    Func<IAudioManager> audioManagerFactory,
    Func<IRadioBandMemory> bandMemoryFactory,
    IEncoderFeedbackSink hud,
    TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _scopeFactory = scopeFactory;
    _audioManagerFactory = audioManagerFactory;
    _bandMemoryFactory = bandMemoryFactory;
    _hud = hud;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  /// <summary>
  /// The encoder index this overlay renders above, set by the router. Passed by the router so the
  /// geometry follows the knob rather than a constant this class would have to be edited to change.
  /// </summary>
  public int EncoderIndex { get; set; } = 2;

  /// <summary>True while the overlay is open. Exposed for tests and for the router's teardown.</summary>
  public bool IsOpen
  {
    get
    {
      lock (_gate)
      {
        return _state.IsOpen;
      }
    }
  }

  /// <summary>
  /// A turn: open if closed, then move the highlight. <b>Nothing plays.</b>
  ///
  /// <para>
  /// A turn that opens the overlay also kicks a background bank read; a turn on an already-open
  /// overlay moves the highlight against the rows already loaded and reads nothing. The first turn
  /// of a session therefore opens on an empty list and fills in once the read returns, which is the
  /// correct trade: blocking the HID read loop on a database read is how a knob becomes laggy, and
  /// a read per detent is load on a box where incidental load is audible.
  /// </para>
  /// </summary>
  public void Turn(int clampedDelta)
  {
    EncoderHudEventArgs payload;
    bool opened;

    lock (_gate)
    {
      opened = !_state.IsOpen;
      _state.Open();
      _state.Move(clampedDelta);
      payload = ComposeLocked(
        EncoderHudPhase.SelectorPreview, EncoderInteractionTimings.SelectorIdleDismissMs);
      ArmDismissLocked(EncoderInteractionTimings.SelectorIdleDismissMs);
    }

    _hud.Publish(payload);

    if (opened)
    {
      _ = RefreshAsync();
    }
  }

  /// <summary>
  /// A press: recall the highlighted preset. With the overlay closed this opens it instead — the
  /// same one-rule press SOURCE has, so a mis-grab in the middle of the panel costs nothing.
  /// </summary>
  public void Press()
  {
    EncoderHudEventArgs? openPayload = null;
    EncoderSelectorRow? row = null;

    lock (_gate)
    {
      bool wasOpen = _state.IsOpen;
      _state.Open();

      if (!wasOpen)
      {
        openPayload = ComposeLocked(
          EncoderHudPhase.SelectorPreview, EncoderInteractionTimings.SelectorIdleDismissMs);
        ArmDismissLocked(EncoderInteractionTimings.SelectorIdleDismissMs);
      }
      else
      {
        row = _state.Highlighted;
      }
    }

    if (openPayload is not null)
    {
      _hud.Publish(openPayload);
      _ = RefreshAsync();
      return;
    }

    if (row is null)
    {
      // The overlay is open with nothing highlighted — an empty bank, on the one screen with no
      // other affordance. Returning silently would publish nothing and leave the idle window
      // running from whatever input opened the overlay, so pressing repeatedly would let the card
      // vanish under the user's finger. Republishing re-shows the instructional empty state and
      // re-arms that window, which is this row's "never a silent no-op" rule.
      PublishPreview();
      return;
    }

    // A recall is in flight from here. The idle dismiss is cancelled so it cannot close the overlay
    // underneath a spinner; every path out of RecallAsync publishes a terminal phase and re-arms.
    CancelDismiss();
    _ = RecallAsync(row);
  }

  /// <summary>
  /// A 600 ms hold: save what is playing.
  ///
  /// <para>
  /// The write appends; <see cref="IRadioPresetService.AddPresetAsync"/> has no overwrite path at
  /// all, so "never overwrites" is a property of that service rather than a guard this method
  /// applies. The slot number reported afterwards is the per-band ordinal derived from creation
  /// order — nothing is searched for and no gap is filled, because deleting a preset renumbers the
  /// ones after it and gaps cannot exist.
  /// </para>
  /// </summary>
  public void LongPress()
  {
    // Same reason as the press: a save is in flight, and its notice arms its own window when it
    // lands.
    CancelDismiss();
    _ = SaveAsync();
  }

  /// <summary>
  /// Tears the overlay down without recalling or saving. Called when the encoder disappears
  /// mid-session — ENC-0's disconnect path — because an overlay you can no longer navigate is a
  /// trap.
  /// </summary>
  public void Dismiss()
  {
    lock (_gate)
    {
      CancelDismissLocked();
      _state.Close();
    }
  }

  // --- Composition ------------------------------------------------------------------------------

  /// <summary>
  /// Reloads the bank and, when the composed list differs from the rows the overlay is holding,
  /// replaces them — republishing a preview when <paramref name="publish"/> is set and the overlay
  /// is open.
  ///
  /// <para>
  /// <b>Called on three occasions, not on every detent:</b> when the overlay opens (by a turn or by
  /// a press), after a save, and after a recall. Turning an open overlay does no I/O. The difference
  /// is measurable rather than theoretical on this appliance, where incidental database and log load
  /// correlates with audible distortion.
  /// </para>
  ///
  /// <para>
  /// The consequence to know: a preset deleted from the touchscreen while the overlay is open stays
  /// in the list until the overlay next opens, so pressing it recalls an id the bank no longer has.
  /// <see cref="RecallAsync"/> resolves that case into a failure card rather than a crash.
  /// </para>
  ///
  /// <para>
  /// The difference test is on the composed content — count, and each row's identity, text, ordinal
  /// and current marker — rather than on reference equality, so an unchanged bank costs no publish
  /// and the steady state stays at one card per detent.
  /// </para>
  ///
  /// <para>
  /// Ordering matches the on-screen rail exactly — band, then the per-band slot ordinal — so the
  /// knob's list and the list the user can see are the same list in the same order. Note the three
  /// orderings already in this stack: the repository sorts by Name, RadioControlPanel re-sorts by
  /// band/slot/created, and RadioPage sorts by CreatedAt. This follows RadioControlPanel, because
  /// that is the bank this knob is a remote control for.
  /// </para>
  /// </summary>
  private async Task RefreshAsync(bool publish = true)
  {
    EncoderHudEventArgs? payload = null;

    try
    {
      IReadOnlyList<RadioPreset> presets;
      using (var scope = _scopeFactory.CreateScope())
      {
        presets = await scope.ServiceProvider
          .GetRequiredService<IRadioPresetService>()
          .GetAllPresetsAsync();
      }

      var live = _audioManagerFactory().ActiveSource as IRadioControl;
      var rows = ComposeRows(presets, live);

      lock (_gate)
      {
        if (SignatureOf(rows) == SignatureOf(_state.Rows))
        {
          return;
        }

        _state.SetRows(rows);

        if (publish && _state.IsOpen)
        {
          payload = ComposeLocked(
            EncoderHudPhase.SelectorPreview, EncoderInteractionTimings.SelectorIdleDismissMs);
          ArmDismissLocked(EncoderInteractionTimings.SelectorIdleDismissMs);
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error loading the preset bank for the PRESETS knob");

      // A read that throws while the overlay is up would otherwise leave State B on screen, and
      // State B reads "NO STATIONS SAVED" — a bank that could not be read, reported as a bank that
      // is empty. The notice replaces the list for its own duration and re-arms the idle window.
      //
      // Published regardless of the publish flag. That flag exists to stop a routine preview
      // replacing a card the caller has just put up deliberately - a notice or a failure - not to
      // suppress an error the user is looking straight at.
      if (IsOpen)
      {
        PublishNotice(
          "Could not read your presets", null, EncoderInteractionTimings.SelectorNoticeMs);
      }

      return;
    }

    if (payload is not null)
    {
      _hud.Publish(payload);
    }
  }

  /// <summary>One row per saved station, in the on-screen rail's order.</summary>
  private static List<EncoderSelectorRow> ComposeRows(
    IReadOnlyList<RadioPreset> presets,
    IRadioControl? live) =>
    InSlotOrder(presets)
      .Select(x => new EncoderSelectorRow
      {
        Id = $"{PresetIdPrefix}{x.Preset.Id}",
        Primary = x.Preset.Name,
        // Band in the secondary line because slot ordinals are per band: two rows can both read
        // "01", and the band is what tells them apart (the plan's D-1).
        Secondary = $"{x.Preset.Band} {new Frequency((long)x.Preset.Frequency).ToDisplayString()}",
        Ordinal = x.Slot.ToString("00"),
        AccentVar = "--source-radio",
        IsCurrent = live is not null
          && live.CurrentBand == x.Preset.Band
          && Math.Abs(live.CurrentFrequency.Hertz - x.Preset.Frequency) < SameStationHertz,
        // Always true. A saved station is always recallable, because recall creates whatever source
        // it needs; a speculative availability check would dim a row for a recall that would have
        // worked.
        IsAvailable = true,
      })
      .ToList();

  /// <summary>
  /// The bank in the order the on-screen rail shows it, each preset paired with its <b>per-band</b>
  /// ordinal.
  ///
  /// <para>
  /// The same derivation <c>RadioController.GetPresets</c> performs on every request: group by band,
  /// order each group by creation time, and number from one within the band. There is no stored slot
  /// — neither <see cref="RadioPreset"/> nor the <c>RadioPresets</c> table has one — so the number on
  /// the knob is a projection computed here, exactly as the number on the screen is.
  /// </para>
  /// </summary>
  private static List<(RadioPreset Preset, int Slot)> InSlotOrder(IReadOnlyList<RadioPreset> presets) =>
    presets
      .GroupBy(p => p.Band)
      .SelectMany(g => g.OrderBy(p => p.CreatedAt).Select((p, i) => (Preset: p, Slot: i + 1)))
      .OrderBy(x => x.Preset.Band.ToString(), StringComparer.Ordinal)
      .ThenBy(x => x.Slot)
      .ToList();

  /// <summary>
  /// A cheap content fingerprint of a composed list, used to decide whether a reload changed
  /// anything worth publishing. Covers exactly the fields the overlay draws from a row and this
  /// service can change between reads.
  /// </summary>
  private static string SignatureOf(IReadOnlyList<EncoderSelectorRow> rows)
  {
    var sb = new StringBuilder().Append(rows.Count);

    foreach (var row in rows)
    {
      // A unit separator between rows, so no run of row text can spell another list's
      // signature.
      sb.Append('\u001f')
        .Append(row.Id).Append('|')
        .Append(row.Primary).Append('|')
        .Append(row.Secondary).Append('|')
        .Append(row.Ordinal).Append('|')
        .Append(row.IsCurrent ? '1' : '0');
    }

    return sb.ToString();
  }

  // --- Recall -----------------------------------------------------------------------------------

  /// <summary>
  /// Switches source and band as needed, tunes, and plays.
  ///
  /// <para>
  /// Not routed through <c>POST /api/radio/presets/{id}/load</c>: that endpoint resolves the tuner
  /// with <c>GetActiveRadioSource()</c> and returns 400 when radio is not already active — which is
  /// exactly the case this knob exists to serve. The steps below are the same three the endpoint
  /// performs, preceded by the source switch it cannot do.
  /// </para>
  /// </summary>
  private async Task RecallAsync(EncoderSelectorRow row)
  {
    string id = row.Id[PresetIdPrefix.Length..];

    try
    {
      RadioPreset? preset;
      using (var scope = _scopeFactory.CreateScope())
      {
        preset = await scope.ServiceProvider
          .GetRequiredService<IRadioPresetService>()
          .GetPresetByIdAsync(id);
      }

      if (preset is null)
      {
        // Deleted from the touchscreen after this overlay last read the bank.
        PublishFailed(row, "That preset is gone");
        await RefreshAsync(publish: false);
        return;
      }

      var mgr = _audioManagerFactory();

      if (mgr.ActiveSource is not IRadioControl radio)
      {
        // A real source switch: fade, spinner, and a failure card if it does not come up.
        PublishCommitting(row);
        if (await mgr.GetOrCreateSourceAsync(AudioSourceType.Radio, switchToSource: true) is not IRadioControl created)
        {
          PublishFailed(row, "Tuner unavailable");
          return;
        }

        radio = created;
      }

      if (radio.IsScanning)
      {
        // The touchscreen's recall does this too. Tuning under a running scan lands somewhere else
        // a second later.
        await radio.StopScanAsync();
      }

      await radio.SetBandAsync(preset.Band);
      await radio.SetFrequencyAsync(new Frequency((long)preset.Frequency));

      // Recall is also a tune, so the band memory learns from it the same way a knob turn does.
      await _bandMemoryFactory().SetAsync(preset.Band, new Frequency((long)preset.Frequency));

      // The bank is re-read because the current marker has just moved to this row; the preview is
      // published unconditionally afterwards because a recall from another source put a spinner up
      // and State D stays on screen until something terminal replaces it.
      await RefreshAsync(publish: false);
      PublishPreview();
      _logger.LogInformation(
        "Recalled preset {Name} ({Band} {Frequency})", preset.Name, preset.Band, preset.Frequency);
    }
    catch (Exception ex)
    {
      // Deliberately not narrowed. SDRRadioAudioSource throws ArgumentOutOfRangeException from
      // SetFrequencyAsync and ArgumentException from SetBandAsync, and an unobserved task exception
      // here would leave the user in silence with a spinner up.
      _logger.LogError(ex, "Error recalling preset {Id}", id);
      PublishFailed(row, "Could not tune that station");
    }
  }

  // --- Save -------------------------------------------------------------------------------------

  private async Task SaveAsync()
  {
    try
    {
      // The bank is read before anything is reported. ComposeLocked builds TitleSuffix from
      // _state.Rows and the overlay draws that title row for a notice as well as for a list, but a
      // hold never opens the overlay — and the bank is otherwise read only on open, after a save
      // and after a recall. Without this the first save of a cold session heads its notice
      // "PRESETS · 0 saved", and every later one shows the count from before that save.
      //
      // This is not the "no I/O per detent" rule being broken: that rule is about turns. A hold is
      // a discrete gesture, so this is one read per gesture.
      await RefreshAsync(publish: false);

      var mgr = _audioManagerFactory();

      if (mgr.ActiveSource is not IRadioControl radio)
      {
        // The one context-limited gesture in the spec, and it says so out loud rather than failing
        // silently. Cross-source presets need a favourites model that does not exist (v2).
        PublishNotice(
          "Only radio stations can be saved", null, EncoderInteractionTimings.SelectorNoticeShortMs);
        return;
      }

      var band = radio.CurrentBand;
      double hz = radio.CurrentFrequency.Hertz;
      string name = NameFor(radio, band, hz);

      using var scope = _scopeFactory.CreateScope();
      var presets = scope.ServiceProvider.GetRequiredService<IRadioPresetService>();

      if (await presets.PresetExistsAsync(band, hz))
      {
        int slot = await SlotOfAsync(presets, band, hz);
        PublishNotice(
          $"ALREADY SAVED · slot {slot:00}", name, EncoderInteractionTimings.SelectorNoticeShortMs);
        return;
      }

      var saved = await presets.AddPresetAsync(name, band, hz);
      int newSlot = await SlotOfAsync(presets, band, hz);

      // Reloaded BEFORE the notice, so the notice's title suffix counts the row just written. Not
      // republished, because a preview published here would replace the notice immediately — a new
      // row always changes the list.
      await RefreshAsync(publish: false);

      PublishNotice(
        $"Saved to {newSlot:00}",
        $"{saved.Name} · {new Frequency((long)hz).ToDisplayString()} {band}",
        EncoderInteractionTimings.SelectorNoticeMs);

      _logger.LogInformation("Saved preset {Name} to {Band} slot {Slot}", saved.Name, band, newSlot);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.Ordinal))
    {
      // The station was written between the PresetExistsAsync check above and the add — the only
      // way to reach this arm, since that check is what normally reports a duplicate.
      //
      // ⚠ THE ORDER IS WHAT MAKES THIS SAFE, not the filter. RadioPresetService's duplicate
      // message interpolates the existing preset's NAME — "A preset already exists for {band} -
      // {frequency}: {name}" — so a station called e.g. "Maximum Rock" satisfies the "full" filter
      // below as well. C# takes the first arm whose filter passes, so the more specific match has
      // to be listed first; moving this arm below the next one reports that duplicate as a full
      // bank.
      //
      // No slot number: presets, band and hz are locals of the try block and are out of scope
      // here, and the scope that owned the repository is disposed on the way out of it. What this
      // notice has to say is that nothing was written.
      PublishNotice(
        "ALREADY SAVED",
        "that station is already in your presets",
        EncoderInteractionTimings.SelectorNoticeShortMs);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("Maximum", StringComparison.Ordinal))
    {
      // The bank is full. Nothing is written, and replacement stays on the touchscreen where it has
      // a confirmation and an undo.
      //
      // ⚠ This matches on a message string because that is the contract RadioPresetService offers
      // today — it signals "full" and "duplicate" through InvalidOperationException text and nothing
      // else. RadioController.cs already matches the same two strings. It is debt, recorded in
      // design/FUTURE-WORK.md as a typed-exception candidate, and not a pattern to copy.
      PublishNotice(
        "PRESETS FULL", "replace a slot on screen", EncoderInteractionTimings.SelectorNoticeMs);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error saving a preset from the PRESETS knob");
      PublishNotice(
        "Could not save that station", null, EncoderInteractionTimings.SelectorNoticeMs);
    }
  }

  /// <summary>
  /// What to call the station being saved: the stable RDS name when the tuner has one, otherwise the
  /// band and the formatted frequency.
  ///
  /// <para>
  /// The fallback is spelled out here, and an explicit non-blank name is always passed to
  /// <see cref="IRadioPresetService.AddPresetAsync"/>, so that method's own default is never
  /// reached. <c>RadioPreset.GetDefaultName</c> formats the frequency as display units while
  /// <c>RadioPreset.Frequency</c> holds hertz, so it would name this preset "FM - 101500000.0". The
  /// wording here matches <c>RadioControlPanel.OpenSavePresetDialog</c>'s own fallback, which is the
  /// dialog this gesture replaces.
  /// </para>
  /// </summary>
  private static string NameFor(IRadioControl radio, RadioBand band, double hz)
  {
    string? stable = radio.RdsStationNameStable;
    return string.IsNullOrWhiteSpace(stable)
      ? $"{band} {new Frequency((long)hz).ToDisplayString()}"
      : stable;
  }

  /// <summary>
  /// The per-band ordinal of the preset at <paramref name="band"/> / <paramref name="hz"/>, or 0
  /// when the bank holds no such preset.
  ///
  /// <para>
  /// It reloads the bank because <see cref="IRadioPresetService.AddPresetAsync"/> returns a
  /// <see cref="RadioPreset"/> and that model carries no slot number — the ordinal is a projection
  /// of creation order within a band, not stored state. Adding one to the persisted model to save
  /// this query would turn a projection into state that every delete has to maintain.
  /// </para>
  /// </summary>
  private static async Task<int> SlotOfAsync(IRadioPresetService presets, RadioBand band, double hz)
  {
    var all = await presets.GetAllPresetsAsync();

    foreach (var (preset, slot) in InSlotOrder(all))
    {
      if (preset.Band == band && Math.Abs(preset.Frequency - hz) < SameStationHertz)
      {
        return slot;
      }
    }

    return 0;
  }

  // --- Publishing -------------------------------------------------------------------------------

  /// <summary>
  /// Builds one selector payload. Every publish goes through here, so the title, footer and — most
  /// importantly — the full row list cannot be forgotten on one path and present on another.
  /// </summary>
  private EncoderHudEventArgs ComposeLocked(
    EncoderHudPhase phase,
    int? durationMs = null,
    string? primary = null,
    string? secondary = null) =>
    new()
    {
      EncoderIndex = EncoderIndex,
      // ENC-4's Label is required and drives the card's label row when a phase renders as a card.
      Label = "PRESETS",
      Phase = phase,
      Title = "PRESETS",
      TitleSuffix = $"{_state.Rows.Count} saved",
      Footer = "PRESS TO PLAY · HOLD TO SAVE",
      EmptyPrimary = "NO STATIONS SAVED",
      // Lower case on purpose: it is a sentence spoken to the user, not a label, and §6.6's mock
      // draws it that way. .encoder-selector-empty-secondary does not uppercase it.
      EmptySecondary = "hold this knob to save what's playing",
      // Always the whole list. EncoderFeedbackService coalesces by replacing the pending update for
      // an encoder, so a rows-less update arriving inside the 50 ms window would leave the overlay
      // with a highlight and no rows.
      Rows = _state.Rows,
      HighlightIndex = _state.HighlightIndex,
      DurationMs = durationMs,
      PrimaryText = primary,
      SecondaryText = secondary,
    };

  private void PublishPreview()
  {
    EncoderHudEventArgs payload;
    lock (_gate)
    {
      payload = ComposeLocked(
        EncoderHudPhase.SelectorPreview, EncoderInteractionTimings.SelectorIdleDismissMs);
      ArmDismissLocked(EncoderInteractionTimings.SelectorIdleDismissMs);
    }

    _hud.Publish(payload);
  }

  private void PublishCommitting(EncoderSelectorRow row)
  {
    EncoderHudEventArgs payload;
    lock (_gate)
    {
      // State D. Only ever published when the recall has to create the radio source; recalling
      // while the tuner is already active skips it, which is what makes that path feel instant.
      //
      // No duration: the card stays up until the recall succeeds or fails. That is why every path
      // out of RecallAsync publishes a terminal phase.
      payload = ComposeLocked(
        EncoderHudPhase.SelectorCommitting,
        durationMs: null,
        primary: $"Switching to {row.Primary}…");
    }

    _hud.Publish(payload);
  }

  private void PublishFailed(EncoderSelectorRow row, string reason)
  {
    EncoderHudEventArgs payload;
    lock (_gate)
    {
      // State E. The second line is what is STILL PLAYING, which is the part that stops the user
      // concluding the knob is broken.
      payload = ComposeLocked(
        EncoderHudPhase.SelectorFailed,
        EncoderInteractionTimings.SelectorFailedMs,
        primary: reason,
        secondary: $"Staying on {CurrentDescription()}");
      ArmDismissLocked(EncoderInteractionTimings.SelectorFailedMs);
    }

    _hud.Publish(payload);
  }

  /// <summary>
  /// State F. A short message that replaces the list for its own duration — the three save
  /// boundaries and the save's own result.
  /// </summary>
  private void PublishNotice(string primary, string? secondary, int durationMs)
  {
    EncoderHudEventArgs payload;
    lock (_gate)
    {
      payload = ComposeLocked(EncoderHudPhase.SelectorNotice, durationMs, primary, secondary);
      // The Web dismisses the notice from DurationMs; this keeps the API-side open flag in step, so
      // the next press does not recall into an overlay nobody can see.
      ArmDismissLocked(durationMs);
    }

    _hud.Publish(payload);
  }

  /// <summary>
  /// What is playing right now, for State E's second line — the current band and frequency on radio,
  /// otherwise the active source's name, otherwise nothing.
  /// </summary>
  private string CurrentDescription()
  {
    var mgr = _audioManagerFactory();
    return mgr.ActiveSource switch
    {
      IRadioControl radio => $"{radio.CurrentBand} {radio.CurrentFrequency.ToDisplayString()}",
      { } source => source.Name,
      _ => "nothing",
    };
  }

  // --- Idle dismissal ---------------------------------------------------------------------------

  /// <summary>
  /// Arms the timer that closes the overlay with nothing recalled and nothing saved.
  ///
  /// <para>
  /// The Web dismisses the card on its own from <c>DurationMs</c>; this keeps the API-side open flag
  /// in step with it, because a press against a closed-but-still-open state would recall a row the
  /// user can no longer see.
  /// </para>
  /// </summary>
  private void ArmDismissLocked(int delayMs)
  {
    CancelDismissLocked();

    if (_disposed)
    {
      return;
    }

    _dismissTimer = _timeProvider.CreateTimer(
      _ => Dismiss(),
      null,
      TimeSpan.FromMilliseconds(delayMs),
      Timeout.InfiniteTimeSpan);
  }

  private void CancelDismiss()
  {
    lock (_gate)
    {
      CancelDismissLocked();
    }
  }

  private void CancelDismissLocked()
  {
    _dismissTimer?.Dispose();
    _dismissTimer = null;
  }

  public void Dispose()
  {
    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }

      _disposed = true;
      CancelDismissLocked();
    }
  }
}
