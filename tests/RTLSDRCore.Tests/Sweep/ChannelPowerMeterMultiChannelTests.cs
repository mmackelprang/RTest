using RTLSDRCore.Models;
using RTLSDRCore.Sweep;

namespace RTLSDRCore.Tests.Sweep;

/// <summary>AUD-91: <see cref="ChannelPowerMeter.MeasureChannelsDbfs"/>.</summary>
public class ChannelPowerMeterMultiChannelTests
{
  private const int Count = 16384;

  // The eight 25 kHz channels of one AIR tune, as offsets from the tuned centre.
  private static readonly long[] Offsets = { -87_500, -62_500, -37_500, -12_500, 12_500, 37_500, 62_500, 87_500 };

  [Theory]
  [InlineData(0)]
  [InlineData(3)]
  [InlineData(4)]
  [InlineData(7)]
  public void ToneAtOneChannelsOffset_RaisesThatChannelOnly(int channel)
  {
    IqSample[] noise = SweepTestSignals.Noise(Count, amplitude: 0.01f);
    IqSample[] block = SweepTestSignals.Add(SweepTestSignals.Tone(Count, Offsets[channel], 0.5f), noise);

    float[] quiet = ChannelPowerMeter.MeasureChannelsDbfs(noise, SweepTestSignals.SampleRate, Offsets, 4_000, 1_000);
    float[] levels = ChannelPowerMeter.MeasureChannelsDbfs(block, SweepTestSignals.SampleRate, Offsets, 4_000, 1_000);

    Assert.Equal(Offsets.Length, levels.Length);
    Assert.True(levels[channel] - quiet[channel] > 20f, $"channel {channel}: {levels[channel]:F1} dB vs noise {quiet[channel]:F1} dB");
    for (int c = 0; c < Offsets.Length; c++)
    {
      if (c != channel)
      {
        Assert.True(Math.Abs(levels[c] - quiet[c]) < 3f, $"neighbour {c}: {levels[c]:F1} dB vs noise {quiet[c]:F1} dB");
      }
    }
  }

  [Theory]
  [InlineData(1234)]
  [InlineData(99)]
  [InlineData(7)]
  public void FmWindowAtOffsetZero_IsBitIdenticalToMeasureDbfs(int seed)
  {
    IqSample[] block = SweepTestSignals.Add(
      SweepTestSignals.Tone(Count, 40_000, 0.3f), SweepTestSignals.Noise(Count, 0.05f, seed));

    float single = ChannelPowerMeter.MeasureDbfs(block, SweepTestSignals.SampleRate);
    float[] multi = ChannelPowerMeter.MeasureChannelsDbfs(
      block, SweepTestSignals.SampleRate, new long[] { 0 }, BandSweepPlans.FmHalfWindowHz, BandSweepPlans.FmDcExcludeHz);

    Assert.Equal(BitConverter.SingleToInt32Bits(single), BitConverter.SingleToInt32Bits(Assert.Single(multi)));
  }

  [Fact]
  public void ToneInsideTheDcExclusion_IsNotMeasured_EvenInsideAChannelWindow()
  {
    IqSample[] noise = SweepTestSignals.Noise(Count, amplitude: 0.01f);
    IqSample[] block = SweepTestSignals.Add(SweepTestSignals.Tone(Count, 0, 0.5f), noise);
    long[] offsets = { 2_000 };

    float quiet = ChannelPowerMeter.MeasureChannelsDbfs(noise, SweepTestSignals.SampleRate, offsets, 4_000, 1_000)[0];
    float dc = ChannelPowerMeter.MeasureChannelsDbfs(block, SweepTestSignals.SampleRate, offsets, 4_000, 1_000)[0];

    // The DC tone's Hann main lobe spans about ±234 Hz; it sits wholly inside the 1 kHz exclusion.
    Assert.True(Math.Abs(dc - quiet) < 3f, $"dc {dc:F1} dB vs noise {quiet:F1} dB");
  }

  [Fact]
  public void ShortBlockOrNoChannels_ReturnsFloorsOrEmpty()
  {
    float[] shortBlock = ChannelPowerMeter.MeasureChannelsDbfs(
      SweepTestSignals.Tone(ChannelPowerMeter.FftSize - 1, 12_500, 0.5f), SweepTestSignals.SampleRate, Offsets, 4_000, 1_000);
    float[] none = ChannelPowerMeter.MeasureChannelsDbfs(
      SweepTestSignals.Tone(Count, 12_500, 0.5f), SweepTestSignals.SampleRate, Array.Empty<long>(), 4_000, 1_000);

    Assert.All(shortBlock, l => Assert.Equal(ChannelPowerMeter.FloorDb, l));
    Assert.Empty(none);
  }

  [Fact]
  public void WindowBeyondNyquist_ReturnsFloor()
  {
    float[] levels = ChannelPowerMeter.MeasureChannelsDbfs(
      SweepTestSignals.Noise(Count, 0.1f), SweepTestSignals.SampleRate, new long[] { 200_000 }, 4_000, 1_000);

    Assert.Equal(ChannelPowerMeter.FloorDb, Assert.Single(levels));
  }
}
