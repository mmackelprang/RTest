namespace Radio.Infrastructure.Tests.Platform.Bluetooth;

/// <summary>
/// Tests for LinuxBluetoothService.ParsePwCliOutputForBtNode.
/// </summary>
public class PipeWireNodeParsingTests
{
  private const string Prefix = "bluez_input.D4_3A_2C_64_87_9E";

  private static string NodeBlock(int id, int serial, string nodeName)
  {
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"id {id}, type PipeWire:Interface:Node/3");
    if (serial > 0) sb.AppendLine($"  object.serial = \"{serial}\"");
    sb.Append($"  node.name = \"{nodeName}\"");
    return sb.ToString();
  }

  private static string NodeBlockNameFirst(int id, string nodeName, int serial)
  {
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"id {id}, type PipeWire:Interface:Node/3");
    sb.AppendLine($"  node.name = \"{nodeName}\"");
    sb.Append($"  object.serial = \"{serial}\"");
    return sb.ToString();
  }

  private static string NodeBlockNoSerial(int id, string nodeName)
  {
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"id {id}, type PipeWire:Interface:Node/3");
    sb.Append($"  node.name = \"{nodeName}\"");
    return sb.ToString();
  }

  private static string DeviceBlock(int id, int serial, string deviceName)
  {
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"id {id}, type PipeWire:Interface:Device/3");
    sb.AppendLine($"  object.serial = \"{serial}\"");
    sb.Append($"  device.name = \"{deviceName}\"");
    return sb.ToString();
  }

  private static string Join(params string[] blocks) => string.Join("\n", blocks);

  [Fact]
  public void SerialBeforeName_ReturnsCorrectResult()
  {
    var output = NodeBlock(68, 142, "bluez_input.D4_3A_2C_64_87_9E.0");

    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode(output, Prefix);

    Assert.Equal("bluez_input.D4_3A_2C_64_87_9E.0", name);
    Assert.Equal(68, id);
    Assert.Equal(142, serial);
  }

  [Fact]
  public void SerialAfterName_ReturnsCorrectResult()
  {
    var output = NodeBlockNameFirst(68, "bluez_input.D4_3A_2C_64_87_9E.0", 142);

    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode(output, Prefix);

    Assert.Equal("bluez_input.D4_3A_2C_64_87_9E.0", name);
    Assert.Equal(68, id);
    Assert.Equal(142, serial);
  }

  [Fact]
  public void DeviceBeforeNode_DoesNotLeakDeviceSerial()
  {
    var output = Join(
        DeviceBlock(50, 999, "alsa_card.usb-Generic_USB_Microphone"),
        NodeBlock(68, 142, "bluez_input.D4_3A_2C_64_87_9E.0"));

    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode(output, Prefix);

    Assert.Equal("bluez_input.D4_3A_2C_64_87_9E.0", name);
    Assert.Equal(68, id);
    Assert.Equal(142, serial);
    Assert.NotEqual(999, serial);
  }

  [Fact]
  public void DeviceAfterNode_DoesNotAffectResult()
  {
    var output = Join(
        NodeBlock(68, 142, "bluez_input.D4_3A_2C_64_87_9E.0"),
        DeviceBlock(50, 999, "alsa_card.usb-Generic_USB_Microphone"));

    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode(output, Prefix);

    Assert.Equal(142, serial);
    Assert.Equal(68, id);
  }

  [Fact]
  public void MultipleNodes_MatchesCorrectNode()
  {
    var output = Join(
        NodeBlock(40, 100, "alsa_output.pci-0000_00_1f.3.analog-stereo"),
        NodeBlock(68, 142, "bluez_input.D4_3A_2C_64_87_9E.0"),
        NodeBlock(72, 200, "alsa_input.usb-Generic_USB_Microphone"));

    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode(output, Prefix);

    Assert.Equal("bluez_input.D4_3A_2C_64_87_9E.0", name);
    Assert.Equal(68, id);
    Assert.Equal(142, serial);
  }

  [Fact]
  public void InterleavedDeviceAndNode_NoCrossObjectLeakage()
  {
    var output = Join(
        NodeBlock(45, 80, "alsa_output.pci-0000_00_1f.3.analog-stereo"),
        DeviceBlock(50, 999, "bluez_card.D4_3A_2C_64_87_9E"),
        NodeBlock(68, 142, "bluez_input.D4_3A_2C_64_87_9E.0"));

    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode(output, Prefix);

    Assert.Equal("bluez_input.D4_3A_2C_64_87_9E.0", name);
    Assert.Equal(68, id);
    Assert.Equal(142, serial);
  }

  [Fact]
  public void NodeWithoutSerial_ReturnsSerialZero()
  {
    var output = NodeBlockNoSerial(68, "bluez_input.D4_3A_2C_64_87_9E.0");

    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode(output, Prefix);

    Assert.Equal("bluez_input.D4_3A_2C_64_87_9E.0", name);
    Assert.Equal(68, id);
    Assert.Equal(0, serial);
  }

  [Fact]
  public void NoMatchingNode_ReturnsNull()
  {
    var output = Join(
        NodeBlock(40, 100, "alsa_output.pci-0000_00_1f.3.analog-stereo"),
        NodeBlock(72, 200, "alsa_input.usb-Generic_USB_Microphone"));

    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode(output, Prefix);

    Assert.Null(name);
    Assert.Equal(0, id);
    Assert.Equal(0, serial);
  }

  [Fact]
  public void EmptyOutput_ReturnsNull()
  {
    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode("", Prefix);

    Assert.Null(name);
    Assert.Equal(0, id);
    Assert.Equal(0, serial);
  }

  [Fact]
  public void RealisticOutput_ParsesCorrectly()
  {
    var output = string.Join("\n", new[]
    {
      "id 31, type PipeWire:Interface:Device/3",
      "    object.serial = \"31\"",
      "    device.name = \"alsa_card.pci-0000_00_1f.3\"",
      "id 32, type PipeWire:Interface:Node/3",
      "    object.serial = \"32\"",
      "    node.name = \"alsa_output.pci-0000_00_1f.3.analog-stereo\"",
      "id 55, type PipeWire:Interface:Device/3",
      "    object.serial = \"55\"",
      "    device.name = \"bluez_card.D4_3A_2C_64_87_9E\"",
      "id 68, type PipeWire:Interface:Node/3",
      "    object.serial = \"142\"",
      "    node.name = \"bluez_input.D4_3A_2C_64_87_9E.0\"",
      "    media.class = \"Audio/Source\"",
      "id 70, type PipeWire:Interface:Node/3",
      "    object.serial = \"150\"",
      "    node.name = \"alsa_input.usb-Generic_USB_Audio-00.mono-fallback\"",
    });

    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode(output, Prefix);

    Assert.Equal("bluez_input.D4_3A_2C_64_87_9E.0", name);
    Assert.Equal(68, id);
    Assert.Equal(142, serial);
  }

  [Fact]
  public void NonNumericSerial_ReturnsSerialZero()
  {
    var output = string.Join("\n", new[]
    {
      "id 68, type PipeWire:Interface:Node/3",
      "  object.serial = \"abc\"",
      "  node.name = \"bluez_input.D4_3A_2C_64_87_9E.0\"",
    });

    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode(output, Prefix);

    Assert.Equal("bluez_input.D4_3A_2C_64_87_9E.0", name);
    Assert.Equal(68, id);
    Assert.Equal(0, serial);
  }

  [Fact]
  public void DifferentNodeSuffix_StillMatches()
  {
    var output = NodeBlock(68, 142, "bluez_input.D4_3A_2C_64_87_9E.1");

    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode(output, Prefix);

    Assert.Equal("bluez_input.D4_3A_2C_64_87_9E.1", name);
    Assert.Equal(68, id);
    Assert.Equal(142, serial);
  }

  [Fact]
  public void NameMatchedThenNewObjectBeforeSerial_ReturnsSerialZero()
  {
    var output = Join(
        NodeBlockNoSerial(68, "bluez_input.D4_3A_2C_64_87_9E.0"),
        NodeBlock(70, 200, "alsa_input.usb-mic"));

    var (name, id, serial) = Radio.Infrastructure.Platform.Bluetooth.LinuxBluetoothService
        .ParsePwCliOutputForBtNode(output, Prefix);

    Assert.Equal("bluez_input.D4_3A_2C_64_87_9E.0", name);
    Assert.Equal(68, id);
    Assert.Equal(0, serial);
  }
}
