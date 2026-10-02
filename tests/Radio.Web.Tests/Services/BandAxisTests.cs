using FluentAssertions;
using Radio.Web.Models;
using Radio.Web.Services;

namespace Radio.Web.Tests.Services;

/// <summary>
/// The BAND view's per-band axis (AUD-91): the plot range and ticks per band, the channel grid, and the
/// two-spacing tap snap. FM must stay exactly as AUD-76 shipped it.
/// </summary>
public class BandAxisTests
{
  private const float Noise = -60f;

  // The axes the API sends (BandMapController.WithAxis), per band.
  private static readonly BandAxis Wb = new("WB", 162_387_500, 162_562_500, 162_400_000, 162_550_000, 25_000);
  private static readonly BandAxis Air = new("AIR", 108_000_000, 137_000_000, 108_000_000, 137_000_000, 25_000);
  // VHF windows as the API produces them: ±1 MHz around a channel of the 12.5 kHz grid. This one is
  // centred on 162.4 MHz, so neither edge is a tick; VhfOnTheHalf is centred on 162.5 MHz, a 0.5 MHz
  // multiple, so both edges are.
  private static readonly BandAxis Vhf = new("VHF", 161_400_000, 163_400_000, 161_400_000, 163_400_000, 12_500);
  private static readonly BandAxis VhfOnTheHalf = new("VHF", 161_500_000, 163_500_000, 161_500_000, 163_500_000, 12_500);
  private static readonly BandAxis Am = new("AM", 530_000, 1_710_000, 530_000, 1_710_000, 10_000);
  private static readonly BandAxis Sw = new("SW", 1_600_000, 30_000_000, 1_600_000, 30_000_000, 5_000);

  private static string[] Labels(BandAxis axis) => axis.Ticks().Select(t => t.Label).ToArray();

  /// <summary>A grid of channels at the noise floor from first to last, with the given ones raised.</summary>
  private static List<BandMapChannelDto> Map(BandAxis axis, params (long Hz, float Level)[] raised)
  {
    var channels = new List<BandMapChannelDto>();
    for (long hz = axis.FirstChannelHz; hz <= axis.LastChannelHz; hz += axis.ChannelSpacingHz)
    {
      channels.Add(new BandMapChannelDto(hz, Noise));
    }

    foreach ((long hz, float level) in raised)
    {
      int i = channels.FindIndex(c => c.FrequencyHz == hz);
      i.Should().BeGreaterThanOrEqualTo(0, $"{hz} Hz is a channel of the grid");
      channels[i] = new BandMapChannelDto(hz, level);
    }

    return channels;
  }

  // ── FM unchanged ─────────────────────────────────────────────────────────

  [Fact]
  public void Fm_KeepsAud76sAxisLabels()
  {
    Labels(BandAxis.Fm).Should().Equal("88", "92", "96", "100", "104", "108");
    BandAxis.Fm.Ticks().Select(t => t.Hz).Should().Equal(88e6, 92e6, 96e6, 100e6, 104e6, 108e6);
  }

  [Fact]
  public void Fm_KeepsAud76sRangeSnapAndBarWidth()
  {
    BandAxis.Fm.DisplayMinHz.Should().Be(87_500_000);
    BandAxis.Fm.DisplayMaxHz.Should().Be(108_000_000);
    BandAxis.Fm.SnapWindowHz.Should().Be(FmBandMath.SnapWindowHz).And.Be(400_000);
    // visualizer.js draws a bar width / channelsAcross * 0.5 wide; AUD-76 hard-coded width / 102.5 * 0.5.
    BandAxis.Fm.ChannelsAcross.Should().Be(102.5);
  }

  [Fact]
  public void FromMap_FmResponse_IsTheFmAxis_WhateverItsFieldsSay()
  {
    BandAxis.FromMap(new BandMapResponseDto { Band = "FM", DisplayMinHz = 1, DisplayMaxHz = 2, ChannelSpacingHz = 1 })
      .Should().BeSameAs(BandAxis.Fm);
  }

  [Fact]
  public void FromMap_NullOrAnOldShapeResponse_IsTheFmAxis()
  {
    BandAxis.FromMap(null).Should().BeSameAs(BandAxis.Fm);
    // An API older than AUD-91 sends no axis fields: they read as zero.
    BandAxis.FromMap(new BandMapResponseDto()).Should().BeSameAs(BandAxis.Fm);
  }

  [Fact]
  public void FromMap_NonFmResponse_UsesItsAxis()
  {
    BandAxis axis = BandAxis.FromMap(new BandMapResponseDto
    {
      Band = "WB",
      DisplayMinHz = 162_387_500,
      DisplayMaxHz = 162_562_500,
      FirstChannelHz = 162_400_000,
      LastChannelHz = 162_550_000,
      ChannelSpacingHz = 25_000,
    });

    axis.Should().Be(Wb);
  }

  // ── ticks per band ───────────────────────────────────────────────────────

  [Fact]
  public void Wb_TicksEvery50kHz_WithTwoDecimals() =>
    Labels(Wb).Should().Equal("162.40", "162.45", "162.50", "162.55");

  [Fact]
  public void Air_TicksEvery5MHz() =>
    Labels(Air).Should().Equal("110", "115", "120", "125", "130", "135");

  [Fact]
  public void Vhf_WindowFromANonRoundEdge_TicksEveryHalfMHz()
  {
    Labels(Vhf).Should().Equal("161.5", "162.0", "162.5", "163.0");
    Vhf.Ticks().Select(t => t.Hz).Should().Equal(161.5e6, 162.0e6, 162.5e6, 163.0e6);
  }

  [Fact]
  public void Vhf_WindowOnAHalfMHz_TicksAtBothEdges() =>
    Labels(VhfOnTheHalf).Should().Equal("161.5", "162.0", "162.5", "163.0", "163.5");

  [Fact]
  public void Am_TicksInKhz()
  {
    Labels(Am).Should().Equal("600", "800", "1000", "1200", "1400", "1600");
    Am.Ticks()[0].Hz.Should().Be(600_000);
  }

  [Fact]
  public void Sw_TicksEvery5MHz() =>
    Labels(Sw).Should().Equal("5", "10", "15", "20", "25", "30");

  [Fact]
  public void NonFmTicks_AreAtMostMaxTicks_AndAllOnThePlot()
  {
    foreach (BandAxis axis in new[] { Wb, Air, Vhf, Am, Sw })
    {
      IReadOnlyList<BandAxisTick> ticks = axis.Ticks();
      ticks.Count.Should().BeInRange(2, BandAxis.MaxTicks, axis.Band);
      ticks.Should().OnlyContain(t => axis.Contains(t.Hz), axis.Band);
    }
  }

  // ── grid and plot ────────────────────────────────────────────────────────

  [Theory]
  [InlineData(118_012_000, 118_000_000)]
  [InlineData(118_013_000, 118_025_000)]
  [InlineData(118_012_500, 118_025_000)] // exactly between: rounds up
  [InlineData(100_000_000, 108_000_000)] // below the band: clamped to the first channel
  [InlineData(140_000_000, 137_000_000)] // above: the last
  public void Air_NearestChannel_IsOnThe25kHzGrid(double hz, long expected) =>
    Air.NearestChannelHz(hz).Should().Be(expected);

  [Theory]
  [InlineData(162_010_000, 162_012_500)]
  [InlineData(162_006_250, 162_012_500)] // exactly between: rounds up
  [InlineData(161_000_000, 161_400_000)] // below the window: clamped to its first channel
  [InlineData(164_000_000, 163_400_000)] // above: its last
  public void Vhf_NearestChannel_IsOnTheWindowsGrid(double hz, long expected) =>
    Vhf.NearestChannelHz(hz).Should().Be(expected);

  [Fact]
  public void HzToFraction_AndBack_FollowTheBandsRange()
  {
    Air.HzToFraction(122_500_000).Should().Be(0.5);
    Air.FractionToHz(0.5).Should().Be(122_500_000);
    Wb.FractionToHz(2.0).Should().Be(162_562_500, "a fraction is clamped to the plot");
  }

  [Fact]
  public void SnapWindow_IsTwoSpacingsOrTwoPercentOfTheSpan_WhicheverIsWider_ButFmKeepsTwoSpacings()
  {
    Wb.SnapWindowHz.Should().Be(50_000, "2% of WB's 175 kHz is 3.5 kHz, less than two 25 kHz spacings");
    Air.SnapWindowHz.Should().Be(580_000, "2% of AIR's 29 MHz");
    Vhf.SnapWindowHz.Should().Be(40_000, "2% of VHF's 2 MHz window");
    BandAxis.Fm.SnapWindowHz.Should().Be(400_000, "FM keeps AUD-76's ±0.4 MHz, not 2% of 20.5 MHz");
  }

  // ── tap snap ─────────────────────────────────────────────────────────────

  [Fact]
  public void Air_TapNearAPeak_TunesThePeak()
  {
    List<BandMapChannelDto> map = Map(Air, (118_050_000, -30f));

    Air.ResolveTapTarget(118_010_000, map).Should().Be(118_050_000);
    Air.ResolveTapTarget(118_100_000, map).Should().Be(118_050_000);
  }

  [Fact]
  public void Air_TapThreeHundredKhzFromAPeak_TunesThePeak()
  {
    // ±50 kHz is ±1.5 px across AIR's 29 MHz; 300 kHz is a near miss a finger makes.
    List<BandMapChannelDto> map = Map(Air, (118_050_000, -30f));

    Air.ResolveTapTarget(118_350_000, map).Should().Be(118_050_000);
    Air.ResolveTapTarget(117_750_000, map).Should().Be(118_050_000);
  }

  [Fact]
  public void Air_TapBeyondTheSnapOfAPeak_TunesTheNearestChannel()
  {
    List<BandMapChannelDto> map = Map(Air, (118_050_000, -30f));

    // 640 kHz above the peak: outside ±580 kHz, so the nearest channel wins.
    Air.ResolveTapTarget(118_690_000, map).Should().Be(118_700_000);
  }

  [Fact]
  public void Wb_TapSnapStaysAtTwoSpacings()
  {
    List<BandMapChannelDto> map = Map(Wb, (162_400_000, -30f));

    Wb.ResolveTapTarget(162_450_000, map).Should().Be(162_400_000, "50 kHz from the peak");
    Wb.ResolveTapTarget(162_460_000, map).Should().Be(162_450_000, "60 kHz from the peak: nearest channel");
  }

  [Fact]
  public void Air_TwoPeaksInTheWindow_TheStrongerWins()
  {
    List<BandMapChannelDto> map = Map(Air, (118_000_000, -40f), (118_050_000, -25f));

    Air.ResolveTapTarget(118_020_000, map).Should().Be(118_050_000);
  }

  [Fact]
  public void Vhf_TapSnapsWithin40kHz_OnTheWindowsGrid()
  {
    List<BandMapChannelDto> map = Map(Vhf, (162_412_500, -30f));

    Vhf.ResolveTapTarget(162_380_000, map).Should().Be(162_412_500, "32.5 kHz from the peak");
    Vhf.ResolveTapTarget(162_360_000, map).Should().Be(162_362_500, "52.5 kHz from the peak: nearest channel");
  }

  // ── axis labels ──────────────────────────────────────────────────────────

  private static (string Text, BandAxisLabelAlign Align)[] Strip(BandAxis axis) =>
    axis.Labels().Select(l => (l.Text, l.Align)).ToArray();

  [Fact]
  public void FmLabels_AreAud76s_With108EndAligned_AndNoUnit()
  {
    Strip(BandAxis.Fm).Should().Equal(
      ("88", BandAxisLabelAlign.Center), ("92", BandAxisLabelAlign.Center), ("96", BandAxisLabelAlign.Center),
      ("100", BandAxisLabelAlign.Center), ("104", BandAxisLabelAlign.Center), ("108", BandAxisLabelAlign.End));
    BandAxis.Fm.Labels().Select(l => l.Fraction)
      .Should().Equal(BandAxis.Fm.Ticks().Select(t => BandAxis.Fm.HzToFraction(t.Hz)));
  }

  [Fact]
  public void WbLabels_LastIsCentred()
  {
    // 162.55 MHz is 93% across WB's plot: not at its edge.
    Strip(Wb).Should().Equal(
      ("162.40", BandAxisLabelAlign.Center), ("162.45", BandAxisLabelAlign.Center),
      ("162.50", BandAxisLabelAlign.Center), ("162.55", BandAxisLabelAlign.Center));
  }

  [Fact]
  public void VhfLabels_OnAWindowWhoseEdgesAreTicks_AreAlignedInsideThePlot()
  {
    Strip(VhfOnTheHalf).Should().Equal(
      ("161.5", BandAxisLabelAlign.Start), ("162.0", BandAxisLabelAlign.Center), ("162.5", BandAxisLabelAlign.Center),
      ("163.0", BandAxisLabelAlign.Center), ("163.5", BandAxisLabelAlign.End));
  }

  [Fact]
  public void NoLabel_CarriesAUnit()
  {
    // UI-31: the unit is in the strip's range line under the map; on the axis it was clipped at the
    // right edge on WB and VHF.
    Air.Labels()[^1].Text.Should().Be("135");
    Am.Labels()[^1].Text.Should().Be("1600");
    Am.Labels()[0].Text.Should().Be("600");
    Sw.Labels()[^1].Should().Be(new BandAxisLabel(1.0, "30", BandAxisLabelAlign.End));
    new[] { BandAxis.Fm, Air, Am, Sw, Wb, VhfOnTheHalf }.SelectMany(a => a.Labels())
      .Should().OnlyContain(l => !l.Text.Contains("Hz"));
  }
}
