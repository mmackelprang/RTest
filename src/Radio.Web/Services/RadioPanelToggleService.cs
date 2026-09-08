using Microsoft.Extensions.Logging;

namespace Radio.Web.Services;

/// <summary>
/// Scoped service for toggling the inline radio control panel on the Home page.
/// MainLayout fires the toggle; Home.razor subscribes and swaps center panel content.
///
/// <para>
/// The Show/Hide/Toggle methods centralise on a private <c>SetIsVisibleAsync</c>
/// that no-ops when the new value equals the current value (idempotent set,
/// event-on-change). Subscribers therefore only see <see cref="RadioPanelToggled"/>
/// invocations on real state transitions — mirrors the pattern adopted by
/// <see cref="VisualizerTelemetryService"/> in Arc 1 PR 4.
/// </para>
/// </summary>
public class RadioPanelToggleService(ILogger<RadioPanelToggleService> logger)
{
  private readonly ILogger<RadioPanelToggleService> _logger = logger;

  /// <summary>
  /// Fired when the radio panel visibility state changes. Not invoked on
  /// idempotent set-to-same-value calls.
  /// </summary>
  public event Func<Task>? RadioPanelToggled;

  /// <summary>
  /// Whether the radio panel is currently shown (vs Queue/History).
  /// External writers should prefer <see cref="ShowRadioPanelAsync"/> /
  /// <see cref="HideRadioPanelAsync"/> / <see cref="ToggleRadioPanelAsync"/>
  /// so subscribers receive change events.
  /// </summary>
  public bool IsRadioPanelVisible { get; private set; }

  /// <summary>
  /// Toggles the radio panel and notifies subscribers (always fires — the new
  /// value is the opposite of the current value by definition).
  /// </summary>
  public Task ToggleRadioPanelAsync() => SetIsVisibleAsync(!IsRadioPanelVisible);

  /// <summary>
  /// Shows the radio panel (e.g., when activating Radio source). No-op + no
  /// event invocation when the panel is already visible.
  /// </summary>
  public Task ShowRadioPanelAsync() => SetIsVisibleAsync(true);

  /// <summary>
  /// Hides the radio panel (e.g., when switching away from Radio source).
  /// No-op + no event invocation when the panel is already hidden.
  /// </summary>
  public Task HideRadioPanelAsync() => SetIsVisibleAsync(false);

  /// <summary>
  /// Centralised set + change-notification. Returns immediately when
  /// <paramref name="value"/> equals the current state so subscribers are not
  /// invoked for no-op writes (idempotent set, event-on-change).
  /// </summary>
  private async Task SetIsVisibleAsync(bool value)
  {
    if (IsRadioPanelVisible == value)
    {
      return;
    }

    IsRadioPanelVisible = value;

    // UI-7. `await RadioPanelToggled.Invoke()` on a multicast Func<Task> runs every subscriber but
    // returns only the LAST one's Task, and a subscriber throwing synchronously starves every
    // handler after it. This service is AddScoped (Program.cs:476) with one subscriber today
    // (Home.razor:36), so the list is length 1 and the defect was latent here — but "one
    // subscriber" is a fact about today, not a constraint, which is exactly the reasoning UI-7
    // C-203 found to be false about AudioStateHubService. The canonical version of this loop, with
    // the full argument, is AudioStateStore.NotifyAsync.
    if (RadioPanelToggled == null)
    {
      return;
    }

    foreach (var subscriber in RadioPanelToggled.GetInvocationList())
    {
      try
      {
        await ((Func<Task>)subscriber).Invoke();
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error notifying RadioPanelToggled subscriber");
      }
    }
  }
}
