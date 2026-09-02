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

  private sealed class PressState
  {
    public bool IsDown;
    public bool LongFired;
    public ITimer? Timer;
  }

  /// <summary>Fired on release, when the hold did not reach the threshold.</summary>
  public event Action<int>? ShortPress;

  /// <summary>Fired at the threshold, while the button is still held.</summary>
  public event Action<int>? LongPress;

  /// <summary>Fired on press-down, so the HUD can start the progress ring.</summary>
  public event Action<int>? HoldStarted;

  /// <summary>Fired on an early release, so the HUD can collapse the ring.</summary>
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
        s.Timer = _timeProvider.CreateTimer(
          _ => OnThreshold(index),
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
        // out of the release would fire it into a UI that just changed underneath the user.
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

  private void OnThreshold(int index)
  {
    bool fire = false;

    lock (_gate)
    {
      PressState s = _state[index];
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
