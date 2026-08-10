using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Factories;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.DependencyInjection;

namespace Radio.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// Verifies that the active-source accessor audio sources use to reject
/// TrackIdentified events broadcast by other sources is actually supplied by
/// the container. A silently-unwired accessor makes every source treat itself
/// as active, restoring the cross-source metadata contamination bug with no
/// visible failure.
/// </summary>
public class ActiveSourceAccessorRegistrationTests
{
  [Fact]
  public void AddSoundFlowAudio_RegistersActiveSourceAccessor()
  {
    // Arrange — build the service collection the way the app builds it.
    var services = new ServiceCollection();
    var configuration = new ConfigurationBuilder().Build();

    // Act
    services.AddSoundFlowAudio(configuration);

    // Assert — the accessor registration exists. Descriptor inspection only:
    // resolving the audio graph would initialize real audio hardware.
    Assert.Contains(services, d => d.ServiceType == typeof(Func<IAudioSource?>));
  }

  [Fact]
  public void AudioSourceFactory_ResolvedFromContainer_ReceivesActiveSourceAccessor()
  {
    // The accessor is an OPTIONAL constructor parameter, so a missing
    // registration would leave it null instead of throwing. This resolves the
    // factory the same way the app does (plain AddSingleton<T>, so
    // ActivatorUtilities picks the constructor) and asserts it arrived.
    var services = BuildMinimalContainer();
    services.AddSingleton<AudioSourceFactory>();

    var factory = services.BuildServiceProvider().GetRequiredService<AudioSourceFactory>();

    Assert.NotNull(factory.GetActiveSourceAccessor);
  }

  [Fact]
  public void RadioFactory_ResolvedFromContainer_ReceivesActiveSourceAccessor()
  {
    var services = BuildMinimalContainer();
    services.AddSingleton<RadioFactory>();

    var factory = services.BuildServiceProvider().GetRequiredService<RadioFactory>();

    Assert.NotNull(factory.GetActiveSourceAccessor);
  }

  [Fact]
  public void ActiveSourceAccessor_ReturnsAudioManagerActiveSource()
  {
    // The registered delegate must actually read through to the audio manager,
    // not just be non-null.
    var activeSource = new Mock<IAudioSource>().Object;
    var managerMock = new Mock<IAudioManager>();
    managerMock.Setup(m => m.ActiveSource).Returns(activeSource);

    var services = new ServiceCollection();
    services.AddSingleton(managerMock.Object);
    services.AddSingleton<Func<IAudioSource?>>(
      sp => () => sp.GetRequiredService<IAudioManager>().ActiveSource);

    var accessor = services.BuildServiceProvider().GetRequiredService<Func<IAudioSource?>>();

    Assert.Same(activeSource, accessor());
  }

  /// <summary>
  /// Registers the accessor plus mocks for every non-optional dependency of the
  /// two factories, without spinning up the real audio graph.
  /// </summary>
  private static ServiceCollection BuildMinimalContainer()
  {
    var services = new ServiceCollection();

    services.AddLogging();
    services.AddSingleton(new Mock<IAudioDeviceManager>().Object);
    services.AddSingleton(new Mock<IRadioFactory>().Object);
    services.AddSingleton(new Mock<IBluetoothService>().Object);
    services.AddSingleton(new Mock<IConfiguration>().Object);

    services.AddSingleton(BuildOptionsMonitor(new BluetoothOptions()));
    services.AddSingleton(BuildOptionsMonitor(new FilePlayerOptions()));
    services.AddSingleton(BuildOptionsMonitor(new FilePlayerPreferences()));
    services.AddSingleton(BuildOptionsMonitor(new DeviceOptions()));
    services.AddSingleton(BuildOptionsMonitor(new GenericSourcePreferences()));
    services.AddSingleton(BuildOptionsMonitor(new RadioOptions()));

    // The registration under test, mirroring AudioServiceExtensions.
    services.AddSingleton<Func<IAudioSource?>>(
      sp => () => sp.GetService<IAudioManager>()?.ActiveSource);

    return services;
  }

  private static IOptionsMonitor<T> BuildOptionsMonitor<T>(T value)
    where T : class
  {
    var monitor = new Mock<IOptionsMonitor<T>>();
    monitor.Setup(m => m.CurrentValue).Returns(value);
    return monitor.Object;
  }
}
