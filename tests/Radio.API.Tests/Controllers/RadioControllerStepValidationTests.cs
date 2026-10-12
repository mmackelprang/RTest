using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore.Enums;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// AUD-102: <c>POST /api/radio/step</c> refuses a step that is not one of the current band's allowed
/// steps with a 400 whose body lists them. A real source and receiver over the mock device, never
/// started.
/// </summary>
public sealed class RadioControllerStepValidationTests : IDisposable
{
  private readonly RTLSDRCore.RadioReceiver _receiver = RTLSDRCore.RadioReceiver.CreateWithMockDevice();
  private readonly SDRRadioAudioSource _source;
  private readonly RadioController _controller;

  public RadioControllerStepValidationTests()
  {
    Mock<IOptionsMonitor<RadioOptions>> options = new();
    options.Setup(o => o.CurrentValue).Returns(new RadioOptions());
    _source = new SDRRadioAudioSource(NullLogger<SDRRadioAudioSource>.Instance, _receiver, options.Object);

    Mock<IMasterMixer> mixer = new();
    mixer.Setup(m => m.GetActiveSources()).Returns(new IAudioSource[] { _source });
    Mock<IAudioEngine> engine = new();
    engine.Setup(e => e.GetMasterMixer()).Returns(mixer.Object);
    _controller = new RadioController(
      NullLogger<RadioController>.Instance, engine.Object, Mock.Of<IRadioPresetService>(), Mock.Of<IRadioFactory>());
  }

  public void Dispose() => _receiver.Dispose();

  private static JsonElement BadRequestBody(ActionResult<RadioStateDto> result)
  {
    BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result.Result);
    return JsonSerializer.SerializeToElement(bad.Value);
  }

  [Fact]
  public async Task SevenHertzOnFm_Is400_WithFmsAllowedSteps()
  {
    Assert.True(_receiver.SetBand(BandType.FM, 101_500_000));
    long before = _source.FrequencyStep.Hertz;

    ActionResult<RadioStateDto> result = await _controller.SetFrequencyStep(new SetFrequencyStepRequest { Step = 7 });

    JsonElement body = BadRequestBody(result);
    Assert.Equal(new long[] { 50_000, 100_000, 200_000 },
      body.GetProperty("allowedStepsHz").EnumerateArray().Select(e => e.GetInt64()).ToArray());
    Assert.Equal("FM Broadcast", body.GetProperty("band").GetString());
    Assert.Equal("FM", body.GetProperty("bandCode").GetString());
    Assert.Contains("7 Hz", body.GetProperty("error").GetString());
    Assert.Equal(before, _source.FrequencyStep.Hertz);
  }

  [Fact]
  public async Task TenKilohertzOnShortwave_Is400_WithShortwavesAllowedSteps()
  {
    Assert.True(_receiver.SetBand(BandType.Shortwave, 5_905_000));

    ActionResult<RadioStateDto> result = await _controller.SetFrequencyStep(new SetFrequencyStepRequest { Step = 10_000 });

    JsonElement body = BadRequestBody(result);
    Assert.Equal(new long[] { 1_000, 2_500, 5_000 },
      body.GetProperty("allowedStepsHz").EnumerateArray().Select(e => e.GetInt64()).ToArray());
    Assert.Equal("SW", body.GetProperty("bandCode").GetString());
  }

  [Theory]
  [InlineData(BandType.Shortwave, 5_905_000L, 2_500L)]
  [InlineData(BandType.AM, 1_010_000L, 9_000L)]
  [InlineData(BandType.FM, 101_500_000L, 200_000L)]
  public async Task AnAllowedStep_Is200_AndTheStateCarriesIt(BandType band, long hz, long stepHz)
  {
    Assert.True(_receiver.SetBand(band, hz));

    ActionResult<RadioStateDto> result = await _controller.SetFrequencyStep(new SetFrequencyStepRequest { Step = stepHz });

    RadioStateDto state = Assert.IsType<RadioStateDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
    Assert.Equal((double)stepHz, state.Step);
  }

  [Fact]
  public async Task ZeroStep_IsStillA400_WithoutAnAllowedList()
  {
    ActionResult<RadioStateDto> result = await _controller.SetFrequencyStep(new SetFrequencyStepRequest { Step = 0 });

    Assert.False(BadRequestBody(result).TryGetProperty("allowedStepsHz", out _));
  }
}
