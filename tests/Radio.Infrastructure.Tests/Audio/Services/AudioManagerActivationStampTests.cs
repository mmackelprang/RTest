using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// AUD-34: <see cref="AudioManager.SwitchSourceAsync"/> stamps a source's activation, so a fingerprint
/// capture begun on the previous source is dropped even when the new source's own track identity
/// (AVRCP key, file path) did not change. The per-source drop is pinned in the BT and FilePlayer tests.
/// </summary>
public class AudioManagerActivationStampTests : IAsyncDisposable
{
  private readonly Mock<IAudioEngine> _engine = new();
  private readonly Mock<IMasterMixer> _mixer = new();
  private readonly AudioManager _sut;

  public AudioManagerActivationStampTests()
  {
    _engine.Setup(e => e.GetMasterMixer()).Returns(_mixer.Object);
    _engine.Setup(e => e.IsReady).Returns(true);
    _mixer.Setup(m => m.GetActiveSources()).Returns(Array.Empty<IAudioSource>());
    _sut = new AudioManager(
      Mock.Of<ILogger<AudioManager>>(), _engine.Object, Mock.Of<IAudioSourceFactory>());
  }

  public async ValueTask DisposeAsync()
  {
    await _sut.DisposeAsync();
    GC.SuppressFinalize(this);
  }

  private static Mock<PrimaryAudioSourceBase> CreateSource(string id, AudioSourceType type)
  {
    Mock<PrimaryAudioSourceBase> source = new(Mock.Of<ILogger>(), null!, null!) { CallBase = true };
    source.SetupGet(s => s.Name).Returns(id);
    source.SetupGet(s => s.Type).Returns(type);
    return source;
  }

  [Fact]
  public async Task SwitchingToASource_StampsItsActivation()
  {
    Mock<PrimaryAudioSourceBase> bt = CreateSource("bt", AudioSourceType.Bluetooth);
    Assert.Null(bt.Object.LatestTrackBoundaryUtc(0));
    DateTime before = DateTime.UtcNow;

    await _sut.SwitchSourceAsync(bt.Object);

    DateTime? boundary = bt.Object.LatestTrackBoundaryUtc(0);
    Assert.NotNull(boundary);
    Assert.True(boundary >= before);
  }

  [Fact]
  public async Task SwitchingAwayAndBack_RestampsTheReturningSource()
  {
    Mock<PrimaryAudioSourceBase> file = CreateSource("file", AudioSourceType.FilePlayer);
    Mock<PrimaryAudioSourceBase> bt = CreateSource("bt", AudioSourceType.Bluetooth);
    await _sut.SwitchSourceAsync(file.Object);
    // Pin the first stamp in the past explicitly rather than waiting for the clock to move.
    DateTime firstActivation = DateTime.UtcNow.AddMinutes(-1);
    file.Object.MarkActivated(firstActivation);

    await _sut.SwitchSourceAsync(bt.Object);
    await _sut.SwitchSourceAsync(file.Object);

    Assert.True(file.Object.LatestTrackBoundaryUtc(0) > firstActivation);
  }

  [Fact]
  public async Task ReselectingTheActiveSource_IsNotAnActivation()
  {
    Mock<PrimaryAudioSourceBase> bt = CreateSource("bt", AudioSourceType.Bluetooth);
    await _sut.SwitchSourceAsync(bt.Object);
    DateTime pinned = DateTime.UtcNow.AddMinutes(-1);
    bt.Object.MarkActivated(pinned);

    await _sut.SwitchSourceAsync(bt.Object);

    Assert.Equal(pinned, bt.Object.LatestTrackBoundaryUtc(0));
  }
}
