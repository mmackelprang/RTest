using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// Covers the no-player-registered contract of the transport methods added by ADR-029 PR 1.
/// The populated-dictionary paths need a real device and are exercised by UAT (see the plan
/// Test Plan §2.2), not here.
/// </summary>
public class SoundFlowPlaybackServiceTransportTests
{
  [Fact]
  public void GetPosition_ReturnsNull_WhenNoPlayerIsRegistered()
  {
    var service = CreateService();

    Assert.Null(service.GetPosition("no-such-source"));
  }

  [Fact]
  public void Seek_ReturnsFalse_WhenNoPlayerIsRegistered()
  {
    var service = CreateService();

    Assert.False(service.Seek("no-such-source", TimeSpan.FromSeconds(5)));
  }

  [Fact]
  public void Seek_ReturnsFalse_ForANegativePositionOnAnUnregisteredSource()
  {
    // Named for what it actually pins. Seek's negative-position guard runs BEFORE the dictionary
    // lookup, so this assertion would still hold with that guard deleted — the lookup alone
    // returns false. Reaching the guard itself needs a registered player, which needs a real
    // device, so it is a UAT case (plan Test Plan §2.2) rather than one this file can carry.
    var service = CreateService();

    Assert.False(service.Seek("no-such-source", TimeSpan.FromSeconds(-1)));
  }

  private static SoundFlowPlaybackService CreateService()
  {
    // The engine is never started; these three assertions never reach a device.
    // SoundFlowAudioEngine's constructor only stores its collaborators and subscribes to three
    // of their events (DevicesChanged, MasterVolumeChanged, MuteStateChanged) — the MiniAudio
    // device is not created until InitializeAsync, which no test here calls. The same
    // construction is already done by SoundFlowAudioEngineTests.
    var engineOptions = new Mock<IOptions<AudioEngineOptions>>();
    engineOptions.Setup(o => o.Value).Returns(new AudioEngineOptions
    {
      EnableHotPlugDetection = false
    });

    var audioPreferences = new Mock<IOptionsMonitor<AudioPreferences>>();
    audioPreferences.Setup(m => m.CurrentValue).Returns(new AudioPreferences());

    var audioOutputOptions = new Mock<IOptionsMonitor<AudioOutputOptions>>();
    audioOutputOptions.Setup(m => m.CurrentValue).Returns(new AudioOutputOptions());

    var masterMixer = new SoundFlowMasterMixer(Mock.Of<ILogger<SoundFlowMasterMixer>>());
    var deviceManager = new SoundFlowDeviceManager(
      Mock.Of<ILogger<SoundFlowDeviceManager>>(),
      Mock.Of<IConfigurationManager>(),
      audioPreferences.Object,
      audioOutputOptions.Object);

    var engine = new SoundFlowAudioEngine(
      Mock.Of<ILogger<SoundFlowAudioEngine>>(),
      engineOptions.Object,
      masterMixer,
      deviceManager);

    return new SoundFlowPlaybackService(
      Mock.Of<ILogger<SoundFlowPlaybackService>>(),
      engine);
  }
}
