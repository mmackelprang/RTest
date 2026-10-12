using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// AUD-102 review M2: the radio source's "Frequency" metadata, which <c>PlayHistoryTracker</c> uses as
/// an unidentified history row's title, follows the same unit rule as every readout (kHz on AM and SW).
/// </summary>
public class SDRRadioAudioSourceMetadataUnitsTests
{
  [Theory]
  [InlineData(BandType.AM, 1_010_000, "1010 kHz")]
  [InlineData(BandType.Shortwave, 5_905_000, "5905 kHz")]
  [InlineData(BandType.FM, 101_500_000, "101.5 MHz")]
  public async Task FrequencyMetadata_UsesTheBandsUnit(BandType band, long hz, string expected)
  {
    Mock<ISdrDevice> device = new();
    device.Setup(d => d.DeviceInfo).Returns(new RTLSDRCore.Models.DeviceInfo
    {
      Name = "Mock",
      Type = DeviceType.Mock,
      IsAvailable = true,
      MinFrequencyHz = 100_000,
      MaxFrequencyHz = 1_766_000_000,
    });
    device.Setup(d => d.IsOpen).Returns(false);   // closed: SetBand records the band for the start
    RadioReceiver receiver = new(device.Object);
    Assert.True(receiver.SetBand(band, hz));

    Mock<IOptionsMonitor<RadioOptions>> options = new();
    options.Setup(o => o.CurrentValue).Returns(new RadioOptions());
    await using SDRRadioAudioSource source = new(NullLogger<SDRRadioAudioSource>.Instance, receiver, options.Object);

    Assert.Equal(expected, source.Metadata["Frequency"]);
  }
}
