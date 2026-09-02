using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Interfaces.Input;
using Radio.Core.Models.Audio;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// The SOURCE knob's list, preview and commit (ENC-5).
///
/// <para>
/// The list is a <b>band selector</b> (D7): the tuner's bands are first-class entries, the way the
/// original cabinet's selector read BROADCAST / SHORTWAVE / PHONO. Committing a band while the radio
/// is already active is a <b>band change</b> — no engine teardown, no fade, no spinner — while
/// committing one from another source is a real source switch that gets all three.
/// </para>
///
/// <para>
/// Composition is resolved once, on the first open after a radio source exists, and cached for the
/// process. Handoff §4.4: "positions never move" is only achievable if the set does not change under
/// the user's hand, so a row that is unavailable is <b>dimmed with a reason</b> rather than removed.
/// </para>
///
/// <para>
/// The rows, the highlight, the composed band set and the dismiss timer are read and written under
/// <c>_gate</c>. The two writers are the HID read loop (turns and presses, through the router) and
/// the dismiss timer's callback. Payloads are built under that lock and published outside it, so no
/// subscriber's work runs while the gate is held. <see cref="EncoderIndex"/> is outside it: it is
/// where the card renders rather than part of the preview, and the router is its only writer.
/// </para>
/// </summary>
public sealed class SourceSelectorService : IDisposable
{
  /// <summary>
  /// The order of the list, fixed. Not <c>Enum.GetValues</c> order — <c>RadioBand</c> declares AM
  /// first — and not recency: a physical selector whose detent 3 is Bluetooth on Tuesday and Phono
  /// on Wednesday is not a physical selector.
  /// </summary>
  private static readonly RadioBand[] BandOrder = [RadioBand.FM, RadioBand.AM, RadioBand.SW, RadioBand.WB];

  /// <summary>
  /// The non-radio entries, in the handoff's §4.4 order. <c>AudioSourceType.Radio</c> is absent on
  /// purpose: the bands above are the radio, and a seventh row reading "RADIO" would be a second way
  /// to reach the same place from the same list.
  /// </summary>
  private static readonly AudioSourceType[] SourceOrder =
    [AudioSourceType.Bluetooth, AudioSourceType.Vinyl, AudioSourceType.GenericUSB, AudioSourceType.FilePlayer];

  private const string BandIdPrefix = "band:";
  private const string SourceIdPrefix = "source:";

  private readonly ILogger<SourceSelectorService> _logger;
  private readonly Func<IAudioManager> _audioManagerFactory;
  // Func<> for the same reason the router uses one for IAudioManager: it defers resolution past
  // container build, so the minimal provider in RotaryEncoderRegistrationTests still resolves the
  // router without needing the configuration store IRadioBandMemory reads through.
  private readonly Func<IRadioBandMemory> _bandMemoryFactory;
  private readonly IEncoderFeedbackSink _hud;
  private readonly TimeProvider _timeProvider;
  private readonly EncoderSelectorState _state = new();
  private readonly object _gate = new();

  private RadioBand[]? _composedBands;
  private ITimer? _dismissTimer;
  private bool _disposed;

  public SourceSelectorService(
    ILogger<SourceSelectorService> logger,
    Func<IAudioManager> audioManagerFactory,
    Func<IRadioBandMemory> bandMemoryFactory,
    IEncoderFeedbackSink hud,
    TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _audioManagerFactory = audioManagerFactory;
    _bandMemoryFactory = bandMemoryFactory;
    _hud = hud;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  /// <summary>
  /// The encoder index this overlay renders above. Passed by the router so the geometry follows the
  /// knob rather than a constant this class would have to be edited to change.
  /// </summary>
  public int EncoderIndex { get; set; } = 1;

  /// <summary>True while the overlay is open. Exposed for tests and for ENC-7's reuse.</summary>
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

  /// <summary>A turn: open if closed, then move the highlight. Nothing switches (handoff §4.4).</summary>
  public void Turn(int clampedDelta)
  {
    EncoderHudEventArgs payload;

    lock (_gate)
    {
      RecomposeLocked();
      _state.Open();
      _state.Move(clampedDelta);
      payload = ComposeLocked(
        EncoderHudPhase.SelectorPreview, EncoderInteractionTimings.SelectorIdleDismissMs);
      ArmDismissLocked(EncoderInteractionTimings.SelectorIdleDismissMs);
    }

    _hud.Publish(payload);
  }

  /// <summary>
  /// A press: commit the highlight. One rule, not two — with the overlay closed the highlight is
  /// what is already playing, so a press commits the status quo, which changes nothing and opens the
  /// overlay showing you where you are.
  /// </summary>
  public void Press()
  {
    EncoderHudEventArgs? openPayload = null;
    EncoderSelectorRow? row = null;

    lock (_gate)
    {
      RecomposeLocked();
      bool wasOpen = _state.IsOpen;
      _state.Open();

      if (!wasOpen)
      {
        // Opening. The highlight is the current row, so committing it would be a no-op anyway;
        // showing the list is the whole of the behaviour here.
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
      return;
    }

    if (row is null)
    {
      return;
    }

    if (!row.IsAvailable)
    {
      PublishBlocked();
      return;
    }

    // A commit is in flight from here. The idle dismiss is cancelled so it cannot close the overlay
    // underneath a spinner; every path out of CommitAsync publishes a terminal phase and re-arms.
    CancelDismiss();
    _ = CommitAsync(row);
  }

  /// <summary>
  /// Tears the overlay down without committing. Called when the encoder disappears mid-session —
  /// ENC-0's disconnect path — because an overlay you can no longer navigate is a trap.
  /// </summary>
  public void Dismiss()
  {
    lock (_gate)
    {
      CancelDismissLocked();
      _state.Close();
    }
  }

  // --- Commit ---------------------------------------------------------------------------------

  private async Task CommitAsync(EncoderSelectorRow row)
  {
    try
    {
      var mgr = _audioManagerFactory();

      if (row.Id.StartsWith(BandIdPrefix, StringComparison.Ordinal))
      {
        var band = Enum.Parse<RadioBand>(row.Id[BandIdPrefix.Length..]);

        if (mgr.ActiveSource is IRadioControl liveRadio)
        {
          // D7 requirement 1. Radio is already playing, so this is a BAND CHANGE, not a source
          // switch: no engine teardown, no fade, no spinner. It should feel instant because it is.
          await ApplyBandAsync(liveRadio, band);
          PublishPreview();
          return;
        }

        // D7 requirement 2. Radio is not active, so this is a real source switch AND a band change.
        PublishCommitting(row);
        var created = await mgr.GetOrCreateSourceAsync(AudioSourceType.Radio, switchToSource: true);
        if (created is not IRadioControl newRadio)
        {
          PublishFailed(row, "Tuner unavailable");
          return;
        }

        await ApplyBandAsync(newRadio, band);
        PublishPreview();
        return;
      }

      // A plain source switch. Bluetooth in particular can take seconds or fail outright, which is
      // why State D is not optional polish.
      var type = Enum.Parse<AudioSourceType>(row.Id[SourceIdPrefix.Length..]);
      PublishCommitting(row);
      var switched = await mgr.GetOrCreateSourceAsync(type, switchToSource: true);
      if (switched is null)
      {
        PublishFailed(row, $"{row.Primary} unavailable");
        return;
      }

      PublishPreview();
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error committing source selection {Id}", row.Id);
      PublishFailed(row, $"{row.Primary} unavailable");
    }
  }

  /// <summary>
  /// Sets the band and restores that band's last-tuned frequency.
  ///
  /// <para>
  /// The read-back is not defensive noise. <c>RadioAudioSource.SetBandAsync</c> logs a warning and
  /// returns <c>Task.CompletedTask</c> — it succeeds and does nothing — so a commit that trusted the
  /// absence of an exception would report a band change that never happened. The row for such a band
  /// is normally dimmed by <c>SupportedBands</c> composition and never reaches here; this covers a
  /// tuner swapped underneath a composed list.
  /// </para>
  /// </summary>
  private async Task ApplyBandAsync(IRadioControl radio, RadioBand band)
  {
    await radio.SetBandAsync(band);

    if (radio.CurrentBand != band)
    {
      _logger.LogWarning("Tuner did not change to {Band}; it reports {Actual}", band, radio.CurrentBand);
      return;
    }

    var restore = await _bandMemoryFactory().GetAsync(band);
    if (restore is { } freq)
    {
      await radio.SetFrequencyAsync(freq);
    }
  }

  // --- Composition ----------------------------------------------------------------------------

  /// <summary>
  /// Resolves the band set once, then refreshes availability and the current marker on every call.
  ///
  /// <para>
  /// Two different lifetimes on purpose. <b>Composition</b> — which rows exist — is fixed for the
  /// session so positions never move under the user's hand. <b>Availability</b> and the current
  /// marker change constantly (a phone connects, a band is switched from the touchscreen) and are
  /// recomputed each time.
  /// </para>
  /// </summary>
  private void RecomposeLocked()
  {
    var mgr = _audioManagerFactory();
    var radio = mgr.ActiveSource as IRadioControl ?? mgr.GetCachedSource(AudioSourceType.Radio) as IRadioControl;

    _composedBands ??= radio is null
      // No tuner has ever existed this session, so nothing can be asked what it supports. FM and AM
      // are rendered anyway, dimmed with a reason: handoff §4.4 wants "no tuner detected" on a row
      // that is there, not an absent row that gives the user nothing to aim at.
      ? [RadioBand.FM, RadioBand.AM]
      : BandOrder.Where(b => radio.SupportedBands.Contains(b)).ToArray();

    var rows = new List<EncoderSelectorRow>(_composedBands.Length + SourceOrder.Length);
    var activeBand = radio is not null && mgr.ActiveSource is IRadioControl ? radio.CurrentBand : (RadioBand?)null;

    foreach (var band in _composedBands)
    {
      bool available = radio is not null;
      rows.Add(new EncoderSelectorRow
      {
        Id = $"{BandIdPrefix}{band}",
        Primary = band.ToString(),
        // The current band's live frequency, so the marked row reads like the frequency well.
        Secondary = available && activeBand == band ? radio!.CurrentFrequency.ToDisplayString() : null,
        Icon = "radio",
        AccentVar = "--source-radio",
        // D7 requirement 3: the marker tracks the active BAND, not "Radio". On AM, row 2 is marked.
        IsCurrent = activeBand == band,
        IsAvailable = available,
        UnavailableReason = available ? null : "no tuner detected",
      });
    }

    foreach (var type in SourceOrder)
    {
      var cached = mgr.GetCachedSource(type);
      // Availability here is shallow, and deliberately so: IAudioManager has no reachability query,
      // so a source that has never been created reads as available. What this knows is that the
      // source has not failed. Finding out it cannot actually start is what State E is for.
      bool available = cached is null || cached.State != AudioSourceState.Error;
      rows.Add(new EncoderSelectorRow
      {
        Id = $"{SourceIdPrefix}{type}",
        Primary = DisplayNameFor(type),
        Secondary = cached?.Name,
        Icon = IconFor(type),
        AccentVar = AccentFor(type),
        IsCurrent = mgr.ActiveSource is not null && ReferenceEquals(mgr.ActiveSource, cached),
        IsAvailable = available,
        UnavailableReason = available ? null : "unavailable",
      });
    }

    _state.SetRows(rows);
  }

  /// <summary>
  /// The row labels from the handoff's §6.6 State A mock. Two of them are not the enum name —
  /// <c>Vinyl</c> reads PHONO and <c>FilePlayer</c> reads FILES — because those are the words the
  /// cabinet and the mock use.
  /// </summary>
  private static string DisplayNameFor(AudioSourceType type) => type switch
  {
    AudioSourceType.Bluetooth => "BLUETOOTH",
    AudioSourceType.Vinyl => "PHONO",
    AudioSourceType.GenericUSB => "USB",
    AudioSourceType.FilePlayer => "FILES",
    _ => type.ToString().ToUpperInvariant(),
  };

  /// <summary>
  /// Radzen icon names, matching <c>SourceTypeHelper.GetIcon</c> in Radio.Web. That helper is a Web
  /// display concern and Radio.Infrastructure cannot reference it, so the four pairs are repeated
  /// here and pinned by a test rather than shared.
  /// </summary>
  private static string IconFor(AudioSourceType type) => type switch
  {
    AudioSourceType.Bluetooth => "bluetooth",
    AudioSourceType.Vinyl => "album",
    AudioSourceType.GenericUSB => "usb",
    AudioSourceType.FilePlayer => "audio_file",
    _ => "music_note",
  };

  /// <summary>
  /// Accent custom-property names, matching <c>SourceTypeHelper.GetAccentVar</c>. This row
  /// introduces no new colour.
  /// </summary>
  private static string AccentFor(AudioSourceType type) => type switch
  {
    AudioSourceType.Bluetooth => "--source-bluetooth",
    AudioSourceType.Vinyl => "--source-vinyl",
    AudioSourceType.GenericUSB => "--source-usb",
    AudioSourceType.FilePlayer => "--source-file",
    _ => "--accent-primary",
  };

  // --- Publishing -----------------------------------------------------------------------------

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
      Label = "SOURCE",
      Phase = phase,
      Title = "SOURCE",
      Footer = "PRESS THE KNOB TO SWITCH",
      // Always the whole list — see the plan's §1.5. A rows-less selector update can be swallowed
      // by the coalescer's 50 ms replace window and leave the overlay with a highlight and no rows.
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
      RecomposeLocked();
      payload = ComposeLocked(
        EncoderHudPhase.SelectorPreview, EncoderInteractionTimings.SelectorIdleDismissMs);
      ArmDismissLocked(EncoderInteractionTimings.SelectorIdleDismissMs);
    }

    _hud.Publish(payload);
  }

  private void PublishBlocked()
  {
    EncoderHudEventArgs payload;
    lock (_gate)
    {
      // State C. The overlay stays open and the highlighted row flashes — the flash is the answer,
      // not a dismissal. The component knows which row to flash from HighlightIndex.
      payload = ComposeLocked(
        EncoderHudPhase.SelectorBlocked, EncoderInteractionTimings.SelectorBlockedFlashMs);
      // A press is interaction, so the idle window restarts here rather than continuing to run down
      // from the turn that opened the overlay.
      ArmDismissLocked(EncoderInteractionTimings.SelectorIdleDismissMs);
    }

    _hud.Publish(payload);
  }

  private void PublishCommitting(EncoderSelectorRow row)
  {
    EncoderHudEventArgs payload;
    lock (_gate)
    {
      // State D. Only ever published for a real source switch; a band change on an already-active
      // radio skips it, which is what makes that path feel instant.
      //
      // No duration: the card stays up until the switch succeeds or fails. That is why every path
      // out of CommitAsync publishes a terminal phase.
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
      // The failure card dismisses itself on the Web after SelectorFailedMs; the API-side open flag
      // follows it, so the next press does not commit into an overlay nobody can see.
      ArmDismissLocked(EncoderInteractionTimings.SelectorFailedMs);
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

  // --- Idle dismissal -------------------------------------------------------------------------

  /// <summary>
  /// Arms the timer that closes the overlay with nothing committed.
  ///
  /// <para>
  /// The Web dismisses the card on its own from <c>DurationMs</c>; this keeps the API-side open flag
  /// in step with it, because a press against a closed-but-still-open state would commit a row the
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
