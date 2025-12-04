using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;

namespace Radio.Infrastructure.Tests.Audio.Services;

public class RadioPresetServiceTests
{
  private readonly Mock<ILogger<RadioPresetService>> _loggerMock;
  private readonly Mock<IRadioPresetRepository> _repositoryMock;

  public RadioPresetServiceTests()
  {
    _loggerMock = new Mock<ILogger<RadioPresetService>>();
    _repositoryMock = new Mock<IRadioPresetRepository>();
  }

  private RadioPresetService CreateService()
  {
    return new RadioPresetService(_loggerMock.Object, _repositoryMock.Object);
  }

  [Fact]
  public void Constructor_ThrowsOnNullLogger()
  {
    Assert.Throws<ArgumentNullException>(
      () => new RadioPresetService(null!, _repositoryMock.Object));
  }

  [Fact]
  public void Constructor_ThrowsOnNullRepository()
  {
    Assert.Throws<ArgumentNullException>(
      () => new RadioPresetService(_loggerMock.Object, null!));
  }

  [Fact]
  public void MaxPresets_Returns50()
  {
    var service = CreateService();
    Assert.Equal(50, service.MaxPresets);
  }

  [Fact]
  public async Task GetAllPresetsAsync_ReturnsAllPresets()
  {
    // Arrange
    var presets = new List<RadioPreset>
    {
      new() { Id = "1", Name = "FM - 101.5", Band = RadioBand.FM, Frequency = 101.5 },
      new() { Id = "2", Name = "AM - 1010", Band = RadioBand.AM, Frequency = 1010 }
    };

    _repositoryMock
      .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(presets);

    var service = CreateService();

    // Act
    var result = await service.GetAllPresetsAsync();

    // Assert
    Assert.Equal(2, result.Count);
    Assert.Contains(result, p => p.Id == "1");
    Assert.Contains(result, p => p.Id == "2");
    _repositoryMock.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task GetPresetByIdAsync_ReturnsPresetWhenFound()
  {
    // Arrange
    var preset = new RadioPreset
    {
      Id = "1",
      Name = "FM - 101.5",
      Band = RadioBand.FM,
      Frequency = 101.5
    };

    _repositoryMock
      .Setup(r => r.GetByIdAsync("1", It.IsAny<CancellationToken>()))
      .ReturnsAsync(preset);

    var service = CreateService();

    // Act
    var result = await service.GetPresetByIdAsync("1");

    // Assert
    Assert.NotNull(result);
    Assert.Equal("1", result.Id);
    Assert.Equal("FM - 101.5", result.Name);
  }

  [Fact]
  public async Task GetPresetByIdAsync_ReturnsNullWhenNotFound()
  {
    // Arrange
    _repositoryMock
      .Setup(r => r.GetByIdAsync("nonexistent", It.IsAny<CancellationToken>()))
      .ReturnsAsync((RadioPreset?)null);

    var service = CreateService();

    // Act
    var result = await service.GetPresetByIdAsync("nonexistent");

    // Assert
    Assert.Null(result);
  }

  [Fact]
  public async Task AddPresetAsync_ThrowsWhenMaxPresetsReached()
  {
    // Arrange
    var service = CreateService();

    _repositoryMock
      .Setup(r => r.GetCountAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(service.MaxPresets);

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
      () => service.AddPresetAsync(null, RadioBand.FM, 101.5));

    Assert.Contains($"Maximum of {service.MaxPresets} presets reached", exception.Message);
    _repositoryMock.Verify(r => r.AddAsync(It.IsAny<RadioPreset>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task AddPresetAsync_ThrowsWhenPresetAlreadyExists()
  {
    // Arrange
    var existingPreset = new RadioPreset
    {
      Id = "existing",
      Name = "FM - 101.5",
      Band = RadioBand.FM,
      Frequency = 101.5
    };

    _repositoryMock
      .Setup(r => r.GetCountAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(10);

    _repositoryMock
      .Setup(r => r.GetByBandAndFrequencyAsync(RadioBand.FM, 101.5, It.IsAny<CancellationToken>()))
      .ReturnsAsync(existingPreset);

    var service = CreateService();

    // Act & Assert
    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
      () => service.AddPresetAsync(null, RadioBand.FM, 101.5));

    Assert.Contains("already exists", exception.Message);
    _repositoryMock.Verify(r => r.AddAsync(It.IsAny<RadioPreset>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task AddPresetAsync_CreatesPresetWithDefaultName()
  {
    // Arrange
    _repositoryMock
      .Setup(r => r.GetCountAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(10);

    _repositoryMock
      .Setup(r => r.GetByBandAndFrequencyAsync(RadioBand.FM, 101.5, It.IsAny<CancellationToken>()))
      .ReturnsAsync((RadioPreset?)null);

    _repositoryMock
      .Setup(r => r.AddAsync(It.IsAny<RadioPreset>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    var service = CreateService();

    // Act
    var result = await service.AddPresetAsync(null, RadioBand.FM, 101.5);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("FM - 101.5", result.Name);
    Assert.Equal(RadioBand.FM, result.Band);
    Assert.Equal(101.5, result.Frequency);
    Assert.NotEmpty(result.Id);

    _repositoryMock.Verify(r => r.AddAsync(
      It.Is<RadioPreset>(p => p.Name == "FM - 101.5" && p.Band == RadioBand.FM && p.Frequency == 101.5),
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task AddPresetAsync_CreatesPresetWithCustomName()
  {
    // Arrange
    _repositoryMock
      .Setup(r => r.GetCountAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(10);

    _repositoryMock
      .Setup(r => r.GetByBandAndFrequencyAsync(RadioBand.FM, 101.5, It.IsAny<CancellationToken>()))
      .ReturnsAsync((RadioPreset?)null);

    _repositoryMock
      .Setup(r => r.AddAsync(It.IsAny<RadioPreset>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    var service = CreateService();

    // Act
    var result = await service.AddPresetAsync("My Favorite Station", RadioBand.FM, 101.5);

    // Assert
    Assert.NotNull(result);
    Assert.Equal("My Favorite Station", result.Name);
    Assert.Equal(RadioBand.FM, result.Band);
    Assert.Equal(101.5, result.Frequency);

    _repositoryMock.Verify(r => r.AddAsync(
      It.Is<RadioPreset>(p => p.Name == "My Favorite Station"),
      It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task AddPresetAsync_TrimsCustomName()
  {
    // Arrange
    _repositoryMock
      .Setup(r => r.GetCountAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(10);

    _repositoryMock
      .Setup(r => r.GetByBandAndFrequencyAsync(RadioBand.AM, 1010, It.IsAny<CancellationToken>()))
      .ReturnsAsync((RadioPreset?)null);

    _repositoryMock
      .Setup(r => r.AddAsync(It.IsAny<RadioPreset>(), It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    var service = CreateService();

    // Act
    var result = await service.AddPresetAsync("  Padded Name  ", RadioBand.AM, 1010);

    // Assert
    Assert.Equal("Padded Name", result.Name);
  }

  [Fact]
  public async Task DeletePresetAsync_ReturnsTrueWhenDeleted()
  {
    // Arrange
    _repositoryMock
      .Setup(r => r.DeleteAsync("1", It.IsAny<CancellationToken>()))
      .ReturnsAsync(true);

    var service = CreateService();

    // Act
    var result = await service.DeletePresetAsync("1");

    // Assert
    Assert.True(result);
    _repositoryMock.Verify(r => r.DeleteAsync("1", It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task DeletePresetAsync_ReturnsFalseWhenNotFound()
  {
    // Arrange
    _repositoryMock
      .Setup(r => r.DeleteAsync("nonexistent", It.IsAny<CancellationToken>()))
      .ReturnsAsync(false);

    var service = CreateService();

    // Act
    var result = await service.DeletePresetAsync("nonexistent");

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task PresetExistsAsync_ReturnsTrueWhenExists()
  {
    // Arrange
    var preset = new RadioPreset
    {
      Id = "1",
      Name = "FM - 101.5",
      Band = RadioBand.FM,
      Frequency = 101.5
    };

    _repositoryMock
      .Setup(r => r.GetByBandAndFrequencyAsync(RadioBand.FM, 101.5, It.IsAny<CancellationToken>()))
      .ReturnsAsync(preset);

    var service = CreateService();

    // Act
    var result = await service.PresetExistsAsync(RadioBand.FM, 101.5);

    // Assert
    Assert.True(result);
  }

  [Fact]
  public async Task PresetExistsAsync_ReturnsFalseWhenNotExists()
  {
    // Arrange
    _repositoryMock
      .Setup(r => r.GetByBandAndFrequencyAsync(RadioBand.FM, 101.5, It.IsAny<CancellationToken>()))
      .ReturnsAsync((RadioPreset?)null);

    var service = CreateService();

    // Act
    var result = await service.PresetExistsAsync(RadioBand.FM, 101.5);

    // Assert
    Assert.False(result);
  }

  [Fact]
  public async Task GetPresetCountAsync_ReturnsCount()
  {
    // Arrange
    _repositoryMock
      .Setup(r => r.GetCountAsync(It.IsAny<CancellationToken>()))
      .ReturnsAsync(25);

    var service = CreateService();

    // Act
    var result = await service.GetPresetCountAsync();

    // Assert
    Assert.Equal(25, result);
  }
}
