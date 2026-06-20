using Microsoft.Extensions.Logging.Abstractions;
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

public class GvBridgeStatusServiceTests
{
  [Fact]
  public void ApplyStatus_DerivesIsAvailable_AndFiresChange()
  {
    var svc = new GvBridgeStatusService(
      scopeFactory: null!, NullLogger<GvBridgeStatusService>.Instance, pollSeconds: 10);

    GvBridgeStatusDto? observed = null;
    var fired = 0;
    svc.StatusChanged += s => { observed = s; fired++; };

    // null status → degraded
    svc.ApplyStatusForTest(null);
    Assert.False(svc.IsAvailable);
    Assert.Equal(1, fired);

    // available
    svc.ApplyStatusForTest(new GvBridgeStatusDto { Available = true });
    Assert.True(svc.IsAvailable);
    Assert.Equal(2, fired);
    Assert.NotNull(observed);

    // no change in availability → still fires (UI may want fresh fields), but
    // IsAvailable holds
    svc.ApplyStatusForTest(new GvBridgeStatusDto { Available = true });
    Assert.True(svc.IsAvailable);
  }
}
