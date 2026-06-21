using Radio.Web.Models;

namespace Radio.Web.Services;

/// <summary>
/// Idempotent read-state reconciliation keyed by (id-or-threadId + isRead) — ADR-024 §9.
/// RotaryPhone broadcasts ReadStateChanged UNCONDITIONALLY, including back to the
/// originator, so every mark produces ≥2 signals (the mark route's returned DTO and the
/// echoed broadcast); 502 adds a third "keep optimistic, reconcile later" path. All
/// resolve to the same key. Apply* returns TRUE only if the list actually changed, so the
/// caller skips StateHasChanged on an echo of an already-applied mark (no flicker, no
/// double-apply). Records are immutable → we replace the element via `with`.
/// </summary>
public static class ReadStateReconciler
{
  /// <summary>Set voicemail {id}.IsRead = isRead. Returns true iff something changed.</summary>
  public static bool ApplyVoicemail(List<VoicemailItemDto> voicemails, string? id, bool isRead)
  {
    if (string.IsNullOrEmpty(id))
    {
      return false;
    }
    var idx = voicemails.FindIndex(v => v.Id == id);
    if (idx < 0)
    {
      return false;                        // unknown id → no-op
    }
    if (voicemails[idx].IsRead == isRead)
    {
      return false;                        // already in state → idempotent no-op
    }
    voicemails[idx] = voicemails[idx] with { IsRead = isRead };
    return true;
  }

  /// <summary>
  /// Set thread {threadId} read-state. The event's isRead:true means "thread fully read"
  /// → HasUnread = false (ADR-024 §4 payload note). Returns true iff something changed.
  /// </summary>
  public static bool ApplyThread(List<SmsThreadDto> threads, string? threadId, bool isRead)
  {
    if (string.IsNullOrEmpty(threadId))
    {
      return false;
    }
    var idx = threads.FindIndex(t => t.ThreadId == threadId);
    if (idx < 0)
    {
      return false;
    }
    var hasUnread = !isRead;
    if (threads[idx].HasUnread == hasUnread)
    {
      return false;                        // idempotent no-op
    }
    threads[idx] = threads[idx] with { HasUnread = hasUnread };
    return true;
  }
}
