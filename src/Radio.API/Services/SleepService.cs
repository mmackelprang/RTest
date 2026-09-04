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
/// <c>SleepStateChanged</c> over SignalR. <b>Ambient</b> changes no audio at all — it is the
/// <c>/sleep</c> route being on screen while playback continues, reported by the page itself. Waking
/// restores the pre-sleep mute state and resumes playback <i>only</i> where playback was parked.
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

  public void SetSleepScreenVisible(bool visible)
  {
    // The claim is released on an EDGE, not on every report. Clearing it unconditionally would mean
    // a client that re-reported the state it is already in - which nothing does today, but which is
    // the shape any future re-report heartbeat would take - could wipe a claim mid-wake and drop the
    // console back into Ambient, consuming a second input for the same wake.
    bool changed = _isSleepScreenVisible != visible;
    _isSleepScreenVisible = visible;
    if (changed)
    {
      Interlocked.Exchange(ref _wakeClaimed, 0);
    }

    _logger.LogDebug("Sleep screen reported {Visible}", visible ? "visible" : "hidden");
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
      // ⚠ Here rather than in Radio.Web. Three client paths reach sleep — the Sleep pill, the
      // idle-dimmer JS callback, and a server-pushed SleepStateChanged — and all three arrive at this
      // method. One place covers every route, every client, and the entry point nobody has written
      // yet.
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
  /// Stops attended event playback that could still be producing sound. Never throws: sleep has to
  /// happen whether or not a voicemail could be stopped.
  /// </summary>
  /// <remarks>
  /// ⚠ A non-null Current is NOT the same as audio in the room. IEventPlaybackService.Current
  /// RETAINS the last snapshot after a playback ends, because StartAsync answers before any audio
  /// exists and that surface is the only place an acquisition failure can be read from. So the state
  /// is what decides, not the null check.
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
      if (await _eventPlayback.StopAsync(snapshot.Id))
      {
        _logger.LogInformation(
          "Sleep stopped attended playback {Id}: /sleep offers no transport (ADR-029 §7.5)",
          snapshot.Id);
      }
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
