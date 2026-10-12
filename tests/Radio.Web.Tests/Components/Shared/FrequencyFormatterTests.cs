using FluentAssertions;
using Radio.Web.Components.Shared;
using Xunit;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>AUD-102: <see cref="FrequencyFormatter"/> shows AM and SW in kHz and every other band in MHz.</summary>
public class FrequencyFormatterTests
{
  [Theory]
  [InlineData(5_905_000d, "SW", "5905 kHz")]
  [InlineData(5_900_000d, "SW", "5900 kHz")]
  [InlineData(5_910_000d, "SW", "5910 kHz")]
  [InlineData(5_907_500d, "SW", "5907.5 kHz")]
  [InlineData(1_010_000d, "AM", "1010 kHz")]
  [InlineData(101_500_000d, "FM", "101.50 MHz")]
  [InlineData(162_550_000d, "WB", "162.55 MHz")] // MHz bands unchanged by AUD-102
  public void FormatFrequency_PerBand(double hz, string band, string expected) =>
    FrequencyFormatter.FormatFrequency(hz, band).Should().Be(expected);

  [Fact]
  public void SwFiveKilohertzApart_AreThreeDistinctReadouts()
  {
    new[] { 5_900_000d, 5_905_000d, 5_910_000d }
      .Select(hz => FrequencyFormatter.FormatFrequency(hz, "SW"))
      .Should().OnlyHaveUniqueItems();
  }

  [Theory]
  [InlineData("SW", " kHz")]
  [InlineData("AM", " kHz")]
  [InlineData("FM", " MHz")]
  [InlineData("AIR", " MHz")]
  public void FrequencyUnit_PerBand(string band, string expected) =>
    FrequencyFormatter.FrequencyUnit(band).Should().Be(expected);

  [Theory]
  [InlineData("SW", 5_905_000d, 5905d)]
  [InlineData("AM", 1_010_000d, 1010d)]
  [InlineData("FM", 101_500_000d, 101.5d)]
  public void DialogUnits_RoundTrip(string band, double hz, double display)
  {
    FrequencyFormatter.ToDisplayUnits(hz, band).Should().BeApproximately(display, 1e-9);
    FrequencyFormatter.FromDisplayUnits(display, band).Should().BeApproximately(hz, 1e-3);
  }

  [Theory]
  [InlineData("SW", "Frequency (kHz)")]
  [InlineData("AM", "Frequency (kHz)")]
  [InlineData("FM", "Frequency (MHz)")]
  [InlineData(null, "Frequency (MHz)")]
  public void DialogPlaceholder_PerBand(string? band, string expected) =>
    FrequencyFormatter.DialogPlaceholder(band).Should().Be(expected);

  [Fact]
  public void DialogStep_OnSw_IsInKhz() =>
    FrequencyFormatter.GetDialogStep(5_000, "SW").Should().Be(5);

  [Theory]
  [InlineData("AM", 530d, 1710d)]
  [InlineData("SW", 1711d, 30_000d)]
  [InlineData("FM", 87.5d, 108d)]
  [InlineData("AIR", 108d, 137d)]
  [InlineData("WB", 162.4d, 162.55d)]
  [InlineData("VHF", 30d, 300d)]
  public void Fallbacks_MatchTheBandPresets_InDisplayUnits(string band, double min, double max)
  {
    // The fallbacks apply when the band list could not be read. They were stale before AUD-102
    // (SW 1.8 MHz, VHF 136–174 MHz, AM 520 kHz).
    RTLSDRCore.Models.RadioBand preset = RTLSDRCore.Bands.BandPresets.AllBands
      .Single(b => Code(b.Type) == band);
    FrequencyFormatter.GetMinFrequency(band, null).Should().BeApproximately(min, 1e-9);
    FrequencyFormatter.GetMaxFrequency(band, null).Should().BeApproximately(max, 1e-9);
    FrequencyFormatter.FromDisplayUnits(min, band).Should().BeApproximately(preset.MinFrequencyHz, 1e-3);
    FrequencyFormatter.FromDisplayUnits(max, band).Should().BeApproximately(preset.MaxFrequencyHz, 1e-3);
  }

  [Fact]
  public void MinAndMax_FromTheBandList_AreInTheBandsUnit()
  {
    var sw = new Radio.Core.Models.RadioBandModel { Type = "SW", MinFrequencyHz = 1_711_000, MaxFrequencyHz = 30_000_000 };

    FrequencyFormatter.GetMinFrequency("SW", new[] { sw }).Should().Be(1711);
    FrequencyFormatter.GetMaxFrequency("SW", new[] { sw }).Should().Be(30_000);
  }

  private static string Code(RTLSDRCore.Enums.BandType type) => RTLSDRCore.Sweep.BandSweepPlans.BandCode(type);
}
