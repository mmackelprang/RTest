using Radio.Infrastructure.Audio.Visualization;

namespace Radio.Infrastructure.Tests.Audio.Visualization;

public class LevelMeterTests
{
  private const int DefaultSampleRate = 48000;

  private static LevelMeter CreateMeter(
    int sampleRate = DefaultSampleRate,
    float peakDecayRate = 0.95f,
    float rmsSmoothing = 0.3f,
    int peakHoldTimeMs = 1000)
  {
    return new LevelMeter(sampleRate, peakDecayRate, rmsSmoothing, peakHoldTimeMs);
  }

  [Fact]
  public void Constructor_WithValidParameters_Succeeds()
  {
    // Act
    var meter = CreateMeter();

    // Assert
    Assert.NotNull(meter);
    Assert.Equal(0f, meter.LeftPeak);
    Assert.Equal(0f, meter.RightPeak);
    Assert.Equal(0f, meter.LeftRms);
    Assert.Equal(0f, meter.RightRms);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  [InlineData(-48000)]
  public void Constructor_WithInvalidSampleRate_ThrowsArgumentOutOfRangeException(int sampleRate)
  {
    // Act & Assert
    Assert.Throws<ArgumentOutOfRangeException>(() => CreateMeter(sampleRate: sampleRate));
  }

  [Fact]
  public void ProcessSamples_WithSilence_ReturnsZeroLevels()
  {
    // Arrange
    var meter = CreateMeter(rmsSmoothing: 0f);
    var silence = new float[1024]; // Interleaved stereo silence

    // Act
    meter.ProcessSamples(silence);

    // Assert
    Assert.Equal(0f, meter.LeftPeak);
    Assert.Equal(0f, meter.RightPeak);
    Assert.Equal(0f, meter.LeftRms);
    Assert.Equal(0f, meter.RightRms);
    Assert.False(meter.IsClipping);
  }

  [Fact]
  public void ProcessSamples_WithMaxAmplitude_DetectsClipping()
  {
    // Arrange
    var meter = CreateMeter();
    var clipping = GenerateStereoTone(1.0f, 1.0f, 1024); // Full amplitude

    // Act
    meter.ProcessSamples(clipping);

    // Assert
    Assert.True(meter.IsClipping);
    Assert.True(meter.LeftPeak >= 0.999f);
    Assert.True(meter.RightPeak >= 0.999f);
  }

  [Fact]
  public void ProcessSamples_Array_CalculatesPeakCorrectly()
  {
    // Arrange
    var meter = CreateMeter();
    var samples = GenerateStereoTone(0.5f, 0.75f, 1024);

    // Act
    meter.ProcessSamples(samples);

    // Assert
    Assert.True(meter.LeftPeak >= 0.49f && meter.LeftPeak <= 0.51f,
      $"Expected left peak ~0.5, got {meter.LeftPeak}");
    Assert.True(meter.RightPeak >= 0.74f && meter.RightPeak <= 0.76f,
      $"Expected right peak ~0.75, got {meter.RightPeak}");
  }

  [Fact]
  public void ProcessSamples_Span_CalculatesPeakCorrectly()
  {
    // Arrange
    var meter = CreateMeter();
    var samples = GenerateStereoTone(0.5f, 0.75f, 1024);

    // Act
    meter.ProcessSamples(samples.AsSpan(), samples.Length);

    // Assert
    Assert.True(meter.LeftPeak >= 0.49f && meter.LeftPeak <= 0.51f,
      $"Expected left peak ~0.5, got {meter.LeftPeak}");
    Assert.True(meter.RightPeak >= 0.74f && meter.RightPeak <= 0.76f,
      $"Expected right peak ~0.75, got {meter.RightPeak}");
  }

  [Fact]
  public void ProcessSamples_CalculatesRmsCorrectly()
  {
    // Arrange
    var meter = CreateMeter(rmsSmoothing: 0f);
    var samples = GenerateStereoTone(0.5f, 0.5f, 1024);

    // Act
    meter.ProcessSamples(samples);

    // Assert - RMS of sine wave is amplitude / sqrt(2) ≈ 0.707 * amplitude
    var expectedRms = 0.5f / MathF.Sqrt(2f);
    Assert.True(Math.Abs(meter.LeftRms - expectedRms) < 0.1f,
      $"Expected RMS ~{expectedRms}, got {meter.LeftRms}");
  }

  [Fact]
  public void MonoPeak_ReturnsMaxOfChannels()
  {
    // Arrange
    var meter = CreateMeter();
    var samples = GenerateStereoTone(0.3f, 0.7f, 1024);

    // Act
    meter.ProcessSamples(samples);

    // Assert
    Assert.Equal(Math.Max(meter.LeftPeak, meter.RightPeak), meter.MonoPeak);
    Assert.True(meter.MonoPeak >= 0.69f && meter.MonoPeak <= 0.71f);
  }

  [Fact]
  public void MonoRms_ReturnsAverageOfChannels()
  {
    // Arrange
    var meter = CreateMeter(rmsSmoothing: 0f);
    var samples = GenerateStereoTone(0.5f, 0.5f, 1024);

    // Act
    meter.ProcessSamples(samples);

    // Assert
    var expectedMonoRms = (meter.LeftRms + meter.RightRms) / 2f;
    Assert.Equal(expectedMonoRms, meter.MonoRms, 0.01f);
  }

  [Fact]
  public void LinearToDb_WithFullAmplitude_ReturnsZero()
  {
    // Act
    var db = LevelMeter.LinearToDb(1.0f);

    // Assert
    Assert.Equal(0f, db, 0.01f);
  }

  [Fact]
  public void LinearToDb_WithHalfAmplitude_ReturnsNegative6dB()
  {
    // Act
    var db = LevelMeter.LinearToDb(0.5f);

    // Assert - 20 * log10(0.5) ≈ -6.02 dB
    Assert.True(db >= -6.1f && db <= -6.0f,
      $"Expected ~-6 dB, got {db}");
  }

  [Fact]
  public void LinearToDb_WithZero_ReturnsMinimumDb()
  {
    // Act
    var db = LevelMeter.LinearToDb(0f);

    // Assert
    Assert.Equal(-96f, db); // MinDbValue constant
  }

  [Fact]
  public void LinearToDb_WithNegativeValue_ReturnsMinimumDb()
  {
    // Act
    var db = LevelMeter.LinearToDb(-0.5f);

    // Assert
    Assert.Equal(-96f, db);
  }

  [Fact]
  public void LeftPeakDb_ReturnsCorrectDecibels()
  {
    // Arrange
    var meter = CreateMeter();
    var samples = GenerateStereoTone(0.5f, 0.25f, 1024);
    meter.ProcessSamples(samples);

    // Act
    var leftDb = meter.LeftPeakDb;
    var rightDb = meter.RightPeakDb;

    // Assert
    Assert.True(leftDb >= -6.5f && leftDb <= -5.5f,
      $"Expected left dB ~-6, got {leftDb}");
    Assert.True(rightDb >= -12.5f && rightDb <= -11.5f,
      $"Expected right dB ~-12, got {rightDb}");
  }

  [Fact]
  public void RmsSmoothing_AffectsRmsValues()
  {
    // Arrange
    var meterNoSmoothing = CreateMeter(rmsSmoothing: 0f);
    var meterHighSmoothing = CreateMeter(rmsSmoothing: 0.9f);
    var samples = GenerateStereoTone(0.5f, 0.5f, 1024);
    var silence = new float[1024];

    // Act - process tone then silence
    meterNoSmoothing.ProcessSamples(samples);
    meterHighSmoothing.ProcessSamples(samples);

    var rmsAfterTone1 = meterNoSmoothing.LeftRms;
    var rmsAfterTone2 = meterHighSmoothing.LeftRms;

    meterNoSmoothing.ProcessSamples(silence);
    meterHighSmoothing.ProcessSamples(silence);

    var rmsAfterSilence1 = meterNoSmoothing.LeftRms;
    var rmsAfterSilence2 = meterHighSmoothing.LeftRms;

    // Assert - with no smoothing, RMS drops to zero; with high smoothing, it stays higher
    Assert.True(rmsAfterSilence1 < rmsAfterTone1 * 0.1f,
      "Without smoothing, RMS should drop quickly");
    Assert.True(rmsAfterSilence2 > rmsAfterTone2 * 0.5f,
      "With high smoothing, RMS should stay higher");
  }

  [Fact]
  public void Reset_ClearsAllLevels()
  {
    // Arrange
    var meter = CreateMeter();
    var samples = GenerateStereoTone(0.8f, 0.8f, 1024);
    meter.ProcessSamples(samples);

    // Verify there's data
    Assert.True(meter.LeftPeak > 0.5f);
    Assert.True(meter.RightPeak > 0.5f);

    // Act
    meter.Reset();

    // Assert
    Assert.Equal(0f, meter.LeftPeak);
    Assert.Equal(0f, meter.RightPeak);
    Assert.Equal(0f, meter.LeftRms);
    Assert.Equal(0f, meter.RightRms);
    Assert.False(meter.IsClipping);
  }

  [Fact]
  public void ProcessSamples_WithOddSampleCount_HandlesGracefully()
  {
    // Arrange
    var meter = CreateMeter();
    var samples = new float[1023]; // Odd count
    samples[0] = 0.5f;

    // Act - should not throw
    meter.ProcessSamples(samples);

    // Assert
    Assert.True(meter.LeftPeak >= 0f);
  }

  [Fact]
  public void ProcessSamples_MultipleCalls_AccumulatesCorrectly()
  {
    // Arrange
    var meter = CreateMeter(rmsSmoothing: 0.5f);
    var samples1 = GenerateStereoTone(0.3f, 0.3f, 512);
    var samples2 = GenerateStereoTone(0.8f, 0.8f, 512);

    // Act
    meter.ProcessSamples(samples1);
    var peakAfterFirst = meter.LeftPeak;

    meter.ProcessSamples(samples2);
    var peakAfterSecond = meter.LeftPeak;

    // Assert
    Assert.True(peakAfterSecond >= peakAfterFirst,
      "Peak should increase with louder signal");
  }

  /// <summary>
  /// Generates interleaved stereo sine wave samples.
  /// </summary>
  private static float[] GenerateStereoTone(float leftAmplitude, float rightAmplitude, int samplePairs)
  {
    var samples = new float[samplePairs * 2];
    var frequency = 440f;
    var sampleRate = DefaultSampleRate;

    for (var i = 0; i < samplePairs; i++)
    {
      var value = MathF.Sin(2f * MathF.PI * frequency * i / sampleRate);
      samples[i * 2] = value * leftAmplitude;     // Left channel
      samples[i * 2 + 1] = value * rightAmplitude; // Right channel
    }

    return samples;
  }
}
