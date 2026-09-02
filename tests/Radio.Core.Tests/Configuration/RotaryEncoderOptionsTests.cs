using Radio.Core.Configuration;

namespace Radio.Core.Tests.Configuration;

/// <summary>
/// Guards ENC-0's change to <see cref="RotaryEncoderOptions.Enabled"/>.
///
/// <para>
/// The default flipped from false to true and the flag changed meaning: it was a gate that had to be
/// opened before the subsystem would run, and is now an escape hatch for switching off a misbehaving
/// encoder. Flipping it back would silently disable knob input on an appliance whose knobs are
/// drilled into the furniture, and nothing else would complain — presence detection is deliberately
/// silent when the flag is off.
/// </para>
/// </summary>
public class RotaryEncoderOptionsTests
{
  [Fact]
  public void Enabled_DefaultsToTrue_BecausePresenceDecidesNotConfiguration()
  {
    Assert.True(new RotaryEncoderOptions().Enabled);
  }

  [Fact]
  public void VendorAndProductId_MatchTheShippedDevice()
  {
    // 0xCAFE:0x4005 - read off the live device's descriptor, not copied from a doc.
    var options = new RotaryEncoderOptions();

    Assert.Equal(0xCAFE, options.VendorId);
    Assert.Equal(0x4005, options.ProductId);
  }
}
