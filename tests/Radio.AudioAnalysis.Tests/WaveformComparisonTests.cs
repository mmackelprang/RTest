using Radio.AudioAnalysis;

namespace Radio.AudioAnalysis.Tests;

public class WaveformComparisonTests
{
  [Fact]
  public void FindTimeOffset_IdenticalSignals_ZeroOffset()
  {
    var signal = WavFileHelper.GenerateMonoSineWave(440, 48000, 4800, 0.8f);
    var (offset, correlation) = WaveformComparison.FindTimeOffset(signal, signal);
    Assert.Equal(0, offset);
    Assert.True(correlation > 0.99f, $"Expected high correlation, got {correlation}");
  }

  [Fact]
  public void FindTimeOffset_DelayedSignal_PositiveOffset()
  {
    var reference = WavFileHelper.GenerateMonoSineWave(440, 48000, 9600, 0.8f);
    // Shift captured by 100 samples
    var captured = new float[9600];
    Array.Copy(reference, 0, captured, 100, 9500);

    var (offset, correlation) = WaveformComparison.FindTimeOffset(reference, captured, maxOffsetSamples: 200);
    Assert.InRange(offset, 95, 105); // Allow small imprecision
    Assert.True(correlation > 0.95f);
  }

  [Fact]
  public void FindTimeOffset_EmptySignals_ReturnsZero()
  {
    var (offset, correlation) = WaveformComparison.FindTimeOffset(
      ReadOnlySpan<float>.Empty, ReadOnlySpan<float>.Empty);
    Assert.Equal(0, offset);
    Assert.Equal(0f, correlation);
  }

  [Fact]
  public void Compare_IdenticalSignals_ReportsClean()
  {
    var signal = WavFileHelper.GenerateStereoSineWave(200, 300, 48000, 4800, 0.8f);
    var report = WaveformComparison.Compare(signal, signal);

    Assert.True(report.IsClean);
    Assert.Equal(0f, report.RmsError, 4);
    Assert.InRange(report.GainRatio, 0.99f, 1.01f);
    Assert.InRange(report.CorrelationCoefficient, 0.99f, 1.01f);
    Assert.Empty(report.Events);
  }

  [Fact]
  public void Compare_GainDifference_DetectsGainError()
  {
    var reference = WavFileHelper.GenerateStereoSineWave(200, 300, 48000, 4800, 0.8f);
    var captured = new float[reference.Length];
    for (int i = 0; i < reference.Length; i++)
      captured[i] = reference[i] * 0.5f; // Half amplitude

    var report = WaveformComparison.Compare(reference, captured);
    Assert.InRange(report.GainRatio, 0.49f, 0.51f);
    Assert.Contains(report.Events, e => e.Type == DistortionType.GainError);
  }

  [Fact]
  public void Compare_SilenceInsertion_Detected()
  {
    var reference = WavFileHelper.GenerateStereoSineWave(200, 300, 48000, 4800, 0.8f);
    var captured = (float[])reference.Clone();
    // Insert silence gap
    for (int i = 1000; i < 1100; i++)
      captured[i] = 0f;

    var report = WaveformComparison.Compare(reference, captured);
    Assert.Contains(report.Events, e => e.Type == DistortionType.SilenceInsertion);
  }

  [Fact]
  public void Compare_ChannelSwap_Detected()
  {
    var reference = WavFileHelper.GenerateStereoSineWave(200, 800, 48000, 4800, 0.8f);
    var swapped = new float[reference.Length];
    for (int i = 0; i < reference.Length / 2; i++)
    {
      swapped[i * 2] = reference[i * 2 + 1];     // L <- R
      swapped[i * 2 + 1] = reference[i * 2];       // R <- L
    }

    var options = new ComparisonOptions { Channels = 2 };
    var report = WaveformComparison.Compare(reference, swapped, options: options);
    Assert.Contains(report.Events, e => e.Type == DistortionType.ChannelSwap);
  }

  [Fact]
  public void Compare_EmptyCompare_ReturnsErrorReport()
  {
    var report = WaveformComparison.Compare(Array.Empty<float>(), Array.Empty<float>());
    Assert.Equal(0, report.TotalSamplesCompared);
    Assert.Equal(float.NegativeInfinity, report.SnrDb);
  }

  [Fact]
  public void DistortionReport_ToString_ContainsKeyMetrics()
  {
    var signal = WavFileHelper.GenerateStereoSineWave(200, 300, 48000, 4800, 0.8f);
    var report = WaveformComparison.Compare(signal, signal);
    var str = report.ToString();
    Assert.Contains("SNR", str);
    Assert.Contains("RMS Error", str);
    Assert.Contains("Gain=", str);
    Assert.Contains("Correlation=", str);
  }
}
