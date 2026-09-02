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
/// The 1500 ms dismissal timer lives here rather than in CSS so that a new detent can re-arm it
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
  private readonly object _gate = new();
  private ITimer? _dismissTimer;
  private bool _disposed;

  /// <summary>
  /// The <paramref name="hub"/> argument is optional so component tests can construct the service
  /// without a SignalR connection; production resolves the registered singleton.
  /// </summary>
  public EncoderHudService(AudioStateHubService? hub = null, TimeProvider? timeProvider = null)
  {
    _timeProvider = timeProvider ?? TimeProvider.System;
    _hub = hub;
    if (_hub is not null)
    {
      _hub.EncoderHudChanged += OnHubEvent;
    }
  }

  /// <summary>The card currently on screen, or null when nothing is showing.</summary>
  public EncoderHudDto? Current { get; private set; }

  /// <summary>
  /// True between a HoldStart and its HoldCancel / HoldCommit. Drives the progress ring.
  /// </summary>
  public bool IsHolding { get; private set; }

  /// <summary>Fired whenever <see cref="Current"/> or <see cref="IsHolding"/> changes.</summary>
  public event Action? StateChanged;

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

      // An unrecognised phase leaves IsHolding where it was rather than guessing. The renderer
      // draws nothing for a phase it does not know, so a newer API build degrades to silence.
      IsHolding = dto.Phase switch
      {
        "HoldStart" => true,
        "HoldCancel" or "HoldCommit" => false,
        _ => IsHolding,
      };

      // While a button is held the card must not time out from under the ring, so the dismissal
      // timer is armed only when nothing is being held.
      if (IsHolding)
      {
        CancelTimerLocked();
      }
      else
      {
        ArmDismissLocked();
      }
    }

    StateChanged?.Invoke();
  }

  /// <summary>Clears the card immediately. Used by ENC-0's disconnect teardown and by tests.</summary>
  public void Dismiss()
  {
    lock (_gate)
    {
      CancelTimerLocked();
      Current = null;
      IsHolding = false;
    }

    StateChanged?.Invoke();
  }

  private Task OnHubEvent(EncoderHudDto dto)
  {
    Publish(dto);
    return Task.CompletedTask;
  }

  private void ArmDismissLocked()
  {
    CancelTimerLocked();
    _dismissTimer = _timeProvider.CreateTimer(
      _ => Dismiss(),
      null,
      TimeSpan.FromMilliseconds(EncoderInteractionTimings.HudHoldMs),
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
    }
  }
}
