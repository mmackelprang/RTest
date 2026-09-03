using Radio.Core.Configuration;

namespace Radio.Core.Tests.Configuration;

/// <summary>
/// Pins the cabinet engraving (ENC-8 Task 1).
///
/// <para>
/// These names are a fact about a drilled escutcheon, not a software mapping, so they must not be
/// derived from the router's index order. Since ENC-5 the two agree everywhere except index 2,
/// where the visualiser sits until ENC-7 — and the point of keeping them separate is that this
/// list does not move when that one does.
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
