using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore;
using RTLSDRCore.Bands;
using RTLSDRCore.Hardware;

namespace Radio.Infrastructure.Tests.Audio.Sources;

/// <summary>
/// Covers <c>IRadioControl.SupportedBands</c> (ENC-5 Task 3).
///
/// <para>
/// The SOURCE overlay composes its band rows from this list, so a tuner that over-reports puts a
/// row on screen whose commit does nothing. These two assertions are what stop that.
/// </para>
/// </summary>
public class RadioControlCapabilityTests
{
  private static Mock<IOptionsMonitor<RadioOptions>> RadioOptionsMonitor()
  {
    var monitor = new Mock<IOptionsMonitor<RadioOptions>>();
    monitor.Setup(o => o.CurrentValue).Returns(new RadioOptions());
    return monitor;
  }

  [Fact]
  public void Rf320_ReportsFmOnly_BecauseItsBandSetterIsANoOp()
  {
    var deviceOptions = new Mock<IOptionsMonitor<DeviceOptions>>();
    deviceOptions.Setup(o => o.CurrentValue).Returns(new DeviceOptions());

    var source = new RadioAudioSource(
      Mock.Of<ILogger<RadioAudioSource>>(),
      deviceOptions.Object,
      RadioOptionsMonitor().Object,
      Mock.Of<IAudioDeviceManager>());

    // RadioAudioSource.SetBandAsync logs a warning and returns without touching the device, so any
    // band beyond FM would be a row the overlay offers and the tuner ignores.
    Assert.Equal(new[] { RadioBand.FM }, source.SupportedBands);
  }

  [Fact]
  public void Sdr_ReportsEveryBandPresetsDefines()
  {
    var device = new Mock<ISdrDevice>();
    device.Setup(d => d.DeviceInfo).Returns(new RTLSDRCore.Models.DeviceInfo
    {
      Index = 0,
      Name = "Mock RTL-SDR Device",
      Type = RTLSDRCore.Enums.DeviceType.Mock,
      Serial = "TEST123",
      Manufacturer = "Test",
      TunerType = "Test Tuner",
      IsAvailable = true,
      MinFrequencyHz = 24_000_000,
      MaxFrequencyHz = 1_766_000_000,
    });
    device.Setup(d => d.IsOpen).Returns(true);

    var receiver = new RadioReceiver(device.Object);
    var source = new SDRRadioAudioSource(
      Mock.Of<ILogger<SDRRadioAudioSource>>(),
      receiver,
      RadioOptionsMonitor().Object);

    // Adding a band to BandPresets without extending SupportedBands fails here rather than in the
    // cabinet, where the symptom would be a band the tuner covers and the knob cannot reach.
    Assert.Equal(BandPresets.AllBands.Count, source.SupportedBands.Count);
    Assert.Equal(
      new[] { RadioBand.FM, RadioBand.AM, RadioBand.SW, RadioBand.WB, RadioBand.VHF, RadioBand.AIR },
      source.SupportedBands);
  }
}
