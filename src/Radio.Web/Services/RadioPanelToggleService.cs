namespace Radio.Web.Services;

/// <summary>
/// Scoped service for toggling the inline radio control panel on the Home page.
/// MainLayout fires the toggle; Home.razor subscribes and swaps center panel content.
/// </summary>
public class RadioPanelToggleService
{
  /// <summary>
  /// Fired when the radio panel should toggle visibility.
  /// </summary>
  public event Func<Task>? RadioPanelToggled;

  /// <summary>
  /// Whether the radio panel is currently shown (vs Queue/History).
  /// </summary>
  public bool IsRadioPanelVisible { get; set; }

  /// <summary>
  /// Toggles the radio panel and notifies subscribers.
  /// </summary>
  public async Task ToggleRadioPanelAsync()
  {
    IsRadioPanelVisible = !IsRadioPanelVisible;
    if (RadioPanelToggled != null)
    {
      await RadioPanelToggled.Invoke();
    }
  }

  /// <summary>
  /// Shows the radio panel (e.g., when activating Radio source).
  /// </summary>
  public async Task ShowRadioPanelAsync()
  {
    if (IsRadioPanelVisible)
    {
      return;
    }

    IsRadioPanelVisible = true;
    if (RadioPanelToggled != null)
    {
      await RadioPanelToggled.Invoke();
    }
  }

  /// <summary>
  /// Hides the radio panel (e.g., when switching away from Radio source).
  /// </summary>
  public async Task HideRadioPanelAsync()
  {
    if (!IsRadioPanelVisible)
    {
      return;
    }

    IsRadioPanelVisible = false;
    if (RadioPanelToggled != null)
    {
      await RadioPanelToggled.Invoke();
    }
  }
}
