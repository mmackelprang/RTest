using Moq;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Services;
using RTLSDRCore.Bands;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Sweep;

namespace Radio.Infrastructure.Tests.Services;

/// <summary>
/// Locks down PR 3 of the Radio Controller Polish arc — the server-side
/// projection of <c>RadioBandModel.Range</c> (human-formatted sub-range
/// string for the tall band pills) and <c>RadioBandModel.BandPresetCapacity</c>
/// (per-band preset-slot count). Tests target the static helpers exposed via
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
  [InlineData("SW", 1_711_000, 30_000_000, "1711–30000 kHz")] // AUD-102: SW is in kHz like AM
  public void FormatRange_ReturnsExpectedString(string band, long minHz, long maxHz, string expected)
  {
    Assert.Equal(expected, RadioBandService.FormatRange(band, minHz, maxHz));
  }

  [Fact]
  public void FormatRange_AmUsesKhz_NotMhz()
  {
    // AM and SW print in kHz (AUD-102) — guard against a regression
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

  // ── AUD-102: the receivable flag ─────────────────────────────────────────

  private static SdrHardwareInfo Device(long minHz, bool inUse = false, int index = 0) => new(
    index, "Generic RTL2832U", "Realtek", "NESDR SMArt v5", "00000001", "R820T",
    minHz < RtlSdrDevice.TunerMinFrequencyHz ? "Q" : "Off", "Auto", false, inUse,
    minHz, 1_766_000_000);

  private static List<Radio.Core.Models.RadioBandModel> BandsFor(params SdrHardwareInfo[] hardware)
  {
    var factory = new Mock<IRadioFactory>();
    factory.Setup(f => f.GetSdrHardware()).Returns(hardware);
    return new RadioBandService(factory.Object).GetAvailableBands().ToList();
  }

  [Fact]
  public void GetAvailableBands_HfDevice_EveryBandIsReceivable()
  {
    // An HF-capable dongle (AUD-99: 100 kHz floor) receives all six bands.
    var bands = BandsFor(Device(HfDirectSampling.HfMinFrequencyHz));

    Assert.Equal(6, bands.Count);
    Assert.All(bands, b =>
    {
      Assert.True(b.IsReceivable, b.Type);
      Assert.Null(b.UnavailableReason);
    });
  }

  [Fact]
  public void GetAvailableBands_DeviceWithoutHf_AmAndSwAreNotReceivable_WithTheBandMapsSentence()
  {
    var bands = BandsFor(Device(RtlSdrDevice.TunerMinFrequencyHz));

    var am = bands.Single(b => b.Type == "AM");
    var sw = bands.Single(b => b.Type == "SW");
    Assert.False(am.IsReceivable);
    Assert.False(sw.IsReceivable);
    Assert.Equal(
      "The AM Broadcast band (530–1,710 kHz) is below this tuner's 24 MHz lower limit and cannot be received.",
      am.UnavailableReason);
    Assert.Equal(
      "The Shortwave band (1.711–30 MHz) is mostly below this tuner's 24 MHz lower limit and cannot be received.",
      sw.UnavailableReason);

    // The same sentence the band map shows today, but for its last two words.
    Assert.Equal(
      BandSweepPlans.UnavailableReason(BandType.AM)!.Replace("cannot be scanned.", "cannot be received.", StringComparison.Ordinal),
      am.UnavailableReason);

    Assert.All(bands.Where(b => b.Type is not ("AM" or "SW")), b =>
    {
      Assert.True(b.IsReceivable, b.Type);
      Assert.Null(b.UnavailableReason);
    });
  }

  [Fact]
  public void GetAvailableBands_PrefersTheDeviceInUse()
  {
    // Device 0 has no HF; device 1 is the radio's and has it. The radio's device decides.
    var bands = BandsFor(
      Device(RtlSdrDevice.TunerMinFrequencyHz, inUse: false, index: 0),
      Device(HfDirectSampling.HfMinFrequencyHz, inUse: true, index: 1));

    Assert.True(bands.Single(b => b.Type == "AM").IsReceivable);
  }

  [Fact]
  public void GetAvailableBands_NoDevice_EveryBandIsReceivable()
  {
    // No factory (tests, dev box) and a factory that enumerates nothing both mean "no device", and
    // with no device there is no limit to report.
    Assert.All(new RadioBandService().GetAvailableBands(), b => Assert.True(b.IsReceivable, b.Type));
    Assert.All(BandsFor(), b =>
    {
      Assert.True(b.IsReceivable, b.Type);
      Assert.Null(b.UnavailableReason);
    });
  }

  [Fact]
  public void GetAvailableBands_FactoryThrows_StillServesTheBands()
  {
    var factory = new Mock<IRadioFactory>();
    factory.Setup(f => f.GetSdrHardware()).Throws(new InvalidOperationException("librtlsdr missing"));

    var bands = new RadioBandService(factory.Object).GetAvailableBands().ToList();

    Assert.Equal(6, bands.Count);
    Assert.All(bands, b => Assert.True(b.IsReceivable, b.Type));
  }

  [Fact]
  public void GetAvailableBands_SwRangeIsInKhz()
  {
    var sw = new RadioBandService().GetAvailableBands().Single(b => b.Type == "SW");

    Assert.Equal("1711–30000 kHz", sw.Range);
    Assert.Equal(1_711_000, sw.MinFrequencyHz);
  }
}
