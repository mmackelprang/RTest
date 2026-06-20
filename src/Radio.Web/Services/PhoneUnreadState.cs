namespace Radio.Web.Services;

/// <summary>
/// UI-local unread sum (unheard voicemail + unread SMS threads + missed calls)
/// surfaced from PhonePage to the topbar /phone pill badge in MainLayout.
/// Singleton so both the page and the layout share one source of truth.
/// v1 counts are UI-local only — a hard reload re-derives from isRead/hasUnread
/// (handoff Badge model). Missed calls DO contribute (owner decision 2).
/// </summary>
public sealed class PhoneUnreadState
{
  private int _count;
  public int Count => _count;
  public event Action<int>? Changed;

  public void Set(int count)
  {
    if (count == _count)
    {
      return;
    }
    _count = count < 0 ? 0 : count;
    Changed?.Invoke(_count);
  }
}
