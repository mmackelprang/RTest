using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Events;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Services;

public class AudioManagerTests : IAsyncDisposable
{
  private readonly Mock<ILogger<AudioManager>> _loggerMock;
  private readonly Mock<IAudioEngine> _engineMock;
  private readonly Mock<IAudioSourceFactory> _sourceFactoryMock;
  private readonly Mock<IMasterMixer> _mixerMock;
  private readonly AudioManager _sut;

  public AudioManagerTests()
  {
    _loggerMock = new Mock<ILogger<AudioManager>>();
    _engineMock = new Mock<IAudioEngine>();
    _sourceFactoryMock = new Mock<IAudioSourceFactory>();
    _mixerMock = new Mock<IMasterMixer>();

    _engineMock.Setup(e => e.GetMasterMixer()).Returns(_mixerMock.Object);
    _engineMock.Setup(e => e.IsReady).Returns(true);
    _mixerMock.Setup(m => m.GetActiveSources()).Returns(Array.Empty<IAudioSource>());

    _sut = new AudioManager(
      _loggerMock.Object,
      _engineMock.Object,
      _sourceFactoryMock.Object);
  }

  public async ValueTask DisposeAsync()
  {
    await _sut.DisposeAsync();
    GC.SuppressFinalize(this);
  }

  // --- Source Management Tests ---

  [Fact]
  public void ActiveSource_Initially_IsNull()
  {
    Assert.Null(_sut.ActiveSource);
  }

  [Fact]
  public async Task SwitchSourceAsync_SetsActiveSource()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");

    // Act
    await _sut.SwitchSourceAsync(source.Object);

    // Assert
    Assert.Equal(source.Object, _sut.ActiveSource);
  }

  [Fact]
  public async Task SwitchSourceAsync_StopsOldSource()
  {
    // Arrange
    var oldSource = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    var newSource = CreateMockPrimarySource(AudioSourceType.Bluetooth, "BT");

    _mixerMock.Setup(m => m.GetActiveSources())
      .Returns(new[] { oldSource.Object });

    await _sut.SwitchSourceAsync(oldSource.Object);

    // Act
    await _sut.SwitchSourceAsync(newSource.Object);

    // Assert
    oldSource.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    Assert.Equal(newSource.Object, _sut.ActiveSource);
  }

  [Fact]
  public async Task SwitchSourceAsync_AddsNewSourceToMixer()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");

    // Act
    await _sut.SwitchSourceAsync(source.Object);

    // Assert
    _mixerMock.Verify(m => m.AddSource(source.Object), Times.Once);
  }

  [Fact]
  public async Task SwitchSourceAsync_AutoPlaysRadioSource()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    source.Setup(s => s.State).Returns(AudioSourceState.Ready);

    // Act
    await _sut.SwitchSourceAsync(source.Object);

    // Assert
    source.Verify(s => s.PlayAsync(It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task SwitchSourceAsync_DoesNotAutoPlayFilePlayer()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.FilePlayer, "File Player");
    source.Setup(s => s.State).Returns(AudioSourceState.Ready);

    // Act
    await _sut.SwitchSourceAsync(source.Object);

    // Assert
    source.Verify(s => s.PlayAsync(It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task SwitchSourceAsync_ThrowsForNonPrimarySource()
  {
    // Arrange
    var source = new Mock<IAudioSource>();
    source.Setup(s => s.Category).Returns(AudioSourceCategory.Event);

    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(
      () => _sut.SwitchSourceAsync(source.Object));
  }

  [Fact]
  public async Task GetOrCreateSourceAsync_CachesSource()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    _sourceFactoryMock.Setup(f => f.CreateSource(AudioSourceType.Radio))
      .Returns(source.Object);

    // Act
    var result1 = await _sut.GetOrCreateSourceAsync(AudioSourceType.Radio);
    var result2 = await _sut.GetOrCreateSourceAsync(AudioSourceType.Radio);

    // Assert
    Assert.Same(result1, result2);
    _sourceFactoryMock.Verify(f => f.CreateSource(AudioSourceType.Radio), Times.Once);
  }

  [Fact]
  public async Task GetOrCreateSourceAsync_ReturnsNullForUnsupportedType()
  {
    // Arrange
    _sourceFactoryMock.Setup(f => f.CreateSource(It.IsAny<AudioSourceType>()))
      .Throws<ArgumentOutOfRangeException>();

    // Act
    var result = await _sut.GetOrCreateSourceAsync(AudioSourceType.TestTone, switchToSource: false);

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task GetOrCreateSourceAsync_SwitchesToSourceByDefault()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    _sourceFactoryMock.Setup(f => f.CreateSource(AudioSourceType.Radio))
      .Returns(source.Object);

    // Act
    await _sut.GetOrCreateSourceAsync(AudioSourceType.Radio);

    // Assert
    Assert.Equal(source.Object, _sut.ActiveSource);
  }

  [Fact]
  public async Task GetOrCreateSourceAsync_WithSwitchFalse_DoesNotSwitch()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    _sourceFactoryMock.Setup(f => f.CreateSource(AudioSourceType.Radio))
      .Returns(source.Object);

    // Act
    await _sut.GetOrCreateSourceAsync(AudioSourceType.Radio, switchToSource: false);

    // Assert — active source should still be null since we didn't switch
    Assert.Null(_sut.ActiveSource);
  }

  [Fact]
  public void GetCachedSource_ReturnsNullIfNotCreated()
  {
    Assert.Null(_sut.GetCachedSource(AudioSourceType.Radio));
  }

  [Fact]
  public async Task GetCachedSource_ReturnsCachedSourceAfterCreation()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    _sourceFactoryMock.Setup(f => f.CreateSource(AudioSourceType.Radio))
      .Returns(source.Object);

    await _sut.GetOrCreateSourceAsync(AudioSourceType.Radio, switchToSource: false);

    // Act
    var cached = _sut.GetCachedSource(AudioSourceType.Radio);

    // Assert
    Assert.Same(source.Object, cached);
  }

  // --- Volume & Mixer Tests ---

  [Fact]
  public void MasterVolume_DelegatesToMixer()
  {
    // Arrange
    _mixerMock.SetupProperty(m => m.MasterVolume, 0.5f);

    // Act
    _sut.MasterVolume = 0.75f;

    // Assert
    _mixerMock.VerifySet(m => m.MasterVolume = 0.75f, Times.Once);
  }

  [Fact]
  public void MasterVolume_ReadsFromMixer()
  {
    _mixerMock.Setup(m => m.MasterVolume).Returns(0.42f);

    Assert.Equal(0.42f, _sut.MasterVolume);
  }

  [Fact]
  public void IsMuted_DelegatesToMixer()
  {
    _mixerMock.SetupProperty(m => m.IsMuted, false);

    _sut.IsMuted = true;

    _mixerMock.VerifySet(m => m.IsMuted = true, Times.Once);
  }

  [Fact]
  public void Balance_DelegatesToMixer()
  {
    _mixerMock.SetupProperty(m => m.Balance, 0.0f);

    _sut.Balance = -0.5f;

    _mixerMock.VerifySet(m => m.Balance = -0.5f, Times.Once);
  }

  // --- Gain Tests ---

  [Fact]
  public void GetSourceGain_ReturnsDefaultWhenNoPersistence()
  {
    // Without persistence service, should return 1.0 default
    Assert.Equal(1.0f, _sut.GetSourceGain(AudioSourceType.Radio));
  }

  [Fact]
  public void GetAllSourceGains_ReturnsEmptyWhenNoPersistence()
  {
    Assert.Empty(_sut.GetAllSourceGains());
  }

  // --- Lifecycle Tests ---

  [Fact]
  public async Task InitializeAsync_InitializesEngine()
  {
    // Arrange
    _engineMock.Setup(e => e.IsReady).Returns(false);

    // Act
    await _sut.InitializeAsync();

    // Assert
    _engineMock.Verify(e => e.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task InitializeAsync_SkipsIfAlreadyReady()
  {
    // Arrange
    _engineMock.Setup(e => e.IsReady).Returns(true);

    // Act
    await _sut.InitializeAsync();

    // Assert
    _engineMock.Verify(e => e.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task InitializeAsync_OnlyRunsOnce()
  {
    // Act
    await _sut.InitializeAsync();
    await _sut.InitializeAsync();

    // Assert — should only try to initialize once
    _engineMock.Verify(e => e.InitializeAsync(It.IsAny<CancellationToken>()), Times.AtMostOnce);
  }

  [Fact]
  public async Task StopAsync_StopsActiveSource()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    await _sut.SwitchSourceAsync(source.Object);

    // Act
    await _sut.StopAsync();

    // Assert
    source.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
  }

  [Fact]
  public async Task StopAsync_NoopWhenNoActiveSource()
  {
    // Should not throw
    await _sut.StopAsync();
  }

  [Fact]
  public async Task DisposeAsync_DisposesSourceCache()
  {
    // Arrange
    var source = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    _sourceFactoryMock.Setup(f => f.CreateSource(AudioSourceType.Radio))
      .Returns(source.Object);

    await _sut.GetOrCreateSourceAsync(AudioSourceType.Radio, switchToSource: false);

    // Act
    await _sut.DisposeAsync();

    // Assert
    source.Verify(s => s.DisposeAsync(), Times.Once);
  }

  // --- Concurrent Source Switching ---

  [Fact]
  public async Task SwitchSourceAsync_ConcurrentCalls_SerializedCorrectly()
  {
    // Arrange
    var source1 = CreateMockPrimarySource(AudioSourceType.Radio, "FM Radio");
    var source2 = CreateMockPrimarySource(AudioSourceType.Bluetooth, "BT");

    // Act — switch concurrently
    var t1 = _sut.SwitchSourceAsync(source1.Object);
    var t2 = _sut.SwitchSourceAsync(source2.Object);

    await Task.WhenAll(t1, t2);

    // Assert — one of them should be the final active source
    Assert.NotNull(_sut.ActiveSource);
  }

  // --- Helper Methods ---

  private static Mock<IPrimaryAudioSource> CreateMockPrimarySource(
    AudioSourceType type, string name)
  {
    var source = new Mock<IPrimaryAudioSource>();
    source.Setup(s => s.Type).Returns(type);
    source.Setup(s => s.Name).Returns(name);
    source.Setup(s => s.Id).Returns(Guid.NewGuid().ToString());
    source.Setup(s => s.Category).Returns(AudioSourceCategory.Primary);
    source.Setup(s => s.State).Returns(AudioSourceState.Ready);
    source.As<IAsyncDisposable>()
      .Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);
    source.As<IAudioSource>()
      .Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);

    // Set up StateChanged event
    source.SetupAdd(s => s.StateChanged += It.IsAny<EventHandler<AudioSourceStateChangedEventArgs>>());
    source.SetupRemove(s => s.StateChanged -= It.IsAny<EventHandler<AudioSourceStateChangedEventArgs>>());

    return source;
  }
}
