using Radio.Infrastructure.Platform.Bluetooth;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth;

/// <summary>
/// AUD-11 Task 5: <see cref="LinuxBluetoothService.ParsePwLinkOutputForStreamPeers"/>.
/// </summary>
/// <remarks>
/// ⭐ The two single-line fixtures are real: they are the <c>pw-link -l</c> lines recorded on the
/// appliance during the AUD-11 incident, verbatim from docs/queue/AUD-11.md (the healthy binding and
/// the line-in it fell back to). The indented fixtures are the shape the existing in-tree pw-link
/// parsers (DisconnectAllLinksToPort) read; which shape the box's pw-link prints was not settled, so
/// both are pinned.
/// </remarks>
public class PwLinkPeerParsingTests
{
  private const string Stream = "radio-bt-stream";

  // Verbatim from docs/queue/AUD-11.md:18 and :21.
  private const string HealthyLine =
    "radio-bt-stream:input_FL  <- bluez_input.B0_D5_FB_D2_0D_68.2:output_FL   [active]";
  private const string DefectiveLine =
    "radio-bt-stream:input_FL  <- alsa_input.pci-0000_00_1f.3.analog-stereo:capture_FL   [active]";

  [Fact]
  public void Parses_TheHealthyBinding()
  {
    var peers = LinuxBluetoothService.ParsePwLinkOutputForStreamPeers(HealthyLine, Stream);
    Assert.Equal(new[] { "bluez_input.B0_D5_FB_D2_0D_68.2" }, peers);
  }

  [Fact]
  public void Parses_TheDefectiveBinding()
  {
    var peers = LinuxBluetoothService.ParsePwLinkOutputForStreamPeers(DefectiveLine, Stream);
    Assert.Equal(new[] { "alsa_input.pci-0000_00_1f.3.analog-stereo" }, peers);
  }

  [Fact]
  public void Parses_TheIndentedForm()
  {
    var output =
      "radio-bt-stream:input_FL\n"
      + "  |<- bluez_input.B0_D5_FB_D2_0D_68.2:output_FL\n"
      + "radio-bt-stream:input_FR\n"
      + "  |<- bluez_input.B0_D5_FB_D2_0D_68.2:output_FR\n";
    var peers = LinuxBluetoothService.ParsePwLinkOutputForStreamPeers(output, Stream);
    Assert.Equal(new[] { "bluez_input.B0_D5_FB_D2_0D_68.2" }, peers);
  }

  [Fact]
  public void StripsThePortSuffixAndTheActiveMarker()
  {
    var output = DefectiveLine + "\n"
      + "radio-bt-stream:input_FR\n"
      + "  |<- alsa_input.pci-0000_00_1f.3.analog-stereo:capture_FR [inactive]\n";
    var peers = LinuxBluetoothService.ParsePwLinkOutputForStreamPeers(output, Stream);
    var peer = Assert.Single(peers);
    Assert.DoesNotContain(":", peer);
    Assert.DoesNotContain("[", peer);
    Assert.Equal("alsa_input.pci-0000_00_1f.3.analog-stereo", peer);
  }

  [Fact]
  public void IgnoresOtherStreamsPorts()
  {
    var output =
      "pw-record:input_FL\n"
      + "  |<- alsa_input.usb-Generic_USB_Microphone_IM20000001-00.analog-stereo:capture_FL\n"
      + "chromium:input_FL  <- alsa_input.pci-0000_00_1f.3.analog-stereo:capture_FL   [active]\n"
      + HealthyLine + "\n"
      + "alsa_output.pci-0000_00_1f.3.analog-stereo:playback_FL\n"
      + "  |<- radio-api:output_FL\n";
    var peers = LinuxBluetoothService.ParsePwLinkOutputForStreamPeers(output, Stream);
    Assert.Equal(new[] { "bluez_input.B0_D5_FB_D2_0D_68.2" }, peers);
  }

  [Fact]
  public void BothChannelsFromOnePeer_CollapseToOne()
  {
    var output = HealthyLine + "\n"
      + "radio-bt-stream:input_FR  <- bluez_input.B0_D5_FB_D2_0D_68.2:output_FR   [active]\n";
    var peers = LinuxBluetoothService.ParsePwLinkOutputForStreamPeers(output, Stream);
    Assert.Single(peers);
  }

  [Fact]
  public void BothPeersAreReported_WhenTheStreamIsFedByTwoNodes()
  {
    var output = HealthyLine + "\n" + DefectiveLine.Replace("input_FL", "input_FR") + "\n";
    var peers = LinuxBluetoothService.ParsePwLinkOutputForStreamPeers(output, Stream);
    Assert.Equal(2, peers.Count);
  }

  [Theory]
  [InlineData("")]
  [InlineData("\n\n")]
  [InlineData("pw-link: failed to connect to pipewire")]
  [InlineData("radio-bt-stream:input_FL\n")] // our port, but nothing linked to it
  public void EmptyOrGarbageInput_ReturnsEmpty(string output)
  {
    // ⛔ The caller treats empty as a FAILED (unverified) audit, never a pass.
    Assert.Empty(LinuxBluetoothService.ParsePwLinkOutputForStreamPeers(output, Stream));
  }

  [Fact]
  public void NodeNamesContainingDots_SurviveIntact()
  {
    var peers = LinuxBluetoothService.ParsePwLinkOutputForStreamPeers(HealthyLine, Stream);
    // The trailing ".2" is part of the node name, not a suffix to strip.
    Assert.EndsWith(".2", Assert.Single(peers));
  }

  [Fact]
  public void CrLfLineEndings_AreTolerated()
  {
    var output = "radio-bt-stream:input_FL\r\n  |<- bluez_input.B0_D5_FB_D2_0D_68.2:output_FL\r\n";
    var peers = LinuxBluetoothService.ParsePwLinkOutputForStreamPeers(output, Stream);
    Assert.Equal(new[] { "bluez_input.B0_D5_FB_D2_0D_68.2" }, peers);
  }
}
