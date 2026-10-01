using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore;
using RTLSDRCore.Enums;
using RTLSDRCore.Hardware;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>AUD-91: <see cref="SDRRadioAudioSource.TuneInBandAsync"/>.</summary>
public class SDRRadioAudioSourceTuneInBandTests
{
  private readonly Mock<ISdrDevice> _device = new();
  private readonly RadioReceiver _receiver;
  private readonly SDRRadioAudioSource _source;

  public SDRRadioAudioSourceTuneInBandTests()
  {
    _device.Setup(d => d.DeviceInfo).Returns(new RTLSDRCore.Models.DeviceInfo
    {
      Name = "Mock RTL-SDR Device",
      Type = DeviceType.Mock,
      IsAvailable = true,
      MinFrequencyHz = 24_000_000,
      MaxFrequencyHz = 1_766_000_000,
    });
    _device.Setup(d => d.IsOpen).Returns(true);
    _device.Setup(d => d.SetFrequency(It.IsAny<long>())).Returns(true);
    _receiver = new RadioReceiver(_device.Object);
    _receiver.SetBand(BandType.FM, 101_100_000);

    Mock<IOptionsMonitor<RadioOptions>> options = new();
    options.Setup(o => o.CurrentValue).Returns(new RadioOptions());
    _source = new SDRRadioAudioSource(NullLogger<SDRRadioAudioSource>.Instance, _receiver, options.Object, null, null, null);
    _device.Invocations.Clear();
  }

  [Fact]
  public async Task FrequencyOutsideTheBand_Throws_WithoutTuning()
  {
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
      _source.TuneInBandAsync(RadioBand.WB, new Frequency(100_100_000)));

    _device.Verify(d => d.SetFrequency(It.IsAny<long>()), Times.Never);
    Assert.Equal(BandType.FM, _receiver.CurrentBand.Type);
    Assert.Equal(101_100_000, _receiver.CurrentFrequency);
  }

  [Fact]
  public async Task VhfAtAnFmFrequency_StaysInVhf_InOneRetune_AndTakesTheVhfStep()
  {
    await _source.TuneInBandAsync(RadioBand.VHF, new Frequency(100_100_000));

    Assert.Equal(BandType.VHF, _receiver.CurrentBand.Type);
    Assert.Equal(RadioBand.VHF, _source.CurrentBand);
    Assert.Equal(100_100_000, _receiver.CurrentFrequency);
    _device.Verify(d => d.SetFrequency(100_100_000), Times.Once);
    _device.Verify(d => d.SetFrequency(It.IsAny<long>()), Times.Once);
    Assert.Equal(12_500, _source.FrequencyStep.Hertz);
  }

  [Fact]
  public async Task ReceiverFailsToRestartStreaming_ThrowsInvalidOperation_NotArgumentOutOfRange()
  {
    _device.Setup(d => d.IsStreaming).Returns(true);
    _device.Setup(d => d.StartStreaming()).Returns(false);

    InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
      _source.TuneInBandAsync(RadioBand.VHF, new Frequency(146_520_000)));

    Assert.Contains("VHF", ex.Message);
    _device.Verify(d => d.StartStreaming(), Times.Once);
  }

  [Fact]
  public async Task SameBand_KeepsTheUsersStep()
  {
    await _source.SetFrequencyStepAsync(new Frequency(50_000));

    await _source.TuneInBandAsync(RadioBand.FM, new Frequency(99_500_000));

    Assert.Equal(99_500_000, _receiver.CurrentFrequency);
    Assert.Equal(50_000, _source.FrequencyStep.Hertz);
  }
}
