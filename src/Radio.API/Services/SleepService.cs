using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Radio.API.Hubs;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Services;

/// <summary>
/// Manages the console's sleep states for the kiosk UI.
///
/// <para>
/// <b>Standby</b> pauses the active source, saves and applies mute, and broadcasts
/// <c>SleepStateChanged</c> over SignalR. <b>Ambient</b> is the <c>/sleep</c> route being on screen
/// while the primary source plays on, reported by the page itself. Waking restores the pre-sleep
/// mute state and resumes playback <i>only</i> where playback was parked.
/// </para>
///
/// <para>
/// ⚠ <b>One qualification on "Ambient changes no audio", which is how that sentence used to read.</b>
/// Ambient still changes nothing about the <i>primary</i> source — that is what defines it. But
/// since ADR-029 Amendment 2 <b>both</b> transitions out of <see cref="ConsoleWakeState.Awake"/>
/// stop attended EVENT playback (D7 §7.5), because the <c>/sleep</c> surface offers no transport to
/// stop it with. See <see cref="SetSleepScreenVisibleAsync"/> for why one edge was not enough.
/// </para>
///
/// <para>
/// ⚠ <b>This service does not touch display power, and must not.</b> <see cref="SetDisplayPowerAsync"/>
/// is retained but uncalled: <c>ENC-15</c> established on the box that the touchscreen is powered by
/// the panel and leaves the USB bus when it blanks, so touch cannot wake a blanked panel, and the
/// encoder exposes no evdev node so it cannot wake one either. See <c>design/INTEGRATIONS.md</c> §1
/// for the recovery commands and <c>design/FUTURE-WORK.md</c> §7 (Sleep Mode) for the full record.
/// </para>
///
/// Wake sources: a screen tap, an encoder input, or an API call.
/// </summary>
public class SleepService : ISleepService
{
  private readonly ILogger<SleepService> _logger;
  private readonly IHubContext<AudioStateHub> _hubContext;
  private readonly IAudioManager? _audioManager;
  private readonly SemaphoreSlim _lock = new(1, 1);
  // Volatile for the same reason _isSleepScreenVisible below is: ENC-6 made this readable from the
  // encoder thread through WakeState, where it is not under _lock. Writes still happen only under
  // _lock; this makes the unsynchronized READ well-defined rather than relying on x64 happening to
  // be stronger than the memory model requires.
  private volatile bool _isSleeping;
  private bool _wasMutedBeforeSleep;
  private bool _wasPlayingBeforeSleep;

  // Set by the /sleep page reporting itself, cleared by that page disposing or by MainLayout
  // rendering. Written from request threads and read from the encoder thread, so it is volatile
  // rather than lock-guarded: it is one independent bool and taking _lock to read it would put an
  // await on the encoder input path.
  private volatile bool _isSleepScreenVisible;

  // 1 once a wake has been claimed and has not yet been confirmed by the browser leaving the route.
  private int _wakeClaimed;

  /// <summary>
  /// How long the attended-playback stop may take before sleep proceeds without it. Bounded because
  /// <see cref="EnterSleepAsync"/> holds <c>_lock</c> across it and <see cref="WakeAsync"/> needs
  /// that same lock — see the note in <see cref="StopAttendedPlaybackAsync"/>.
  /// </summary>
  private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

  // GNOME ScreenSaver D-Bus for physical display DPMS control.
  // Runs as the desktop session user (mmack) to reach the GNOME session bus.
  private const string GnomeScreenSaverSetActive =
    "gdbus call --session --dest org.gnome.ScreenSaver --object-path /org/gnome/ScreenSaver --method org.gnome.ScreenSaver.SetActive";
  private const string SessionUser = "mmack";
  private const string SessionBusEnv = "DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus";

  public bool IsSleeping => _isSleeping;

  public bool IsSleepScreenVisible => _isSleepScreenVisible;

  /// <summary>
  /// The three states, derived rather than stored, so there is no second state machine to keep in
  /// step with <see cref="IsSleeping"/>.
  /// </summary>
  public ConsoleWakeState WakeState
  {
    get
    {
      // A claimed wake reads as Awake from this instant. Both of the things that would otherwise
      // clear it - the resume inside WakeAsync, and the browser navigating off /sleep - are far
      // slower than the 10 ms encoder poll, so without this the second detent of a fast spin is
      // discarded along with the tenth.
      if (Volatile.Read(ref _wakeClaimed) == 1)
      {
        return ConsoleWakeState.Awake;
      }

      // Standby is checked first because it is defined by audio being parked, which is true before
      // any client has reported the route.
      if (_isSleeping)
      {
        return ConsoleWakeState.Standby;
      }

      return _isSleepScreenVisible ? ConsoleWakeState.Ambient : ConsoleWakeState.Awake;
    }
  }

  /// <summary>
  /// Records that a client has put the sleep screen on screen, or taken it off — and, when a client
  /// reports it ON screen, stops attended playback (ADR-029 D7 §7.5 on §16.5's corrected trigger).
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠ <b>This is the SECOND of §7.5's two edges, and it is the only one that sees the idle timer.</b>
  /// §7.5 originally hung the rule on <see cref="EnterSleepAsync"/> alone, reasoning that "sleep
  /// parks the audio". ADR-029 §16.4 measured that against the tree and found it false for the case
  /// §7.5 was written about: <c>idle-dimmer.js</c>'s <c>navigateToSleep('idle')</c> reaches
  /// <c>/sleep</c> by <c>window.location.href</c> and deliberately calls nothing else, so the
  /// 30-minute idle path never reaches <see cref="EnterSleepAsync"/> and
  /// <see cref="IsSleeping"/> is <b>false</b> on it. The obvious predicate is the wrong one.
  /// </para>
  /// <para>
  /// ⚠ <b>Which routes THIS edge is actually the stopper for, stated precisely, because an earlier
  /// revision of this remark got it wrong.</b> It claimed this edge covers "the idle timer, the pill,
  /// the server push and a direct navigation". It does not. The pill and the server push both park
  /// audio <i>first</i> — <see cref="EnterSleepAsync"/> is on their path and is the origin of the
  /// push — so by the time <c>Sleep.razor</c> reports itself the playback is already stopped and this
  /// method finds nothing to do. The routes for which this edge is the <b>only</b> stopper are the
  /// ones that reach <c>/sleep</c> without parking audio: the <b>30-minute idle timer</b> and a
  /// <b>direct navigation</b>. That is still exactly the case §7.5's own motivating sentence names,
  /// which is the whole reason the edge exists.
  /// </para>
  /// <para>
  /// ⚠ <b>The two edges are not redundant and neither may be dropped.</b>
  /// <see cref="EnterSleepAsync"/> covers the routes with no page at all — the encoder long-press and
  /// <c>POST /api/system/sleep</c> park the room with no browser to report anything.
  /// </para>
  /// <para>
  /// ⭐ <b>THE PREDICATE IS THE REPORT ITSELF — "a client says the no-transport surface is up" — and
  /// NOT a transition of <see cref="WakeState"/>. That is a correction, and both of the reasons are
  /// concrete.</b>
  /// </para>
  /// <para>
  /// <b>(1) The flag can be STALE, and the tree says so in two places.</b>
  /// <c>Sleep.razor</c>'s dispose report is best-effort behind a 2 s CTS and its own comment says a
  /// hard navigation can tear the circuit down before it lands; <c>MainLayout</c>'s corrective
  /// <c>false</c> on first render is fire-and-forget with the failure swallowed. Lose both — one API
  /// blip on a WiFi-only box — and <c>_isSleepScreenVisible</c> sits <c>true</c> while the console is
  /// genuinely awake on Home. A voicemail then starts; thirty minutes later the idle timer navigates
  /// to <c>/sleep</c>; the page reports <c>true</c>; nothing <i>changed</i> and the state was already
  /// Ambient, so a transition-based rule <b>would not fire</b> — leaving attended audio on a surface
  /// with no transport, which is the exact failure §7.5 exists to prevent.
  /// </para>
  /// <para>
  /// <b>(2) A transition read is not safe against a concurrent report.</b> This method takes no lock
  /// (deliberately — see <see cref="_isSleepScreenVisible"/>), so an "after" state re-read from the
  /// field could observe a <i>different</i> caller's write. A kiosk reload while on <c>/sleep</c>
  /// overlaps the old circuit's dispose <c>false</c> with the new circuit's first-render
  /// <c>true</c>; interleaved the wrong way, the <c>true</c> report reads back <c>Awake</c> and
  /// skips its own stop. Deciding from <b>this call's own argument</b> removes the race by
  /// construction rather than by locking.
  /// </para>
  /// <para>
  /// ⚠ <b>"Never polled" is untouched by this, and it is the constraint that mattered.</b> Trap 5 of
  /// the arc breakdown forbids polls and timers — CPU churn on the N100 correlates with audible
  /// distortion. This rule is still driven entirely by a client's report; there is no loop and no
  /// timer. And <see cref="WakeState"/> is deliberately <i>not</i> consulted, which §16.5 also asks
  /// for: it reads <see cref="ConsoleWakeState.Awake"/> while a wake claim is outstanding
  /// (<c>ENC-6</c>'s fast spin), so any rule reading it answers "Awake" for a console that is not.
  /// </para>
  /// <para>
  /// ⚠ <b>Repeat reports cost nothing, so idempotence needs no edge to provide it.</b>
  /// <see cref="StopAttendedPlaybackAsync"/> decides on the playback's STATE: a second report finds
  /// a <c>Stopped</c> snapshot and returns without an HTTP call or any work. That is a stronger
  /// guarantee than an edge gave, because it also holds when the two reports come from different
  /// clients.
  /// </para>
  /// <para>
  /// ⚠ <b>Stopping, not muting</b> — see <see cref="WakeAsync"/>'s remarks and ADR-029 §16.4. And no
  /// primary source is touched: Ambient is <i>defined</i> by the radio continuing to play with the
  /// sleep screen up. What ends here is attended EVENT playback, which is the thing §7.5's rule is
  /// about.
  /// </para>
  /// <para>
  /// ⚠ <b>One case NEITHER edge covers, named so it is not mistaken for covered:</b> a playback
  /// <i>started</i> while the console is already on <c>/sleep</c>. No report and no sleep entry
  /// follows it, so nothing stops it. §7.5 is written about <i>entering</i> the surface; a playback
  /// arriving at one is the mirror case, and it belongs with <c>D28</c>'s queue in <c>PHN-1f</c>.
  /// </para>
  /// </remarks>
  public async Task SetSleepScreenVisibleAsync(bool visible)
  {
    // The claim is released on an EDGE, not on every report. Clearing it unconditionally would mean
    // a client that re-reported the state it is already in - which nothing does today, but which is
    // the shape any future re-report heartbeat would take - could wipe a claim mid-wake and drop the
    // console back into Ambient, consuming a second input for the same wake.
    //
    // ⚠ This is ENC-6's wake-claim rule and it is UNCHANGED. It is deliberately NOT the rule the
    // stop below uses; the two were briefly conflated and the remarks above say what that cost.
    bool changed = _isSleepScreenVisible != visible;
    _isSleepScreenVisible = visible;
    if (changed)
    {
      Interlocked.Exchange(ref _wakeClaimed, 0);
    }

    _logger.LogDebug("Sleep screen reported {Visible}", visible ? "visible" : "hidden");

    // The rule: a client is telling us the no-transport surface is on screen. Decided from THIS
    // call's argument and nothing else - not from the field just written, and not from WakeState.
    // The remarks above carry the two scenarios that reasoning survives and a transition read does
    // not.
    if (visible)
    {
      await StopAttendedPlaybackAsync();
    }
  }

  public bool TryClaimWake()
  {
    // Read before claiming so an already-awake console never burns the claim that the next genuine
    // sleep would need.
    if (WakeState == ConsoleWakeState.Awake)
    {
      return false;
    }

    return Interlocked.CompareExchange(ref _wakeClaimed, 1, 0) == 0;
  }

  /// <summary>
  /// The attended-playback seam, or null when event playback is not registered. Used for one thing
  /// only: ADR-029 §7.5's rule that entering /sleep stops attended playback.
  /// </summary>
  private readonly IEventPlaybackService? _eventPlayback;

  public SleepService(
    ILogger<SleepService> logger,
    IHubContext<AudioStateHub> hubContext,
    IAudioManager? audioManager = null,
    IEventPlaybackService? eventPlayback = null)
  {
    _logger = logger;
    _hubContext = hubContext;
    _audioManager = audioManager;
    _eventPlayback = eventPlayback;
  }

  /// <summary>
  /// Enters sleep mode: pauses active audio source, saves mute state, mutes audio, broadcasts to UI.
  /// </summary>
  public async Task EnterSleepAsync()
  {
    await _lock.WaitAsync();
    try
    {
      if (_isSleeping)
      {
        return;
      }

      _logger.LogInformation("Entering sleep mode");

      // ADR-029 D7 §7.5, closing that ADR's open question Q8 in the direction it called safe.
      // /sleep declares @layout EmptyLayout, so PR 6's stop chip — which lives in MainLayout's
      // .topbar-primary — does not render there, and MainLayout navigates the console to /sleep
      // ITSELF on a server-pushed sleep and on the idle timer. Attended playback may not exist on a
      // surface that offers no way to stop it.
      //
      // ⚠ A STOP, not a reliance on the mute two blocks below. WakeAsync restores
      // _wasMutedBeforeSleep, so a muted-but-still-playing voicemail would become audible again
      // MID-WORD the instant somebody touches the panel in a dark room — worse than the problem the
      // rule was written about.
      //
      // ⚠ Here rather than in Radio.Web, and this is ONE OF TWO edges rather than the single funnel
      // an earlier revision of this comment claimed. It said "three client paths reach sleep … and
      // all three arrive at this method". ADR-029 §16.4 checked that against the tree and it is
      // false: the 30-minute idle timer reaches /sleep by window.location.href and calls nothing
      // server-side, so IsSleeping is false on the path §7.5 was actually written about.
      //
      // This method covers every route that PARKS THE ROOM — the pill and the server push (which
      // originates here), plus the browserless entries, POST /api/system/sleep and the encoder
      // long-press. The browserless ones are the reason this edge cannot be dropped in favour of the
      // screen report: they have no page to render and so can never produce one.
      // SetSleepScreenVisibleAsync covers the routes that reach /sleep WITHOUT parking audio - the
      // idle timer and a direct navigation - and is a no-op after this method, because by then the
      // playback it would stop is already Stopped.
      await StopAttendedPlaybackAsync();

      if (_audioManager != null)
      {
        // Save and pause active playback
        _wasPlayingBeforeSleep = false;
        if (_audioManager.ActiveSource is IPrimaryAudioSource primary
            && primary.State == AudioSourceState.Playing)
        {
          _wasPlayingBeforeSleep = true;
          try
          {
            await primary.PauseAsync();
            _logger.LogInformation("Paused active source {SourceName} for sleep", primary.Name);
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Failed to pause source for sleep, falling back to mute-only");
          }
        }

        // Save current mute state and mute audio
        _wasMutedBeforeSleep = _audioManager.IsMuted;
        _audioManager.IsMuted = true;
      }

      _isSleeping = true;

      // A claim that was never confirmed would otherwise keep WakeState reading Awake through this
      // standby, and every knob would act on a console the owner just parked.
      Interlocked.Exchange(ref _wakeClaimed, 0);

      await _hubContext.Clients.All
        .SendAsync("SleepStateChanged", true);

      // Hardware DPMS stays off. ENC-15 (2026-09-02) tested the precondition on this box and it
      // failed: the touchscreen leaves the USB bus when the panel powers down, so no touch event can
      // be generated while dark, and the encoder has no evdev node so it cannot wake the compositor
      // either. That leaves one application-mediated wake path where two were required.
      // await SetDisplayPowerAsync(false);

      _logger.LogInformation("Sleep mode entered");
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <summary>
  /// Stops attended event playback that could still be producing sound. Never throws, and never
  /// waits indefinitely: sleep has to happen whether or not a voicemail could be stopped.
  /// </summary>
  /// <remarks>
  /// ⚠ A non-null Current is NOT the same as audio in the room. IEventPlaybackService.Current
  /// RETAINS the last snapshot after a playback ends, because StartAsync answers before any audio
  /// exists — so an acquisition failure has no response left to carry it, and this is the surface a
  /// caller re-reads to find out what happened. (Not the ONLY one: since PHN-1e the same Failed
  /// snapshot is also broadcast over /hubs/audio. Retention is what serves a caller who was not
  /// listening at the moment it fired.) So the state is what decides, not the null check.
  /// </remarks>
  private async Task StopAttendedPlaybackAsync()
  {
    if (_eventPlayback?.Current is not { } snapshot)
    {
      return;
    }

    // Preparing is included deliberately: a fetch or a synthesis still in flight would otherwise
    // start audio moments after the panel went dark.
    if (snapshot.State is not (EventPlaybackState.Preparing
        or EventPlaybackState.Playing
        or EventPlaybackState.Paused))
    {
      return;
    }

    try
    {
      // ⚠ TIME-BOXED, and this is a hang fix rather than hygiene. EnterSleepAsync calls this while
      // holding _lock, and WakeAsync takes the same _lock - so an unbounded wait here makes the
      // console UNWAKEABLE by every route at once: the encoder, the screen tap and the REST call all
      // queue behind it. IEventPlaybackService.StopAsync awaits its own gate and then the source's
      // StopAsync, neither of which is bounded on the API side, and this repo has a documented class
      // of hang in exactly that layer (the long-running capture lifecycle bug).
      //
      // The token only abandons the WAIT - it is what StopAsync passes to its gate - so a timeout
      // leaves any teardown already in progress to finish on its own thread rather than tearing it
      // in half. The catch below absorbs the OperationCanceledException, and sleep proceeds: a
      // voicemail that outlives the timeout is then bounded by GvMedia:MaxPlaybackSeconds, which is
      // the guarantee that needs no client (§7.1).
      using var cts = new CancellationTokenSource(StopTimeout);
      if (await _eventPlayback.StopAsync(snapshot.Id, cts.Token))
      {
        _logger.LogInformation(
          "Sleep stopped attended playback {Id}: /sleep offers no transport (ADR-029 §7.5)",
          snapshot.Id);
      }
    }
    catch (OperationCanceledException)
    {
      _logger.LogWarning(
        "Timed out after {Seconds}s stopping attended playback {Id} on the way into sleep; "
        + "continuing so the console stays wakeable (GvMedia:MaxPlaybackSeconds still bounds it)",
        StopTimeout.TotalSeconds, snapshot.Id);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Error stopping attended playback on the way into sleep");
    }
  }

  /// <summary>
  /// Wakes from sleep: restores mute state, resumes playback if it was active before sleep, broadcasts to UI.
  /// </summary>
  /// <remarks>
  /// ⚠ There is no symmetric restore for the attended playback EnterSleepAsync stopped, and that is
  /// deliberate. ADR-029 §6.2 rule 2's reasoning applies: the recording is replayable at zero cost,
  /// and resuming a voicemail mid-word after a wake is worse than restarting it.
  /// </remarks>
  public async Task WakeAsync(string wakeSource = "unknown")
  {
    await _lock.WaitAsync();
    try
    {
      // Two ways to be somewhere other than Awake, and only one of them parked audio. Standby has
      // playback to restore; Ambient has nothing but a browser to send home. Both need the
      // broadcast, so both fall through.
      bool wasSleeping = _isSleeping;
      if (!wasSleeping && !_isSleepScreenVisible)
      {
        Interlocked.Exchange(ref _wakeClaimed, 0);
        return;
      }

      _logger.LogInformation("Waking from sleep mode (source: {WakeSource})", wakeSource);

      // Hardware DPMS wake stays off - see the note in EnterSleepAsync.
      // await SetDisplayPowerAsync(true);

      _isSleeping = false;

      if (wasSleeping && _audioManager != null)
      {
        // Restore pre-sleep mute state
        _audioManager.IsMuted = _wasMutedBeforeSleep;

        // Resume playback if it was playing before sleep
        if (_wasPlayingBeforeSleep
            && _audioManager.ActiveSource is IPrimaryAudioSource primary
            && primary.State == AudioSourceState.Paused)
        {
          try
          {
            await primary.ResumeAsync();
            _logger.LogInformation("Resumed source {SourceName} after wake", primary.Name);
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "Failed to resume source after wake");
          }
        }
      }

      await _hubContext.Clients.All
        .SendAsync("SleepStateChanged", false);

      _logger.LogInformation("Sleep mode exited");
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <summary>
  /// Controls the physical display via GNOME ScreenSaver D-Bus.
  ///
  /// <para>
  /// ⚠ <b>Nothing calls this, deliberately</b> (see the class remarks). It is retained as the
  /// recorded shape of the thing <c>ENC-15</c> ruled out, so the FUTURE-WORK entry explaining why
  /// blanking does not ship points at real code. Two further reasons not to revive it as written:
  /// the ScreenSaver route <b>does not reach DPMS-off</b> — <c>ENC-15</c> found the panel dark with
  /// <c>dpms=Off</c> while the screensaver reported inactive — and it needs the desktop session bus,
  /// which it reaches by shelling out as another user.
  /// </para>
  /// </summary>
  private async Task SetDisplayPowerAsync(bool on)
  {
    if (!OperatingSystem.IsLinux()) return;

    var active = on ? "false" : "true"; // ScreenSaver active=true means display OFF
    var command = $"sudo -u {SessionUser} {SessionBusEnv} {GnomeScreenSaverSetActive} {active}";

    try
    {
      using var process = Process.Start(new ProcessStartInfo
      {
        FileName = "/bin/bash",
        Arguments = $"-c \"{command}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      });

      if (process != null)
      {
        await process.WaitForExitAsync();
        if (process.ExitCode == 0)
        {
          _logger.LogInformation("Display DPMS {State}", on ? "on" : "off");
        }
        else
        {
          var stderr = await process.StandardError.ReadToEndAsync();
          _logger.LogWarning("Display DPMS control failed (exit {Code}): {Error}",
            process.ExitCode, stderr.Trim());
        }
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to set display power — GNOME ScreenSaver D-Bus may not be available");
    }
  }
}
