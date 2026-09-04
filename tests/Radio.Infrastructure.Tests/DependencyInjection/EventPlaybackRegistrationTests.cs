using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.DependencyInjection;
using Radio.Infrastructure.Tests.Audio.Events;

namespace Radio.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// The container guard for AddEventPlayback, extending the ValidateOnBuild/ValidateScopes pattern
/// GvMediaRegistrationTests introduced rather than inventing another.
/// </summary>
/// <remarks>
/// One honest difference from that one. AddGvMedia's graph is CLOSED — everything it needs it
/// registers. AddEventPlayback's is not: ITTSFactory, IDuckingService and AudioFileEventSourceFactory
/// come from AddSoundFlowAudio, which initialises real audio hardware and is precisely why
/// ActiveSourceAccessorRegistrationTests can only inspect descriptors. So this guard registers FAKES
/// for those three and validates what AddEventPlayback itself contributes.
///
/// ⚠ That constraint is what shaped EventPlaybackService's constructor, and the causality is worth
/// recording because it looks like a coincidence. Constructing AudioFileEventSource directly would
/// need SoundFlowPlaybackService — a concrete class whose constructor takes the concrete
/// SoundFlowAudioEngine and starts a background monitor task, and which therefore cannot be faked.
/// Routing through AudioFileEventSourceFactory.CreateFromAbsolutePathAsync instead means no
/// SoundFlow type appears in EventPlaybackService's constructor at all, which is what makes this
/// guard possible.
///
/// ⚠ What this cannot do, so nobody over-trusts it. AddSingleton&lt;EventPlaybackService&gt;() is a
/// CONSTRUCTOR registration, so ValidateOnBuild genuinely introspects it and a missing dependency
/// fails the build of the provider. But AddSingleton&lt;IEventPlaybackService&gt;(sp =&gt; …) is a
/// FACTORY, which ValidateOnBuild cannot introspect — so the GetRequiredService in each fact is what
/// actually exercises it. And the fakes mean this proves nothing about AddSoundFlowAudio still
/// registering those three; EventPlaybackControllerTests, which builds the real API container, is
/// what covers that.
/// </remarks>
public class EventPlaybackRegistrationTests
{
  private static ServiceProvider BuildProvider(IConfiguration? configuration = null)
  {
    var config = configuration ?? new ConfigurationBuilder().Build();

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddOptions();
    services.AddSingleton<IConfiguration>(config);

    // From AddGvMedia — a real registration, closed graph, already guarded by
    // GvMediaRegistrationTests.
    services.AddGvMedia(config);

    // From AddSoundFlowAudio, which cannot be called here: it initialises real audio hardware.
    // These three are what EventPlaybackService needs from it, and faking them is what keeps this a
    // REAL build-and-resolve guard rather than a descriptor check.
    services.AddSingleton<ITTSFactory, FakeTtsFactory>();
    services.AddSingleton<IDuckingService, FakeDuckingService>();
    services.AddSingleton<AudioFileEventSourceFactory>();
    services.Configure<FilePlayerOptions>(_ => { });

    services.AddEventPlayback();

    return services.BuildServiceProvider(new ServiceProviderOptions
    {
      ValidateOnBuild = true,
      ValidateScopes = true
    });
  }

  [Fact]
  public void AddEventPlayback_BuildsAndResolvesTheSeam_WithNoGvMediaSectionAtAll()
  {
    // ⚠ Merged, because this and ItResolvesWithNoGvMediaSectionAtAll were the SAME TEST written
    // twice: BuildProvider's default configuration is an empty ConfigurationBuilder, so both were
    // "resolve with no GvMedia section", differing only in which of the two registrations they asked
    // for — and TheInterfaceAndTheConcreteTypeResolveToOneInstance already covers that difference
    // properly, by asserting they are the same object. Two green tests for one fact is a coverage
    // number, not coverage.
    //
    // What it asserts: the defaults are sufficient to construct everything, which is what an
    // appliance with no GvMedia block gets. ItResolvesWithAGvMediaSectionPresent is the contrast.
    using var provider = BuildProvider();

    Assert.NotNull(provider.GetRequiredService<IEventPlaybackService>());
  }

  [Fact]
  public void TheInterfaceAndTheConcreteTypeResolveToOneInstance()
  {
    // Two instances would be two Current properties and two single slots — two attended playbacks
    // that each believe they are the only one, on one set of speakers.
    using var provider = BuildProvider();

    Assert.Same(
      provider.GetRequiredService<EventPlaybackService>(),
      (EventPlaybackService)provider.GetRequiredService<IEventPlaybackService>());
  }

  [Fact]
  public void ItResolvesWithAGvMediaSectionPresent()
  {
    // The other half of the pair: options bound from configuration must not break construction
    // either. Enabled=true is the state PR 6 will ship with, and it is worth knowing now that the
    // graph builds under it rather than only under the shipped false.
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["GvMedia:Enabled"] = "true",
        ["GvMedia:BaseUrl"] = "http://example.invalid:1234"
      })
      .Build();

    using var provider = BuildProvider(configuration);

    Assert.NotNull(provider.GetRequiredService<IEventPlaybackService>());
  }
}
