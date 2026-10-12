using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.API.Controllers;
using Radio.API.Models;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore.Hardware;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// AUD-105: <c>POST api/radio/band</c> tunes the band's remembered frequency (or its default), as the
/// encoder does, instead of wherever <c>RadioReceiver.SetBand</c> clamps the old frequency (a band
/// edge: FM → AM gave 1710 kHz, SW → FM gave 87.5 MHz on the box). Over a real
/// <see cref="SDRRadioAudioSource"/> and <c>RadioReceiver</c>, so the clamp is the real one.
/// </summary>
public class RadioControllerBandMemoryTests
{
  private readonly InMemoryBandMemory _memory = new();
  private readonly Mock<ISdrDevice> _device = new();
  private readonly RadioController _controller;

  public RadioControllerBandMemoryTests()
  {
    _device.Setup(d => d.DeviceInfo).Returns(new RTLSDRCore.Models.DeviceInfo
    {
      Name = "NESDR SMArt v5",
      Type = RTLSDRCore.Enums.DeviceType.Mock,
      IsAvailable = true,
      MinFrequencyHz = 100_000,
      MaxFrequencyHz = 1_766_000_000,
    });
    _device.Setup(d => d.IsOpen).Returns(true);
    _device.Setup(d => d.SupportsHfDirectSampling).Returns(true);
    bool hfMode = false;
    _device.Setup(d => d.IsHfMode).Returns(() => hfMode);
    _device.Setup(d => d.SetHfMode(It.IsAny<bool>()))
      .Callback<bool>(on => hfMode = on)
      .Returns(true);
    _device.Setup(d => d.SetFrequency(It.IsAny<long>())).Returns(true);
    _device.Setup(d => d.SetSampleRate(It.IsAny<int>())).Returns(true);

    Mock<IOptionsMonitor<RadioOptions>> options = new();
    options.Setup(o => o.CurrentValue).Returns(new RadioOptions());
    SDRRadioAudioSource source = new(
      NullLogger<SDRRadioAudioSource>.Instance, new RTLSDRCore.RadioReceiver(_device.Object), options.Object);

    Mock<IMasterMixer> mixer = new();
    mixer.Setup(m => m.GetActiveSources()).Returns(new IAudioSource[] { source });
    Mock<IAudioEngine> engine = new();
    engine.Setup(e => e.GetMasterMixer()).Returns(mixer.Object);
    _controller = new RadioController(
      NullLogger<RadioController>.Instance, engine.Object, Mock.Of<IRadioPresetService>(), Mock.Of<IRadioFactory>(), _memory);
  }

  private static RadioStateDto Ok200(ActionResult<RadioStateDto> result) =>
    Assert.IsType<RadioStateDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

  private async Task<RadioStateDto> Band(string band) =>
    Ok200(await _controller.SetBand(new SetBandRequest { Band = band }));

  private async Task Tune(long hz) =>
    Ok200(await _controller.SetFrequency(new SetFrequencyRequest { Frequency = hz }));

  [Fact]
  public async Task FmToAm_TunesTheRememberedAmFrequency_NotTheBandEdge()
  {
    _memory.Remembered[RadioBand.AM] = 1_030_000;
    await Tune(97_745_000);

    RadioStateDto state = await Band("AM");

    Assert.Equal("AM", state.Band);
    Assert.Equal(1_030_000, state.Frequency);
  }

  [Fact]
  public async Task FmToAmAndBack_ReturnsToTheFmStation_EvenWithNothingRememberedBefore()
  {
    // The box's case: the station was tuned moments before the band change, so the 30 s
    // preferences loop had not recorded it. The band change records it itself.
    await Tune(97_745_000);

    await Band("AM");
    RadioStateDto back = await Band("FM");

    Assert.Equal("FM", back.Band);
    Assert.Equal(97_745_000, back.Frequency);
    Assert.Equal(97_745_000, _memory.Remembered[RadioBand.FM]);
  }

  [Fact]
  public async Task AmToSwToFm_EachBandComesBackToItsOwnFrequency()
  {
    _memory.Remembered[RadioBand.AM] = 1_030_000;
    _memory.Remembered[RadioBand.SW] = 9_580_000;
    await Tune(97_745_000);

    Assert.Equal(1_030_000, (await Band("AM")).Frequency);
    Assert.Equal(9_580_000, (await Band("SW")).Frequency);   // not the 1.711 MHz edge
    Assert.Equal(97_745_000, (await Band("FM")).Frequency);  // not the 87.5 MHz edge
    Assert.Equal(1_030_000, (await Band("AM")).Frequency);
  }

  [Fact]
  public async Task NothingRememberedForTheBand_TunesTheBandDefault()
  {
    _memory.Defaults[RadioBand.AM] = 1_000_000;
    await Tune(97_745_000);

    Assert.Equal(1_000_000, (await Band("AM")).Frequency);
  }

  [Fact]
  public async Task ReselectingTheCurrentBand_StaysOnTheCurrentFrequency()
  {
    _memory.Remembered[RadioBand.FM] = 92_300_000;   // older than the station tuned now
    await Tune(97_745_000);

    Assert.Equal(97_745_000, (await Band("FM")).Frequency);
  }

  /// <summary>
  /// <see cref="IRadioBandMemory"/> over two dictionaries: what was remembered, and what the real
  /// service would return from configuration when nothing is (its default rung).
  /// </summary>
  private sealed class InMemoryBandMemory : IRadioBandMemory
  {
    public Dictionary<RadioBand, long> Remembered { get; } = [];

    public Dictionary<RadioBand, long> Defaults { get; } = [];

    public Task<Frequency?> GetAsync(RadioBand band, CancellationToken cancellationToken = default)
    {
      if (Remembered.TryGetValue(band, out long hz) || Defaults.TryGetValue(band, out hz))
      {
        return Task.FromResult<Frequency?>(new Frequency(hz));
      }
      return Task.FromResult<Frequency?>(null);
    }

    public Task SetAsync(RadioBand band, Frequency frequency, CancellationToken cancellationToken = default)
    {
      if (frequency.Hertz > 0)
      {
        Remembered[band] = frequency.Hertz;
      }
      return Task.CompletedTask;
    }
  }
}
