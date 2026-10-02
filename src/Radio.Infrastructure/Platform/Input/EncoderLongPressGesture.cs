using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// Turns raw press/release edges into short-press and long-press actions.
///
/// <para>
/// <b>The device has no long-press gesture.</b> It reports button state changes and nothing else, so
/// the threshold, the timer and the decision all live here.
/// </para>
///
/// <para>
/// Two rules give this its feel, and both are deliberate:
/// <list type="bullet">
/// <item>The <b>short</b> action fires on <b>release</b>, not on press. Firing on press would fire
/// it on the way into every hold.</item>
/// <item>The <b>long</b> action fires <b>at</b> the threshold while the button is still held, and
/// the release that follows does nothing. That is what lets the on-screen ring complete and the
/// action happen together, instead of the action waiting for a finger to lift.</item>
/// </list>
/// </para>
/// </summary>
public sealed class EncoderLongPressGesture : IDisposable
{
  private readonly ILogger _logger;
  private readonly TimeProvider _timeProvider;
  private readonly object _gate = new();
  private readonly PressState[] _state;
  private bool _disposed;

  /// <summary>
  /// How far a held knob must turn, net, before the turn cancels the hold (ENC-24).
  ///
  /// <para>
  /// Two detents, not one, because one is not evidence of intent. The HID parse raises a report's
  /// button edges before its turn, so a push that jostles the knob by a single detent reaches
  /// <see cref="OnTurn"/> as a turn of a held knob; cancelling on that would eat the press. The count
  /// is <b>signed and net</b>, so a back-and-forth wobble of one detent each way sums to zero and
  /// cancels nothing.
  /// </para>
  /// </summary>
  internal const int TurnCancelDetents = 2;

  private sealed class PressState
  {
    public bool IsDown;
    public bool LongFired;
    public ITimer? Timer;

    /// <summary>Signed detents turned since this press went down. Reset on press-down.</summary>
    public int NetTurn;

    /// <summary>
    /// Incremented on every press-down. The threshold timer captures the value it was created
    /// under, so a callback belonging to an earlier press is recognised and ignored.
    /// </summary>
    public long Generation;
  }

  /// <summary>Fired on release, when the hold did not reach the threshold.</summary>
  public event Action<int>? ShortPress;

  /// <summary>Fired at the threshold, while the button is still held.</summary>
  public event Action<int>? LongPress;

  /// <summary>Fired on press-down, so the HUD can start the progress ring.</summary>
  public event Action<int>? HoldStarted;

  /// <summary>
  /// Fired on an early release, or on a turn of the held knob (<see cref="OnTurn"/>), so the HUD can
  /// collapse the ring.
  /// </summary>
  public event Action<int>? HoldCancelled;

  public EncoderLongPressGesture(int encoderCount, ILogger logger, TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _timeProvider = timeProvider ?? TimeProvider.System;
    _state = new PressState[encoderCount];
    for (int i = 0; i < encoderCount; i++)
    {
      _state[i] = new PressState();
    }
  }

  /// <summary>
  /// Whether this gesture currently has the button of <paramref name="index"/> down — from its press
  /// edge until its release, including after the long action fired, and false once a turn has cancelled
  /// the press (<see cref="OnTurn"/>). A press the router consumed before it reached the gesture (a
  /// sleep wake) was never recorded, so it reads false.
  /// </summary>
  public bool IsHeld(int index)
  {
    if (index < 0 || index >= _state.Length)
    {
      return false;
    }

    lock (_gate)
    {
      return _state[index].IsDown;
    }
  }

  /// <summary>Feeds one button edge in. <paramref name="isPressed"/> false is a release.</summary>
  public void OnButtonEdge(int index, bool isPressed)
  {
    if (index < 0 || index >= _state.Length)
    {
      return;
    }

    bool raiseHoldStarted = false;
    bool raiseHoldCancelled = false;
    bool raiseShort = false;

    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }

      PressState s = _state[index];

      if (isPressed)
      {
        // A second press edge without an intervening release should not stack a second timer. The
        // device is change-only, so this is not expected - it is cheap to make it harmless anyway.
        if (s.IsDown)
        {
          return;
        }

        s.IsDown = true;
        s.LongFired = false;
        s.NetTurn = 0;
        long generation = ++s.Generation;
        s.Timer = _timeProvider.CreateTimer(
          _ => OnThreshold(index, generation),
          null,
          TimeSpan.FromMilliseconds(EncoderInteractionTimings.LongPressThresholdMs),
          Timeout.InfiniteTimeSpan);
        raiseHoldStarted = true;
      }
      else
      {
        // A release edge with no press recorded for it. The case this guard exists for is the
        // sleep-wake path in RotaryEncoderActionRouter: that consumes the PRESS edge to wake, so
        // this gesture never saw the press and there is no hold to end. Synthesising a short action
        // out of the release would fire it into a UI that just changed underneath the user. The
        // second path is OnTurn: a turn of the held knob ends the press, and its release lands here.
        if (!s.IsDown)
        {
          return;
        }

        s.IsDown = false;
        s.Timer?.Dispose();
        s.Timer = null;

        if (s.LongFired)
        {
          // The long action already fired at the threshold. The release is deliberately inert -
          // firing the short action here as well would mute the console every time you held for
          // standby.
          s.LongFired = false;
        }
        else
        {
          raiseHoldCancelled = true;
          raiseShort = true;
        }
      }
    }

    if (raiseHoldStarted) { Raise(HoldStarted, index, nameof(HoldStarted)); }

    // ShortPress is raised BEFORE HoldCancelled, and that order is load-bearing rather than
    // incidental. The router's HoldCancelled handler publishes a HUD card carrying the console's
    // mute state, and the short action on the volume knob is what toggles that state. Raising the
    // card first published the value from before the toggle and nothing re-published it, so the HUD
    // asserted the opposite of the truth for the card's full lifetime. This order also makes
    // EncoderHudPhase.HoldCancel's "the short action fired" true of the event it names.
    if (raiseShort) { Raise(ShortPress, index, nameof(ShortPress)); }
    if (raiseHoldCancelled) { Raise(HoldCancelled, index, nameof(HoldCancelled)); }
  }

  /// <summary>
  /// Feeds one turn report in, with its signed <paramref name="delta"/> in detents. A held knob that
  /// has turned <see cref="TurnCancelDetents"/> or more detents net since its press went down, before
  /// its long action fired, has its hold cancelled (ENC-24).
  ///
  /// <para>
  /// A press-and-turn is neither a click nor a hold. Without this, holding VOLUME while adjusting it
  /// would drop the console into Standby at the threshold, and releasing a sub-threshold
  /// press-and-turn would toggle mute. Below the tolerance the press is untouched: a single stray
  /// detent, or a wobble that nets to zero, still ends in the short action on release or the long
  /// action at the threshold.
  /// </para>
  ///
  /// <para>
  /// The cancel ends the press: the timer is disposed and the button is recorded as up, so the
  /// release that follows is dropped by the orphan-release guard in <see cref="OnButtonEdge"/> and
  /// neither <see cref="ShortPress"/> nor <see cref="LongPress"/> fires for this press.
  /// <see cref="HoldCancelled"/> is raised once, so the HUD can collapse the ring. A turn after the
  /// long action fired, a turn with the button up, and a turn on a different index do nothing here.
  /// </para>
  /// </summary>
  public void OnTurn(int index, int delta)
  {
    if (index < 0 || index >= _state.Length)
    {
      return;
    }

    bool raiseHoldCancelled = false;

    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }

      PressState s = _state[index];
      if (!s.IsDown || s.LongFired)
      {
        return;
      }

      s.NetTurn += delta;
      if (Math.Abs(s.NetTurn) >= TurnCancelDetents)
      {
        s.Timer?.Dispose();
        s.Timer = null;
        s.IsDown = false;
        raiseHoldCancelled = true;
      }
    }

    if (raiseHoldCancelled) { Raise(HoldCancelled, index, nameof(HoldCancelled)); }
  }

  private void OnThreshold(int index, long generation)
  {
    bool fire = false;

    lock (_gate)
    {
      PressState s = _state[index];

      // A callback from an earlier press. Disposing a timer does not recall a callback the timer
      // queue had already dispatched, so a release or a turn-cancel can race one in; without this it
      // would find the NEXT press down and fire the long action into it early. Return before
      // touching s.Timer, which now belongs to that next press.
      if (s.Generation != generation)
      {
        return;
      }

      s.Timer?.Dispose();
      s.Timer = null;

      if (s.IsDown && !s.LongFired)
      {
        s.LongFired = true;
        fire = true;
      }
    }

    if (fire)
    {
      Raise(LongPress, index, nameof(LongPress));
    }
  }

  private void Raise(Action<int>? handler, int index, string name)
  {
    try
    {
      handler?.Invoke(index);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Encoder {Index} {Gesture} handler threw", index, name);
    }
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
      foreach (PressState s in _state)
      {
        s.Timer?.Dispose();
        s.Timer = null;
        s.IsDown = false;
      }
    }
  }
}
