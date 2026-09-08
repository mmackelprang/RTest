using Microsoft.Extensions.Logging;

namespace Radio.Web.Services;

/// <summary>
/// Scoped service that notifies subscribers when device display settings change
/// (e.g., visibility toggled in Device Management page). This allows MainLayout
/// to refresh its output device list without a full page reload.
/// </summary>
public class DeviceDisplayStateService(ILogger<DeviceDisplayStateService> logger)
{
  private readonly ILogger<DeviceDisplayStateService> _logger = logger;

  /// <summary>
  /// Raised when device display settings change (visibility, friendly name, etc.).
  /// </summary>
  public event Func<Task>? DisplaySettingsChanged;

  /// <summary>
  /// Notifies every subscriber that display settings have changed, awaiting each one and isolating
  /// its exceptions. UI-7 — before that row this said "all subscribers" while awaiting only the last.
  /// </summary>
  public async Task NotifyDisplaySettingsChangedAsync()
  {
    // UI-7. `await DisplaySettingsChanged.Invoke()` on a multicast Func<Task> runs every subscriber
    // but returns only the LAST one's Task, and a subscriber throwing synchronously starves every
    // handler after it. This service is AddScoped (Program.cs:475) with one subscriber today
    // (MainLayout.razor:387), so the list is length 1 and the defect was latent here — but "one
    // subscriber" is a fact about today, not a constraint, which is exactly the reasoning UI-7
    // C-203 found to be false about AudioStateHubService. The canonical version of this loop, with
    // the full argument, is AudioStateStore.NotifyAsync.
    if (DisplaySettingsChanged == null)
    {
      return;
    }

    foreach (var subscriber in DisplaySettingsChanged.GetInvocationList())
    {
      try
      {
        await ((Func<Task>)subscriber).Invoke();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error notifying DisplaySettingsChanged subscriber");
      }
    }
  }
}
