using Microsoft.Extensions.Logging;
using Moq;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Fingerprinting;

namespace Radio.Infrastructure.Tests.Audio.Fingerprinting;

/// <summary>
/// Unit tests for the MockFingerprintService class.
/// </summary>
public class MockFingerprintServiceTests
{
  private readonly Mock<ILogger<MockFingerprintService>> _loggerMock;
  private readonly MockFingerprintService _service;

  public MockFingerprintServiceTests()
  {
    _loggerMock = new Mock<ILogger<MockFingerprintService>>();
    _service = new MockFingerprintService(_loggerMock.Object);
  }

  [Fact]
  public async Task GenerateFingerprintAsync_WithValidSamples_ReturnsFingerprint()
  {
    // Arrange
    var samples = CreateTestSamples(5.0);

    // Act
    var result = await _service.GenerateFingerprintAsync(samples);

    // Assert
    Assert.NotNull(result);
    Assert.NotEmpty(result.Id);
    Assert.NotEmpty(result.ChromaprintHash);
    Assert.Equal((int)samples.Duration.TotalSeconds, result.DurationSeconds);
    Assert.Equal(samples.SourceName, result.SourcePath);
  }

  [Fact]
  public async Task GenerateFingerprintAsync_WithDifferentSamples_ReturnsDifferentHashes()
  {
    // Arrange
    var samples1 = CreateTestSamples(5.0, frequency: 440);
    var samples2 = CreateTestSamples(5.0, frequency: 880);

    // Act
    var result1 = await _service.GenerateFingerprintAsync(samples1);
    var result2 = await _service.GenerateFingerprintAsync(samples2);

    // Assert
    Assert.NotEqual(result1.ChromaprintHash, result2.ChromaprintHash);
  }

  [Fact]
  public async Task GenerateFingerprintAsync_WithNullSamples_ThrowsArgumentNullException()
  {
    // Act & Assert
    await Assert.ThrowsAsync<ArgumentNullException>(
      () => _service.GenerateFingerprintAsync(null!));
  }

  [Fact]
  public async Task GenerateFingerprintAsync_SetsCorrectGeneratedAt()
  {
    // Arrange
    var samples = CreateTestSamples(5.0);
    var before = DateTime.UtcNow;

    // Act
    var result = await _service.GenerateFingerprintAsync(samples);

    // Assert
    var after = DateTime.UtcNow;
    Assert.InRange(result.GeneratedAt, before, after);
  }

  [Fact]
  public async Task GenerateFingerprintFromFileAsync_WithNonExistentFile_ThrowsFileNotFoundException()
  {
    // Act & Assert
    await Assert.ThrowsAsync<FileNotFoundException>(
      () => _service.GenerateFingerprintFromFileAsync("/nonexistent/file.wav"));
  }

  [Fact]
  public async Task GenerateFingerprintFromFileAsync_WithNullPath_ThrowsArgumentException()
  {
    // Act & Assert
    await Assert.ThrowsAsync<ArgumentNullException>(
      () => _service.GenerateFingerprintFromFileAsync(null!));
  }

  [Fact]
  public async Task GenerateFingerprintFromFileAsync_WithEmptyPath_ThrowsArgumentException()
  {
    // Act & Assert
    await Assert.ThrowsAsync<ArgumentException>(
      () => _service.GenerateFingerprintFromFileAsync(""));
  }

  private static AudioSampleBuffer CreateTestSamples(
    double durationSeconds,
    int sampleRate = 44100,
    int channels = 2,
    double frequency = 440.0)
  {
    var sampleCount = (int)(durationSeconds * sampleRate * channels);
    var samples = new float[sampleCount];

    for (int i = 0; i < sampleCount; i++)
    {
      var t = (double)i / channels / sampleRate;
      samples[i] = (float)Math.Sin(2 * Math.PI * frequency * t);
    }

    return new AudioSampleBuffer
    {
      Samples = samples,
      SampleRate = sampleRate,
      Channels = channels,
      Duration = TimeSpan.FromSeconds(durationSeconds),
      SourceName = "Test Source"
    };
  }
}
