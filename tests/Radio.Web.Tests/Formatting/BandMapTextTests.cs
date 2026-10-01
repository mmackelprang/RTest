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
  [InlineData(12.0, 0.4, "Scanning… 12 s")]
  [InlineData(11.2, 0.4, "Scanning… 12 s")]
  [InlineData(-1.0, 0.99, "Scanning… 0 s")]
  public void FormatScanning_PrefersTheSecondsEstimate(double remaining, double progress, string expected)
  {
    BandMapText.FormatScanning(remaining, progress).Should().Be(expected);
  }

  [Fact]
  public void FormatScanning_FallsBackToProgress_ThenToNothing()
  {
    BandMapText.FormatScanning(null, 0.4).Should().Be("Scanning… 40%");
    BandMapText.FormatScanning(null, 0).Should().Be("Scanning…");
  }

  [Fact]
  public void FormatMhz_OneDecimal()
  {
    BandMapText.FormatMhz(99_500_000).Should().Be("99.5");
    BandMapText.FormatMhz(107_900_000).Should().Be("107.9");
  }

  // ── AUD-91 ───────────────────────────────────────────────────────────────

  [Fact]
  public void FormatScanning_AnotherBand_NamesIt()
  {
    BandMapText.FormatScanning(12.0, 0.4, "AIR").Should().Be("Scanning AIR… 12 s");
    BandMapText.FormatScanning(null, 0.4, "AIR").Should().Be("Scanning AIR… 40%");
    BandMapText.FormatScanning(null, 0, "AIR").Should().Be("Scanning AIR…");
  }

  [Theory]
  [InlineData(99_500_000, "FM", "99.5")]
  [InlineData(162_475_000, "WB", "162.475")]
  [InlineData(118_025_000, "AIR", "118.025")]
  [InlineData(162_000_000, "VHF", "162.000")]
  [InlineData(1_010_000, "AM", "1010")]
  public void FormatFrequency_PerBand(double hz, string band, string expected) =>
    BandMapText.FormatFrequency(hz, band).Should().Be(expected);
}
