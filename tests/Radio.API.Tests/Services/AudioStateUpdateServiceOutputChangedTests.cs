using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Hubs;
using Radio.API.Services;
using Radio.Core.Interfaces.Audio;

namespace Radio.API.Tests.Services;

/// <summary>
/// AUD-84: the API now changes the output on its own when a Cast speaker drops, so it has to tell
/// the Web UI — whose output chip otherwise only ever followed its own clicks. Each check is
/// invoked directly and awaited; nothing here reads a clock.
/// </summary>
public class AudioStateUpdateServiceOutputChangedTests
{
  private sealed class RecordingClientProxy : IClientProxy
  {
    public List<(string Method, object? Payload)> Sent { get; } = [];

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
      Sent.Add((method, args.Length > 0 ? args[0] : null));
      return Task.CompletedTask;
    }
  }

  [Fact]
  public async Task ActiveOutputChange_IsBroadcastOnce_WithTheNewOutputId()
  {
    var active = "google-cast";
    var engine = new Mock<IAudioEngine>();
    engine.SetupGet(e => e.ActiveOutputId).Returns(() => active);
    var (service, proxy) = CreateService(engine.Object);

    await service.CheckActiveOutputChangedAsync(CancellationToken.None); // baseline, no send
    await service.CheckActiveOutputChangedAsync(CancellationToken.None); // unchanged
    Assert.Empty(proxy.Sent);

    active = "speakers";
    await service.CheckActiveOutputChangedAsync(CancellationToken.None);
    await service.CheckActiveOutputChangedAsync(CancellationToken.None); // unchanged again

    var sent = Assert.Single(proxy.Sent);
    Assert.Equal("OutputChanged", sent.Method);
    Assert.Equal("speakers", sent.Payload);
  }

  private static (AudioStateUpdateService Service, RecordingClientProxy Proxy) CreateService(IAudioEngine engine)
  {
    var proxy = new RecordingClientProxy();
    var clients = new Mock<IHubClients>();
    clients.SetupGet(c => c.All).Returns(proxy);
    clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy);

    var hubContext = new Mock<IHubContext<AudioStateHub>>();
    hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

    var collection = new ServiceCollection();
    collection.AddSingleton(new Mock<IAudioManager>().Object);
    collection.AddSingleton(engine);

    var service = new AudioStateUpdateService(
      NullLogger<AudioStateUpdateService>.Instance,
      hubContext.Object,
      collection.BuildServiceProvider(),
      new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

    return (service, proxy);
  }
}
