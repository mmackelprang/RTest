using Radio.Core.Interfaces.Audio;

namespace Radio.Core.Tests;

/// <summary>
/// Tests for the IAudioDeviceManager interface and related types.
/// </summary>
public class AudioDeviceTests
{
  [Fact]
  public void AudioDeviceType_ContainsAllExpectedValues_WhenEnumerated()
  {
    var types = Enum.GetValues<AudioDeviceType>();

    Assert.Contains(AudioDeviceType.Output, types);
    Assert.Contains(AudioDeviceType.Input, types);
    Assert.Contains(AudioDeviceType.Duplex, types);
  }

  [Fact]
  public void AudioDeviceInfo_CanBeCreated()
  {
    var device = new AudioDeviceInfo
    {
      Id = "device-1",
      Name = "Test Device",
      Type = AudioDeviceType.Output,
      IsDefault = true,
      MaxChannels = 2,
      SupportedSampleRates = new[] { 44100, 48000, 96000 },
      IsUSBDevice = true,
      USBPort = "/dev/ttyUSB0"
    };

    Assert.Equal("device-1", device.Id);
    Assert.Equal("Test Device", device.Name);
    Assert.Equal(AudioDeviceType.Output, device.Type);
    Assert.True(device.IsDefault);
    Assert.Equal(2, device.MaxChannels);
    Assert.Contains(48000, device.SupportedSampleRates);
    Assert.True(device.IsUSBDevice);
    Assert.Equal("/dev/ttyUSB0", device.USBPort);
  }

  [Fact]
  public void AudioDeviceInfo_DefaultValues()
  {
    var device = new AudioDeviceInfo
    {
      Id = "device-1",
      Name = "Test Device",
      Type = AudioDeviceType.Input
    };

    Assert.False(device.IsDefault);
    Assert.Equal(0, device.MaxChannels);
    Assert.Empty(device.SupportedSampleRates);
    Assert.False(device.IsUSBDevice);
    Assert.Null(device.USBPort);
    Assert.Null(device.AlsaDeviceId);
  }
}
