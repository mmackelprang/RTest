using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Interfaces.Input;
using Radio.Infrastructure.DependencyInjection;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// Verifies the encoder registrations the ENC-4 HUD path depends on.
///
/// <para>
/// The router is built by an explicit factory rather than by <c>ActivatorUtilities</c>, so a
/// constructor argument added without a matching registration fails at resolution time — which on
/// this box means at service start, on an appliance in a cabinet. Resolving it here is cheaper than
/// finding out there.
/// </para>
/// </summary>
public class RotaryEncoderRegistrationTests
{
  private static ServiceProvider BuildProvider()
  {
    var services = new ServiceCollection();
    services.AddLogging();
    // The router resolves IAudioManager through a Func<>, so the audio graph is not needed here -
    // and must not be built, because doing so would initialize real audio hardware.
    services.AddRotaryEncoders(new ConfigurationBuilder().Build());
    return services.BuildServiceProvider();
  }

  [Fact]
  public void AddRotaryEncoders_ResolvesTheActionRouter()
  {
    using var provider = BuildProvider();

    using var router = provider.GetRequiredService<RotaryEncoderActionRouter>();

    Assert.NotNull(router);
  }

  [Fact]
  public void AddRotaryEncoders_ResolvesTheFeedbackSink()
  {
    using var provider = BuildProvider();

    Assert.NotNull(provider.GetRequiredService<IEncoderFeedbackSink>());
  }

  [Fact]
  public void TheSinkAndTheConcreteFeedbackService_AreTheSameInstance()
  {
    // The router publishes into IEncoderFeedbackSink and AudioStateUpdateService subscribes to it.
    // Two instances would be a channel with a publisher on one end and a subscriber on the other,
    // failing silently: no exception, no log line, just a HUD that never appears.
    using var provider = BuildProvider();

    var sink = provider.GetRequiredService<IEncoderFeedbackSink>();
    var concrete = provider.GetRequiredService<EncoderFeedbackService>();

    Assert.Same(concrete, sink);
  }
}
