using RTLSDRCore.Bands;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Sweep;

namespace RTLSDRCore.Tests.Sweep;

/// <summary>AUD-91: the per-band sweep plans.</summary>
public class BandSweepPlansTests
{
  public static TheoryData<BandType> NarrowbandBands => new()
  {
    BandType.Weather, BandType.Aircraft, BandType.VHF, BandType.AM, BandType.Shortwave,
  };

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
  public void Am_OnADeviceWithoutHf_SaysItIsBelowTheTunersLowerLimit_InKilohertz()
  {
    string reason = BandSweepPlans.UnavailableReason(
      BandType.AM, RtlSdrDevice.TunerMinFrequencyHz, RtlSdrDevice.TunerMaxFrequencyHz)!;
    Assert.Contains("530–1,710 kHz", reason);
    Assert.Contains("is below this tuner's 24 MHz lower limit", reason);
    Assert.Contains("cannot be scanned", reason);
  }

  [Fact]
  public void Shortwave_OnADeviceWithoutHf_SaysItIsMostlyBelowTheTunersLowerLimit_InMegahertz()
  {
    string reason = BandSweepPlans.UnavailableReason(
      BandType.Shortwave, RtlSdrDevice.TunerMinFrequencyHz, RtlSdrDevice.TunerMaxFrequencyHz)!;
    Assert.Contains("1.711–30 MHz", reason); // AUD-102: SW starts above AM's 1710 kHz
    Assert.Contains("mostly below this tuner's 24 MHz lower limit", reason);
  }

  [Fact]
  public void UnavailableReason_FollowsTheDevicesRange_NotTheBandName()
  {
    // AUD-103: with HF direct sampling (100 kHz floor) every preset band can be swept; without it
    // (24 MHz floor) only the bands wholly above 24 MHz. Either way every preset band has a plan.
    foreach (var preset in BandPresets.AllBands)
    {
      Assert.Null(BandSweepPlans.UnavailableReason(
        preset.Type, HfDirectSampling.HfMinFrequencyHz, RtlSdrDevice.TunerMaxFrequencyHz));

      bool inTunerRange = preset.MinFrequencyHz >= RtlSdrDevice.TunerMinFrequencyHz
        && preset.MaxFrequencyHz <= RtlSdrDevice.TunerMaxFrequencyHz;
      Assert.Equal(inTunerRange, BandSweepPlans.UnavailableReason(
        preset.Type, RtlSdrDevice.TunerMinFrequencyHz, RtlSdrDevice.TunerMaxFrequencyHz) == null);

      Assert.NotNull(BandSweepPlans.For(preset.Type, null));
    }

    Assert.Null(BandSweepPlans.For(BandType.Custom, null));
    Assert.Equal("This band has no channel plan and cannot be scanned.",
      BandSweepPlans.UnavailableReason(BandType.Custom, HfDirectSampling.HfMinFrequencyHz, RtlSdrDevice.TunerMaxFrequencyHz));
  }

  [Fact]
  public void Am_Is119ChannelsAt10Khz_In14Tunes_DisplayedOverTheBand()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.AM, 1_000_000)!;

    Assert.Equal("AM", plan.Band);
    Assert.Equal(119, plan.Channels.Count);
    Assert.Equal(530_000, plan.Channels[0]);
    Assert.Equal(1_710_000, plan.Channels[^1]);
    Assert.Equal(10_000, plan.ChannelSpacingHz);
    // AUD-103 review M2: 9 channels per tune, each tune 16 kHz (window 4 + clearance 12) below its first.
    Assert.Equal(9, BandSweepPlans.HfGroupSize(10_000));
    Assert.Equal(Enumerable.Repeat(9, 13).Append(2), plan.Tunes.Select(t => t.ChannelHz.Count));
    Assert.Equal(514_000, plan.Tunes[0].CentreHz);
    Assert.Equal(1_684_000, plan.Tunes[^1].CentreHz);
    Assert.Equal(530_000, plan.DisplayMinHz);
    Assert.Equal(1_710_000, plan.DisplayMaxHz);
    // The radio's frequency does not move the AM plan.
    Assert.Equal(plan.Channels, BandSweepPlans.For(BandType.AM, null)!.Channels);
  }

  [Theory]
  [InlineData(9_600_000L, "31 m", 9_400_000L, 9_900_000L, 101, 6)]     // inside a segment
  [InlineData(5_000_000L, "60 m", 4_750_000L, 5_060_000L, 63, 4)]      // WWV, inside 60 m
  [InlineData(8_000_000L, "41 m", 7_200_000L, 7_450_000L, 51, 3)]      // between 41 m (550 kHz) and 31 m (1.4 MHz)
  [InlineData(9_000_000L, "31 m", 9_400_000L, 9_900_000L, 101, 6)]     // between, nearer 31 m (400 kHz)
  [InlineData(27_000_000L, "11 m", 25_670_000L, 26_100_000L, 87, 6)]   // above the top segment
  [InlineData(15_400_000L, "19 m", 15_100_000L, 15_800_000L, 141, 9)]  // the widest segment
  [InlineData(1_715_000L, "120 m", 2_300_000L, 2_495_000L, 40, 3)]     // the SW band memory's no-memory default
  [InlineData(null, "120 m", 2_300_000L, 2_495_000L, 40, 3)]           // radio not on SW, nothing remembered
  [InlineData(1_000_000L, "120 m", 2_300_000L, 2_495_000L, 40, 3)]     // not an SW frequency: treated as none
  public void Shortwave_IsTheBroadcastSegmentContainingOrNearestTheFrequency_At5Khz(
    long? currentHz, string segment, long firstHz, long lastHz, int channels, int tunes)
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.Shortwave, currentHz)!;

    Assert.Equal("SW", plan.Band);
    Assert.Equal(segment, ShortwaveSegments.WithEdges(plan.DisplayMinHz, plan.DisplayMaxHz)!.Name);
    Assert.Equal(firstHz, plan.Channels[0]);
    Assert.Equal(lastHz, plan.Channels[^1]);
    Assert.Equal(firstHz, plan.DisplayMinHz);
    Assert.Equal(lastHz, plan.DisplayMaxHz);
    Assert.Equal(channels, plan.Channels.Count);
    Assert.Equal(tunes, plan.Tunes.Count);
    Assert.Equal(5_000, plan.ChannelSpacingHz);
    Assert.Equal(17, BandSweepPlans.HfGroupSize(5_000));
    Assert.All(plan.Channels, hz => Assert.Equal(0, hz % 5_000));
  }

  [Fact]
  public void ShortwaveSegments_AreAscendingDisjoint_OnThe5KhzGrid_AndInsideTheBand()
  {
    RTLSDRCore.Models.RadioBand sw = BandPresets.Shortwave;

    Assert.Equal(14, ShortwaveSegments.All.Count);
    Assert.Equal(
      new[] { "120 m", "90 m", "75 m", "60 m", "49 m", "41 m", "31 m", "25 m", "22 m", "19 m", "16 m", "15 m", "13 m", "11 m" },
      ShortwaveSegments.All.Select(s => s.Name));
    Assert.All(ShortwaveSegments.All, s =>
    {
      Assert.True(s.MinHz < s.MaxHz);
      Assert.Equal(0, s.MinHz % 5_000);
      Assert.Equal(0, s.MaxHz % 5_000);
      Assert.True(sw.ContainsFrequency(s.MinHz) && sw.ContainsFrequency(s.MaxHz), $"{s.Name} is outside SW");
    });
    Assert.All(ShortwaveSegments.All.Zip(ShortwaveSegments.All.Skip(1)), pair =>
      Assert.True(pair.First.MaxHz < pair.Second.MinHz, $"{pair.First.Name} overlaps {pair.Second.Name}"));
  }

  [Fact]
  public void ShortwaveSegments_ExactlyBetweenTwo_PicksTheLower()
  {
    // 41 m ends at 7450 kHz and 31 m starts at 9400 kHz: 8425 kHz is 975 kHz from each.
    Assert.Equal("41 m", ShortwaveSegments.Nearest(8_425_000).Name);
    Assert.Equal("31 m", ShortwaveSegments.Nearest(8_430_000).Name);
  }

  public static TheoryData<BandType, long?> HfPlans => new()
  {
    { BandType.AM, null },
    { BandType.Shortwave, 2_400_000 },
    { BandType.Shortwave, 9_600_000 },
    { BandType.Shortwave, 15_400_000 },   // 19 m, the widest segment: 141 channels
    { BandType.Shortwave, 25_900_000 },
  };

  [Theory]
  [MemberData(nameof(HfPlans))]
  public void AmAndSw_NoWindowComesWithinTheDcClearanceOfItsTuneCentre_AndEveryWindowFitsTheCapture(
    BandType band, long? currentHz)
  {
    // AUD-103 review M2: in HF direct sampling the DC spike sits on every tune centre.
    BandSweepPlan plan = BandSweepPlans.For(band, currentHz)!;

    Assert.Equal(12_000, BandSweepPlans.HfDcClearanceHz);
    Assert.Equal(BandSweepPlans.NarrowbandHalfWindowHz, plan.HalfWindowHz);
    Assert.Equal(BandSweepPlans.NarrowbandDcExcludeHz, plan.DcExcludeHz);
    Assert.Equal(plan.Channels, plan.Tunes.SelectMany(t => t.ChannelHz));
    Assert.All(plan.Tunes, tune =>
    {
      Assert.DoesNotContain(tune.CentreHz, plan.Channels);
      Assert.All(tune.ChannelHz, hz =>
      {
        long offset = Math.Abs(hz - tune.CentreHz);
        Assert.True(offset + plan.HalfWindowHz <= BandSweepPlans.NarrowbandMaxOffsetHz,
          $"{hz} Hz window reaches {offset + plan.HalfWindowHz} Hz from centre {tune.CentreHz}");
        Assert.True(offset - plan.HalfWindowHz >= BandSweepPlans.HfDcClearanceHz,
          $"{hz} Hz window comes within {offset - plan.HalfWindowHz} Hz of centre {tune.CentreHz}");
      });
    });
  }

  [Fact]
  public void Am_CoversEveryChannelFrom530To1710Khz_ExactlyOnce()
  {
    BandSweepPlan plan = BandSweepPlans.For(BandType.AM, null)!;
    long[] expected = Enumerable.Range(0, 119).Select(i => 530_000L + i * 10_000L).ToArray();

    List<long> measured = plan.Tunes.SelectMany(t => t.ChannelHz).ToList();
    Assert.Equal(expected, measured.Order());
    Assert.Equal(measured.Count, measured.Distinct().Count());
  }

  [Theory]
  [InlineData(10_000L, 9)]
  [InlineData(5_000L, 17)]
  public void HfGroupSize_MatchesTheClearance(long spacingHz, int expected)
  {
    Assert.Equal(expected, BandSweepPlans.HfGroupSize(spacingHz));
  }

  public static TheoryData<BandType, long?> NonHfNarrowbandPlans => new()
  {
    { BandType.Weather, null },
    { BandType.Aircraft, null },
    { BandType.VHF, null },
    { BandType.VHF, 146_523_000 },
    { BandType.VHF, 299_900_000 },
  };

  [Theory]
  [MemberData(nameof(NonHfNarrowbandPlans))]
  public void NonHfNarrowbandPlans_AreTheAud91Grouping_Unchanged(BandType band, long? currentHz)
  {
    // AUD-103 review M2 changed AM and SW only. The AUD-91 grouping, written out independently here:
    // runs of NarrowbandGroupSize channels, an even run centred between its ends, an odd one half a
    // spacing above its middle channel.
    BandSweepPlan plan = BandSweepPlans.For(band, currentHz)!;
    long spacing = plan.ChannelSpacingHz;
    int size = BandSweepPlans.NarrowbandGroupSize(spacing);
    List<(long Centre, long[] Channels)> expected = new();
    for (int start = 0; start < plan.Channels.Count; start += size)
    {
      long[] run = plan.Channels.Skip(start).Take(size).ToArray();
      long centre = run.Length % 2 == 0 ? (run[0] + run[^1]) / 2 : run[run.Length / 2] + spacing / 2;
      expected.Add((centre, run));
    }

    Assert.Equal(expected.Select(e => e.Centre), plan.Tunes.Select(t => t.CentreHz));
    Assert.Equal(expected.Select(e => e.Channels.Length), plan.Tunes.Select(t => t.ChannelHz.Count));
    Assert.Equal(BandSweepPlans.NarrowbandHalfWindowHz, plan.HalfWindowHz);
    Assert.Equal(BandSweepPlans.NarrowbandDcExcludeHz, plan.DcExcludeHz);
  }

  [Theory]
  [InlineData(BandType.VHF, true)]
  [InlineData(BandType.Shortwave, true)]
  [InlineData(BandType.AM, false)]
  [InlineData(BandType.FM, false)]
  [InlineData(BandType.Weather, false)]
  [InlineData(BandType.Aircraft, false)]
  public void FollowsRadio_IsVhfAndSwOnly(BandType band, bool expected)
  {
    Assert.Equal(expected, BandSweepPlans.FollowsRadio(band));
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
