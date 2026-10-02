using FluentAssertions;
using Radio.Web.Formatting;

namespace Radio.Web.Tests.Formatting;

/// <summary>The BAND view's age and sweep text (AUD-76 PR 2).</summary>
public class BandMapTextTests
{
  [Theory]
  [InlineData(0, "just now")]
  [InlineData(59.9, "just now")]
  [InlineData(-5, "just now")]
  [InlineData(60, "1 min ago")]
  [InlineData(3599, "59 min ago")]
  [InlineData(3600, "1 h ago")]
  [InlineData(3 * 3600 + 1800, "3 h ago")]
  [InlineData(86399, "23 h ago")]
  [InlineData(86400, "1 d ago")]
  [InlineData(9 * 86400, "9 d ago")]
  public void FormatAge_UsesCoarseTruncatedUnits(double seconds, string expected)
  {
    BandMapText.FormatAge(seconds).Should().Be(expected);
  }

  [Fact]
  public void FormatAge_NoMap_IsNull()
  {
    BandMapText.FormatAge(null).Should().BeNull();
  }

  [Theory]
  [InlineData(12.0, 0.4, "Discovering… 12 s")]
  [InlineData(11.2, 0.4, "Discovering… 12 s")]
  [InlineData(-1.0, 0.99, "Discovering… 0 s")]
  public void FormatDiscovering_PrefersTheSecondsEstimate(double remaining, double progress, string expected)
  {
    BandMapText.FormatDiscovering(remaining, progress).Should().Be(expected);
  }

  [Fact]
  public void FormatDiscovering_FallsBackToProgress_ThenToNothing()
  {
    BandMapText.FormatDiscovering(null, 0.4).Should().Be("Discovering… 40%");
    BandMapText.FormatDiscovering(null, 0).Should().Be("Discovering…");
  }

  [Fact]
  public void FormatMhz_OneDecimal()
  {
    BandMapText.FormatMhz(99_500_000).Should().Be("99.5");
    BandMapText.FormatMhz(107_900_000).Should().Be("107.9");
  }

  // ── AUD-91 ───────────────────────────────────────────────────────────────

  [Fact]
  public void FormatDiscovering_AnotherBand_NamesIt()
  {
    BandMapText.FormatDiscovering(12.0, 0.4, "AIR").Should().Be("Discovering AIR… 12 s");
    BandMapText.FormatDiscovering(null, 0.4, "AIR").Should().Be("Discovering AIR… 40%");
    BandMapText.FormatDiscovering(null, 0, "AIR").Should().Be("Discovering AIR…");
  }

  [Theory]
  [InlineData(99_500_000, "FM", "99.5")]
  [InlineData(162_475_000, "WB", "162.475")]
  [InlineData(118_025_000, "AIR", "118.025")]
  [InlineData(162_000_000, "VHF", "162.000")]
  [InlineData(162_412_500, "VHF", "162.4125")] // a 12.5 kHz channel needs a fourth decimal
  [InlineData(118_100_000, "AIR", "118.100")]
  [InlineData(1_010_000, "AM", "1010")]
  public void FormatFrequency_PerBand(double hz, string band, string expected) =>
    BandMapText.FormatFrequency(hz, band).Should().Be(expected);

  [Theory]
  [InlineData(99_500_000, "FM", "99.5 FM")]
  [InlineData(162_475_000, "WB", "162.475 MHz WB")]
  [InlineData(118_100_000, "AIR", "118.100 MHz AIR")]
  [InlineData(162_412_500, "VHF", "162.4125 MHz VHF")]
  [InlineData(1_010_000, "AM", "1010 kHz AM")]
  public void FormatStation_FmAsAud76_OtherBandsExactWithTheUnit(double hz, string band, string expected) =>
    BandMapText.FormatStation(hz, band).Should().Be(expected);

  [Theory]
  [InlineData(87_500_000L, 108_000_000L, "FM", "87.5–108 MHz")]
  [InlineData(145_520_000L, 147_520_000L, "VHF", "145.52–147.52 MHz")]
  [InlineData(161_500_000L, 163_500_000L, "VHF", "161.5–163.5 MHz")]
  [InlineData(530_000L, 1_710_000L, "AM", "530–1710 kHz")]
  [InlineData(108_000_000L, 137_000_000L, "AIR", "108–137 MHz")]
  public void FormatRange_EnDashAndUnit(long min, long max, string band, string expected) =>
    BandMapText.FormatRange(min, max, band).Should().Be(expected);
}
