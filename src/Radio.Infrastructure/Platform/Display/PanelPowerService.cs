using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Input;

namespace Radio.Infrastructure.Platform.Display;

/// <summary>
/// Powers the kiosk panel off after <see cref="PanelPowerOptions.PanelOffAfterMinutes"/> of
/// continuous sleep screen, and back on when a knob is turned or pressed (<c>ENC-22</c>).
///
/// <para>
/// <b>Keyed on <see cref="ISleepService.IsSleepScreenVisible"/>, not <see cref="ISleepService.IsSleeping"/>.</b>
/// The 30-minute idle path reaches <c>/sleep</c> without ever setting <c>IsSleeping</c>
/// (<c>HANDOFF-NEXT-SESSION.md</c> gotcha #9), so a timer on <c>IsSleeping</c> would never run for the
/// case the owner asked about. Both sleep entries put the page on screen, and the page reports it.
/// </para>
///
/// <para>
/// ⛔ <b>The safety rules, which are the reason this class is shaped the way it is.</b> Touch leaves
/// the USB bus when the panel goes dark, so the knobs are the <i>only</i> wake source, and a dark panel
/// with a lost encoder is a screen nobody can turn on inside a sealed cabinet:
/// <list type="number">
///   <item>The panel is never powered off unless the encoder is connected, and has been for
///   <see cref="PanelPowerOptions.EncoderStableSeconds"/>.</item>
///   <item>If the encoder disconnects while the panel is off, the panel is powered on at once —
///   on the <c>ConnectionChanged</c> event, not on a poll.</item>
///   <item>The panel is powered on unconditionally at start-up (a crash while dark must not survive
///   the restart), and on a clean stop. <c>radio-api.service</c>'s <c>ExecStopPost=</c> covers the
///   stop that this process does not get to see.</item>
///   <item>A power-on that the compositor did not confirm is retried with backoff, so a failed
///   command cannot leave the panel dark with the service believing it is lit.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Commands are applied by one pump, last writer wins.</b> Every decision sets
/// <c>_desiredOn</c> under <c>_gate</c> and then kicks the pump, which applies the latest desired
/// state until the applied state matches. Two decisions in quick succession (a timer firing as a knob
/// turns) therefore cannot land on the compositor in the wrong order.
/// </para>
///
/// <para>
/// ⚠ <b>Not persisted across a <c>radio-api</c> restart</b>, and the direction that fails in is the
/// safe one: <c>SleepService</c>'s visibility flag starts false, so after a restart on <c>/sleep</c>
/// the timer does not arm until the page reports itself again, and the panel stays on.
/// </para>
/// </summary>
public sealed class PanelPowerService : IPanelPowerService, IHostedService, IDisposable
{
  private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

  private readonly ILogger<PanelPowerService> _logger;
  private readonly IOptionsMonitor<PanelPowerOptions> _options;
  private readonly ISleepService _sleep;
  private readonly IRotaryEncoderService? _encoder;
  private readonly IPanelPowerControl _control;
  private readonly TimeProvider _time;

  private readonly object _gate = new();

  // What the panel should be. Written only under _gate.
  private bool _desiredOn = true;

  // What the compositor last confirmed. Null until the first command lands: at start-up the panel's
  // state is unknown, and "unknown" must not be treated as "on" by anything that decides to skip a
  // command.
  private bool? _appliedOn;

  private bool _pumpRunning;
  private Task _pump = Task.CompletedTask;

  private readonly ITimer _offTimer;
  private bool _offTimerArmed;
  private DateTimeOffset _offDueAt;

  private readonly ITimer _retryTimer;
  private TimeSpan _nextRetryDelay = FirstRetryDelay;

  private DateTimeOffset? _encoderConnectedSince;
  private DateTimeOffset _wakeGraceUntil = DateTimeOffset.MinValue;

  private IDisposable? _optionsSubscription;
  private bool _started;
  private bool _disposed;

  public PanelPowerService(
    ILogger<PanelPowerService> logger,
    IOptionsMonitor<PanelPowerOptions> options,
    ISleepService sleep,
    IPanelPowerControl control,
    IRotaryEncoderService? encoder = null,
    TimeProvider? timeProvider = null)
  {
    _logger = logger;
    _options = options;
    _sleep = sleep;
    _control = control;
    _encoder = encoder;
    _time = timeProvider ?? TimeProvider.System;

    _offTimer = _time.CreateTimer(_ => OnOffTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    _retryTimer = _time.CreateTimer(_ => KickPump(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
  }

  /// <inheritdoc />
  public bool IsPanelOff
  {
    get
    {
      lock (_gate)
      {
        return IsDarkLocked();
      }
    }
  }

  /// <summary>
  /// Completes when the pump has nothing left to apply. For tests, which need a rendezvous with a
  /// command rather than a sleep.
  /// </summary>
  internal Task PumpIdle
  {
    get
    {
      lock (_gate)
      {
        return _pump;
      }
    }
  }

  // Dark = commanded off, OR the compositor has not confirmed a power-on it was asked for. The second
  // half is what makes a failed power-on retryable from a knob: without it the knob would see "on"
  // and dispatch into a panel that is still black.
  private bool IsDarkLocked() => !_desiredOn || _appliedOn == false;

  /// <inheritdoc />
  public Task StartAsync(CancellationToken cancellationToken)
  {
    // Subscribe first, then read: a connect landing between the two is then seen by the handler
    // rather than lost. (The handler ignores events until _started, so an event in the gap sets
    // nothing; the read below then observes the connection it reported.)
    _sleep.SleepScreenVisibilityChanged += OnSleepScreenVisibilityChanged;
    if (_encoder is not null)
    {
      _encoder.ConnectionChanged += OnEncoderConnectionChanged;
    }

    lock (_gate)
    {
      _started = true;
      _encoderConnectedSince = _encoder?.IsConnected == true ? _time.GetUtcNow() : null;
    }

    _optionsSubscription = _options.OnChange(_ => ReevaluateTimer());

    double minutes = _options.CurrentValue.PanelOffAfterMinutes;
    _logger.LogInformation(
      "Panel power: powering the panel on at start-up (ENC-22 safety rule); power-off after sleep is {State}",
      minutes > 0 ? $"on, after {minutes} min" : "off (Sleep:PanelOffAfterMinutes = 0)");

    // Unconditional, and not awaited: a crash while dark must not survive the restart, and the host's
    // start-up must not wait on the compositor. _appliedOn is null here, so the pump always sends it.
    KickPump();

    ReevaluateTimer();
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public async Task StopAsync(CancellationToken cancellationToken)
  {
    _sleep.SleepScreenVisibilityChanged -= OnSleepScreenVisibilityChanged;
    if (_encoder is not null)
    {
      _encoder.ConnectionChanged -= OnEncoderConnectionChanged;
    }

    _optionsSubscription?.Dispose();

    bool needsOn;
    lock (_gate)
    {
      _started = false;
      DisarmOffTimerLocked();
      needsOn = IsDarkLocked() || _appliedOn != true;
      _desiredOn = true;
    }

    if (needsOn)
    {
      // A clean stop while dark. ExecStopPost= in radio-api.service does the same thing from outside
      // the process; doing it here too means a stop that systemd's line cannot reach is still covered.
      _logger.LogInformation("Panel power: powering the panel on at shutdown");
      KickPump();
      Task pump = PumpIdle;
      try
      {
        await pump.WaitAsync(cancellationToken);
      }
      catch (OperationCanceledException)
      {
        // Host shutdown timeout. ExecStopPost= is the backstop.
      }
    }
  }

  /// <inheritdoc />
  public bool OnEncoderInput(string source)
  {
    bool powerOn = false;
    bool consume;

    lock (_gate)
    {
      DateTimeOffset now = _time.GetUtcNow();

      if (IsDarkLocked())
      {
        _desiredOn = true;
        _wakeGraceUntil = now + TimeSpan.FromMilliseconds(Math.Max(0, _options.CurrentValue.WakeGraceMilliseconds));
        powerOn = true;
        consume = true;
      }
      else
      {
        // Inside the grace window the panel is showing its own power-up splash, so nothing a knob does
        // here would be visible. Consumed rather than acted on, so a fast spin that woke the panel
        // cannot go on to move the volume unseen.
        consume = now < _wakeGraceUntil;
      }

      // Any knob input on the sleep screen is someone at the console: the countdown starts again.
      if (_started && _sleep.IsSleepScreenVisible)
      {
        ArmOffTimerLocked(FullPeriod());
      }
    }

    if (powerOn)
    {
      _logger.LogInformation("Panel power: {Source} on a dark panel; requested power-on, input consumed", source);
      KickPump();
    }

    return consume;
  }

  private void OnSleepScreenVisibilityChanged(object? sender, bool visible)
  {
    // The field is re-read rather than trusting the argument: SleepService's report is lock-free and a
    // concurrent pair of reports can deliver their events out of order. Reading the settled value
    // narrows that, but does not close it — a handler can read the field before a later write and take
    // _gate after that write's handler, arming the timer for a screen that is now hidden. What makes
    // that harmless is OnOffTimer re-checking IsSleepScreenVisible at the moment it would power off.
    bool nowVisible = _sleep.IsSleepScreenVisible;
    bool powerOn = false;

    lock (_gate)
    {
      if (!_started)
      {
        return;
      }

      if (nowVisible)
      {
        ArmOffTimerLocked(FullPeriod());
      }
      else
      {
        DisarmOffTimerLocked();

        // Leaving /sleep by any route — a REST wake, an incoming call, a navigation — lights the panel.
        if (IsDarkLocked())
        {
          _desiredOn = true;
          powerOn = true;
        }
      }
    }

    if (powerOn)
    {
      _logger.LogInformation("Panel power: the sleep screen closed; powering the panel on");
      KickPump();
    }
  }

  private void OnEncoderConnectionChanged(object? sender, EncoderConnectionEventArgs e)
  {
    bool powerOn = false;

    lock (_gate)
    {
      if (!_started)
      {
        return;
      }

      if (e.IsConnected)
      {
        _encoderConnectedSince = _time.GetUtcNow();

        // The off-timer may have fired while the encoder was away and declined; nothing else would
        // re-arm it. Armed for the stability period rather than the full period, because the sleep
        // screen has already been up at least that long.
        if (_sleep.IsSleepScreenVisible && !_offTimerArmed && !IsDarkLocked())
        {
          ArmOffTimerLocked(StablePeriod());
        }
      }
      else
      {
        _encoderConnectedSince = null;

        // ⛔ Safety rule 2. The only wake source has just gone; the panel comes on now.
        if (IsDarkLocked())
        {
          _desiredOn = true;
          powerOn = true;
        }
      }
    }

    if (powerOn)
    {
      _logger.LogWarning(
        "Panel power: the encoder disconnected while the panel was off; powering the panel on (the knobs are its only wake source)");
      KickPump();
    }
  }

  private void OnOffTimer()
  {
    bool powerOff = false;

    lock (_gate)
    {
      if (!_offTimerArmed)
      {
        return;
      }

      DateTimeOffset firedAt = _time.GetUtcNow();
      if (firedAt < _offDueAt)
      {
        // Early by the wall clock. Either a stale firing from before a re-arm, or the real timer —
        // which runs on a monotonic tick — landing a little ahead of GetUtcNow(), or an NTP step
        // backwards. Re-point at the remaining time rather than dropping it: returning here used to
        // lose the firing for the whole sleep period (pre-merge review, M1).
        TimeSpan remaining = _offDueAt - firedAt;
        _offTimer.Change(remaining < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : remaining,
          Timeout.InfiniteTimeSpan);
        return;
      }

      _offTimerArmed = false;

      if (!_started || FullPeriod() <= TimeSpan.Zero || !_sleep.IsSleepScreenVisible || IsDarkLocked())
      {
        return;
      }

      // ⛔ Safety rule 1. Declined, not deferred: OnEncoderConnectionChanged re-arms on reconnect.
      if (_encoder is null || !_encoder.IsConnected || _encoderConnectedSince is null)
      {
        _logger.LogInformation("Panel power: not powering off — the encoder is not connected");
        return;
      }

      TimeSpan connectedFor = _time.GetUtcNow() - _encoderConnectedSince.Value;
      if (connectedFor < StablePeriod())
      {
        ArmOffTimerLocked(StablePeriod() - connectedFor);
        return;
      }

      _desiredOn = false;
      powerOff = true;
    }

    if (powerOff)
    {
      _logger.LogInformation(
        "Panel power: {Minutes} min on the sleep screen; powering the panel off (any knob wakes it)",
        _options.CurrentValue.PanelOffAfterMinutes);
      KickPump();
    }
  }

  private void ReevaluateTimer()
  {
    lock (_gate)
    {
      if (!_started)
      {
        return;
      }

      if (_sleep.IsSleepScreenVisible && !IsDarkLocked())
      {
        ArmOffTimerLocked(FullPeriod());
      }
      else if (!_sleep.IsSleepScreenVisible)
      {
        DisarmOffTimerLocked();
      }
    }
  }

  private TimeSpan FullPeriod()
  {
    double minutes = _options.CurrentValue.PanelOffAfterMinutes;
    return minutes > 0 ? TimeSpan.FromMinutes(minutes) : TimeSpan.Zero;
  }

  private TimeSpan StablePeriod() =>
    TimeSpan.FromSeconds(Math.Max(0, _options.CurrentValue.EncoderStableSeconds));

  private void ArmOffTimerLocked(TimeSpan due)
  {
    if (FullPeriod() <= TimeSpan.Zero)
    {
      // Feature off. Nothing arms, so nothing can power the panel off.
      DisarmOffTimerLocked();
      return;
    }

    // Never zero: a zero due time can fire synchronously inside Change on some TimeProviders, which
    // would re-enter _gate from inside this lock.
    if (due < TimeSpan.FromMilliseconds(1))
    {
      due = TimeSpan.FromMilliseconds(1);
    }

    _offTimerArmed = true;
    _offDueAt = _time.GetUtcNow() + due;
    _offTimer.Change(due, Timeout.InfiniteTimeSpan);
  }

  private void DisarmOffTimerLocked()
  {
    _offTimerArmed = false;
    _offTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
  }

  private void KickPump()
  {
    lock (_gate)
    {
      if (_pumpRunning || _disposed)
      {
        return;
      }

      _pumpRunning = true;
      _pump = PumpAsync();
    }
  }

  private async Task PumpAsync()
  {
    // Yield first so the pump never runs its first command inside the caller's lock or on the
    // encoder read thread.
    await Task.Yield();

    while (true)
    {
      bool target;
      lock (_gate)
      {
        if (_appliedOn == _desiredOn)
        {
          _pumpRunning = false;
          return;
        }

        target = _desiredOn;
      }

      bool ok = await _control.SetPanelPowerAsync(target);

      lock (_gate)
      {
        if (ok)
        {
          _appliedOn = target;
          _nextRetryDelay = FirstRetryDelay;
          _logger.LogDebug("Panel power: compositor confirmed the panel {State}", target ? "on" : "off");
          continue;
        }

        if (!target)
        {
          // A power-off that was NOT CONFIRMED — which is not the same as one that did not land. gdbus
          // times out on the reply, not on the request, so a slow compositor can take the panel dark
          // and still report failure. Assume the worst: treat the panel as dark (so a knob keeps
          // consuming and kicking) and stop wanting it off, which makes the loop send "on" next.
          // Settling here instead — "presumably still lit" — left a possibly-dark panel with every
          // knob dispatching and nothing ever sending "on" (pre-merge review, H1).
          _appliedOn = false;
          _desiredOn = true;
          continue;
        }

        // A power-on that did not land. Stop the loop and retry with backoff; a knob input in the
        // meantime still sees the panel as dark (_appliedOn is not true) and kicks it again.
        _pumpRunning = false;
        if (!_disposed)
        {
          _retryTimer.Change(_nextRetryDelay, Timeout.InfiniteTimeSpan);
          _nextRetryDelay = _nextRetryDelay * 2 > MaxRetryDelay ? MaxRetryDelay : _nextRetryDelay * 2;
        }

        return;
      }
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
    }

    _sleep.SleepScreenVisibilityChanged -= OnSleepScreenVisibilityChanged;
    if (_encoder is not null)
    {
      _encoder.ConnectionChanged -= OnEncoderConnectionChanged;
    }

    _optionsSubscription?.Dispose();
    _offTimer.Dispose();
    _retryTimer.Dispose();
  }
}
