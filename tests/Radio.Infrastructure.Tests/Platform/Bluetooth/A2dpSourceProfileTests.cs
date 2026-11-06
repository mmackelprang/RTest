using Radio.Infrastructure.Platform.Bluetooth;

namespace Radio.Infrastructure.Tests.Platform.Bluetooth;

/// <summary>
/// What decides whether a connect can stop the radio: the device's own service list (M3).
/// </summary>
public class A2dpSourceProfileTests
{
  [Fact]
  public void AnAudioSource_IsRecognised()
  {
    // Three of the Pixel 10 Pro XL's 26 entries, read from the box 2026-11-06.
    string[] pixel =
    [
      "00001105-0000-1000-8000-00805f9b34fb",
      "0000110a-0000-1000-8000-00805f9b34fb",
      "0000111f-0000-1000-8000-00805f9b34fb",
    ];

    Assert.True(LinuxBluetoothService.HasA2dpSourceProfile(pixel));
  }

  [Fact]
  public void AMatchIgnoresCase() =>
    Assert.True(LinuxBluetoothService.HasA2dpSourceProfile(["0000110A-0000-1000-8000-00805F9B34FB"]));

  [Fact]
  public void ADeviceThatOnlyPlaysAudio_IsNotASource()
  {
    // A speaker or headset offers Audio Sink (110b), and hands-free (111e): it receives audio.
    string[] speaker =
    [
      "0000110b-0000-1000-8000-00805f9b34fb",
      "0000111e-0000-1000-8000-00805f9b34fb",
    ];

    Assert.False(LinuxBluetoothService.HasA2dpSourceProfile(speaker));
  }

  [Fact]
  public void ANoServicesYet_IsNotKnownToBeASource() =>
    Assert.False(LinuxBluetoothService.HasA2dpSourceProfile([]));
}
