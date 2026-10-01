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
  // A VHF window centred on 162.40625 MHz: its edges are on the 12.5 kHz grid but not on a round number.
  private static readonly BandAxis Vhf = new("VHF", 161_406_250, 163_406_250, 161_406_250, 163_406_250, 12_500);
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
  [InlineData(162_010_000, 162_006_250)]
  [InlineData(162_000_000, 162_006_250)] // 47.5 steps from the edge: rounds up
  [InlineData(161_412_000, 161_406_250)]
  public void Vhf_NearestChannel_IsOnTheWindowsGrid_NotOnRoundNumbers(double hz, long expected) =>
    Vhf.NearestChannelHz(hz).Should().Be(expected);

  [Fact]
  public void HzToFraction_AndBack_FollowTheBandsRange()
  {
    Air.HzToFraction(122_500_000).Should().Be(0.5);
    Air.FractionToHz(0.5).Should().Be(122_500_000);
    Wb.FractionToHz(2.0).Should().Be(162_562_500, "a fraction is clamped to the plot");
  }

  [Fact]
  public void SnapWindow_IsTwoChannelSpacings()
  {
    Wb.SnapWindowHz.Should().Be(50_000);
    Air.SnapWindowHz.Should().Be(50_000);
    Vhf.SnapWindowHz.Should().Be(25_000);
  }

  // ── tap snap ─────────────────────────────────────────────────────────────

  [Fact]
  public void Air_TapWithin50kHzOfAPeak_TunesThePeak()
  {
    List<BandMapChannelDto> map = Map(Air, (118_050_000, -30f));

    Air.ResolveTapTarget(118_010_000, map).Should().Be(118_050_000);
    Air.ResolveTapTarget(118_100_000, map).Should().Be(118_050_000);
  }

  [Fact]
  public void Air_TapBeyond50kHzOfAPeak_TunesTheNearestChannel()
  {
    List<BandMapChannelDto> map = Map(Air, (118_050_000, -30f));

    // 70 kHz above the peak: outside ±50 kHz, so the nearest channel wins.
    Air.ResolveTapTarget(118_120_000, map).Should().Be(118_125_000);
  }

  [Fact]
  public void Air_TwoPeaksInTheWindow_TheStrongerWins()
  {
    List<BandMapChannelDto> map = Map(Air, (118_000_000, -40f), (118_050_000, -25f));

    Air.ResolveTapTarget(118_020_000, map).Should().Be(118_050_000);
  }

  [Fact]
  public void Vhf_TapSnapsWithin25kHz_OnTheWindowsGrid()
  {
    List<BandMapChannelDto> map = Map(Vhf, (162_406_250, -30f));

    Vhf.ResolveTapTarget(162_385_000, map).Should().Be(162_406_250, "21.25 kHz from the peak");
    Vhf.ResolveTapTarget(162_370_000, map).Should().Be(162_368_750, "36.25 kHz from the peak: nearest channel");
  }
}
