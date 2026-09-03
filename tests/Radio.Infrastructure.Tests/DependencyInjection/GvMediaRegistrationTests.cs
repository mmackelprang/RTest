using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Radio.Core.Configuration;
using Radio.Infrastructure.DependencyInjection;
using Radio.Infrastructure.External;

namespace Radio.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// The first container guard in this repository with eager validation.
///
/// <para>
/// PR 1 registered nothing and was exempt. PR 2 is the first PR in the ADR-029 arc that registers
/// services, and the failure mode is a service that will not start — on an appliance, in a cabinet.
/// Nothing existing would catch it: RotaryEncoderRegistrationTests covers only AddRotaryEncoders,
/// ActiveSourceAccessorRegistrationTests inspects descriptors rather than resolving, and neither
/// ValidateOnBuild nor ValidateScopes appeared anywhere in src/ or tests/ before this file - which
/// is why RotaryEncoderRegistrationTests, which does build a provider and resolve from it, cannot
/// fail on a dependency that is merely unregistered until something asks for it.
/// </para>
///
/// <para>
/// Validation is scoped to AddGvMedia on purpose. Turning it on over the whole graph would fail on
/// pre-existing hardware-touching registrations, which is a different row and not this one's to
/// open.
/// </para>
/// </summary>
public class GvMediaRegistrationTests
{
  private static ServiceProvider BuildProvider(IConfiguration? configuration = null)
  {
    var config = configuration ?? new ConfigurationBuilder().Build();

    var services = new ServiceCollection();
    services.AddLogging();
    // ⚠ IConfiguration has to be IN the container, not merely passed to AddGvMedia. The real host
    // registers it; this container would not, and GvMediaStartupCheck takes it as a constructor
    // dependency — so with ValidateOnBuild on, omitting this line fails the build of the provider
    // rather than any assertion below. Registering it is also the more faithful reproduction of
    // what Radio.API actually does.
    services.AddSingleton<IConfiguration>(config);
    services.AddGvMedia(config);

    return services.BuildServiceProvider(new ServiceProviderOptions
    {
      ValidateOnBuild = true,
      ValidateScopes = true
    });
  }

  [Fact]
  public void AddGvMedia_BuildsAndResolvesTheClient()
  {
    // An empty configuration on purpose: this also proves the defaults in GvMediaOptions are
    // sufficient to construct everything, which is what an appliance with no GvMedia block gets.
    using var provider = BuildProvider();

    var client = provider.GetRequiredService<GvMediaClient>();

    Assert.NotNull(client);
  }

  [Fact]
  public void AddGvMedia_ResolvesTheAuthHandlerAndTheCache()
  {
    using var provider = BuildProvider();

    Assert.NotNull(provider.GetRequiredService<GvMediaAuthHandler>());
    Assert.NotNull(provider.GetRequiredService<GvMediaCache>());
  }

  [Fact]
  public void TheCacheIsASingleton_SoOneWriteLockSerialisesEveryWriter()
  {
    // Two instances would be two write locks over one directory: concurrent evictions racing each
    // other's deletes, failing quietly rather than loudly. Two things this does NOT claim. The
    // scope is this PROVIDER, not the process - a second container in the same process gets its own
    // cache and its own lock, which is exactly what every test in this file relies on. And the lock
    // serialises writers against writers only: TryGetPath takes no lock at all, which is why
    // WriteAsync publishes through a staging file rather than filling the final path in place.
    using var provider = BuildProvider();

    Assert.Same(
      provider.GetRequiredService<GvMediaCache>(),
      provider.GetRequiredService<GvMediaCache>());
  }

  [Fact]
  public void OptionsBindFromTheGvMediaSection()
  {
    var configuration = new ConfigurationBuilder()
      .AddInMemoryCollection(new Dictionary<string, string?>
      {
        ["GvMedia:Enabled"] = "true",
        ["GvMedia:CacheMaxMegabytes"] = "7",
        ["GvMedia:BaseUrl"] = "http://example.invalid:1234"
      })
      .Build();

    using var provider = BuildProvider(configuration);

    var options = provider.GetRequiredService<IOptionsMonitor<GvMediaOptions>>().CurrentValue;

    Assert.True(options.Enabled);
    Assert.Equal(7, options.CacheMaxMegabytes);
    Assert.Equal("http://example.invalid:1234", options.BaseUrl);
  }
}
