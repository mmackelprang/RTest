using Microsoft.Extensions.Logging;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Input;

namespace Radio.Infrastructure.Platform.Input;

/// <summary>
/// Coalesces HUD updates and re-publishes them to whoever is broadcasting.
///
/// <para>
/// <b>Why a coalescer and not a straight pass-through.</b> <c>PollIntervalMs</c> defaults to 10, so
/// a fast spin can present up to 100 movements a second. Each one that reached SignalR would fan out to a
/// Blazor Server circuit and re-render a component tree, on an Intel N100 where incidental load
/// correlates with audible distortion — and it would only reproduce while somebody was touching the
/// radio, which is a miserable thing to diagnose. The audio action is <b>not</b> throttled: the
/// router applies volume per event at full rate before it publishes here. The ear leads; the screen
/// catches up.
/// </para>
///
/// <para>
/// <b>Only <see cref="EncoderHudPhase.Value"/> is coalesced.</b> The hold phases are discrete edges,
/// not samples of a moving value, and dropping one would strand a progress ring on screen. They
/// flush immediately and clear any pending value for that encoder.
/// </para>
/// </summary>
public sealed class EncoderFeedbackService : IEncoderFeedbackSink, IDisposable
{
  private readonly ILogger<EncoderFeedbackService> _logger;
  private readonly TimeProvider _timeProvider;
  private readonly object _gate = new();

  // One pending value + one timer per encoder. Per-encoder rather than global so a turn on one knob
  // can never swallow a turn on another - two hands on the cabinet is an ordinary case.
  private readonly EncoderHudEventArgs?[] _pending = new EncoderHudEventArgs?[EncoderCount];
  private readonly ITimer?[] _timers = new ITimer?[EncoderCount];
  private readonly long[] _lastEmittedTicks = new long[EncoderCount];
  private bool _disposed;

  private const int EncoderCount = 4;

  public EncoderFeedbackService(ILogger<EncoderFeedbackService> logger, TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  /// <inheritdoc />
  public event EventHandler<EncoderHudEventArgs>? Feedback;

  /// <inheritdoc />
  public void Publish(EncoderHudEventArgs update)
  {
    ArgumentNullException.ThrowIfNull(update);

    if (update.EncoderIndex < 0 || update.EncoderIndex >= EncoderCount)
    {
      _logger.LogDebug("Dropping HUD update for out-of-range encoder {Index}", update.EncoderIndex);
      return;
    }

    EncoderHudEventArgs? emitNow = null;

    lock (_gate)
    {
      if (_disposed)
      {
        return;
      }

      int i = update.EncoderIndex;

      if (update.Phase != EncoderHudPhase.Value)
      {
        // Discrete edge. Cancel anything pending for this encoder and let it through unchanged.
        CancelTimerLocked(i);
        _pending[i] = null;
        _lastEmittedTicks[i] = _timeProvider.GetTimestamp();
        emitNow = update;
      }
      else
      {
        long now = _timeProvider.GetTimestamp();
        double sinceMs = _lastEmittedTicks[i] == 0
          ? double.MaxValue
          : _timeProvider.GetElapsedTime(_lastEmittedTicks[i], now).TotalMilliseconds;

        if (sinceMs >= EncoderInteractionTimings.HudCoalesceMs)
        {
          // Leading edge of a burst: emit at once, so the first detent is on screen inside 100 ms.
          CancelTimerLocked(i);
          _pending[i] = null;
          _lastEmittedTicks[i] = now;
          emitNow = update;
        }
        else
        {
          // Inside the window: replace the pending value and arm a trailing-edge flush. Replacing
          // rather than queuing is what makes the last value the one that lands.
          _pending[i] = update;
          if (_timers[i] is null)
          {
            int captured = i;
            _timers[i] = _timeProvider.CreateTimer(
              _ => Flush(captured),
              null,
              TimeSpan.FromMilliseconds(EncoderInteractionTimings.HudCoalesceMs - sinceMs),
              Timeout.InfiniteTimeSpan);
          }
        }
      }
    }

    if (emitNow is not null)
    {
      Raise(emitNow);
    }
  }

  private void Flush(int index)
  {
    EncoderHudEventArgs? toEmit;

    lock (_gate)
    {
      CancelTimerLocked(index);
      toEmit = _pending[index];
      _pending[index] = null;
      if (toEmit is not null)
      {
        _lastEmittedTicks[index] = _timeProvider.GetTimestamp();
      }
    }

    if (toEmit is not null)
    {
      Raise(toEmit);
    }
  }

  private void Raise(EncoderHudEventArgs update)
  {
    try
    {
      Feedback?.Invoke(this, update);
    }
    catch (Exception ex)
    {
      // A HUD update is cosmetic. A subscriber that throws must not take the encoder input path
      // down with it - the knobs stay live either way.
      _logger.LogError(ex, "Encoder HUD subscriber threw for encoder {Index}", update.EncoderIndex);
    }
  }

  private void CancelTimerLocked(int index)
  {
    _timers[index]?.Dispose();
    _timers[index] = null;
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
      for (int i = 0; i < EncoderCount; i++)
      {
        CancelTimerLocked(i);
        _pending[i] = null;
      }
    }
  }
}
