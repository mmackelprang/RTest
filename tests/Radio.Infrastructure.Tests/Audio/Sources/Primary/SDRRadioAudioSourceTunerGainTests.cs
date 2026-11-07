using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.Sources.Primary;
using RTLSDRCore;
using Xunit;

namespace Radio.Infrastructure.Tests.Audio.Sources.Primary;

/// <summary>
/// The tuner's gain comes from <see cref="RadioOptions"/>. Automatic gain overloaded the device's
/// converter on strong FM stations: measured on the box on 97.7 MHz, and heard as distortion.
/// </summary>
public class SDRRadioAudioSourceTunerGainTests
{
  private static (SDRRadioAudioSource Source, RadioReceiver Receiver) Build(RadioOptions options)
  {
    Mock<IOptionsMonitor<RadioOptions>> monitor = new();
    monitor.Setup(m => m.CurrentValue).Returns(options);
    RadioReceiver receiver = RadioReceiver.CreateWithMockDevice();
    return (new SDRRadioAudioSource(NullLogger<SDRRadioAudioSource>.Instance, receiver, monitor.Object), receiver);
  }

  [Fact]
  public void ByDefault_TheTunerIsAtAFixedThirtyDb()
  {
    (_, RadioReceiver receiver) = Build(new RadioOptions());

    Assert.False(receiver.AutoGainEnabled);
    Assert.Equal(30f, receiver.Gain);
  }

  [Fact]
  public void TheConfiguredGain_ReachesTheReceiver()
  {
    (_, RadioReceiver receiver) = Build(new RadioOptions { TunerGainDb = 22f });

    Assert.Equal(22f, receiver.Gain);
  }

  // At 30 dB weak stations read under the receiver's own 0.1 default, and a closed squelch is exact
  // digital silence. Under automatic gain it was effectively never closed, so it stays open.
  [Fact]
  public void ByDefault_TheSquelchIsOpen()
  {
    (_, RadioReceiver receiver) = Build(new RadioOptions());

    Assert.Equal(0f, receiver.SquelchThreshold);
  }

  // Signal strength follows the gain, so a seek threshold set for automatic gain walks past
  // stations at the fixed gain.
  [Fact]
  public void ByDefault_SeekStopsWellBelowHalfOfFullScale()
  {
    Assert.Equal(14, new RadioOptions().ScanStopThreshold);
  }

  [Fact]
  public void TheConfiguredSquelch_ReachesTheReceiver()
  {
    (_, RadioReceiver receiver) = Build(new RadioOptions { SquelchThreshold = 0.2f });

    Assert.Equal(0.2f, receiver.SquelchThreshold);
  }

  [Fact]
  public void AutomaticGain_CanBeConfiguredBackOn()
  {
    (_, RadioReceiver receiver) = Build(new RadioOptions { AutoGain = true });

    Assert.True(receiver.AutoGainEnabled);
  }
}
