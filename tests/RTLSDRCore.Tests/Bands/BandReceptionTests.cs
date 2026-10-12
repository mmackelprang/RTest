using RTLSDRCore.Bands;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using RTLSDRCore.Sweep;
using Xunit;

namespace RTLSDRCore.Tests.Bands;

/// <summary>
/// AUD-102: <see cref="BandReception.UnavailableReason"/>, the one function behind the band list's
/// receivable flag and the band map's reason.
/// </summary>
public class BandReceptionTests
{
  [Fact]
  public void BandMapsReason_IsUnchanged_ByTheMoveIntoTheSharedFunction()
  {
    // The exact sentence the BAND view showed before AUD-102 (VisualizerPanelBandTests.AmReason).
    Assert.Equal(
      "The AM Broadcast band (530–1,710 kHz) is below this tuner's 24 MHz lower limit and cannot be scanned.",
      BandSweepPlans.UnavailableReason(BandType.AM));
    Assert.Equal("This band has no channel plan and cannot be scanned.", BandSweepPlans.UnavailableReason(BandType.Custom));
  }

  [Theory]
  [InlineData(BandType.AM)]
  [InlineData(BandType.FM)]
  [InlineData(BandType.Shortwave)]
  [InlineData(BandType.Aircraft)]
  [InlineData(BandType.Weather)]
  [InlineData(BandType.VHF)]
  public void HfDevice_ReceivesEveryBand(BandType band)
  {
    Assert.Null(BandReception.UnavailableReason(band, HfDirectSampling.HfMinFrequencyHz, RtlSdrDevice.TunerMaxFrequencyHz));
  }

  [Fact]
  public void DeviceWithoutHf_CannotReceiveAmOrSw_AndSaysWhyLikeTheBandMap()
  {
    long min = RtlSdrDevice.TunerMinFrequencyHz;
    long max = RtlSdrDevice.TunerMaxFrequencyHz;

    Assert.Equal(
      "The AM Broadcast band (530–1,710 kHz) is below this tuner's 24 MHz lower limit and cannot be received.",
      BandReception.UnavailableReason(BandType.AM, min, max));
    Assert.Equal(
      "The Shortwave band (1.711–30 MHz) is mostly below this tuner's 24 MHz lower limit and cannot be received.",
      BandReception.UnavailableReason(BandType.Shortwave, min, max));
    Assert.Null(BandReception.UnavailableReason(BandType.FM, min, max));
    Assert.Null(BandReception.UnavailableReason(BandType.VHF, min, max));
  }

  [Fact]
  public void ABandPartlyAboveTheDevice_IsNotReceivable()
  {
    string? reason = BandReception.UnavailableReason(BandType.VHF, HfDirectSampling.HfMinFrequencyHz, 200_000_000);

    Assert.Equal(
      "The VHF band (30–300 MHz) extends above this tuner's 200 MHz upper limit and cannot be received.", reason);
  }

  [Fact]
  public void ABandPartlyBelowTheDevice_IsPartly()
  {
    // VHF 30–300 MHz on a device whose floor is 40 MHz: 10 MHz below, 260 above.
    Assert.Contains("is partly below this tuner's 40 MHz lower limit",
      BandReception.UnavailableReason(BandType.VHF, 40_000_000, RtlSdrDevice.TunerMaxFrequencyHz));
  }
}
