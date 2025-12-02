using Radio.Infrastructure.Audio.Visualization;

namespace Radio.Infrastructure.Tests.Audio.Visualization;

public class SpectrumAnalyzerTests
{
  private const int DefaultSampleRate = 48000;
  private const int DefaultFFTSize = 1024;

  private SpectrumAnalyzer CreateAnalyzer(
    int fftSize = DefaultFFTSize,
    int sampleRate = DefaultSampleRate,
    bool applyWindow = true,
    float smoothingFactor = 0.5f)
  {
    return new SpectrumAnalyzer(fftSize, sampleRate, applyWindow, smoothingFactor);
  }

  [Fact]
  public void Constructor_WithValidParameters_Succeeds()
  {
    // Act
    var analyzer = CreateAnalyzer();

    // Assert
    Assert.NotNull(analyzer);
    Assert.Equal(DefaultFFTSize / 2, analyzer.BinCount);
    Assert.True(analyzer.FrequencyResolution > 0);
  }

  [Theory]
  [InlineData(256)]
  [InlineData(512)]
  [InlineData(1024)]
  [InlineData(2048)]
  [InlineData(4096)]
  public void Constructor_WithPowerOfTwoFFTSize_Succeeds(int fftSize)
  {
    // Act
    var analyzer = new SpectrumAnalyzer(fftSize, DefaultSampleRate);

    // Assert
    Assert.Equal(fftSize / 2, analyzer.BinCount);
  }

  [Theory]
  [InlineData(100)]
  [InlineData(500)]
  [InlineData(1000)]
  [InlineData(1023)]
  [InlineData(1025)]
  public void Constructor_WithNonPowerOfTwoFFTSize_ThrowsArgumentException(int fftSize)
  {
    // Act & Assert
    Assert.Throws<ArgumentException>(() => new SpectrumAnalyzer(fftSize, DefaultSampleRate));
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  [InlineData(-48000)]
  public void Constructor_WithInvalidSampleRate_ThrowsArgumentOutOfRangeException(int sampleRate)
  {
    // Act & Assert
    Assert.Throws<ArgumentOutOfRangeException>(() => new SpectrumAnalyzer(DefaultFFTSize, sampleRate));
  }

  [Fact]
  public void BinCount_IsHalfOfFFTSize()
  {
    // Arrange
    var analyzer = CreateAnalyzer(fftSize: 2048);

    // Assert
    Assert.Equal(1024, analyzer.BinCount);
  }

  [Fact]
  public void FrequencyResolution_CalculatedCorrectly()
  {
    // Arrange
    var analyzer = CreateAnalyzer(fftSize: 1024, sampleRate: 48000);

    // Assert - Resolution = SampleRate / FFTSize
    Assert.Equal(48000f / 1024f, analyzer.FrequencyResolution);
  }

  [Fact]
  public void GetFrequencies_ReturnsCorrectFrequencyArray()
  {
    // Arrange
    var analyzer = CreateAnalyzer(fftSize: 1024, sampleRate: 48000);

    // Act
    var frequencies = analyzer.GetFrequencies();

    // Assert
    Assert.Equal(512, frequencies.Length);
    Assert.Equal(0f, frequencies[0]); // First bin is DC (0 Hz)
    Assert.Equal(analyzer.FrequencyResolution, frequencies[1], 0.001f);
    Assert.Equal(analyzer.FrequencyResolution * 2, frequencies[2], 0.001f);
  }

  [Fact]
  public void AddSamples_Array_AcceptsSamples()
  {
    // Arrange
    var analyzer = CreateAnalyzer();
    var samples = GenerateSinWave(440f, DefaultSampleRate, DefaultFFTSize);

    // Act - should not throw
    analyzer.AddSamples(samples);

    // Assert
    var magnitudes = analyzer.GetMagnitudes();
    Assert.Equal(analyzer.BinCount, magnitudes.Length);
  }

  [Fact]
  public void AddSamples_Span_AcceptsSamples()
  {
    // Arrange
    var analyzer = CreateAnalyzer();
    var samples = GenerateSinWave(440f, DefaultSampleRate, DefaultFFTSize);

    // Act - should not throw
    analyzer.AddSamples(samples.AsSpan(), samples.Length);

    // Assert
    var magnitudes = analyzer.GetMagnitudes();
    Assert.Equal(analyzer.BinCount, magnitudes.Length);
  }

  [Fact]
  public void GetMagnitudes_WithSilentInput_ReturnsNearZeroValues()
  {
    // Arrange
    var analyzer = CreateAnalyzer();
    var silence = new float[DefaultFFTSize];
    analyzer.AddSamples(silence);

    // Act
    var magnitudes = analyzer.GetMagnitudes();

    // Assert - all magnitudes should be very low
    foreach (var mag in magnitudes)
    {
      Assert.True(mag < 0.01f, $"Expected near-zero magnitude, got {mag}");
    }
  }

  [Fact]
  public void GetMagnitudes_WithSineWave_ShowsPeakAtCorrectFrequency()
  {
    // Arrange
    var analyzer = CreateAnalyzer(fftSize: 2048, sampleRate: 48000, applyWindow: true, smoothingFactor: 0f);
    var frequency = 1000f; // 1 kHz test tone
    var samples = GenerateSinWave(frequency, 48000, 2048);

    // Fill the buffer
    analyzer.AddSamples(samples);

    // Act
    var magnitudes = analyzer.GetMagnitudes();
    var frequencies = analyzer.GetFrequencies();

    // Find the peak
    var maxIndex = 0;
    var maxValue = magnitudes[0];
    for (var i = 1; i < magnitudes.Length; i++)
    {
      if (magnitudes[i] > maxValue)
      {
        maxValue = magnitudes[i];
        maxIndex = i;
      }
    }

    // Assert - peak should be near 1 kHz
    var peakFrequency = frequencies[maxIndex];
    Assert.True(Math.Abs(peakFrequency - frequency) < analyzer.FrequencyResolution * 2,
      $"Expected peak near {frequency} Hz, got {peakFrequency} Hz");
  }

  [Fact]
  public void GetMagnitudes_WithSmoothing_ProducesSmoothOutput()
  {
    // Arrange
    var analyzer = CreateAnalyzer(smoothingFactor: 0.9f);
    var samples1 = GenerateSinWave(440f, DefaultSampleRate, DefaultFFTSize);
    var silence = new float[DefaultFFTSize];

    // First, feed sine wave
    analyzer.AddSamples(samples1);
    var magnitudes1 = analyzer.GetMagnitudes();

    // Then feed silence
    analyzer.AddSamples(silence);
    var magnitudes2 = analyzer.GetMagnitudes();

    // Find peak in first measurement
    var maxValue1 = magnitudes1.Max();
    var maxValue2 = magnitudes2.Max();

    // Assert - with smoothing, second measurement should still have some energy
    Assert.True(maxValue2 > maxValue1 * 0.5f,
      "With high smoothing, energy should decay slowly");
  }

  [Fact]
  public void Reset_ClearsAllData()
  {
    // Arrange
    var analyzer = CreateAnalyzer(smoothingFactor: 0f);
    var samples = GenerateSinWave(1000f, DefaultSampleRate, DefaultFFTSize);
    analyzer.AddSamples(samples);

    // Verify there's data
    var beforeReset = analyzer.GetMagnitudes();
    Assert.True(beforeReset.Max() > 0.1f);

    // Act
    analyzer.Reset();

    // Add silence to get fresh magnitudes
    analyzer.AddSamples(new float[DefaultFFTSize]);
    var afterReset = analyzer.GetMagnitudes();

    // Assert - all magnitudes should be low after reset
    Assert.True(afterReset.Max() < 0.01f);
  }

  [Fact]
  public void GetMagnitudes_ReturnsNormalizedValues()
  {
    // Arrange
    var analyzer = CreateAnalyzer(smoothingFactor: 0f);
    var samples = GenerateSinWave(1000f, DefaultSampleRate, DefaultFFTSize);
    analyzer.AddSamples(samples);

    // Act
    var magnitudes = analyzer.GetMagnitudes();

    // Assert - values should be in [0, 1] range
    foreach (var mag in magnitudes)
    {
      Assert.True(mag >= 0f && mag <= 1f,
        $"Magnitude {mag} is outside expected [0, 1] range");
    }
  }

  [Fact]
  public void AddSamples_WithMoreSamplesThanFFTSize_WrapsCorrectly()
  {
    // Arrange
    var analyzer = CreateAnalyzer(fftSize: 512);
    var samples = GenerateSinWave(1000f, DefaultSampleRate, 1024); // 2x FFT size

    // Act - should not throw
    analyzer.AddSamples(samples);

    // Assert
    var magnitudes = analyzer.GetMagnitudes();
    Assert.Equal(256, magnitudes.Length);
  }

  /// <summary>
  /// Generates a sine wave at the specified frequency.
  /// </summary>
  private static float[] GenerateSinWave(float frequency, int sampleRate, int sampleCount)
  {
    var samples = new float[sampleCount];
    for (var i = 0; i < sampleCount; i++)
    {
      samples[i] = MathF.Sin(2f * MathF.PI * frequency * i / sampleRate);
    }
    return samples;
  }
}
