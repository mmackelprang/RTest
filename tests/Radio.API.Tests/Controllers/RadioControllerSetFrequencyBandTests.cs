using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;

namespace Radio.API.Tests.Controllers;

/// <summary>AUD-91: <c>POST api/radio/frequency</c> with and without a band.</summary>
public class RadioControllerSetFrequencyBandTests
{
  private readonly Mock<IAudioSource> _source = new();
  private readonly Mock<IRadioControl> _radio;
  private readonly RadioController _controller;

  public RadioControllerSetFrequencyBandTests()
  {
    _source.Setup(s => s.Category).Returns(AudioSourceCategory.Primary);
    _radio = _source.As<IRadioControl>();
    Mock<IMasterMixer> mixer = new();
    mixer.Setup(m => m.GetActiveSources()).Returns(new[] { _source.Object });
    Mock<IAudioEngine> engine = new();
    engine.Setup(e => e.GetMasterMixer()).Returns(mixer.Object);
    _controller = new RadioController(
      NullLogger<RadioController>.Instance, engine.Object, Mock.Of<IRadioPresetService>(), Mock.Of<IRadioFactory>());
  }

  [Fact]
  public async Task WithoutBand_UsesSetFrequencyAsync_Only()
  {
    ActionResult<RadioStateDto> result = await _controller.SetFrequency(new SetFrequencyRequest { Frequency = 162_400_000 });

    Assert.IsType<OkObjectResult>(result.Result);
    _radio.Verify(r => r.SetFrequencyAsync(new Frequency(162_400_000), It.IsAny<CancellationToken>()), Times.Once);
    _radio.Verify(r => r.TuneInBandAsync(It.IsAny<RadioBand>(), It.IsAny<Frequency>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Theory]
  [InlineData("VHF", RadioBand.VHF)]
  [InlineData("wb", RadioBand.WB)]
  [InlineData("Air", RadioBand.AIR)]
  public async Task WithBand_TunesInThatBand_Only(string band, RadioBand expected)
  {
    ActionResult<RadioStateDto> result = await _controller.SetFrequency(
      new SetFrequencyRequest { Frequency = 120_000_000, Band = band });

    Assert.IsType<OkObjectResult>(result.Result);
    _radio.Verify(r => r.TuneInBandAsync(expected, new Frequency(120_000_000), It.IsAny<CancellationToken>()), Times.Once);
    _radio.Verify(r => r.SetFrequencyAsync(It.IsAny<Frequency>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Theory]
  [InlineData("LW")]
  [InlineData("3")]
  [InlineData("")]
  public async Task WithInvalidBand_Returns400_WithoutTuning(string band)
  {
    ActionResult<RadioStateDto> result = await _controller.SetFrequency(
      new SetFrequencyRequest { Frequency = 120_000_000, Band = band });

    BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result.Result);
    Assert.Contains("Invalid band", System.Text.Json.JsonSerializer.Serialize(bad.Value));
    _radio.Verify(r => r.TuneInBandAsync(It.IsAny<RadioBand>(), It.IsAny<Frequency>(), It.IsAny<CancellationToken>()), Times.Never);
    _radio.Verify(r => r.SetFrequencyAsync(It.IsAny<Frequency>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task WithBand_FrequencyOutsideTheBand_Returns400()
  {
    _radio.Setup(r => r.TuneInBandAsync(RadioBand.WB, It.IsAny<Frequency>(), It.IsAny<CancellationToken>()))
      .ThrowsAsync(new ArgumentOutOfRangeException("frequency", "100.1 MHz is outside the WB band"));

    ActionResult<RadioStateDto> result = await _controller.SetFrequency(
      new SetFrequencyRequest { Frequency = 100_100_000, Band = "WB" });

    BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result.Result);
    Assert.Contains("outside the WB band", System.Text.Json.JsonSerializer.Serialize(bad.Value));
  }
}
