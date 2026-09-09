using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Core.Configuration;
using Radio.Infrastructure.Audio.SoundFlow;

namespace Radio.Infrastructure.Tests.Audio;

/// <summary>
/// Builds a real <see cref="SoundFlowPlaybackService"/> over a real
/// <see cref="SoundFlowAudioEngine"/> that has never been initialised, and therefore reaches no
/// audio hardware.
/// </summary>
/// <remarks>
/// ⚠ THE SERVICE IS REAL, NOT MOCKED, AND THAT IS FORCED RATHER THAN CHOSEN. AudioManager's
/// constructor takes the CONCRETE SoundFlowPlaybackService — there is no ISoundFlowPlaybackService
/// in the tree — and its setters are non-virtual public methods, so Moq can neither substitute them
/// nor record the calls.
///
/// Constructing it reaches no hardware: SoundFlowAudioEngine's constructor only stores its
/// collaborators and subscribes to three of their events; the MiniAudio device is not created until
/// InitializeAsync, which nothing here calls. <c>GetUnderlyingEngine()</c> then returns the null
/// <c>_engine</c> and <c>GetPlaybackDevice()</c> returns null without attempting recovery, because
/// its recovery arm is gated on <c>_engine != null</c>.
///
/// ⚠ THE CONSEQUENCE THAT SHAPES EVERY TEST BUILT ON THIS: with no device,
/// <c>PlayComponentAsync</c> and <c>PlayFileAsync</c> return early and REGISTER NOTHING, so every
/// dictionary lookup in the service misses by construction. To exercise the populated-dictionary
/// half — which is the half AUD-2 is about — a test must inject a component through
/// <c>SoundFlowPlaybackService.RegisterComponentForTests</c>, the kind-B seam added by AUD-2. Read
/// that member's remarks before relying on it; in particular it does NOT connect the component to a
/// mixer, so nothing built on this helper can say anything about audibility.
///
/// ⚠ EXTRACTED BY AUD-2, AND THE EXTRACTION IS THE POINT RATHER THAN TIDINESS. This construction
/// existed twice already — <c>AudioManagerTests.CreatePlaybackService</c> and
/// <c>SoundFlowPlaybackServiceTransportTests.CreateService</c> — and AUD-2 needed a third. Three
/// copies is three chances to disagree about what "reaches no hardware" means, which is exactly the
/// argument <c>Radio.Core.Tests.RepositoryRoot</c> records for the walker it consolidated in UI-7.
/// </remarks>
internal static class DeviceLessPlaybackService
{
  public static SoundFlowPlaybackService Create()
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

    return new SoundFlowPlaybackService(
      Mock.Of<ILogger<SoundFlowPlaybackService>>(),
      engine);
  }
}
