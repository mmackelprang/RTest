using Radio.Infrastructure.Platform.Bluetooth.Native;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth.Native;

/// <summary>
/// Tests for <see cref="PipeWireRegistryFilter.TryExtractBtCaptureAddress"/> — AUD-10 T1.
/// </summary>
/// <remarks>
/// ⚠ AUD-10: the fixtures here used to be an invented <c>bluez_input.&lt;MAC&gt;.a2dp-source</c>
/// shape, written to match the filter rather than the box, and the filter was correct
/// against a naming scheme the appliance never produces — so the registry listener never
/// delivered an event in production while this file stayed green. Every ACCEPTED fixture
/// below is a node name measured on real hardware:
/// <list type="bullet">
///   <item><c>bluez_input.B0_D5_FB_D2_0D_68.2</c> — the owner's phone on <c>radio</c>,
///   2026-09-10 and 2026-09-25 (docs/queue/AUD-10.md, AUD-11.md).</item>
///   <item><c>bluez_input.D4_3A_2C_64_87_9E.0</c> — from PipeWireNodeParsingTests, the scrape
///   that has always worked.</item>
/// </list>
/// No <c>.a2dp-source</c> case is kept: no source in this repository says any PipeWire version
/// emits that name (plan AUD-10 Task A).
/// </remarks>
public class PipeWireRegistryFilterTests
{
  [Theory]
  [InlineData("bluez_input.B0_D5_FB_D2_0D_68.2", "B0:D5:FB:D2:0D:68")]
  [InlineData("bluez_input.D4_3A_2C_64_87_9E.0", "D4:3A:2C:64:87:9E")]
  // The bare prefix form — FindPipeWireBluetoothNodeAsync's prefix match accepts it too.
  [InlineData("bluez_input.B0_D5_FB_D2_0D_68", "B0:D5:FB:D2:0D:68")]
  public void TryExtract_MeasuredNodeName_ReturnsTrueWithUppercaseColonAddress(
      string nodeName, string expectedAddress)
  {
    var ok = PipeWireRegistryFilter.TryExtractBtCaptureAddress(nodeName, out var address);
    Assert.True(ok, $"expected filter to recognise {nodeName}");
    Assert.Equal(expectedAddress, address);
  }

  [Theory]
  // Other PipeWire node families (other prefixes) — must reject.
  [InlineData("alsa_input.pci-0000_00_1f.3.analog-stereo")] // the line-in AUD-11 fell back to
  [InlineData("alsa_output.pci-0000_00_1f.3.analog-stereo")]
  [InlineData("bluez_output.B0_D5_FB_D2_0D_68.1")]
  [InlineData("radio-bt-stream")]
  // HFP profile suffixes — must reject.
  [InlineData("bluez_input.B0_D5_FB_D2_0D_68.hfp-ag")]
  [InlineData("bluez_input.B0_D5_FB_D2_0D_68.hfp-hf")]
  // Malformed MAC bodies — must reject.
  [InlineData("bluez_input.too_short.2")]
  [InlineData("bluez_input.B0_D5_FB_D2_0D_68_EXTRA.2")]  // six pairs plus a seventh token
  [InlineData("bluez_input.B0_D5_FB_D2_0D.2")]           // five pairs
  [InlineData("bluez_input.GG_HH_II_JJ_KK_LL.2")]        // non-hex
  [InlineData("bluez_input.B0-D5-FB-D2-0D-68.2")]        // dashes not underscores
  [InlineData("bluez_input.B0D5_FB_D2_0D_68_X.2")]       // wrong separator positions
  // A '.' with nothing after it, or a non-'.' separator after the MAC — must reject.
  [InlineData("bluez_input.B0_D5_FB_D2_0D_68.")]
  [InlineData("bluez_input.B0_D5_FB_D2_0D_68-2")]
  // Empty / nonsense input — must reject.
  [InlineData("")]
  [InlineData("bluez_input.")]
  [InlineData("bluez_input.2")]
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
      "bluez_input.b0_d5_fb_d2_0d_68.2", out var address);
    Assert.True(ok);
    Assert.Equal("B0:D5:FB:D2:0D:68", address);
  }

  [Fact]
  public void TryExtract_DifferentSuffixesOfTheSameDevice_YieldTheSameAddress()
  {
    // The recreated node's suffix is NOT established to be stable (plan AUD-10 §0.3) —
    // two sightings, both .2. The filter must identify the DEVICE regardless of it.
    Assert.True(PipeWireRegistryFilter.TryExtractBtCaptureAddress(
      "bluez_input.B0_D5_FB_D2_0D_68.2", out var a));
    Assert.True(PipeWireRegistryFilter.TryExtractBtCaptureAddress(
      "bluez_input.B0_D5_FB_D2_0D_68.3", out var b));
    Assert.Equal(a, b);
  }
}
