using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Web.Models;
using Radio.Web.Services.Hub;

namespace Radio.Web.Services;

/// <summary>
/// Owns what the encoder HUD is currently showing, and for how long.
///
/// <para>
/// <b>Singleton, not scoped</b> — unlike <see cref="GainPopoverService"/>, which is per-circuit
/// because it tracks a click the user made in that circuit. This tracks a physical knob on one
/// cabinet: there is exactly one, and both hosts (MainLayout and Sleep) must agree about it. It
/// also has to survive the route change between them.
/// </para>
///
/// <para>
/// The dismissal timer (<c>EncoderInteractionTimings.HudHoldMs</c>, 2500 ms since ENC-20) lives
/// here rather than in CSS so that a new detent can re-arm it
/// without re-animating the card — continuous turning shows one stable card that stays up, which is
/// what the handoff's "re-arm" row asks for.
/// </para>
///
/// <para>
/// <see cref="StateChanged"/> is raised from whichever thread called <see cref="Publish"/> or
/// <see cref="Dismiss"/> — for the timer path that is a thread-pool thread, not a circuit's
/// renderer. Blazor subscribers must marshal with <c>InvokeAsync(StateHasChanged)</c>.
/// </para>
/// </summary>
public sealed class EncoderHudService : IDisposable
{
  private readonly TimeProvider _timeProvider;
  private readonly AudioStateHubService? _hub;
  private readonly ILogger<EncoderHudService>? _logger;
  private readonly object _gate = new();
  private ITimer? _dismissTimer;
  private bool _disposed;

  /// <summary>
  /// The <paramref name="hub"/> argument is optional so component tests can construct the service
  /// without a SignalR connection; production resolves the registered singleton.
  /// </summary>
  public EncoderHudService(
    AudioStateHubService? hub = null,
    TimeProvider? timeProvider = null,
    ILogger<EncoderHudService>? logger = null)
  {
    _timeProvider = timeProvider ?? TimeProvider.System;
    _hub = hub;
    _logger = logger;
    if (_hub is not null)
    {
      _hub.EncoderHudChanged += OnHubEvent;
      _hub.EncoderConnectionChanged += OnHubConnectionEvent;
    }
  }

  /// <summary>The card currently on screen, or null when nothing is showing.</summary>
  public EncoderHudDto? Current { get; private set; }

  /// <summary>
  /// True between a HoldStart and its HoldCancel / HoldCommit. Drives the progress ring.
  ///
  /// <para>
  /// A <c>Value</c> phase leaves this alone, so turning the knob mid-hold does not collapse the
  /// ring. Every other unrecognised phase clears it - see <see cref="Publish"/>.
  /// </para>
  /// </summary>
  public bool IsHolding { get; private set; }

  /// <summary>Fired whenever <see cref="Current"/> or <see cref="IsHolding"/> changes.</summary>
  public event Action? StateChanged;

  /// <summary>
  /// The phases this build knows how to draw. An unrecognised phase renders nothing rather than
  /// throwing, so a newer API build degrades to silence on an older kiosk (plan §2.5).
  ///
  /// <para>
  /// ENC-5 adds the five selector phases. This list gates <c>EncoderHud.razor</c>'s entire render
  /// and, through <see cref="HasRenderableCard"/>, whether <c>Sleep.razor</c> swaps its clock out
  /// for the HUD at all — so a selector phase missing from here is an overlay that never draws,
  /// not an overlay that draws wrongly.
  /// </para>
  /// </summary>
  public static bool IsKnownPhase(string? phase)
    => phase is "Value" or "HoldStart" or "HoldCancel" or "HoldCommit"
      or "SelectorPreview" or "SelectorBlocked" or "SelectorCommitting"
      or "SelectorFailed" or "SelectorNotice";

  /// <summary>
  /// True when <see cref="Current"/> holds a card this build can actually render.
  ///
  /// <para>
  /// A host that swaps its own composition out for the HUD must branch on this rather than on
  /// <see cref="Current"/> alone. Branching on the card's mere presence would hide that host's
  /// content and then draw nothing in its place, because an unrecognised phase renders nothing —
  /// on the sleep screen that is a blank panel rather than a clock.
  /// </para>
  /// </summary>
  public bool HasRenderableCard
  {
    get
    {
      EncoderHudDto? card = Current;
      return card is not null && IsKnownPhase(card.Phase);
    }
  }

  /// <summary>
  /// Shows (or updates) the card. Public so tests and the future selector overlays (ENC-5, ENC-7)
  /// drive the HUD through one entry point rather than adding a second host.
  /// </summary>
  public void Publish(EncoderHudDto dto)
  {
    ArgumentNullException.ThrowIfNull(dto);

    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }

      Current = dto;

      // Handoff 6.10 - four explicit arms, and the fourth one is the point.
      //
      // "Value" PRESERVES IsHolding: turning the knob while the button is held publishes a Value
      // card, and the ring has to keep drawing through it.
      //
      // An unrecognised phase is NOT HOLDING. The renderer draws nothing for a phase it does not
      // know, so a true IsHolding on one can never draw a ring - its only reachable effect is to
      // suspend the dismissal timer below and strand a card on a kiosk nobody is watching.
      // Unreachable while both builds know the same four names, but ENC-5 and ENC-7 add phases and
      // an API-ahead-of-Web deploy is ordinary.
      //
      // Value and the unknown case therefore need separate arms even though the obvious edit -
      // flipping the shared default to false - would fix the stranding and silently break the
      // hold-and-turn ring.
      IsHolding = dto.Phase switch
      {
        "HoldStart" => true,
        "HoldCancel" or "HoldCommit" => false,
        "Value" => IsHolding,
        _ => false,
      };

      // While a button is held the card must not time out from under the ring, so the dismissal
      // timer is armed only when nothing is being held.
      if (IsHolding)
      {
        CancelTimerLocked();
      }
      else
      {
        ArmDismissLocked(dto);
      }
    }

    RaiseStateChanged();
  }

  /// <summary>
  /// Clears the card immediately.
  ///
  /// <para>
  /// Called when the encoder device disconnects (ENC-0's <c>EncoderConnectionChanged</c>
  /// broadcast), by the dismissal timer, and by tests. The disconnect path is not decoration: the
  /// dismissal timer is suspended while a button is held, and a device that vanishes mid-hold sends
  /// no HoldCancel or HoldCommit, so without this the card would stay on screen indefinitely.
  /// </para>
  /// </summary>
  public void Dismiss()
  {
    lock (_gate)
    {
      CancelTimerLocked();
      Current = null;
      IsHolding = false;
    }

    RaiseStateChanged();
  }

  private Task OnHubEvent(EncoderHudDto dto)
  {
    Publish(dto);
    return Task.CompletedTask;
  }

  private Task OnHubConnectionEvent(EncoderConnectionDto dto)
  {
    // Only a disconnect clears the card. A connect must not, or plugging the device in would wipe
    // a readout the user is in the middle of reading.
    if (dto is { IsConnected: false })
    {
      Dismiss();
    }

    return Task.CompletedTask;
  }

  /// <summary>
  /// Raises <see cref="StateChanged"/>, absorbing anything a subscriber throws.
  ///
  /// <para>
  /// This mirrors <c>EncoderFeedbackService.Raise</c> on the API side, for the same reason and one
  /// more. A HUD update is cosmetic, so a throwing subscriber must not escalate — and the dismissal
  /// timer calls this from a thread-pool thread with no Blazor or hosting exception boundary above
  /// it, where an unhandled exception would end the process and every circuit in it, not just this
  /// card.
  /// </para>
  /// </summary>
  private void RaiseStateChanged()
  {
    try
    {
      StateChanged?.Invoke();
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Encoder HUD subscriber threw while handling a state change");
    }
  }

  private void ArmDismissLocked(EncoderHudDto dto)
  {
    CancelTimerLocked();

    // ENC-5. The payload carries how long to hold, because the handoff specifies four different
    // durations across the value card, the blocked flash, the save notice and the selector's idle
    // dismiss.
    //
    // A null duration means two different things depending on the phase, and conflating them is a
    // bug this shipped once. For an ordinary card it means "use the HudHoldMs default" — ENC-4 set
    // that to 1500 and ENC-20 raised it to 2500. For a commit in flight it means "no duration is
    // known" — the card is supposed to stay until the switch succeeds or fails (handoff §6.6
    // State D), and arming the ordinary hold against it drops the
    // spinner mid-switch on exactly the slow Bluetooth connect the spinner exists to explain, then
    // flashes it back when the terminal phase lands.
    //
    // So a commit gets the failsafe ceiling instead: long enough that no real switch reaches it,
    // bounded so that a dropped hub connection cannot strand a spinner forever the way ENC-4's
    // ring was once stranded by a device unplugged mid-hold.
    int holdMs = dto.Phase == "SelectorCommitting"
      ? dto.DurationMs ?? EncoderInteractionTimings.SelectorCommitCeilingMs
      : dto.DurationMs ?? EncoderInteractionTimings.HudHoldMs;

    _dismissTimer = _timeProvider.CreateTimer(
      _ => Dismiss(),
      null,
      TimeSpan.FromMilliseconds(holdMs),
      Timeout.InfiniteTimeSpan);
  }

  private void CancelTimerLocked()
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
      CancelTimerLocked();
    }

    if (_hub is not null)
    {
      _hub.EncoderHudChanged -= OnHubEvent;
      _hub.EncoderConnectionChanged -= OnHubConnectionEvent;
    }
  }
}
