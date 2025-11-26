using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Tests.Audio;

/// <summary>
/// Unit tests for the SoundFlowMasterMixer class.
/// </summary>
public class SoundFlowMasterMixerTests
{
  private readonly Mock<ILogger<SoundFlowMasterMixer>> _loggerMock;
  private readonly SoundFlowMasterMixer _mixer;

  public SoundFlowMasterMixerTests()
  {
    _loggerMock = new Mock<ILogger<SoundFlowMasterMixer>>();
    _mixer = new SoundFlowMasterMixer(_loggerMock.Object);
  }

  [Fact]
  public void MasterVolume_DefaultIs0_75()
  {
    // Assert
    Assert.Equal(0.75f, _mixer.MasterVolume);
  }

  [Fact]
  public void MasterVolume_CanBeSet()
  {
    // Act
    _mixer.MasterVolume = 0.5f;

    // Assert
    Assert.Equal(0.5f, _mixer.MasterVolume);
  }

  [Fact]
  public void MasterVolume_ClampsToZero()
  {
    // Act
    _mixer.MasterVolume = -0.5f;

    // Assert
    Assert.Equal(0f, _mixer.MasterVolume);
  }

  [Fact]
  public void MasterVolume_ClampsToOne()
  {
    // Act
    _mixer.MasterVolume = 1.5f;

    // Assert
    Assert.Equal(1f, _mixer.MasterVolume);
  }

  [Fact]
  public void Balance_DefaultIsZero()
  {
    // Assert
    Assert.Equal(0f, _mixer.Balance);
  }

  [Fact]
  public void Balance_CanBeSet()
  {
    // Act
    _mixer.Balance = 0.5f;

    // Assert
    Assert.Equal(0.5f, _mixer.Balance);
  }

  [Fact]
  public void Balance_ClampsToNegativeOne()
  {
    // Act
    _mixer.Balance = -1.5f;

    // Assert
    Assert.Equal(-1f, _mixer.Balance);
  }

  [Fact]
  public void Balance_ClampsToPositiveOne()
  {
    // Act
    _mixer.Balance = 1.5f;

    // Assert
    Assert.Equal(1f, _mixer.Balance);
  }

  [Fact]
  public void IsMuted_DefaultIsFalse()
  {
    // Assert
    Assert.False(_mixer.IsMuted);
  }

  [Fact]
  public void IsMuted_CanBeSet()
  {
    // Act
    _mixer.IsMuted = true;

    // Assert
    Assert.True(_mixer.IsMuted);
  }

  [Fact]
  public void AddSource_AddsSourceToMixer()
  {
    // Arrange
    var sourceMock = new Mock<IAudioSource>();
    sourceMock.Setup(s => s.Id).Returns("test-source");
    sourceMock.Setup(s => s.Name).Returns("Test Source");

    // Act
    _mixer.AddSource(sourceMock.Object);

    // Assert
    var sources = _mixer.GetActiveSources();
    Assert.Single(sources);
    Assert.Equal("test-source", sources[0].Id);
  }

  [Fact]
  public void AddSource_DoesNotAddDuplicates()
  {
    // Arrange
    var sourceMock = new Mock<IAudioSource>();
    sourceMock.Setup(s => s.Id).Returns("test-source");
    sourceMock.Setup(s => s.Name).Returns("Test Source");

    // Act
    _mixer.AddSource(sourceMock.Object);
    _mixer.AddSource(sourceMock.Object);

    // Assert
    var sources = _mixer.GetActiveSources();
    Assert.Single(sources);
  }

  [Fact]
  public void AddSource_ThrowsForNullSource()
  {
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() => _mixer.AddSource(null!));
  }

  [Fact]
  public void RemoveSource_RemovesSourceFromMixer()
  {
    // Arrange
    var sourceMock = new Mock<IAudioSource>();
    sourceMock.Setup(s => s.Id).Returns("test-source");
    sourceMock.Setup(s => s.Name).Returns("Test Source");
    _mixer.AddSource(sourceMock.Object);

    // Act
    _mixer.RemoveSource(sourceMock.Object);

    // Assert
    var sources = _mixer.GetActiveSources();
    Assert.Empty(sources);
  }

  [Fact]
  public void RemoveSource_DoesNotThrowForNonExistentSource()
  {
    // Arrange
    var sourceMock = new Mock<IAudioSource>();
    sourceMock.Setup(s => s.Id).Returns("test-source");

    // Act & Assert - should not throw
    _mixer.RemoveSource(sourceMock.Object);
    Assert.Empty(_mixer.GetActiveSources());
  }

  [Fact]
  public void RemoveSource_ThrowsForNullSource()
  {
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() => _mixer.RemoveSource(null!));
  }

  [Fact]
  public void GetActiveSources_ReturnsEmptyListInitially()
  {
    // Act
    var sources = _mixer.GetActiveSources();

    // Assert
    Assert.NotNull(sources);
    Assert.Empty(sources);
  }

  [Fact]
  public void GetActiveSources_ReturnsAllAddedSources()
  {
    // Arrange
    var source1 = new Mock<IAudioSource>();
    source1.Setup(s => s.Id).Returns("source-1");
    source1.Setup(s => s.Name).Returns("Source 1");

    var source2 = new Mock<IAudioSource>();
    source2.Setup(s => s.Id).Returns("source-2");
    source2.Setup(s => s.Name).Returns("Source 2");

    // Act
    _mixer.AddSource(source1.Object);
    _mixer.AddSource(source2.Object);

    // Assert
    var sources = _mixer.GetActiveSources();
    Assert.Equal(2, sources.Count);
  }

  [Fact]
  public void ClearSources_RemovesAllSources()
  {
    // Arrange
    var source1 = new Mock<IAudioSource>();
    source1.Setup(s => s.Id).Returns("source-1");
    source1.Setup(s => s.Name).Returns("Source 1");

    var source2 = new Mock<IAudioSource>();
    source2.Setup(s => s.Id).Returns("source-2");
    source2.Setup(s => s.Name).Returns("Source 2");

    _mixer.AddSource(source1.Object);
    _mixer.AddSource(source2.Object);

    // Act
    _mixer.ClearSources();

    // Assert
    Assert.Empty(_mixer.GetActiveSources());
  }

  [Fact]
  public void GetEffectiveVolume_ReturnsMasterVolumeWhenNotMuted()
  {
    // Arrange
    _mixer.MasterVolume = 0.5f;
    _mixer.IsMuted = false;

    // Act
    var effectiveVolume = _mixer.GetEffectiveVolume();

    // Assert
    Assert.Equal(0.5f, effectiveVolume);
  }

  [Fact]
  public void GetEffectiveVolume_ReturnsZeroWhenMuted()
  {
    // Arrange
    _mixer.MasterVolume = 0.5f;
    _mixer.IsMuted = true;

    // Act
    var effectiveVolume = _mixer.GetEffectiveVolume();

    // Assert
    Assert.Equal(0f, effectiveVolume);
  }

  [Fact]
  public void GetLeftChannelGain_ReturnsOneWhenBalanceIsCenter()
  {
    // Arrange
    _mixer.Balance = 0f;

    // Act
    var leftGain = _mixer.GetLeftChannelGain();

    // Assert
    Assert.Equal(1f, leftGain);
  }

  [Fact]
  public void GetLeftChannelGain_ReducesWhenBalanceIsRight()
  {
    // Arrange
    _mixer.Balance = 0.5f;

    // Act
    var leftGain = _mixer.GetLeftChannelGain();

    // Assert
    Assert.Equal(0.5f, leftGain);
  }

  [Fact]
  public void GetLeftChannelGain_StaysFullWhenBalanceIsLeft()
  {
    // Arrange
    _mixer.Balance = -0.5f;

    // Act
    var leftGain = _mixer.GetLeftChannelGain();

    // Assert
    Assert.Equal(1f, leftGain);
  }

  [Fact]
  public void GetRightChannelGain_ReturnsOneWhenBalanceIsCenter()
  {
    // Arrange
    _mixer.Balance = 0f;

    // Act
    var rightGain = _mixer.GetRightChannelGain();

    // Assert
    Assert.Equal(1f, rightGain);
  }

  [Fact]
  public void GetRightChannelGain_ReducesWhenBalanceIsLeft()
  {
    // Arrange
    _mixer.Balance = -0.5f;

    // Act
    var rightGain = _mixer.GetRightChannelGain();

    // Assert
    Assert.Equal(0.5f, rightGain);
  }

  [Fact]
  public void GetRightChannelGain_StaysFullWhenBalanceIsRight()
  {
    // Arrange
    _mixer.Balance = 0.5f;

    // Act
    var rightGain = _mixer.GetRightChannelGain();

    // Assert
    Assert.Equal(1f, rightGain);
  }
}
