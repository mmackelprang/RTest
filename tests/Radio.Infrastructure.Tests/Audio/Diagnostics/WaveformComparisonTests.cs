using Radio.AudioAnalysis;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Diagnostics;

/// <summary>
/// Tests for <see cref="WaveformComparison"/> and <see cref="FrequencyAnalysis"/>.
/// Verifies cross-correlation offset detection, distortion event detection,
/// and THD measurement.
/// </summary>
public class WaveformComparisonTests
{
  [Fact]
  public void IdenticalSignals_ZeroError()
  {
    var tone = WavFileHelper.GenerateStereoSineWave(
      leftHz: 200, rightHz: 300, durationSamples: 48000);

    var report = WaveformComparison.Compare(tone, tone);

    Assert.True(report.IsClean, $"Expected clean, got: {report}");
    Assert.Equal(0f, report.RmsError);
    Assert.Equal(0f, report.PeakError);
    Assert.Equal(1.0f, report.GainRatio, 3);
    Assert.True(report.CorrelationCoefficient > 0.999f);
  }

  [Fact]
  public void TimeShiftedSignals_CorrectOffsetDetection()
  {
    var tone = WavFileHelper.GenerateStereoSineWave(
      leftHz: 200, rightHz: 300, durationSamples: 48000);

    // Shift by 100 samples
    var shifted = new float[tone.Length];
    Array.Copy(tone, 0, shifted, 100, tone.Length - 100);

    var (offset, correlation) = WaveformComparison.FindTimeOffset(tone, shifted, maxOffsetSamples: 200);

    Assert.InRange(offset, 95, 105); // Should detect ~100 sample offset
    Assert.True(correlation > 0.95f);
  }

  [Fact]
  public void GainScaledSignal_DetectsGainError()
  {
    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, durationSamples: 48000, amplitude: 0.8f);

    // Apply 50% gain reduction
    var gained = new float[reference.Length];
    for (int i = 0; i < reference.Length; i++)
      gained[i] = reference[i] * 0.5f;

    var report = WaveformComparison.Compare(reference, gained);

    Assert.False(report.IsClean);
    Assert.Contains(report.Events, e => e.Type == DistortionType.GainError);
    Assert.InRange(report.GainRatio, 0.45f, 0.55f); // ~0.5 gain ratio
  }

  [Fact]
  public void SilenceInserted_DetectsSilenceInsertion()
  {
    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, durationSamples: 48000, amplitude: 0.8f);

    // Copy and insert 100 samples of silence at position 10000
    var captured = (float[])reference.Clone();
    for (int i = 10000; i < 10100; i++)
      captured[i] = 0f;

    var report = WaveformComparison.Compare(reference, captured);

    Assert.False(report.IsClean);
    Assert.Contains(report.Events, e => e.Type == DistortionType.SilenceInsertion);
  }

  [Fact]
  public void RepeatedSamples_DetectsRepetition()
  {
    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, durationSamples: 48000, amplitude: 0.8f);

    // Copy and repeat a sample value 50 times at position 5000
    var captured = (float[])reference.Clone();
    var repeatedVal = captured[5000];
    if (repeatedVal == 0f) repeatedVal = 0.42f; // Ensure non-zero for detection
    for (int i = 5000; i < 5050; i++)
      captured[i] = repeatedVal;

    var report = WaveformComparison.Compare(reference, captured);

    Assert.False(report.IsClean);
    Assert.Contains(report.Events, e => e.Type == DistortionType.RepeatedSamples);
  }

  [Fact]
  public void ChannelSwap_Detected()
  {
    // Generate stereo with very different L/R content
    var reference = WavFileHelper.GenerateStereoSineWave(
      leftHz: 200, rightHz: 800, durationSamples: 48000, amplitude: 0.8f);

    // Swap channels
    var swapped = new float[reference.Length];
    for (int i = 0; i < reference.Length / 2; i++)
    {
      swapped[i * 2] = reference[i * 2 + 1]; // L = original R
      swapped[i * 2 + 1] = reference[i * 2]; // R = original L
    }

    var report = WaveformComparison.Compare(reference, swapped,
      options: new ComparisonOptions { Channels = 2 });

    Assert.Contains(report.Events, e => e.Type == DistortionType.ChannelSwap);
  }

  [Fact]
  public void WavRoundTrip_PreservesSignal()
  {
    var original = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, rightHz: 880, durationSamples: 4800);

    var tempFile = Path.Combine(Path.GetTempPath(), $"wav_roundtrip_{Guid.NewGuid()}.wav");
    try
    {
      WavFileHelper.WriteWavFile(tempFile, original);
      var readBack = WavFileHelper.ReadWavFile(tempFile, out var sr, out var ch);

      Assert.Equal(48000, sr);
      Assert.Equal(2, ch);
      Assert.Equal(original.Length, readBack.Length);

      // 16-bit quantization introduces small error — should be < 1/32768
      for (int i = 0; i < original.Length; i++)
      {
        var error = MathF.Abs(original[i] - readBack[i]);
        Assert.True(error < 0.001f, $"Sample {i}: expected {original[i]:F6}, got {readBack[i]:F6}");
      }
    }
    finally
    {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  [Fact]
  public void SilenceDetector_FindsZeroRuns()
  {
    var samples = new float[1000];
    // Fill with non-zero
    for (int i = 0; i < samples.Length; i++)
      samples[i] = 0.5f;

    // Insert two zero runs
    for (int i = 100; i < 120; i++) samples[i] = 0f; // 20-sample run
    for (int i = 500; i < 510; i++) samples[i] = 0f; // 10-sample run

    var runs = SilenceDetector.FindZeroRuns(samples, minRunLength: 8);

    Assert.Equal(2, runs.Count);
    Assert.Equal(100, runs[0].Start);
    Assert.Equal(20, runs[0].Length);
    Assert.Equal(500, runs[1].Start);
    Assert.Equal(10, runs[1].Length);
  }

  [Fact]
  public void SilenceDetector_FindsRepeatedSampleRuns()
  {
    var samples = new float[1000];
    for (int i = 0; i < samples.Length; i++)
      samples[i] = MathF.Sin(2 * MathF.PI * 440f * i / 48000f) * 0.5f;

    // Insert a repeated sample run
    for (int i = 300; i < 320; i++)
      samples[i] = 0.42f;

    var runs = SilenceDetector.FindRepeatedSampleRuns(samples, minRunLength: 8);

    Assert.Single(runs);
    Assert.Equal(300, runs[0].Start);
    Assert.Equal(20, runs[0].Length);
  }

  [Fact]
  public void SilenceDetector_FindsClippingRuns()
  {
    var samples = new float[1000];
    for (int i = 0; i < samples.Length; i++)
      samples[i] = MathF.Sin(2 * MathF.PI * 440f * i / 48000f) * 0.5f;

    // Insert clipping
    for (int i = 200; i < 215; i++)
      samples[i] = 1.0f;

    var runs = SilenceDetector.FindClippingRuns(samples, threshold: 0.999f, minRunLength: 4);

    Assert.Single(runs);
    Assert.Equal(200, runs[0].Start);
    Assert.Equal(15, runs[0].Length);
  }

  [Fact]
  public void THD_PureSineWave_LowDistortion()
  {
    // Pure 440Hz sine, stereo
    var samples = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, rightHz: 440, durationSamples: 48000, amplitude: 0.8f);

    var thd = FrequencyAnalysis.MeasureTotalHarmonicDistortion(
      samples, sampleRate: 48000, channels: 2, expectedFrequencyHz: 440);

    // Pure sine wave should have < 1% THD (any residual is numerical precision)
    Assert.True(thd < 1.0f, $"THD of pure sine should be < 1%, got {thd:F2}%");
  }

  [Fact]
  public void THD_ClippedSineWave_HigherDistortion()
  {
    // Generate a sine wave then hard-clip it
    var samples = WavFileHelper.GenerateStereoSineWave(
      leftHz: 440, rightHz: 440, durationSamples: 48000, amplitude: 0.8f);

    // Hard clip at 0.5 — creates significant harmonics
    for (int i = 0; i < samples.Length; i++)
      samples[i] = Math.Clamp(samples[i], -0.5f, 0.5f);

    var thd = FrequencyAnalysis.MeasureTotalHarmonicDistortion(
      samples, sampleRate: 48000, channels: 2, expectedFrequencyHz: 440);

    // Clipped sine should have measurable THD (typically 10-40%)
    Assert.True(thd > 5.0f, $"THD of clipped sine should be > 5%, got {thd:F2}%");
  }
}
