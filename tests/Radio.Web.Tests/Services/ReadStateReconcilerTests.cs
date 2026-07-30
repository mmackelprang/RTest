using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

public class ReadStateReconcilerTests
{
  private static VoicemailItemDto Vm(string id, bool isRead) =>
    new(id, "t1", "+15551234567", "Jane", DateTime.UtcNow, 42, isRead, "hi",
      $"/api/gvbridge/voicemail/{id}/audio");

  private static SmsThreadDto Thread(string id, bool hasUnread) =>
    new(id, "+15551234567", "Mom", DateTime.UtcNow, hasUnread, "ok");

  [Fact]
  public void ApplyVoicemail_FlipsUnreadToRead_ReturnsTrue()
  {
    var list = new List<VoicemailItemDto> { Vm("vm1", isRead: false) };

    var changed = ReadStateReconciler.ApplyVoicemail(list, "vm1", isRead: true);

    Assert.True(changed);
    Assert.True(list[0].IsRead);
  }

  [Fact]
  public void ApplyVoicemail_AlreadyInState_IsNoOp_ReturnsFalse()
  {
    // The echoed broadcast of our own mark, or a re-mark, must be idempotent.
    var list = new List<VoicemailItemDto> { Vm("vm1", isRead: true) };

    var changed = ReadStateReconciler.ApplyVoicemail(list, "vm1", isRead: true);

    Assert.False(changed);                // no change → caller skips StateHasChanged
    Assert.True(list[0].IsRead);
  }

  [Fact]
  public void ApplyVoicemail_UnknownId_IsNoOp_ReturnsFalse()
  {
    var list = new List<VoicemailItemDto> { Vm("vm1", isRead: false) };

    Assert.False(ReadStateReconciler.ApplyVoicemail(list, "missing", isRead: true));
    Assert.False(list[0].IsRead);
  }

  [Fact]
  public void ApplyVoicemail_TwoSignalsSameKey_AppliesOnce()
  {
    // Mark route returned DTO + echoed broadcast = same (id, isRead). Second is no-op.
    var list = new List<VoicemailItemDto> { Vm("vm1", isRead: false) };

    var first = ReadStateReconciler.ApplyVoicemail(list, "vm1", isRead: true);
    var second = ReadStateReconciler.ApplyVoicemail(list, "vm1", isRead: true);

    Assert.True(first);
    Assert.False(second);                 // idempotent on (id-or-threadId + isRead)
    Assert.True(list[0].IsRead);
  }

  [Fact]
  public void ApplyThread_FlipsHasUnread_ReturnsTrue_ThenNoOp()
  {
    // Thread "read" = hasUnread:false. isRead:true in the event maps to hasUnread:false.
    var list = new List<SmsThreadDto> { Thread("t1", hasUnread: true) };

    var first = ReadStateReconciler.ApplyThread(list, "t1", isRead: true);
    var second = ReadStateReconciler.ApplyThread(list, "t1", isRead: true);

    Assert.True(first);
    Assert.False(second);
    Assert.False(list[0].HasUnread);
  }
}
