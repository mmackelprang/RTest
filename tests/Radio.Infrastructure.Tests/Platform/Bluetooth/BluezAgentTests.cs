using Microsoft.Extensions.Logging;
using Moq;
using Radio.Infrastructure.Platform.Bluetooth.Linux;
using Tmds.DBus;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth;

public class BluezAgentTests
{
  private readonly Mock<ILogger> _logger = new();

  private BluezAgent CreateAgent(
    bool autoAccept = true,
    string? connectedAddress = null,
    string? connectedName = null)
  {
    return new BluezAgent(_logger.Object, autoAccept, () => (connectedAddress, connectedName));
  }

  [Theory]
  [InlineData("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF", "AA:BB:CC:DD:EE:FF")]
  [InlineData("/org/bluez/hci1/dev_11_22_33_44_55_66", "11:22:33:44:55:66")]
  [InlineData("/org/bluez/hci0/dev_78_20_51_F5_FB_A7", "78:20:51:F5:FB:A7")]
  public void ExtractAddressFromDevicePath_ParsesCorrectly(string path, string expected)
  {
    var result = BluezAgent.ExtractAddressFromDevicePath(new ObjectPath(path));
    Assert.Equal(expected, result);
  }

  [Theory]
  [InlineData("/org/bluez/hci0")]
  [InlineData("/org/bluez")]
  [InlineData("/")]
  public void ExtractAddressFromDevicePath_ReturnsNull_ForInvalidPaths(string path)
  {
    var result = BluezAgent.ExtractAddressFromDevicePath(new ObjectPath(path));
    Assert.Null(result);
  }

  [Fact]
  public async Task AuthorizeServiceAsync_Accepts_WhenNoDeviceConnected()
  {
    var agent = CreateAgent(autoAccept: true, connectedAddress: null);
    // Should not throw
    await agent.AuthorizeServiceAsync(
      new ObjectPath("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF"),
      "0000110b-0000-1000-8000-00805f9b34fb");
  }

  [Fact]
  public async Task AuthorizeServiceAsync_Accepts_WhenSameDeviceReconnects()
  {
    var agent = CreateAgent(
      autoAccept: true,
      connectedAddress: "AA:BB:CC:DD:EE:FF",
      connectedName: "Phone");

    // Same device requesting auth -- should accept
    await agent.AuthorizeServiceAsync(
      new ObjectPath("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF"),
      "0000110b-0000-1000-8000-00805f9b34fb");
  }

  [Fact]
  public async Task AuthorizeServiceAsync_Rejects_WhenDifferentDeviceTriesToConnect()
  {
    var agent = CreateAgent(
      autoAccept: true,
      connectedAddress: "AA:BB:CC:DD:EE:FF",
      connectedName: "Phone A");

    // Different device trying to connect
    var ex = await Assert.ThrowsAsync<DBusException>(() =>
      agent.AuthorizeServiceAsync(
        new ObjectPath("/org/bluez/hci0/dev_11_22_33_44_55_66"),
        "0000110b-0000-1000-8000-00805f9b34fb"));

    Assert.Contains("Another device is already connected", ex.ErrorMessage);
  }

  [Fact]
  public async Task AuthorizeServiceAsync_CaseInsensitiveAddressMatch()
  {
    var agent = CreateAgent(
      autoAccept: true,
      connectedAddress: "aa:bb:cc:dd:ee:ff",
      connectedName: "Phone");

    // Same address but different case in path -- should accept
    await agent.AuthorizeServiceAsync(
      new ObjectPath("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF"),
      "0000110b-0000-1000-8000-00805f9b34fb");
  }

  [Fact]
  public async Task AuthorizeServiceAsync_Rejects_WhenAutoAcceptDisabled()
  {
    var agent = CreateAgent(autoAccept: false);

    var ex = await Assert.ThrowsAsync<DBusException>(() =>
      agent.AuthorizeServiceAsync(
        new ObjectPath("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF"),
        "0000110b-0000-1000-8000-00805f9b34fb"));

    Assert.Contains("rejected by agent", ex.ErrorMessage);
  }

  [Fact]
  public async Task RequestConfirmationAsync_AutoAccepts()
  {
    var agent = CreateAgent(autoAccept: true);
    // Should not throw
    await agent.RequestConfirmationAsync(
      new ObjectPath("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF"), 123456);
  }

  [Fact]
  public async Task RequestConfirmationAsync_Rejects_WhenAutoAcceptDisabled()
  {
    var agent = CreateAgent(autoAccept: false);

    await Assert.ThrowsAsync<DBusException>(() =>
      agent.RequestConfirmationAsync(
        new ObjectPath("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF"), 123456));
  }
}
