using Radio.Infrastructure.Audio.Outputs;

namespace Radio.API.Services;

/// <summary>
/// What the lost-Cast recovery (AUD-84) left behind: the local output it switched to and, on
/// the production engine, the output-selection epoch read under the same lock acquisition as
/// that switch. Null <see cref="Epoch"/> means the engine does not expose one, and the watcher
/// falls back to comparing the active output id.
/// </summary>
internal readonly record struct CastRecoveryMark(string LocalOutputId, int? Epoch);

/// <summary>How a <see cref="CastReconnectWatcher"/> run ended.</summary>
internal enum CastReconnectOutcome
{
  /// <summary>The speaker came back; the output is Cast again.</summary>
  Reconnected,

  /// <summary>Someone chose an output after the drop; the watcher left that choice alone.</summary>
  OutputChangedByUser,

  /// <summary>Another party connected (or is connecting) the Cast output; the watcher stood down.</summary>
  CastBusy,

  /// <summary>The reconnect window ran out without the speaker coming back.</summary>
  GaveUp,

  /// <summary>Cancelled: the service is stopping, or a newer drop replaced this watcher.</summary>
  Cancelled,

  /// <summary>The output was moved back to Cast, but the new connection was already lost again.</summary>
  LostAgainAfterSwitch,

  /// <summary>
  /// The conditional switch back to Cast threw (the new Cast connection was then torn down), or
  /// a host call failed unexpectedly.
  /// </summary>
  SwitchFailed
}

/// <summary>The backoff schedule for <see cref="CastReconnectWatcher"/>.</summary>
internal readonly record struct CastReconnectSchedule(TimeSpan InitialDelay, TimeSpan MaxDelay, TimeSpan Window)
{
  /// <summary>Builds a schedule from configuration, clamping nonsense values to something sane.</summary>
  public static CastReconnectSchedule From(Radio.Core.Configuration.GoogleCastOutputOptions options)
  {
    var initial = TimeSpan.FromSeconds(Math.Max(1, options.AutoReconnectInitialBackoffSeconds));
    var max = TimeSpan.FromSeconds(Math.Max(1, options.AutoReconnectMaxBackoffSeconds));
    if (max < initial)
    {
      max = initial;
    }

    var window = TimeSpan.FromMinutes(Math.Max(1, options.AutoReconnectWindowMinutes));
    return new CastReconnectSchedule(initial, max, window);
  }
}

/// <summary>
/// Everything the watcher needs from the outside world. Production is implemented by
/// <see cref="AudioEngineInitializationService"/>; tests substitute a fake so every step can be
/// driven deterministically.
/// </summary>
internal interface ICastReconnectHost
{
  /// <summary>True while no output selection of any kind has been made since the recovery.</summary>
  bool IsStillOnRecoveryOutput(CastRecoveryMark mark);

  /// <summary>
  /// True when the Cast output has no connected device and is not mid-transition — i.e. a
  /// connect by the watcher would not make it a second connecting party (AUD-85).
  /// </summary>
  bool IsCastIdle { get; }

  /// <summary>True when the Cast output is <c>Streaming</c>.</summary>
  bool IsCastStreaming { get; }

  /// <summary>The engine's active output id.</summary>
  string? ActiveOutputId { get; }

  /// <summary>
  /// A cheap reachability check (a TCP connect to the device's Cast port). Returns the device
  /// record to connect to — refreshed from the discovery cache where possible — or null when
  /// the device does not answer.
  /// </summary>
  Task<ChromecastDeviceInfo?> ProbeAsync(ChromecastDeviceInfo device, CancellationToken ct);

  /// <summary>
  /// Wires the audio source for the configured streaming mode, connects and starts the Cast
  /// output. Throws on failure, having torn down any connection it made itself. It does not
  /// touch the active output — the local speakers keep playing until the switch.
  /// </summary>
  Task ConnectAndStartAsync(ChromecastDeviceInfo device, CancellationToken ct);

  /// <summary>
  /// Switches the active output to Cast only if no output selection has been made since the
  /// recovery, checking and switching atomically. False when the switch was refused.
  /// </summary>
  Task<bool> TrySwitchToCastAsync(CastRecoveryMark mark, CancellationToken ct);

  /// <summary>Stops and disconnects the Cast output. Best-effort; never throws.</summary>
  Task TearDownCastAsync();
}

/// <summary>
/// AUD-37. After a Cast speaker drops and the console falls back to a local output (AUD-84),
/// waits for the speaker to come back and moves the output back to Cast — once, with capped
/// exponential backoff, for a bounded window, and only if nobody has chosen an output since.
/// </summary>
/// <remarks>
/// <para>
/// One run of <see cref="RunAsync"/> is one watcher. The owning service starts a replacement only
/// after the previous run has finished, so two never act at once. Immediately before connecting,
/// a run checks that no output has been chosen since the drop and that nobody else owns the Cast
/// output (connected, connecting or stopping). Those are checks, not locks: a choice made in the
/// moment between the check and the connect is caught afterwards by the atomic, conditional
/// switch back to Cast (which then refuses, and the new connection is torn down), and a
/// connect started by someone else in that moment is refused by <c>GoogleCastOutput.ConnectAsync</c>
/// for whichever party comes second while the other is still <c>Connecting</c>. The run's only
/// output change is that conditional switch.
/// </para>
/// <para>
/// Every wait goes through the injected <see cref="TimeProvider"/>, so tests drive the backoff
/// with a fake clock instead of racing real delays (CLAUDE.md § Test Timing).
/// </para>
/// <para>
/// Per-probe logging is Debug on purpose: a speaker that stays away for the whole window would
/// otherwise put a line in journald every minute for half an hour, on a box where journal
/// volume correlates with audible distortion.
/// </para>
/// </remarks>
internal sealed class CastReconnectWatcher
{
  private readonly ICastReconnectHost _host;
  private readonly ChromecastDeviceInfo _device;
  private readonly CastRecoveryMark _mark;
  private readonly CastReconnectSchedule _schedule;
  private readonly TimeProvider _time;
  private readonly ILogger _logger;

  public CastReconnectWatcher(
    ICastReconnectHost host,
    ChromecastDeviceInfo device,
    CastRecoveryMark mark,
    CastReconnectSchedule schedule,
    TimeProvider time,
    ILogger logger)
  {
    _host = host;
    _device = device;
    _mark = mark;
    _schedule = schedule;
    _time = time;
    _logger = logger;
  }

  /// <summary>Runs the watcher to one of the <see cref="CastReconnectOutcome"/>s. Never throws.</summary>
  public async Task<CastReconnectOutcome> RunAsync(CancellationToken ct)
  {
    var name = _device.FriendlyName;
    _logger.LogInformation(
      "Cast: will try to reconnect to \"{Name}\" when it returns (backoff up to {MaxBackoff} s, for {Window} min)",
      name, (int)_schedule.MaxDelay.TotalSeconds, (int)_schedule.Window.TotalMinutes);

    var started = _time.GetTimestamp();
    var delay = _schedule.InitialDelay;
    var attempt = 0;

    try
    {
      while (true)
      {
        // No wait may END past the window: a probe that starts after it is a probe the
        // configuration said not to make.
        if (_time.GetElapsedTime(started) + delay > _schedule.Window)
        {
          _logger.LogInformation(
            "Cast: \"{Name}\" did not come back within {Window} min — giving up on reconnecting; pick Cast again to reconnect",
            name, (int)_schedule.Window.TotalMinutes);
          return CastReconnectOutcome.GaveUp;
        }

        await Task.Delay(delay, _time, ct).ConfigureAwait(false);
        attempt++;

        // Double, then clamp to the cap.
        var next = delay + delay;
        delay = next > _schedule.MaxDelay ? _schedule.MaxDelay : next;

        if (StandDownBeforeConnecting(name) is { } early)
        {
          return early;
        }

        var target = await _host.ProbeAsync(_device, ct).ConfigureAwait(false);
        if (target == null)
        {
          _logger.LogDebug("Cast: reconnect probe {Attempt} — \"{Name}\" not reachable yet", attempt, name);
          continue;
        }

        _logger.LogDebug("Cast: reconnect probe {Attempt} — \"{Name}\" is answering; reconnecting", attempt, name);

        // Re-checked: the probe took time, and the user or another party may have acted in it.
        if (StandDownBeforeConnecting(name) is { } late)
        {
          return late;
        }

        try
        {
          await _host.ConnectAndStartAsync(target, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex)
        {
          // The device answered TCP but the Cast session would not come up (still booting,
          // receiver app not ready). Keep backing off.
          _logger.LogDebug(ex, "Cast: reconnect attempt {Attempt} to \"{Name}\" failed; will retry", attempt, name);
          continue;
        }

        bool switched;
        try
        {
          // CancellationToken.None: once connected, the switch must be decided, not abandoned
          // half-way — an abandoned connect would leave Cast streaming beside the local speakers.
          switched = await _host.TrySwitchToCastAsync(_mark, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "Cast: reconnected to \"{Name}\" but could not switch the output back to Cast", name);
          await _host.TearDownCastAsync().ConfigureAwait(false);
          return CastReconnectOutcome.SwitchFailed;
        }

        if (!switched)
        {
          var active = _host.ActiveOutputId;
          if (string.Equals(active, "google-cast", StringComparison.OrdinalIgnoreCase))
          {
            // The user picked Cast while this reconnect was in flight. Their own connect was either
            // refused (ours was still Connecting) or replaced ours once it was up; either way the
            // connection standing now serves their choice, and tearing it down would leave Cast
            // selected and silent.
            _logger.LogInformation(
              "Cast: the output was switched to Cast while reconnecting to \"{Name}\" — leaving the connection to that choice",
              name);
            return CastReconnectOutcome.OutputChangedByUser;
          }

          _logger.LogInformation(
            "Cast: \"{Name}\" is back, but the output was changed to {ActiveOutput} since the drop — not switching back; disconnecting Cast",
            name, active ?? "<none>");
          await _host.TearDownCastAsync().ConfigureAwait(false);
          return CastReconnectOutcome.OutputChangedByUser;
        }

        if (!_host.IsCastStreaming)
        {
          // Lost again between the start and the switch. The loss was reported while the
          // output was still local, so the AUD-84 recovery may have declined it; the owner
          // re-runs that recovery so the local speakers are not left muted.
          _logger.LogInformation(
            "Cast: \"{Name}\" dropped again right after reconnecting — returning to the local output", name);
          return CastReconnectOutcome.LostAgainAfterSwitch;
        }

        _logger.LogInformation(
          "Cast: \"{Name}\" is back — reconnected and switched the output back to Cast", name);
        return CastReconnectOutcome.Reconnected;
      }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      _logger.LogDebug("Cast: reconnect watcher for \"{Name}\" cancelled", name);
      return CastReconnectOutcome.Cancelled;
    }
    catch (Exception ex)
    {
      // Defensive: a host call that is documented not to throw did. Stop rather than spin.
      _logger.LogError(ex, "Cast: reconnect watcher for \"{Name}\" failed", name);
      return CastReconnectOutcome.SwitchFailed;
    }
  }

  /// <summary>
  /// The checks that end the watcher before it may connect. Null when it may proceed.
  /// </summary>
  private CastReconnectOutcome? StandDownBeforeConnecting(string name)
  {
    if (!_host.IsStillOnRecoveryOutput(_mark))
    {
      _logger.LogInformation(
        "Cast: no longer trying to reconnect to \"{Name}\" — the output was changed to {ActiveOutput} since the drop",
        name, _host.ActiveOutputId ?? "<none>");
      return CastReconnectOutcome.OutputChangedByUser;
    }

    if (!_host.IsCastIdle)
    {
      _logger.LogInformation(
        "Cast: no longer trying to reconnect to \"{Name}\" — the Cast output is already connected or connecting", name);
      return CastReconnectOutcome.CastBusy;
    }

    return null;
  }
}
