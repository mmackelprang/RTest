using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Radio.Core.Configuration;
using Radio.Core.Interfaces.Audio;
using Radio.Core.Utilities;
using Radio.Infrastructure.Audio.Services;
using Radio.Infrastructure.Audio.Sources.Events;
using Radio.Infrastructure.Tests.External;

namespace Radio.Infrastructure.Tests.Audio.Services;

/// <summary>
/// <c>TTS-11</c> <c>L7</c>/<c>L8</c>: neither entry point of the real
/// <see cref="AnnouncementService"/> writes the announcement text to a log line.
/// </summary>
/// <remarks>
/// ⚠ <b>THE PLAN CALLS <c>AnnounceAsync</c>'s LINE "the single worst line in the set"</b> — it
/// logged the message UNTRUNCATED where every other site clipped to 47 or 50 characters, and it is
/// the shared entry point for <c>NotificationsController</c> and for phone-call announcements.
/// It had no behavioural pin at all until this file: its only coverage was the regression lint's
/// <c>bare message</c> rule, which is a match on the PARAMETER NAME. Renaming <c>message</c> to
/// <c>body</c> would have silently deleted every trace of coverage while the suite stayed green.
/// That is the same shape of hole <c>PHN-1c</c> shipped, and the answer is the same: a harness
/// that observes the property.
///
/// ⚠ <b>The service is real; only its collaborators are not.</b> The subject is what
/// <c>AnnouncementService</c>'s own logger writes, and both log lines fire before any collaborator
/// is reached — so a mocked <see cref="ITTSFactory"/> cannot stand in for the thing under test the
/// way <c>PHN-1c</c>'s <c>FakeTtsFactory</c> did. The real <c>TTSFactory</c>'s own line is pinned
/// separately by <c>TTSFactoryLogSafetyTests</c>, and the real <c>TTSEventSource</c>'s by
/// <c>TTSEventSourceLogSafetyTests</c>.
///
/// ⚠ <b>What these tests do NOT cover.</b> <c>PlaySoundWithAnnouncementAsync</c> is driven only as
/// far as its first failure (see the test), so the second-phase lines after the sound file plays
/// are not reached here. They were read and carry no message text — <c>"Sound + announcement
/// playback completed"</c> and the <c>CleanupSourceAsync</c> warnings are all literals — but
/// nothing in this file pins them, which is why the test is named for the entry rather than the
/// route.
/// </remarks>
public class AnnouncementServiceLogSafetyTests
{
  /// <summary>
  /// Chosen to be absent from every other fixture in the suite, so a <c>DoesNotContain</c> cannot
  /// pass by accident against generic text.
  /// </summary>
  private const string Sentinel = "Marmalade sentinel four seven";

  /// <summary>
  /// A TTS source whose <c>PlayAsync</c> completes itself, so <c>AnnounceAsync</c>'s wait on
  /// <c>PlaybackCompleted</c> returns instead of hanging the test.
  /// </summary>
  private static Mock<IEventAudioSource> CreateSelfCompletingSource()
  {
    var source = new Mock<IEventAudioSource>();
    source.SetupGet(s => s.Id).Returns("evt-test");
    source.SetupGet(s => s.Name).Returns("TTS event (mocked)");
    source
      .Setup(s => s.PlayAsync(It.IsAny<CancellationToken>()))
      .Returns(() =>
      {
        source.Raise(
          s => s.PlaybackCompleted += null,
          source.Object,
          new AudioSourceCompletedEventArgs { SourceId = "evt-test" });
        return Task.CompletedTask;
      });

    return source;
  }

  /// <summary>
  /// A real <see cref="AudioFileEventSourceFactory"/> over mocked options. It is never reached by
  /// the <c>AnnounceAsync</c> test and deliberately fails in the other one.
  /// </summary>
  private static AudioFileEventSourceFactory CreateFileFactory()
  {
    var options = new Mock<IOptionsMonitor<FilePlayerOptions>>();
    options.SetupGet(o => o.CurrentValue).Returns(new FilePlayerOptions());

    return new AudioFileEventSourceFactory(
      Mock.Of<ILogger<AudioFileEventSourceFactory>>(),
      Mock.Of<ILogger<AudioFileEventSource>>(),
      options.Object);
  }

  private static AnnouncementService CreateService(
    CapturingLoggerProvider logs, Mock<IEventAudioSource> ttsSource)
  {
    var factory = new Mock<ITTSFactory>();
    factory
      .Setup(f => f.CreateAsync(
        It.IsAny<string>(), It.IsAny<TTSParameters>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(ttsSource.Object);

    return new AnnouncementService(
      logs.CreateLogger<AnnouncementService>(),
      factory.Object,
      Mock.Of<IDuckingService>(),
      CreateFileFactory());
  }

  [Fact]
  public async Task AnnounceAsyncWritesTheTokenAndNeverTheMessage()
  {
    var logs = new CapturingLoggerProvider();
    var ttsSource = CreateSelfCompletingSource();

    await CreateService(logs, ttsSource).AnnounceAsync(Sentinel, priority: 8);

    // The route really ran: if CreateAsync had never been reached, the log line under test would
    // still have fired and every DoesNotContain below would pass for the wrong reason.
    ttsSource.Verify(s => s.PlayAsync(It.IsAny<CancellationToken>()), Times.Once);

    // ⚠ Without this the whole test passes vacuously against a service that logs nothing at all.
    Assert.NotEmpty(logs.Messages);

    foreach (var message in logs.Messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
      // Untruncated is what this line used to be, so an exact-match check would be enough — but
      // "Marmalade" also catches a future edit that reintroduces the leak with a clip on it.
      Assert.DoesNotContain("Marmalade", message, StringComparison.Ordinal);
    }

    // "No message" must not be achieved by logging nothing: priority is the field an operator
    // reads, and the token joins this line to the factory's and the source's.
    var line = Assert.Single(
      logs.Messages, m => m.StartsWith("Announcing:", StringComparison.Ordinal));

    Assert.Contains(LogSafeText.For(Sentinel), line, StringComparison.Ordinal);
    Assert.Contains("priority 8", line, StringComparison.Ordinal);
  }

  [Fact]
  public async Task PlaySoundWithAnnouncementAsyncWritesTheTokenAndNeverTheMessage()
  {
    // ⚠ The sound path does not exist, and that is deliberate rather than lazy. The log line under
    // test fires BEFORE AudioFileEventSourceFactory is called, so the FileNotFoundException that
    // follows is caught by the method's own handler and logged — which additionally proves the
    // message does not reach the sink through an EXCEPTION either. CapturingLoggerProvider records
    // exception.ToString() as its own entry precisely so that path is asserted on.
    var logs = new CapturingLoggerProvider();
    var ttsSource = CreateSelfCompletingSource();

    await CreateService(logs, ttsSource).PlaySoundWithAnnouncementAsync(
      "no-such-chime-file.wav", Sentinel, priority: 3);

    Assert.NotEmpty(logs.Messages);

    foreach (var message in logs.Messages)
    {
      Assert.DoesNotContain(Sentinel, message, StringComparison.Ordinal);
      Assert.DoesNotContain("Marmalade", message, StringComparison.Ordinal);
    }

    var line = Assert.Single(
      logs.Messages, m => m.StartsWith("Playing sound", StringComparison.Ordinal));

    Assert.Contains(LogSafeText.For(Sentinel), line, StringComparison.Ordinal);
    // The sound path is a server-side file chosen by config, not user text, so it stays — quoted,
    // because a path can contain spaces and the quotes are what show where it ends.
    Assert.Contains("'no-such-chime-file.wav'", line, StringComparison.Ordinal);
    Assert.Contains("priority 3", line, StringComparison.Ordinal);
  }
}
