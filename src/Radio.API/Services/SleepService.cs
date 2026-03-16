using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Radio.API.Hubs;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Services;

/// <summary>
/// Manages sleep/standby mode for the kiosk UI.
/// When sleeping: pauses audio playback, mutes audio, turns off display via DPMS,
/// broadcasts state via SignalR so UI shows black overlay.
/// Wake sources: touch screen, rotary encoder, API call.
/// On wake: restores display, mute state, and resumes playback if it was playing before sleep.
/// </summary>
public class SleepService : ISleepService
{
  private readonly ILogger<SleepService> _logger;
  private readonly IHubContext<AudioStateHub> _hubContext;
  private readonly IAudioManager? _audioManager;
  private readonly SemaphoreSlim _lock = new(1, 1);
  private bool _isSleeping;
  private bool _wasMutedBeforeSleep;
  private bool _wasPlayingBeforeSleep;

  // GNOME ScreenSaver D-Bus for physical display DPMS control.
  // Runs as the desktop session user (mmack) to reach the GNOME session bus.
  private const string GnomeScreenSaverSetActive =
    "gdbus call --session --dest org.gnome.ScreenSaver --object-path /org/gnome/ScreenSaver --method org.gnome.ScreenSaver.SetActive";
  private const string SessionUser = "mmack";
  private const string SessionBusEnv = "DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus";

  public bool IsSleeping => _isSleeping;

  public SleepService(
    ILogger<SleepService> logger,
    IHubContext<AudioStateHub> hubContext,
    IAudioManager? audioManager = null)
  {
    _logger = logger;
    _hubContext = hubContext;
    _audioManager = audioManager;
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

      await _hubContext.Clients.All
        .SendAsync("SleepStateChanged", true);

      // Turn off physical display via GNOME ScreenSaver DPMS
      await SetDisplayPowerAsync(false);

      _logger.LogInformation("Sleep mode entered");
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <summary>
  /// Wakes from sleep: restores mute state, resumes playback if it was active before sleep, broadcasts to UI.
  /// </summary>
  public async Task WakeAsync(string wakeSource = "unknown")
  {
    await _lock.WaitAsync();
    try
    {
      if (!_isSleeping)
      {
        return;
      }

      _logger.LogInformation("Waking from sleep mode (source: {WakeSource})", wakeSource);

      // Turn on physical display FIRST so user sees the UI immediately
      await SetDisplayPowerAsync(true);

      _isSleeping = false;

      if (_audioManager != null)
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
  /// Controls the physical display via GNOME ScreenSaver D-Bus (DPMS on/off).
  /// Runs as the desktop session user to reach the GNOME session bus.
  /// Fails silently on non-Linux or non-GNOME environments.
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
