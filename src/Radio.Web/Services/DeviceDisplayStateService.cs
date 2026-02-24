namespace Radio.Web.Services;

/// <summary>
/// Scoped service that notifies subscribers when device display settings change
/// (e.g., visibility toggled in Device Management page). This allows MainLayout
/// to refresh its output device list without a full page reload.
/// </summary>
public class DeviceDisplayStateService
{
  /// <summary>
  /// Raised when device display settings change (visibility, friendly name, etc.).
  /// </summary>
  public event Func<Task>? DisplaySettingsChanged;

  /// <summary>
  /// Notifies all subscribers that display settings have changed.
  /// </summary>
  public async Task NotifyDisplaySettingsChangedAsync()
  {
    if (DisplaySettingsChanged != null)
    {
      await DisplaySettingsChanged.Invoke();
    }
  }
}
