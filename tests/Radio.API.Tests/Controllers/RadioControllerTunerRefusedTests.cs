using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.Core.Configuration;
using Radio.Core.Exceptions;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore.Hardware;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// AUD-99: a tune the hardware refuses is a server-side error (503 problem+json saying the tuner
/// refused), not the 400 an out-of-range frequency gets.
/// </summary>
public class RadioControllerTunerRefusedTests
{
  private readonly Mock<ISdrDevice> _device = new();
  private readonly SDRRadioAudioSource _source;
  private readonly RadioController _controller;

  public RadioControllerTunerRefusedTests()
  {
    // A real source and receiver over a device that is open and refuses every frequency.
    _device.Setup(d => d.DeviceInfo).Returns(new RTLSDRCore.Models.DeviceInfo
    {
      Name = "Refusing RTL-SDR",
      Type = RTLSDRCore.Enums.DeviceType.Mock,
      IsAvailable = true,
      MinFrequencyHz = 24_000_000,
      MaxFrequencyHz = 1_766_000_000,
    });
    _device.Setup(d => d.IsOpen).Returns(true);
    _device.Setup(d => d.SetFrequency(It.IsAny<long>())).Returns(false);

    Mock<IOptionsMonitor<RadioOptions>> options = new();
    options.Setup(o => o.CurrentValue).Returns(new RadioOptions());
    _source = new SDRRadioAudioSource(NullLogger<SDRRadioAudioSource>.Instance, new RTLSDRCore.RadioReceiver(_device.Object), options.Object);

    Mock<IMasterMixer> mixer = new();
    mixer.Setup(m => m.GetActiveSources()).Returns(new IAudioSource[] { _source });
    Mock<IAudioEngine> engine = new();
    engine.Setup(e => e.GetMasterMixer()).Returns(mixer.Object);
    _controller = new RadioController(
      NullLogger<RadioController>.Instance, engine.Object, Mock.Of<IRadioPresetService>(), Mock.Of<IRadioFactory>());
  }

  private static ProblemDetails Assert503(ActionResult<RadioStateDto> result)
  {
    ObjectResult error = Assert.IsType<ObjectResult>(result.Result);
    Assert.Equal(503, error.StatusCode);
    ProblemDetails problem = Assert.IsType<ProblemDetails>(error.Value);
    Assert.Equal(503, problem.Status);
    Assert.Contains("tuner refused", problem.Title);
    return problem;
  }

  [Fact]
  public async Task SetFrequency_RefusedByTheDevice_Is503_NotA400()
  {
    ActionResult<RadioStateDto> result = await _controller.SetFrequency(new SetFrequencyRequest { Frequency = 99_500_000 });

    ProblemDetails problem = Assert503(result);
    Assert.Contains("refused", problem.Detail);
    _device.Verify(d => d.SetFrequency(99_500_000), Times.Once);
  }

  [Fact]
  public async Task SetFrequency_WithBand_RefusedByTheDevice_Is503()
  {
    ActionResult<RadioStateDto> result = await _controller.SetFrequency(
      new SetFrequencyRequest { Frequency = 146_520_000, Band = "VHF" });

    Assert503(result);
  }

  [Fact]
  public async Task SetBand_RefusedByTheDevice_Is503()
  {
    ActionResult<RadioStateDto> result = await _controller.SetBand(new SetBandRequest { Band = "AIR" });

    Assert503(result);
  }

  private static ProblemDetails Assert409NoHf(ActionResult<RadioStateDto> result)
  {
    ObjectResult error = Assert.IsType<ObjectResult>(result.Result);
    Assert.Equal(409, error.StatusCode);
    ProblemDetails problem = Assert.IsType<ProblemDetails>(error.Value);
    Assert.Equal(409, problem.Status);
    Assert.Contains("no HF direct sampling", problem.Title);
    return problem;
  }

  /// <summary>A controller over a real source and receiver whose open device has no HF direct sampling.</summary>
  private static RadioController ControllerOverADeviceWithoutHf()
  {
    Mock<ISdrDevice> device = new();
    device.Setup(d => d.DeviceInfo).Returns(new RTLSDRCore.Models.DeviceInfo
    {
      Name = "Generic RTL-SDR",
      Product = "RTL2838UHIDIR",
      Type = RTLSDRCore.Enums.DeviceType.Mock,
      IsAvailable = true,
      MinFrequencyHz = 24_000_000,
      MaxFrequencyHz = 1_766_000_000,
    });
    device.Setup(d => d.IsOpen).Returns(true);
    device.Setup(d => d.SupportsHfDirectSampling).Returns(false);
    device.Setup(d => d.SetFrequency(It.IsAny<long>())).Returns(true);

    Mock<IOptionsMonitor<RadioOptions>> options = new();
    options.Setup(o => o.CurrentValue).Returns(new RadioOptions());
    SDRRadioAudioSource source = new(NullLogger<SDRRadioAudioSource>.Instance, new RTLSDRCore.RadioReceiver(device.Object), options.Object);
    Mock<IMasterMixer> mixer = new();
    mixer.Setup(m => m.GetActiveSources()).Returns(new IAudioSource[] { source });
    Mock<IAudioEngine> engine = new();
    engine.Setup(e => e.GetMasterMixer()).Returns(mixer.Object);
    return new RadioController(
      NullLogger<RadioController>.Instance, engine.Object, Mock.Of<IRadioPresetService>(), Mock.Of<IRadioFactory>());
  }

  [Fact]
  public async Task SetBand_ToAm_OnADeviceWithoutHf_Is409_Not503()
  {
    ActionResult<RadioStateDto> result = await ControllerOverADeviceWithoutHf().SetBand(new SetBandRequest { Band = "AM" });

    Assert.Contains("HF direct sampling", Assert409NoHf(result).Detail);
  }

  [Fact]
  public async Task SetFrequency_InSw_OnADeviceWithoutHf_Is409()
  {
    RadioController controller = ControllerOverADeviceWithoutHf();

    Assert409NoHf(await controller.SetFrequency(new SetFrequencyRequest { Frequency = 6_000_000 }));
    Assert409NoHf(await controller.SetFrequency(new SetFrequencyRequest { Frequency = 6_000_000, Band = "SW" }));
  }

  [Fact]
  public async Task SetFrequency_OutsideEveryBandAndTheDevice_IsStill400()
  {
    ActionResult<RadioStateDto> result = await _controller.SetFrequency(new SetFrequencyRequest { Frequency = 2_000_000_000 });

    Assert.IsType<BadRequestObjectResult>(result.Result);
  }

  [Fact]
  public void Devices_ListsTheDongles_WithProductTunerAndHfState()
  {
    Mock<IRadioFactory> factory = new();
    factory.Setup(f => f.GetAvailableDeviceTypes()).Returns(new[] { "RTLSDRCore" });
    factory.Setup(f => f.GetSdrHardware()).Returns(new[]
    {
      new SdrHardwareInfo(0, "Generic RTL2832U OEM", "Nooelec", "NESDR SMArt v5", "56001418", "R820T",
        "Q", "Auto", IsHfMode: true, IsInUse: true, 100_000, 1_766_000_000),
    });
    RadioController controller = new(
      NullLogger<RadioController>.Instance, Mock.Of<IAudioEngine>(), Mock.Of<IRadioPresetService>(), factory.Object);

    ActionResult<RadioDeviceListDto> result = controller.GetAvailableDevices();

    RadioDeviceListDto list = Assert.IsType<RadioDeviceListDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
    SdrHardwareDto dongle = Assert.Single(list.Hardware);
    Assert.Equal("NESDR SMArt v5", dongle.Product);
    Assert.Equal("R820T", dongle.TunerType);
    Assert.Equal("Q", dongle.HfDirectSampling);
    Assert.True(dongle.HfEnabled);
    Assert.True(dongle.IsHfMode);
    Assert.Equal(100_000, dongle.MinFrequencyHz);
  }

  [Fact]
  public async Task TunerRefusedException_FromAnyRadioSource_Is503()
  {
    Mock<IAudioSource> source = new();
    source.Setup(s => s.Category).Returns(AudioSourceCategory.Primary);
    Mock<IRadioControl> radio = source.As<IRadioControl>();
    radio.Setup(r => r.SetBandAsync(RadioBand.AM, It.IsAny<CancellationToken>()))
      .ThrowsAsync(new TunerRefusedException("The tuner refused the switch to the AM band"));
    Mock<IMasterMixer> mixer = new();
    mixer.Setup(m => m.GetActiveSources()).Returns(new[] { source.Object });
    Mock<IAudioEngine> engine = new();
    engine.Setup(e => e.GetMasterMixer()).Returns(mixer.Object);
    RadioController controller = new(
      NullLogger<RadioController>.Instance, engine.Object, Mock.Of<IRadioPresetService>(), Mock.Of<IRadioFactory>());

    ActionResult<RadioStateDto> result = await controller.SetBand(new SetBandRequest { Band = "AM" });

    Assert.Equal("The tuner refused the switch to the AM band", Assert503(result).Detail);
  }
}
