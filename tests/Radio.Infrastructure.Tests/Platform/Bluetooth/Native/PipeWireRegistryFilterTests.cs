using Radio.Infrastructure.Platform.Bluetooth.Native;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth.Native;

/// <summary>
/// Tests for <see cref="PipeWireRegistryFilter.TryExtractBtCaptureAddress"/>.
///
/// The filter is the only piece of the Plan E event-subscription path that
/// is purely value-level (no native daemon required). These tests lock in
/// its strict behaviour so future changes can't accidentally start
/// recognising HFP / malformed / non-BT nodes as A2DP sources.
/// </summary>
public class PipeWireRegistryFilterTests
{
  [Theory]
  [InlineData("bluez_input.78_20_51_F5_FB_A7.a2dp-source", "78:20:51:F5:FB:A7")]
  [InlineData("bluez_input.aa_bb_cc_dd_ee_ff.a2dp-source", "AA:BB:CC:DD:EE:FF")]
  [InlineData("bluez_input.D4_3A_2C_64_87_9E.a2dp-source", "D4:3A:2C:64:87:9E")]
  [InlineData("bluez_input.00_00_00_00_00_00.a2dp-source", "00:00:00:00:00:00")]
  public void TryExtract_ValidA2dpSourceNode_ReturnsTrueWithUppercaseColonAddress(
      string nodeName, string expectedAddress)
  {
    var ok = PipeWireRegistryFilter.TryExtractBtCaptureAddress(nodeName, out var address);
    Assert.True(ok, $"expected filter to recognise {nodeName}");
    Assert.Equal(expectedAddress, address);
  }

  [Theory]
  // Other PipeWire node families — must reject.
  [InlineData("alsa_input.usb-some-mic.analog-stereo")]
  [InlineData("alsa_output.pci-0000_00_1f.3.analog-stereo")]
  [InlineData("bluez_output.aa_bb_cc_dd_ee_ff.a2dp-sink")]
  // HFP profile (lives on the second adapter per the cross-service boundary) — must reject.
  [InlineData("bluez_input.78_20_51_F5_FB_A7.hfp-ag")]
  [InlineData("bluez_input.78_20_51_F5_FB_A7.hfp-hf")]
  // Malformed MAC bodies — must reject.
  [InlineData("bluez_input.too_short.a2dp-source")]
  [InlineData("bluez_input.78_20_51_F5_FB_A7_EXTRA.a2dp-source")]
  [InlineData("bluez_input.78_20_51_F5_FB.a2dp-source")] // five pairs
  [InlineData("bluez_input.GG_HH_II_JJ_KK_LL.a2dp-source")] // non-hex
  [InlineData("bluez_input.78-20-51-F5-FB-A7.a2dp-source")] // dashes not underscores
  [InlineData("bluez_input.7820_51_F5_FB_A7_X.a2dp-source")] // wrong separator positions
  // Empty / nonsense input — must reject.
  [InlineData("")]
  [InlineData("bluez_input.")]
  [InlineData("bluez_input.a2dp-source")]
  [InlineData("some-other-node")]
  public void TryExtract_InvalidNode_ReturnsFalseAndEmptyAddress(string nodeName)
  {
    var ok = PipeWireRegistryFilter.TryExtractBtCaptureAddress(nodeName, out var address);
    Assert.False(ok, $"filter should reject {nodeName}");
    Assert.Equal(string.Empty, address);
  }

  [Fact]
  public void TryExtract_NullNodeName_ReturnsFalse()
  {
    var ok = PipeWireRegistryFilter.TryExtractBtCaptureAddress(null!, out var address);
    Assert.False(ok);
    Assert.Equal(string.Empty, address);
  }

  [Fact]
  public void TryExtract_LowercaseMac_NormalisesToUppercase()
  {
    var ok = PipeWireRegistryFilter.TryExtractBtCaptureAddress(
      "bluez_input.de_ad_be_ef_ca_fe.a2dp-source", out var address);
    Assert.True(ok);
    Assert.Equal("DE:AD:BE:EF:CA:FE", address);
  }
}
