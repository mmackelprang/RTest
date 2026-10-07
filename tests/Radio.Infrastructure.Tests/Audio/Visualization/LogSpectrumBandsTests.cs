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
  public void Tone_PeaksInTheBandContainingItsFrequency_AtItsAbsoluteLevel(float toneHz)
  {
    // A sine of amplitude 0.01 is -40 dBFS. Untilted and uncurved, that draws at (-40 - -65) / 40 =
    // 0.625 wherever it is: the analyzer is calibrated (full-scale sine = 1.0) and the scale is absolute.
    float[] bins = AnalyzeTone(toneHz, amplitude: 0.01f);

    (float[] magnitudes, float[] frequencies) = LogSpectrumBands.Compute(bins, Resolution, tiltDbPerOctave: 0f, curve: 1f);

    int peak = Array.IndexOf(magnitudes, magnitudes.Max());
    // One band is ~4.8% wide at 128 bands over 40 Hz–16 kHz; allow a few bands either side.
    Assert.InRange(frequencies[peak], toneHz / 1.25f, toneHz * 1.25f);
    // Off-bin tones lose up to ~1.4 dB to Hann scalloping, and low bands interpolate between bins.
    Assert.InRange(magnitudes[peak], 0.58f, 0.63f);
  }

  /// <summary>
  /// The static wall: frames used to be scaled to their own loudest band, so broadband noise filled
  /// every bar. White noise at -28 dBFS RMS — radio static's level at the tap, measured on the box
  /// 2026-10-06 — must now draw low (a ramp rising toward the treble once tilted), not a wall. Under
  /// the old per-frame normalization its loudest band was 1.0 on every draw.
  /// </summary>
  [Fact]
  public void StaticLevelNoise_DrawsLow_NotAWall()
  {
    float[] bins = AnalyzeNoise(rmsDbfs: -28f);

    (float[] magnitudes, _) = LogSpectrumBands.Compute(bins, Resolution);

    Assert.True(magnitudes.Max() < 0.8f, $"loudest band {magnitudes.Max():F2}");
    Assert.InRange(magnitudes.Average(), 0.05f, 0.3f);
  }

  /// <summary>
  /// The bass-heavy display: music falls ~3 dB/octave. A spectrum falling exactly 3 dB/octave must
  /// draw flat once tilted, and the band at the pivot must draw at its true level.
  /// </summary>
  [Fact]
  public void Tilt_FlattensAThreeDbPerOctaveFall()
  {
    float[] bins = new float[FftSize / 2];
    for (int i = 1; i < bins.Length; i++)
    {
      // -40 dBFS at 1 kHz, falling 3.01 dB per octave (amplitude ∝ f^-0.5).
      bins[i] = 0.01f * MathF.Pow(i * Resolution / 1000f, -0.5f);
    }

    (float[] magnitudes, float[] frequencies) = LogSpectrumBands.Compute(bins, Resolution);

    // Above the interpolated region, where each band takes a real bin, the tilted display is flat.
    float[] upper = magnitudes.Where((_, b) => frequencies[b] > 500f).ToArray();
    Assert.True(upper.Max() - upper.Min() < 0.03f, $"spread {upper.Max() - upper.Min():F3}");
    // -40 dBFS at the default window and curve: ((-40 - -65) / 40)^1.5 = 0.494. The input falls 3.01
    // dB/octave against a 3.00 tilt, so allow a hair of drift either side.
    Assert.InRange(upper.Average(), 0.48f, 0.51f);
  }

  [Theory]
  [InlineData(1f, 1f)]             // 0 dBFS: above the ceiling
  [InlineData(0.056234f, 1f)]      // -25 dBFS: the ceiling
  [InlineData(0.01f, 0.625f)]      // -40 dBFS
  [InlineData(0.0056234f, 0.5f)]   // -45 dBFS: mid-window
  [InlineData(0.00056234f, 0f)]    // -65 dBFS: the floor
  [InlineData(0.0001f, 0f)]        // below the floor
  public void Values_AreAbsoluteDbfs_MappedFromFloorToCeiling(float binValue, float expected)
  {
    float[] bins = Enumerable.Repeat(binValue, FftSize / 2).ToArray();

    (float[] magnitudes, _) = LogSpectrumBands.Compute(bins, Resolution, tiltDbPerOctave: 0f, curve: 1f);

    Assert.All(magnitudes, m => Assert.Equal(expected, m, 3));
  }

  /// <summary>
  /// The contrast curve: above 1 it shortens quieter bars more than loud ones. Mid-window (-45 dBFS,
  /// 0.5 uncurved) draws at 0.5^curve; the ends of the window are unchanged.
  /// </summary>
  [Theory]
  [InlineData(1f, 0.5f)]
  [InlineData(1.5f, 0.35355f)]
  [InlineData(2f, 0.25f)]
  public void Curve_ShapesTheHeight_ButKeepsTheWindowEnds(float curve, float expectedMid)
  {
    float[] mid = Enumerable.Repeat(0.0056234f, FftSize / 2).ToArray();
    float[] top = Enumerable.Repeat(0.056234f, FftSize / 2).ToArray();

    (float[] midBands, _) = LogSpectrumBands.Compute(mid, Resolution, tiltDbPerOctave: 0f, curve: curve);
    (float[] topBands, _) = LogSpectrumBands.Compute(top, Resolution, tiltDbPerOctave: 0f, curve: curve);

    Assert.All(midBands, m => Assert.Equal(expectedMid, m, 3));
    Assert.All(topBands, m => Assert.Equal(1f, m, 3));
  }

  [Theory]
  [InlineData(0f)]
  [InlineData(-1f)]
  [InlineData(float.NaN)]
  public void InvalidCurve_Throws(float curve)
  {
    Assert.Throws<ArgumentOutOfRangeException>(
      () => LogSpectrumBands.Compute(new float[FftSize / 2], Resolution, curve: curve));
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

  /// <summary>
  /// Low bands are narrower than one bin. They interpolate rather than repeat the same bin, so a
  /// rising slope across the bottom bins comes out rising, not as flat steps.
  /// </summary>
  [Fact]
  public void LowBandsNarrowerThanABin_InterpolateRatherThanRepeat()
  {
    // A gentle ramp that stays inside the dB window across these bands (about -61 to -52 dBFS), so
    // neither the floor nor the ceiling flattens it; tilt off so only the interpolation is under test.
    float[] bins = new float[FftSize / 2];
    for (int i = 0; i < bins.Length; i++)
    {
      bins[i] = 0.0006f + i * 0.0002f;
    }

    (float[] magnitudes, float[] frequencies) = LogSpectrumBands.Compute(bins, Resolution, tiltDbPerOctave: 0f, curve: 1f);

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

  [Theory]
  [InlineData(0f)]
  [InlineData(-1f)]
  [InlineData(float.NaN)]
  public void InvalidMaxHz_Throws(float maxHz)
  {
    Assert.Throws<ArgumentOutOfRangeException>(
      () => LogSpectrumBands.Compute(new float[FftSize / 2], Resolution, maxHz: maxHz));
  }

  [Fact]
  public void NaNMinHz_Throws()
  {
    Assert.Throws<ArgumentOutOfRangeException>(
      () => LogSpectrumBands.Compute(new float[FftSize / 2], Resolution, minHz: float.NaN));
  }

  /// <summary>
  /// At a small FFT (256 → 187.5 Hz per bin) the lowest bands sit below bin 1. A DC offset lives in
  /// bin 0 and must not light them up.
  /// </summary>
  [Fact]
  public void DcBin_IsNeverUsed()
  {
    const int smallFft = 256;
    float[] bins = new float[smallFft / 2];
    bins[0] = 1f; // pure DC

    (float[] magnitudes, _) = LogSpectrumBands.Compute(bins, (float)SampleRate / smallFft);

    Assert.All(magnitudes, m => Assert.Equal(0f, m));
  }

  private static float[] AnalyzeTone(float toneHz, float amplitude = 0.5f)
  {
    var analyzer = new SpectrumAnalyzer(FftSize, SampleRate, applyWindow: true, smoothingFactor: 0f);
    float[] samples = new float[FftSize];
    for (int i = 0; i < samples.Length; i++)
    {
      samples[i] = amplitude * MathF.Sin(2f * MathF.PI * toneHz * i / SampleRate);
    }

    analyzer.AddSamples(samples);
    return analyzer.GetMagnitudes();
  }

  // Seeded uniform white noise scaled to the requested RMS, through the real analyzer.
  private static float[] AnalyzeNoise(float rmsDbfs)
  {
    var analyzer = new SpectrumAnalyzer(FftSize, SampleRate, applyWindow: true, smoothingFactor: 0f);
    var random = new Random(12345);
    float scale = MathF.Pow(10f, rmsDbfs / 20f) * MathF.Sqrt(3f); // uniform [-1,1] has RMS 1/√3
    float[] samples = new float[FftSize];
    for (int i = 0; i < samples.Length; i++)
    {
      samples[i] = scale * (float)(random.NextDouble() * 2 - 1);
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
