using Radio.AudioAnalysis;

namespace Radio.AudioAnalysis.Tests;

public class FrequencyAnalysisTests
{
  [Fact]
  public void GoertzelPower_DetectsKnownFrequency()
  {
    var samples = WavFileHelper.GenerateMonoSineWave(440, 48000, 48000, 0.8f);
    var power = FrequencyAnalysis.GoertzelPower(samples, 48000, 440);
    Assert.True(power > 0.1, $"Expected significant power at 440Hz, got {power}");
  }

  [Fact]
  public void GoertzelPower_LowAtWrongFrequency()
  {
    var samples = WavFileHelper.GenerateMonoSineWave(440, 48000, 48000, 0.8f);
    var powerAtTarget = FrequencyAnalysis.GoertzelPower(samples, 48000, 440);
    var powerAtWrong = FrequencyAnalysis.GoertzelPower(samples, 48000, 1000);

    // Power at wrong frequency should be much lower
    Assert.True(powerAtTarget > powerAtWrong * 10,
      $"Power at 440Hz ({powerAtTarget}) should be >> power at 1000Hz ({powerAtWrong})");
  }

  [Fact]
  public void MeasureTHD_PureSineHasLowDistortion()
  {
    // Pure sine should have near-zero THD
    var stereo = WavFileHelper.GenerateStereoSineWave(440, 440, 48000, 48000, 0.8f);
    var thd = FrequencyAnalysis.MeasureTotalHarmonicDistortion(stereo, 48000, 2, 440, channel: 0);
    Assert.InRange(thd, 0f, 1f); // < 1% THD for a pure sine
  }

  [Fact]
  public void MeasureTHD_ClippedSineHasHigherDistortion()
  {
    // Generate a clipped sine (creates harmonics)
    var samples = WavFileHelper.GenerateMonoSineWave(440, 48000, 48000, 1.0f);
    for (int i = 0; i < samples.Length; i++)
      samples[i] = Math.Clamp(samples[i] * 2.0f, -1.0f, 1.0f); // Hard clip

    var stereo = new float[samples.Length * 2];
    for (int i = 0; i < samples.Length; i++)
    {
      stereo[i * 2] = samples[i];
      stereo[i * 2 + 1] = samples[i];
    }

    var thd = FrequencyAnalysis.MeasureTotalHarmonicDistortion(stereo, 48000, 2, 440, channel: 0);
    Assert.True(thd > 5f, $"Clipped sine should have >5% THD, got {thd}%");
  }

  [Fact]
  public void MeasureTHD_ReturnsZeroForSilence()
  {
    var silence = new float[96000]; // stereo silence
    var thd = FrequencyAnalysis.MeasureTotalHarmonicDistortion(silence, 48000, 2, 440);
    Assert.Equal(0f, thd);
  }

  [Fact]
  public void MeasureTHD_ReturnsZeroForTooShortInput()
  {
    var samples = new float[4]; // way too short
    var thd = FrequencyAnalysis.MeasureTotalHarmonicDistortion(samples, 48000, 2, 440);
    Assert.Equal(0f, thd);
  }
}
