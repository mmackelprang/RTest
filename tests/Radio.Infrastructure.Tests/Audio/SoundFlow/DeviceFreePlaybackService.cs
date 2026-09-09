using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Tests.Audio.SoundFlow;

/// <summary>
/// Builds a real <see cref="SoundFlowPlaybackService"/> over a real
/// <see cref="SoundFlowAudioEngine"/> that has never been initialised, so no MiniAudio device is
/// ever created.
/// </summary>
/// <remarks>
/// ⚠ <b>Why this is safe.</b> <c>SoundFlowAudioEngine</c>'s constructor only stores its collaborators
/// and subscribes to three of their events (<c>DevicesChanged</c>, <c>MasterVolumeChanged</c>,
/// <c>MuteStateChanged</c>). The device is not created until <c>InitializeAsync</c>, which no caller
/// of this helper invokes. The same construction is already done by <c>SoundFlowAudioEngineTests</c>.
///
/// ⚠⚠ <b>WHAT A DEVICE-FREE SERVICE CANNOT DO, because it bounds every test built on it.</b> All four
/// <c>Play*Async</c> methods return <c>false</c> early on a null engine or null playback device, so
/// <b>no player can be registered</b> — <c>_activePlayers</c> and <c>_activeComponents</c> stay empty
/// for the lifetime of the instance. That is exactly why PHN-10 has no behavioural unit test that
/// asserts a component is removed from the mixer: the state the defect lived in is unreachable
/// without real audio hardware. Anything asserting a detach is UAT (PHN-10 plan §2.2, §3).
///
/// ⭐ Extracted rather than copied. <c>SoundFlowPlaybackServiceTransportTests</c> owned this
/// construction privately and PHN-10 needed a second caller; a second copy is the failure UI-7's
/// <c>RepositoryRoot</c> extraction exists to prevent.
/// </remarks>
internal static class DeviceFreePlaybackService
{
  /// <summary>Creates a live, un-initialised playback service.</summary>
  public static SoundFlowPlaybackService Create() => Create(Mock.Of<ILogger<SoundFlowPlaybackService>>());

  /// <summary>Creates a live, un-initialised playback service logging to <paramref name="logger"/>.</summary>
  public static SoundFlowPlaybackService Create(ILogger<SoundFlowPlaybackService> logger)
  {
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

    return new SoundFlowPlaybackService(logger, engine);
  }
}
