using Microsoft.AspNetCore.SignalR;
using Radio.API.Hubs;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Services;

/// <summary>
/// Manages sleep/standby mode for the kiosk UI.
/// When sleeping: mutes audio, broadcasts state via SignalR so UI shows black overlay.
/// Wake sources: touch screen, rotary encoder, API call.
/// </summary>
public class SleepService : ISleepService
{
  private readonly ILogger<SleepService> _logger;
  private readonly IHubContext<AudioStateHub> _hubContext;
  private readonly IAudioManager? _audioManager;
  private readonly SemaphoreSlim _lock = new(1, 1);
  private bool _isSleeping;
  private bool _wasMutedBeforeSleep;

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
  /// Enters sleep mode: saves mute state, mutes audio, broadcasts to UI.
  /// </summary>
  public async Task EnterSleepAsync()
  {
    await _lock.WaitAsync();
    try
    {
      if (_isSleeping) return;

      _logger.LogInformation("Entering sleep mode");

      // Save current mute state and mute audio
      if (_audioManager != null)
      {
        _wasMutedBeforeSleep = _audioManager.IsMuted;
        _audioManager.IsMuted = true;
      }

      _isSleeping = true;

      await _hubContext.Clients.All
        .SendAsync("SleepStateChanged", true);

      _logger.LogInformation("Sleep mode entered");
    }
    finally
    {
      _lock.Release();
    }
  }

  /// <summary>
  /// Wakes from sleep: restores mute state, broadcasts to UI.
  /// </summary>
  public async Task WakeAsync(string wakeSource = "unknown")
  {
    await _lock.WaitAsync();
    try
    {
      if (!_isSleeping) return;

      _logger.LogInformation("Waking from sleep mode (source: {WakeSource})", wakeSource);

      _isSleeping = false;

      // Restore pre-sleep mute state
      if (_audioManager != null)
      {
        _audioManager.IsMuted = _wasMutedBeforeSleep;
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
}
