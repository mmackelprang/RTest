using Radio.Core.Models.Audio;
using Xunit;

namespace Radio.Core.Tests.Models.Audio;

/// <summary>
/// AUD-102: <see cref="BandUnits"/>, the one kHz/MHz rule, and <see cref="RadioPreset.GetDefaultName"/>,
/// which names an unnamed preset with it from hertz.
/// </summary>
public class BandUnitsTests
{
  [Theory]
  [InlineData("AM", true)]
  [InlineData("am", true)]
  [InlineData("SW", true)]
  [InlineData("Sw", true)]
  [InlineData("FM", false)]
  [InlineData("WB", false)]
  [InlineData("VHF", false)]
  [InlineData("AIR", false)]
  [InlineData("", false)]
  [InlineData(null, false)]
  public void IsKhz_ByCode_IsAmOrSw(string? band, bool expected) =>
    Assert.Equal(expected, BandUnits.IsKhz(band));

  [Theory]
  [InlineData(RadioBand.AM, true)]
  [InlineData(RadioBand.SW, true)]
  [InlineData(RadioBand.FM, false)]
  [InlineData(RadioBand.WB, false)]
  [InlineData(RadioBand.VHF, false)]
  [InlineData(RadioBand.AIR, false)]
  public void IsKhz_ByEnum_IsAmOrSw(RadioBand band, bool expected) =>
    Assert.Equal(expected, BandUnits.IsKhz(band));

  [Fact]
  public void IsKhz_ByCodeAndByEnum_AgreeForEveryBand()
  {
    foreach (RadioBand band in Enum.GetValues<RadioBand>())
    {
      Assert.Equal(BandUnits.IsKhz(band), BandUnits.IsKhz(band.ToString()));
    }
  }

  [Theory]
  [InlineData(RadioBand.SW, 5_905_000L, "5905 kHz")]
  [InlineData(RadioBand.SW, 5_900_000L, "5900 kHz")]
  [InlineData(RadioBand.SW, 5_910_000L, "5910 kHz")]
  [InlineData(RadioBand.SW, 5_907_500L, "5907.5 kHz")]
  [InlineData(RadioBand.AM, 1_010_000L, "1010 kHz")] // ToDisplayString alone printed "1.0 MHz"
  [InlineData(RadioBand.AM, 710_000L, "710 kHz")]
  [InlineData(RadioBand.FM, 101_500_000L, "101.5 MHz")]
  [InlineData(RadioBand.VHF, 146_520_000L, "146.5 MHz")] // unchanged: ToDisplayString's MHz form
  public void Format_UsesTheBandsUnit(RadioBand band, long hz, string expected) =>
    Assert.Equal(expected, BandUnits.Format(band, new Frequency(hz)));

  [Fact]
  public void Format_SwFiveKilohertzApart_AreDistinct()
  {
    // The collision AUD-102 removes: in MHz with two decimals these were 5.90, 5.91, 5.91.
    string[] shown = new[] { 5_900_000L, 5_905_000L, 5_910_000L }
      .Select(hz => BandUnits.Format(RadioBand.SW, new Frequency(hz)))
      .ToArray();

    Assert.Equal(3, shown.Distinct().Count());
  }

  [Theory]
  [InlineData(RadioBand.AM, 1_010_000d, "AM - 1010")]
  [InlineData(RadioBand.SW, 5_905_000d, "SW - 5905")]
  [InlineData(RadioBand.SW, 5_907_500d, "SW - 5907.5")]
  [InlineData(RadioBand.FM, 101_500_000d, "FM - 101.5")]
  [InlineData(RadioBand.FM, 99_900_000d, "FM - 99.9")]
  [InlineData(RadioBand.WB, 162_475_000d, "WB - 162.475")]
  [InlineData(RadioBand.AIR, 118_100_000d, "AIR - 118.100")]
  [InlineData(RadioBand.VHF, 146_512_500d, "VHF - 146.5125")]
  public void GetDefaultName_TakesHertz_AndNamesInTheBandsUnit(RadioBand band, double hz, string expected) =>
    Assert.Equal(expected, RadioPreset.GetDefaultName(band, hz));

  [Fact]
  public void GetDefaultName_NeverPrintsRawHertz()
  {
    // Before AUD-102 a hertz value printed raw: "AM - 1000000", "SW - 5900000.00".
    Assert.Equal("AM - 1000", RadioPreset.GetDefaultName(RadioBand.AM, 1_000_000));
    Assert.Equal("SW - 5900", RadioPreset.GetDefaultName(RadioBand.SW, 5_900_000));
  }
}
