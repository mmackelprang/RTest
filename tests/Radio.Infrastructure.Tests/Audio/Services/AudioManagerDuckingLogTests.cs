using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Configuration.Abstractions;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.Tests.External;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// <c>TTS-11</c> <c>T4</c>: neither ducking log line in <see cref="AudioManager"/> carries the
/// triggering source's name, and therefore neither carries the utterance.
/// </summary>
/// <remarks>
/// ⚠ <b>BOTH ARMS, AND THAT IS THE WHOLE DESIGN OF THIS FILE.</b> The row as filed named one
/// ducking site. There are two: the <c>Started</c> arm at Information and the arm <c>PHN-1f</c>
/// added for "a source left while others remain" at Debug, which logs the identical argument and
/// which nobody filed. A test driving only the <c>Started</c> arm leaves the second completely
/// unpinned — which is exactly how it escaped the original filing — so each arm has its own test
/// and each has its own mutation recorded.
///
/// ⚠ THE PLAYBACK SERVICE IS REAL, NOT MOCKED, and that is forced rather than chosen:
/// <see cref="AudioManager"/>'s constructor takes the concrete <see cref="SoundFlowPlaybackService"/>
/// and the handler returns early when it is null, so a null one would make every assertion below
/// vacuous. Constructing it reaches no hardware — the MiniAudio device is not created until
/// <c>InitializeAsync</c>, which nothing here calls. <c>AudioManagerTests</c> §
/// <c>CreateManagerWithDuckingAsync</c> does the same construction for the same reason.
/// </remarks>
public class AudioManagerDuckingLogTests
{
  /// <summary>
  /// Chosen to be absent from every other fixture in the suite, so a <c>DoesNotContain</c> cannot
  /// pass by accident against generic text.
  /// </summary>
  private const string Sentinel = "Marmalade sentinel four seven";

  private static SoundFlowPlaybackService CreatePlaybackService()
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

    var engine = new SoundFlowAudioEngine(
      Mock.Of<ILogger<SoundFlowAudioEngine>>(),
      engineOptions.Object,
      new SoundFlowMasterMixer(Mock.Of<ILogger<SoundFlowMasterMixer>>()),
      new SoundFlowDeviceManager(
        Mock.Of<ILogger<SoundFlowDeviceManager>>(),
        Mock.Of<IConfigurationManager>(),
        audioPreferences.Object,
        audioOutputOptions.Object));

    return new SoundFlowPlaybackService(Mock.Of<ILogger<SoundFlowPlaybackService>>(), engine);
  }

  /// <summary>
  /// Raises one <c>DuckingStateChanged</c> at the real <see cref="AudioManager"/> with a real
  /// <see cref="TTSEventSource"/> as the triggering source, and hands back what was logged.
  /// </summary>
  private static async Task<(CapturingLoggerProvider Logs, TTSEventSource Source)> RaiseAsync(
    DuckingSourceTransition transition)
  {
    var logs = new CapturingLoggerProvider();
    var engine = new Mock<IAudioEngine>();
    var mixer = new Mock<IMasterMixer>();
    engine.Setup(e => e.GetMasterMixer()).Returns(mixer.Object);
    mixer.Setup(m => m.GetActiveSources()).Returns(Array.Empty<IAudioSource>());

    var ducking = new Mock<IDuckingService>();
    var playback = CreatePlaybackService();

    // The REAL TTS source, so TriggeringSource.Name genuinely embeds the utterance. A fake with a
    // constant name would make this test pass against the leak it exists to catch.
    var source = new TTSEventSource(
      Sentinel,
      new TTSParameters(),
      new MemoryStream(new byte[1000]),
      TimeSpan.FromMilliseconds(10),
      logs.CreateLogger<TTSEventSource>());

    var manager = new AudioManager(
      logs.CreateLogger<AudioManager>(),
      engine.Object,
      Mock.Of<IAudioSourceFactory>(),
      playbackService: playback,
      duckingService: ducking.Object);

    await using (manager)
    {
      ducking.Raise(d => d.DuckingStateChanged += null, ducking.Object, new DuckingStateChangedEventArgs
      {
        // IsDucking true is what selects the pair of lines under test; Transition then chooses
        // between them. See AudioManager.OnDuckingStateChanged's remarks for why the OUTER branch
        // must stay keyed on IsDucking.
        IsDucking = true,
        TriggeringSource = source,
        DuckLevel = 30f,
        ActiveEventCount = 1,
        Transition = transition,
        TriggeringSourcePriority = 8
      });
    }

    return (logs, source);
  }

  [Fact]
  public async Task TheStartedArmLogsNeitherTheUtteranceNorTheSourceName()
  {
    var (logs, source) = await RaiseAsync(DuckingSourceTransition.Started);

    // The source's Name really does carry the text — the leak is declined by the log line, not by
    // a quietly changed Name (plan section 0.4).
    Assert.Contains(Sentinel[..20], source.Name, StringComparison.Ordinal);

    // ⚠ Without this the whole test passes vacuously against a handler that logs nothing at all.
    Assert.NotEmpty(logs.Messages);

    var started = Assert.Single(
      logs.Messages, m => m.StartsWith("Ducking started:", StringComparison.Ordinal));

    Assert.DoesNotContain(Sentinel, started, StringComparison.Ordinal);
    Assert.DoesNotContain("Marmalade", started, StringComparison.Ordinal);
    Assert.DoesNotContain("TTS: ", started, StringComparison.Ordinal);

    // …and "no name" is not achieved by logging nothing: Id still says WHICH event source
    // triggered the duck, which is the only handle left once Name is gone.
    //
    // ⚠ Id does NOT join this line to the mixer's Added/Removed line, whatever an earlier revision
    // of this comment said. See AudioManager.OnDuckingStateChanged's Started arm for the
    // enumeration of why no path emits both.
    Assert.Contains(source.Id, started, StringComparison.Ordinal);
    // ⚠ WEAK ON ITS OWN, deliberately kept and deliberately annotated: "source=TTS" is ALSO
    // satisfied by the leaky form, because that read "source=TTS: Marmalade sentinel four seven".
    // The DoesNotContain assertions above are the real guards; this one only confirms the Type
    // field is still populated. Do not "simplify" this test down to this line.
    Assert.Contains("source=TTS", started, StringComparison.Ordinal);
  }

  [Fact]
  public async Task TheEndedArmLogsNeitherTheUtteranceNorTheSourceName()
  {
    // ⚠ THE ARM THAT WAS NEVER FILED. It is a LogDebug and unwritten under today's appsettings, so
    // it is observable here only because the capturing logger's IsEnabled returns true at every
    // level. It is one appsettings edit from being written.
    var (logs, source) = await RaiseAsync(DuckingSourceTransition.Ended);

    Assert.Contains(Sentinel[..20], source.Name, StringComparison.Ordinal);
    Assert.NotEmpty(logs.Messages);

    var continued = Assert.Single(
      logs.Messages, m => m.StartsWith("Ducking continues:", StringComparison.Ordinal));

    Assert.DoesNotContain(Sentinel, continued, StringComparison.Ordinal);
    Assert.DoesNotContain("Marmalade", continued, StringComparison.Ordinal);
    Assert.DoesNotContain("TTS: ", continued, StringComparison.Ordinal);

    Assert.Contains(source.Id, continued, StringComparison.Ordinal);
    // ⚠ Weak on its own for the same reason as the Started arm's — see the note there.
    Assert.Contains("source=TTS", continued, StringComparison.Ordinal);
  }
}
