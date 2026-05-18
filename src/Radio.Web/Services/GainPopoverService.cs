namespace Radio.Web.Services;

/// <summary>
/// Scoped service that owns the visibility of the gain popover's click-away
/// backdrop. The backdrop ITSELF is rendered by <c>MainLayout.razor</c> — a
/// layout-level mount-point that is OUTSIDE the <c>.page-transition</c>
/// wrapper. That escape from the page-transition stacking context is
/// load-bearing: <c>.page-transition</c> declares <c>transform</c> +
/// <c>will-change: transform, opacity</c> which creates a sub-tree stacking
/// context that traps any descendant's <c>z-index</c> regardless of how
/// large it is. The legacy backdrop lived inside <c>NowPlayingPanel.razor</c>
/// and therefore inside that trapped sub-tree, so clicks on the
/// <c>RadioControlPanel</c> (also a descendant of <c>.page-transition</c>)
/// rendered above the z-index 9999 backdrop and the popover's click-away
/// dismiss silently failed.
///
/// <para>
/// Wire-path: <c>NowPlayingPanel</c>'s gain trigger calls <see cref="Open"/>
/// when the popover opens and <see cref="Close"/> on the close paths
/// (transitively from the backdrop click via the <see cref="OnClose"/>
/// callback the panel subscribes to). <c>MainLayout</c> watches
/// <see cref="IsOpen"/> + the <see cref="StateChanged"/> event and renders
/// the backdrop element accordingly.
/// </para>
///
/// <para>
/// Idempotent set + event-on-change — mirrors <see cref="RadioPanelToggleService"/>
/// so subscribers only see <see cref="StateChanged"/> on real transitions.
/// </para>
/// </summary>
public class GainPopoverService
{
  /// <summary>
  /// Whether the click-away backdrop is currently mounted in MainLayout.
  /// Set via <see cref="Open"/> / <see cref="Close"/>; never assigned
  /// externally so the change-event pattern stays consistent.
  /// </summary>
  public bool IsOpen { get; private set; }

  /// <summary>
  /// Fired when <see cref="IsOpen"/> transitions. Not invoked on idempotent
  /// set-to-same-value calls.
  /// </summary>
  public event Action? StateChanged;

  /// <summary>
  /// Fired when the backdrop in MainLayout is clicked. NowPlayingPanel
  /// subscribes so it can tear down its popover state (showing/hiding the
  /// popover anchor + restoring the gain trigger button state). Stays on
  /// the service rather than the panel so MainLayout never reaches into
  /// the panel directly.
  /// </summary>
  public event Action? OnClose;

  /// <summary>Mounts the backdrop. Called by the popover owner when it
  /// opens the popover.</summary>
  public void Open() => Set(true);

  /// <summary>Unmounts the backdrop. Called by the popover owner on every
  /// close path (Esc, programmatic close, etc.).</summary>
  public void Close() => Set(false);

  /// <summary>
  /// Handler bound to the backdrop's <c>@onclick</c> in MainLayout. Fires
  /// <see cref="OnClose"/> so the panel can clean up its own state, then
  /// closes the backdrop. The two-step (notify-then-unmount) order keeps
  /// the panel and the backdrop in sync even if the panel's subscriber
  /// throws.
  /// </summary>
  public void HandleBackdropClick()
  {
    try
    {
      OnClose?.Invoke();
    }
    finally
    {
      Close();
    }
  }

  private void Set(bool value)
  {
    if (IsOpen == value)
    {
      return;
    }
    IsOpen = value;
    StateChanged?.Invoke();
  }
}
