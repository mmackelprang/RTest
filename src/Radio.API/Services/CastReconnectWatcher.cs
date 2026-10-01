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

  /// <summary>Cancelled: the service is stopping, a user action took over, or a newer drop replaced this watcher.</summary>
  Cancelled,

  /// <summary>The output was moved back to Cast, but the new connection was already lost again.</summary>
  LostAgainAfterSwitch,

  /// <summary>
  /// The conditional switch back to Cast threw (the new Cast connection was then torn down and
  /// the local output restored), or a host call failed unexpectedly.
  /// </summary>
  SwitchFailed,

  /// <summary>
  /// The speaker is back but running another sender's application; the watcher stood down for
  /// good rather than replace that session with ours.
  /// </summary>
  SpeakerInUse
}

/// <summary>The backoff schedule for <see cref="CastReconnectWatcher"/>.</summary>
internal readonly record struct CastReconnectSchedule(
  TimeSpan InitialDelay, TimeSpan MaxDelay, TimeSpan Window, TimeSpan Stability)
{
  /// <summary>A schedule with the default 120 s stability period.</summary>
  public CastReconnectSchedule(TimeSpan initialDelay, TimeSpan maxDelay, TimeSpan window)
    : this(initialDelay, maxDelay, window, TimeSpan.FromSeconds(120))
  {
  }

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
    var stability = TimeSpan.FromSeconds(Math.Max(0, options.AutoReconnectStabilitySeconds));
    return new CastReconnectSchedule(initial, max, window, stability);
  }

  /// <summary>Doubles <paramref name="delay"/>, clamped to <see cref="MaxDelay"/>.</summary>
  public TimeSpan Next(TimeSpan delay)
  {
    var next = delay + delay;
    return next > MaxDelay ? MaxDelay : next;
  }
}

/// <summary>
/// Where a watcher's window and backoff begin. A fresh drop starts both now; a drop soon after
/// a watcher-made reconnect continues the earlier window and backoff (AUD-37, review H1).
/// </summary>
/// <param name="WindowStartTimestamp">A <see cref="TimeProvider.GetTimestamp"/> value.</param>
/// <param name="FirstDelay">The first wait.</param>
/// <param name="FailureWarned">
/// True when an earlier watcher of the same episode already logged the episode's one Warning
/// for a failed attempt (review M3).
/// </param>
internal readonly record struct CastReconnectStart(long WindowStartTimestamp, TimeSpan FirstDelay, bool FailureWarned = false);

/// <summary>
/// Thrown by <see cref="ICastReconnectHost.ConnectAndStartAsync"/> when the speaker answered and
/// its status positively shows another sender's application running. A status that could not be
/// read is a failed attempt instead, retried (review M4). The host has already removed its own
/// connection.
/// </summary>
internal sealed class CastSpeakerInUseException : Exception
{
  public CastSpeakerInUseException(string message, Exception? inner = null)
    : base(message, inner)
  {
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
  /// Wires the audio source for the configured streaming mode, connects, checks the receiver is
  /// free for us, and starts the Cast output. Throws on failure, having torn down any connection
  /// it made itself; throws <see cref="CastSpeakerInUseException"/> when another application
  /// holds the receiver. Throws <see cref="OperationCanceledException"/> when
  /// <paramref name="ct"/> is cancelled — then leaving a connection of its own standing, for the
  /// caller to keep (<see cref="TryKeepForCastChoiceAsync"/>) or tear down — and, with
  /// <paramref name="ct"/> not cancelled, when its connect was superseded (a newer connect or a
  /// disconnect claimed the output), in which case nothing published is its own. Before each step
  /// after the connect it checks that the connection is still the one its connect published (the
  /// start, which cannot be interrupted, guards itself the same way). It does not touch the active
  /// output — the local speakers keep playing until the switch.
  /// </summary>
  Task ConnectAndStartAsync(ChromecastDeviceInfo device, CancellationToken ct);

  /// <summary>
  /// After a cancelled run: when the published Cast connection is still the one this host's
  /// connect made, the active output is Cast, and the service is not stopping, confirms the
  /// receiver, starts that connection if it is not streaming, and keeps it — true. False when any
  /// of those does not hold (nothing is done) or the start failed. Never throws.
  /// </summary>
  Task<bool> TryKeepForCastChoiceAsync();

  /// <summary>
  /// Switches the active output to Cast only if no output selection has been made since the
  /// recovery, checking and switching atomically. False when the switch was refused.
  /// </summary>
  Task<bool> TrySwitchToCastAsync(CastRecoveryMark mark, CancellationToken ct);

  /// <summary>
  /// Stops and disconnects the Cast connection this host's own connect published — and only
  /// that one: a connection someone else has made (or is making) since is left alone.
  /// Best-effort; never throws.
  /// </summary>
  Task TearDownCastAsync();

  /// <summary>
  /// After a switch to Cast threw: if the active output is not Cast, re-applies the recovery's
  /// local output through the gate (when it is still the active one), so local is not left
  /// muted by a switch that muted it before failing. Best-effort; never throws.
  /// </summary>
  Task RestoreLocalOutputAsync(CastRecoveryMark mark);
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
/// output (connected, connecting or stopping). Those are checks, not locks. Two things cover the
/// moment between a check and the connect. First, the user-facing output and Cast actions
/// (<c>DevicesController</c>) cancel the watcher and wait for it — up to a bound, after which they
/// proceed regardless — before touching Cast. Second, the watcher only ever tears down a
/// connection its own connect published and that no newer connect has claimed since, so a
/// connection someone else made is never removed by it. Neither makes a collision impossible:
/// a user connect that starts while the watcher's connect is still on the network (past the
/// bound) is refused by <c>GoogleCastOutput.ConnectAsync</c> while the watcher's is
/// <c>Connecting</c>, and that refusal is the user's request failing (AUD-85). A choice of
/// output made in that moment is caught by the atomic, conditional switch back to Cast, which
/// refuses — or, when the choice cancelled the run, by the cancellation path, which keeps (and
/// starts) the run's own connection when the choice was Cast and removes it otherwise (review M3).
/// After its connect returns, the run checks before each step that the connection is still the one
/// its connect published: a superseded connect returns normally with nothing of ours published
/// (review M2). The run makes output selections in exactly two places: that switch back to Cast
/// (conditional on no selection since the drop), and — only when that switch throws —
/// <see cref="ICastReconnectHost.RestoreLocalOutputAsync"/>, which re-applies the recovery's local
/// output through the gate when it is still the active output (a selection of its own: on the
/// production engine it bumps the epoch, re-applies the mute state and persists). Separately, when a
/// run ends <see cref="CastReconnectOutcome.LostAgainAfterSwitch"/> its owner re-runs the AUD-84
/// recovery, which may switch the output to local.
/// </para>
/// <para>
/// A full connect is attempted only after the speaker has answered two probes in a row (the
/// second a short confirmation wait after the first), and after a failed full connect the next
/// waits at the backoff cap — a speaker that answers TCP but fails the Cast handshake is
/// retried at most once a minute.
/// </para>
/// <para>
/// Every wait goes through the injected <see cref="TimeProvider"/>, so tests drive the backoff
/// with a fake clock instead of racing real delays (CLAUDE.md § Test Timing).
/// </para>
/// <para>
/// Logging (review M3). Only Warning and above reach journald from radio-api, on a box where
/// journal volume correlates with audible distortion, and a speaker that fails every handshake
/// for the default window is retried about 30 times. So the per-probe and per-attempt lines are
/// Debug, and the host makes its attempts as automatic ones (<c>CastConnectOptions.AutomaticAttempt</c>,
/// <c>StartAsync(automaticAttempt: true)</c>, <c>DisconnectAsync(automaticAttempt: true)</c>,
/// <c>TearDownCastOutputAsync(..., automaticAttempt: true)</c>), for which <c>GoogleCastOutput</c>
/// and the engine log their own failure lines at Debug instead of Error/Warning — a user's connect
/// still logs Error. The watcher logs ONE Warning for the first failed full connect of an episode
/// (carried across the watchers of an episode by <see cref="CastReconnectStart.FailureWarned"/>),
/// and Information when it gives up or reconnects. Lines the automatic flag does not reach — a
/// failing <c>InitializeAsync</c>, the HTTP output's own start, a superseded connect, and
/// <c>StopAsync</c>'s media-stop lines on a tear-down after streaming began — keep their levels.
/// </para>
/// </remarks>
internal sealed class CastReconnectWatcher
{
  private const int AnswersBeforeConnecting = 2;

  private readonly ICastReconnectHost _host;
  private readonly ChromecastDeviceInfo _device;
  private readonly CastRecoveryMark _mark;
  private readonly CastReconnectSchedule _schedule;
  private readonly CastReconnectStart? _start;
  private readonly Action<TimeSpan>? _onConnected;
  private readonly Action? _onFailureWarned;
  private readonly TimeProvider _time;
  private readonly ILogger _logger;
  private bool _failureWarned;

  public CastReconnectWatcher(
    ICastReconnectHost host,
    ChromecastDeviceInfo device,
    CastRecoveryMark mark,
    CastReconnectSchedule schedule,
    TimeProvider time,
    ILogger logger,
    CastReconnectStart? start = null,
    Action<TimeSpan>? onConnected = null,
    Action? onFailureWarned = null)
  {
    _host = host;
    _device = device;
    _mark = mark;
    _schedule = schedule;
    _time = time;
    _logger = logger;
    _start = start;
    _onConnected = onConnected;
    _onFailureWarned = onFailureWarned;
    _failureWarned = start?.FailureWarned ?? false;
    NextDelay = start?.FirstDelay ?? schedule.InitialDelay;
  }

  /// <summary>
  /// The first wait a watcher continuing this one's window should use: the backoff this run
  /// has reached, doubled (or the cap, after a failed connect).
  /// </summary>
  public TimeSpan NextDelay { get; private set; }

  /// <summary>Runs the watcher to one of the <see cref="CastReconnectOutcome"/>s. Never throws.</summary>
  public async Task<CastReconnectOutcome> RunAsync(CancellationToken ct)
  {
    var name = _device.FriendlyName;
    _logger.LogInformation(
      "Cast: will try to reconnect to \"{Name}\" when it returns (backoff up to {MaxBackoff} s, for {Window} min)",
      name, (int)_schedule.MaxDelay.TotalSeconds, (int)_schedule.Window.TotalMinutes);

    var started = _start?.WindowStartTimestamp ?? _time.GetTimestamp();
    var backoff = _start?.FirstDelay ?? _schedule.InitialDelay; // the exponential schedule's step
    var wait = backoff;                                           // the next wait actually taken
    var attempt = 0;
    var answers = 0;
    NextDelay = _schedule.Next(backoff);

    try
    {
      while (true)
      {
        // No wait may END past the window: a probe that starts after it is a probe the
        // configuration said not to make.
        if (_time.GetElapsedTime(started) + wait > _schedule.Window)
        {
          _logger.LogInformation(
            "Cast: \"{Name}\" did not come back within {Window} min — giving up on reconnecting; pick Cast again to reconnect",
            name, (int)_schedule.Window.TotalMinutes);
          return CastReconnectOutcome.GaveUp;
        }

        await Task.Delay(wait, _time, ct).ConfigureAwait(false);
        attempt++;

        if (StandDownBeforeConnecting(name) is { } early)
        {
          return early;
        }

        var target = await _host.ProbeAsync(_device, ct).ConfigureAwait(false);
        if (target == null)
        {
          _logger.LogDebug("Cast: reconnect probe {Attempt} — \"{Name}\" not reachable yet", attempt, name);
          answers = 0;
          backoff = _schedule.Next(backoff);
          wait = backoff;
          NextDelay = _schedule.Next(backoff);
          continue;
        }

        if (++answers < AnswersBeforeConnecting)
        {
          // One answer is not yet a speaker that is back: one still booting answers TCP and
          // fails the Cast handshake. Confirm shortly, without advancing the backoff.
          _logger.LogDebug("Cast: reconnect probe {Attempt} — \"{Name}\" answered; confirming", attempt, name);
          wait = _schedule.InitialDelay < backoff ? _schedule.InitialDelay : backoff;
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
          // The host leaves a connection of its own standing when cancelled, for this to decide.
          return await EndCancelledAfterConnectAsync(name).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
          // Not our token: our connect was superseded — another connect or a disconnect claimed
          // the Cast output while ours was on the network (review M2). It is theirs now.
          _logger.LogInformation(
            "Cast: no longer trying to reconnect to \"{Name}\" — another connect or a disconnect took over the Cast output", name);
          return CastReconnectOutcome.CastBusy;
        }
        catch (CastSpeakerInUseException ex)
        {
          _logger.LogInformation(
            "Cast: \"{Name}\" is in use by another app — not reconnecting ({Detail})", name, ex.Message);
          return CastReconnectOutcome.SpeakerInUse;
        }
        catch (Exception ex)
        {
          // The device answered TCP but the Cast session would not come up (still booting,
          // receiver app not ready). Retry no more than once per backoff cap from here on.
          _logger.LogDebug(ex, "Cast: reconnect attempt {Attempt} to \"{Name}\" failed; will retry", attempt, name);

          // Review L2: a run cancelled meanwhile is not retrying — the next wait ends it as
          // Cancelled — so it must not log the episode's "retrying" Warning (nor use it up).
          if (!_failureWarned && !ct.IsCancellationRequested)
          {
            // Review M3: the episode's one Warning. The output logged this attempt's failure at
            // Debug, so without this line a speaker that never takes the session would leave
            // nothing in journald.
            _failureWarned = true;
            _onFailureWarned?.Invoke();
            _logger.LogWarning(
              "Cast: \"{Name}\" answers but the Cast session would not come up ({Error}); retrying at most every {MaxBackoff} s for the rest of the {Window} min window — further failures are logged at Debug",
              name, ex.Message, (int)_schedule.MaxDelay.TotalSeconds, (int)_schedule.Window.TotalMinutes);
          }
          backoff = _schedule.MaxDelay;
          wait = backoff;
          NextDelay = backoff;
          continue;
        }

        if (ct.IsCancellationRequested)
        {
          // Cancelled while the connect was on the network (SharpCaster does not observe the
          // token): a user action or shutdown owns the outputs now.
          return await EndCancelledAfterConnectAsync(name).ConfigureAwait(false);
        }

        // The owner records the reconnect (and where the backoff got to) before the switch, so a
        // drop of this connection is recognised as part of the same episode however soon it comes.
        _onConnected?.Invoke(NextDelay);

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

          // The gate mutes local before activating the Cast/HTTP outputs; a throw there leaves
          // the active output local and muted.
          await _host.RestoreLocalOutputAsync(_mark).ConfigureAwait(false);
          return CastReconnectOutcome.SwitchFailed;
        }

        if (!switched)
        {
          var active = _host.ActiveOutputId;
          if (string.Equals(active, "google-cast", StringComparison.OrdinalIgnoreCase))
          {
            // Someone picked Cast since the drop. Nothing is torn down: if the connection
            // standing is ours, it is now the one serving that choice; if it is not, it is theirs.
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
  /// The run was cancelled after its connect was made (or while the host was finishing it). If
  /// the cancelling action was a pick of Cast — the active output is now <c>google-cast</c> —
  /// the connection is what serves that choice: the host keeps it (and starts it if it is not
  /// streaming), as the refused-switch branch does for a Cast pick (review M3). Otherwise the
  /// action was a local pick, a disconnect or shutdown: only what this run made is removed.
  /// </summary>
  /// <remarks>
  /// Without the keep, a Cast pick that lost the cancel-bound race left the console silent: the
  /// pick made Cast active and muted local while our connect was <c>Connecting</c> (so its own
  /// activation was a no-op and its auto-connect was refused), and the run then removed the only
  /// connection there was.
  /// </remarks>
  private async Task<CastReconnectOutcome> EndCancelledAfterConnectAsync(string name)
  {
    if (string.Equals(_host.ActiveOutputId, "google-cast", StringComparison.OrdinalIgnoreCase))
    {
      if (await _host.TryKeepForCastChoiceAsync().ConfigureAwait(false))
      {
        _logger.LogInformation(
          "Cast: the output was switched to Cast while reconnecting to \"{Name}\" — keeping the connection for that choice",
          name);
        return CastReconnectOutcome.OutputChangedByUser;
      }

      // Not ours to keep (superseded — then whoever superseded it serves the choice — or the
      // service is stopping) or its start failed. Review L4: Warning — when the start failed, Cast
      // is the active output, local is muted and no connection is left, so the console is silent.
      // Once per user action, never per retry.
      _logger.LogWarning(
        "Cast: the output was switched to Cast while reconnecting to \"{Name}\", but the reconnect's connection was not kept for it (no longer ours, or it would not start)",
        name);
    }

    _logger.LogDebug("Cast: reconnect to \"{Name}\" cancelled — removing any connection it made", name);
    await _host.TearDownCastAsync().ConfigureAwait(false);
    return CastReconnectOutcome.Cancelled;
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
