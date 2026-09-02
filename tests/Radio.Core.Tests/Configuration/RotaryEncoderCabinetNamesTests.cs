using Radio.Core.Configuration;

namespace Radio.Core.Tests.Configuration;

/// <summary>
/// Pins the cabinet engraving (ENC-8 Task 1).
///
/// <para>
/// These names are a fact about a drilled escutcheon, not a software mapping, so they must not be
/// derived from the router's index order — the two deliberately disagree on indices 1-3 until
/// ENC-5 / ENC-7 land.
/// </para>
/// </summary>
public class RotaryEncoderCabinetNamesTests
{
  [Fact]
  public void CabinetNames_PutVolumeAtIndexZero_WhichIsTheOneIndexTheRouterAlsoAgreesOn()
  {
    Assert.Equal("VOLUME", RotaryEncoderCabinetNames.For(RotaryEncoderConfigDefaults.VolumeEncoderIndex));
    Assert.Equal(RotaryEncoderDeviceConfig.EncoderCount, RotaryEncoderCabinetNames.Ordered.Count);
  }

  [Fact]
  public void CabinetNames_DoNotThrowForAnIndexOffTheFace()
  {
    Assert.Equal("KNOB 9", RotaryEncoderCabinetNames.For(9));
  }
}
