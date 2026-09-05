using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Radio.API.Controllers;
using Radio.API.Tests.TestSupport;
using Radio.Core.Interfaces.Audio;
using Radio.Infrastructure.Audio.SoundFlow;
using Radio.Infrastructure.Audio.Sources.Events;

namespace Radio.API.Tests.Controllers;

/// <summary>
/// <c>TTS-11</c> <c>L12</c>: <see cref="AudioController.Next"/>'s "no primary source" warning
/// names the mixer's roster by <c>Type</c> and <c>Id</c>, never by <c>Name</c>.
/// </summary>
/// <remarks>
/// ⚠ <b>THIS IS THE TWELFTH LEAK SITE, AND IT WAS THE WORST OF THEM.</b> The eleven the row was
/// filed for are Information and Debug, which since <c>LOG-11</c> reach the file sink only. This
/// one is a <b>Warning</b>, so it reached journald as well — a strictly larger exposure than
/// anything the original row fixed.
///
/// ⚠ <b>The path is real, not theoretical.</b> <c>SourcesController.PlayTTSEvent</c> calls
/// <c>mixer.AddSource(ttsSource)</c> and nothing ever removes it — the only two
/// <c>RemoveSource</c> callers in the tree are <c>SourcesController</c>'s file-source route and
/// <c>AudioManager</c>'s primary-source switch. <c>GetActiveSources()</c> returns the roster
/// unfiltered. So after any TTS event, a <c>POST /api/audio/next</c> with no primary source active
/// printed 47 characters of the utterance, because <c>TTSEventSource.Name</c> is
/// <c>"TTS: " + text[..47]</c>.
///
/// ⚠ <b>The mixer and the TTS source are REAL.</b> A mock <c>IMasterMixer</c> handing back a fake
/// source with a constant <c>Name</c> would pass against the leak this file exists to catch — the
/// leak IS the real <c>Name</c>. The constructor is <c>internal</c> and
/// <c>Radio.Infrastructure</c> does not grant <c>InternalsVisibleTo</c> to <c>Radio.API.Tests</c>
/// (only to <c>Radio.Infrastructure.Tests</c>, <c>Radio.IntegrationTests</c> and
/// <c>Radio.Fingerprinting.Tests</c>), so it is reached by reflection rather than by widening the
/// production assembly's friend list for a test's convenience.
/// </remarks>
public class AudioControllerLogSafetyTests
{
  /// <summary>
  /// Chosen to be absent from every other fixture in the suite, so a <c>DoesNotContain</c> cannot
  /// pass by accident against generic text.
  /// </summary>
  private const string Sentinel = "Marmalade sentinel four seven";

  /// <summary>
  /// Builds the real <see cref="TTSEventSource"/> over its <c>internal</c> constructor.
  /// </summary>
  /// <remarks>
  /// The type itself is <c>public</c>; only the constructor is <c>internal</c>, which is why
  /// <see cref="Activator"/> with <see cref="BindingFlags.NonPublic"/> is enough and no
  /// <c>InternalsVisibleTo</c> change is needed. If this ever throws, the constructor's signature
  /// moved — fix the call, do not fall back to a fake.
  /// </remarks>
  private static TTSEventSource CreateRealTtsSource(CapturingLoggerProvider logs)
  {
    var source = Activator.CreateInstance(
      typeof(TTSEventSource),
      BindingFlags.Instance | BindingFlags.NonPublic,
      binder: null,
      args:
      [
        Sentinel,
        new TTSParameters(),
        new MemoryStream(new byte[1000]),
        TimeSpan.FromMilliseconds(10),
        logs.CreateLogger<TTSEventSource>(),
        null
      ],
      culture: null);

    return Assert.IsType<TTSEventSource>(source);
  }

  /// <summary>
  /// Drives <c>POST /api/audio/next</c> against a real mixer holding one real TTS event source and
  /// no primary source at all, which is the exact state that reaches the warning.
  /// </summary>
  private static async Task<(CapturingLoggerProvider Logs, TTSEventSource Source)> NextWithNoPrimaryAsync()
  {
    var logs = new CapturingLoggerProvider();

    var mixer = new SoundFlowMasterMixer(Mock.Of<ILogger<SoundFlowMasterMixer>>());
    var source = CreateRealTtsSource(logs);
    mixer.AddSource(source);

    var engine = new Mock<IAudioEngine>();
    engine.Setup(e => e.GetMasterMixer()).Returns(mixer);

    // audioManager left null on purpose: the controller then resolves the primary source through
    // GetActivePrimaryAudioSource(), which filters the roster to Category == Primary and finds
    // nothing — the branch under test.
    var controller = new AudioController(
      logs.CreateLogger<AudioController>(), engine.Object, Mock.Of<IDuckingService>());

    var result = await controller.Next();

    // The branch really was taken. Any other outcome would skip the log line and make every
    // assertion below vacuous in a way "no sentinel in the log" cannot distinguish.
    Assert.IsType<BadRequestObjectResult>(result.Result);

    return (logs, source);
  }

  [Fact]
  public async Task TheNoPrimarySourceWarningCarriesNeitherTheUtteranceNorTheSourceName()
  {
    var (logs, source) = await NextWithNoPrimaryAsync();

    // The source's Name really does embed the text — the leak is declined by the log line, not by
    // a quietly changed Name (plan § 0.4).
    Assert.Contains(Sentinel[..20], source.Name, StringComparison.Ordinal);

    // ⚠ Without this the whole test passes vacuously against a controller that logs nothing.
    Assert.NotEmpty(logs.Messages);

    foreach (var message in logs.Messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
      // The truncated form too: Name clips at 47 characters and the sentinel is 29, so a leak
      // through Name carries it WHOLE — but a longer utterance would arrive as a prefix, and this
      // assertion is the one that would still catch that.
      Assert.DoesNotContain("Marmalade", message, StringComparison.Ordinal);
      Assert.DoesNotContain("TTS: ", message, StringComparison.Ordinal);
    }
  }

  [Fact]
  public async Task TheWarningStillNamesEverySourceOnTheRosterByTypeAndId()
  {
    // "No name" must not be achieved by logging nothing, or by logging a roster of empty strings:
    // the whole diagnostic value of this line is "what IS in the mixer, if not a primary source".
    var (logs, source) = await NextWithNoPrimaryAsync();

    var warning = Assert.Single(
      logs.Messages, m => m.StartsWith("Next track failed: No primary", StringComparison.Ordinal));

    // The exact projected form, asserted whole. Asserting "TTS" alone would pass on the Id by
    // itself — Id is $"{Type}-{Guid:N}" — and so could not tell "logs Type and Id" apart from
    // "logs only Id".
    Assert.Contains($"{AudioSourceType.TTS}:{source.Id}", warning, StringComparison.Ordinal);
  }
}
