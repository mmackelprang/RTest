using Radio.Infrastructure.Services;

namespace Radio.Infrastructure.Tests.Services;

/// <summary>
/// Locks down PR 3 of the Radio Controller Polish arc — the server-side
/// projection of <c>RadioBandModel.Range</c> (human-formatted sub-range
/// string for the tall band pills) and <c>RadioBandModel.BandPresetCapacity</c>
/// (per-band memory-slot count). Tests target the static helpers exposed via
/// <c>internal</c> + <c>InternalsVisibleTo</c> so the formatting logic is
/// covered without needing to spin up the full <c>BandPresets</c> reflection
/// path.
/// </summary>
public class RadioBandServiceTests
{
  [Theory]
  [InlineData("FM", 87_500_000, 108_000_000, "87.5–108 MHz")]
  [InlineData("AM", 530_000, 1_700_000, "530–1700 kHz")]
  [InlineData("WB", 162_400_000, 162_550_000, "162.4–162.55 MHz")]
  [InlineData("VHF", 136_000_000, 174_000_000, "136–174 MHz")]
  [InlineData("AIR", 108_000_000, 137_000_000, "108–137 MHz")]
  [InlineData("SW", 1_800_000, 30_000_000, "1.8–30 MHz")]
  public void FormatRange_ReturnsExpectedString(string band, long minHz, long maxHz, string expected)
  {
    Assert.Equal(expected, RadioBandService.FormatRange(band, minHz, maxHz));
  }

  [Fact]
  public void FormatRange_AmUsesKhz_NotMhz()
  {
    // AM is the only band that prints in kHz — guard against a regression
    // that would render "AM 0.53–1.7 MHz".
    var range = RadioBandService.FormatRange("AM", 520_000, 1_710_000);
    Assert.EndsWith("kHz", range);
    Assert.DoesNotContain("MHz", range);
  }

  [Fact]
  public void FormatRange_UsesEnDash_NotHyphen()
  {
    // Canvas mock uses the typographic en-dash (U+2013) between min/max.
    // This is the same character the canvas mock uses in its band pill copy.
    var range = RadioBandService.FormatRange("FM", 87_500_000, 108_000_000);
    Assert.Contains("–", range);
    // ASCII hyphen-minus must NOT appear between numbers — only the en-dash
    // separator. (A hyphen WOULD be fine if it were part of a unit string,
    // but the format string never produces one.)
    Assert.DoesNotContain("-", range);
  }

  [Theory]
  [InlineData("FM", 16)]
  [InlineData("AM", 16)]
  [InlineData("SW", 16)]
  [InlineData("VHF", 16)]
  [InlineData("AIR", 16)]
  [InlineData("WB", 4)]
  [InlineData("UNKNOWN-BAND", 16)]   // default fallback
  public void ResolveCapacity_ReturnsExpectedCapacity(string bandCode, int expected)
  {
    Assert.Equal(expected, RadioBandService.ResolveCapacity(bandCode));
  }

  [Fact]
  public void ResolveCapacity_IsCaseInsensitive()
  {
    Assert.Equal(4, RadioBandService.ResolveCapacity("wb"));
    Assert.Equal(4, RadioBandService.ResolveCapacity("Wb"));
  }

  [Fact]
  public void GetAvailableBands_PopulatesRangeAndCapacityForKnownBands()
  {
    // Integration-style: run the full reflection-based projection, then
    // confirm the new PR 3 fields are populated for the well-known FM band.
    // Tester's PR 2 retrospective flagged that pure unit tests had missed a
    // wire-path bug; exercising the real projection path here mirrors what
    // ships to the Web layer.
    var service = new RadioBandService();
    var bands = service.GetAvailableBands().ToList();

    var fm = bands.FirstOrDefault(b => b.Type == "FM");
    Assert.NotNull(fm);
    Assert.False(string.IsNullOrEmpty(fm!.Range), "FM band should have a non-empty Range");
    Assert.Contains("MHz", fm.Range);
    Assert.Equal(16, fm.BandPresetCapacity);

    var wb = bands.FirstOrDefault(b => b.Type == "WB");
    if (wb != null)
    {
      // WB is in the BandPresets reflection set on most builds — when
      // present, the capacity must be 4.
      Assert.Equal(4, wb.BandPresetCapacity);
    }
  }
}
