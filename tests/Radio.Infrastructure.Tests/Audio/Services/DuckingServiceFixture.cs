using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// The single construction fixture for <see cref="DuckingService"/> tests.
///
/// Shared by <see cref="DuckingServiceTests"/> and
/// <see cref="DuckingServiceCharacterizationTests"/> deliberately: two copies of a ducking
/// fixture is how the two get to disagree about what "today's behaviour" is, which is exactly
/// the thing the characterization tests exist to hold still.
/// </summary>
internal sealed class DuckingServiceFixture
{
  public Mock<ILogger<DuckingService>> LoggerMock { get; } = new();

  public Mock<IOptionsMonitor<AudioOptions>> OptionsMock { get; } = new();

  public Mock<IMasterMixer> MixerMock { get; } = new();

  /// <summary>
  /// The live options instance behind <see cref="OptionsMock"/>. Tests mutate this in place —
  /// the monitor returns this same object every time, so a change takes effect on the next read.
  /// </summary>
  public AudioOptions Options { get; }

  public DuckingServiceFixture()
  {
    Options = new AudioOptions
    {
      DuckingPercentage = 20,
      DuckingPolicy = DuckingPolicy.FadeSmooth,
      DuckingAttackMs = 100,
      DuckingReleaseMs = 500
    };

    OptionsMock.Setup(x => x.CurrentValue).Returns(Options);
  }

  public DuckingService CreateService() =>
    new(LoggerMock.Object, OptionsMock.Object, MixerMock.Object);

  public Mock<IEventAudioSource> CreateMockEventSource(string? id = null)
  {
    var mock = new Mock<IEventAudioSource>();
    mock.Setup(x => x.Id).Returns(id ?? Guid.NewGuid().ToString("N"));
    mock.Setup(x => x.Category).Returns(AudioSourceCategory.Event);
    mock.Setup(x => x.Type).Returns(AudioSourceType.TTS);
    mock.Setup(x => x.Duration).Returns(TimeSpan.FromSeconds(2));
    return mock;
  }

  /// <summary>Convenience for callers that only need the source, not the mock behind it.</summary>
  public IEventAudioSource CreateEventSource(string? id = null) =>
    CreateMockEventSource(id).Object;

  public Mock<IAudioSource> CreateMockPrimarySource(string? id = null)
  {
    var mock = new Mock<IAudioSource>();
    mock.Setup(x => x.Id).Returns(id ?? Guid.NewGuid().ToString("N"));
    mock.Setup(x => x.Category).Returns(AudioSourceCategory.Primary);
    mock.Setup(x => x.Type).Returns(AudioSourceType.Radio);
    return mock;
  }
}
