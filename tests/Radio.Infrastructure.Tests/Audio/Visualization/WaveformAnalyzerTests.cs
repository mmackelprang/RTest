using Radio.Infrastructure.Audio.Visualization;

namespace Radio.Infrastructure.Tests.Audio.Visualization;

public class WaveformAnalyzerTests
{
  private const int DefaultSampleCount = 512;
  private const int DefaultSampleRate = 48000;

  private static WaveformAnalyzer CreateAnalyzer(
    int sampleCount = DefaultSampleCount,
    int sampleRate = DefaultSampleRate)
  {
    return new WaveformAnalyzer(sampleCount, sampleRate);
  }

  [Fact]
  public void Constructor_WithValidParameters_Succeeds()
  {
    // Act
    var analyzer = CreateAnalyzer();

    // Assert
    Assert.NotNull(analyzer);
    Assert.Equal(DefaultSampleCount, analyzer.SampleCount);
    Assert.Equal(DefaultSampleRate, analyzer.SampleRate);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  [InlineData(-512)]
  public void Constructor_WithInvalidSampleCount_ThrowsArgumentOutOfRangeException(int sampleCount)
  {
    // Act & Assert
    Assert.Throws<ArgumentOutOfRangeException>(
      () => new WaveformAnalyzer(sampleCount, DefaultSampleRate));
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  [InlineData(-48000)]
  public void Constructor_WithInvalidSampleRate_ThrowsArgumentOutOfRangeException(int sampleRate)
  {
    // Act & Assert
    Assert.Throws<ArgumentOutOfRangeException>(
      () => new WaveformAnalyzer(DefaultSampleCount, sampleRate));
  }

  [Fact]
  public void Duration_CalculatedCorrectly()
  {
    // Arrange
    var analyzer = CreateAnalyzer(sampleCount: 48000, sampleRate: 48000);

    // Assert - 48000 samples at 48000 Hz = 1 second
    Assert.Equal(TimeSpan.FromSeconds(1), analyzer.Duration);
  }

  [Fact]
  public void AddSamples_Array_AcceptsSamples()
  {
    // Arrange
    var analyzer = CreateAnalyzer();
    var samples = GenerateStereoSamples(DefaultSampleCount);

    // Act - should not throw
    analyzer.AddSamples(samples);

    // Assert
    var (left, right) = analyzer.GetSamples();
    Assert.Equal(DefaultSampleCount, left.Length);
    Assert.Equal(DefaultSampleCount, right.Length);
  }

  [Fact]
  public void AddSamples_Span_AcceptsSamples()
  {
    // Arrange
    var analyzer = CreateAnalyzer();
    var samples = GenerateStereoSamples(DefaultSampleCount);

    // Act - should not throw
    analyzer.AddSamples(samples.AsSpan(), samples.Length);

    // Assert
    var (left, right) = analyzer.GetSamples();
    Assert.Equal(DefaultSampleCount, left.Length);
    Assert.Equal(DefaultSampleCount, right.Length);
  }

  [Fact]
  public void GetSamples_WithNoInput_ReturnsSilence()
  {
    // Arrange
    var analyzer = CreateAnalyzer();

    // Act
    var (left, right) = analyzer.GetSamples();

    // Assert
    Assert.All(left, s => Assert.Equal(0f, s));
    Assert.All(right, s => Assert.Equal(0f, s));
  }

  [Fact]
  public void GetSamples_ReturnsSeparateChannels()
  {
    // Arrange
    var analyzer = CreateAnalyzer(sampleCount: 4);
    // Create interleaved stereo: L0, R0, L1, R1, L2, R2, L3, R3
    var samples = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f };

    // Act
    analyzer.AddSamples(samples);
    var (left, right) = analyzer.GetSamples();

    // Assert - newest samples should be at the end
    Assert.Equal(0.1f, left[0], 0.001f);
    Assert.Equal(0.3f, left[1], 0.001f);
    Assert.Equal(0.5f, left[2], 0.001f);
    Assert.Equal(0.7f, left[3], 0.001f);

    Assert.Equal(0.2f, right[0], 0.001f);
    Assert.Equal(0.4f, right[1], 0.001f);
    Assert.Equal(0.6f, right[2], 0.001f);
    Assert.Equal(0.8f, right[3], 0.001f);
  }

  [Fact]
  public void GetLeftSamples_ReturnsOnlyLeftChannel()
  {
    // Arrange
    var analyzer = CreateAnalyzer(sampleCount: 4);
    var samples = new float[] { 0.1f, 0.9f, 0.2f, 0.8f, 0.3f, 0.7f, 0.4f, 0.6f };

    // Act
    analyzer.AddSamples(samples);
    var left = analyzer.GetLeftSamples();

    // Assert
    Assert.Equal(4, left.Length);
    Assert.Equal(0.1f, left[0], 0.001f);
    Assert.Equal(0.2f, left[1], 0.001f);
    Assert.Equal(0.3f, left[2], 0.001f);
    Assert.Equal(0.4f, left[3], 0.001f);
  }

  [Fact]
  public void GetRightSamples_ReturnsOnlyRightChannel()
  {
    // Arrange
    var analyzer = CreateAnalyzer(sampleCount: 4);
    var samples = new float[] { 0.1f, 0.9f, 0.2f, 0.8f, 0.3f, 0.7f, 0.4f, 0.6f };

    // Act
    analyzer.AddSamples(samples);
    var right = analyzer.GetRightSamples();

    // Assert
    Assert.Equal(4, right.Length);
    Assert.Equal(0.9f, right[0], 0.001f);
    Assert.Equal(0.8f, right[1], 0.001f);
    Assert.Equal(0.7f, right[2], 0.001f);
    Assert.Equal(0.6f, right[3], 0.001f);
  }

  [Fact]
  public void AddSamples_WhenOverflows_OverwritesOldestSamples()
  {
    // Arrange
    var analyzer = CreateAnalyzer(sampleCount: 4);
    var firstBatch = new float[] { 0.1f, 0.1f, 0.2f, 0.2f, 0.3f, 0.3f, 0.4f, 0.4f }; // 4 pairs
    var secondBatch = new float[] { 0.5f, 0.5f, 0.6f, 0.6f }; // 2 more pairs

    // Act
    analyzer.AddSamples(firstBatch);
    analyzer.AddSamples(secondBatch);
    var left = analyzer.GetLeftSamples();

    // Assert - oldest 2 samples should be overwritten
    // After second batch: [0.5, 0.6, 0.3, 0.4] -> ordered: [0.3, 0.4, 0.5, 0.6]
    Assert.Equal(0.3f, left[0], 0.001f);
    Assert.Equal(0.4f, left[1], 0.001f);
    Assert.Equal(0.5f, left[2], 0.001f);
    Assert.Equal(0.6f, left[3], 0.001f);
  }

  [Fact]
  public void GetDownsampledSamples_WithTargetEqualToSampleCount_ReturnsSameData()
  {
    // Arrange
    var analyzer = CreateAnalyzer(sampleCount: 8);
    var samples = GenerateStereoSamples(8);
    analyzer.AddSamples(samples);

    // Act
    var (left, right) = analyzer.GetDownsampledSamples(8);

    // Assert
    Assert.Equal(8, left.Length);
    Assert.Equal(8, right.Length);
  }

  [Fact]
  public void GetDownsampledSamples_WithSmallerTarget_ReturnsDownsampledData()
  {
    // Arrange
    var analyzer = CreateAnalyzer(sampleCount: 16);
    var samples = GenerateStereoSamples(16);
    analyzer.AddSamples(samples);

    // Act
    var (left, right) = analyzer.GetDownsampledSamples(4);

    // Assert
    Assert.Equal(4, left.Length);
    Assert.Equal(4, right.Length);
  }

  [Fact]
  public void GetDownsampledSamples_WithLargerTarget_ReturnsOriginalData()
  {
    // Arrange
    var analyzer = CreateAnalyzer(sampleCount: 8);
    var samples = GenerateStereoSamples(8);
    analyzer.AddSamples(samples);

    // Act
    var (left, right) = analyzer.GetDownsampledSamples(16);

    // Assert - should return original size, not upsampled
    Assert.Equal(8, left.Length);
    Assert.Equal(8, right.Length);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void GetDownsampledSamples_WithInvalidTarget_ThrowsArgumentOutOfRangeException(int targetCount)
  {
    // Arrange
    var analyzer = CreateAnalyzer();

    // Act & Assert
    Assert.Throws<ArgumentOutOfRangeException>(
      () => analyzer.GetDownsampledSamples(targetCount));
  }

  [Fact]
  public void Reset_ClearsSamples()
  {
    // Arrange
    var analyzer = CreateAnalyzer();
    var samples = GenerateStereoSamples(DefaultSampleCount);
    analyzer.AddSamples(samples);

    // Verify there's data
    var (leftBefore, _) = analyzer.GetSamples();
    Assert.Contains(leftBefore, s => s != 0f);

    // Act
    analyzer.Reset();

    // Assert
    var (leftAfter, rightAfter) = analyzer.GetSamples();
    Assert.All(leftAfter, s => Assert.Equal(0f, s));
    Assert.All(rightAfter, s => Assert.Equal(0f, s));
  }

  [Fact]
  public void AddSamples_WithOddCount_HandlesGracefully()
  {
    // Arrange
    var analyzer = CreateAnalyzer(sampleCount: 4);
    var samples = new float[] { 0.1f, 0.2f, 0.3f }; // Odd count

    // Act - should not throw
    analyzer.AddSamples(samples);

    // Assert
    var (left, right) = analyzer.GetSamples();
    Assert.Equal(4, left.Length);
  }

  /// <summary>
  /// Generates interleaved stereo samples for testing.
  /// </summary>
  private static float[] GenerateStereoSamples(int samplePairs)
  {
    var samples = new float[samplePairs * 2];
    var random = new Random(42); // Fixed seed for reproducibility

    for (var i = 0; i < samplePairs * 2; i++)
    {
      samples[i] = (float)(random.NextDouble() * 2 - 1);
    }

    return samples;
  }
}
