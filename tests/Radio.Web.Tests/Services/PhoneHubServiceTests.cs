using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services.Hub;

namespace Radio.Web.Tests.Services;

/// <summary>
/// Unit tests for PhoneHubService's GV read-state surface (GV-4 / ADR-024 §4).
/// The defensive Kind guard is exercised via the internal RaiseReadStateChangedForTest
/// seam, which runs the SAME guard the live /hub `.On&lt;&gt;` lambda calls.
/// </summary>
public class PhoneHubServiceTests
{
  private static PhoneHubService BuildHubServiceUnderTest() =>
    new(NullLogger<PhoneHubService>.Instance,
      new ConfigurationBuilder().Build());

  [Fact]
  public void ReadStateChanged_RaisesEvent_ForKnownKind()
  {
    var svc = BuildHubServiceUnderTest();
    ReadStateChangedDto? captured = null;
    svc.ReadStateChanged += d => captured = d;

    svc.RaiseReadStateChangedForTest(
      new ReadStateChangedDto("Voicemail", "vm1", "t1", true, DateTime.UtcNow));

    Assert.NotNull(captured);
    Assert.Equal("vm1", captured!.Id);
  }

  [Fact]
  public void ReadStateChanged_RaisesEvent_ForSmsKind_CaseInsensitive()
  {
    var svc = BuildHubServiceUnderTest();
    ReadStateChangedDto? captured = null;
    svc.ReadStateChanged += d => captured = d;

    svc.RaiseReadStateChangedForTest(
      new ReadStateChangedDto("sms", null, "t1", true, DateTime.UtcNow));

    Assert.NotNull(captured);
    Assert.Equal("t1", captured!.ThreadId);
  }

  [Fact]
  public void ReadStateChanged_IgnoresUnknownKind()
  {
    var svc = BuildHubServiceUnderTest();
    var raised = false;
    svc.ReadStateChanged += _ => raised = true;

    svc.RaiseReadStateChangedForTest(
      new ReadStateChangedDto("Garbage", null, null, true, DateTime.UtcNow));

    Assert.False(raised);   // unknown Kind ignored
  }
}
