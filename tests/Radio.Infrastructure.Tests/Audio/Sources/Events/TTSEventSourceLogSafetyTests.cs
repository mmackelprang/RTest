using Radio.Core.Interfaces.Audio;
using Radio.Core.Utilities;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.Tests.External;

namespace Radio.Infrastructure.Tests.Audio.Sources.Events;

/// <summary>
/// <c>TTS-11</c> <c>T1</c>: the real <see cref="TTSEventSource"/> writes no utterance text to any
/// log line on the initialize-then-play path.
/// </summary>
/// <remarks>
/// ⚠ NO FAKE ANYWHERE IN THE CHAIN, and that is the point. The type is <c>internal</c> and
/// <c>Radio.Infrastructure.csproj</c> grants <c>InternalsVisibleTo</c> to this assembly, so the real
/// source is constructed over a <see cref="MemoryStream"/> with no HTTP, no factory and no
/// substitute logger behaviour. <c>PHN-1c</c>'s trap was a test whose name promised a property its
/// harness could not observe (see <c>EventPlaybackServiceTests</c> §
/// <c>…ThisSeamWrites</c>); the answer to that is a harness that observes it, which is this file.
///
/// ⚠ <b>What these tests DO NOT cover.</b> Only the paths <c>InitializeAsync</c> and
/// <c>PlayAsync</c> reach. The error branch, the stop path and disposal were read and log no text
/// today, but nothing here pins them — hence the test names say "initialized and played" rather
/// than anything broader.
///
/// The capturing logger's <c>IsEnabled</c> returns <c>true</c> at EVERY level, which is the only
/// reason the <c>LogDebug</c> at <c>TTSEventSource.PlayCoreAsync</c> is observable at all.
/// </remarks>
public class TTSEventSourceLogSafetyTests
{
  /// <summary>
  /// Chosen to be absent from every other fixture in the suite, so a <c>DoesNotContain</c> cannot
  /// pass by accident against generic text.
  /// </summary>
  private const string Sentinel = "Marmalade sentinel four seven";

  private static async Task<CapturingLoggerProvider> InitializeAndPlayAsync(string text)
  {
    var logs = new CapturingLoggerProvider();

    // No playback service: PlayCoreAsync falls back to SimulatePlaybackAsync, which needs no audio
    // device. The LogDebug under test fires before that branch is chosen either way.
    var source = new TTSEventSource(
      text,
      new TTSParameters(),
      new MemoryStream(new byte[1000]),
      TimeSpan.FromMilliseconds(10),
      logs.CreateLogger<TTSEventSource>());

    await source.InitializeAsync();
    await source.PlayAsync();

    return logs;
  }

  [Fact]
  public async Task NoLogLineWrittenWhileInitializedAndPlayedCarriesTheUtterance()
  {
    var logs = await InitializeAndPlayAsync(Sentinel);

    // ⚠ Without this the whole test passes vacuously against a source that logs nothing at all.
    Assert.NotEmpty(logs.Messages);

    foreach (var message in logs.Messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
    }
  }

  [Fact]
  public async Task TheTokenIsPresentSoNoTextIsNotAchievedByLoggingNothing()
  {
    // The mirror of the assertion above, and the guard …ThisSeamWrites uses for the media-id mask:
    // deleting the log statements outright would satisfy "no utterance in the log" while destroying
    // the evidence that the audio stream was ever materialised.
    var logs = await InitializeAndPlayAsync(Sentinel);

    Assert.NotEmpty(logs.Messages);
    Assert.Contains(
      logs.Messages, m => m.Contains(LogSafeText.For(Sentinel), StringComparison.Ordinal));
  }

  [Fact]
  public async Task TheDebugPlaybackLineIsObservedAndCarriesTheToken()
  {
    // ⚠ THIS TEST EXISTS TO PROVE THE HARNESS SEES Debug AT ALL. TTSEventSource's play line is a
    // LogDebug, and it is unwritten under today's appsettings — so if the capturing logger were
    // ever changed to honour a minimum level, the tests above would silently stop covering it while
    // still passing. Asserting on the play line's own message text is what makes that visible.
    var logs = await InitializeAndPlayAsync(Sentinel);

    var playLine = Assert.Single(
      logs.Messages, m => m.StartsWith("Playing TTS audio:", StringComparison.Ordinal));

    Assert.DoesNotContain(Sentinel, playLine, StringComparison.Ordinal);
    Assert.Contains(LogSafeText.For(Sentinel), playLine, StringComparison.Ordinal);
  }
}
