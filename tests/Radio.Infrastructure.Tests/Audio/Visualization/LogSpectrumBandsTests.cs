using Radio.Infrastructure.Audio.Visualization;

namespace Radio.Infrastructure.Tests.Audio.Visualization;

public class LogSpectrumBandsTests
{
  private const int SampleRate = 48000;
  private const int FftSize = 2048; // VisualizerOptions' default
  private const float Resolution = (float)SampleRate / FftSize;

  /// <summary>
  /// The bug this class fixes: the UI drew the first 64 linear bins (0–1.5 kHz), so a treble tone
  /// never appeared. Through the real analyzer, a 5 kHz tone must now peak in the band around 5 kHz.
  /// </summary>
  [Theory]
  [InlineData(100f)]
  [InlineData(1000f)]
  [InlineData(5000f)]
  [InlineData(12000f)]
  public void Tone_PeaksInTheBandContainingItsFrequency(float toneHz)
  {
    float[] bins = AnalyzeTone(toneHz);

    (float[] magnitudes, float[] frequencies) = LogSpectrumBands.Compute(bins, Resolution);

    int peak = Array.IndexOf(magnitudes, magnitudes.Max());
    // One band is ~4.8% wide at 128 bands over 40 Hz–16 kHz; allow a few bands either side.
    Assert.InRange(frequencies[peak], toneHz / 1.25f, toneHz * 1.25f);
    // Near full scale: a low band interpolates between bins, so it can sit just under the bin peak.
    Assert.InRange(magnitudes[peak], 0.9f, 1f);
  }

  [Fact]
  public void Bands_SpanTheConfiguredRange_LogSpaced_AndAreTheRequestedCount()
  {
    (float[] magnitudes, float[] frequencies) = LogSpectrumBands.Compute(new float[FftSize / 2], Resolution);

    Assert.Equal(LogSpectrumBands.DefaultBandCount, magnitudes.Length);
    Assert.Equal(LogSpectrumBands.DefaultBandCount, frequencies.Length);
    Assert.InRange(frequencies[0], LogSpectrumBands.DefaultMinHz, LogSpectrumBands.DefaultMinHz * 1.1f);
    Assert.InRange(frequencies[^1], LogSpectrumBands.DefaultMaxHz / 1.1f, LogSpectrumBands.DefaultMaxHz);

    // Log spacing: every neighbouring pair has the same ratio.
    float ratio = frequencies[1] / frequencies[0];
    for (int i = 2; i < frequencies.Length; i++)
    {
      Assert.Equal(ratio, frequencies[i] / frequencies[i - 1], 3);
    }
  }

  [Fact]
  public void Silence_DrawsNothing()
  {
    (float[] magnitudes, _) = LogSpectrumBands.Compute(AnalyzeSilence(), Resolution);

    Assert.All(magnitudes, m => Assert.Equal(0f, m));
  }

  [Theory]
  [InlineData(1f, 1f)]          // the frame's peak
  [InlineData(0.1f, 2f / 3f)]   // -20 dB of a 60 dB range
  [InlineData(0.001f, 0f)]      // -60 dB: the floor
  [InlineData(0.0001f, 0f)]     // below the floor
  public void Values_AreDbScaledOverTheRange(float binValue, float expected)
  {
    float[] bins = Enumerable.Repeat(binValue, FftSize / 2).ToArray();

    (float[] magnitudes, _) = LogSpectrumBands.Compute(bins, Resolution);

    Assert.All(magnitudes, m => Assert.Equal(expected, m, 3));
  }

  /// <summary>
  /// Low bands are narrower than one bin. They interpolate rather than repeat the same bin, so a
  /// rising slope across the bottom bins comes out rising, not as flat steps.
  /// </summary>
  [Fact]
  public void LowBandsNarrowerThanABin_InterpolateRatherThanRepeat()
  {
    float[] bins = new float[FftSize / 2];
    for (int i = 0; i < bins.Length; i++)
    {
      bins[i] = Math.Min(1f, 0.01f + i * 0.02f);
    }

    (float[] magnitudes, float[] frequencies) = LogSpectrumBands.Compute(bins, Resolution);

    for (int b = 1; frequencies[b] < 200f; b++)
    {
      Assert.True(magnitudes[b] > magnitudes[b - 1],
        $"band {b} ({frequencies[b]:F0} Hz) did not rise above band {b - 1} ({frequencies[b - 1]:F0} Hz)");
    }
  }

  [Fact]
  public void NoBins_ReturnsEmpty()
  {
    (float[] magnitudes, float[] frequencies) = LogSpectrumBands.Compute([], Resolution);

    Assert.Empty(magnitudes);
    Assert.Empty(frequencies);
  }

  [Fact]
  public void MaxHz_IsClampedToTheHighestBin()
  {
    // 16 kHz sample rate: the bins stop at ~8 kHz, so the top band must too.
    const float lowRateResolution = 16000f / FftSize;

    (_, float[] frequencies) = LogSpectrumBands.Compute(new float[FftSize / 2], lowRateResolution);

    Assert.True(frequencies[^1] <= (FftSize / 2 - 1) * lowRateResolution);
  }

  private static float[] AnalyzeTone(float toneHz)
  {
    var analyzer = new SpectrumAnalyzer(FftSize, SampleRate, applyWindow: true, smoothingFactor: 0f);
    float[] samples = new float[FftSize];
    for (int i = 0; i < samples.Length; i++)
    {
      samples[i] = 0.5f * MathF.Sin(2f * MathF.PI * toneHz * i / SampleRate);
    }

    analyzer.AddSamples(samples);
    return analyzer.GetMagnitudes();
  }

  private static float[] AnalyzeSilence()
  {
    var analyzer = new SpectrumAnalyzer(FftSize, SampleRate, applyWindow: true, smoothingFactor: 0f);
    analyzer.AddSamples(new float[FftSize]);
    return analyzer.GetMagnitudes();
  }
}
