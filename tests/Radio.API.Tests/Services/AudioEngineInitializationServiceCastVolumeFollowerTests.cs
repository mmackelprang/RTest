using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Services;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Outputs;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.DependencyInjection;

namespace Radio.API.Tests.Services;

/// <summary>
/// AUD-81: the console-volume follower is a singleton nothing depends on, so it would never be
/// built lazily. The startup service constructs it — and must still start when it cannot.
/// </summary>
public class AudioEngineInitializationServiceCastVolumeFollowerTests
{
  [Fact]
  public void AddAudioOutputs_RegistersTheFollowerAsASingleton()
  {
    var services = new ServiceCollection();

    services.AddAudioOutputs(new ConfigurationBuilder().Build());

    var descriptor = Assert.Single(services, d => d.ServiceType == typeof(CastConsoleVolumeFollower));
    Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
  }

  [Fact]
  public void StartupService_ConstructsTheContainersFollower()
  {
    var options = new AudioOutputOptions();
    options.GoogleCast.CacheFilePath = Path.Combine(Path.GetTempPath(), $"cast-cache-{Guid.NewGuid():N}.json");

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton(new SoundFlowMasterMixer(NullLogger<SoundFlowMasterMixer>.Instance));
    services.AddSingleton(new Mock<IAudioEngine>().Object);
    services.AddSingleton(new GoogleCastOutput(NullLogger<GoogleCastOutput>.Instance, Options.Create(options)));
    services.AddSingleton<CastConsoleVolumeFollower>();
    using var provider = services.BuildServiceProvider();

    var service = CreateService(provider);

    Assert.NotNull(service.CastVolumeFollower);
    Assert.Same(provider.GetRequiredService<CastConsoleVolumeFollower>(), service.CastVolumeFollower);
  }

  [Fact]
  public void StartupService_StillStarts_WhenTheFollowerCannotBeBuilt()
  {
    var provider = new Mock<IServiceProvider>();
    provider.Setup(p => p.GetService(typeof(CastConsoleVolumeFollower)))
      .Throws(new InvalidOperationException("Unable to resolve SoundFlowMasterMixer"));

    var service = CreateService(provider.Object);

    Assert.Null(service.CastVolumeFollower);
  }

  private static AudioEngineInitializationService CreateService(IServiceProvider provider)
  {
    var preferences = new Mock<IOptionsMonitor<AudioPreferences>>();
    preferences.SetupGet(p => p.CurrentValue).Returns(new AudioPreferences());

    return new AudioEngineInitializationService(
      new Mock<ILogger<AudioEngineInitializationService>>().Object,
      new Mock<IAudioEngine>().Object,
      new Mock<IAudioDeviceManager>().Object,
      preferences.Object,
      new Mock<IMasterMixer>().Object,
      Options.Create(new BluetoothOptions { Enabled = false, EnableOnStartup = false }),
      Options.Create(new AudioOutputOptions()),
      provider);
  }
}
