using Microsoft.Extensions.Logging;

namespace Radio.Web.Services;

/// <summary>
/// A view the layout can ask the Home centre panel (<c>QueueHistoryPanel</c>) to show.
/// </summary>
public enum CentrePanelView
{
  /// <summary>The queue view — the Queue nav pill (UI-17 point 4). Shown whatever the source.</summary>
  Queue,

  /// <summary>The radio controls tab — the radio-family source chevron (UI-17 point 5).</summary>
  Radio,
}

/// <summary>
/// Scoped carrier for "show this view" requests from <c>MainLayout</c> to the Home centre panel
/// (UI-17). Replaces <c>RadioPanelToggleService</c>, which swapped the whole centre panel for
/// <c>RadioControlPanel</c>; radio controls are now a tab inside the centre panel, so the layout no
/// longer owns a visibility flag — it only asks for a view.
///
/// <para>
/// <b>Why a pending slot as well as an event.</b> The requests come from nav affordances that also
/// navigate to <c>/</c>. When the user is already on Home the centre panel is mounted and subscribed,
/// and the event delivers the request. When the user is on another page the panel does not exist
/// yet: the event reaches no subscriber, the request stays in <see cref="PendingView"/>, and the
/// panel collects it with <see cref="TakePendingView"/> when it initialises. A subscriber that acts
/// on the event is expected to call <see cref="TakePendingView"/> too, so a request is consumed at
/// most once.
/// </para>
/// </summary>
public class CentrePanelViewService(ILogger<CentrePanelViewService> logger)
{
  private readonly ILogger<CentrePanelViewService> _logger = logger;

  /// <summary>
  /// Fired on every <see cref="RequestViewAsync"/> call, with the requested view. Unlike the old
  /// toggle this is not change-gated: asking for the queue twice is two user taps, and the second one
  /// must still win over a tab the user picked in between.
  /// </summary>
  public event Func<CentrePanelView, Task>? ViewRequested;

  /// <summary>
  /// The most recent request not yet collected by a centre panel, or <c>null</c>.
  /// </summary>
  public CentrePanelView? PendingView { get; private set; }

  /// <summary>
  /// Returns <see cref="PendingView"/> and clears it.
  /// </summary>
  public CentrePanelView? TakePendingView()
  {
    CentrePanelView? view = PendingView;
    PendingView = null;
    return view;
  }

  /// <summary>
  /// Records <paramref name="view"/> as pending and notifies every subscriber in turn.
  /// </summary>
  public async Task RequestViewAsync(CentrePanelView view)
  {
    PendingView = view;

    // UI-7. `await ViewRequested.Invoke(view)` on a multicast Func<,Task> runs every subscriber but
    // returns only the LAST one's Task, and a subscriber throwing synchronously starves every handler
    // after it. Walk the list instead; the canonical version of this loop, with the full argument,
    // is AudioStateStore.NotifyAsync.
    if (ViewRequested == null)
    {
      return;
    }

    foreach (Delegate subscriber in ViewRequested.GetInvocationList())
    {
      try
      {
        await ((Func<CentrePanelView, Task>)subscriber).Invoke(view);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Error notifying ViewRequested subscriber");
      }
    }
  }
}
