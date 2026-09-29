using Radio.Infrastructure.Audio.Sources.Primary;
using Xunit;
using static Radio.Infrastructure.Audio.Sources.Primary.USBAudioSourceBase;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// AUD-13: a USB source binds only to a physical input whose name contains its configured port. An empty
/// port, or one that matches nothing, selects nothing — never "the first device", which chose the input by
/// enumeration order.
/// </summary>
public class USBCaptureDeviceSelectionTests
{
  // The appliance's capture devices, in its enumeration order (pactl, 2026-09-29).
  private static readonly string[] Appliance =
  {
    "USB Microphone Analog Stereo",
    "Monitor of Built-in Audio Analog Stereo",
    "Built-in Audio Analog Stereo",
  };

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public void AnEmptyPort_IsNotConfigured_NotTheFirstDevice(string? port)
  {
    Assert.Equal((-1, CaptureDeviceMatch.PortNotConfigured), SelectCaptureDevice(Appliance, port));
  }

  [Fact]
  public void APortMatchingNothing_SelectsNothing()
  {
    // USB Audio's shipped port on the appliance. It used to fall back to device 0 — Vinyl's turntable.
    Assert.Equal((-1, CaptureDeviceMatch.NoMatch), SelectCaptureDevice(Appliance, "AB13X"));
  }

  [Fact]
  public void AMatchingPort_SelectsThatDevice()
  {
    Assert.Equal((0, CaptureDeviceMatch.Matched), SelectCaptureDevice(Appliance, "USB Microphone"));
    Assert.Equal((2, CaptureDeviceMatch.Matched), SelectCaptureDevice(Appliance, "built-in audio"));
  }

  [Fact]
  public void APortMatchingOnlyAMonitorLoopback_SelectsNothing()
  {
    var devices = new[] { "Monitor of Soundbar", "USB Microphone Analog Stereo" };

    Assert.Equal((-1, CaptureDeviceMatch.NoMatch), SelectCaptureDevice(devices, "Soundbar"));
  }

  [Fact]
  public void NoDevicesAtAll_SelectsNothing()
  {
    Assert.Equal((-1, CaptureDeviceMatch.NoMatch), SelectCaptureDevice(Array.Empty<string>(), "USB Microphone"));
  }
}
