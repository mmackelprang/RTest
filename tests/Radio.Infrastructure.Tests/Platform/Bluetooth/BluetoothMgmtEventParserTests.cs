using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Platform.Bluetooth;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth;

public class BluetoothMgmtEventParserTests
{
  [Fact]
  public void TryParseDeviceDisconnected_ValidEvent_ReturnsTrue()
  {
    // MGMT_EV_DEVICE_DISCONNECTED = 0x000C, index=0x0000, len=8
    // bdaddr = D4:3A:2C:64:87:9E (little-endian: 9E 87 64 2C 3A D4)
    // addr_type = 0x00 (BR/EDR), reason = 0x03 (Remote)
    byte[] data =
    {
      0x0C, 0x00,  // opcode
      0x00, 0x00,  // index
      0x08, 0x00,  // param_len
      0x9E, 0x87, 0x64, 0x2C, 0x3A, 0xD4,  // bdaddr (LE)
      0x00,        // addr_type
      0x03         // reason: Remote
    };

    var result = BluetoothMgmtEventParser.TryParseDeviceDisconnected(
      data, out var address, out var reason);

    Assert.True(result);
    Assert.Equal("D4:3A:2C:64:87:9E", address);
    Assert.Equal(BluetoothDisconnectReason.Remote, reason);
  }

  [Fact]
  public void TryParseDeviceDisconnected_TimeoutReason_ParsesCorrectly()
  {
    byte[] data =
    {
      0x0C, 0x00, 0x00, 0x00, 0x08, 0x00,
      0xA7, 0xFB, 0xF5, 0x51, 0x20, 0x78,  // 78:20:51:F5:FB:A7
      0x00, 0x01  // reason: Timeout
    };

    var result = BluetoothMgmtEventParser.TryParseDeviceDisconnected(
      data, out var address, out var reason);

    Assert.True(result);
    Assert.Equal("78:20:51:F5:FB:A7", address);
    Assert.Equal(BluetoothDisconnectReason.Timeout, reason);
  }

  [Fact]
  public void TryParseDeviceDisconnected_WrongOpcode_ReturnsFalse()
  {
    byte[] data =
    {
      0x0D, 0x00,  // wrong opcode
      0x00, 0x00, 0x08, 0x00,
      0x9E, 0x87, 0x64, 0x2C, 0x3A, 0xD4,
      0x00, 0x03
    };

    var result = BluetoothMgmtEventParser.TryParseDeviceDisconnected(
      data, out _, out _);

    Assert.False(result);
  }

  [Fact]
  public void TryParseDeviceDisconnected_TooShort_ReturnsFalse()
  {
    byte[] data = { 0x0C, 0x00, 0x00, 0x00, 0x08, 0x00 }; // header only

    var result = BluetoothMgmtEventParser.TryParseDeviceDisconnected(
      data, out _, out _);

    Assert.False(result);
  }

  [Fact]
  public void TryParseDeviceDisconnected_UnknownReasonByte_ParsesRawValue()
  {
    byte[] data =
    {
      0x0C, 0x00, 0x00, 0x00, 0x08, 0x00,
      0x9E, 0x87, 0x64, 0x2C, 0x3A, 0xD4,
      0x00, 0xFF  // unknown reason byte
    };

    var result = BluetoothMgmtEventParser.TryParseDeviceDisconnected(
      data, out _, out var reason);

    Assert.True(result);
    Assert.Equal((BluetoothDisconnectReason)0xFF, reason);
  }

  [Theory]
  [InlineData(new byte[] { 0x9E, 0x87, 0x64, 0x2C, 0x3A, 0xD4 }, "D4:3A:2C:64:87:9E")]
  [InlineData(new byte[] { 0xA7, 0xFB, 0xF5, 0x51, 0x20, 0x78 }, "78:20:51:F5:FB:A7")]
  [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, "00:00:00:00:00:00")]
  public void FormatBdAddr_FormatsCorrectly(byte[] bdaddr, string expected)
  {
    Assert.Equal(expected, BluetoothMgmtEventParser.FormatBdAddr(bdaddr));
  }
}
