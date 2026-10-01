using RTLSDRCore.Bands;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Sweep;

namespace RTLSDRCore.Tests.Sweep;

/// <summary>AUD-91: the per-band sweep plans.</summary>
public class BandSweepPlansTests
{
  public static TheoryData<BandType> NarrowbandBands => new() { BandType.Weather, BandType.Aircraft, BandType.VHF };

  [Fact]
  public void Fm_IsTheAud76Plan_OneTunePerChannelCentredOnIt()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.FM, null)!;

    Assert.Equal("FM", plan.Band);
    Assert.Equal(FmChannelPlan.Channels, plan.Channels);
    Assert.Equal(101, plan.Tunes.Count);
    Assert.All(plan.Tunes, t => Assert.Equal(new[] { t.CentreHz }, t.ChannelHz));
    Assert.Equal(80_000, plan.HalfWindowHz);
    Assert.Equal(8_000, plan.DcExcludeHz);
    Assert.Equal(200_000, plan.ChannelSpacingHz);
    Assert.Equal(FmChannelPlan.DisplayMinHz, plan.DisplayMinHz);
    Assert.Equal(FmChannelPlan.DisplayMaxHz, plan.DisplayMaxHz);
    Assert.Equal(DeviceSweepTuner.SweepSampleRate, plan.SampleRate);
  }

  [Fact]
  public void Weather_IsSevenNoaaChannels_InOneTuneCentredAt162_4875()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.Weather, null)!;

    Assert.Equal("WB", plan.Band);
    Assert.Equal(
      new long[] { 162_400_000, 162_425_000, 162_450_000, 162_475_000, 162_500_000, 162_525_000, 162_550_000 },
      plan.Channels);
    SweepTune tune = Assert.Single(plan.Tunes);
    Assert.Equal(162_487_500, tune.CentreHz);
    Assert.Equal(25_000, plan.ChannelSpacingHz);
    Assert.Equal(162_387_500, plan.DisplayMinHz);
    Assert.Equal(162_562_500, plan.DisplayMaxHz);
  }

  [Fact]
  public void Aircraft_Is1161Channels_In146Tunes_DisplayedOverTheBand()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.Aircraft, null)!;

    Assert.Equal("AIR", plan.Band);
    Assert.Equal(1161, plan.Channels.Count);
    Assert.Equal(108_000_000, plan.Channels[0]);
    Assert.Equal(137_000_000, plan.Channels[^1]);
    Assert.Equal(146, plan.Tunes.Count);
    Assert.All(plan.Tunes, t => Assert.InRange(t.ChannelHz.Count, 1, 8));
    Assert.Equal(108_000_000, plan.DisplayMinHz);
    Assert.Equal(137_000_000, plan.DisplayMaxHz);
  }

  [Fact]
  public void Vhf_IsA2MhzWindowAroundTheCurrentFrequency_SnappedToTheGrid()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.VHF, 146_523_000)!;

    Assert.Equal("VHF", plan.Band);
    Assert.Equal(161, plan.Channels.Count);
    Assert.Equal(11, plan.Tunes.Count);
    Assert.Equal(12_500, plan.ChannelSpacingHz);
    // 146.523 MHz snaps to 146.525 MHz.
    Assert.Equal(145_525_000, plan.Channels[0]);
    Assert.Equal(147_525_000, plan.Channels[^1]);
    Assert.Equal(plan.Channels[0], plan.DisplayMinHz);
    Assert.Equal(plan.Channels[^1], plan.DisplayMaxHz);
    Assert.All(plan.Channels, hz => Assert.Equal(0, hz % 12_500));
  }

  [Theory]
  [InlineData(30_000_000L, 30_000_000L)]
  [InlineData(30_400_000L, 30_000_000L)]
  [InlineData(300_000_000L, 298_000_000L)]
  [InlineData(299_900_000L, 298_000_000L)]
  public void Vhf_NearABandEdge_ShiftsTheWindowInsideTheBandWithoutShrinkingIt(long currentHz, long expectedFirstHz)
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.VHF, currentHz)!;

    Assert.Equal(161, plan.Channels.Count);
    Assert.Equal(expectedFirstHz, plan.Channels[0]);
    Assert.Equal(expectedFirstHz + 2_000_000, plan.Channels[^1]);
  }

  [Theory]
  [InlineData(null)]
  [InlineData(20_000_000L)]
  [InlineData(400_000_000L)]
  public void Vhf_WithNoUsableFrequency_UsesTheBandsLowest2Mhz(long? currentHz)
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.VHF, currentHz)!;

    Assert.Equal(30_000_000, plan.Channels[0]);
    Assert.Equal(32_000_000, plan.Channels[^1]);
  }

  [Theory]
  [MemberData(nameof(NarrowbandBands))]
  public void Narrowband_EveryChannelWindowFitsTheCapture_AndNoneTouchesDc(BandType band)
  {
    BandSweepPlan plan = BandSweepPlans.For(band, 146_520_000)!;

    Assert.Equal(4_000, plan.HalfWindowHz);
    Assert.Equal(1_000, plan.DcExcludeHz);
    Assert.All(plan.Tunes, tune => Assert.All(tune.ChannelHz, hz =>
    {
      long offset = Math.Abs(hz - tune.CentreHz);
      Assert.True(offset <= BandSweepPlans.NarrowbandMaxOffsetHz - plan.HalfWindowHz,
        $"{hz} Hz is {offset} Hz from centre {tune.CentreHz}");
      Assert.True(offset - plan.HalfWindowHz > plan.DcExcludeHz,
        $"{hz} Hz window reaches the DC exclusion of centre {tune.CentreHz}");
    }));
  }

  [Theory]
  [MemberData(nameof(NarrowbandBands))]
  public void Narrowband_TunesCoverEveryChannelOnceInOrder_OnAUniformGrid(BandType band)
  {
    BandSweepPlan plan = BandSweepPlans.For(band, 146_520_000)!;

    Assert.Equal(plan.Channels, plan.Tunes.SelectMany(t => t.ChannelHz));
    Assert.All(plan.Channels.Zip(plan.Channels.Skip(1)), pair =>
      Assert.Equal(plan.ChannelSpacingHz, pair.Second - pair.First));
  }

  [Theory]
  [InlineData(25_000L, 8)]
  [InlineData(12_500L, 16)]
  public void NarrowbandGroupSize_MatchesTheDesign(long spacingHz, int expected)
  {
    Assert.Equal(expected, BandSweepPlans.NarrowbandGroupSize(spacingHz));
  }

  [Fact]
  public void Am_HasNoPlan_AndSaysItIsBelowTheTunersLowerLimit_InKilohertz()
  {
    Assert.Null(BandSweepPlans.For(BandType.AM, 1_000_000));

    string reason = BandSweepPlans.UnavailableReason(BandType.AM)!;
    Assert.Contains("530–1,710 kHz", reason);
    Assert.Contains("is below this tuner's 24 MHz lower limit", reason);
    Assert.Contains("cannot be scanned", reason);
  }

  [Fact]
  public void Shortwave_HasNoPlan_AndSaysItIsMostlyBelowTheTunersLowerLimit_InMegahertz()
  {
    Assert.Null(BandSweepPlans.For(BandType.Shortwave, 27_000_000));

    string reason = BandSweepPlans.UnavailableReason(BandType.Shortwave)!;
    Assert.Contains("1.6–30 MHz", reason);
    Assert.Contains("mostly below this tuner's 24 MHz lower limit", reason);
  }

  [Fact]
  public void UnavailableReason_FollowsTheTunerRange_NotTheBandName()
  {
    // Every preset band wholly inside the tuner's range is mappable; every other one is not.
    foreach (var preset in BandPresets.AllBands)
    {
      bool inRange = preset.MinFrequencyHz >= RtlSdrDevice.TunerMinFrequencyHz
        && preset.MaxFrequencyHz <= RtlSdrDevice.TunerMaxFrequencyHz;
      Assert.Equal(inRange, BandSweepPlans.UnavailableReason(preset.Type) == null);
      Assert.Equal(inRange, BandSweepPlans.For(preset.Type, null) != null);
    }
  }

  [Theory]
  [InlineData("fm", BandType.FM)]
  [InlineData("WB", BandType.Weather)]
  [InlineData("air", BandType.Aircraft)]
  [InlineData("Vhf", BandType.VHF)]
  [InlineData("AM", BandType.AM)]
  [InlineData("sw", BandType.Shortwave)]
  public void TryParseBandCode_AcceptsTheCodesCaseInsensitively(string code, BandType expected)
  {
    Assert.True(BandSweepPlans.TryParseBandCode(code, out BandType band));
    Assert.Equal(expected, band);
    Assert.Equal(code.ToUpperInvariant(), BandSweepPlans.BandCode(band));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("1")]
  [InlineData("Shortwave")]
  [InlineData("Custom")]
  public void TryParseBandCode_RejectsAnythingElse(string? code)
  {
    Assert.False(BandSweepPlans.TryParseBandCode(code, out _));
  }
}
