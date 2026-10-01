using RTLSDRCore.Models;
using RTLSDRCore.Sweep;

namespace RTLSDRCore.Tests.Sweep;

public class ChannelPowerMeterTests
{
  private const int Count = 16384;

  [Fact]
  public void InBandTone_ReadsFarAboveWeakerNoise()
  {
    IqSample[] noise = SweepTestSignals.Noise(Count, amplitude: 0.01f);
    IqSample[] toneAndNoise = SweepTestSignals.Add(SweepTestSignals.Tone(Count, 40_000, 0.5f), noise);

    float noiseDb = ChannelPowerMeter.MeasureDbfs(noise, SweepTestSignals.SampleRate);
    float toneDb = ChannelPowerMeter.MeasureDbfs(toneAndNoise, SweepTestSignals.SampleRate);

    Assert.True(toneDb - noiseDb > 20f, $"tone {toneDb:F1} dB vs noise {noiseDb:F1} dB");
  }

  [Fact]
  public void ToneAtDc_IsExcluded()
  {
    IqSample[] noise = SweepTestSignals.Noise(Count, amplitude: 0.01f);
    IqSample[] dcAndNoise = SweepTestSignals.Add(SweepTestSignals.Tone(Count, 0, 0.5f), noise);

    float noiseDb = ChannelPowerMeter.MeasureDbfs(noise, SweepTestSignals.SampleRate);
    float dcDb = ChannelPowerMeter.MeasureDbfs(dcAndNoise, SweepTestSignals.SampleRate);

    Assert.True(Math.Abs(dcDb - noiseDb) < 3f, $"dc {dcDb:F1} dB vs noise {noiseDb:F1} dB");
  }

  [Fact]
  public void ToneOutsideHalfBandwidth_IsExcluded()
  {
    IqSample[] noise = SweepTestSignals.Noise(Count, amplitude: 0.01f);
    IqSample[] outAndNoise = SweepTestSignals.Add(SweepTestSignals.Tone(Count, 110_000, 0.5f), noise);

    float noiseDb = ChannelPowerMeter.MeasureDbfs(noise, SweepTestSignals.SampleRate);
    float outDb = ChannelPowerMeter.MeasureDbfs(outAndNoise, SweepTestSignals.SampleRate);

    Assert.True(Math.Abs(outDb - noiseDb) < 3f, $"out-of-band {outDb:F1} dB vs noise {noiseDb:F1} dB");
  }

  [Fact]
  public void NegativeFrequencyTone_IsMeasured()
  {
    IqSample[] noise = SweepTestSignals.Noise(Count, amplitude: 0.01f);
    IqSample[] toneAndNoise = SweepTestSignals.Add(SweepTestSignals.Tone(Count, -40_000, 0.5f), noise);

    float noiseDb = ChannelPowerMeter.MeasureDbfs(noise, SweepTestSignals.SampleRate);
    float toneDb = ChannelPowerMeter.MeasureDbfs(toneAndNoise, SweepTestSignals.SampleRate);

    Assert.True(toneDb - noiseDb > 20f, $"tone {toneDb:F1} dB vs noise {noiseDb:F1} dB");
  }

  [Fact]
  public void ZeroInput_ReturnsFloor()
  {
    Assert.Equal(ChannelPowerMeter.FloorDb, ChannelPowerMeter.MeasureDbfs(new IqSample[Count], SweepTestSignals.SampleRate));
  }

  [Fact]
  public void FewerSamplesThanOneFrame_ReturnsFloor()
  {
    IqSample[] tone = SweepTestSignals.Tone(ChannelPowerMeter.FftSize - 1, 40_000, 0.5f);

    Assert.Equal(ChannelPowerMeter.FloorDb, ChannelPowerMeter.MeasureDbfs(tone, SweepTestSignals.SampleRate));
  }

  [Fact]
  public void LouderTone_ReadsHigherByAmplitudeRatio()
  {
    float quiet = ChannelPowerMeter.MeasureDbfs(SweepTestSignals.Tone(Count, 40_000, 0.05f), SweepTestSignals.SampleRate);
    float loud = ChannelPowerMeter.MeasureDbfs(SweepTestSignals.Tone(Count, 40_000, 0.5f), SweepTestSignals.SampleRate);

    // 10x amplitude = +20 dB.
    Assert.InRange(loud - quiet, 19.5f, 20.5f);
  }
}
