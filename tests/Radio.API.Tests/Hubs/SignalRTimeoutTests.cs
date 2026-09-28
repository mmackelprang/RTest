using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Radio.API.Tests.TestSupport;
using Xunit;

namespace Radio.API.Tests.Hubs;

/// <summary>
/// UI-10: the server's keepalive must reach an otherwise silent client well inside the client's
/// ServerTimeout, or every idle connection reconnects once per timeout (535 a day on the box).
/// </summary>
public class SignalRTimeoutTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
  // SignalR's client default (HubConnection.DefaultServerTimeout), used by every client of these hubs.
  // Radio.Web.Tests pins that premise (HubReconnectLoggingTests.ClientDefaultServerTimeout_Is30Seconds).
  private static readonly TimeSpan ClientServerTimeout = TimeSpan.FromSeconds(30);

  private readonly CustomWebApplicationFactory<Program> _factory;

  public SignalRTimeoutTests(CustomWebApplicationFactory<Program> factory)
  {
    _factory = factory;
  }

  [Fact]
  public void KeepAlive_IsAtMostHalfTheClientServerTimeout_ForEachHub()
  {
    // Per-hub options too: an AddHubOptions<T> override would otherwise slip past a global check. A
    // null per-hub value means "inherit the global one", so the effective value is checked.
    var global = _factory.Services.GetRequiredService<IOptions<HubOptions>>().Value.KeepAliveInterval;
    Assert.NotNull(global);
    AssertKeepAlive(global, "global");
    AssertKeepAlive(_factory.Services.GetRequiredService<IOptions<HubOptions<Radio.API.Hubs.AudioVisualizationHub>>>().Value.KeepAliveInterval ?? global, "AudioVisualizationHub");
    AssertKeepAlive(_factory.Services.GetRequiredService<IOptions<HubOptions<Radio.API.Hubs.AudioStateHub>>>().Value.KeepAliveInterval ?? global, "AudioStateHub");
  }

  private static void AssertKeepAlive(TimeSpan? keepAlive, string which)
  {
    Assert.True(keepAlive!.Value * 2 <= ClientServerTimeout,
      $"{which}: KeepAliveInterval {keepAlive} must be <= half the clients' ServerTimeout ({ClientServerTimeout})");
  }

  [Fact]
  public void ClientTimeout_StaysGenerousForThrottledKioskTimers()
  {
    var options = _factory.Services.GetRequiredService<IOptions<HubOptions>>().Value;
    Assert.Equal(TimeSpan.FromMinutes(2), options.ClientTimeoutInterval);
  }
}
