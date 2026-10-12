using Microsoft.Extensions.Logging.Abstractions;
using Radio.Core.Models.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Tests.Platform.Input;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>AUD-105: <see cref="RadioBandSwitch.SwitchAsync"/>, the one band change that uses band memory.</summary>
public class RadioBandSwitchTests
{
  [Fact]
  public async Task ReturnsTrue_WhenTheTunerReportsTheBand()
  {
    FakeRadioSource radio = new() { CurrentBand = RadioBand.FM };
    FakeBandMemory memory = new();
    memory.Remembered[RadioBand.AM] = Frequency.FromKilohertz(1130);

    bool switched = await RadioBandSwitch.SwitchAsync(radio, memory, RadioBand.AM, NullLogger.Instance);

    Assert.True(switched);
    Assert.Equal(RadioBand.AM, radio.CurrentBand);
    Assert.Equal(1_130_000, radio.CurrentFrequency.Hertz);
  }

  [Fact]
  public async Task ReturnsFalse_WhenTheTunerStaysOnAnotherBand()
  {
    FakeRadioSource radio = new() { CurrentBand = RadioBand.FM, IgnoresBandChanges = true };
    FakeBandMemory memory = new();
    memory.Remembered[RadioBand.AM] = Frequency.FromKilohertz(1130);

    bool switched = await RadioBandSwitch.SwitchAsync(radio, memory, RadioBand.AM, NullLogger.Instance);

    Assert.False(switched);
    Assert.Equal(RadioBand.FM, radio.CurrentBand);
  }

  [Fact]
  public async Task ReturnsFalse_WhenTheTunerIgnoresTheClampToo()
  {
    // Nothing remembered and no default: SetBandAsync, which the tuner also ignores.
    FakeRadioSource radio = new() { CurrentBand = RadioBand.FM, IgnoresBandChanges = true };

    bool switched = await RadioBandSwitch.SwitchAsync(radio, new FakeBandMemory(), RadioBand.SW, NullLogger.Instance);

    Assert.False(switched);
    Assert.Equal(new[] { RadioBand.SW }, radio.BandsSet);
  }
}
